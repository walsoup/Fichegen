; ==============================================================================
; PROFstudio - Windows Graphical Setup Wizard (Inno Setup 6)
; ==============================================================================

#define MyAppName "PROFstudio"
#define MyAppVersion "1.4.0"
#define MyAppPublisher "walsoup / PROFstudio"
#define MyAppURL "https://github.com/walsoup/fichegen"
#define MyAppExeName "FicheGen.App.exe"

[Setup]
AppId={{7E1F4B62-9C3D-4A58-B7E0-2F6A1D8C5E93}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
AppComments=L'atelier pedagogique intelligent pour enseignants sous Windows 10 et 11
DefaultDirName={userpf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\dist
OutputBaseFilename=PROFstudio-Setup-v{#MyAppVersion}-x64
SetupIconFile=..\src\FicheGen.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=100
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\app.ico"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Installed: Boolean;
  ErrorCode: Integer;
begin
  Result := True;

  // Verification du runtime WebView2 (requis pour l'affichage de l'apercu de document)
  Installed := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
               RegKeyExists(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
               RegKeyExists(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');

  if not Installed then
  begin
    if MsgBox('Le runtime Microsoft Edge WebView2 est requis pour afficher les aperçus de documents dans PROFstudio.' + #13#10 + #13#10 +
              'Souhaitez-vous ouvrir la page de téléchargement officielle Microsoft maintenant ?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://go.microsoft.com/fwlink/?linkid=2124701', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;
