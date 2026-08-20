; Cartridge OS installer. Build with: installer\publish.ps1 then ISCC installer\CartridgeOS.iss
; (see installer\README.md). Packages the self-contained Launcher + Service publish output,
; registers the Service with the SCM (auto-start, restart-on-crash), and sets the Launcher to
; start at user logon. Requires admin (Program Files + service install).

#define MyAppName "Cartridge OS"
#define MyAppVersion "1.0.2"
#define MyAppExeName "CartridgeOS.Launcher.exe"
#define MyServiceExeName "CartridgeOS.Service.exe"
#define MyServiceName "CartridgeOS"

[Setup]
AppId={{9F2C6B7E-9B7B-4C57-9F0A-9C6B6B0E7E2A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=CartridgeOS-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\CartridgeOS.Launcher\Assets\app.ico
UninstallDisplayIcon={app}\Launcher\{#MyAppExeName}
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#MyAppName} automatically when you sign in"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "publish\Launcher\*"; DestDir: "{app}\Launcher"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\Launcher\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; HKLM Run (not a {userstartup} shortcut) so autostart works regardless of which account performed
; the elevated install — {userstartup} resolves to whichever account's token actually ran Setup,
; which on a real machine can be a dedicated Administrator account rather than the person who'll
; actually use the app day to day. Confirmed live: a {userstartup} shortcut landed in
; C:\Users\Administrator\...\Startup instead of the real end-user account during a manual test.
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\Launcher\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; Service: created stopped, given a restart-on-crash policy, then started. sc.exe is quoted with
; embedded quotes around binPath since the install path itself can contain spaces ("Program Files").
Filename: "{sys}\sc.exe"; Parameters: "create {#MyServiceName} binPath= ""\""{app}\Service\{#MyServiceExeName}\"""" start= auto DisplayName= ""{#MyAppName} Service"""; Flags: runhidden; StatusMsg: "Registering {#MyAppName} service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#MyServiceName} ""Background service for {#MyAppName}."""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden; StatusMsg: "Starting {#MyAppName} service..."
Filename: "{app}\Launcher\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Best-effort: a stopped/nonexistent service still returns nonexistent-cleanly, so `runhidden` +
; the default "continue on error" behavior is enough here without wrapping every call.
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{cmd}"; Parameters: "/c taskkill /IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillLauncher"

[Code]
// Installing over a running instance would leave locked files that Inno can't overwrite; same for
// uninstall. Both directions need the app (and its service) stopped first.
procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  StopRunningApp;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp;
  Result := True;
end;
