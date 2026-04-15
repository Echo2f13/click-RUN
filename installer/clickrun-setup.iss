; Click Run - Inno Setup Script
; Builds a Windows installer for the Click Run tray application.
;
; UPGRADE BEHAVIOR:
;   Running a newer Setup.exe over an existing install performs a clean in-place upgrade.
;   - Same AppId across all versions (NEVER change this)
;   - Same install directory reused automatically
;   - All files overwritten via ignoreversion flag
;   - Running instance killed before file replacement
;   - No uninstall required, no side-by-side installs
;
; Prerequisites:
;   1. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
;   2. Publish the app: dotnet publish src/ClickRun/ClickRun.csproj -c Release
;   3. Open this .iss file in Inno Setup Compiler and click Build
;
; Silent upgrade: ClickRunSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
;
; Output: installer/Output/ClickRunSetup.exe

#define MyAppName "Click Run"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Click Run Contributors"
#define MyAppURL "https://github.com/Echo2f13/click-RUN"
#define MyAppExeName "ClickRun.exe"
#define MyAppMutex "ClickRunMutex"
#define PublishDir "..\src\ClickRun\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
; CRITICAL: AppId MUST remain identical across ALL versions. Changing it breaks upgrades.
AppId={{B7E3F2A1-4D5C-4E6F-8A9B-1C2D3E4F5A6B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Install directory — MUST remain the same across versions
DefaultDirName={autopf}\{#MyAppName}
UsePreviousAppDir=yes

; Start menu group
DefaultGroupName={#MyAppName}
UsePreviousGroup=yes
DisableProgramGroupPage=yes

; Prevent duplicate instances during install
AppMutex={#MyAppMutex}

; Installer output
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=ClickRunSetup
SetupIconFile=..\src\ClickRun\icon.ico

; Compression
Compression=lzma2
SolidCompression=yes

; UI
WizardStyle=modern

; Privileges — admin for Program Files install
PrivilegesRequired=admin

; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Display
UninstallDisplayIcon={app}\{#MyAppExeName}

; Upgrade behavior — close running app, don't auto-restart
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start Click Run on Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Main executable and all published files — ignoreversion ensures overwrite on upgrade
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Icon file (if present)
Source: "..\src\ClickRun\icon.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

; Desktop shortcut (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Registry]
; Auto-start on Windows startup (optional)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClickRun"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Launch after install (skipped in silent mode)
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the app before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "StopClickRun"

[UninstallDelete]
; Clean up config directory (optional — user data)
; Uncomment the next line to remove config on uninstall:
; Type: filesandordirs; Name: "{userprofile}\.clickrun"

[Code]
// Stop running instance before install/upgrade
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec('taskkill', '/F /IM ClickRun.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Brief pause to ensure file handles are released
    Sleep(500);
  end;
end;
