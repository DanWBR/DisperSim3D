; DisperSim3D.iss — Inno Setup 6 script for the Windows installer.
;
; Compiled by packaging/windows/build-installer.ps1, which publishes the
; payload first and then passes the paths in as preprocessor defines:
;
;   ISCC.exe /DAppVersion=1.0.0 /DPayloadDir=...\build\win-x64 ^
;            /DOutputDir=...\dist /DRepoRoot=... DisperSim3D.iss
;
; The payload is a self-contained .NET 10 publish, so the installer has no
; runtime prerequisite — nothing to download, nothing to detect.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
; VersionInfoVersion goes into the Win32 VERSIONINFO resource and must be plain
; numeric — the display version may carry a pre-release suffix (1.0.0-ci.42),
; so build-installer.ps1 passes the stripped form separately.
#ifndef AppVersionNumeric
  #define AppVersionNumeric "0.0.0"
#endif
#ifndef RepoRoot
  #define RepoRoot "..\.."
#endif
#ifndef PayloadDir
  #define PayloadDir RepoRoot + "\build\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir RepoRoot + "\dist"
#endif

#define AppName        "DisperSim 3D"
#define AppPublisher   "Daniel Wagner Oliveira de Medeiros"
#define AppURL         "https://github.com/DanWBR/DisperSim3D"
#define AppExeName     "DisperSim3D.App.exe"
#define CliExeName     "DisperSim3D.CLI.exe"

[Setup]
; AppId uniquely identifies the product across versions — never change it, or
; upgrades will install side by side instead of replacing the old build.
AppId={{89EE1E69-1698-4B96-93D8-A34B77402725}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersionNumeric}
VersionInfoTextVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile={#RepoRoot}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=DisperSim3D-{#AppVersion}-win-x64-setup
SetupIconFile={#RepoRoot}\DisperSim3D\Resources\Icons\Air.ico
; The payload is ~200 MB of self-contained runtime; LZMA2/max keeps the
; installer near a third of that at the cost of a slower compile.
Compression=lzma2/max
SolidCompression=yes
; x64-only: the FluidX3D bridge is compiled for x64 and the publish RID is
; win-x64, so refuse to install anywhere it cannot run.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-machine by default (Program Files), but `/CURRENTUSER` on the command
; line — or the dialog — lets a user without admin rights install into
; %LOCALAPPDATA% instead.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
WizardStyle=modern
DisableProgramGroupPage=yes
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Whole publish tree, including the cli\ subfolder and FluidX3D.dll.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#RepoRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
