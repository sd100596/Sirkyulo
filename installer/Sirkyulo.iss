; Inno Setup script for Sirkyulo.
;
; Builds a per-user installer that registers in Add/Remove Programs. Per-user rather than
; machine-wide because Sirkyulo only ever touches the current user's desktop, HKCU and
; %APPDATA% — asking for admin would be dishonest about what it does, and would break
; drag-and-drop from Explorer, which runs unelevated.
;
; The uninstaller runs "Sirkyulo.exe --uninstall" before deleting anything. That step returns
; every grouped shortcut to the desktop. Without it, uninstalling would destroy the user's
; shortcuts along with %APPDATA%\Sirkyulo.
;
; Build:  ISCC.exe installer\Sirkyulo.iss

#define AppName        "Sirkyulo"
#define AppVersion     "1.0.0"
#define AppPublisher   "sd100596"
#define AppUrl         "https://github.com/sd100596/Sirkyulo"
#define AppExe         "Sirkyulo.exe"
#define PublishDir     "..\publish"

[Setup]
AppId={{7B3C5E41-9D2A-4F68-B1E7-2C6A8F45D390}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Per-user install: no UAC prompt, no admin rights.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

OutputDir=..\dist
OutputBaseFilename=Sirkyulo-{#AppVersion}-setup
SetupIconFile=..\assets\sirkyulo.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Close a running copy before overwriting its files.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startupicon"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Additional options:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; The app manages this key itself from its tray menu; the task just sets the initial state.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Runs before any files are removed. Returns every grouped shortcut to the desktop, clears
; the startup entry and removes %APPDATA%\Sirkyulo.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; RunOnceId: "SirkyuloCleanup"; \
    Flags: waituntilterminated runhidden

[Code]

{ ---------------------------------------------------------------------------
  .NET 9 Desktop Runtime check.

  Sirkyulo ships framework-dependent (under 1 MB) rather than bundling a 60 MB runtime, so
  the runtime has to be present. Without this check the app would install cleanly and then
  simply fail to start, which is the worst possible failure: silent and unexplained.
  --------------------------------------------------------------------------- }

function IsDotNet9DesktopInstalled(): Boolean;
var
  SharedDir: String;
  FindRec: TFindRec;
begin
  Result := False;
  SharedDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');

  if not DirExists(SharedDir) then
    Exit;

  if FindFirst(SharedDir + '\9.*', FindRec) then
  begin
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Response: Integer;
  ErrorCode: Integer;
begin
  Result := True;

  if IsDotNet9DesktopInstalled() then
    Exit;

  Response := MsgBox(
    'Sirkyulo needs the .NET 9 Desktop Runtime, which does not appear to be installed.' + #13#10#13#10 +
    'Without it, Sirkyulo will install but will not start.' + #13#10#13#10 +
    'Open the download page now?' + #13#10#13#10 +
    'You can also install it from a terminal with:' + #13#10 +
    '    winget install Microsoft.DotNet.DesktopRuntime.9',
    mbConfirmation, MB_YESNOCANCEL);

  if Response = IDYES then
  begin
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/9.0',
      '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end
  else if Response = IDCANCEL then
    Result := False;

  { IDNO falls through: the user has chosen to install anyway. }
end;
