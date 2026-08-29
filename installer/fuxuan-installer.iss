; ============================================================
; 符玄桌宠 FuXuan Desktop Pet — Inno Setup 安装器（阶段2）
; 方案依据: docs/installer-plan.md §四（安装目录结构/流程/升级卸载）
; 编译: installer\build-installer.ps1（自动获取 ISCC 并传版本参数）
; 测试模式: /DPrivileges=lowest 编译后 setup.exe /VERYSILENT /SKIPENV /DIR=... 可本地静默验证
; ============================================================
#ifndef MyAppVersion
  #define MyAppVersion "1.0.12"
#endif
#ifndef OutputSuffix
  #define OutputSuffix ""
#endif
#ifndef Privileges
  #define Privileges "admin"
#endif
#define MyAppName "符玄桌宠 FuXuan"
#define MyAppExeName "DesktopPet.exe"
#define MyAppMutex "FuXuanDesktopPetMutex"
#define DefaultDataDir "{localappdata}\FuXuan\DesktopPetData"
#define MyAppPublisher "xuan"

[Setup]
AppId={{8C4E9A1F-5B2D-4E6A-9C3F-2D7B1A5E8C4F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FuXuan
DefaultGroupName=符玄桌宠
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=FuXuanSetup-{#MyAppVersion}{#OutputSuffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired={#Privileges}
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex={#MyAppMutex}
UninstallDisplayName=符玄桌宠
VersionInfoVersion={#MyAppVersion}
SetupLogging=yes
CloseApplications=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Components]
Name: "core"; Description: "桌宠本体 + 桥接服务器 + Python 脚本层（必选，约 500MB）"; Types: full compact custom; Flags: fixed
Name: "openclaw"; Description: "OpenClaw Gateway（已内置：自动初始化配置、注册服务并启动，免手动 npm 安装）"; Types: full compact
Name: "ollama"; Description: "Ollama 本体 + qwen2.5:3b + nomic-embed-text（自动安装并下载约 2.2GB，建议勾选）"; Types: full; ExtraDiskSpaceRequired: 2400000000
Name: "tex"; Description: "MiKTeX（compile_latex 需要，约 200MB，可选）"; Types: full
Name: "extras"; Description: "Everything 便携版 / Pogget 收纳工具（可选）"; Types: full

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"
Name: "autostart"; Description: "登录时自动启动桌宠"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "portable\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs; Components: core; Excludes: "crash_log.txt,test-bridge*.log,test-bridge*.err.log"

[Icons]
Name: "{group}\符玄桌宠"; Filename: "{app}\{#MyAppExeName}"; Components: core
Name: "{group}\启动桥接"; Filename: "{app}\start-bridge.cmd"; Components: core
Name: "{group}\停止桥接"; Filename: "{app}\stop-bridge.cmd"; Components: core
Name: "{group}\检查运行环境"; Filename: "{app}\extras\components\verify-runtime.cmd"; Components: core
Name: "{group}\卸载符玄桌宠"; Filename: "{uninstallexe}"; Components: core
Name: "{autodesktop}\符玄桌宠"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Components: core

[Run]
; ── 阶段3 组件自动化（可按组件条件运行；/SKIPCOMPONENTS 时全部跳过，用于本地测试）──
Filename: "{app}\extras\components\install-vcredist.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: core; Check: not SkipComps(); StatusMsg: "安装 VC++ 运行库..."
Filename: "{app}\extras\components\install-miktex.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: tex; Check: not SkipComps(); StatusMsg: "安装 MiKTeX..."
Filename: "{app}\extras\components\install-everything.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: extras; Check: not SkipComps(); StatusMsg: "配置 Everything 搜索..."
Filename: "{app}\extras\components\install-service.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: core; Check: not SkipComps(); StatusMsg: "注册桥接为 Windows 服务..."
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行符玄桌宠"; Flags: postinstall nowait skipifsilent; Components: core

[UninstallRun]
Filename: "{app}\extras\components\uninstall-service.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UninstallBridgeService"

[Code]
const
  TokenChars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';

var
  SkipEnv: Boolean;
  KeepData: Boolean;   // 卸载时是否保留数据目录（默认保留；静默卸载默认也是保留）
  DataDirPage: TInputDirWizardPage;
  KeyPage: TWizardPage;
  edDeepSeek: TNewEdit;
  edGlm: TNewEdit;
  BridgeToken: String;
  GatewayToken: String;
  RuntimeComponentsAttempted: Boolean;

function RunRuntimeComponent(const ComponentName, ScriptName, DisplayName: String): Boolean;
var
  ScriptPath: String;
  ResultCode: Integer;
begin
  Result := False;
  ScriptPath := ExpandConstant('{app}\extras\components\' + ScriptName);
  if not FileExists(ScriptPath) then
  begin
    Log('FuXuan runtime component missing: ' + ScriptPath);
    MsgBox(DisplayName + '组件文件缺失：' + #13#10 + ScriptPath,
      mbError, MB_OK);
    exit;
  end;

  Log('FuXuan runtime component start: ' + ComponentName);
  // .cmd 不是原生可执行文件。显式经由 cmd.exe 调用，才能保证
  // ExecAsOriginalUser 等待脚本真正结束并拿到退出码。
  if not ExecAsOriginalUser(ExpandConstant('{sys}\cmd.exe'),
    '/d /c call "' + ScriptPath + '"', ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('FuXuan runtime component launch failed: ' + ComponentName);
    MsgBox(DisplayName + '启动失败。' + #13#10 +
      '请查看 %TEMP% 中对应的安装日志，然后重试安装。', mbError, MB_OK);
    exit;
  end;

  Log('FuXuan runtime component exit: ' + ComponentName + ' code=' + IntToStr(ResultCode));
  if ResultCode <> 0 then
  begin
    MsgBox(DisplayName + '安装未完成，退出码：' + IntToStr(ResultCode) + #13#10 +
      '安装器不会把失败的组件当作成功。请根据日志修复网络或权限后重试。',
      mbError, MB_OK);
    exit;
  end;
  Result := True;
end;

function CmdLineParamExists(const Param: String): Boolean;
var
  i: Integer;
begin
  Result := False;
  for i := 1 to ParamCount do
    if CompareText(ParamStr(i), Param) = 0 then
      Result := True;
end;

// 测试模式：跳过全部组件安装脚本（本地静默验证用，避免安装 VC++/Ollama/服务等）
function SkipComps(): Boolean;
begin
  Result := CmdLineParamExists('/SKIPCOMPONENTS');
end;

function GenerateToken: String;
var
  i: Integer;
begin
  Result := '';
  for i := 1 to 64 do
    Result := Result + TokenChars[Random(Length(TokenChars)) + 1];
end;

procedure SetUserEnv(const Name, Value: String);
begin
  RegWriteStringValue(HKCU, 'Environment', Name, Value);
end;

function GetExistingUserEnv(const Name: String): String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', Name, Result) then
    Result := '';
end;

function NormalizeDataDir(const Value: String): String;
begin
  Result := Trim(Value);
  while (Length(Result) > 1) and (Result[1] = '"') and
    (Result[Length(Result)] = '"') do
  begin
    Delete(Result, Length(Result), 1);
    Delete(Result, 1, 1);
    Result := Trim(Result);
  end;
  if Result <> '' then
    Result := ExpandFileName(Result);
  while (Length(Result) > 3) and
    ((Result[Length(Result)] = '\') or (Result[Length(Result)] = '/')) do
    Delete(Result, Length(Result), 1);
end;

function IsDriveRoot(const Value: String): Boolean;
begin
  Result := (Length(Value) = 3) and (Value[2] = ':') and
    ((Value[3] = '\') or (Value[3] = '/'));
end;

function IsSameOrChildPath(const Path, Parent: String): Boolean;
var
  P, B: String;
begin
  P := AddBackslash(NormalizeDataDir(Path));
  B := AddBackslash(NormalizeDataDir(Parent));
  Result := (P <> '\') and (B <> '\') and (Pos(B, P) = 1);
end;

function IsValidDataDir(const Value: String): Boolean; forward;

function IsWritableDataDir(const Value: String): Boolean; forward;

function GetDetectedDataDir: String;
var
  Configured, LegacyC, LegacyD, DefaultDir: String;
begin
  Configured := NormalizeDataDir(GetExistingUserEnv('FU_XUAN_DATA'));
  DefaultDir := NormalizeDataDir(ExpandConstant('{#DefaultDataDir}'));
  // 只复用有效且可写的已配置目录。
  // 旧版本可能曾把 FU_XUAN_DATA 错误写成 C:\，不能仅凭 DirExists 直接复用磁盘根目录。
  if (Configured <> '') and IsValidDataDir(Configured) and
    DirExists(Configured) and IsWritableDataDir(Configured) then
  begin
    Result := Configured;
    exit;
  end;
  // 无有效环境变量时优先复用旧版目录，避免升级后产生第二份忆境。
  LegacyC := NormalizeDataDir('C:\DesktopPetData');
  LegacyD := NormalizeDataDir('D:\DesktopPetData');
  if DirExists(LegacyC) and IsWritableDataDir(LegacyC) then
  begin
    Result := LegacyC;
    exit;
  end;
  if DirExists(LegacyD) and IsWritableDataDir(LegacyD) then
  begin
    Result := LegacyD;
    exit;
  end;
  // 配置不存在、失效或不可用时，回退到当前用户的默认数据目录。
  // 安装完成后会用最终选中的目录覆盖 FU_XUAN_DATA，避免继续复用旧的错误路径。
  Result := DefaultDir;
end;

function IsWritableDataDir(const Value: String): Boolean;
var
  DataDir, ProbeFile: String;
begin
  Result := False;
  DataDir := NormalizeDataDir(Value);
  if not IsValidDataDir(DataDir) then
    exit;
  try
    if (not DirExists(DataDir)) and (not ForceDirectories(DataDir)) then
      exit;
    ProbeFile := AddBackslash(DataDir) + '.fuxuan-write-test';
    DeleteFile(ProbeFile);
    if not SaveStringToFile(ProbeFile, 'write-test', False) then
      exit;
    DeleteFile(ProbeFile);
    Result := True;
  except
    Result := False;
  end;
end;

function IsValidDataDir(const Value: String): Boolean;
var
  DataDir, AppDir: String;
begin
  DataDir := NormalizeDataDir(Value);
  // GetDetectedDataDir is called while the wizard is being initialized.  At
  // that point {app} is not initialized yet, so expanding it raises a runtime
  // error before the user can reach the install directory page.  The default
  // install location is stable and is sufficient for the early safety check;
  // the final component launch still uses {app} after ssPostInstall.
  AppDir := NormalizeDataDir(ExpandConstant('{autopf}\FuXuan'));
  Result := (DataDir <> '') and (not IsDriveRoot(DataDir)) and
    (CompareText(DataDir, AppDir) <> 0) and
    (not IsSameOrChildPath(DataDir, AppDir)) and
    (not IsSameOrChildPath(AppDir, DataDir));
end;

procedure InitializeWizard;
var
  Label1, Label2: TNewStaticText;
begin
  // ── 数据目录页 ──
  DataDirPage := CreateInputDirPage(wpSelectComponents,
    '数据目录', '符玄的忆境/人格/文档等数据存放位置',
    '安装器会优先复用已有数据目录；新安装默认使用当前用户可写目录。' + #13#10 +
    '安装前会检测目录是否可写，写入 FU_XUAN_DATA 环境变量。' + #13#10 +
    '卸载/升级不会删除该目录。',
    False, '');
  DataDirPage.Add('');
  DataDirPage.Values[0] := GetDetectedDataDir;

  // ── API 密钥页 ──
  KeyPage := CreateCustomPage(wpSelectComponents, 'API 密钥', '填写桌宠所需的 API 密钥（写入用户级环境变量，不落盘明文）');
  Label1 := TNewStaticText.Create(KeyPage);
  Label1.Parent := KeyPage.Surface;
  Label1.Caption := 'DeepSeek API Key（对话/动作翻译必需）：';
  Label1.Left := 0; Label1.Top := 8; Label1.Width := KeyPage.SurfaceWidth;

  edDeepSeek := TNewEdit.Create(KeyPage);
  edDeepSeek.Parent := KeyPage.Surface;
  edDeepSeek.Left := 0; edDeepSeek.Top := 30; edDeepSeek.Width := KeyPage.SurfaceWidth;
  edDeepSeek.Text := GetExistingUserEnv('DEEPSEEK_API_KEY');

  Label2 := TNewStaticText.Create(KeyPage);
  Label2.Parent := KeyPage.Surface;
  Label2.Caption := 'GLM API Key（视觉校验/表情验证，可选）：';
  Label2.Left := 0; Label2.Top := 60; Label2.Width := KeyPage.SurfaceWidth;

  edGlm := TNewEdit.Create(KeyPage);
  edGlm.Parent := KeyPage.Surface;
  edGlm.Left := 0; edGlm.Top := 82; edGlm.Width := KeyPage.SurfaceWidth;
  edGlm.Text := GetExistingUserEnv('GLM_API_KEY');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  DataDir: String;
begin
  Result := True;
  if CurPageID = DataDirPage.ID then
  begin
    DataDir := NormalizeDataDir(DataDirPage.Values[0]);
    if not IsValidDataDir(DataDir) then
    begin
      MsgBox('数据目录无效：不能使用磁盘根目录，也不能放在安装目录内。' + #13#10 +
        '请选一个专用的数据目录，例如 %LOCALAPPDATA%\FuXuan\DesktopPetData。', mbError, MB_OK);
      Result := False;
      exit;
    end;
    if not IsWritableDataDir(DataDir) then
    begin
      MsgBox('数据目录不可写：' + #13#10 + DataDir + #13#10 +
        '请确认磁盘已连接、有写入权限，或选择其他目录。', mbError, MB_OK);
      Result := False;
      exit;
    end;
    DataDirPage.Values[0] := DataDir;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PyPath, DataDir: String;
begin
  if CurStep = ssPostInstall then
  begin
    DataDir := NormalizeDataDir(DataDirPage.Values[0]);
    if not ForceDirectories(DataDir) then
    begin
      Log('FuXuan install: data dir creation failed: ' + DataDir);
      MsgBox('无法创建数据目录：' + DataDir + #13#10 +
        '请检查磁盘权限后重新安装。', mbError, MB_OK);
      exit;
    end;
    if not SkipEnv then
    begin
      BridgeToken := GenerateToken;
      GatewayToken := GenerateToken;
      SetUserEnv('FU_XUAN_DATA', DataDir);
      SetUserEnv('BRIDGE_TOKEN', BridgeToken);
      SetUserEnv('OPENCLAW_GATEWAY_TOKEN', GatewayToken);
      SetUserEnv('OFFICE_SCRIPTS_DIR', ExpandConstant('{app}\scripts\office'));
      SetUserEnv('KNOWLEDGE_SCRIPTS_DIR', ExpandConstant('{app}\scripts\knowledge'));
      SetUserEnv('OPENCLAW_NODE_MODULES', ExpandConstant('{app}\bridge\node_modules'));
      PyPath := ExpandConstant('{app}\scripts\python\python.exe');
      if FileExists(PyPath) then
        SetUserEnv('OFFICE_PYTHON', PyPath);
      if Trim(edDeepSeek.Text) <> '' then
        SetUserEnv('DEEPSEEK_API_KEY', Trim(edDeepSeek.Text));
      if Trim(edGlm.Text) <> '' then
        SetUserEnv('GLM_API_KEY', Trim(edGlm.Text));
      SaveStringToFile(ExpandConstant('{app}\.env-written'), '1', False);
    end;

    // 依赖组件由安装器显式等待并检查退出码，避免 [Run] 的隐藏失败被误报为安装成功。
    if (not SkipComps()) and (not RuntimeComponentsAttempted) then
    begin
      RuntimeComponentsAttempted := True;
      if WizardIsComponentSelected('openclaw') then
        RunRuntimeComponent('openclaw', 'install-openclaw.cmd', 'OpenClaw Gateway');
      if WizardIsComponentSelected('ollama') then
        RunRuntimeComponent('ollama', 'install-ollama.cmd', 'Ollama 与本地模型');
    end;
    Log('FuXuan install done. DataDir=' + DataDir);
  end;
end;

function GetDataDirForUninstall: String;
var
  S: String;
begin
  S := GetExistingUserEnv('FU_XUAN_DATA');
  if S = '' then S := ExpandConstant('{#DefaultDataDir}');
  Result := NormalizeDataDir(S);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  EnvWritten: Boolean;
  DataDir: String;
  DataRemoved: Boolean;
begin
  EnvWritten := FileExists(ExpandConstant('{app}\.env-written'));
  if CurUninstallStep = usUninstall then
  begin
    KeepData := True;
    // 静默卸载不弹确认框，默认保留数据，避免因隐藏 MsgBox 造成不确定行为。
    if not UninstallSilent then
      if MsgBox('是否保留数据目录（忆境/人格/文档）？' + #13#10 +
        '选择「是」将原地保留 ' + GetDataDirForUninstall + #13#10 +
        '选择「否」将删除全部数据（不可恢复！）',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDNO then
        KeepData := False;
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := GetDataDirForUninstall;
    DataRemoved := False;
    if not KeepData then
    begin
      // 只删除安装时记录的专用目录，拒绝磁盘根目录和安装目录。
      if IsValidDataDir(DataDir) then
      begin
        DataRemoved := (not DirExists(DataDir)) or DelTree(DataDir, True, True, True);
        if DataRemoved then
          Log('FuXuan uninstall: data dir removed: ' + DataDir)
        else
          Log('FuXuan uninstall: data dir removal failed (may be in use): ' + DataDir);
      end;
      if (not DataRemoved) and (not UninstallSilent) then
        MsgBox('数据目录未能删除（可能仍有桌宠/桥接进程占用）：' + #13#10 +
          DataDir + #13#10 + '为避免丢失入口，已保留 FU_XUAN_DATA 配置。', mbError, MB_OK);
    end;
    if EnvWritten then
    begin
      // 保留数据时保留 FU_XUAN_DATA。自定义目录重装会被自动检测，
      // 不会因环境变量被删而再创建第二份记忆。
      if (not KeepData) and DataRemoved then
        RegDeleteValue(HKCU, 'Environment', 'FU_XUAN_DATA');
      RegDeleteValue(HKCU, 'Environment', 'BRIDGE_TOKEN');
      RegDeleteValue(HKCU, 'Environment', 'OPENCLAW_GATEWAY_TOKEN');
      RegDeleteValue(HKCU, 'Environment', 'OFFICE_SCRIPTS_DIR');
      RegDeleteValue(HKCU, 'Environment', 'KNOWLEDGE_SCRIPTS_DIR');
      RegDeleteValue(HKCU, 'Environment', 'OPENCLAW_NODE_MODULES');
      RegDeleteValue(HKCU, 'Environment', 'OFFICE_PYTHON');
    end;
    // .env-written is created at install time and is not part of the Inno file log.
    // Remove it explicitly, then remove the app directory only if it is empty.
    DeleteFile(ExpandConstant('{app}\\.env-written'));
    RemoveDir(ExpandConstant('{app}'));
  end;
end;

function InitializeSetup(): Boolean;
begin
  SkipEnv := CmdLineParamExists('/SKIPENV');
  Result := True;
end;
