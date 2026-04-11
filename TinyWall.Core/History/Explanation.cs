using System;
using System.Collections.Generic;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Kind of evidence chip displayed in the History UI. See
    /// Docs/EXPLAINABILITY.md section 9.
    /// </summary>
    public enum EvidenceKind
    {
        App,
        Remote,
        Protocol,
        Direction,
        Mode,
        Rule,
        Signer,
        Hash,
        Parent,
    }

    public enum ChipSeverity
    {
        Neutral = 0,
        Good = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>
    /// Kind of one-click remediation action the UI should offer for an
    /// Explanation. The concrete handling lives in the UI layer — this is
    /// only the contract the engine emits.
    /// </summary>
    public enum RemediationKind
    {
        AllowOnce,
        AllowAlways,
        BlockPermanently,
        OpenRuleEditor,
        Dismiss,
    }

    /// <summary>
    /// The primary explanation record returned by <see cref="IExplanationService"/>.
    /// See Docs/EXPLAINABILITY.md section 4.3.
    /// </summary>
    public sealed record Explanation(
        ReasonId PrimaryReason,
        string HumanTextKey,
        Confidence Confidence,
        string? MatchedRuleId,
        string? MatchedRuleDescription,
        IReadOnlyList<NearMiss> NearMisses,
        IReadOnlyList<EvidenceChip> Evidence,
        IReadOnlyList<RemediationAction> Remediations)
    {
        public static Explanation Empty(ReasonId reason, Confidence confidence) =>
            new(
                reason,
                ReasonTextKey(reason),
                confidence,
                null,
                null,
                Array.Empty<NearMiss>(),
                Array.Empty<EvidenceChip>(),
                Array.Empty<RemediationAction>());

        public static string ReasonTextKey(ReasonId r) => "ReasonId_" + r.ToString();
    }

    /// <summary>
    /// A rule that partially matched an event but didn't fully match.
    /// </summary>
    public sealed record NearMiss(
        string RuleId,
        string RuleDescription,
        string WhyItDidntMatch);

    /// <summary>
    /// One evidence chip. Clicking the chip in the UI filters the history
    /// by <see cref="FilterKey"/> = <see cref="Value"/>.
    /// </summary>
    public sealed record EvidenceChip(
        EvidenceKind Kind,
        string Label,
        string Value,
        ChipSeverity Severity = ChipSeverity.Neutral,
        string? FilterKey = null);

    /// <summary>
    /// One remediation option the UI should offer. <see cref="Parameters"/>
    /// is an opaque string bag the UI hands back to whatever dialog it
    /// opens (e.g. the rule editor).
    /// </summary>
    public sealed record RemediationAction(
        RemediationKind Kind,
        string Label,
        string? Description = null,
        IReadOnlyDictionary<string, string>? Parameters = null);
}
