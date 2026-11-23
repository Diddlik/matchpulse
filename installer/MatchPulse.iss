; MatchPulse installer (Inno Setup 6).
; Build: ISCC /DAppVersion=1.2.3 /DSourceDir=<dotnet publish output> installer\MatchPulse.iss
; Installs per user (no admin rights), like the app's data in %APPDATA%\MatchPulse, which uninstall keeps.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{6D3C1E52-8B0A-4C5E-9E3F-4B1F0C7A2D91}
AppName=MatchPulse
AppVersion={#AppVersion}
AppPublisher=Diddlik
AppPublisherURL=https://github.com/Diddlik/matchpulse
AppSupportURL=https://github.com/Diddlik/matchpulse/issues
DefaultDirName={autopf}\MatchPulse
DefaultGroupName=MatchPulse
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=MatchPulse-Setup-{#AppVersion}
SetupIconFile=..\src\MatchPulse\matchpulse.ico
UninstallDisplayIcon={app}\MatchPulse.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.Autostart=MatchPulse mit Windows starten
en.Autostart=Start MatchPulse with Windows
de.Launch=MatchPulse jetzt starten
en.Launch=Start MatchPulse now

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Files of an older version that the new one no longer ships.
Type: filesandordirs; Name: "{app}\ui"
Type: filesandordirs; Name: "{app}\cues"

[Icons]
Name: "{group}\MatchPulse"; Filename: "{app}\MatchPulse.exe"; Parameters: "--show"
Name: "{group}\{cm:UninstallProgram,MatchPulse}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MatchPulse"; Filename: "{app}\MatchPulse.exe"; Parameters: "--show"; Tasks: desktopicon

[Registry]
; Autostart in the tray, without opening the window.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MatchPulse"; ValueData: """{app}\MatchPulse.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\MatchPulse.exe"; Parameters: "--show"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent
; Auto-update (MatchPulse runs the installer with /VERYSILENT /relaunch=1): start again in the tray.
Filename: "{app}\MatchPulse.exe"; Flags: nowait; Check: Relaunch

[UninstallRun]
; Stop only the MatchPulse that runs from this installation, never another copy (e.g. a developer build).
Filename: "powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Get-Process MatchPulse -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -like '{app}\*' } | Stop-Process -Force"""; Flags: runhidden; RunOnceId: "StopMatchPulse"

[Code]
function Relaunch: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;
