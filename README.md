<br />
<div align="center">
  <h3 align="center">PonyWall</h3>

  <p align="center">
    A lightweight Windows firewall with forensic-grade observability.
    <br />
    <em>Fork of TinyWall · Avalonia UI · .NET 8</em>
  </p>
</div>

## About this repository

**PonyWall is a fork of [TinyWall](https://github.com/pylorak/tinywall)** by Károly Pados (<https://tinywall.pados.hu>), modified extensively and continued under a new name starting April 2026.

The fork exists because the architectural changes required for the new direction — a port from WinForms to Avalonia, retargeting to .NET 8, splitting the service into its own binary, and adding a SQLite-backed event store with an explanation engine — are too invasive to land as PRs against upstream. Per upstream's own guidance in its README, redistributions must use a different name. This is that different name.

Upstream's design goals ("lightweight, non-intrusive") are respected where possible, but PonyWall deliberately trades some footprint for forensics: it records every blocked event in a persistent history, recomputes explanations against the ruleset snapshot that was active at the time of capture, and surfaces that information through a HistoryWindow, a Stats window, and Windows toast notifications. See [`Docs/EXPLAINABILITY.md`](Docs/EXPLAINABILITY.md) for the design contract.

## How PonyWall differs from upstream TinyWall

- **UI**: Ported from WinForms to [Avalonia 11](https://avaloniaui.net/) with compiled bindings. Runs on .NET 8 with single-file publish and partial trimming.
- **Service split**: The firewall service is its own binary (`PonyWallService.exe`) separate from the tray UI (`PonyWall.exe`).
- **Observability stack**: SQLite event store (hot/warm schema), per-rule ruleset snapshots (SHA-256 keyed), explanation engine that classifies each event against its historical ruleset and surfaces evidence chips + near-miss rules, forensic JSON export.
- **Stats**: A dashboard showing top apps/destinations, reason distribution, 24h sparkline, and event-store health.
- **First-block toasts**: Windows toast notification when a never-before-seen app is blocked, with Allow once / Allow always / Block always action buttons. Deduped per-app with a persistent seen-apps map.
- **Live tail + time range filters** in HistoryWindow.
- **Identity rename**: `PonyWall` service, `PonyWallController` pipe, `%ProgramData%\PonyWall\` — can be installed side-by-side with upstream TinyWall without collisions. **Existing TinyWall config is not auto-migrated.**

## Status

**Pre-release.** Version `0.1.0`. I'm dogfooding it as my daily firewall on Windows 11. Expect rough edges.

## How to build

### Prerequisites

- Visual Studio 2022 or 2026 (Build Tools edition is enough — the solution uses classic MSBuild because of COM interop)
- .NET 8 SDK
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (only needed if you want to build the installer)

### Building the binaries

```
msbuild PonyWall.sln /p:Configuration=Release
```

Or, for the single-file self-contained publish used by the installer:

```
publish.cmd
```

This produces `PonyWall.exe` and `PonyWallService.exe` under `publish\` at the repo root.

### Building the installer

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\PonyWall.iss
```

## Contributing

Issues and PRs are welcome on this repo. For substantial changes, please open an issue first so we can discuss scope — this is a small-team project and I care about not letting scope creep derail the explainability goals. See `Docs/EXPLAINABILITY.md` for the design contract that guides what belongs in PonyWall.

Bugs caught during dogfooding are especially welcome. The observability stack is new code and probably has sharp edges.

## License

PonyWall is licensed under the **GNU GPLv3** (same as upstream TinyWall). See `LICENSE.txt` for the full text.

- Original TinyWall code: Copyright © 2011 Károly Pados
- PonyWall modifications: Copyright © 2026 ingnio
- The `TaskDialog` wrapper in `pylorak.Windows\TaskDialog` is Public Domain (originally by KevinGre, [source](https://www.codeproject.com/Articles/17026/TaskDialog-for-WinForms)), retained from upstream.

Per GPLv3 section 5(a), this fork carries prominent notices of modification: this README, the `CHANGES.md` file, and each modified file's git history via `git log`/`git blame`.

## Credits

- **Károly Pados** — original author of TinyWall, the foundation this project builds on. PonyWall exists because TinyWall's core firewall logic (WFP filter management, rule model, app database) is solid enough to be worth preserving and extending. Upstream: <https://tinywall.pados.hu>, <https://github.com/pylorak/tinywall>
- **Avalonia UI** — the cross-platform .NET UI framework that made the port viable.
- **SQLite** via Microsoft.Data.Sqlite — powers the event store.
