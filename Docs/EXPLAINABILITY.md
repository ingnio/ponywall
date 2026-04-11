# TinyWall Explainability and Forensic History

This document is the design contract for the forensic event store, the
explanation engine, and the History UI. It defines the data shapes, the
reason taxonomy, the threat model, and the retention policy *before* any
code is written, so later commits can be judged against it rather than
against a moving target.

**Status:** Design contract. Not yet implemented.

---

## 1. What this must answer

Any feature added under this design has to help the user answer one of the
following five questions in the History UI within 1 second, or in the
Explain API within 100 ms:

1. **What was blocked (or allowed)?** Process, remote endpoint, protocol,
   direction, timestamp.
2. **Why?** Which rule matched, or — if none matched — what the default
   policy was, in plain language.
3. **What evidence existed at the time of the decision?** Executable path,
   signer, hash, pid, parent process (best-effort), package SID (for UWP),
   service mapping, firewall mode.
4. **Could a different rule have allowed it?** "Near-miss" rules — the
   top 3 rules that partially matched and why they didn't fully match.
5. **What should the user do next?** One-click remediation: add an allow
   rule for this app, block permanently, dismiss.

Any proposed feature that doesn't map onto one of these five questions is
out of scope for this contract. If you find yourself writing code that
doesn't answer one of them, reopen this doc.

## 2. Threat model and architectural limits

TinyWall does **not** own the policy engine. Allow/deny decisions happen
inside the Windows Filtering Platform (WFP) kernel driver, which is a
black box to the user-space service. TinyWall *installs* WFP filters
compiled from the active rule set and *reads back* the kernel's audit
events via the Windows Event Log (`Security` channel, EventIDs 5152–5159).

What this means for explainability:

- **We cannot observe "top N evaluated rule candidates with scores"** at
  decision time. WFP does not expose its evaluation trace. The existing
  `FirewallLogWatcher` reads the kernel's post-decision summary.
- **Everything in this document is post-hoc explanation.** We take an
  event the kernel already decided, and run the same rule set in user
  space to reconstruct the most likely reason.
- Post-hoc explanation is **inherently fuzzy**. By the time we explain:
  - The process may have exited (pid unresolvable).
  - The executable file may have been deleted or updated (signer/hash
    different from decision time).
  - The UWP package may have been uninstalled.
  - The rule set may have been edited (matched rule ID may no longer exist).
- **We flag this fuzziness** via an explicit `Confidence` field on every
  `Explanation`. Low-confidence explanations are marked visually in the UI.

**Privacy threat model:** the only adversary we design against for the
history store is *another local user* on the same machine. An attacker
with admin is already past the point where a firewall audit log could
help — they can uninstall TinyWall, rewrite its config, or stop the
service. We do not attempt tamper-evident storage (rolling hash chains,
signed segments). We rely on file ACLs (`BUILTIN\Administrators` only) on
the history DB.

