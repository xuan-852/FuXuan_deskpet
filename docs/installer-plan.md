# 安装包与分发方案 — 让符玄在任意 Windows 电脑上跑起来

> **文档作用**: 回答「怎么把符玄桌宠打包成常规软件样式的安装包，在别的电脑上完整安装运行」。给出运行时依赖全景、三个移植障碍、Inno Setup 安装器设计、组件安装流程、验收清单与分期计划。
> **当前状态**: ✅ **阶段 0 已完成（2026-08-14）**——三个移植障碍代码改造全部落地并验证（import 动态解析 / `FU_XUAN_DATA` / requirements.txt / Pogget 路径配置化，见 §三清单）；✅ **阶段 1 便携原型已完成（2026-08-14）**——`installer\build-portable.ps1` 组装便携目录 `installer\portable\`，真机验证通过（便携桥 `/health` 200 且连上 Gateway、`/extract_pdf` 26 页 PDF 提取成功、桌宠从便携目录启动正常零异常）；⚠️ 遗留：Python embeddable 内置待补测（当前回退系统 Python）、阶段 2~4 未开始。
> **关联文档**: [`README.md`](../README.md)（环境依赖/快速上手）、[`code-truth-architecture.md`](code-truth-architecture.md)（六层架构）、[`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md)（功能路线）、[`development-standards.md`](development-standards.md)（通信/密钥规范）

---

## 一、目标与决策

| 决策点 | 结论 |
|---|---|
| 分发形态 | **正式安装包**（类似常规软件）：Inno Setup 生成的 `setup.exe`，含安装向导、快捷方式、卸载器 |
| 部署策略 | **每台目标电脑完整安装**（OpenClaw + Ollama + 全部组件都装本机，不搞远程主控） |
| 数据策略 | 用户数据独立于安装目录：默认 `D:\DesktopPetData`，卸载/升级**不删用户数据** |
| 代码改造 | **本方案只列改造清单，暂不动代码**（阶段 0 统一实施） |

---

## 二、运行时依赖全景（目标机清单）

符玄是**五层运行时**，安装包必须让这五层在目标机全部就绪：

| 层 | 组件 | 版本/内容（本机实测） | 部署方式 |
|---|---|---|---|
| ① 桌宠本体 | `DesktopPet.exe` + `DesktopPet_Data/` | 135.9 MB，154 文件，Live2D 模型已在 `StreamingAssets/` 内 | **打进安装包**，拷到安装目录 |
| ② 桥接服务器 | `openclaw_bridge.js`（Node.js，端口 19876） | 依赖 OpenClaw 的 `gateway-chat-*.js`（版本 2026.7.1-2） | **打进安装包** + Node 运行时（见 §5.2） |
| ③ OpenClaw Gateway | npm 包 `openclaw`，`ws://127.0.0.1:18789` | 2026.7.1-2 | **安装器静默安装**（`npm i -g openclaw`）+ 启动 gateway |
| ④ Python 脚本层 | `scripts/office`、`scripts/knowledge` 等 | Python 3.12 + 7 个包（见 §5.3） | **内置 Python embeddable 打进包** |
| ⑤ 本地 LLM | Ollama + `qwen2.5:3b` + `nomic-embed-text` | 模型约 1.9GB + 0.27GB | **安装器引导安装**（可选组件，见 §5.4） |

**目标机其他前置条件**：

| 依赖 | 必需 | 说明 |
|---|---|---|
| Windows 10/11 x64 | ✅ | 需启用 .NET Framework 4.x（Unity 默认） |
| VC++ 2015-2022 x64 运行库 | ✅ | `TuanjiePlayer.dll` 依赖，安装器检测/静默安装 |
| DeepSeek / GLM API 密钥 | ✅ | 安装向导收集，写用户级环境变量 |
| TeX（xelatex） | ⭕ 可选 | `compile_latex` 用；MiKTeX（小）或 TeX Live（2-4GB），安装器可选组件 |
| Everything（`es.exe`） | ⭕ 可选 | 毫秒级文件搜索，便携版可打进包 |
| Pogget（`d:\pogget\Pogget.exe`） | ⭕ 可选 | 桌面整理工具，路径需配置化（改造 3） |
| Server酱³ | ⭕ 可选 | 三级提醒推送，需用户自备 key |

---

## 三、三个移植障碍（阶段 0 必须改造，否则换机必炸）

