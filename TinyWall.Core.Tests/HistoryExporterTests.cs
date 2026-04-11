using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using pylorak.TinyWall.History;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    /// <summary>
    /// End-to-end test of the forensic export path. Seeds a temp DB with
    /// two events pointing at a single ruleset snapshot, runs the
    /// exporter, then parses the output and asserts the bundle shape.
    /// </summary>
    public class HistoryExporterTests : IDisposable
    {
        private readonly string _dbPath;

        public HistoryExporterTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(),
                "tw_historyexport_" + Guid.NewGuid().ToString("N") + ".db");
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
        public void Export_bundle_has_expected_top_level_shape()
        {
            using var reader = new HistoryReader(_dbPath);
            var rows = reader.GetRecentEvents(100, 0, null);
            Assert.Equal(2, rows.Count);

            using var ms = new MemoryStream();
            int written = HistoryExporter.Export(rows, reader, ms, toolName: "TinyWallTest", toolVersion: "0.0-test");
            Assert.Equal(2, written);

            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            var root = doc.RootElement;

            Assert.Equal(HistoryExporter.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
            Assert.NotEmpty(root.GetProperty("exportedAt").GetString()!);
            Assert.Equal("TinyWallTest", root.GetProperty("tool").GetProperty("name").GetString());
            Assert.Equal("0.0-test", root.GetProperty("tool").GetProperty("version").GetString());

            Assert.Equal(2, root.GetProperty("summary").GetProperty("eventCount").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("rulesetCount").GetInt32());
        }

        [Fact]
        public void Export_embeds_ruleset_as_nested_object()
        {
            using var reader = new HistoryReader(_dbPath);
            var rows = reader.GetRecentEvents(100, 0, null);

            using var ms = new MemoryStream();
            HistoryExporter.Export(rows, reader, ms);

            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            var rulesets = doc.RootElement.GetProperty("rulesets");

            // Keyed by string id; should contain "1"
            Assert.True(rulesets.TryGetProperty("1", out var rs));
            Assert.Equal(1, rs.GetProperty("id").GetInt64());

            // content is a nested object — parsed from the stored blob —
            // not an escaped string. We stored {"hello":"world"} in the
            // seed, so it should round-trip.
            var content = rs.GetProperty("content");
            Assert.Equal(JsonValueKind.Object, content.ValueKind);
            Assert.Equal("world", content.GetProperty("hello").GetString());
        }

        [Fact]
        public void Export_event_record_has_stored_and_explanation_blocks()
        {
            using var reader = new HistoryReader(_dbPath);
            var rows = reader.GetRecentEvents(100, 0, null);

            using var ms = new MemoryStream();
            HistoryExporter.Export(rows, reader, ms);

            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            var events = doc.RootElement.GetProperty("events");
            Assert.Equal(2, events.GetArrayLength());

            var first = events[0];
            Assert.True(first.TryGetProperty("id", out _));
            Assert.True(first.TryGetProperty("timestampUtcMs", out _));
            Assert.True(first.TryGetProperty("timestampIso", out _));
            Assert.True(first.TryGetProperty("action", out _));
            Assert.True(first.TryGetProperty("direction", out _));
            Assert.True(first.TryGetProperty("protocol", out _));
            Assert.True(first.TryGetProperty("rulesetId", out _));

            // stored block
            var stored = first.GetProperty("stored");
            Assert.True(stored.TryGetProperty("reasonId", out _));
            Assert.True(stored.TryGetProperty("reasonName", out _));
            Assert.True(stored.TryGetProperty("confidence", out _));

            // explanation block — should have error because our stub
            // ruleset content isn't a valid ServerConfiguration, so the
            // engine can't run. That's still a valid (documented) state.
            var exp = first.GetProperty("explanation");
            Assert.True(
                exp.TryGetProperty("error", out _) ||
                exp.TryGetProperty("primaryReasonId", out _),
                "explanation should either carry an error or a primaryReasonId");
        }

        [Fact]
        public void Export_writes_well_formed_json_when_events_list_is_empty()
        {
            using var reader = new HistoryReader(_dbPath);

            using var ms = new MemoryStream();
            int written = HistoryExporter.Export(Array.Empty<FirewallEventRecord>(), reader, ms);
            Assert.Equal(0, written);

            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("eventCount").GetInt32());
            Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("rulesetCount").GetInt32());
            Assert.Equal(0, doc.RootElement.GetProperty("events").GetArrayLength());
        }

        /// <summary>
        /// Seeds a fresh SQLite file with a minimal schema + 2 events and
        /// 1 ruleset. The ruleset content_json is a tiny stub object so
        /// the test can verify it round-trips as a nested JSON object
        /// rather than an escaped string.
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
                ";
                ddl.ExecuteNonQuery();
            }

            // Insert a stub ruleset whose content_json is a well-formed
            // (but minimal) JSON object. This lets us assert that the
            // exporter parses it and writes it as a nested object.
            using (var rs = conn.CreateCommand())
            {
                rs.CommandText = @"INSERT INTO rulesets(timestamp_utc_ms, content_hash, content_json) VALUES(1000, 'stub', $blob);";
                var blobParam = rs.Parameters.Add("$blob", SqliteType.Blob);
                blobParam.Value = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
                rs.ExecuteNonQuery();
            }

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
                    '192.168.1.10', 50000, '1.2.3.4', 443,
                    4000, 'C:\\test\\chrome.exe', 'chrome.exe', NULL, NULL,
                    0, 1, 103, 2,
                    NULL, NULL, 1);
            ";
            var pDid = insert.Parameters.Add("$did", SqliteType.Text);
            var pTs = insert.Parameters.Add("$ts", SqliteType.Integer);
            var pAction = insert.Parameters.Add("$action", SqliteType.Integer);

            pDid.Value = Guid.NewGuid().ToString("N");
            pTs.Value = 2000L;
            pAction.Value = 1;
            insert.ExecuteNonQuery();

            pDid.Value = Guid.NewGuid().ToString("N");
            pTs.Value = 3000L;
            pAction.Value = 1;
            insert.ExecuteNonQuery();
        }
    }
}
