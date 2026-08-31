# 太卜司·符玄 — 桌面灵伴 Desktop Pet

<div align="center">

![版本](https://img.shields.io/badge/版本-v1.0.12-blue)
![引擎](https://img.shields.io/badge/引擎-团结引擎%20Tuanjie%202022.3.62t7-purple)
![平台](https://img.shields.io/badge/平台-Windows%2010%2F11%2064位-green)
![Live2D](https://img.shields.io/badge/Live2D-Cubism%205--r.4-orange)
![对话](https://img.shields.io/badge/对话-本地%20Ollama%20优先-red)
![云端](https://img.shields.io/badge/云端-DeepSeek%20可选-orange)
![本地LLM](https://img.shields.io/badge/本地LLM-Ollama%20Qwen3-blue)
![工具](https://img.shields.io/badge/工具-65%20个%20%7C%20危险审批-orange)

**「穷观妙算，天机尽显。」** — 仙舟「罗浮」太卜司之首，符玄大人驾临您的桌面。

一个基于 **Unity + Live2D + Ollama** 的 Windows 桌面 AI 伴侣：能对话、能感知、能执行、能记忆。
默认优先使用本地模型，云端 DeepSeek 作为可选的高质量对话与工具调用通道；
系统围绕 **感知 → 决策 → 执行 → 验证 → 记忆** 构建，并尽量避免不必要的云端消耗。

</div>

## ⬇️ 下载

[![下载最新版](https://img.shields.io/github/v/release/xuan-852/FuXuan_deskpet?label=%E4%B8%8B%E8%BD%BD%E6%9C%80%E6%96%B0%E7%89%88&style=for-the-badge)](https://github.com/xuan-852/FuXuan_deskpet/releases/latest)

[下载 Windows 安装包 FuXuanSetup-1.0.12.exe](https://github.com/xuan-852/FuXuan_deskpet/releases/latest/download/FuXuanSetup-1.0.12.exe)<br>
[备用 ZIP 下载（浏览器拦截 EXE 时使用）](https://github.com/xuan-852/FuXuan_deskpet/releases/latest/download/FuXuanSetup-1.0.12.zip)

当前版本：`v1.0.12` · EXE SHA256：`56FFDF40F96123D6834F2193145F0752E45D93E47D1C6274D2ACB6F96859DFF5`<br>
备用 ZIP SHA256：`8A2F63B89EACBEC86F7B47DE5CDAFDFD44F4D449B9C63C6EAA1855EFBE41636D`

> 首次发布的 Windows EXE 可能被 SmartScreen/浏览器提示“通常不会下载”。请优先从本仓库 Release 下载，并用对应 `.sha256` 文件校验；当前发布包尚未接入商业代码签名证书，正式签名接入后将减少此提示。

也可以打开 [全部版本](https://github.com/xuan-852/FuXuan_deskpet/releases)，在对应版本的 **Assets** 区域下载。

---

## ✨ 特色亮点

| 亮点 | 说明 |
|------|------|
| 🤖 **本地优先的桌面 AI 伴侣** | Ollama 本地聊天 + 可选 DeepSeek Function Calling + **65 个工具**，不只是聊天 |
| 🪄 **本地也能执行术式** | qwen2.5:3b 规划 JSON → 本地白名单 → ToolEngine 执行 → qwen3:8b 根据真实结果回复 |
| 🎭 **Live2D 活灵活现** | Cubism 5-r.4 参数化动画 + 语义化普通空闲动作 + 物理模拟（裙/发/法盘惯性跟随）+ 模型局部 RT + DWM 透明窗口融入桌面 |
| 🧠 **忆境与人格** | 相关性记忆检索、核心事实、对话摘要、五维人格与本地知识库 RAG |
| 🔍 **感知你的一举一动** | 前台窗口、浏览器标签、时间天气、系统性能、GPU 负载、剪贴板 |
| 🏃 **具身动作闭环** | 轻量本地模型决策 → 动作模板/关键帧播放 → 可选视觉验证与运动记忆沉淀 |
| 🖥️ **任务外包执行** | 经本地 Node 桥接调用 OpenClaw 智能体，浏览器/命令行/定时任务全外包，**进度可视化 + 关键步审批** |
| 📋 **办公文档生成** | 一句话生成 PPT / Word / Excel（python-pptx / python-docx / openpyxl） |
| 📜 **LaTeX 编译** | 长文档分块生成（AgentWrite 式），实测 31 页 PDF 一次成功 |
| 🛡️ **安全第一** | 危险工具审批弹窗、exec 敏感命令审批、测试模式隔离、密钥不入库 |
| ⚙️ **模型可切换** | 设置页可选择 qwen3:8b、qwen2.5:3b、qwen2.5:1.5b、qwen2.5:0.5b，并查看当前模型的真实生成示例 |
| 🗃️ **忆境可管理** | 设置页可浏览核心事实与长期记忆，可清理过期记忆或二次确认清空忆境 |
| 🧩 **像素表情包** | 17×24 像素小符玄，9 种表情帧 + 符号徽章 |
| 🎉 **像素节日适配** | 8 个节日主题覆盖像素符玄配饰、聊天窗口/气泡和独立动态背景；新春烟花、元宵灯笼、端午水波、七夕星桥、中秋月兔、万圣南瓜、圣诞飘雪、元旦彩带；Live2D 保持不变 |

---

## 🖼️ 效果展示

| 像素符玄 | 预览网格 |
|---------|---------|
| ![像素符玄](code/desktop_unity/Assets/Resources/PixelFuXuan_48px.png) | ![像素网格](code/desktop_unity/Assets/Resources/PixelFuXuan_17x24_preview.png) |

> 💡 Live2D 模型贴图位于 `code/desktop_unity/Assets/Live2D/Models/Fuxuan/`；
> 运行截图见 `code/desktop_unity/screenshots/`（本地未入库，GitHub 访客请自行构建体验）。

---

## 🧠 系统架构（六层）

```
┌────────────────────────────────────────────────────────────┐
│                     UI 表现层                               │
│  ChatBubble · BallPanel · RightPanel · DebugWindow        │
│  SystemTray · VisualHeartbeat                             │
├────────────────────────────────────────────────────────────┤
│                    AI 核心层                                │
│  ChatManager(本地优先·10轮工具循环·意图过滤·看门狗)        │
│  LocalLLMAgentService(Ollama) · ApiClient(可选云端 SSE)     │
│  ModelSettings · RuntimeReadiness · QualityTelemetry        │
│  IdleChatGenerator · ProactiveMessageScheduler              │
├────────────────────────────────────────────────────────────┤
│                    感知层                                   │
│  ActivityTracker · BrowserTabReader · PetMemory            │
│  TimeWeatherController · PerformanceMonitor · GpuLoad      │
│  KnowledgeBaseManager · MemoryGovernance · ClipboardListener│
├────────────────────────────────────────────────────────────┤
│                    具身层                                   │
│  MotionAgent · MotionTranslator · MotionPlanner            │
│  MotionGenerator · MotionMemoryManager · SafetyValidator   │
│  VisionMotionVerifier(可选 GLM-4V) · PersonalityManager    │
│  EmotionState · IdleActionScheduler                        │
├────────────────────────────────────────────────────────────┤
│                    物理与渲染层                              │
│  DesktopPet (物理) · DragHandler (交互)                     │
│  HybridRenderer → Live2DRenderer (3D 分支规划中)           │
│  Live2DFramework/ (参数映射/物理/分析)                     │
├────────────────────────────────────────────────────────────┤
│                    系统层                                   │
│  WindowOverlay (DWM透明) · SystemTrayManager               │
│  ReminderManager · ServerPollService · OpenClawBridge      │
│  ToolEngine (插件化工具发现/审批 · 65 工具)                │
└────────────────────────────────────────────────────────────┘
```

### 通信架构（C# ↔ Node 桥 ↔ OpenClaw/Python）

```
C# (OpenClawBridge.cs) --HTTP JSON, x-bridge-token--> openclaw_bridge.js (:19876)
                                                        |--WebSocket--> OpenClaw Gateway (:18789)
                                                        |--execSync 临时JSON--> Python 脚本
                                                        |   (PPT/Word/Excel/LaTeX 生成器)
```

- 桥接服务器：Node.js，开发环境可由 PM2 管理，安装包支持本地服务方式运行；鉴权 `x-bridge-token`，**per-session 请求锁**（同会话串行、多任务并行）
- 端点：`/health` `/search` `/task`（提交/轮询/取消/**审批**）`/compile_latex` `/generate_office`
- **exec 审批闭环**（2026-08-12 E2E 实测打通）：`tools.exec.mode=ask` → 敏感命令触发审批事件
  → 桌宠弹窗（60s 倒计时）→ 用户决策（allow-once/allow-always/deny）→ `exec.approval.resolve` 回执 → 命令继续/中止

### 本地优先对话与模型路由

```
用户输入 → ChatManager → RuntimeReadiness 检查
                       ├─ 本地聊天：Ollama / qwen3:8b（可在设置页切换）
                       └─ 云端通道：DeepSeek API（用户配置后按需启用）

动作、意图分类、摘要、记忆提取 → Ollama / qwen2.5:3b
相关忆境 → 当前问题检索 → 本地最多 700 字符 / 云端最多 1400 字符
```

本地聊天和动作模型分开，避免动作循环占用质量模型；Qwen3 默认关闭 thinking，
把等待预算用于完整回复。模型设置页切换的是聊天模型，不会改变动作/摘要模型。

### 具身智能数据流

```
MotionAgent ──→ MotionTranslator ──→ MotionPlanner ──→ MotionGenerator
(感知/决策)    (本地/可选云端)       (10 模板/6 曲线)   (插值播放)
     ↑                                                      │
     │    MotionMemoryManager ←── VisionMotionVerifier ←────┘
     │    (30 条 + 负反馈)      (可选 GLM-4V 多帧拼图评分)
     └────────────────────────────────────────────────────────┘
```

---

## 🚀 快速上手

### 环境依赖

| 依赖 | 必需 | 说明 |
|------|------|------|
| 团结引擎 | ✅ | `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe` |
| Live2D SDK | ✅ | CubismSdkForUnity-5-r.4（模型在 `StreamingAssets/Live2D/Fuxuan/`） |
| Ollama | 推荐 | 默认本地聊天 `qwen3:8b`；动作/分类/摘要使用 `qwen2.5:3b`；知识库可选 `nomic-embed-text` |
| DeepSeek API | 可选 | 云端高质量对话、工具调用与部分复杂动作生成；需要用户自行配置专属 API Key |
| GLM-4V API | 可选 | 截图分析与动作视觉评分；未配置时不影响本地聊天 |
| OpenClaw | ⭕ | 搜索网关 / 任务外包（HTTP 19876 / WebSocket 18789） |
| Everything CLI | ⭕ | 毫秒级文件搜索（自动检测 `es.exe`） |
| Node.js | ⭕ | 桥接服务器；开发环境可配合 PM2，安装包可使用本地服务方式 |

### 环境变量

```powershell
# DeepSeek（可选 — 启用云端对话/工具调用时配置）
setx DEEPSEEK_API_KEY "sk-your-key-here"
# 智谱 GLM（可选 — 启用视觉分析/动作评分时配置）
setx GLM_API_KEY "your-glm-key-here"
# 本地聊天模型（默认 qwen3:8b，可在设置页选择）
setx FU_XUAN_LOCAL_CHAT_MODEL "qwen3:8b"
# 动作/分类/摘要模型（默认 qwen2.5:3b）
setx FU_XUAN_LOCAL_MODEL "qwen2.5:3b"
# 用户数据根目录（默认 %LOCALAPPDATA%\FuXuan\DesktopPetData；安装器会优先复用已有目录）
setx FU_XUAN_DATA "%LOCALAPPDATA%\FuXuan\DesktopPetData"
# 和风天气（可选 — 默认 wttr.in，cityCode=Nanjing）
setx QWEATHER_API_KEY "your-qweather-key-here"
# OpenClaw Bridge 认证（可选 — 搜索/LaTeX/任务外包网关）
setx BRIDGE_TOKEN "your-bridge-token-here"
# 服务端轮询（可选 — 便签同步到本地服务 localhost:3000）
setx DESKTOP_TOKEN "your-server-token-here"
```

> ⚠ 设完后需**重启 VS Code / 重新登录**使环境变量生效。
> 密钥一律环境变量读取（`ChatConfig.cs`，附 `.cs.example` 模板），**不入库**。
> 本地聊天不需要 API Key；选择云端模型前才需要配置 `DEEPSEEK_API_KEY`。
> 环境变量名以代码为准：OpenClaw Bridge 用独立的 `BRIDGE_TOKEN`，不会回退到 `GATEWAY_TOKEN`；
> `DESKTOP_TOKEN` 仅用于 ServerPollService 便签轮询（`localhost:3000`）。

### 构建 & 运行

```powershell
.\build.ps1              # 完整构建 → Build/DesktopPet.exe
.\build.ps1 -Quick       # 仅编译验证（快速）
.\build.ps1 -RunTests    # 运行 Editor 测试套件（EditMode 78+ 个）
.\Build\DesktopPet.exe   # 启动桌面宠物
```

> 构建 Tuanjie/Unity 需要本机 full-access 权限；受限沙箱中可能卡在授权或原生子进程阶段。以本次新日志中的 `[OK] Build succeeded!` 判断，不要把无输出直接当成 C# 编译失败。

> 首次运行后可从系统托盘右键菜单启用开机自启。ESC 隐藏到托盘。

---

## 🤖 核心能力

### 💬 AI 对话系统（本地优先，云端可选）

日常聊天默认走本地 **Ollama**，聊天模型与动作/分类/摘要模型分离：聊天默认 `qwen3:8b`，
动作、意图分类、摘要和记忆提取默认使用轻量 `qwen2.5:3b`。用户可在设置页切换聊天模型，
并查看当前模型的真实生成示例。配置 DeepSeek API Key 后，才启用云端高质量对话与工具调用。

云端通道通过 `ToolEngine/IPetTool` 插件接口自动发现并注册 **65 个工具**，支持最多 **10 轮** 回环调用；
本地聊天不会因为没有云端 Key 而失效。

| 类别 | 工具（真实注册名） | 能力 |
|------|------|------|
| 🔭 观星术 | `search_web` `search` `openclaw_search` | 联网搜索（OpenClaw Bridge / 传统搜索） |
| 📷 摄形术 | `take_screenshot` | 截图 + GLM-4V 视觉分析 |
| 🎵 调音术 | `set_volume` `mute` | 系统音量调节 |
| 🔒 封印术 | `lock_screen` `power` | 锁屏/关机/重启/睡眠（需用户确认） |
| 📢 传音术 | `notify` `get_clipboard` `set_clipboard` | 推送 + 剪贴板 |
| 🔍 洞观术 | `get_system_info` `get_mouse_pos` `list_files` `search_files` `search_file` `file_open` `file_move` `file_copy` `file_delete` `file_rename` `file_info` `file_create` `dir_create` `file_read` | 系统状态 + Everything 毫秒级搜索 + 文件操作 |
| 🚀 开阵术 | `open_app` `open_folder` `open_url` | 启动应用、打开文件夹/网址 |
| 🏃 行动术 | `launch_pogget` `pogget_agent` | 启动 Pogget / 与 Pogget Agent IPC（8 子命令） |
| 📋 卜算记事簿 | `set_reminder` `query_reminders` `mark_reminder_done` `delete_reminder` | 便签增删改查（重复规则 + 三级推送） |
| 📚 卜算传讯 | `query_exams` `query_scores` `query_schedule` `query_user_status` | 课表、成绩、考试、学业概览查询 |
| 📜 问天录 | `compile_latex` | LaTeX 编译（经 OpenClawBridge，分块生成） |
| 📊 办公生成 | `generate_ppt` `generate_docx` `generate_xlsx` | 一句话生成办公文档（python-pptx/docx/openpyxl） |
| 🎭 演武术式 | `play_action` `stop_action` `generate_motion` `set_expression` `control_body` | 预设/LLM 生成动作 + 表情/身体控制 |
| 🧠 忆境术 | `inspect_personality` `inspect_motion_memory` | 人格自省 + 演武心经查看 |
| 📚 藏书阁 | `knowledge_search` `knowledge_index` | 本地 RAG 语义检索 |
| 🕵️ 自省术 | `explore_body` `explore_body_vision` `self_review` `run_verification` `vis_verify` | 参数探索 + 视觉验证 |
| 🧭 心之所向 | `set_preference` `query_preferences` `remove_preference` | 偏好结构化存储（P4） |
| 📌 太卜阵法图 | `query_task_templates` `save_task_template` `remove_task_template` | 任务模板库 CRUD（P5，30 条上限） |
| 🖥️ 太卜神行法 | `openclaw_task` | 复杂多步任务外包给 OpenClaw（浏览器/命令），支持 template/template_args + 轨迹沉淀 |
| 🌤️ 观天气 | `get_weather` | 天气查询（wttr.in 默认 / QWeather 可选） |
| 💬 言出法随 | `【表情:开心】` / `【动作:伸懒腰】` | 回复文本内嵌动作标记 → Live2D 脸部表情 + 像素画同步表情 |
| 🚫 颜文字禁绝 | SystemPrompt 明令禁止 + 代码兜底 | 模型若输出颜文字 → 自动翻译为表情动作并从文本剥离 |

**核心机制**：
- **意图过滤**：本地 Ollama 分类（chat/emotion/command/knowledge/operation），chat/emotion 不发工具，其余按白名单注入子集（首轮 65→约 27）
- **模型分层**：聊天模型单独保留质量预算；动作/分类/摘要使用轻量模型，避免动作循环拖慢输入响应
- **本地质量护栏**：短角色卡、相关忆境、最近两轮历史、回复后处理和确定性句数约束共同控制低能力模型的输出
- **本地工具路由**：普通闲聊不额外规划；明确操作时只注入相关工具目录，危险术式仍走统一确认，不把 65 个 schema 全塞给本地模型
- **Token 优化（N40 T1-T8）**：时间戳挪尾部（缓存命中率 98.6%）、body schema 裁剪、max_tokens:1200+thinking 禁用、历史 15000 字符预算 + Ollama 摘要、SystemPrompt -41%、Speculative Multi-Action（一次多 tool_call）
- **危险工具审批**：10 个危险工具（含 file_read/get_clipboard/take_screenshot，以及 file_delete/power/lock_screen/run_command/set_volume/mute/openclaw_task）走 `ToolConfirmManager` 确认
- **看门狗**：单次请求总超时 600s；任务外包有心跳熔断（无进展自动取消）+ 成本熔断（不可重试错误禁重复调用）
- **自动重试**：最多 3 次，400/401/403 不重试
- **打字机逐句显示**（2.5s 间隔）、**消息队列**（等待时不丢输入）
- **运行状态**：标题栏显示本地生成、云端连接、流式输出、工具执行、重试、错误和取消状态；可取消卡住的请求
- **成本保护**：本地模式不发云端请求；测试模式与 `--no-cloud` 可在请求前阻断云端调用

### 🎨 Live2D 渲染

| 特性 | 说明 |
|------|------|
| **Cubism SDK 5-r.4** | 参数化变形 + 实时物理模拟（裙/发/法盘惯性跟随） |
| **DWM 透明窗口** | `DwmExtendFrameIntoClientArea` 无缝融合桌面，无绿边 |
| **Perlin 噪声微动** | 8 通道独立噪声：呼吸/身体晃(3)/头部/眼球(2) |
| **参数映射** | 80+ 参数，按部位分组 → 语义名双向映射 |
| **局部渲染** | 模型包围盒驱动局部 RenderTexture + 正交叠加相机；透明窗口仍保留全屏交互层，主相机不兼容时回退全屏 RT |
| **天气↔表情联动** | ☀️微笑 / 🌧委屈 / ❄️好奇 / 🌙垂眼 |

### 🏃 物理与交互

| 特性 | 细节 |
|------|------|
| **桌面物理** | 重力加速度、碰撞弹跳衰减、摩擦阻尼 |
| **拖拽抛掷** | 5 帧速度缓冲平均 → 目标点式物理输入 → 物理抛掷 + 随拖拽强度渐进的挣扎动画 |
| **AI 调试输入** | 测试模式通过 `inbox.txt` 的 `@@sim:*`/`@@input:*` 模拟点击、拖动、状态查询，并用 `@@sim:screenshot` 保存 Unity 当前帧 |
| **分区点击** | 🐱头=摸头眯眼 / 👗身=戳胸惊讶 / 🦵腿=害羞踢腿 |
| **鼠标跟随** | 眼球 150px 触发平滑追踪 |
| **屏幕碰撞** | 边缘撞墙动画 + 反弹物理 |
| **地面状态机** | 5 种行为：左/右行走（边缘/定时）、停留，加权随机切换 |
| **多显示器** | ✅ `WindowOverlay.isMultiMonitor` 可检测虚拟桌面范围；跨屏行走策略仍为规划中能力 |

### 🎭 动作系统 — ActionAgent（15 文件）

- **MotionAgent 自主决策**：4 档密度（High/Med/Low/Sleep），本地 Ollama `qwen2.5:3b` 决策，连续 3 次失败回退概率模式；GPU 监控检测游戏自动暂停
- **MotionTranslator**：自然语言 → Live2D 关键帧序列（本地/可选云端），10 条规则 + 10 种特殊模式，VERIFIED FEEDBACK 注入
- **MotionPlanner/Generator**：10 种硬编码模板 + 6 种插值曲线（Linear/Smooth/EaseOut/EaseIn/Hold/Bounce）+ 3 阶段计划（淡入/保持/回归）
- **闭环视觉验证**：GLM-4V 对动作 20/40/60/80% 截图合成 2×2 拼图评分（1-5 分，passThreshold=3）
- **MotionMemoryManager**：上限 30 条，负反馈反例（≤2 分），无望检测，冷却 120s 防复读
- **SafetyValidator**：互斥组（2 组）/ 对称对（5 对）/ 极端值 3 类保护

### 🧠 感知与记忆

| 系统 | 方式 | 用途 |
|------|------|------|
| 法眼 | 2s 轮询前台窗口 | 8 类分类：编程/游戏/学习/浏览/娱乐/通讯/空闲/其他 |
| 多窗口 | EnumWindows 扫描 | 环境感知（注入 prompt） |
| 浏览器标签 | UIA 反射 | 读取 6 种浏览器标签 |
| 忆境记忆 | 分层 JSON 持久化 | `entries`、`coreFacts`、`conversationSummary`；写入经过重要度/置信度/相关性治理 |
| 知识库 | Ollama 嵌入语义检索 | `nomic-embed-text` + 余弦 TopK，25+ 文件类型分块索引 |
| 人格 | 五维 + 三维关系 | 跨会话连续演化 |
| 记忆注入 | 本地与云端共用 | 按当前问题检索；本地最多 700 字符，云端最多 1400 字符，避免无关记忆污染回复 |
| 反思 | ✅ 已接线 | CheckReflection → DoReflection（按可用模型提炼）→ CommitReflection |

所有用户数据默认位于 `%LOCALAPPDATA%\FuXuan\DesktopPetData`，可由 `FU_XUAN_DATA` 覆盖；安装器会优先复用已有的 C/D 盘旧目录。安装、升级和卸载不会默认删除忆境；
卸载时选择“保留”会原地保留同一个数据目录，选择“删除”才清理已记录的专用目录，避免生成第二份活动数据。

### 🧬 人格演化系统

| 维度 | 范围 | 描述 | 正触发 |
|------|------|------|--------|
| **diligence** | 0~1 | 勤勉 vs 慵懒 | 工作/学习/编码 |
| **warmth** | 0~1 | 温暖 vs 高冷 | 用户感谢/称赞 |
| **playfulness** | 0~1 | 活泼 vs 稳重 | 游戏/表情/语气词 |
| **confidence** | 0~1 | 自信 vs 谦逊 | 工具调用成功 |
| **curiosity** | 0~1 | 求知 vs 淡然 | 搜索/提问 |

**初始值**：勤奋0.5/温暖0.6/活泼0.5/自信0.5/好奇0.6；**学习率** 0.01；**三维关系**：信任/亲密/熟悉度
**人格↔情绪联动**：五维 × 权重 → EmotionState 四维基线偏移；**持久化**：`pet_personality.json`

### 📋 便签与提醒

- 本地 JSON 持久化，优先级：low / normal / high；重复规则：一次性/每日/工作日/每周
- **三级推送**：气泡 → Windows Toast（AppId '符玄'）→ Server酱³ 手机
- 服务端同步（轮询 localhost:3000）+ AI 语音创建（"提醒我…"）+ 去重机制
- 考试提醒：前 3 天 19:00 + 考前 2h

### 🖥️ 交互界面

| 元素 | 触发 | 功能 |
|------|------|------|
| 🟣 **悬浮球 BallPanel** | 右下角粉✦ | 420×580px 辐射菜单（设置/报告/便签） |
| 📋 **右侧面板 RightPanel** | `~`键 / 鼠标划过右边缘 | 220px Widgets（聊/设/签/告）+ **任务进度 + 审批弹窗（60s 倒计时）** |
| 💬 **古风气泡 ChatBubble** | AI 回复/提醒/闲话 | OnGUI 手绘圆角 + 12 星点 |
| ⚙️ **模型设置页** | 右侧面板「设」 | 切换本地聊天模型、查看对应真实样例；云端选项仅在安全配置完成后启用 |
| 🧠 **忆境页** | 右侧面板「忆境」 | 只读查看核心事实/长期记忆、清理过期记忆、二次确认清空 |
| 🧩 **像素表情包**（N41） | AI 回复 `【表情:xxx】` | 17×24 像素形象 → ×4 显示，9 种脸部表情帧，呼吸浮动 + 点击/回复跳跃 + 眨眼，右上角符号徽章（4 秒） |

### ⚡ 性能优化

| 档位 | FPS | RT 比例 | 触发条件 |
|------|-----|---------|---------|
| High | 60 | 100% | 正常 |
| Normal | 40 | 75% | 中等负载 |
| Low | 20 | 50% | CPU/GPU > 92% / 内存 > 93% |

- 升档拦截：CPU/GPU > 80% 阻止升档；内存管理：800MB GC / 1.2GB 强制 GC
- GpuLoadMonitor：检测游戏时暂停 LLM 动作决策，退出后 30s 冷却恢复

---

## 📁 项目结构

```
Desktop_per_pro/
├── code/desktop_unity/          # Unity/Tuanjie 工程
│   ├── Assets/Scripts/
│   │   ├── ChatManager.cs       # 本地优先对话核心（意图过滤/记忆/云端回退）
│   │   ├── ToolEngine/          # 65 工具插件架构（自动发现 + 审批）
│   │   ├── Live2DFramework/ActionAgent/ # 具身动作闭环
│   │   ├── LocalToolRouter.cs   # 本地模型 JSON 工具规划/白名单
│   │   ├── MemoryGovernance.cs  # 忆境写入/容量/相关性治理
│   │   ├── LocalChatModelProfiles.cs # 聊天模型档位与预算
│   │   ├── Live2DFramework/     # Live2D 渲染/参数映射
│   │   └── ...（感知/记忆/UI/物理 各子系统）
│   └── openclaw_bridge.js       # Node.js 桥接服务器（:19876）
├── installer/                   # Inno Setup 安装包与组件脚本
│   ├── dist/FuXuanSetup-1.0.12.exe # 当前发布安装包（GitHub Release Assets）
│   └── dist/FuXuanSetup-1.0.12.zip # EXE 备用下载包
├── scripts/office/              # Python 办公生成器（PPT/Word/Excel）
├── docs/                        # 权威文档（架构/规范/模块/路线图）
│   ├── code-truth-architecture.md   # 代码真相架构审计
│   ├── development-standards.md     # 开发规范（唯一权威）
│   ├── desktop-assistant-roadmap.md # 演进路线图
│   └── modules/                     # 10 份模块文档（四要素模板）
├── tools/                       # 诊断/测试/清理脚本
├── build.ps1                    # 构建脚本（Quick/Full/Tests）
├── AGENTS.md                    # AI 协作快速入口（7 条铁律）
└── CHANGELOG.md                 # 版本迭代日志
```

---

## 🧭 文档导航

| 文档 | 用途 |
|------|------|
| [`docs/code-truth-architecture.md`](docs/code-truth-architecture.md) | 代码真相架构审计（六层架构 + 工具清单） |
| [`docs/development-standards.md`](docs/development-standards.md) | 开发规范（9 章，AI + 人类通用） |
| [`docs/quality-measurement-test-guide.md`](docs/quality-measurement-test-guide.md) | 编译产物核验与 Ollama 本地质量采样说明 |
| [`docs/quality-comparison-test-guide.md`](docs/quality-comparison-test-guide.md) | 本地 / 云端配对质量对照说明 |
| [`docs/desktop-assistant-roadmap.md`](docs/desktop-assistant-roadmap.md) | 演进路线图（v0.3，含开源调研） |
| [`docs/README.md`](docs/README.md) | 文档总索引（10 模块四要素文档） |
| [`AGENTS.md`](AGENTS.md) | AI 协作快速入口 |
| [`CHANGELOG.md`](CHANGELOG.md) | 版本迭代日志 |

---

## ⚠️ 当前限制与注意事项

详见 [`docs/code-truth-architecture.md`](docs/code-truth-architecture.md) 与 [`docs/modules/tool-engine.md`](docs/modules/tool-engine.md)：

1. 云端 API Key 目前只应通过安全配置提供；项目不把完整 Key 写入仓库、日志或公开示例，仅记录用于归属核对的短 `key_id`/哈希
2. `qwen3:8b` 是质量优先的本地聊天模型，低端设备可切换到更小模型，但回复质量和长度会下降
3. 动作/分类/摘要仍使用轻量本地模型，避免动作循环抢占聊天模型资源
4. `isMultiMonitor` 已能检测虚拟桌面范围，但跨屏行走策略仍是后续能力；3D 渲染分支也未作为当前版本功能承诺
5. 此前约 ¥5/天异常已确认来自其他项目复用同一个用户级 `DEEPSEEK_API_KEY`，不归因于桌宠；运行期间仍应保留 `usage_log.jsonl`，并为不同项目使用独立 Key
6. 开源分发前仍需确认符玄 Live2D 模型的授权范围

---

## 📜 版本历史

完整历史见 [`CHANGELOG.md`](CHANGELOG.md)。当前发布版本 **v1.0.12**：

| 版本 | 日期 | 亮点 |
|------|------|------|
| **v1.0.8** | 2026-08-21 | 本地优先聊天模型路由与模型设置页；忆境治理与只读管理页；统一用户数据目录；安装/升级/卸载保留与删除逻辑；发布 Windows 安装包 |
| **v1.0.9** | 2026-08-22 | 本地模型工具规划与安全执行链路；自然语言触发系统信息、文件搜索、文件夹、剪贴板和办公工具；规划 JSON 容错与本地隔离验收 |
| **v1.0.12** | 2026-08-22 | Ollama 下载、安装、API 等待与模型拉取增加超时、独立日志和明确失败返回；安装器增加跨盘数据目录检测、可写性预检与旧目录复用 |
| **2026-08-12** | — | 任务可视化（进度/审批弹窗）+ **exec 审批 E2E 打通** + 多任务并行（per-session 锁）+ 任务模板库/轨迹沉淀（65 工具）+ 办公文档生成 + 偏好系统 |
| **N41** | 2026-08-09 | 像素表情包（9 种脸部表情帧）+ 颜文字禁绝（SystemPrompt + 代码兜底翻译为表情动作） |
| N40 | 2026-08-05~08 | 安全加固（危险工具审批/Bridge 鉴权）+ Token 优化 T1-T8（缓存命中率 98.6%、成本降 60%） |
| N39 | 2026-08-02 | 9 个代码真相 Bug 修复 + 结构偏差修正 |
| N37 | 2026-07-26 | 言出法随（文本内嵌动作标记）、ToolEngine 插件化、OpenClaw Bridge 搜索网关、Pogget 集成 |
| N36 | 2026-07 | ToolCallInvoker 4065 行 → 插件化 ToolEngine，动态工具发现/注册 |
| N35 | 2026-07 | 移除 Qwen-VL，GLM-4V 多帧拼图视觉验证定型 |
| N32~N34 | 2026-07 | MotionVerifier + 人格系统 V3 + 闭环视觉验证 + SPECIAL PATTERNS 优化 |
| N16~N18 | 2026-06 | Everything 毫秒级搜索 + 提醒去重 + 已完成任务视图 |
| v0.2 | 2026-05 | 初始 Unity 版本 |

---

## 🗺️ 当前路线图

- ✅ 本地优先聊天：qwen3:8b 质量档与 qwen2.5 系列低占用档位
- ✅ 模型设置页：按模型展示真实生成样例，不复用其他模型的假样例
- ✅ 忆境治理：重要度/置信度/相关性闸门、容量控制、按问题选择性注入、忆境只读管理页
- ✅ v1.0.12 安装与分发：跨盘数据目录检测、可写性预检、旧目录复用、卸载保留/删除选择、Ollama 依赖超时保护、EXE/ZIP Release 安装包与 SHA256 校验
- 🔜 真实运行采样：对话质量、延迟、GPU/CPU 占用与动作状态切换
- 🔜 多屏行走、3D 渲染模式、任务模板可视化画布

---

## 🤝 贡献与规范

- **开发规范**：见 [`docs/development-standards.md`](docs/development-standards.md)（唯一权威，9 章）
- **AI 协作**：见 [`AGENTS.md`](AGENTS.md)（7 条铁律：测试模式/禁空参遍历/密钥不入库/PM2 管理…）
- **提交规范**：Conventional Commits（`<type>(<scope>): <中文描述>`）
- **文档铁则**：写完代码必须更新对应模块文档，且**测试通过后再写**（文档只记录已验证的代码真相）

---

## ⚖️ 许可证 & 致谢

**许可证：** 本项目为个人学习与娱乐用途，角色「符玄」版权属于 miHoYo / HoYoverse。代码部分参考 Apache License 2.0 精神开源。

**致谢：** [Live2D Cubism](https://www.live2d.com/) · [DeepSeek](https://platform.deepseek.com/) · [智谱 GLM](https://open.bigmodel.cn/) · [Ollama](https://ollama.com/) · [和风天气](https://www.qweather.com/) · [wttr.in](https://wttr.in/) · [Server酱³](https://sc3.ft07.com/) · [Everything](https://www.voidtools.com/) · [OpenClaw](https://github.com/open-claw/open-claw) · 符玄 Live2D 模型（by 琉璃奈希）

---

<div align="center">

**「卜卦问天，不如观心。」**

⭐ 如果符玄大人合您心意，欢迎 Star 支持！

</div>
> 2026-08-30：安装包/OpenClaw 桥接链路已完成一轮稳定性迭代：内置包路径可配置、服务注册失败可见、升级自动刷新、桥接 `/health` 可诊断，安装器编译与静默安装/卸载验收通过。详见 [`docs/installer-plan.md`](docs/installer-plan.md)。
> 2026-08-30：继续完成安装器安全收敛：Bridge/Gateway 令牌禁止复用，桥接服务固定使用内置 Node.js，Ollama/MiKTeX/VC++ 外部安装器执行前强制验证 Authenticode；服务最小权限、发布签名和依赖哈希仍待目标机/发布流程验收。