> 已逐一在代码中核实（2026-08-14）。

### 障碍 1：桥接 import 硬编码 OpenClaw 绝对路径 🔴

`openclaw_bridge.js` L17：

```js
import { GatewayChatClient } from 'file:///D:/openclaw/node_modules/openclaw/dist/gateway-chat-BW6uyvQL.js';
```

问题：
- 路径写死本机 `D:/openclaw`，目标机 OpenClaw 装哪不知道
- 文件名带**构建哈希**（`BW6uyvQL`）——OpenClaw 升级后哈希变化，桥直接崩
- 桥无 package.json，靠 OpenClaw 的 node_modules 存活

**改造方案（推荐）**：静态 import 改**动态解析**——

```js
const OPENCLAW_LIB = process.env.OPENCLAW_NODE_MODULES
    || resolveOpenClawFromPaths();  // 依次探测：
    // ① 环境变量 OPENCLAW_NODE_MODULES
    // ② 桥同目录 ./node_modules/openclaw
    // ③ `npm root -g`（全局安装位置）+ '/openclaw'
const { GatewayChatClient } = await import(pathToFileURL(join(OPENCLAW_LIB, 'dist', 'gateway-chat-*.js')));
```

哈希文件名用 `glob`/`readdir` 匹配 `gateway-chat-*.js` 兜底，避免版本升级再断。改造后目标机只需设 `OPENCLAW_NODE_MODULES` 或用全局 npm 安装。

### 障碍 2：数据目录硬编码值 🔴

`DataPathConfig.cs` L9：`DataRoot => @"D:\DesktopPetData"`（值硬编码，虽然已集中到一处）。

**改造方案**：支持环境变量覆盖，默认值不变（向后兼容）：

```csharp
public static string DataRoot =>
    Environment.GetEnvironmentVariable("FU_XUAN_DATA") ?? @"D:\DesktopPetData";
```

目标机若 D 盘不存在/无权限，安装器可写 `FU_XUAN_DATA` 指向其他盘。

### 障碍 3：无 requirements.txt + 硬编码 Pogget 路径 🟡

- 本机 Python 依赖只装在 `%LOCALAPPDATA%\Programs\Python\Python312`（实测：openpyxl 3.1.5 / pillow 12.3.0 / pymupdf 1.28.2 / pypdf 6.14.2 / python-docx 1.2.0 / python-pptx 1.0.2 / requests 2.33.0），**仓库无 requirements.txt**
- `RightPanel.cs` L283 硬编码 `d:\pogget\Pogget.exe`

**改造方案**：新增 `scripts/requirements.txt`（锁定上述版本）；Pogget 路径改为环境变量 `POGGET_EXE` 或配置项，默认值保留。

### 改造清单汇总（阶段 0，全部不改变默认行为）

| # | 改动 | 文件 | 验证 |
|---|---|---|---|
| 0.1 | import 动态解析 | `openclaw_bridge.js` | `node --check` + `/health` 实测 |
| 0.2 | `FU_XUAN_DATA` 环境变量覆盖 | `DataPathConfig.cs` | `build.ps1 -Quick` + 测试 |
| 0.3 | `scripts/requirements.txt` | 新文件 | pip install 干净环境可装 |
| 0.4 | Pogget 路径配置化 | `RightPanel.cs` | `build.ps1 -Quick` |

---

## 四、安装包组成（Inno Setup 设计）

### 4.1 安装目录结构

```
C:\Program Files\FuXuan\                  ← 安装目录（用户可选，默认 Program Files）
├── DesktopPet.exe                        ← ① 桌宠本体（含 _Data 文件夹）
├── DesktopPet_Data\
├── bridge\
│   ├── openclaw_bridge.js                ← ② 桥接（阶段 0 改造后）
│   └── node\                             ← 内置 Node 便携运行时（见 §5.2）
├── scripts\
│   ├── office\ latex\ knowledge\ ...     ← ④ Python 脚本层（原样拷贝）
│   └── python\                           ← 内置 Python embeddable（见 §5.3）
├── extras\
│   ├── es.exe                            ← Everything 便携版（可选组件）
│   └── vc_redist.x64.exe                 ← VC++ 运行库（静默安装用）
├── start-bridge.cmd / stop-bridge.cmd    ← 桥启停（服务注册后仍保留，供调试）
├── version.txt                           ← 安装包版本号
└── unins000.exe                          ← Inno Setup 卸载器（自动生成）
```

