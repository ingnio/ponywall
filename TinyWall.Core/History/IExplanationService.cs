using System.Threading;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Post-hoc explanation engine. See Docs/EXPLAINABILITY.md section 6.
    /// Everything is post-hoc — we replay the same rule set that was
    /// active when the kernel made its decision, and reconstruct the most
    /// likely reason.
    /// </summary>
    public interface IExplanationService
    {
        /// <summary>
        /// Explain a specific captured event by its SQLite row id.
        /// Returns an Explanation with <see cref="ReasonId.Unknown"/> +
        /// <see cref="Confidence.Low"/> if the event is missing, the
        /// snapshot is missing, or the snapshot cannot be decoded.
        /// </summary>
        Explanation Explain(long decisionId);

        /// <summary>
        /// Explain a flow by its fingerprint. This is the "what would
        /// happen if I tried this right now?" simulation.
        /// </summary>
        Explanation ExplainFlow(FlowFingerprint flow);

        /// <summary>
        /// Runs one backfill pass over events_hot rows whose
        /// reason_id = Unknown. Bounded per call so the writer thread
        /// does not starve. Called periodically from the service.
        /// </summary>
        void Backfill(CancellationToken cancel);
    }
}
