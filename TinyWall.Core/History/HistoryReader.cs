using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Read-only access to the firewall history database, safe to use from
    /// the unelevated UI process. Opens the SQLite file with
    /// <see cref="SqliteOpenMode.ReadOnly"/> so it does not compete with the
    /// service's writer connection — WAL mode allows concurrent readers.
    ///
    /// See Docs/EXPLAINABILITY.md section 7 ("Explain API").
    /// </summary>
    public sealed class HistoryReader : IDisposable
    {
        private readonly SqliteConnection? _connection;
        private readonly string _dbPath;
        private bool _disposed;

        /// <summary>True if the database file exists and was opened successfully.</summary>
        public bool IsAvailable => _connection != null;

        public string DatabasePath => _dbPath;

        public HistoryReader()
            : this(Path.Combine(Utils.AppDataPath, "history.db"))
        {
        }

        public HistoryReader(string dbPath)
        {
            _dbPath = dbPath;

            // The service creates the DB on startup; if it hasn't yet, we
            // silently disable rather than crash the UI.
            if (!File.Exists(_dbPath))
            {
                _connection = null;
                return;
            }

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Default,
            };

            try
            {
                _connection = new SqliteConnection(csb.ToString());
                _connection.Open();
            }
            catch (SqliteException)
            {
                _connection?.Dispose();
                _connection = null;
            }
        }

        /// <summary>
        /// Returns up to <paramref name="limit"/> events from events_hot
        /// matching <paramref name="filter"/>, ordered newest first, skipping
        /// <paramref name="offset"/> rows. Returns an empty list if the DB
        /// is not available.
        /// </summary>
        public IReadOnlyList<FirewallEventRecord> GetRecentEvents(int limit, int offset, HistoryFilter? filter)
        {
            if (_disposed || _connection is null || limit <= 0)
                return Array.Empty<FirewallEventRecord>();

            var results = new List<FirewallEventRecord>(Math.Min(limit, 256));

            using var cmd = _connection.CreateCommand();
            var sb = new StringBuilder();
            sb.Append(@"
                SELECT id, decision_id, flow_id, timestamp_utc_ms, action, direction, protocol,
                       local_ip, local_port, remote_ip, remote_port,
                       pid, app_path, app_name, package_sid, service_name,
                       mode_at_event, ruleset_id, reason_id, confidence,
                       matched_rule_id, near_miss_rule_ids, schema_version
                  FROM events_hot");
            AppendWhereClause(cmd, sb, filter);
            sb.Append(" ORDER BY id DESC LIMIT $limit OFFSET $offset;");

            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(FirewallEventStore.MapRecord(reader));

            return results;
        }

        /// <summary>
        /// Loads the raw JSON bytes of a ruleset snapshot. Returns null
        /// if the snapshot is missing or the DB is not available. Used by
        /// the forensic export path which needs the exact stored blob to
        /// embed verbatim in the bundle.
        /// </summary>
        public byte[]? GetRulesetSnapshotBytes(long rulesetId)
        {
            if (_disposed || _connection is null)
                return null;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT content_json FROM rulesets WHERE id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", rulesetId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
                return null;

            using var stream = reader.GetStream(0);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Loads a ruleset snapshot by id and deserializes it into a
        /// <see cref="ServerConfiguration"/>. Returns null if the snapshot
        /// is missing, the database is not available, or the blob cannot
        /// be decoded.
        ///
        /// Used by the drill-down pane in HistoryWindow to re-run
        /// <see cref="ExplanationService.ExplainAgainst"/> against the
        /// ruleset that was active when the event was captured.
        /// </summary>
        public ServerConfiguration? GetRulesetConfig(long rulesetId)
        {
            var json = GetRulesetSnapshotBytes(rulesetId);
            if (json is null || json.Length == 0)
                return null;

            try
            {
                return SerializationHelper.Deserialize(json, new ServerConfiguration());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Total row count in events_hot matching <paramref name="filter"/>.
        /// Returns 0 if the DB is not available.
        /// </summary>
        public long CountEvents(HistoryFilter? filter)
        {
            if (_disposed || _connection is null)
                return 0;

            using var cmd = _connection.CreateCommand();
            var sb = new StringBuilder();
            sb.Append("SELECT COUNT(*) FROM events_hot");
            AppendWhereClause(cmd, sb, filter);
            sb.Append(';');
            cmd.CommandText = sb.ToString();

            var result = cmd.ExecuteScalar();
            return result is long l ? l : Convert.ToInt64(result ?? 0);
        }

        private static void AppendWhereClause(SqliteCommand cmd, StringBuilder sb, HistoryFilter? filter)
        {
            if (filter is null)
                return;

            bool hasAny = false;
            void Add(string clause)
            {
                sb.Append(hasAny ? " AND " : " WHERE ");
                sb.Append(clause);
                hasAny = true;
            }

            if (filter.SinceUtcMs.HasValue)
            {
                Add("timestamp_utc_ms >= $since");
                cmd.Parameters.AddWithValue("$since", filter.SinceUtcMs.Value);
            }
            if (filter.UntilUtcMs.HasValue)
            {
                Add("timestamp_utc_ms < $until");
                cmd.Parameters.AddWithValue("$until", filter.UntilUtcMs.Value);
            }
            if (filter.Action.HasValue)
            {
                Add("action = $action");
                cmd.Parameters.AddWithValue("$action", (int)filter.Action.Value);
            }
            if (filter.Reason.HasValue)
            {
                Add("reason_id = $reason");
                cmd.Parameters.AddWithValue("$reason", (int)filter.Reason.Value);
            }
            if (filter.Direction.HasValue)
            {
                Add("direction = $dir");
                cmd.Parameters.AddWithValue("$dir", (int)filter.Direction.Value);
            }
            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                // Case-insensitive substring match across the common identity columns.
                Add("(app_name LIKE $q OR app_path LIKE $q OR remote_ip LIKE $q OR service_name LIKE $q)");
                cmd.Parameters.AddWithValue("$q", "%" + filter.SearchText + "%");
            }
        }

        // ====================================================================
        // ===== Stats / aggregate queries (Phase 4.4) ========================
        // ====================================================================

        /// <summary>
        /// Health snapshot of the event store: row counts, file size,
        /// ruleset count, and event time range. Returns a record with all
        /// zero/null fields if the database is not available.
        /// </summary>
        public StoreHealth GetStoreHealth()
        {
            if (_disposed || _connection is null)
                return new StoreHealth(0, 0, 0, 0, null, null);

            long hot = ScalarLong("SELECT COUNT(*) FROM events_hot");
            long warm = ScalarLong("SELECT COUNT(*) FROM events_warm");
            long rulesets = ScalarLong("SELECT COUNT(*) FROM rulesets");

            long? oldest = ScalarNullableLong("SELECT MIN(timestamp_utc_ms) FROM events_hot");
            long? newest = ScalarNullableLong("SELECT MAX(timestamp_utc_ms) FROM events_hot");

            long sizeBytes = 0;
            try
            {
                if (File.Exists(_dbPath))
                    sizeBytes = new FileInfo(_dbPath).Length;
            }
            catch { /* best-effort */ }

            return new StoreHealth(hot, warm, sizeBytes, rulesets, oldest, newest);
        }

        /// <summary>
        /// Top <paramref name="limit"/> app_name values by event count in
        /// events_hot, optionally filtered by action. Apps with NULL/empty
        /// names are excluded. Ordered descending.
        /// </summary>
        public IReadOnlyList<StatsBucket<string>> GetTopApps(int limit, EventAction? action)
        {
            return GroupByCount<string>(
                column: "app_name",
                limit: limit,
                action: action,
                read: r => r.GetString(0));
        }

        /// <summary>
        /// Top <paramref name="limit"/> remote_ip values by event count.
        /// </summary>
        public IReadOnlyList<StatsBucket<string>> GetTopRemoteIps(int limit, EventAction? action)
        {
            return GroupByCount<string>(
                column: "remote_ip",
                limit: limit,
                action: action,
                read: r => r.GetString(0));
        }

        /// <summary>
        /// Counts of each reason_id in events_hot, ordered by count DESC.
        /// </summary>
        public IReadOnlyList<StatsBucket<int>> GetReasonDistribution(EventAction? action)
        {
            if (_disposed || _connection is null)
                return Array.Empty<StatsBucket<int>>();

            using var cmd = _connection.CreateCommand();
            var sql = new System.Text.StringBuilder();
            sql.Append("SELECT reason_id, COUNT(*) AS c FROM events_hot");
            if (action.HasValue)
            {
                sql.Append(" WHERE action = $action");
                cmd.Parameters.AddWithValue("$action", (int)action.Value);
            }
            sql.Append(" GROUP BY reason_id ORDER BY c DESC;");
            cmd.CommandText = sql.ToString();

            var results = new List<StatsBucket<int>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new StatsBucket<int>(reader.GetInt32(0), reader.GetInt64(1)));
            }
            return results;
        }

        /// <summary>
        /// Returns one row per hour for the last <paramref name="hoursBack"/>
        /// hours, each containing the bucket-start UTC ms and the count of
        /// events in that hour. Hours with zero events are filled in so the
        /// caller can render a contiguous sparkline without gaps.
        /// </summary>
        public IReadOnlyList<StatsBucket<long>> GetEventsPerHour(int hoursBack, EventAction? action)
        {
            if (_disposed || _connection is null || hoursBack <= 0)
                return Array.Empty<StatsBucket<long>>();

            const long MillisPerHour = 3600L * 1000L;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long alignedNow = (nowMs / MillisPerHour) * MillisPerHour;
            long startMs = alignedNow - (hoursBack - 1) * MillisPerHour;

            // Pull non-zero buckets in one query, then fill gaps in C#.
            var counts = new Dictionary<long, long>(hoursBack);
            using (var cmd = _connection.CreateCommand())
            {
                var sql = new System.Text.StringBuilder();
                sql.Append("SELECT (timestamp_utc_ms / $hr) * $hr AS bucket, COUNT(*) AS c ");
                sql.Append("FROM events_hot WHERE timestamp_utc_ms >= $start");
                if (action.HasValue)
                {
                    sql.Append(" AND action = $action");
                    cmd.Parameters.AddWithValue("$action", (int)action.Value);
                }
                sql.Append(" GROUP BY bucket;");
                cmd.CommandText = sql.ToString();
                cmd.Parameters.AddWithValue("$hr", MillisPerHour);
                cmd.Parameters.AddWithValue("$start", startMs);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    counts[reader.GetInt64(0)] = reader.GetInt64(1);
            }

            var results = new List<StatsBucket<long>>(hoursBack);
            for (int i = 0; i < hoursBack; i++)
            {
                long bucket = startMs + i * MillisPerHour;
                counts.TryGetValue(bucket, out long c);
                results.Add(new StatsBucket<long>(bucket, c));
            }
            return results;
        }

        // ----- Helpers ------------------------------------------------------

        private long ScalarLong(string sql)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;
            var r = cmd.ExecuteScalar();
            return r is long l ? l : Convert.ToInt64(r ?? 0);
        }

        private long? ScalarNullableLong(string sql)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql;
            var r = cmd.ExecuteScalar();
            if (r is null || r is DBNull) return null;
            return r is long l ? l : Convert.ToInt64(r);
        }

        private IReadOnlyList<StatsBucket<T>> GroupByCount<T>(
            string column,
            int limit,
            EventAction? action,
            Func<SqliteDataReader, T> read)
        {
            if (_disposed || _connection is null || limit <= 0)
                return Array.Empty<StatsBucket<T>>();

            using var cmd = _connection.CreateCommand();
            var sql = new System.Text.StringBuilder();
            sql.Append($"SELECT {column}, COUNT(*) AS c FROM events_hot WHERE {column} IS NOT NULL AND {column} != ''");
            if (action.HasValue)
            {
                sql.Append(" AND action = $action");
                cmd.Parameters.AddWithValue("$action", (int)action.Value);
            }
            sql.Append($" GROUP BY {column} ORDER BY c DESC LIMIT $limit;");
            cmd.CommandText = sql.ToString();
            cmd.Parameters.AddWithValue("$limit", limit);

            var results = new List<StatsBucket<T>>(Math.Min(limit, 32));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new StatsBucket<T>(read(reader), reader.GetInt64(1)));
            }
            return results;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try { _connection?.Dispose(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Health snapshot of the event store. Returned by
    /// <see cref="HistoryReader.GetStoreHealth"/>.
    /// </summary>
    public sealed record StoreHealth(
        long HotRowCount,
        long WarmRowCount,
        long DbSizeBytes,
        long RulesetCount,
        long? OldestEventUtcMs,
        long? NewestEventUtcMs);

    /// <summary>
    /// One bucket from a stats query: a key plus the number of events
    /// that fell into it.
    /// </summary>
    public sealed record StatsBucket<T>(T Key, long Count);

    /// <summary>
    /// Optional filter applied to <see cref="HistoryReader.GetRecentEvents"/>
    /// and <see cref="HistoryReader.CountEvents"/>. Nullable fields mean
    /// "no restriction" — the filter composes the non-null fields with AND.
    /// </summary>
    public sealed class HistoryFilter
    {
        public long? SinceUtcMs { get; set; }
        public long? UntilUtcMs { get; set; }
        public EventAction? Action { get; set; }
        public ReasonId? Reason { get; set; }
        public RuleDirection? Direction { get; set; }
        public string? SearchText { get; set; }
    }
}
