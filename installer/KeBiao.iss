; 课表 安装程序（Inno Setup 6）
; 由 GitHub Actions 编译：ISCC.exe /DAppVersion=1.0.0 /DPublishDir=..\publish installer\KeBiao.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{6C1F4E7A-3B2D-4C8E-9A51-7D0E2F4B8C13}
AppName=课表
AppVersion={#AppVersion}
AppPublisher=课表
DefaultDirName={autopf}\KeBiao
DefaultGroupName=课表
DisableProgramGroupPage=yes
DisableDirPage=yes
; 装在当前用户下，不需要管理员权限
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=课表-安装-{#AppVersion}
SetupIconFile=..\src\KeBiao.App\Assets\kebiao.ico
UninstallDisplayIcon={app}\KeBiao.exe
UninstallDisplayName=课表
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "zh"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "autostart"; Description: "开机自动启动"; Flags: checkedonce
Name: "desktopicon"; Description: "在桌面创建快捷方式"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\安装说明.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\课表"; Filename: "{app}\KeBiao.exe"
Name: "{autodesktop}\课表"; Filename: "{app}\KeBiao.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KeBiao"; ValueData: """{app}\KeBiao.exe"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\KeBiao.exe"; Description: "立即启动课表"; Flags: nowait postinstall

[UninstallRun]
Filename: "{app}\KeBiao.exe"; Parameters: "--exit"; RunOnceId: "StopKeBiao"; Flags: runhidden waituntilterminated

[Code]
// 覆盖安装前，先让正在运行的课表退出（课表数据保存在 %AppData%\KeBiao，不受影响）
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\KeBiao.exe');
  if FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
