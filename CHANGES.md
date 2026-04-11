# Changes from upstream TinyWall

This file satisfies GPLv3 section 5(a): *"The work must carry prominent notices stating that you modified it, and giving a relevant date."*

## Fork point

**Forked**: 2026-04-11
**Upstream source**: <https://github.com/pylorak/tinywall>
**Upstream last observed commit**: `d9efce8` (still present locally as the `master` branch)
**Upstream license**: GNU GPLv3 (preserved)
**Fork name**: PonyWall
**Fork maintainer**: ingnio

## Summary of modifications

PonyWall diverges from TinyWall in the following substantial ways. Git history (`git log`, `git blame`) is the authoritative per-file record; this list is a high-level tour for new contributors and license compliance.

### Architecture

1. **UI framework port: WinForms → Avalonia 11**. Every window was rewritten against Avalonia's XAML with compiled bindings. The WinForms references are removed entirely — `Microsoft.WindowsDesktop.App.WindowsForms` is no longer a dependency. Files touched: all of `PonyWall.Avalonia/Views/`, all of `PonyWall.Avalonia/ViewModels/`.

2. **Target framework: .NET Framework 4.x → .NET 8** (`net8.0-windows10.0.19041.0`). Updated nullable annotations, async patterns, record types throughout. C# LangVersion set to `latest`.

3. **Single-file self-contained publish** with partial trimming and feature switches (`DebuggerSupport=false`, `EventSourceSupport=false`, etc.). TrimmerRootAssembly entries preserve COM/WMI/toast reflection paths that partial trimming otherwise strips. See `publish.cmd` and the publish profile in each csproj.

4. **Service/UI split**. The firewall service is now its own binary (`PonyWallService.exe`, in `PonyWallService/`) separate from the tray UI (`PonyWall.exe`, in `PonyWall.Avalonia/`). Previously a single binary hosted both.

5. **Core extraction**. Firewall logic, rule model, WFP glue, and shared types moved into a `PonyWall.Core` class library. Used by both the service and the UI.

### Observability stack (new in PonyWall, not in upstream)

6. **SQLite event store** for firewall decision history (`PonyWall.Core/History/FirewallEventStore.cs`). Hot/warm schema, WAL mode, bounded queue with background flush thread, per-rule ruleset snapshots SHA-256 keyed and content-addressed. Design contract in `Docs/EXPLAINABILITY.md`.

7. **Explanation engine** (`PonyWall.Core/History/ExplanationService.cs`). Post-hoc replays each captured event against the ruleset snapshot that was active when the kernel made its decision, classifies it with a stable reason taxonomy (see `ReasonId.cs`), and produces evidence chips + near-miss rules. Runs on a background backfill timer (30s cadence, up to 1000 rows/pass).

8. **HistoryWindow** (`PonyWall.Avalonia/Views/HistoryWindow.axaml`). DataGrid of recent events with action/reason/time-range filters, text search, live tail auto-refresh, and a drill-down pane that re-runs the explanation engine on click to surface full evidence.

9. **Forensic JSON export** (`PonyWall.Core/History/HistoryExporter.cs`). Self-contained bundle with events + deduplicated ruleset snapshots embedded as nested JSON + recomputed explanations. Shareable with support engineers.

10. **Stats window** (`PonyWall.Avalonia/Views/StatsWindow.axaml`). Top apps / top remote addresses / reason distribution / 24h sparkline / event-store health. Built on GROUP BY queries in `HistoryReader`.

11. **First-block toast notifications** (`PonyWall.Core/History/ToastDeduper.cs`, `PonyWall.Avalonia/Services/NotificationService.cs`). Windows toast when a never-before-seen app is blocked, with Allow once / Allow always / Block always action buttons via `Microsoft.Toolkit.Uwp.Notifications`. Dedupe persisted to `%ProgramData%\PonyWall\toasted-apps.json`. Opt-out via Settings.

### Identity and branding

12. **Identity rename**. The Windows service, named pipe, ProgramData folder, WFP provider/session/sublayer names, atom name, and global mutex names were renamed from `TinyWall` to `PonyWall` so the fork can be installed side-by-side with upstream without collisions. Project folders, solution file, csproj filenames, and individual `.cs` files with `TinyWall` in their name were also renamed to `PonyWall` (with `git mv` to preserve history). Internal C# namespaces (`pylorak.TinyWall.*`) and type names (`TinyWallService`, `TinyWallServer`, `TinyWallApp`) were deliberately left unchanged — the upstream code inside retains those symbol names for git-blame continuity against `pylorak/tinywall`.

13. **Version reset**. Semantic version reset to `0.1.0` to signal the fork is pre-release and its API/config surface is not yet frozen.

### Smaller improvements

14. **Tray menu cleanup**: "Allow Local Subnet" and "Enable Hosts Blocklist" moved out of the tray popup into Settings → Machine Settings → Security, where persistent profile configuration belongs.

15. **Settings rename**: the tray menu item previously called "Manage" is now "Settings". The old label was ambiguous.

16. **Dead WinForms removal**: several hundred LOC of WinForms-era code were deleted after the Avalonia port stabilized. See commit `5e27c1a`.

17. **Logging**: Replaced the legacy `Utils.Log` with a `Microsoft.Extensions.Logging` pipeline backed by a custom file logger provider, while preserving the on-disk log format for continuity.

18. **Test coverage**: Added xUnit tests for the event store, explanation engine, history reader, history exporter, and toast deduper. 57 tests passing at the fork point.

## Per-file modification date

The authoritative per-file modification record is the git history. Use `git log --follow <path>` or `git blame <path>` to inspect. Every commit authored under the `merth` / `ingnio` identity after the fork point is a PonyWall modification.

## What was NOT changed

- The GPL-3.0 license text itself.
- Károly Pados's original copyright notices in source files that retain substantial upstream code.
- The core WFP filter management in `PonyWall.Core/PonyWallService.cs` (heavily extended for history capture, but the baseline filter installation logic is upstream).
- The application database format (`profiles.json` / the `DatabaseClasses.*` types).
- The rule model (`FirewallExceptionV3`, `ExceptionPolicy`, `ExceptionSubject` and subclasses) — intentionally preserved for config compatibility reads.
- The `TaskDialog` wrapper in `pylorak.Windows/TaskDialog` (Public Domain, originally by KevinGre).
