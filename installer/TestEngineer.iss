; TestEngineer Inno Setup — 由 scripts/publish.ps1 在发布目录就绪后调用
#define MyAppName "TestEngineer"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "TestEngineer"
#ifndef MyAppSource
  #define MyAppSource "..\artifacts\publish\TestEngineer-win-x64"
#endif

[Setup]
AppId={{A7C3E9B1-4D2F-4E8A-9C11-7B6E5D4F3A21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\publish
OutputBaseFilename=TestEngineer-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes
UninstallDisplayIcon={app}\TestEngineer.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: unchecked

[Files]
Source: "{#MyAppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\TestEngineer.exe"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\TestEngineer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TestEngineer.exe"; Description: "启动 TestEngineer"; Flags: nowait postinstall skipifsilent
