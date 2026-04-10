; Click Run - Inno Setup Script
; Builds a Windows installer for the Click Run tray application.
;
; Prerequisites:
;   1. Install Inno Setup from https://jrsoftware.org/isinfo.php
;   2. Publish the app: dotnet publish src/ClickRun/ClickRun.csproj -c Release
;   3. (Optional) Place icon.ico in src/ClickRun/
;   4. Open this .iss file in Inno Setup Compiler and click Build
;
; Output: installer/Output/ClickRunSetup.exe

#define MyAppName "Click Run"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Click Run Contributors"
#define MyAppURL "https://github.com/click-run/click-run"
#define MyAppExeName "ClickRun.exe"
#define PublishDir "..\src\ClickRun\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{B7E3F2A1-4D5C-4E6F-8A9B-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=ClickRunSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\ClickRun\icon.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start Click Run on Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Main executable and all published files
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

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
; Launch after install
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
  end;
end;
