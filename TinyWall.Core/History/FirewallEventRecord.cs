using System;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// The full-detail event record stored in events_hot. See
    /// Docs/EXPLAINABILITY.md section 4.1.
    /// </summary>
    public sealed record FirewallEventRecord
    {
        public long Id;                       // SQLite auto-increment
        public Guid DecisionId;               // synthetic, for Explain API lookups
        public long FlowId;                   // hash of (5-tuple + 10s time bucket)
        public long TimestampUtcMs;           // Unix epoch milliseconds
        public EventAction Action;            // Allow | Block
        public RuleDirection Direction;       // In | Out
        public Protocol Protocol;             // TCP | UDP | ICMP | ...

        public string? LocalIp;
        public int LocalPort;
        public string? RemoteIp;
        public int RemotePort;

        public uint Pid;
        public string? AppPath;               // may be null if kernel event
        public string? AppName;               // filename only
        public string? PackageSid;            // for UWP
        public string? ServiceName;           // if pid hosts a Windows service
        public FirewallMode ModeAtEvent;      // mode at the time of capture

        public long RulesetId;                // FK into rulesets table
        public ReasonId ReasonId;             // set by ExplanationService
        public Confidence Confidence;
        public string? MatchedRuleId;         // FirewallExceptionV3.Id if matched
        public string? NearMissRuleIds;       // comma-joined, max 3

        public int SchemaVersion;             // see section 7
    }
}