### 4.2 安装流程（安装器步骤）

```
1. 欢迎页（版本、简介、Live2D 模型版权提示）
2. 许可协议页
3. 组件选择页：
   [必选] 桌宠本体 + 桥 + Python
   [勾选] OpenClaw Gateway（推荐默认勾选）
   [勾选] Ollama + 模型（默认勾选，提示 ~2.2GB 下载）
   [勾选] TeX (MiKTeX)（默认不勾，compile_latex 才需要）
   [勾选] Everything 便携 / Pogget（默认不勾）
4. 安装目录选择（默认 Program Files\FuXuan）
5. 数据目录选择（默认 D:\DesktopPetData，可改 → 写 FU_XUAN_DATA）
6. 配置收集页（首次安装才显示）：
   - DeepSeek API Key（必填）
   - GLM-4V API Key（必填）
   - Server酱³ key（可选）
   - BRIDGE_TOKEN：自动生成 64 字符随机串（无需用户输入）
7. 安装执行：
   a. 拷贝文件
   b. 静默装 VC++ 运行库（如缺）
   c. 内置 Node/Python 解包
   d. 可选组件安装（OpenClaw npm 全局装 + gateway 启动、Ollama 安装 + 模型拉取、MiKTeX 静默）
   e. 写用户级环境变量（setx，见 §6）
   f. 注册桥为 Windows 服务（NSSM）或计划任务（见 §5.5）
   g. 创建桌面/开始菜单快捷方式
8. 完成页：立即启动桌宠 ☑
```

### 4.3 升级与卸载

| 场景 | 行为 |
|---|---|
| 重复安装（升级） | Inno Setup 覆盖安装目录；`AppMutex` 防止桌宠/桥运行中升级（先提示停止服务）；**数据目录不触碰** |
| 版本号 | 与 CHANGELOG 迭代号对齐：`v<N号>.<build>`（如 `v42.1`），写入 `version.txt`；安装器「关于」页显示 |
| 卸载 | 删安装目录 + 移除环境变量 + 停并删桥服务/计划任务；**卸载时询问是否保留 D:\DesktopPetData**（默认保留） |

---

## 五、关键组件实现要点

### 5.1 Inno Setup 选型

- 免费、成熟、支持 x64、静默参数（`/VERYSILENT`）、`[Registry]`/`[Run]`/`[UninstallDelete]` 段
- 环境变量写入：Inno 的 `[Registry]` 写 `HKCU\Environment`（用户级，免管理员）+ `setx` 触发广播（或安装后提示注销重登；README 已注明此坑）
- 组件逻辑（`[Components]`）、任务（`[Tasks]` 自启）原生支持

### 5.2 Node 运行时内置方案

| 方案 | 评价 |
|---|---|
| **官方 Node 便携 zip**（推荐） | 从 `nodejs.org/dist` 取 Windows x64 zip（约 30MB），解包到 `bridge\node\`，`start-bridge.cmd` 用 `bridge\node\node.exe openclaw_bridge.js` 启动；零安装、无 PATH 污染 |
| pkg/nexe 打包桥为单 exe | ⚠️ 桥是 ESM + 动态 `file://` import + 依赖 openclaw dist，pkg 对 ESM 支持差，**不推荐** |
| 要求系统装 Node | 增加用户负担，放弃 |

> 桥的 `OFFICE_SCRIPTS_DIR` / `KNOWLEDGE_SCRIPTS_DIR` / `OFFICE_PYTHON` 环境变量由安装器指向包内绝对路径（桥已支持 ✓）。

### 5.3 Python embeddable 内置方案

