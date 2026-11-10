#ifndef AppIdentity
  #define AppIdentity "DemoMux.Windows"
#endif
#ifndef ProductName
  #define ProductName "DemoMux"
#endif
#ifndef DefaultDirectory
  #define DefaultDirectory "{localappdata}\Programs\DemoMux"
#endif
#define Version GetVersionNumbersString(Payload + "\DemoMux.exe")
#define PackedVersion
#expr GetPackedVersion(Payload + "\DemoMux.exe", PackedVersion)

[Setup]
AppId={#AppIdentity}
AppName={#ProductName}
AppVersion={#Version}
AppPublisher=DemoMux
AppPublisherURL=https://github.com/AidenHwang99/DemoMux
DefaultDirName={#DefaultDirectory}
DefaultGroupName={#ProductName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir={#Output}
OutputBaseFilename=DemoMux-Windows-{#ReleaseVersion}-Setup
SetupIconFile=..\Assets\DemoMux.ico
UninstallDisplayIcon={app}\DemoMux.exe
Compression=zip/6
SolidCompression=no
WizardStyle=modern
SetupLogging=yes
CloseApplications=no
RestartApplications=no
AppMutex=Local\DemoMux.InstallationGuard
SetupMutex={#AppIdentity}.Setup
UninstallDisplayName={#ProductName}
AllowNoIcons=yes
DisableWelcomePage=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: checkedonce

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\DemoMux.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\DemoMux.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\DemoMux.exe"; Description: "启动 DemoMux"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingVersion: Int64;
  Locator, Service, Processes: Variant;
begin
  Result := '';
  if CheckForMutexes('Local\DemoMux.InstallationGuard') then
    Result := 'DemoMux 正在运行。请从系统托盘选择退出 DemoMux，然后重试。';
  // Older portable versions predate InstallationGuard. Never install over, terminate,
  // or accidentally activate a running old copy. Isolated test identities do not touch it.
  #if AppIdentity == "DemoMux.Windows"
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''DemoMux.exe''');
    if Processes.Count > 0 then
      Result := 'DemoMux 正在运行（包括便携版）。请从系统托盘退出后重试。';
  except
    Result := '无法检查正在运行的 DemoMux。请退出应用后重试，或检查 Windows WMI 服务。';
  end;
  #endif
  if GetPackedVersion(ExpandConstant('{app}\DemoMux.exe'), ExistingVersion) then
    if ComparePackedVersion(ExistingVersion, {#PackedVersion}) > 0 then
      Result := '已安装更新版本，不能降级。请下载最新安装包。';
end;

procedure InitializeWizard;
begin
  WizardForm.ReadyMemo.Text := '';
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '点击安装即可使用，无需自行整理文件。' + NewLine + NewLine +
    MemoDirInfo + NewLine + NewLine +
    '包含 .NET、WebView2、Caddy 和 Tailscale。组件准备无需下载；登录及公网分享需要联网。' + NewLine +
    '升级保留账号、项目列表和偏好。卸载默认保留这些数据及原始项目。' + NewLine + NewLine + MemoTasksInfo;
end;
