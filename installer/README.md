# TinyWall Installer

This directory contains the Inno Setup script for building the TinyWall installer.

## Building the installer

1. **Publish the binaries first:**
   ```
   cd ..
   publish.cmd
   ```
   This produces `..\publish\TinyWall.Avalonia.exe` and `..\publish\TinyWallService.exe`.

2. **Install Inno Setup 6:**
   Download from <https://jrsoftware.org/isdl.php> and install. The compiler is `iscc.exe` and is added to PATH by default.

3. **Compile the installer:**
   ```
   iscc TinyWall.iss
   ```
   The output goes to `Output\TinyWallSetup-{version}.exe`.

## What the installer does

- Installs both `TinyWall.Avalonia.exe` and `TinyWallService.exe` side-by-side under `Program Files\TinyWall\`
- Adds `LICENSE.txt` to the install dir (so the Settings → About link works)
- Creates a Start menu shortcut
- Optional: desktop icon and Windows startup entry
- On first launch, the UI exe self-registers the Windows Service via SCM (no separate installer step needed)

## What the uninstaller does

- Stops and deletes the `TinyWall` Windows Service via `sc.exe`
- Removes installed files
- Removes the log directory under `%ProgramData%\TinyWall\logs`
- Leaves user config (`%ProgramData%\TinyWall\config`) in place — manual cleanup if you want a full purge

## Migrating from the legacy WiX installer

The old `MsiSetup\` directory is the WiX installer for the WinForms version of TinyWall. It references hundreds of .NET 4.8 dependency DLLs that don't exist in the new single-file build. It's preserved for reference but should not be used to install the new version.
