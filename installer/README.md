# PonyWall Installer

This directory contains the Inno Setup script for building the PonyWall installer.

## Building the installer

1. **Publish the binaries first:**
   ```
   cd ..
   publish.cmd
   ```
   This produces `..\publish\PonyWall.exe` and `..\publish\PonyWallService.exe`.

2. **Install Inno Setup 6:**
   Download from <https://jrsoftware.org/isdl.php> and install. The compiler is `iscc.exe` and is added to PATH by default.

3. **Compile the installer:**
   ```
   iscc PonyWall.iss
   ```
   The output goes to `Output\PonyWallSetup-{version}.exe`.

## What the installer does

- Installs both `PonyWall.exe` (the UI) and `PonyWallService.exe` (the firewall service) side-by-side under `Program Files\PonyWall\`
- Adds `LICENSE.txt`, `README.md`, and `CHANGES.md` to the install dir for GPLv3 compliance and user reference
- Creates a Start menu shortcut
- Optional: desktop icon and Windows startup entry
- On first launch, the UI exe self-registers the Windows Service (named `PonyWall`) via SCM — no separate installer step needed

## What the uninstaller does

- Stops and deletes the `PonyWall` Windows Service via `sc.exe`
- Removes installed files
- Removes the log directory under `%ProgramData%\PonyWall\logs`
- Leaves user config (`%ProgramData%\PonyWall\config`) in place — manual cleanup if you want a full purge

## Coexistence with upstream TinyWall

PonyWall uses a distinct AppId (`{A1B2C3D4-E5F6-4789-A0B1-C2D3E4F50001}`), a distinct service name (`PonyWall`), a distinct pipe (`PonyWallController`), and a distinct ProgramData folder (`%ProgramData%\PonyWall\`). You can install PonyWall side-by-side with upstream TinyWall without Windows confusing them in Add/Remove Programs, without service name collisions, and without either product touching the other's data folder.

Existing TinyWall config is **not** auto-migrated. If you want to carry over your exceptions list, manually copy `%ProgramData%\TinyWall\config` to `%ProgramData%\PonyWall\config` before the first PonyWall launch.

## Migrating from the legacy WiX installer

The old `MsiSetup\` directory is the WiX installer for the WinForms version of TinyWall. It references hundreds of .NET 4.8 dependency DLLs that don't exist in the new single-file build. It's preserved for reference but should not be used to install PonyWall.
