using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pylorak.TinyWall.History;

namespace pylorak.TinyWall.ViewModels
{
    /// <summary>
    /// Display projection for the drill-down pane in HistoryWindow. Holds
    /// the full <see cref="FirewallEventRecord"/> plus a freshly-computed
    /// <see cref="Explanation"/> against the event's historical ruleset
    /// snapshot. Kept as a plain POCO — the window rebinds DataContext
    /// on each selection change, so no INPC is needed.
    /// </summary>
    internal sealed class HistoryDetailsViewModel
    {
        // ---- Header ----
        public string Timestamp { get; init; } = string.Empty;
        public string ActionLabel { get; init; } = string.Empty;
        public bool IsBlock { get; init; }

        // ---- Subject ----
        public string AppName { get; init; } = string.Empty;
        public string AppPath { get; init; } = string.Empty;
        public string Pid { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public bool HasServiceName => !string.IsNullOrEmpty(ServiceName);

        // ---- Flow ----
        public string Protocol { get; init; } = string.Empty;
        public string DirectionLabel { get; init; } = string.Empty;
        public string LocalEndpoint { get; init; } = string.Empty;
        public string RemoteEndpoint { get; init; } = string.Empty;

        // ---- Decision context ----
        public string Mode { get; init; } = string.Empty;
        public string RulesetIdText { get; init; } = string.Empty;
        public string ReasonText { get; init; } = string.Empty;
        public string ConfidenceText { get; init; } = string.Empty;
        public string MatchedRuleName { get; init; } = string.Empty;
        public bool HasMatchedRule => !string.IsNullOrEmpty(MatchedRuleName);

        // ---- Explanation payload ----
        public IReadOnlyList<EvidenceChipViewModel> Evidence { get; init; } = Array.Empty<EvidenceChipViewModel>();
        public bool HasEvidence => Evidence.Count > 0;

        public IReadOnlyList<NearMissViewModel> NearMisses { get; init; } = Array.Empty<NearMissViewModel>();
        public bool HasNearMisses => NearMisses.Count > 0;

        public string StatusHint { get; init; } = string.Empty;
        public bool HasStatusHint => !string.IsNullOrEmpty(StatusHint);

        /// <summary>
        /// Builds a details VM from a raw record and (optionally) a
        /// fresh Explanation computed against the historical ruleset.
        /// When <paramref name="explanation"/> is null the VM still
        /// renders — the evidence/near-miss sections collapse and a
        /// status hint explains why.
        /// </summary>
        public static HistoryDetailsViewModel FromRecord(FirewallEventRecord rec, Explanation? explanation, string? statusHint)
        {
            var ts = DateTimeOffset.FromUnixTimeMilliseconds(rec.TimestampUtcMs).ToLocalTime();

            var chips = explanation?.Evidence
                .Select(EvidenceChipViewModel.From)
                .ToList()
                ?? new List<EvidenceChipViewModel>();

            var nearMisses = explanation?.NearMisses
                .Select(NearMissViewModel.From)
                .ToList()
                ?? new List<NearMissViewModel>();

            string matchedRule = explanation?.MatchedRuleDescription
                ?? (rec.MatchedRuleId ?? string.Empty);

            return new HistoryDetailsViewModel
            {
                Timestamp = ts.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                ActionLabel = rec.Action == EventAction.Allow ? "ALLOW" : "BLOCK",
                IsBlock = rec.Action == EventAction.Block,
                AppName = rec.AppName ?? "(unknown)",
                AppPath = rec.AppPath ?? "(unknown path)",
                Pid = rec.Pid > 0 ? rec.Pid.ToString(CultureInfo.InvariantCulture) : string.Empty,
                ServiceName = rec.ServiceName ?? string.Empty,
                Protocol = rec.Protocol.ToString(),
                DirectionLabel = rec.Direction switch
                {
                    RuleDirection.In => "Inbound",
                    RuleDirection.Out => "Outbound",
                    RuleDirection.InOut => "In/Out",
                    _ => "?",
                },
                LocalEndpoint = FormatEndpoint(rec.LocalIp, rec.LocalPort),
                RemoteEndpoint = FormatEndpoint(rec.RemoteIp, rec.RemotePort),
                Mode = rec.ModeAtEvent.ToString(),
                RulesetIdText = "#" + rec.RulesetId.ToString(CultureInfo.InvariantCulture),
                ReasonText = FormatReason(explanation?.PrimaryReason ?? rec.ReasonId),
                ConfidenceText = FormatConfidence(explanation?.Confidence ?? rec.Confidence),
                MatchedRuleName = matchedRule,
                Evidence = chips,
                NearMisses = nearMisses,
                StatusHint = statusHint ?? string.Empty,
            };
        }

        private static string FormatEndpoint(string? ip, int port)
        {
            if (string.IsNullOrEmpty(ip))
                return port > 0 ? $":{port}" : "—";
            return port > 0 ? $"{ip}:{port}" : ip;
        }

        private static string FormatReason(ReasonId id) => id switch
        {
            ReasonId.Unknown => "Pending explanation",
            ReasonId.AllowedByMatchedRule => "Allowed — matched exception rule",
            ReasonId.AllowedByOutgoingMode => "Allowed — Outgoing mode default-allow",
            ReasonId.AllowedByLearningMode => "Allowed — Learning mode auto-whitelist",
            ReasonId.AllowedBySpecialException => "Allowed — matched Special exception",
            ReasonId.BlockedByModeBlockAll => "Blocked — firewall is in Block-All mode",
            ReasonId.BlockedByModeDisabled => "Blocked — firewall disabled (Windows default deny)",
            ReasonId.BlockedByMatchedBlockRule => "Blocked — matched a hard-block rule",
            ReasonId.BlockedNoMatchInNormal => "Blocked — no exception matched in Normal mode (implicit deny)",
            ReasonId.BlockedRestrictedPorts => "Blocked — app matched but port was not allowed",
            ReasonId.BlockedRestrictedLocalNetwork => "Blocked — app matched but remote is not on local network",
            ReasonId.BlockedWrongDirection => "Blocked — app matched but rule is for the opposite direction",
            ReasonId.BlockedExpiredRule => "Blocked — temporary rule expired before enforcement",
            ReasonId.BlockedCompromisedSignature => "Blocked — signed binary but signature is invalid",
            ReasonId.BlockedSubjectNotResolvable => "Blocked — subject could not be resolved",
            _ => $"Reason #{(int)id}",
        };

        private static string FormatConfidence(Confidence c) => c switch
        {
            Confidence.Low => "Low",
            Confidence.Medium => "Medium",
            Confidence.High => "High",
            _ => "Unknown",
        };
    }

    /// <summary>Single evidence chip row bound in the drill-down pane.</summary>
    internal sealed class EvidenceChipViewModel
    {
        public string Label { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public string Severity { get; init; } = "Neutral";

        public static EvidenceChipViewModel From(EvidenceChip chip) => new()
        {
            Label = chip.Kind.ToString(),
            Value = chip.Label,
            Severity = chip.Severity.ToString(),
        };
    }

    /// <summary>Single near-miss rule row bound in the drill-down pane.</summary>
    internal sealed class NearMissViewModel
    {
        public string RuleName { get; init; } = string.Empty;
        public string Divergence { get; init; } = string.Empty;

        public static NearMissViewModel From(NearMiss miss) => new()
        {
            RuleName = miss.RuleDescription,
            Divergence = miss.WhyItDidntMatch,
        };
    }
}
