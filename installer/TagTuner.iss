; TagTuner Setup
;
; Installiert pro Benutzer, ohne Administratorrechte. Das Kontextmenu traegt
; die App selbst unter HKCU ein, hier wird dafuer nichts geschrieben.

#define AppName      "TagTuner"
#define AppPublisher "yyusvf"
#define AppExeName   "TagTuner.exe"
#define AppId        "{{8C4A3F21-7D6B-4E9A-B2C5-1F0E9D3A6B47}"

#ifndef AppVersion
  #define AppVersion "0.5.0"
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

; Dieselben dreizehn Sprachen wie die App. Setup nimmt von sich aus die,
; die zur Systemsprache passt, und faellt sonst auf Englisch zurueck.
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "cs"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "sv"; MessagesFile: "compiler:Languages\Swedish.isl"

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
