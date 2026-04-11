using System;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Compact, comparable identity for a single flow. Used by the
    /// <see cref="IExplanationService.ExplainFlow"/> "what would happen
    /// if I tried this right now" API, and for deduping events in the
    /// history UI. See Docs/EXPLAINABILITY.md section 4.1 / 5.3.
    /// </summary>
    public readonly record struct FlowFingerprint(
        RuleDirection Direction,
        Protocol Protocol,
        string? LocalIp,
        int LocalPort,
        string? RemoteIp,
        int RemotePort,
        string? AppPath)
    {
        /// <summary>Builds a fingerprint from a captured event record.</summary>
        public static FlowFingerprint FromRecord(FirewallEventRecord r) =>
            new(r.Direction, r.Protocol, r.LocalIp, r.LocalPort, r.RemoteIp, r.RemotePort, r.AppPath);

        /// <summary>
        /// 64-bit hash of the 5-tuple + app path. Stable across processes
        /// so events with the same fingerprint bucket into the same flow.
        /// </summary>
        public long ToStableHash()
        {
            const ulong FnvOffset = 14695981039346656037UL;
            const ulong FnvPrime = 1099511628211UL;

            ulong hash = FnvOffset;
            hash = Mix(hash, (int)Direction);
            hash = Mix(hash, (int)Protocol);
            hash = Mix(hash, LocalIp);
            hash = Mix(hash, LocalPort);
            hash = Mix(hash, RemoteIp);
            hash = Mix(hash, RemotePort);
            hash = Mix(hash, AppPath);
            return unchecked((long)hash);

            static ulong Mix(ulong acc, object? value)
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
    }
}
