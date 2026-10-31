#define MyAppName "ArcadeGridz"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.1"
#endif
#define MyAppPublisher "GRiDDiM"
#define MyAppURL "https://arcadegridz.com"
#define MyAppExeName "ArcadeGridz.App.exe"

[Setup]
AppId={{AD8C06E7-2695-4D6F-93D4-0CA83D18E989}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/GRiDDiM/arcadegridz/issues
AppUpdatesURL=https://github.com/GRiDDiM/arcadegridz/releases
DefaultDirName={localappdata}\Programs\ArcadeGridz
DefaultGroupName=ArcadeGridz
DisableProgramGroupPage=yes
OutputDir=..\publish\installer
OutputBaseFilename=ArcadeGridz-Setup-x64
SetupIconFile=..\ArcadeGridz.App\Assets\ArcadeGridz.ico
UninstallDisplayIcon={app}\Assets\ArcadeGridz.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
AppMutex=ArcadeGridz-{AD8C06E7-2695-4D6F-93D4-0CA83D18E989}
ChangesAssociations=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
Uninstallable=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=ArcadeGridz Windows Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\ArcadeGridz\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ArcadeGridz"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\ArcadeGridz.ico"
Name: "{group}\Uninstall ArcadeGridz"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ArcadeGridz"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\ArcadeGridz.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ArcadeGridz"; WorkingDir: "{app}"; Flags: nowait postinstall runascurrentuser; Check: ShouldLaunchArcadeGridz

[Code]
function HasCommandLineParameter(const Value: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function ShouldLaunchArcadeGridz: Boolean;
begin
  Result := not HasCommandLineParameter('/NOLAUNCH');
end;
