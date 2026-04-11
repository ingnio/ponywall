; PonyWall Inno Setup script
;
; Builds an installer for the Avalonia/.NET 8 version of PonyWall
; (a fork of TinyWall by Károly Pados, heavily modified).
;
; Prerequisites:
;   1. Run publish.cmd from the repo root to produce single-file binaries in publish\
;   2. Install Inno Setup 6 (https://jrsoftware.org/isdl.php)
;   3. Compile this script with: iscc installer\PonyWall.iss
;
; Output: installer\Output\PonyWallSetup-{version}.exe

#define MyAppName "PonyWall"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "ingnio"
#define MyAppURL "https://github.com/ingnio/ponywall"
; Note: the exe source file names are still TinyWall.Avalonia.exe / TinyWallService.exe
; because the project folders weren't renamed. The installed filename ends up
; matching the source.
#define MyAppExeName "TinyWall.Avalonia.exe"
#define MyServiceExeName "TinyWallService.exe"
#define MyServiceName "PonyWall"

[Setup]
; Distinct AppId from upstream TinyWall so PonyWall can be installed side-by-side
; without confusing Windows' Add/Remove Programs or triggering upgrade detection
; on a different product.
AppId={{A1B2C3D4-E5F6-4789-A0B1-C2D3E4F50001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\PonyWall
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
OutputDir=Output
OutputBaseFilename=PonyWallSetup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\TinyWall.Avalonia\Assets\firewall.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start PonyWall on Windows startup"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\{#MyServiceExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
; Launch the UI exe after install — it will self-install the service via SCM on first run.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Stop and delete the service before uninstalling files.
; The Avalonia UI exposes /uninstall (TODO: wire this up). For now, use sc.exe directly.
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopPonyWallService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeletePonyWallService"

[UninstallDelete]
; Clean up PonyWall data on uninstall (optional - users may want to keep settings)
Type: filesandordirs; Name: "{commonappdata}\PonyWall\logs"
