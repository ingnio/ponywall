using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Capture-side API for the forensic event store. See
    /// Docs/EXPLAINABILITY.md sections 4 and 5.
    /// </summary>
    public interface IFirewallEventStore : IDisposable
    {
        /// <summary>Enqueues an event for batched write. Thread-safe, non-blocking.</summary>
        void Enqueue(FirewallLogEntry entry, FirewallMode modeAtEvent, long rulesetId);

        /// <summary>Writes or returns the existing ruleset snapshot for the given config JSON. Returns the row ID.</summary>
        long GetOrCreateRulesetSnapshot(byte[] canonicalJson);

        /// <summary>Current count of events dropped due to backpressure (queue overflow).</summary>
        long EventsDropped { get; }

        /// <summary>Enables or disables the store. When disabled, Enqueue is a no-op.</summary>
        bool Enabled { get; set; }

        /// <summary>Runs the hot to warm migration and retention cleanup. Called on a timer.</summary>
        void RunMaintenance();

        /// <summary>Fetches a single event by its row id from events_hot. Returns null if not found.</summary>
        FirewallEventRecord? GetEventById(long id);

        /// <summary>
        /// Returns up to <paramref name="limit"/> events from events_hot
        /// whose reason_id is still Unknown. Used by the backfill job.
        /// </summary>
        IReadOnlyList<FirewallEventRecord> GetUnexplainedBatch(int limit);

        /// <summary>
        /// Updates the reason_id, confidence and matched_rule_id of an
        /// events_hot row. Single-row update. For batched updates from
        /// the backfill job, use <see cref="UpdateReasons"/>.
        /// </summary>
        void UpdateReason(long eventId, ReasonId reason, Confidence confidence, string? matchedRuleId, string? nearMissRuleIds);

        /// <summary>
        /// Applies many reason updates inside a single transaction.
        /// Returns the number of rows actually updated.
        /// </summary>
        int UpdateReasons(IReadOnlyList<ReasonUpdate> updates);

        /// <summary>Loads a stored ruleset snapshot by id. Returns null if no such row.</summary>
        RulesetSnapshot? GetRulesetSnapshot(long id);
    }

    /// <summary>
    /// Batched reason update payload used by <see cref="IFirewallEventStore.UpdateReasons"/>.
    /// </summary>
    public readonly record struct ReasonUpdate(
        long EventId,
        ReasonId Reason,
        Confidence Confidence,
        string? MatchedRuleId,
        string? NearMissRuleIds);

    /// <summary>
    /// SQLite-backed implementation of <see cref="IFirewallEventStore"/>.
    /// Uses a bounded in-memory queue flushed by a background thread on a
    /// 2-second interval (or when the queue crosses 500 items). See
    /// Docs/EXPLAINABILITY.md section 5.2 for the design contract.
    /// </summary>
    public sealed class FirewallEventStore : IFirewallEventStore
    {
        // Bounds (contract, section 5.2)
        private const int QueueCapacity = 5000;
        private const int FlushThreshold = 500;
        private const int FlushIntervalMs = 2000;

        // Retention (contract, section 5.2)
        private const long HotRetentionMs = 72L * 60 * 60 * 1000;
        private const long HotMaxRows = 100_000;
        private const long WarmMaxRows = 1_000_000;

        private readonly string _dbPath;
        private readonly SqliteConnection _connection;
        private readonly ConcurrentQueue<PendingEvent> _queue = new();
        private readonly Thread _flushThread;
        private readonly ManualResetEventSlim _flushSignal = new(false);
        private readonly object _writeLock = new();
        private volatile bool _disposed;
        private long _eventsDropped;
        private long _queueCount; // approximate, used only for flush threshold
        private bool _enabled = true;

        public long EventsDropped => Interlocked.Read(ref _eventsDropped);

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public FirewallEventStore()
            : this(Path.Combine(Utils.AppDataPath, "history.db"))
        {
        }

        public FirewallEventStore(string dbPath)
        {
            _dbPath = dbPath;

            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default,
            };
            _connection = new SqliteConnection(csb.ToString());
            _connection.Open();

            EventStoreSchema.EnsureSchema(_connection);

            _flushThread = new Thread(FlushLoop)
            {
                IsBackground = true,
                Name = "TinyWall.FirewallEventStore.Flush",
            };
            _flushThread.Start();
        }

        public void Enqueue(FirewallLogEntry entry, FirewallMode modeAtEvent, long rulesetId)
        {
            if (_disposed || !_enabled)
                return;

            long current = Interlocked.Read(ref _queueCount);
            if (current >= QueueCapacity)
            {
                Interlocked.Increment(ref _eventsDropped);
                return;
            }

            _queue.Enqueue(new PendingEvent(entry, modeAtEvent, rulesetId));
            long newCount = Interlocked.Increment(ref _queueCount);
            if (newCount >= FlushThreshold)
                _flushSignal.Set();
        }

        public long GetOrCreateRulesetSnapshot(byte[] canonicalJson)
        {
            if (canonicalJson is null)
                throw new ArgumentNullException(nameof(canonicalJson));

            string hash;
            using (var sha = SHA256.Create())
            {
                hash = Convert.ToHexString(sha.ComputeHash(canonicalJson));
            }

            lock (_writeLock)
            {
                using (var selectCmd = _connection.CreateCommand())
                {
                    selectCmd.CommandText = "SELECT id FROM rulesets WHERE content_hash = $h LIMIT 1";
                    selectCmd.Parameters.AddWithValue("$h", hash);
                    var existing = selectCmd.ExecuteScalar();
                    if (existing is long lid)
                        return lid;
                    if (existing is not null)
                        return Convert.ToInt64(existing);
                }

                using var insertCmd = _connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO rulesets (timestamp_utc_ms, content_hash, content_json)
                    VALUES ($ts, $h, $j);
                    SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                insertCmd.Parameters.AddWithValue("$h", hash);
                insertCmd.Parameters.AddWithValue("$j", canonicalJson);
                var id = insertCmd.ExecuteScalar();
                return Convert.ToInt64(id);
            }
        }

        public void RunMaintenance()
        {
            if (_disposed)
                return;

            lock (_writeLock)
            {
                long cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - HotRetentionMs;

                // Move rows older than cutoff from hot to warm.
                using (var tx = _connection.BeginTransaction())
                {
                    using (var insert = _connection.CreateCommand())
                    {
                        insert.Transaction = tx;
                        insert.CommandText = @"
                            INSERT INTO events_warm
                                (timestamp_utc_ms, action, reason_id, app_name, remote_ip,
                                 remote_port, protocol, ruleset_id, schema_version)
                            SELECT timestamp_utc_ms, action, reason_id, app_name, remote_ip,
                                   remote_port, protocol, ruleset_id, schema_version
                            FROM events_hot
                            WHERE timestamp_utc_ms < $cutoff;";
                        insert.Parameters.AddWithValue("$cutoff", cutoff);
                        insert.ExecuteNonQuery();
                    }

                    using (var del = _connection.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM events_hot WHERE timestamp_utc_ms < $cutoff;";
                        del.Parameters.AddWithValue("$cutoff", cutoff);
                        del.ExecuteNonQuery();
                    }

                    tx.Commit();
                }

                // Enforce the hot cap: oldest rows over HotMaxRows go away.
                using (var trim = _connection.CreateCommand())
                {
                    trim.CommandText = @"
                        DELETE FROM events_hot
                        WHERE id IN (
                            SELECT id FROM events_hot
                            ORDER BY timestamp_utc_ms ASC, id ASC
                            LIMIT MAX(0, (SELECT COUNT(*) FROM events_hot) - $cap)
                        );";
                    trim.Parameters.AddWithValue("$cap", HotMaxRows);
                    trim.ExecuteNonQuery();
                }

                // Enforce the warm cap.
                using (var trim = _connection.CreateCommand())
                {
                    trim.CommandText = @"
                        DELETE FROM events_warm
                        WHERE id IN (
                            SELECT id FROM events_warm
                            ORDER BY timestamp_utc_ms ASC, id ASC
                            LIMIT MAX(0, (SELECT COUNT(*) FROM events_warm) - $cap)
                        );";
                    trim.Parameters.AddWithValue("$cap", WarmMaxRows);
                    trim.ExecuteNonQuery();
                }
            }
        }

        private void FlushLoop()
        {
            while (!_disposed)
            {
                // Wait for either the interval or a manual signal from the producer.
                _flushSignal.Wait(FlushIntervalMs);
                _flushSignal.Reset();

                if (_disposed)
                    break;

                try
                {
                    FlushOnce();
                }
                catch
                {
                    // Swallow — we never want the flush thread to take the service down.
                    // Drops are already accounted for via _eventsDropped on the enqueue side.
                }
            }
        }

        private void FlushOnce()
        {
            if (_queue.IsEmpty)
                return;

            lock (_writeLock)
            {
                using var tx = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO events_hot (
                        decision_id, flow_id, timestamp_utc_ms, action, direction, protocol,
                        local_ip, local_port, remote_ip, remote_port,
                        pid, app_path, app_name, package_sid, service_name,
                        mode_at_event, ruleset_id, reason_id, confidence,
                        matched_rule_id, near_miss_rule_ids, schema_version
                    ) VALUES (
                        $decision_id, $flow_id, $ts, $action, $direction, $protocol,
                        $local_ip, $local_port, $remote_ip, $remote_port,
                        $pid, $app_path, $app_name, $package_sid, $service_name,
                        $mode, $ruleset_id, $reason_id, $confidence,
                        $matched_rule_id, $near_miss_rule_ids, $schema_version
                    );";

                var pDecision = cmd.Parameters.Add("$decision_id", SqliteType.Text);
                var pFlow = cmd.Parameters.Add("$flow_id", SqliteType.Integer);
                var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);
                var pAction = cmd.Parameters.Add("$action", SqliteType.Integer);
                var pDirection = cmd.Parameters.Add("$direction", SqliteType.Integer);
                var pProtocol = cmd.Parameters.Add("$protocol", SqliteType.Integer);
                var pLocalIp = cmd.Parameters.Add("$local_ip", SqliteType.Text);
                var pLocalPort = cmd.Parameters.Add("$local_port", SqliteType.Integer);
                var pRemoteIp = cmd.Parameters.Add("$remote_ip", SqliteType.Text);
                var pRemotePort = cmd.Parameters.Add("$remote_port", SqliteType.Integer);
                var pPid = cmd.Parameters.Add("$pid", SqliteType.Integer);
                var pAppPath = cmd.Parameters.Add("$app_path", SqliteType.Text);
                var pAppName = cmd.Parameters.Add("$app_name", SqliteType.Text);
                var pPackageSid = cmd.Parameters.Add("$package_sid", SqliteType.Text);
                var pServiceName = cmd.Parameters.Add("$service_name", SqliteType.Text);
                var pMode = cmd.Parameters.Add("$mode", SqliteType.Integer);
                var pRulesetId = cmd.Parameters.Add("$ruleset_id", SqliteType.Integer);
                var pReasonId = cmd.Parameters.Add("$reason_id", SqliteType.Integer);
                var pConfidence = cmd.Parameters.Add("$confidence", SqliteType.Integer);
                var pMatchedRuleId = cmd.Parameters.Add("$matched_rule_id", SqliteType.Text);
                var pNearMissIds = cmd.Parameters.Add("$near_miss_rule_ids", SqliteType.Text);
                var pSchemaVersion = cmd.Parameters.Add("$schema_version", SqliteType.Integer);

                int drained = 0;
                while (_queue.TryDequeue(out var pending))
                {
                    Interlocked.Decrement(ref _queueCount);
                    drained++;

                    var entry = pending.Entry;
                    var action = entry.Event == EventLogEvent.BLOCKED ? EventAction.Block : EventAction.Allow;
                    long tsMs = new DateTimeOffset(entry.Timestamp.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds();

                    pDecision.Value = Guid.NewGuid().ToString("N");
                    pFlow.Value = ComputeFlowId(entry, tsMs);
                    pTs.Value = tsMs;
                    pAction.Value = (int)action;
                    pDirection.Value = (int)entry.Direction;
                    pProtocol.Value = (int)entry.Protocol;
                    pLocalIp.Value = (object?)entry.LocalIp ?? DBNull.Value;
                    pLocalPort.Value = entry.LocalPort;
                    pRemoteIp.Value = (object?)entry.RemoteIp ?? DBNull.Value;
                    pRemotePort.Value = entry.RemotePort;
                    pPid.Value = (long)entry.ProcessId;
                    pAppPath.Value = (object?)entry.AppPath ?? DBNull.Value;
                    pAppName.Value = (object?)(entry.AppPath is null ? null : Path.GetFileName(entry.AppPath)) ?? DBNull.Value;
                    pPackageSid.Value = (object?)entry.PackageId ?? DBNull.Value;
                    pServiceName.Value = DBNull.Value;
                    pMode.Value = (int)pending.ModeAtEvent;
                    pRulesetId.Value = pending.RulesetId;
                    pReasonId.Value = (int)ReasonId.Unknown;      // Phase 1: always Unknown
                    pConfidence.Value = (int)Confidence.Unknown;  // Phase 1: always Unknown
                    pMatchedRuleId.Value = DBNull.Value;
                    pNearMissIds.Value = DBNull.Value;
                    pSchemaVersion.Value = EventStoreSchema.CurrentSchemaVersion;

                    cmd.ExecuteNonQuery();
                }

                if (drained > 0)
                    tx.Commit();
                else
                    tx.Rollback();
            }
        }

        /// <summary>
        /// Computes the flow_id as FNV-1a(5-tuple) XOR (timestamp_bucket / 10s).
        /// See Docs/EXPLAINABILITY.md section 5.3.
        /// </summary>
        private static long ComputeFlowId(FirewallLogEntry entry, long tsMs)
        {
            const ulong FnvOffset = 14695981039346656037UL;
            const ulong FnvPrime = 1099511628211UL;

            ulong hash = FnvOffset;
            hash = Fnv(hash, entry.LocalIp);
            hash = Fnv(hash, entry.RemoteIp);
            hash = Fnv(hash, entry.LocalPort);
            hash = Fnv(hash, entry.RemotePort);
            hash = Fnv(hash, (int)entry.Protocol);

            long bucket = tsMs / 10_000;
            return unchecked((long)(hash ^ (ulong)bucket));

            static ulong Fnv(ulong acc, object? value)
            {
                if (value is null)
                    return acc * FnvPrime;
                string s = value.ToString() ?? string.Empty;
                foreach (char c in s)
                {
                    acc ^= c;
                    acc *= FnvPrime;
                }
                return acc;
            }
        }

        public FirewallEventRecord? GetEventById(long id)
        {
            if (_disposed)
                return null;

            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, decision_id, flow_id, timestamp_utc_ms, action, direction, protocol,
                           local_ip, local_port, remote_ip, remote_port,
                           pid, app_path, app_name, package_sid, service_name,
                           mode_at_event, ruleset_id, reason_id, confidence,
                           matched_rule_id, near_miss_rule_ids, schema_version
                      FROM events_hot
                     WHERE id = $id
                     LIMIT 1;";
                cmd.Parameters.AddWithValue("$id", id);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return null;
                return MapRecord(reader);
            }
        }

        public IReadOnlyList<FirewallEventRecord> GetUnexplainedBatch(int limit)
        {
            if (_disposed || limit <= 0)
                return Array.Empty<FirewallEventRecord>();

            var results = new List<FirewallEventRecord>(Math.Min(limit, 256));
            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, decision_id, flow_id, timestamp_utc_ms, action, direction, protocol,
                           local_ip, local_port, remote_ip, remote_port,
                           pid, app_path, app_name, package_sid, service_name,
                           mode_at_event, ruleset_id, reason_id, confidence,
                           matched_rule_id, near_miss_rule_ids, schema_version
                      FROM events_hot
                     WHERE reason_id = 0
                     ORDER BY id ASC
                     LIMIT $limit;";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    results.Add(MapRecord(reader));
            }
            return results;
        }

        public void UpdateReason(long eventId, ReasonId reason, Confidence confidence, string? matchedRuleId, string? nearMissRuleIds)
        {
            if (_disposed)
                return;

            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE events_hot
                       SET reason_id = $reason,
                           confidence = $conf,
                           matched_rule_id = $ruleId,
                           near_miss_rule_ids = $nearMiss
                     WHERE id = $id;";
                cmd.Parameters.AddWithValue("$reason", (int)reason);
                cmd.Parameters.AddWithValue("$conf", (int)confidence);
                cmd.Parameters.AddWithValue("$ruleId", (object?)matchedRuleId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$nearMiss", (object?)nearMissRuleIds ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$id", eventId);
                cmd.ExecuteNonQuery();
            }
        }

        public int UpdateReasons(IReadOnlyList<ReasonUpdate> updates)
        {
            if (_disposed || updates is null || updates.Count == 0)
                return 0;

            int n = 0;
            lock (_writeLock)
            {
                using var tx = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    UPDATE events_hot
                       SET reason_id = $reason,
                           confidence = $conf,
                           matched_rule_id = $ruleId,
                           near_miss_rule_ids = $nearMiss
                     WHERE id = $id;";
                var pReason = cmd.Parameters.Add("$reason", SqliteType.Integer);
                var pConf = cmd.Parameters.Add("$conf", SqliteType.Integer);
                var pRuleId = cmd.Parameters.Add("$ruleId", SqliteType.Text);
                var pNearMiss = cmd.Parameters.Add("$nearMiss", SqliteType.Text);
                var pId = cmd.Parameters.Add("$id", SqliteType.Integer);

                foreach (var u in updates)
                {
                    pReason.Value = (int)u.Reason;
                    pConf.Value = (int)u.Confidence;
                    pRuleId.Value = (object?)u.MatchedRuleId ?? DBNull.Value;
                    pNearMiss.Value = (object?)u.NearMissRuleIds ?? DBNull.Value;
                    pId.Value = u.EventId;
                    n += cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            return n;
        }

        public RulesetSnapshot? GetRulesetSnapshot(long id)
        {
            if (_disposed)
                return null;

            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id, timestamp_utc_ms, content_hash, content_json FROM rulesets WHERE id = $id LIMIT 1;";
                cmd.Parameters.AddWithValue("$id", id);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return null;

                long rid = reader.GetInt64(0);
                long ts = reader.GetInt64(1);
                string hash = reader.GetString(2);
                byte[] json;
                using (var stream = reader.GetStream(3))
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    json = ms.ToArray();
                }

                return new RulesetSnapshot
                {
                    Id = rid,
                    TimestampUtcMs = ts,
                    ContentHash = hash,
                    ContentJson = json,
                };
            }
        }

        private static FirewallEventRecord MapRecord(SqliteDataReader r)
        {
            var rec = new FirewallEventRecord
            {
                Id = r.GetInt64(0),
                DecisionId = Guid.TryParseExact(r.GetString(1), "N", out var g) ? g : Guid.Empty,
                FlowId = r.GetInt64(2),
                TimestampUtcMs = r.GetInt64(3),
                Action = (EventAction)r.GetInt32(4),
                Direction = (RuleDirection)r.GetInt32(5),
                Protocol = (Protocol)r.GetInt32(6),
                LocalIp = r.IsDBNull(7) ? null : r.GetString(7),
                LocalPort = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                RemoteIp = r.IsDBNull(9) ? null : r.GetString(9),
                RemotePort = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                Pid = r.IsDBNull(11) ? 0u : (uint)r.GetInt64(11),
                AppPath = r.IsDBNull(12) ? null : r.GetString(12),
                AppName = r.IsDBNull(13) ? null : r.GetString(13),
                PackageSid = r.IsDBNull(14) ? null : r.GetString(14),
                ServiceName = r.IsDBNull(15) ? null : r.GetString(15),
                ModeAtEvent = (FirewallMode)r.GetInt32(16),
                RulesetId = r.GetInt64(17),
                ReasonId = (ReasonId)r.GetInt32(18),
                Confidence = (Confidence)r.GetInt32(19),
                MatchedRuleId = r.IsDBNull(20) ? null : r.GetString(20),
                NearMissRuleIds = r.IsDBNull(21) ? null : r.GetString(21),
                SchemaVersion = r.GetInt32(22),
            };
            return rec;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _flushSignal.Set();
            try { _flushThread.Join(TimeSpan.FromSeconds(5)); } catch { }

            try { FlushOnce(); } catch { }

            try { _connection.Dispose(); } catch { }
            _flushSignal.Dispose();
        }

        private readonly struct PendingEvent
        {
            public readonly FirewallLogEntry Entry;
            public readonly FirewallMode ModeAtEvent;
            public readonly long RulesetId;

            public PendingEvent(FirewallLogEntry entry, FirewallMode modeAtEvent, long rulesetId)
            {
                Entry = entry;
                ModeAtEvent = modeAtEvent;
                RulesetId = rulesetId;
            }
        }
    }
}
