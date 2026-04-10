; TinyWall Inno Setup script
;
; Builds an installer for the Avalonia/.NET 8 version of TinyWall.
; Prerequisites:
;   1. Run publish.cmd from the repo root to produce single-file binaries in publish\
;   2. Install Inno Setup 6 (https://jrsoftware.org/isdl.php)
;   3. Compile this script with: iscc installer\TinyWall.iss
;
; Output: installer\Output\TinyWallSetup-{version}.exe

#define MyAppName "TinyWall"
#define MyAppVersion "3.4.1"
#define MyAppPublisher "Karoly Pados"
#define MyAppURL "https://tinywall.pados.hu"
#define MyAppExeName "TinyWall.Avalonia.exe"
#define MyServiceExeName "TinyWallService.exe"
#define MyServiceName "TinyWall"

[Setup]
AppId={{B0F0F0F0-0000-4000-8000-000000000001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\TinyWall
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
OutputDir=Output
OutputBaseFilename=TinyWallSetup-{#MyAppVersion}
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
Name: "startupicon"; Description: "Start TinyWall on Windows startup"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\{#MyServiceExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

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
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopTinyWallService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteTinyWallService"

[UninstallDelete]
; Clean up TinyWall data on uninstall (optional - users may want to keep settings)
Type: filesandordirs; Name: "{commonappdata}\TinyWall\logs"
