; Youthnic Packing Desk — Inno Setup script
; Build:  1) powershell -File installer/publish.ps1
;         2) Compile this script with Inno Setup 6 (ISCC.exe installer/setup.iss)
; Output: installer/Output/YouthnicPackingDeskSetup.exe
;
; Install-time options:
;   * Components: packing desk app (always) + optional desk-side lookup API
;   * Lookup API auto-start at logon (only when the API component is chosen)
;   * Optional admin page: default lookup API URL + key (for the hosted lookup
;     server) + suggested seller accounts, written to install-defaults.json for
;     the desk setup wizard

#define AppName "Youthnic Packing Desk"
; Version comes from the git tag in CI (/DAppVersion=...) — falls back to 1.0.0 locally.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "Youthnic"
#define AppExe "Youthnic.Desktop.exe"
#define ApiExe "Youthnic.LookupApi.exe"

[Setup]
AppId={{7C3A9E10-4F1B-4E5A-9C2D-YPDESK001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Youthnic\PackingDesk
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=YouthnicPackingDeskSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
; Per-machine install. Per-desk credentials/profiles live in each operator's
; LocalAppData and are created on first run — nothing secret in Program Files.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Components]
Name: "desktop"; Description: "Packing desk application"; Types: full compact custom; Flags: fixed
Name: "lookupapi"; Description: "Lookup API on this PC (desk-side database access)"; Types: full

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "autostartapi"; Description: "Run the lookup API on this PC at logon (uncheck if the API runs on another PC/server)"; GroupDescription: "Lookup backend:"; Components: lookupapi; Flags: checkedonce

[Files]
; Desktop app (self-contained — no .NET runtime needed on the packing PC)
Source: "publish\Desktop\*"; DestDir: "{app}\Desktop"; Flags: ignoreversion recursesubdirs; Components: desktop
; Lookup API (desk-side option; configured via environment — see DEPLOYMENT.md)
Source: "publish\LookupApi\*"; DestDir: "{app}\LookupApi"; Flags: ignoreversion recursesubdirs; Components: lookupapi

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Desktop\{#AppExe}"; Components: desktop
Name: "{group}\Lookup API (console)"; Filename: "{app}\LookupApi\{#ApiExe}"; Components: lookupapi
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Desktop\{#AppExe}"; Tasks: desktopicon; Components: desktop

[Registry]
; Auto-start the lookup API on logon when the desk-side task is selected.
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "YouthnicLookupApi"; ValueData: """{app}\LookupApi\{#ApiExe}"""; \
  Flags: uninsdeletevalue; Tasks: autostartapi

[Run]
Filename: "{app}\Desktop\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent; Components: desktop

[Code]
var
  DefaultsPage: TInputQueryWizardPage;

// WebView2 Evergreen Runtime is required. It is preinstalled on most Windows 10/11
// systems; if missing, point the admin at the official bootstrapper.
function WebView2Installed(): Boolean;
var
  Key: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Key) then exit;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Key) then exit;
  Result := False;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not WebView2Installed() then
    SuppressibleMsgBox(
      'Microsoft Edge WebView2 Runtime was not detected.' + #13#10 +
      'Install it first (Evergreen Bootstrapper from https://developer.microsoft.com/microsoft-edge/webview2/).' + #13#10 +
      'Setup will continue, but the application will not start without WebView2.',
      mbInformation, MB_OK, IDOK);
end;

procedure InitializeWizard();
begin
  // Optional admin page shown after the directory page: first-run defaults for the
  // desk setup wizard. Everything here is optional; only the lookup API key is a
  // (desk-to-server) shared secret — needed solely when the hosted lookup server
  // has key auth enabled, so leave it blank for a desk-side API.
  DefaultsPage := CreateInputQueryPage(wpSelectComponents,
    'Desk defaults (optional)',
    'Pre-fill the setup wizard on each desk',
    'Leave blank to enter these on each desk instead. Only fill the API key when using the hosted lookup server with key auth.');
  DefaultsPage.Add('Lookup API URL (e.g. http://127.0.0.1:5080, http://192.168.1.50:5080 or the hosted server URL)', False);
  DefaultsPage.Add('Suggested Flipkart seller account (ETHNIC JUNCTION - Flipkart)', False);
  DefaultsPage.Add('Suggested Myntra seller account (ETHNIC JUNCTION - Myntra Youthnic / VB EXPORT - Myntra PPMP — type one)', False);
  DefaultsPage.Add('Lookup API key (only when the hosted lookup server requires X-Api-Key)', False);
  // Pre-fill production defaults for Youthnic hosted lookup service
  DefaultsPage.Values[0] := 'https://flipkart-vms-server2-0.onrender.com';
  DefaultsPage.Values[1] := 'ETHNIC JUNCTION - Flipkart';
  DefaultsPage.Values[2] := 'ETHNIC JUNCTION - Myntra Youthnic';
  DefaultsPage.Values[3] := '237c32350310eb6d69fed654499b55216f7be9f6ab54f537f503f799bb1c0cc1';
end;

function DefaultsProvided(): Boolean;
begin
  Result := (Trim(DefaultsPage.Values[0]) <> '') or
            (Trim(DefaultsPage.Values[1]) <> '') or
            (Trim(DefaultsPage.Values[2]) <> '') or
            (Trim(DefaultsPage.Values[3]) <> '');
end;

function JsonEscape(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

procedure WriteInstallDefaults();
var
  Path, Json: String;
begin
  if not DefaultsProvided() then exit;
  Path := ExpandConstant('{commonappdata}\Youthnic\PackingDesk');
  if not ForceDirectories(Path) then exit;
  Json := '{' +
    '"lookupApiUrl":"' + JsonEscape(Trim(DefaultsPage.Values[0])) + '",' +
    '"flipkartSeller":"' + JsonEscape(Trim(DefaultsPage.Values[1])) + '",' +
    '"myntraSeller":"' + JsonEscape(Trim(DefaultsPage.Values[2])) + '",' +
    '"lookupApiKey":"' + JsonEscape(Trim(DefaultsPage.Values[3])) + '",' +
    '"flipkartVmsUrl":"https://fkvms.synlabs.io/",' +
    '"flipkartUsername":"ethnicfeed8",' +
    '"flipkartPassword":"Ethnic@123",' +
    '"flipkartFeedId":"9e5195bd",' +
    '"myntraVmsUrl":"https://fkvms.synlabs.io/",' +
    '"myntraUsername":"ethnicfeed8",' +
    '"myntraPassword":"Ethnic@123",' +
    '"myntraFeedId":"9e5195bd"}';
  SaveStringToFile(Path + '\install-defaults.json', Utf8Encode(Json), False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteInstallDefaults();
end;