**Network threat model:** TinyWall is a privacy tool. The explainability
subsystem **must not make any outbound network request**. No telemetry
egress, no reputation lookups, no DNS-based enrichment at capture time.
Enrichment that requires a network (e.g. a user clicking "look up this
IP on AbuseIPDB") is always an explicit, per-click user action, never
automatic.

## 3. Reason taxonomy

Every stored event carries a `reason_id` from a stable enum. Stable means
**integer values are never reused** after a reason is retired. When a
reason is obsoleted, its integer stays reserved and the enum member is
kept with an `[Obsolete]` attribute.

```csharp
namespace pylorak.TinyWall.History
{
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

    public enum Confidence
    {
        Unknown = 0,
        Low     = 1, // post-hoc, subject or rules changed since decision
        Medium  = 2, // post-hoc, subject resolved but rules may have changed
        High    = 3, // post-hoc, same ruleset version, subject fully resolved
    }
}
```

Rules for adding a new `ReasonId`:

1. Always append to the end of its category range. Do not reuse integers.
2. Add a localized string for the reason in `Resources/Messages.resx`
   under the key `ReasonId_<Name>` so the UI can display it.
3. Add a `RemediationHint` entry in `ExplanationService` for the new reason.
4. Update this document's enum listing in the same commit.

## 4. Data shapes

### 4.1 Event record (captured for every firewall log entry)

```csharp
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
```

### 4.2 Ruleset snapshot

```csharp
public sealed record RulesetSnapshot
{
    public long Id;                       // SQLite auto-increment
    public long TimestampUtcMs;           // when this version became active
    public string ContentHash;            // SHA-256 of canonical JSON
    public byte[] ContentJson;            // the ServerConfiguration at that time
}
```

A new row is written to `rulesets` every time `SetServerConfig` is called
with a config whose canonical hash differs from the current one. Events
captured between two config changes all reference the same ruleset_id.
Hash-dedup means no-op saves don't grow the table.

### 4.3 Explanation (returned by the Explain API)

```csharp
public sealed record Explanation(
    ReasonId PrimaryReason,
    string HumanText,                     // localized
    Confidence Confidence,
    string? MatchedRuleId,
    string? MatchedRuleDescription,       // human label for display
    IReadOnlyList<NearMiss> NearMisses,
    IReadOnlyList<EvidenceChip> Evidence,
    IReadOnlyList<RemediationAction> Remediations);

public sealed record NearMiss(
    string RuleId,
    string RuleDescription,
    string WhyItDidntMatch);              // localized

public sealed record EvidenceChip(
    EvidenceKind Kind,                    // App, Remote, Protocol, Mode, Rule, Signer, Hash, Parent
    string Label,                         // display text
    string Value,                         // canonical value, used for filter clicks
    ChipSeverity Severity);               // Neutral | Good | Warning | Error

public sealed record RemediationAction(
    string Id,                            // "allow-once" | "allow-always" | "block-permanent" | "dismiss"
    string Label,                         // localized
    string Description);                  // localized tooltip
```

## 5. Storage model

### 5.1 Location and access control

- File path: `%ProgramData%\TinyWall\history.db`
- DACL: `BUILTIN\Administrators` full control only. No user, no service,
  no world access. The service process already runs as `LocalSystem`
  which has SYSTEM-level access.
- The user-space UI opens the DB in read-only mode when displaying
  history. Only the service process writes.

### 5.2 Hot and warm tiers

Two tables with different retention and detail levels:

**`events_hot`** — last 72 hours, full detail.

Columns: every field on `FirewallEventRecord`.
Indexes: `(timestamp_utc_ms DESC, action)`, `(app_path, timestamp_utc_ms DESC)`, `(decision_id)`.

**`events_warm`** — older than 72 hours, compact summary.

Columns: `id, timestamp_utc_ms, action, reason_id, app_name, remote_ip,
remote_port, protocol, ruleset_id`. No pid, no local endpoint, no hash,
no near-miss data. This is for trend analysis, not incident investigation.
Indexes: `(timestamp_utc_ms DESC)`, `(app_name, timestamp_utc_ms DESC)`.

**Migration job:** runs once per hour in the service. Moves rows older
than 72h from `events_hot` to `events_warm`, dropping the fields that
don't exist in warm. Deletes from hot after successful warm insert.

**Retention caps:**
- `events_hot`: max 100k rows (≈ ~30 MB with indexes), or 72h by age.
  Whichever is smaller.
- `events_warm`: max 1M rows (≈ ~80 MB), or 90 days by age. Whichever is
  smaller.
- Both caps user-configurable via Settings → History.
- Retention job runs every 5 minutes. Deletes oldest rows beyond the cap.

**Write batching:** the service writes in batches. Events accumulate in
an in-memory queue; flushed on timer every 2 seconds or when queue
exceeds 500 items. Uses `PRAGMA journal_mode=WAL` for append throughput.

**Backpressure:** the queue has a max size (5000 items). If the queue is
full, new events increment `events_dropped_counter` (separate one-row
table) and are dropped. The counter is displayed in the History UI as a
health indicator. Target: `events_dropped = 0` under normal load.

### 5.3 Flow correlation

`flow_id` is computed as `FNV-1a(5-tuple) XOR (timestamp_bucket / 10s)`.
This groups events in the same flow (same 5-tuple, same 10-second window)
under one ID, so the UI can show "chrome.exe tried 127 times" as a single
row with a count, instead of 127 separate rows.

The History UI defaults to collapsed-by-flow view. An expanded view shows
each individual attempt.

## 6. The Explain API

A single interface in `TinyWall.Core.History`:

```csharp
public interface IExplanationService
{
    /// <summary>
    /// Explain a specific captured event by its DecisionId.
    /// Returns null if the event is not found.
    /// </summary>
    Explanation? Explain(Guid decisionId);

    /// <summary>
    /// Explain a flow by its fingerprint (5-tuple + app). Used for
    /// "what would happen if I tried this right now?" simulation.
    /// </summary>
    Explanation ExplainFlow(FlowFingerprint flow);
}
```

**Implementation outline:**

```
Explain(decisionId):
  record = eventStore.GetByDecisionId(decisionId)
  if record is null: return null
  ruleset = rulesetStore.GetById(record.RulesetId)  // historical ruleset
  subject = ResolveSubject(record)                    // best-effort
  return RunExplanation(record, ruleset, subject, isHistorical: true)

RunExplanation(record, ruleset, subject, isHistorical):
  confidence = DetermineConfidence(record, ruleset, subject, isHistorical)

  # Mode-level fast paths
  if record.ModeAtEvent == BlockAll and record.Action == Block:
    return Explanation(BlockedByModeBlockAll, ..., confidence)
  if record.ModeAtEvent == Disabled and record.Action == Block:
    return Explanation(BlockedByModeDisabled, ..., confidence)

  # Rule-level match against the historical ruleset
  matches = ruleset.ActiveProfile.AppExceptions
    .Where(ex => ex.Subject.Matches(subject))
    .ToList()

  if matches.Count == 0:
    nearMisses = FindNearMisses(ruleset, subject, record)
    return Explanation(BlockedNoMatchInNormal, ..., nearMisses, confidence)

  # ... run the full match logic, return the appropriate ReasonId
```

`FindNearMisses` walks the rule set looking for exceptions that partially
match. For each, it computes a "why it didn't fully match" string:

- "Subject matches but protocol was UDP, rule is TCP only"
- "Subject matches but remote port 443 not in allowed list [80, 8080]"
- "Subject name matches but path differs (rule expects `C:\A.exe`, event
  was `C:\B.exe`)"
