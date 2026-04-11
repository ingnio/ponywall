using System;
using System.Globalization;
using pylorak.TinyWall.History;

namespace pylorak.TinyWall.ViewModels
{
    /// <summary>
    /// Read-only display projection of a single <see cref="FirewallEventRecord"/>.
    /// Kept as a flat POCO (no <c>INotifyPropertyChanged</c>) to match the
    /// existing ConnectionsWindow pattern — the DataGrid is re-bound wholesale
    /// on each refresh.
    /// </summary>
    internal sealed class HistoryRowViewModel
    {
        public long Id { get; init; }
        public string Timestamp { get; init; } = string.Empty;
        public string ActionLabel { get; init; } = string.Empty;
        public string App { get; init; } = string.Empty;
        public string AppPath { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
        public string Protocol { get; init; } = string.Empty;
        public string Remote { get; init; } = string.Empty;
        public string LocalPort { get; init; } = string.Empty;
        public string ReasonLabel { get; init; } = string.Empty;
        public string ConfidenceLabel { get; init; } = string.Empty;
        public string NearMissCount { get; init; } = string.Empty;
        public string MatchedRule { get; init; } = string.Empty;

        public static HistoryRowViewModel FromRecord(FirewallEventRecord rec)
        {
            var ts = DateTimeOffset.FromUnixTimeMilliseconds(rec.TimestampUtcMs).ToLocalTime();
            return new HistoryRowViewModel
            {
                Id = rec.Id,
                Timestamp = ts.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ActionLabel = rec.Action == EventAction.Allow ? "Allow" : "Block",
                App = rec.AppName ?? "(unknown)",
                AppPath = rec.AppPath ?? string.Empty,
                Direction = rec.Direction switch
                {
                    RuleDirection.In => "In",
                    RuleDirection.Out => "Out",
                    RuleDirection.InOut => "In/Out",
                    _ => "?",
                },
                Protocol = rec.Protocol.ToString(),
                Remote = FormatEndpoint(rec.RemoteIp, rec.RemotePort),
                LocalPort = rec.LocalPort > 0 ? rec.LocalPort.ToString(CultureInfo.InvariantCulture) : string.Empty,
                ReasonLabel = FormatReason(rec.ReasonId),
                ConfidenceLabel = rec.Confidence switch
                {
                    Confidence.Low => "Low",
                    Confidence.Medium => "Medium",
                    Confidence.High => "High",
                    _ => "—",
                },
                NearMissCount = CountNearMisses(rec.NearMissRuleIds),
                MatchedRule = rec.MatchedRuleId ?? string.Empty,
            };
        }

        private static string FormatEndpoint(string? ip, int port)
        {
            if (string.IsNullOrEmpty(ip))
                return port > 0 ? $":{port}" : string.Empty;
            return port > 0 ? $"{ip}:{port}" : ip;
        }

        private static string FormatReason(ReasonId id) => id switch
        {
            ReasonId.Unknown => "Pending",
            ReasonId.AllowedByMatchedRule => "Allowed — matched rule",
            ReasonId.AllowedByOutgoingMode => "Allowed — outgoing mode",
            ReasonId.AllowedByLearningMode => "Allowed — learning",
            ReasonId.AllowedBySpecialException => "Allowed — special",
            ReasonId.BlockedByModeBlockAll => "Blocked — block-all mode",
            ReasonId.BlockedByModeDisabled => "Blocked — firewall disabled",
            ReasonId.BlockedByMatchedBlockRule => "Blocked — hard-block rule",
            ReasonId.BlockedNoMatchInNormal => "Blocked — no rule",
            ReasonId.BlockedRestrictedPorts => "Blocked — port mismatch",
            ReasonId.BlockedRestrictedLocalNetwork => "Blocked — not local net",
            ReasonId.BlockedWrongDirection => "Blocked — wrong direction",
            ReasonId.BlockedExpiredRule => "Blocked — rule expired",
            ReasonId.BlockedCompromisedSignature => "Blocked — bad signature",
            ReasonId.BlockedSubjectNotResolvable => "Blocked — unresolvable",
            _ => $"#{(int)id}",
        };

        private static string CountNearMisses(string? nearMissCsv)
        {
            if (string.IsNullOrWhiteSpace(nearMissCsv))
                return string.Empty;
            int n = 1;
            foreach (char c in nearMissCsv)
                if (c == ',') n++;
            return n.ToString(CultureInfo.InvariantCulture);
        }
    }
}
