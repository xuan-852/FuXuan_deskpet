; ============================================================
; 符玄桌宠 FuXuan Desktop Pet — Inno Setup 安装器（阶段2）
; 方案依据: docs/installer-plan.md §四（安装目录结构/流程/升级卸载）
; 编译: installer\build-installer.ps1（自动获取 ISCC 并传版本参数）
; 测试模式: /DPrivileges=lowest 编译后 setup.exe /VERYSILENT /SKIPENV /DIR=... 可本地静默验证
; ============================================================
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
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
#define DefaultDataDir "D:\DesktopPetData"
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
Name: "openclaw"; Description: "OpenClaw Gateway 说明（桥接需网关运行在 127.0.0.1:18789；包已内置，网关服务见阶段3）"; Types: full compact
Name: "ollama"; Description: "Ollama 本地模型 qwen2.5:3b（约 2.2GB，可选，建议勾选）"; Types: full; ExtraDiskSpaceRequired: 2400000000
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
Name: "{group}\卸载符玄桌宠"; Filename: "{uninstallexe}"; Components: core
Name: "{autodesktop}\符玄桌宠"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Components: core

[Run]
; ── 阶段3 组件自动化（可按组件条件运行；/SKIPCOMPONENTS 时全部跳过，用于本地测试）──
Filename: "{app}\extras\components\install-vcredist.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: core; Check: not SkipComps(); StatusMsg: "安装 VC++ 运行库..."
Filename: "{app}\extras\components\install-openclaw.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: openclaw; Check: not SkipComps(); StatusMsg: "配置 OpenClaw Gateway..."
Filename: "{app}\extras\components\install-ollama.cmd"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: ollama; Check: not SkipComps(); StatusMsg: "安装 Ollama 并拉取模型（可能数 GB，可跳过）..."
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

function GetDetectedDataDir: String;
var
  Configured, DefaultDir: String;
begin
  Configured := NormalizeDataDir(GetExistingUserEnv('FU_XUAN_DATA'));
  DefaultDir := NormalizeDataDir('{#DefaultDataDir}');
  if (Configured <> '') and DirExists(Configured) then
  begin
    Result := Configured;
    exit;
  end;
  if DirExists(DefaultDir) then
  begin
    Result := DefaultDir;
    exit;
  end;
  Result := DefaultDir;
end;

function IsValidDataDir(const Value: String): Boolean;
var
  DataDir, AppDir: String;
begin
  DataDir := NormalizeDataDir(Value);
  AppDir := NormalizeDataDir(ExpandConstant('{app}'));
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
    '默认 D:\DesktopPetData。目标机无 D 盘时请改为其他盘（写入 FU_XUAN_DATA 环境变量）。' + #13#10 +
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
        '请选一个专用的数据目录，例如 D:\DesktopPetData。', mbError, MB_OK);
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
      SetUserEnv('FU_XUAN_DATA', DataDir);
      SetUserEnv('BRIDGE_TOKEN', BridgeToken);
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
    Log('FuXuan install done. DataDir=' + DataDir);
  end;
end;

function GetDataDirForUninstall: String;
var
  S: String;
begin
  S := GetExistingUserEnv('FU_XUAN_DATA');
  if S = '' then S := '{#DefaultDataDir}';
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
      // 不会因环境变量被删而在 D:\DesktopPetData 再创建第二份记忆。
      if (not KeepData) and DataRemoved then
        RegDeleteValue(HKCU, 'Environment', 'FU_XUAN_DATA');
      RegDeleteValue(HKCU, 'Environment', 'BRIDGE_TOKEN');
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