- "Rule is for inbound, this was outbound"

Top 3 near-misses (by match strength) are returned. If there are fewer
than 3, fewer are returned.

**Backfill:** when `ExplanationService` is upgraded (new `ReasonId`,
better near-miss logic), a background job re-runs `Explain` over existing
rows whose `reason_id = Unknown`, or optionally over all rows, and
updates them in place.

## 7. Schema versioning

`events_hot` and `events_warm` both carry a `schema_version INTEGER NOT
NULL` column. The current version is a constant in `EventStoreSchema.cs`.

On startup, the event store compares the stored version to the code
version. If they differ, a migration runs. Migrations are numbered and
cumulative; each migration is a single `.sql` script named
`Migrations/0001_initial.sql`, `0002_add_service_name.sql`, etc.

Rule: **never break existing columns**. Only add new columns, or mark old
columns deprecated. Old code reading new data must degrade gracefully by
ignoring unknown columns.

## 8. Privacy controls in the UI

Settings → History tab will have:

- [checkbox] **Enable firewall event history** — default on. When off,
  `FirewallEventStore` drops all writes and the DB file is not created.
- [slider] **Keep events for up to** `[24h / 7d / 30d / 90d / unlimited]`
- [slider] **Maximum storage** `[50 / 100 / 500 / 1000] MB`
- [button] **Clear history now** — deletes both tables and reclaims disk.
- [label] **Current database size:** *(live value)*
- [label] **Events dropped:** *(from the backpressure counter)*

The button must require confirmation ("Are you sure? This cannot be undone.").

## 9. Evidence chip kinds

The UI renders events as a row of chips. Each chip is clickable and
filters the history grid by that value.

| Kind     | Example label                  | Value used for filter          |
|----------|--------------------------------|--------------------------------|
| App      | `chrome.exe`                   | `app_path = C:\Program Files\...`|
| Remote   | `1.2.3.4:443`                  | `remote_ip = 1.2.3.4`          |
| Protocol | `TCP`                          | `protocol = TCP`               |
| Mode     | `Normal mode`                  | `mode_at_event = Normal`       |
| Rule     | `matched: Chrome (TCP allow)`  | `matched_rule_id = {guid}`     |
| Signer   | `Google LLC ✓`                 | `signer = Google LLC`          |
| Hash     | `SHA256: a1b2…`                | `hash = ...`                   |
| Parent   | `explorer.exe (8976)`          | `parent_pid = 8976`            |

