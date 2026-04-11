using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using pylorak.TinyWall.History;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    /// <summary>
    /// Exercises <see cref="HistoryReader"/> against a temp SQLite file.
    /// The tests seed rows with raw SQL (bypassing the service's background
    /// flush thread) so assertions are deterministic.
    /// </summary>
    public class HistoryReaderTests : IDisposable
    {
        private readonly string _dbPath;

        public HistoryReaderTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(),
                "tw_historyreader_" + Guid.NewGuid().ToString("N") + ".db");
            SeedDatabase(_dbPath);
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
            try
            {
                var walPath = _dbPath + "-wal";
                if (File.Exists(walPath)) File.Delete(walPath);
                var shmPath = _dbPath + "-shm";
                if (File.Exists(shmPath)) File.Delete(shmPath);
            }
            catch { }
        }

        [Fact]
        public void Returns_empty_list_when_database_missing()
        {
            using var reader = new HistoryReader(
                Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".db"));

            Assert.False(reader.IsAvailable);
            Assert.Empty(reader.GetRecentEvents(10, 0, null));
            Assert.Equal(0, reader.CountEvents(null));
        }

        [Fact]
        public void Returns_all_rows_ordered_newest_first_when_filter_is_null()
        {
            using var reader = new HistoryReader(_dbPath);
            Assert.True(reader.IsAvailable);

            var rows = reader.GetRecentEvents(100, 0, null);
            Assert.Equal(6, rows.Count);

            // Highest id first.
            Assert.Equal(6, rows[0].Id);
            Assert.Equal(1, rows[^1].Id);
        }

        [Fact]
        public void Pagination_respects_limit_and_offset()
        {
            using var reader = new HistoryReader(_dbPath);

            var page1 = reader.GetRecentEvents(2, 0, null);
            var page2 = reader.GetRecentEvents(2, 2, null);

            Assert.Equal(2, page1.Count);
            Assert.Equal(2, page2.Count);
            Assert.Equal(6, page1[0].Id);
            Assert.Equal(5, page1[1].Id);
            Assert.Equal(4, page2[0].Id);
            Assert.Equal(3, page2[1].Id);
        }

        [Fact]
        public void Filter_by_action_returns_only_matching_rows()
        {
            using var reader = new HistoryReader(_dbPath);

            var blocks = reader.GetRecentEvents(100, 0, new HistoryFilter { Action = EventAction.Block });
            var allows = reader.GetRecentEvents(100, 0, new HistoryFilter { Action = EventAction.Allow });

            Assert.Equal(4, blocks.Count);
            Assert.Equal(2, allows.Count);
            Assert.All(blocks, r => Assert.Equal(EventAction.Block, r.Action));
            Assert.All(allows, r => Assert.Equal(EventAction.Allow, r.Action));
        }

        [Fact]
        public void Filter_by_reason_returns_only_matching_rows()
        {
            using var reader = new HistoryReader(_dbPath);

            var rows = reader.GetRecentEvents(100, 0,
                new HistoryFilter { Reason = ReasonId.BlockedNoMatchInNormal });

            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Equal(ReasonId.BlockedNoMatchInNormal, r.ReasonId));
        }

        [Fact]
        public void Filter_by_search_text_matches_app_name_and_remote_ip()
        {
            using var reader = new HistoryReader(_dbPath);

            var byApp = reader.GetRecentEvents(100, 0, new HistoryFilter { SearchText = "chrome" });
            Assert.All(byApp, r => Assert.Contains("chrome", r.AppName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(byApp);

            var byIp = reader.GetRecentEvents(100, 0, new HistoryFilter { SearchText = "10.0.0" });
            Assert.NotEmpty(byIp);
            Assert.All(byIp, r => Assert.StartsWith("10.0.0", r.RemoteIp));
        }

        [Fact]
        public void Filter_by_time_window_excludes_rows_outside_range()
        {
            using var reader = new HistoryReader(_dbPath);

            // Rows were seeded with ts = 1000..6000. Window [3000, 5000) keeps ids 3 and 4.
            var rows = reader.GetRecentEvents(100, 0,
                new HistoryFilter { SinceUtcMs = 3000, UntilUtcMs = 5000 });

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.Id == 3);
            Assert.Contains(rows, r => r.Id == 4);
        }

        [Fact]
        public void CountEvents_matches_GetRecentEvents_total_when_unpaginated()
        {
            using var reader = new HistoryReader(_dbPath);

            Assert.Equal(6, reader.CountEvents(null));
            Assert.Equal(4, reader.CountEvents(new HistoryFilter { Action = EventAction.Block }));
            Assert.Equal(3, reader.CountEvents(new HistoryFilter { Reason = ReasonId.BlockedNoMatchInNormal }));
        }

        [Fact]
        public void GetStoreHealth_reports_row_counts_and_time_range()
        {
            using var reader = new HistoryReader(_dbPath);
            var h = reader.GetStoreHealth();

            Assert.Equal(6, h.HotRowCount);
            Assert.Equal(0, h.WarmRowCount);
            Assert.True(h.DbSizeBytes > 0);
            Assert.Equal(1, h.RulesetCount);
            Assert.Equal(1000, h.OldestEventUtcMs);
            Assert.Equal(6000, h.NewestEventUtcMs);
        }

        [Fact]
        public void GetTopApps_returns_buckets_ordered_descending()
        {
            using var reader = new HistoryReader(_dbPath);
            var top = reader.GetTopApps(10, action: null);

            // chrome.exe, firefox.exe, svchost.exe each appear twice in the seed
            Assert.Equal(3, top.Count);
            Assert.All(top, b => Assert.Equal(2, b.Count));
            Assert.Contains(top, b => b.Key == "chrome.exe");
            Assert.Contains(top, b => b.Key == "firefox.exe");
            Assert.Contains(top, b => b.Key == "svchost.exe");
        }

        [Fact]
        public void GetTopApps_with_block_filter_excludes_allows()
        {
            using var reader = new HistoryReader(_dbPath);
            var blocks = reader.GetTopApps(10, EventAction.Block);

            // 4 block rows: chrome (1), firefox (1), svchost (2)
            Assert.Equal(3, blocks.Count);
            var byKey = blocks.ToDictionary(b => b.Key, b => b.Count);
            Assert.Equal(2, byKey["svchost.exe"]);
            Assert.Equal(1, byKey["chrome.exe"]);
            Assert.Equal(1, byKey["firefox.exe"]);
        }

        [Fact]
        public void GetTopRemoteIps_returns_distinct_remotes()
        {
            using var reader = new HistoryReader(_dbPath);
            var top = reader.GetTopRemoteIps(10, action: null);

            // Each of the 6 rows has a unique remote ip
            Assert.Equal(6, top.Count);
            Assert.All(top, b => Assert.Equal(1, b.Count));
        }

        [Fact]
        public void GetReasonDistribution_groups_by_reason_id()
        {
            using var reader = new HistoryReader(_dbPath);
            var dist = reader.GetReasonDistribution(action: null);

            var byKey = dist.ToDictionary(b => b.Key, b => b.Count);
            Assert.Equal(3, byKey[103]); // BlockedNoMatchInNormal
            Assert.Equal(2, byKey[10]);  // AllowedByMatchedRule
            Assert.Equal(1, byKey[101]); // BlockedByModeDisabled
            // Ordered DESC by count → 103 first
            Assert.Equal(103, dist[0].Key);
        }

        [Fact]
        public void GetEventsPerHour_returns_contiguous_buckets_with_zeros_filled()
        {
            using var reader = new HistoryReader(_dbPath);
            var buckets = reader.GetEventsPerHour(24, action: null);

            // 24 contiguous hour buckets, even if all are empty (the seed
            // events are at fake epoch ms 1000..6000 which is in 1970, so
            // they're definitely older than the last 24h).
            Assert.Equal(24, buckets.Count);
            Assert.All(buckets, b => Assert.Equal(0, b.Count));

            // Bucket starts should be increasing and exactly one hour apart
            for (int i = 1; i < buckets.Count; i++)
                Assert.Equal(3600_000L, buckets[i].Key - buckets[i - 1].Key);
        }

        /// <summary>
        /// Seeds a fresh SQLite file with the minimum schema + six rows that
        /// exercise the filter matrix: two allows, four blocks, three
        /// different reasons, two apps, two remote IPs, timestamps 1000..6000.
        /// </summary>
        private static void SeedDatabase(string path)
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
            };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            using (var ddl = conn.CreateCommand())
            {
                ddl.CommandText = @"
                    CREATE TABLE rulesets (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp_utc_ms INTEGER NOT NULL,
                        content_hash TEXT NOT NULL UNIQUE,
                        content_json BLOB NOT NULL
                    );
                    CREATE TABLE events_hot (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        decision_id TEXT NOT NULL,
                        flow_id INTEGER NOT NULL,
                        timestamp_utc_ms INTEGER NOT NULL,
                        action INTEGER NOT NULL,
                        direction INTEGER NOT NULL,
                        protocol INTEGER NOT NULL,
                        local_ip TEXT,
                        local_port INTEGER,
                        remote_ip TEXT,
                        remote_port INTEGER,
                        pid INTEGER,
                        app_path TEXT,
                        app_name TEXT,
                        package_sid TEXT,
                        service_name TEXT,
                        mode_at_event INTEGER NOT NULL,
                        ruleset_id INTEGER NOT NULL,
                        reason_id INTEGER NOT NULL DEFAULT 0,
                        confidence INTEGER NOT NULL DEFAULT 0,
                        matched_rule_id TEXT,
                        near_miss_rule_ids TEXT,
                        schema_version INTEGER NOT NULL
                    );
                    CREATE TABLE events_warm (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp_utc_ms INTEGER NOT NULL,
                        action INTEGER NOT NULL,
                        reason_id INTEGER NOT NULL,
                        app_name TEXT,
                        remote_ip TEXT,
                        remote_port INTEGER,
                        protocol INTEGER NOT NULL,
                        ruleset_id INTEGER NOT NULL,
                        schema_version INTEGER NOT NULL
                    );
                    INSERT INTO rulesets(timestamp_utc_ms, content_hash, content_json)
                    VALUES(0, 'stub', zeroblob(1));
                ";
                ddl.ExecuteNonQuery();
            }

            // Row schema: (id, ts, action, reason, app, remote_ip)
            // action: 0=Allow, 1=Block
            // reason: 0=Unknown, 10=AllowedByMatchedRule, 103=BlockedNoMatchInNormal, 101=BlockedByModeDisabled
            var rows = new (int id, long ts, int action, int reason, string app, string ip)[]
            {
                (1, 1000, 1, 103, "chrome.exe",   "10.0.0.1"),
                (2, 2000, 1, 103, "firefox.exe",  "10.0.0.2"),
                (3, 3000, 0, 10,  "chrome.exe",   "10.0.0.3"),
                (4, 4000, 1, 103, "svchost.exe",  "1.2.3.4"),
                (5, 5000, 1, 101, "svchost.exe",  "1.2.3.5"),
                (6, 6000, 0, 10,  "firefox.exe",  "1.2.3.6"),
            };

            using var insert = conn.CreateCommand();
            insert.CommandText = @"
                INSERT INTO events_hot(
                    decision_id, flow_id, timestamp_utc_ms, action, direction, protocol,
                    local_ip, local_port, remote_ip, remote_port,
                    pid, app_path, app_name, package_sid, service_name,
                    mode_at_event, ruleset_id, reason_id, confidence,
                    matched_rule_id, near_miss_rule_ids, schema_version)
                VALUES(
                    $did, 0, $ts, $action, 1, 6,
                    '192.168.1.10', 50000, $ip, 443,
                    4000, '/test/' || $app, $app, NULL, NULL,
                    0, 1, $reason, 2,
                    NULL, NULL, 1);
            ";
            var pDid = insert.Parameters.Add("$did", SqliteType.Text);
            var pTs = insert.Parameters.Add("$ts", SqliteType.Integer);
            var pAction = insert.Parameters.Add("$action", SqliteType.Integer);
            var pIp = insert.Parameters.Add("$ip", SqliteType.Text);
            var pApp = insert.Parameters.Add("$app", SqliteType.Text);
            var pReason = insert.Parameters.Add("$reason", SqliteType.Integer);

            foreach (var r in rows)
            {
                pDid.Value = Guid.NewGuid().ToString("N");
                pTs.Value = r.ts;
                pAction.Value = r.action;
                pIp.Value = r.ip;
                pApp.Value = r.app;
                pReason.Value = r.reason;
                insert.ExecuteNonQuery();
            }
        }
    }
}
