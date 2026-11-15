#define MyAppName "ClipboardWB"
#define MyAppVersion "1.3.0"
#define MyAppExeName "ClipboardWB.exe"

[Setup]
AppId={{8D5A54D1-646E-45AF-B38E-6A2642DAA02B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=ClipboardWB
DefaultDirName={localappdata}\Programs\ClipboardWB
DefaultGroupName=ClipboardWB
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=release
OutputBaseFilename=ClipboardWB-Setup-{#MyAppVersion}-win-x64
SetupIconFile=assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "dist-installer\ClipboardWB.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist-installer\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist-installer\*.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClipboardWB"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall ClipboardWB"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ClipboardWB"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ClipboardWB"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunValue: String;
  InstalledExe: String;
begin
  if CurUninstallStep = usUninstall then begin
    InstalledExe := '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipboardWB', RunValue)
      and (CompareText(RunValue, InstalledExe) = 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipboardWB');
  end;
end;
