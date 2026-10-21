; Open Driver Updater — Inno Setup 7 script. Produces OpenDriverUpdater-Setup.exe.
#define MyAppName "Open Driver Updater"
#define MyAppVersion "0.7.9"
#define MyAppPublisher "open-driver-updater contributors"
#define MyAppURL "https://github.com/alphingj/open-driver-updater"
#define MyAppExeName "OduApp.exe"

[Setup]
AppId={{8E2C3A40-ODU-42B6-9C1A-OPEN-DRIVER-UPDATER}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Open Driver Updater
DefaultGroupName={#MyAppName}
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=OpenDriverUpdater-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible x86
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\cs\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\native\bin\x64\odu_scan.exe"; DestDir: "{app}\native\bin\x64"; Flags: ignoreversion
Source: "..\native\bin\x86\odu_scan.exe"; DestDir: "{app}\native\bin\x86"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN OpenDriverUpdaterScan /F"; Flags: runhidden; RunOnceId: "DelTask"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