Signer and Hash chips are "best effort" — computed by `ResolveSubject`
at explain time, not stored in the event. If the file no longer exists,
the chip is rendered as `(unavailable)` and marked Warning severity.

## 10. Success metrics (self-measured)

TinyWall doesn't phone home, so these are metrics the user can see in
the History UI's "Health" section, measured continuously:

| Metric | Target | Displayed as |
|--------|--------|--------------|
| `% blocked events with PrimaryReason != Unknown` | > 99% | "Explanation coverage: 99.8%" |
| `median Explain() latency`                       | < 100 ms | "Explain latency: 42 ms" |
| `median history page load (100 rows)`            | < 300 ms | (implicit — no slider lag) |
| `events_dropped_counter / day`                   | 0 | "Events dropped: 0" |
| `history db size`                                | < user cap | "Database: 42 MB of 100 MB cap" |

If explanation coverage drops below 99%, the `Unknown` events should be
surfaced in a "Review" view so the developer can improve the engine.

## 11. What we're not building

Explicit non-goals so future PRs don't scope-creep:

- **Kernel-level decision traces.** Can't. WFP doesn't expose them.
- **Outbound reputation lookups.** Conflicts with privacy model.
- **Automatic DNS reverse lookups.** Too much overhead, answers stale
  by the time the user reads them. Manual per-event only, explicit click.
- **Tamper-evident hash chains.** Wrong threat model (admin attacker
  defeats them trivially).
- **Full packet capture.** No driver for it, major scope.
- **Cross-machine history sync.** No network egress.
- **Parent process tree beyond one level.** ETW correlation is a rabbit
  hole, one-level parent via `GetProcessSnapshot` is the limit.
- **Anomaly detection** ("this app has never contacted this IP before").
  Interesting but a stretch goal after Phase 4 has run for a week.

## 12. Phase ordering and acceptance criteria

| Phase | Scope | Acceptance |
|-------|-------|------------|
| 0 | This document | Design contract reviewed. No code. |
| 1 | `FirewallEventStore` + hot/warm SQLite + `FirewallLogWatcher` hook + `rulesets` snapshots | Events appear in `events_hot` within 2 s of firewall decision. 24 hours of dogfooding with no queue drops. |
| 2 | `ExplanationService` with mode fast-paths, rule match, near-miss | 99%+ of captured events get `PrimaryReason != Unknown`. Unit tests cover each `ReasonId`. |
| 3 | `HistoryWindow` with DataGrid, chips, detail panel, remediation buttons | User can click a block, see the reason and chips, and add a rule in under 10 s. |
| 4 | Stats tab, forensic export (JSON with ruleset snapshots) | Export bundle round-trips through `ExplainFlow` — re-running explanation on exported data produces the same result. |
| 5 | First-block toasts with action buttons | Toast fires for a new (app, remote) tuple once per 24h, with reason text and two action buttons. Noise audit: < 5 toasts/day on a typical machine. |

Each phase lands in a single commit; the commit message references this
document and lists the specific acceptance criteria it meets.

## 13. File locations

| Thing | Path |
|-------|------|
| Event store implementation | `TinyWall.Core/History/FirewallEventStore.cs` |
| SQLite schema + migrations | `TinyWall.Core/History/Migrations/` |
| `IExplanationService` interface | `TinyWall.Core/History/IExplanationService.cs` |
| `ExplanationService` implementation | `TinyWall.Core/History/ExplanationService.cs` |
| Reason enum + Confidence enum | `TinyWall.Core/History/ReasonId.cs` |
| Explanation records | `TinyWall.Core/History/Explanation.cs` |
| History DB file | `%ProgramData%\TinyWall\history.db` |
| History UI window | `TinyWall.Avalonia/Views/HistoryWindow.axaml` |
| History viewmodel | `TinyWall.Avalonia/ViewModels/HistoryViewModel.cs` |
| This document | `Docs/EXPLAINABILITY.md` |

## 14. Revision log

| Date | Change |
|------|--------|
| 2026-04-10 | Initial contract draft (Phase 0). |