1. 下载 Python 3.12 x64 **embeddable zip**（约 11MB，注意 embeddable 无 tkinter/pip）
2. 解包到 `scripts\python\`，`python312._pth` 追加 `Lib\site-packages` 与 `..\office`（或设 `PYTHONPATH`）
3. `pip install --target scripts\python\Lib\site-packages` 安装 7 个包（python-docx / openpyxl / python-pptx / Pillow / PyMuPDF / pypdf / requests），全部有 win_amd64 wheel，embeddable 可跑
4. 安装器设 `OFFICE_PYTHON = scripts\python\python.exe`
5. ⚠️ 备选：若 embeddable 跑 PyMuPDF/PIL 出问题，改静默调用 Python 官方安装器（`python-3.12.x-amd64.exe /quiet InstallAllUsers=0`）再 pip 装——多 ~80MB 但兼容性最稳

### 5.4 OpenClaw 与 Ollama 完整安装（每台全装）

**OpenClaw**（版本对齐本机 2026.7.1-2）：

```
npm install -g openclaw@2026.7.1-2
openclaw gateway start          # 或按官方文档配置 gateway 服务
# GATEWAY_TOKEN 从 %USERPROFILE%\.openclaw\openclaw.json 自动读取（桥已支持 ✓）
```

> ⚠️ 依赖阶段 0 的「障碍 1」改造：桥通过 `OPENCLAW_NODE_MODULES`（`npm root -g`）找到目标机安装，不再写死 `D:/openclaw`。

**Ollama**：

```
安装 ollama-setup.exe（静默 /S）
ollama pull qwen2.5:3b          # ~1.9GB
ollama pull nomic-embed-text    # ~274MB
ollama serve（注册为 Windows 服务自启，官方安装器默认）
```

> 模型体积大：安装器组件页明示下载量；Ollama 模型目录默认 `%USERPROFILE%\.ollama`，也可设 `OLLAMA_MODELS` 到数据盘。

**TeX（可选）**：MiKTeX 静默安装（`basic-miktex-x64.exe --unattended`），首次编译按需装包；`LATEX_COMPILER=xelatex` 桥已支持 ✓。

### 5.5 桥的常驻与守护（替代 PM2）

现状：本机靠 PM2 管理（`openclaw-bridge`），但**仓库无 ecosystem.config.js，PM2 是机器级全局配置**，目标机不能依赖。

| 方案 | 说明 |
|---|---|
| **NSSM 注册 Windows 服务**（推荐） | `nssm install FuXuanBridge "bridge\node\node.exe" "openclaw_bridge.js"` + 服务自启/崩溃重启；卸载时 `nssm remove` |
| 计划任务（登录时启动） | 简单，但崩溃不自动拉起；配「失败后重启」任务设置可缓解 |
| 桌宠侧拉起（Unity 检测桥健康） | 桥生命周期交给桌宠不理想（桌宠退出即断） |

> 服务方式最接近「常规软件」体验：开机自启、崩溃自愈、任务管理器可见。

---

## 六、环境变量规范（安装器写入，用户级）

| 变量 | 值 | 来源 |
|---|---|---|
| `DEEPSEEK_API_KEY` | 用户输入 | 向导收集 |
| `GLM_API_KEY` | 用户输入 | 向导收集 |
| `QWEATHER_API_KEY` | 可选，用户输入 | 向导收集 |
| `BRIDGE_TOKEN` | 随机 64 字符 | 安装器生成（不落盘明文） |
| `GATEWAY_TOKEN` | — | 桥自动从 `~/.openclaw/openclaw.json` 读取（无需写） |
| `FU_XUAN_DATA` | 数据目录（默认 `D:\DesktopPetData`） | 安装器写入 |
| `OFFICE_PYTHON` | `scripts\python\python.exe` | 安装器写入 |
| `OFFICE_SCRIPTS_DIR` / `KNOWLEDGE_SCRIPTS_DIR` | `scripts\office` / `scripts\knowledge` | 安装器写入 |
| `OPENCLAW_NODE_MODULES` | `npm root -g`（openclaw 全局安装位置） | 安装器写入（配合障碍 1 改造） |
| `POGGET_EXE` | 可选 | 安装器写入（配合障碍 3） |

> 密钥只进用户级环境变量（`HKCU\Environment`），**不写注册表明文业务数据、不进日志**；分发时仓库继续用 `.example` 模板。

---

## 七、安全与版权（分发必读）

1. **密钥**：安装向导收集 → 用户级环境变量；安装器日志不得输出密钥；`BRIDGE_TOKEN` 生成后仅告知一次
2. **危险工具边界不放开**：目标机上 `run_command` 白名单、审批流照旧（桥/桌宠代码不变，天然继承）
3. **符玄 Live2D 模型版权** ⚠️：符玄是米哈游《崩坏：星穹铁道》角色，模型随包分发**需确认授权**。若用于公开分发/商用：① 取得授权，或 ② 安装器提供「自定义模型」接口（用户自备模型替换 `StreamingAssets/Live2D/`），发行版默认带占位模型。本方案默认按「自用/小范围授权分发」设计，公开分发前必须解决此点。
4. **远程默认关闭**：桥只监听 `127.0.0.1`（现状），不开放局域网；`profile="user"` 等高危能力默认关（沿 roadmap §四 安全边界）

---

## 八、验收清单（干净 Windows 虚拟机/新电脑）

- [ ] 全新 Win10/11 x64 虚拟机，仅装 Windows，跑 `setup.exe`
- [ ] 安装器完成全部 8 步，无管理员弹窗卡死（VC++ 静默成功）
- [ ] 桥服务已注册且运行，`curl http://127.0.0.1:19876/health` 返回成功
- [ ] 桌宠启动，无 `D:\DesktopPetData` 缺失报错，聊天正常（DeepSeek 生效）
- [ ] `get_system_info` / 天气 / 截图工具可用
- [ ] `generate_ppt`（Python 链路）出文件并自动打开
- [ ] `openclaw_task` 提交一个任务 → 审批弹窗 → 放行 → 返回结果（OpenClaw 链路）
- [ ] `knowledge_index` 索引 PDF（PyMuPDF 链路）
- [ ] `compile_latex`（若装了 TeX）出 PDF
- [ ] 重启电脑：桥服务自启、桌宠（若勾选自启）自启
- [ ] 卸载：安装目录清空、服务移除、环境变量移除、`D:\DesktopPetData` 按选择保留
- [ ] 升级：旧数据目录完好，新版本正常

