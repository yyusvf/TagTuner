; TagTuner Setup
;
; Installiert pro Benutzer, ohne Administratorrechte. Das Kontextmenu traegt
; die App selbst unter HKCU ein, hier wird dafuer nichts geschrieben.

#define AppName      "TagTuner"
#define AppPublisher "yyusvf"
#define AppExeName   "TagTuner.exe"
#define AppId        "{{8C4A3F21-7D6B-4E9A-B2C5-1F0E9D3A6B47}"

#ifndef AppVersion
  #define AppVersion "0.4.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/yyusvf/TagTuner
AppSupportURL=https://github.com/yyusvf/TagTuner/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=TagTuner-{#AppVersion}-Setup
SetupIconFile=..\src\TagTuner.App\Assets\TagTuner.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Waehrend die App laeuft, laesst sich ihre Datei nicht ersetzen. Setup
; schliesst sie deshalb selbst; genau darauf setzt die Selbstaktualisierung.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
