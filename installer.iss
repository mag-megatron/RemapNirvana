; ==========================================================
; NirvanaRemap - Inno Setup Script
; Gerado para o projeto RemapNirvana
; ==========================================================

#define MyAppName      "NirvanaRemap"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "RemapNirvana Team"
#define MyAppURL       ""
#define MyAppExeName   "NirvanaRemap.exe"

[Setup]
; IMPORTANTE: Substitua o GUID abaixo por um válido (execute no PowerShell: [guid]::NewGuid().ToString())
AppId={{GERE-UM-GUID-AQUI}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.\installer_output
OutputBaseFilename=NirvanaRemap_Setup_{#MyAppVersion}
SetupIconFile=Avalonia\Assets\nirvanaIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english";             MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Inclui TUDO da pasta publish (exe, dlls, runtimes, etc.)
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";         Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}";   Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Executar {#MyAppName}"; Flags: nowait postinstall skipifsilent