---

## 九、分期计划与工作量

| 阶段 | 内容 | 估时 | 产出 |
|---|---|---|---|
| **0** | 三个移植障碍代码改造（§三清单）+ `build.ps1 -Quick` + 测试 + 更新架构文档 | 0.5~1 天 | ✅ 2026-08-14 完成（import 动态解析 / `FU_XUAN_DATA` / requirements.txt / Pogget 环境变量；EditMode 78/78 + 冒烟测试通过） |
| **1** | 便携目录原型：内置 Node/Python 解包 + `start-bridge.cmd` + 数据目录 + 环境变量脚本 | 1 天 | ✅ 2026-08-14 完成（`installer\build-portable.ps1` 组装 `installer\portable\`；便携桥 `/health`+`/extract_pdf` 通过、桌宠从便携目录启动零异常；**Node 锁定 v22.22.3+**（OpenClaw 要求 SQLite 3.51.3+，v22.14.0 实测启动报 WAL bug）；⚠️ Python embeddable 内置待补测，当前回退系统 Python） |
| **2** | Inno Setup 安装器：§四全部页面/组件/注册/卸载 | 1~2 天 | `setup.exe` |
| **3** | 组件安装脚本：OpenClaw npm 静默 + Ollama 拉模型 + MiKTeX + VC++ + Everything | 1 天 | 组件自动化 |
| **4** | 虚拟机验收（§八清单）+ 版权/密钥文档 + 发布流程（构建→打包→版本号） | 0.5~1 天 | 发布 v1.0 |
| 合计 | | **4~6 天** | |

**关键路径**：阶段 0（代码）→ 阶段 1（原型验证内置运行时可行性，特别是 Python embeddable 跑 PyMuPDF/PIL）→ 阶段 2（安装器壳）→ 阶段 3（组件自动化）→ 阶段 4（验收）。

---

## 十、风险与对策

| 风险 | 对策 |
|---|---|
| Python embeddable 兼容性（PyMuPDF/PIL 加载失败） | 阶段 1 先做最小验证；失败改官方安装器静默装（§5.3 备选） |
| Node 便携版与 OpenClaw 版本不匹配（哈希文件名） | 阶段 0 障碍 1 用 `gateway-chat-*.js` 通配匹配兜底 |
| Ollama 2.2GB 模型下载中断/超时 | 组件页显示进度；`ollama pull` 可断点续传；失败允许跳过（桌宠降级为云端 LLM 模式） |
| 目标机无 D 盘 | `FU_XUAN_DATA` 指向其他盘（阶段 0 改造 0.2） |
| 卸载误删用户数据 | 卸载器二次确认，默认保留 `D:\DesktopPetData` |
| 符玄模型版权 | §七.3：公开分发前必须解决授权或提供自定义模型接口 |
