using System;
using System.Collections.Generic;
using System.Globalization;
using pylorak.TinyWall.History;

namespace pylorak.TinyWall.ViewModels
{
    /// <summary>
    /// Plain POCO bag bound to <see cref="Views.StatsWindow"/>. The window
    /// rebuilds an instance from <see cref="HistoryReader"/> queries on
    /// load and on each Refresh click — no INPC needed.
    /// </summary>
    internal sealed class StatsViewModel
    {
        public string HotRowsText { get; init; } = "—";
        public string WarmRowsText { get; init; } = "—";
        public string DbSizeText { get; init; } = "—";
        public string RulesetCountText { get; init; } = "—";
        public string TimeRangeText { get; init; } = "—";

        public IReadOnlyList<StatsRowViewModel> TopApps { get; init; } = Array.Empty<StatsRowViewModel>();
        public IReadOnlyList<StatsRowViewModel> TopRemotes { get; init; } = Array.Empty<StatsRowViewModel>();
        public IReadOnlyList<StatsRowViewModel> Reasons { get; init; } = Array.Empty<StatsRowViewModel>();

        /// <summary>
        /// 24 hourly counts ordered oldest-to-newest, normalized to
        /// 0..1 of the max in the window. Used to render a sparkline
        /// of bars in pure XAML.
        /// </summary>
        public IReadOnlyList<HourBucketViewModel> Last24Hours { get; init; } = Array.Empty<HourBucketViewModel>();

        public string LoadHint { get; init; } = string.Empty;
        public bool HasLoadHint => !string.IsNullOrEmpty(LoadHint);

        public static StatsViewModel FromReader(HistoryReader reader)
        {
            var health = reader.GetStoreHealth();
            var topApps = reader.GetTopApps(10, action: null);
            var topRemotes = reader.GetTopRemoteIps(10, action: null);
            var reasons = reader.GetReasonDistribution(action: null);
            var hourly = reader.GetEventsPerHour(24, action: null);

            long maxApp = 0; foreach (var b in topApps) if (b.Count > maxApp) maxApp = b.Count;
            long maxRemote = 0; foreach (var b in topRemotes) if (b.Count > maxRemote) maxRemote = b.Count;
            long maxReason = 0; foreach (var b in reasons) if (b.Count > maxReason) maxReason = b.Count;
            long maxHour = 0; foreach (var b in hourly) if (b.Count > maxHour) maxHour = b.Count;

            return new StatsViewModel
            {
                HotRowsText = health.HotRowCount.ToString("N0", CultureInfo.CurrentCulture),
                WarmRowsText = health.WarmRowCount.ToString("N0", CultureInfo.CurrentCulture),
                DbSizeText = FormatBytes(health.DbSizeBytes),
                RulesetCountText = health.RulesetCount.ToString("N0", CultureInfo.CurrentCulture),
                TimeRangeText = FormatTimeRange(health.OldestEventUtcMs, health.NewestEventUtcMs),
                TopApps = ToRows(topApps, maxApp, b => b.Key),
                TopRemotes = ToRows(topRemotes, maxRemote, b => b.Key),
                Reasons = ToRows(reasons, maxReason, b => FormatReason(b.Key)),
                Last24Hours = ToHourBuckets(hourly, maxHour),
            };
        }

        private static IReadOnlyList<StatsRowViewModel> ToRows<T>(
            IReadOnlyList<StatsBucket<T>> buckets,
            long max,
            Func<StatsBucket<T>, string> labelOf)
        {
            var rows = new List<StatsRowViewModel>(buckets.Count);
            foreach (var b in buckets)
            {
                rows.Add(new StatsRowViewModel
                {
                    Label = labelOf(b),
                    Count = b.Count,
                    CountText = b.Count.ToString("N0", CultureInfo.CurrentCulture),
                    BarRatio = max > 0 ? (double)b.Count / max : 0.0,
                });
            }
            return rows;
        }

        private static IReadOnlyList<HourBucketViewModel> ToHourBuckets(
            IReadOnlyList<StatsBucket<long>> hourly,
            long max)
        {
            var rows = new List<HourBucketViewModel>(hourly.Count);
            foreach (var b in hourly)
            {
                var ts = DateTimeOffset.FromUnixTimeMilliseconds(b.Key).ToLocalTime();
                rows.Add(new HourBucketViewModel
                {
                    Hour = ts.ToString("HH", CultureInfo.InvariantCulture),
                    Count = b.Count,
                    BarHeight = max > 0 ? Math.Max(2.0, (b.Count / (double)max) * 60.0) : 2.0,
                    Tooltip = $"{ts:yyyy-MM-dd HH:00}  →  {b.Count:N0} events",
                });
            }
            return rows;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;
            if (bytes < KB) return $"{bytes} B";
            if (bytes < MB) return $"{bytes / KB:N1} KB";
            if (bytes < GB) return $"{bytes / MB:N2} MB";
            return $"{bytes / GB:N2} GB";
        }

        private static string FormatTimeRange(long? oldestMs, long? newestMs)
        {
            if (!oldestMs.HasValue || !newestMs.HasValue)
                return "no events";
            var oldest = DateTimeOffset.FromUnixTimeMilliseconds(oldestMs.Value).ToLocalTime();
            var newest = DateTimeOffset.FromUnixTimeMilliseconds(newestMs.Value).ToLocalTime();
            var span = newest - oldest;
            string spanText = span.TotalDays >= 1
                ? $"{span.TotalDays:N1} d"
                : span.TotalHours >= 1
                    ? $"{span.TotalHours:N1} h"
                    : $"{span.TotalMinutes:N0} m";
            return $"{oldest:yyyy-MM-dd HH:mm}  →  {newest:yyyy-MM-dd HH:mm}  ({spanText})";
        }

        private static string FormatReason(int reasonId) => ((ReasonId)reasonId) switch
        {
            ReasonId.Unknown => "Pending",
            ReasonId.AllowedByMatchedRule => "Allowed — matched rule",
            ReasonId.AllowedByOutgoingMode => "Allowed — outgoing mode",
            ReasonId.AllowedByLearningMode => "Allowed — learning",
            ReasonId.AllowedBySpecialException => "Allowed — special",
            ReasonId.BlockedByModeBlockAll => "Blocked — block-all",
            ReasonId.BlockedByModeDisabled => "Blocked — disabled",
            ReasonId.BlockedByMatchedBlockRule => "Blocked — hard block",
            ReasonId.BlockedNoMatchInNormal => "Blocked — no rule",
            ReasonId.BlockedRestrictedPorts => "Blocked — port mismatch",
            ReasonId.BlockedRestrictedLocalNetwork => "Blocked — not local net",
            ReasonId.BlockedWrongDirection => "Blocked — wrong direction",
            ReasonId.BlockedExpiredRule => "Blocked — expired",
            ReasonId.BlockedCompromisedSignature => "Blocked — bad sig",
            ReasonId.BlockedSubjectNotResolvable => "Blocked — unresolvable",
            _ => $"#{reasonId}",
        };
    }

    internal sealed class StatsRowViewModel
    {
        public string Label { get; init; } = string.Empty;
        public long Count { get; init; }
        public string CountText { get; init; } = string.Empty;

        /// <summary>0..1 — normalized fraction of the bucket's count vs the max in the set.</summary>
        public double BarRatio { get; init; }
    }

    internal sealed class HourBucketViewModel
    {
        public string Hour { get; init; } = string.Empty;
        public long Count { get; init; }
        public double BarHeight { get; init; }
        public string Tooltip { get; init; } = string.Empty;
    }
}
