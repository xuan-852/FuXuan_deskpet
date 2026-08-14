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
CloseApplications=no

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
  DataDirPage.Values[0] := GetExistingUserEnv('FU_XUAN_DATA');
  if DataDirPage.Values[0] = '' then
    DataDirPage.Values[0] := 'D:\DesktopPetData';

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

procedure CurStepChanged(CurStep: TSetupStep);
var
  PyPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    CreateDir(DataDirPage.Values[0]);
    if not SkipEnv then
    begin
      BridgeToken := GenerateToken;
      SetUserEnv('FU_XUAN_DATA', DataDirPage.Values[0]);
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
    Log('FuXuan install done. DataDir=' + DataDirPage.Values[0]);
  end;
end;

function GetDataDirForUninstall: String;
var
  S: String;
begin
  S := GetExistingUserEnv('FU_XUAN_DATA');
  if S = '' then S := 'D:\DesktopPetData';
  Result := S;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  EnvWritten: Boolean;
  DataDir: String;
begin
  EnvWritten := FileExists(ExpandConstant('{app}\.env-written'));
  if CurUninstallStep = usUninstall then
  begin
    KeepData := True; // 默认保留；仅用户明确选「否」才删除（静默卸载 = 保留）
    if MsgBox('是否保留数据目录（忆境/人格/文档）？' + #13#10 +
      '选择「是」将保留 ' + GetDataDirForUninstall + #13#10 +
      '选择「否」将删除全部数据（不可恢复！）',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDNO then
      KeepData := False;
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    if not KeepData then
    begin
      // ★ 用户明确选择删除：删数据目录（带安全护栏，防误删系统/安装目录）
      DataDir := GetDataDirForUninstall;
      if (DataDir <> '') and (DataDir <> ExpandConstant('{app}')) and
         (DataDir <> 'C:\') and (DataDir <> 'D:\') and
         (CompareText(DataDir, 'C:\Windows') <> 0) then
      begin
        if DelTree(DataDir, True, True, True) then
          Log('FuXuan uninstall: data dir removed: ' + DataDir)
        else
          Log('FuXuan uninstall: data dir removal failed (may be in use): ' + DataDir);
      end;
    end;
    if EnvWritten then
    begin
      RegDeleteValue(HKCU, 'Environment', 'FU_XUAN_DATA');
      RegDeleteValue(HKCU, 'Environment', 'BRIDGE_TOKEN');
      RegDeleteValue(HKCU, 'Environment', 'OFFICE_SCRIPTS_DIR');
      RegDeleteValue(HKCU, 'Environment', 'KNOWLEDGE_SCRIPTS_DIR');
      RegDeleteValue(HKCU, 'Environment', 'OPENCLAW_NODE_MODULES');
      RegDeleteValue(HKCU, 'Environment', 'OFFICE_PYTHON');
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  SkipEnv := CmdLineParamExists('/SKIPENV');
  Result := True;
end;
