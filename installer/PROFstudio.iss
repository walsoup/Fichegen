; PROFstudio - Inno Setup script (per-user classic installer)
; Compile with ISCC: iscc installer\PROFstudio.iss
; Requires the Release publish output in artifacts\publish\win-x64\

#define MyAppName "PROFstudio"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "PROFstudio Team"
#define MyAppExeName "FicheGen.App.exe"

[Setup]
AppId={{7E1F4B62-9C3D-4A58-B7E0-2F6A1D8C5E93}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={userpf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\dist
OutputBaseFilename=PROFstudio-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\FicheGen.App\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
