; TestEngineer Inno Setup — 由 scripts/publish.ps1 在发布目录就绪后调用
#define MyAppName "TestEngineer"
#define MyAppVersion "0.4.2"
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
SetupIconFile=..\src\TestEngineer.Desktop\Assets\App.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
; 复制前由 [Code] 结束安装目录里的本程序、scrcpy 和 adb。仍有占用时强制关闭，避免停在“无法自动关闭应用程序”。
CloseApplications=force
CloseApplicationsFilterExcludes=adb.exe,AdbWinApi.dll,AdbWinUsbApi.dll
UninstallDisplayIcon={app}\TestEngineer.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: unchecked

[Files]
Source: "{#MyAppSource}\*"; Excludes: "adb.exe,AdbWinApi.dll,AdbWinUsbApi.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 这三个文件会被 adb 服务锁住。先尝试结束随包的 adb；仍被占用时改为重启后替换，不让安装失败。
Source: "{#MyAppSource}\tools\third-party\platform-tools\adb.exe"; DestDir: "{app}\tools\third-party\platform-tools"; Flags: ignoreversion restartreplace
Source: "{#MyAppSource}\tools\third-party\platform-tools\AdbWinApi.dll"; DestDir: "{app}\tools\third-party\platform-tools"; Flags: ignoreversion restartreplace
Source: "{#MyAppSource}\tools\third-party\platform-tools\AdbWinUsbApi.dll"; DestDir: "{app}\tools\third-party\platform-tools"; Flags: ignoreversion restartreplace
Source: "{#MyAppSource}\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\adb.exe"; DestDir: "{app}\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3"; Flags: ignoreversion restartreplace
Source: "{#MyAppSource}\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\AdbWinApi.dll"; DestDir: "{app}\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3"; Flags: ignoreversion restartreplace
Source: "{#MyAppSource}\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\AdbWinUsbApi.dll"; DestDir: "{app}\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3"; Flags: ignoreversion restartreplace

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\TestEngineer.exe"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\TestEngineer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TestEngineer.exe"; Description: "启动 TestEngineer"; Flags: nowait postinstall skipifsilent

[Code]
procedure StopAppProcesses();
var
  AppDir, ScriptFile: String;
  ResultCode: Integer;
begin
  AppDir := AddBackslash(ExpandConstant('{app}'));
  StringChangeEx(AppDir, '''', '''''', True);
  ScriptFile := ExpandConstant('{tmp}\stop-app-processes.ps1');
  SaveStringToFile(ScriptFile,
    '$app = ''' + AppDir + '''' + #13#10 +
    'function Get-Owned {' + #13#10 +
    '  Get-CimInstance Win32_Process | Where-Object {' + #13#10 +
    '    $path = [string]$_.ExecutablePath' + #13#10 +
    '    $path -and $path.StartsWith($app, [StringComparison]::OrdinalIgnoreCase)' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10 +
    '@(Get-Owned) | Where-Object { $_.Name -eq ''TestEngineer.exe'' } | ForEach-Object {' + #13#10 +
    '  Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue' + #13#10 +
    '}' + #13#10 +
    'Start-Sleep -Milliseconds 400' + #13#10 +
    'Get-Owned | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }' + #13#10 +
    'Start-Sleep -Milliseconds 500' + #13#10,
    False);
  Exec('powershell.exe', '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptFile + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopAppProcesses();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopAppProcesses();
end;
