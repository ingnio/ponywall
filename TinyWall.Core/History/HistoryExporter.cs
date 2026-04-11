using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Writes a self-contained forensic export bundle from a set of
    /// <see cref="FirewallEventRecord"/> rows. Each bundle contains:
    ///   1. Metadata (schema version, tool version, export timestamp, counts).
    ///   2. The deduplicated ruleset snapshots that the events reference,
    ///      embedded as nested JSON objects (not opaque strings).
    ///   3. Each event as a flat record with an inline <see cref="Explanation"/>
    ///      freshly computed against its historical ruleset.
    ///
    /// The output is a single self-describing JSON document that can be
    /// shared with a support engineer or pasted into a bug report — no
    /// references to external files. See Docs/EXPLAINABILITY.md section 11
    /// (Forensic export contract).
    ///
    /// The writer is AOT- and trim-safe because it uses
    /// <see cref="Utf8JsonWriter"/> directly instead of reflection-based
    /// serialization. The one reflection-ish operation is
    /// <see cref="JsonDocument.Parse(byte[], JsonDocumentOptions)"/> on the
    /// ruleset blob, which is pure byte-level parsing and has no type
    /// dependency.
    /// </summary>
    public static class HistoryExporter
    {
        public const int SchemaVersion = 1;

        /// <summary>
        /// Writes an export bundle containing the supplied events, their
        /// associated ruleset snapshots, and fresh explanations computed
        /// against those snapshots. The caller supplies the destination
        /// stream (typically a <see cref="FileStream"/>) and a reader used
        /// to resolve ruleset blobs.
        /// </summary>
        /// <returns>The number of events successfully written.</returns>
        public static int Export(
            IReadOnlyList<FirewallEventRecord> events,
            HistoryReader reader,
            Stream destination,
            string? toolName = "TinyWall",
            string? toolVersion = null)
        {
            if (events is null) throw new ArgumentNullException(nameof(events));
            if (reader is null) throw new ArgumentNullException(nameof(reader));
            if (destination is null) throw new ArgumentNullException(nameof(destination));

            // Resolve the distinct ruleset ids used by the selection, so we
            // can embed each snapshot exactly once and keep the file size
            // under control even when exporting thousands of events.
            var rulesetIds = new HashSet<long>();
            foreach (var rec in events)
                if (rec.RulesetId > 0)
                    rulesetIds.Add(rec.RulesetId);

            // Load all distinct snapshot blobs and deserialized configs up
            // front. The deserialized configs are needed for the inline
            // explanation pass below.
            var snapshotBlobs = new Dictionary<long, byte[]>();
            var snapshotConfigs = new Dictionary<long, ServerConfiguration?>();
            foreach (var id in rulesetIds)
            {
                var blob = reader.GetRulesetSnapshotBytes(id);
                if (blob is not null)
                    snapshotBlobs[id] = blob;
                snapshotConfigs[id] = reader.GetRulesetConfig(id);
            }

            var options = new JsonWriterOptions { Indented = true };
            using var writer = new Utf8JsonWriter(destination, options);

            writer.WriteStartObject();

            // ---- Metadata ----
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("exportedAt", DateTimeOffset.UtcNow.ToString("o"));
            writer.WriteStartObject("tool");
            writer.WriteString("name", toolName ?? "TinyWall");
            if (!string.IsNullOrEmpty(toolVersion))
                writer.WriteString("version", toolVersion);
            writer.WriteEndObject();

            // ---- Summary ----
            writer.WriteStartObject("summary");
            writer.WriteNumber("eventCount", events.Count);
            writer.WriteNumber("rulesetCount", rulesetIds.Count);
            writer.WriteEndObject();

            // ---- Rulesets (deduplicated) ----
            writer.WriteStartObject("rulesets");
            foreach (var kv in snapshotBlobs)
            {
                writer.WritePropertyName(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteStartObject();
                writer.WriteNumber("id", kv.Key);
                // Parse the raw blob back into a JsonDocument so the
                // content appears as a proper nested object in the export,
                // not as an escaped string. If the blob is malformed we
                // fall back to a base64 placeholder.
                try
                {
                    using var doc = JsonDocument.Parse(kv.Value);
                    writer.WritePropertyName("content");
                    doc.RootElement.WriteTo(writer);
                }
                catch (JsonException)
                {
                    writer.WriteString("contentBase64", Convert.ToBase64String(kv.Value));
                    writer.WriteString("contentParseError", "snapshot blob was not valid JSON");
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            // ---- Events ----
            writer.WritePropertyName("events");
            writer.WriteStartArray();
            int written = 0;
            foreach (var rec in events)
            {
                WriteEvent(writer, rec, snapshotConfigs);
                written++;
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();
            return written;
        }

        private static void WriteEvent(
            Utf8JsonWriter writer,
            FirewallEventRecord rec,
            IReadOnlyDictionary<long, ServerConfiguration?> snapshotConfigs)
        {
            writer.WriteStartObject();

            writer.WriteNumber("id", rec.Id);
            writer.WriteString("decisionId", rec.DecisionId.ToString("N"));
            writer.WriteNumber("flowId", rec.FlowId);
            writer.WriteNumber("timestampUtcMs", rec.TimestampUtcMs);
            writer.WriteString("timestampIso", DateTimeOffset.FromUnixTimeMilliseconds(rec.TimestampUtcMs).ToString("o"));
            writer.WriteString("action", rec.Action.ToString());
            writer.WriteString("direction", rec.Direction.ToString());
            writer.WriteString("protocol", rec.Protocol.ToString());

            if (rec.LocalIp is not null) writer.WriteString("localIp", rec.LocalIp);
            writer.WriteNumber("localPort", rec.LocalPort);
            if (rec.RemoteIp is not null) writer.WriteString("remoteIp", rec.RemoteIp);
            writer.WriteNumber("remotePort", rec.RemotePort);

            writer.WriteNumber("pid", rec.Pid);
            if (rec.AppPath is not null) writer.WriteString("appPath", rec.AppPath);
            if (rec.AppName is not null) writer.WriteString("appName", rec.AppName);
            if (rec.PackageSid is not null) writer.WriteString("packageSid", rec.PackageSid);
            if (rec.ServiceName is not null) writer.WriteString("serviceName", rec.ServiceName);

            writer.WriteString("modeAtEvent", rec.ModeAtEvent.ToString());
            writer.WriteNumber("rulesetId", rec.RulesetId);
            writer.WriteNumber("schemaVersion", rec.SchemaVersion);

            // Stored (at capture time) reason/confidence. May differ from
            // the freshly-computed explanation below if the taxonomy or
            // engine evolved between capture and export.
            writer.WriteStartObject("stored");
            writer.WriteNumber("reasonId", (int)rec.ReasonId);
            writer.WriteString("reasonName", rec.ReasonId.ToString());
            writer.WriteString("confidence", rec.Confidence.ToString());
            if (rec.MatchedRuleId is not null) writer.WriteString("matchedRuleId", rec.MatchedRuleId);
            if (rec.NearMissRuleIds is not null) writer.WriteString("nearMissRuleIds", rec.NearMissRuleIds);
            writer.WriteEndObject();

            // Recomputed explanation against the stored ruleset snapshot.
            writer.WritePropertyName("explanation");
            if (snapshotConfigs.TryGetValue(rec.RulesetId, out var config) && config is not null)
            {
                var exp = ExplanationService.ExplainAgainst(rec, config, rec.ModeAtEvent);
                WriteExplanation(writer, exp);
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString("error", "ruleset snapshot not available");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        private static void WriteExplanation(Utf8JsonWriter writer, Explanation exp)
        {
            writer.WriteStartObject();
            writer.WriteNumber("primaryReasonId", (int)exp.PrimaryReason);
            writer.WriteString("primaryReasonName", exp.PrimaryReason.ToString());
            writer.WriteString("confidence", exp.Confidence.ToString());

            if (exp.MatchedRuleId is not null) writer.WriteString("matchedRuleId", exp.MatchedRuleId);
            if (exp.MatchedRuleDescription is not null) writer.WriteString("matchedRuleDescription", exp.MatchedRuleDescription);

            writer.WritePropertyName("evidence");
            writer.WriteStartArray();
            foreach (var chip in exp.Evidence)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", chip.Kind.ToString());
                writer.WriteString("label", chip.Label);
                writer.WriteString("value", chip.Value);
                writer.WriteString("severity", chip.Severity.ToString());
                if (chip.FilterKey is not null) writer.WriteString("filterKey", chip.FilterKey);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("nearMisses");
            writer.WriteStartArray();
            foreach (var miss in exp.NearMisses)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", miss.RuleId);
                writer.WriteString("ruleDescription", miss.RuleDescription);
                writer.WriteString("whyItDidntMatch", miss.WhyItDidntMatch);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("remediations");
            writer.WriteStartArray();
            foreach (var rem in exp.Remediations)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", rem.Kind.ToString());
                writer.WriteString("label", rem.Label);
                if (rem.Description is not null) writer.WriteString("description", rem.Description);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
    }
}
