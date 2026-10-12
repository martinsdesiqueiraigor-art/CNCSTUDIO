; Instalador do CncStudio (Inno Setup 6). Gerado no CI a partir do executável publicado:
;   iscc /DAppVersion=1.0.123 installer\CncStudio.iss
; Saída: installer-output\CncStudio-Setup-<versão>.exe

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6B0E5B53-1E2C-4F7B-9C2E-6F1C2A7D9A41}
AppName=CncStudio
AppVersion={#AppVersion}
AppVerName=CncStudio {#AppVersion}
AppPublisher=CncStudio
DefaultDirName={autopf}\CncStudio
DefaultGroupName=CncStudio
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=CncStudio-Setup-{#AppVersion}
SetupIconFile=..\src\CncStudio.App\Assets\cncstudio.ico
UninstallDisplayIcon={app}\CncStudio.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Instala sem pedir administrador (pasta do usuário), ou para todos, se o usuário escolher
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "associate"; Description: "Abrir arquivos .nc, .mpf e .spf com o CncStudio (duplo clique)"; GroupDescription: "Associação de arquivos:"; Flags: unchecked

[Files]
Source: "..\publish\CncStudio-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CncStudio"; Filename: "{app}\CncStudio.exe"
Name: "{group}\Exemplos do CncStudio"; Filename: "{app}\samples"
Name: "{group}\{cm:UninstallProgram,CncStudio}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CncStudio"; Filename: "{app}\CncStudio.exe"; Tasks: desktopicon

[Registry]
; Associação opcional: o CncStudio aparece em "Abrir com" e vira o padrão para .nc/.mpf/.spf
Root: HKA; Subkey: "Software\Classes\CncStudio.Programa"; ValueType: string; ValueName: ""; ValueData: "Programa CNC"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\CncStudio.Programa\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\CncStudio.exe,0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\CncStudio.Programa\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\CncStudio.exe"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.nc"; ValueType: string; ValueName: ""; ValueData: "CncStudio.Programa"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.mpf"; ValueType: string; ValueName: ""; ValueData: "CncStudio.Programa"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.spf"; ValueType: string; ValueName: ""; ValueData: "CncStudio.Programa"; Flags: uninsdeletevalue; Tasks: associate

[Run]
Filename: "{app}\CncStudio.exe"; Description: "{cm:LaunchProgram,CncStudio}"; Flags: nowait postinstall skipifsilent
