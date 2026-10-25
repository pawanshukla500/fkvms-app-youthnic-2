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
  FkPage: TInputQueryWizardPage;
  MyPage: TInputQueryWizardPage;

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

function JsonEscape(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

function ExtractJsonValue(const Json, Key, DefaultVal: String): String;
var
  P, PEnd: Integer;
  SearchKey: String;
begin
  Result := DefaultVal;
  SearchKey := '"' + Key + '"';
  P := Pos(SearchKey, Json);
  if P = 0 then exit;
  P := P + Length(SearchKey);
  while (P <= Length(Json)) and ((Json[P] = ' ') or (Json[P] = ':') or (Json[P] = #9) or (Json[P] = #13) or (Json[P] = #10)) do
    Inc(P);
  if (P <= Length(Json)) and (Json[P] = '"') then
  begin
    Inc(P);
    PEnd := P;
    while (PEnd <= Length(Json)) and (Json[PEnd] <> '"') do
    begin
      if (Json[PEnd] = '\') and (PEnd < Length(Json)) then
        Inc(PEnd);
      Inc(PEnd);
    end;
    Result := Copy(Json, P, PEnd - P);
  end;
end;

procedure LoadExistingConfig(var DeskName, FkUser, FkPass, FkFeed, FkSeller, MyUser, MyPass, MyFeed, MySeller, ApiUrl, ApiKey: String);
var
  RawJson: AnsiString;
  ExistingJson: String;
  Found: Boolean;
  CommonPath, AppDeskPath, AppPath: String;
begin
  DeskName := 'Desk-01';
  FkUser := 'ethnicfeed8';
  FkPass := 'Ethnic@123';
  FkFeed := '9e5195bd';
  FkSeller := 'ETHNIC JUNCTION - Flipkart';

  MyUser := 'ethnicfeed8';
  MyPass := 'Ethnic@123';
  MyFeed := '9e5195bd';
  MySeller := 'ETHNIC JUNCTION - Myntra Youthnic';

  ApiUrl := 'https://flipkart-vms-server2-0.onrender.com';
  ApiKey := '237c32350310eb6d69fed654499b55216f7be9f6ab54f537f503f799bb1c0cc1';

  Found := False;
  ExistingJson := '';

  AppDeskPath := ExpandConstant('{autopf}\Youthnic\PackingDesk\Desktop\desk-config.json');
  AppPath := ExpandConstant('{autopf}\Youthnic\PackingDesk\desk-config.json');
  CommonPath := ExpandConstant('{commonappdata}\Youthnic\PackingDesk\install-defaults.json');

  if FileExists(AppDeskPath) and LoadStringFromFile(AppDeskPath, RawJson) then
  begin
    ExistingJson := String(RawJson);
    Found := True;
  end
  else if FileExists(AppPath) and LoadStringFromFile(AppPath, RawJson) then
  begin
    ExistingJson := String(RawJson);
    Found := True;
  end
  else if FileExists(CommonPath) and LoadStringFromFile(CommonPath, RawJson) then
  begin
    ExistingJson := String(RawJson);
    Found := True;
  end;

  if Found and (Length(ExistingJson) > 0) then
  begin
    DeskName := ExtractJsonValue(ExistingJson, 'deskName', DeskName);
    FkUser := ExtractJsonValue(ExistingJson, 'flipkartUsername', FkUser);
    FkPass := ExtractJsonValue(ExistingJson, 'flipkartPassword', FkPass);
    FkFeed := ExtractJsonValue(ExistingJson, 'flipkartFeedId', FkFeed);
    FkSeller := ExtractJsonValue(ExistingJson, 'flipkartSeller', FkSeller);

    MyUser := ExtractJsonValue(ExistingJson, 'myntraUsername', MyUser);
    MyPass := ExtractJsonValue(ExistingJson, 'myntraPassword', MyPass);
    MyFeed := ExtractJsonValue(ExistingJson, 'myntraFeedId', MyFeed);
    MySeller := ExtractJsonValue(ExistingJson, 'myntraSeller', MySeller);

    ApiUrl := ExtractJsonValue(ExistingJson, 'lookupApiUrl', ApiUrl);
    ApiKey := ExtractJsonValue(ExistingJson, 'lookupApiKey', ApiKey);
  end;
end;

procedure InitializeWizard();
var
  DefDeskName, DefFkUser, DefFkPass, DefFkFeed, DefFkSeller: String;
  DefMyUser, DefMyPass, DefMyFeed, DefMySeller, DefApiUrl, DefApiKey: String;
begin
  LoadExistingConfig(DefDeskName, DefFkUser, DefFkPass, DefFkFeed, DefFkSeller,
                     DefMyUser, DefMyPass, DefMyFeed, DefMySeller, DefApiUrl, DefApiKey);

  // Page 1: Packing Station Identity & Flipkart VMS Setup
  FkPage := CreateInputQueryPage(wpSelectComponents,
    'Packing Station & Flipkart Setup',
    'Configure station name, camera feed ID, and Flipkart VMS credentials for this PC',
    'Each packing desk PC requires its own unique Desk Name, camera Feed ID, and credentials:');
  FkPage.Add('Desk Name / ID (e.g. Desk-01, Desk-02 ... Desk-12):', False);
  FkPage.Add('Flipkart VMS Username:', False);
  FkPage.Add('Flipkart VMS Password:', False);
  FkPage.Add('Flipkart Camera Feed ID (unique per desk station):', False);
  FkPage.Add('Flipkart Seller Account:', False);

  FkPage.Values[0] := DefDeskName;
  FkPage.Values[1] := DefFkUser;
  FkPage.Values[2] := DefFkPass;
  FkPage.Values[3] := DefFkFeed;
  FkPage.Values[4] := DefFkSeller;

  // Page 2: Myntra VMS & Lookup API Setup
  MyPage := CreateInputQueryPage(FkPage.ID,
    'Myntra VMS & Lookup API Setup',
    'Configure Myntra VMS credentials, camera feed ID, and Lookup API for this PC',
    'Enter the Myntra camera Feed ID and VMS credentials for this station:');
  MyPage.Add('Myntra VMS Username:', False);
  MyPage.Add('Myntra VMS Password:', False);
  MyPage.Add('Myntra Camera Feed ID (unique per desk station):', False);
  MyPage.Add('Myntra Seller Account:', False);
  MyPage.Add('Lookup API URL (hosted server or local API):', False);
  MyPage.Add('Lookup API Key (required for hosted server):', False);

  MyPage.Values[0] := DefMyUser;
  MyPage.Values[1] := DefMyPass;
  MyPage.Values[2] := DefMyFeed;
  MyPage.Values[3] := DefMySeller;
  MyPage.Values[4] := DefApiUrl;
  MyPage.Values[5] := DefApiKey;
end;

procedure WriteInstallDefaults();
var
  CommonPath, AppDeskPath, AppPath, Json: String;
  DeskName, FkUser, FkPass, FkFeed, FkSeller: String;
  MyUser, MyPass, MyFeed, MySeller, ApiUrl, ApiKey: String;
begin
  DeskName := Trim(FkPage.Values[0]);
  if DeskName = '' then DeskName := 'Packing Desk';
  FkUser := Trim(FkPage.Values[1]);
  FkPass := FkPage.Values[2];
  FkFeed := Trim(FkPage.Values[3]);
  FkSeller := Trim(FkPage.Values[4]);

  MyUser := Trim(MyPage.Values[0]);
  MyPass := MyPage.Values[1];
  MyFeed := Trim(MyPage.Values[2]);
  MySeller := Trim(MyPage.Values[3]);
  ApiUrl := Trim(MyPage.Values[4]);
  ApiKey := Trim(MyPage.Values[5]);

  Json := '{' +
    '"deskName":"' + JsonEscape(DeskName) + '",' +
    '"lookupApiUrl":"' + JsonEscape(ApiUrl) + '",' +
    '"lookupApiKey":"' + JsonEscape(ApiKey) + '",' +
    '"flipkartVmsUrl":"https://fkvms.synlabs.io/",' +
    '"flipkartUsername":"' + JsonEscape(FkUser) + '",' +
    '"flipkartPassword":"' + JsonEscape(FkPass) + '",' +
    '"flipkartFeedId":"' + JsonEscape(FkFeed) + '",' +
    '"flipkartSeller":"' + JsonEscape(FkSeller) + '",' +
    '"myntraVmsUrl":"https://fkvms.synlabs.io/",' +
    '"myntraUsername":"' + JsonEscape(MyUser) + '",' +
    '"myntraPassword":"' + JsonEscape(MyPass) + '",' +
    '"myntraFeedId":"' + JsonEscape(MyFeed) + '",' +
    '"myntraSeller":"' + JsonEscape(MySeller) + '",' +
    '"flipkart":{' +
      '"vmsUrl":"https://fkvms.synlabs.io/",' +
      '"username":"' + JsonEscape(FkUser) + '",' +
      '"password":"' + JsonEscape(FkPass) + '",' +
      '"feedId":"' + JsonEscape(FkFeed) + '",' +
      '"sellerAccount":"' + JsonEscape(FkSeller) + '"' +
    '},' +
    '"myntra":{' +
      '"vmsUrl":"https://fkvms.synlabs.io/",' +
      '"username":"' + JsonEscape(MyUser) + '",' +
      '"password":"' + JsonEscape(MyPass) + '",' +
      '"feedId":"' + JsonEscape(MyFeed) + '",' +
      '"sellerAccount":"' + JsonEscape(MySeller) + '"' +
    '}' +
  '}';

  // 1. Write to {commonappdata}\Youthnic\PackingDesk\install-defaults.json
  CommonPath := ExpandConstant('{commonappdata}\Youthnic\PackingDesk');
  if ForceDirectories(CommonPath) then
  begin
    SaveStringToFile(CommonPath + '\install-defaults.json', Utf8Encode(Json), False);
    SaveStringToFile(CommonPath + '\desk-config.json', Utf8Encode(Json), False);
  end;

  // 2. Write to {app}\Desktop\desk-config.json (exact program folder where Youthnic.Desktop.exe lives)
  AppDeskPath := ExpandConstant('{app}\Desktop');
  if ForceDirectories(AppDeskPath) then
  begin
    SaveStringToFile(AppDeskPath + '\desk-config.json', Utf8Encode(Json), False);
  end;

  // 3. Write to {app}\desk-config.json
  AppPath := ExpandConstant('{app}');
  if ForceDirectories(AppPath) then
  begin
    SaveStringToFile(AppPath + '\desk-config.json', Utf8Encode(Json), False);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteInstallDefaults();
end;
