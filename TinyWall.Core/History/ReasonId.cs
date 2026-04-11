namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Stable reason taxonomy for firewall decisions. Integer values are
    /// never reused after retirement — obsoleted members stay reserved.
    /// See Docs/EXPLAINABILITY.md section 3.
    /// </summary>
    public enum ReasonId
    {
        // 0 reserved for "unset" during event emission
        Unknown = 0,

        // --- Allowed reasons ---
        AllowedByMatchedRule          = 10, // an AppException matched and allowed
        AllowedByOutgoingMode         = 11, // AllowOutgoing mode default-allowed this
        AllowedByLearningMode         = 12, // learning mode auto-whitelisted
        AllowedBySpecialException     = 13, // matched a "Recommended Special" entry

        // --- Blocked reasons ---
        BlockedByModeBlockAll         = 100, // firewall is in BlockAll mode
        BlockedByModeDisabled         = 101, // firewall is "disabled" (Windows default-deny)
        BlockedByMatchedBlockRule     = 102, // matched a HardBlockPolicy exception
        BlockedNoMatchInNormal        = 103, // nothing matched in Normal mode (implicit deny)
        BlockedRestrictedPorts        = 104, // TcpUdp exception, port didn't match
        BlockedRestrictedLocalNetwork = 105, // TcpUdp exception, remote wasn't local-network
        BlockedWrongDirection         = 106, // rule existed but for opposite direction
        BlockedExpiredRule            = 107, // a temp rule expired between match and enforcement

        // --- Integrity / subject-resolution problems ---
        BlockedCompromisedSignature   = 200, // exe was signed but signature invalid
        BlockedSubjectNotResolvable   = 201, // pid dead and no app path — can't explain
    }

    /// <summary>
    /// Confidence level for a post-hoc explanation. See section 2 of
    /// Docs/EXPLAINABILITY.md for why this matters.
    /// </summary>
    public enum Confidence
    {
        Unknown = 0,
        Low     = 1, // post-hoc, subject or rules changed since decision
        Medium  = 2, // post-hoc, subject resolved but rules may have changed
        High    = 3, // post-hoc, same ruleset version, subject fully resolved
    }
}
