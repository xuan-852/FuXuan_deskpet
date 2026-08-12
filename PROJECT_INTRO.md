# 太卜司·符玄 — 桌面灵伴 Desktop Pet

<div align="center">

![版本](https://img.shields.io/badge/迭代-N41+-blue)
![引擎](https://img.shields.io/badge/引擎-团结引擎%20Tuanjie%202022.3.62t7-purple)
![平台](https://img.shields.io/badge/平台-Windows%2010%2F11%2064位-green)
![Live2D](https://img.shields.io/badge/Live2D-Cubism%205--r.4-orange)
![对话](https://img.shields.io/badge/对话-DeepSeek%20Chat%20%2B%20Function%20Calling-red)
![视觉](https://img.shields.io/badge/视觉-GLM--4V%20自评闭环-yellow)
![本地LLM](https://img.shields.io/badge/本地LLM-Ollama%20Qwen2.5-blue)
![工具](https://img.shields.io/badge/工具-65%20个%20%7C%20危险审批-orange)

**「穷观妙算，天机尽显。」** — 仙舟「罗浮」太卜司之首，符玄大人驾临您的桌面。

一个基于 **Unity + Live2D** 的 Windows 桌面 AI 伴侣：能对话、能感知、能执行、能记忆，
具备完整的 **感知 → 决策 → 执行 → 验证 → 记忆** 具身智能闭环。

</div>

---

## ✨ 特色亮点

| 亮点 | 说明 |
|------|------|
| 🤖 **真正的桌面 AI 伴侣** | DeepSeek Function Calling + **65 个工具** + 最多 10 轮工具回环，不只是聊天 |
| 🎭 **Live2D 活灵活现** | Cubism 5-r.4 参数化动画 + 物理模拟（裙/发/法盘惯性跟随）+ DWM 透明窗口融入桌面 |
| 🧠 **三层记忆 + 五维人格** | 长期记忆、情感演化、知识库 RAG，符玄会"记得"你 |
| 🔍 **感知你的一举一动** | 前台窗口、浏览器标签、时间天气、系统性能、GPU 负载、剪贴板 |
| 🏃 **具身动作闭环** | LLM 生成动作关键帧 → 插值播放 → GLM-4V 视觉自评 → 记忆沉淀 |
| 🖥️ **任务外包执行** | 经本地 Node 桥接调用 OpenClaw 智能体，浏览器/命令行/定时任务全外包 |
| 📋 **办公文档生成** | 一句话生成 PPT / Word / Excel（python-pptx / python-docx / openpyxl） |
| 📜 **LaTeX 编译** | 长文档分块生成（AgentWrite 式），实测 31 页 PDF 一次成功 |
| 🛡️ **安全第一** | 危险工具审批弹窗（60s 倒计时）、敏感命令需确认、密钥不入库 |
| 🧩 **像素表情包** | 17×24 像素小符玄，9 种表情帧 + 符号徽章 |

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
│  ChatManager(10轮工具循环·意图过滤·600s看门狗)             │
│  ApiClient(SSE) · LocalLLMAgentService(4离线能力)          │
│  IdleChatGenerator · ProactiveMessageScheduler            │
├────────────────────────────────────────────────────────────┤
│                    感知层                                   │
│  ActivityTracker · BrowserTabReader · PetMemory            │
│  TimeWeatherController · PerformanceMonitor · GpuLoad      │
│  KnowledgeBaseManager · ClipboardListener                  │
├────────────────────────────────────────────────────────────┤
│                    具身层                                   │
│  MotionAgent · MotionTranslator · MotionPlanner            │
│  MotionGenerator · MotionMemoryManager · SafetyValidator   │
│  VisionMotionVerifier(GLm-4V) · PersonalityManager         │
│  EmotionState · IdleActionScheduler                        │
├────────────────────────────────────────────────────────────┤
│                    物理与渲染层                              │
│  DesktopPet (物理) · DragHandler (交互)                     │
│  HybridRenderer → Live2DRenderer (3D 分支规划中)           │
│  Live2DFramework/ (参数映射/物理/分析)                     │
├────────────────────────────────────────────────────────────┤
│                    系统层                                   │
│  WindowOverlay (DWM透明) · SystemTrayManager               │
│  ReminderManager · ServerPollService · OpenClawBridge     │
│  ToolEngine (10 工具文件 + 7 基础设施 · 65 工具)           │
└────────────────────────────────────────────────────────────┘
```

### 通信架构（C# ↔ Node 桥 ↔ OpenClaw/Python）

```
C# (OpenClawBridge.cs) --HTTP JSON, x-bridge-token--> openclaw_bridge.js (:19876)
                                                        |--WebSocket--> OpenClaw Gateway (:18789)
                                                        |--execSync 临时JSON--> Python 脚本
                                                        |   (PPT/Word/Excel/LaTeX 生成器)
```

- 桥接服务器：Node.js，PM2 管理，鉴权 `x-bridge-token`，**per-session 请求锁**（多任务并行）
- 端点：`/health` `/search` `/task`（提交/轮询/取消/**审批**）`/compile_latex` `/generate_office`
- **exec 审批闭环**（2026-08-12 E2E 实测打通）：`tools.exec.mode=ask` → 敏感命令触发审批事件
  → 桌宠弹窗（60s 倒计时）→ 用户决策 → `exec.approval.resolve` 回执 → 命令继续/中止

### 具身智能数据流

```
MotionAgent ──→ MotionTranslator ──→ MotionPlanner ──→ MotionGenerator
(感知/决策)    (DeepSeek 关键帧)     (10 模板/6 曲线)   (插值播放)
     ↑                                                      │
     │    MotionMemoryManager ←── VisionMotionVerifier ←────┘
     │    (30 条 + 负反馈)      (GLM-4V 多帧拼图评分)
     └────────────────────────────────────────────────────────┘
```

---

## 🤖 核心能力

### 💬 AI 对话（65 工具）

| 类别 | 代表工具 | 能力 |
|------|---------|------|
| 🔭 观星术 | `search_web` `openclaw_search` | 联网搜索（OpenClaw Bridge / 传统搜索） |
| 📷 摄形术 | `take_screenshot` | 截图 + GLM-4V 视觉分析 |
| 🔍 洞观术 | `get_system_info` `search_files` | 系统状态 + Everything 毫秒级搜索 |
| 🚀 开阵术 | `open_app` `open_folder` | 启动应用、打开文件夹 |
| 🔒 封印术 | `lock_screen` `power` | 锁屏/关机/重启（需用户确认） |
| 📋 卜算记事簿 | `set_reminder` `query_reminders` | 便签增删改查 + 三级推送 |
| 📚 卜算传讯 | `query_exams` `query_scores` `query_schedule` | 课表/成绩/考试/学业概览 |
| 📜 问天录 | `compile_latex` | LaTeX 长文档分块编译 |
| 📊 办公生成 | `generate_ppt` `generate_docx` `generate_xlsx` | 一句话生成办公文档 |
| 🧠 忆境术 | `inspect_personality` `inspect_motion_memory` | 人格自省 + 演武心经 |
| 📚 藏书阁 | `knowledge_search` `knowledge_index` | 本地 RAG 语义检索 |
| 🕵️ 自省术 | `explore_body` `self_review` `vis_verify` | 参数探索 + 视觉验证 |
| 🖥️ 太卜神行法 | `openclaw_task` | 复杂多步任务外包给 OpenClaw（浏览器/命令） |
| 📌 太卜阵法图 | `query_task_templates` `save_task_template` | 任务模板库 + 执行轨迹沉淀 |

**核心机制**：
- **意图过滤**：本地 Ollama 分类（chat/emotion/command/knowledge/operation），chat/emotion 不发工具
- **Token 优化**：工具按意图注入子集（首轮 65→27）、历史预算 15000 字符 + Ollama 摘要、缓存命中率 98.6%
- **危险工具审批**：7 个危险工具（file_delete/power/lock_screen/run_command/set_volume/mute/openclaw_task）走 `ToolConfirmManager` 确认
- **看门狗**：单次请求总超时 600s；任务外包有心跳熔断（无进展自动取消）

### 🎭 Live2D 表现

| 特性 | 说明 |
|------|------|
| **Cubism SDK 5-r.4** | 参数化变形 + 实时物理模拟（裙/发/法盘惯性跟随） |
| **DWM 透明窗口** | `DwmExtendFrameIntoClientArea` 无缝融合桌面，无绿边 |
| **Perlin 噪声微动** | 7 通道独立噪声：呼吸/身体晃(3)/头部/眼球(2) |
| **参数映射** | 80+ 参数按部位分组 → 语义名双向映射 |
| **天气↔表情联动** | ☀️微笑 / 🌧委屈 / ❄️好奇 / 🌙垂眼 |
| **颜文字禁绝** | SystemPrompt 禁止 + 代码兜底：模型若违规输出 → 自动翻译为表情动作并剥离 |

### 🏃 桌面物理

- 重力/碰撞/抛掷物理模拟、头/身/腿分区点击反馈、拖拽悬挂
- 系统托盘 + 开机自启、ESC 隐藏
- 多屏行走为**规划中能力**（勿宣称可用）

---

## 🚀 快速上手

### 环境依赖

| 依赖 | 必需 | 说明 |
|------|------|------|
| 团结引擎 | ✅ | `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe` |
| Live2D SDK | ✅ | CubismSdkForUnity-5-r.4（模型在 `StreamingAssets/Live2D/Fuxuan/`） |
| DeepSeek API | ✅ | 对话 + 工具调用 + 动作关键帧生成 |
| GLM-4V API | ✅ | 截图分析 + 动作视觉评分 |
| Ollama | ⭕ | 本地 LLM 动作决策 + RAG 嵌入（`qwen2.5:3b` / `nomic-embed-text`） |
| OpenClaw | ⭕ | 搜索网关 / 任务外包（HTTP 19876 / WebSocket 18789） |
| Everything CLI | ⭕ | 毫秒级文件搜索（自动检测 `es.exe`） |
| Node.js + PM2 | ⭕ | 桥接服务器（`openclaw-bridge`） |

### 环境变量

```powershell
# DeepSeek（必需 — 对话 + 动作生成）
setx DEEPSEEK_API_KEY "sk-your-key-here"
# 智谱 GLM（必需 — 视觉分析 + 动作自评）
setx GLM_API_KEY "your-glm-key-here"
# 和风天气（可选 — 默认 wttr.in）
setx QWEATHER_API_KEY "your-qweather-key-here"
# OpenClaw Bridge 认证（可选）
setx BRIDGE_TOKEN "your-bridge-token-here"
# 服务端轮询（可选 — 便签同步）
setx DESKTOP_TOKEN "your-server-token-here"
```

> ⚠ 设完后需**重启 VS Code / 重新登录**使环境变量生效。密钥一律环境变量读取，不入库。

### 构建 & 运行

```powershell
.\build.ps1              # 完整构建 → Build/DesktopPet.exe
.\build.ps1 -Quick       # 仅编译验证（快速）
.\build.ps1 -RunTests    # 运行 Editor 测试套件（EditMode 78 个）
.\Build\DesktopPet.exe   # 启动桌面宠物
```

> 首次运行后可从系统托盘右键菜单启用开机自启。ESC 隐藏到托盘。

---

## 📁 项目结构

```
Desktop_per_pro/
├── code/desktop_unity/          # Unity 工程（Assets/Scripts/ 84 个 C# 文件）
│   ├── Assets/Scripts/
│   │   ├── ChatManager.cs       # AI 对话核心（10轮工具循环）
│   │   ├── ToolEngine/          # 65 工具插件架构
│   │   ├── ActionAgent/         # 具身动作闭环（15 文件）
│   │   ├── Live2DFramework/     # Live2D 渲染/参数映射
│   │   └── ...（感知/记忆/UI/物理 各子系统）
│   └── openclaw_bridge.js       # Node.js 桥接服务器（:19876）
├── scripts/office/              # Python 办公生成器（PPT/Word/Excel）
├── docs/                        # 权威文档（架构/规范/模块/路线图）
│   ├── code-truth-architecture.md   # 代码真相架构审计
│   ├── development-standards.md     # 开发规范（唯一权威）
│   ├── desktop-assistant-roadmap.md # 演进路线图
│   └── modules/                     # 9 份模块文档（四要素模板）
├── tools/                       # 诊断/测试/清理脚本
├── build.ps1                    # 构建脚本（Quick/Full/Tests）
├── AGENTS.md                    # AI 协作快速入口（7 条铁律）
└── CHANGELOG.md                 # 版本迭代日志（N38~N41+）
```

---

## 🧭 文档导航

| 文档 | 用途 |
|------|------|
| [`docs/code-truth-architecture.md`](docs/code-truth-architecture.md) | 代码真相架构审计（六层架构 + 工具清单） |
| [`docs/development-standards.md`](docs/development-standards.md) | 开发规范（9 章，AI + 人类通用） |
| [`docs/desktop-assistant-roadmap.md`](docs/desktop-assistant-roadmap.md) | 演进路线图（v0.2，含开源调研） |
| [`docs/README.md`](docs/README.md) | 文档总索引（9 模块四要素文档） |
| [`AGENTS.md`](AGENTS.md) | AI 协作快速入口 |
| [`CHANGELOG.md`](CHANGELOG.md) | 版本迭代日志 |

---

## 🗺️ 路线图（摘录 v0.2）

- ✅ N39 代码真相修复（9 Bug + 结构偏差）
- ✅ N40 安全加固 + Token 优化 T1-T8（成本降 60%）
- ✅ N41 像素表情包 + 颜文字禁绝
- ✅ 2026-08-12 任务执行可视化：进度可见 + 关键步审批 + exec 审批 E2E + 多任务并行
- ✅ 2026-08-12 任务模板库 + 执行轨迹沉淀（省钱自学习）
- 🔜 多屏行走、3D 渲染模式、任务模板可视化画布

---

## 🤝 贡献与规范

- **开发规范**：见 [`docs/development-standards.md`](docs/development-standards.md)（唯一权威，9 章）
- **AI 协作**：见 [`AGENTS.md`](AGENTS.md)（7 条铁律：测试模式/禁空参遍历/密钥不入库/PM2 管理…）
- **提交规范**：Conventional Commits（`<type>(<scope>): <中文描述>`）
- **文档铁则**：写完代码必须更新对应模块文档，且**测试通过后再写**（文档只记录已验证的代码真相）

---

## 📜 致谢

- **角色**：《崩坏：星穹铁道》符玄（本作品为非商业个人学习项目）
- **模型**：符玄 Live2D 模型（by 琉璃奈希）
- **框架**：Unity 团结引擎 · Live2D Cubism SDK · OpenClaw · python-pptx/docx/openpyxl
- **灵感**：微软 UFO²（Speculative Multi-Action）、AutoGPT（工作流可视化）

---

<div align="center">

**「卜卦问天，不如观心。」**

⭐ 如果符玄大人合您心意，欢迎 Star 支持！

</div>
