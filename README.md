# 太卜司·符玄 — 桌面灵伴 Desktop Pet

<div align="center">

![版本](https://img.shields.io/badge/版本-N37%2B-blue)
![引擎](https://img.shields.io/badge/引擎-Tuanjie%202022.3.62t7-purple)
![平台](https://img.shields.io/badge/平台-Windows%2064位-green)
![Live2D](https://img.shields.io/badge/Live2D-Cubism%205--r.4-orange)
![AI](https://img.shields.io/badge/AI-DeepSeek%20Chat%20%7C%20GLM--4V-red)
![本地LLM](https://img.shields.io/badge/本地LLM-Ollama%20Qwen2.5-yellow)
![工具](https://img.shields.io/badge/工具-52-blue)

**「穷观妙算，天机尽显。」** — 仙舟「罗浮」太卜司之首，符玄大人驾临您的桌面。

> ⚠️ **本文档已按「代码真相审计」重写（2026-08-02）**：所有数字与表述均以
> `code/desktop_unity/Assets/Scripts/` 下的真实代码为准，旧版文档的过时描述已修正。
> 权威架构参照见 [`project_brief/code-truth-architecture.md`](project_brief/code-truth-architecture.md)。

</div>

---

## 📖 项目概览

基于 **Unity（团结引擎 Tuanjie 2022.3.62t7）+ Live2D Cubism SDK 5-r.4** 构建的 Windows 桌面 AI 伴侣。角色为《崩坏：星穹铁道》中的 **符玄**，具备完整的感知—决策—执行—验证—记忆闭环系统。

| 维度 | 能力 |
|------|------|
| 💬 **AI 对话** | DeepSeek Chat + Function Calling，**52 个工具，最多 10 轮回环**，意图过滤 + 600s 看门狗 |
| 🎭 **表情动作** | Live2D 参数化动画 + LLM 实时动作生成 + GLM-4V 视觉自评闭环 |
| 🧠 **记忆人格** | 三层长期记忆 + 五维人格演化 + 三维关系模型 |
| 🌤️ **环境感知** | 前台活动 / 浏览器标签 / 时间天气 / 系统性能 |
| 📋 **日程管理** | 便签/提醒 + 课表成绩查询 + 三级推送链路 |
| 🏃 **桌面物理** | 重力/碰撞/抛掷/分区点击反馈（多屏行走为规划中能力） |

---

## 🚀 快速上手

### 环境变量

```powershell
# DeepSeek（必需 — 对话 + 动作生成）
setx DEEPSEEK_API_KEY "sk-your-key-here"
# 智谱 GLM（必需 — 视觉分析 + 动作自评）
setx GLM_API_KEY "your-glm-key-here"
# 和风天气（可选 — 精准天气；代码默认使用 wttr.in，cityCode=Nanjing）
setx QWEATHER_API_KEY "your-qweather-key-here"
# OpenClaw Bridge 认证（可选）
setx DESKTOP_TOKEN "your-bridge-token-here"
```

> ⚠ 设完后需**重启 VS Code / 重新登录**使环境变量生效。
> 配置统一读取自 `ChatConfig.cs`（仅环境变量，附 `.cs.example` 模板，密钥不入库）。

### 构建 & 运行

```powershell
.\build.ps1              # 完整构建 → Build/DesktopPet.exe
.\build.ps1 -Quick       # 仅编译验证（快速）
.\build.ps1 -RunTests    # 运行 Editor 测试套件
.\Build\DesktopPet.exe   # 启动桌面宠物
```

> 首次运行后可从系统托盘右键菜单启用开机自启。ESC 隐藏到托盘。

### 环境依赖

| 依赖 | 备注 |
|------|------|
| 引擎 | `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe`（有路径拼接 Bug，需 cd + 相对路径） |
| Live2D SDK | CubismSdkForUnity-5-r.4（模型位于 `StreamingAssets/Live2D/Fuxuan/`） |
| DeepSeek API | 对话、工具调用、动作关键帧生成 |
| GLM-4V API | 截图分析、动作视觉评分、自探索 |
| Ollama（可选） | 本地 LLM 动作决策 + RAG 嵌入（`qwen2.5:3b` / `nomic-embed-text`） |
| Everything CLI（可选） | 毫秒级文件搜索（自动检测 `es.exe`） |
| OpenClaw Bridge（可选） | 搜索网关（HTTP 19876 / WebSocket 18789） |
| 系统 | Windows 10/11 64 位 |

---

## 🧠 系统架构

### 六层架构

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
│  TimeWeatherController · PerformanceMonitor                │
│  GpuLoadMonitor · KnowledgeBaseManager                    │
├────────────────────────────────────────────────────────────┤
│                    具身层                                   │
│  ActionAgent/ (16 组件)                                    │
│  MotionAgent · MotionTranslator · MotionPlanner            │
│  MotionGenerator · MotionMemoryManager · SafetyValidator   │
│  DualModelValidator(单GLM) · VisionMotionVerifier          │
│  PersonalityManager · EmotionState · IdleActionScheduler   │
├────────────────────────────────────────────────────────────┤
│                    物理与渲染层                              │
│  DesktopPet (物理引擎) · DragHandler (交互)                 │
│  HybridRenderer → Live2DRenderer (3D 分支不可用)           │
│  Live2DFramework/ (参数映射/物理/分析)                     │
├────────────────────────────────────────────────────────────┤
│                    系统层                                   │
│  WindowOverlay (DWM透明) · SystemTrayManager               │
│  ReminderManager · ServerPollService · OpenClawBridge     │
│  ToolEngine (10 文件 · 52 工具)                           │
└────────────────────────────────────────────────────────────┘
```

### 执行顺序

| Order | 组件 | 职责 |
|-------|------|------|
| 0 | `DesktopPet.Update()` | 物理更新、状态转换、行走相位 |
| 800 | `CubismPhysicsController.LateUpdate()` | 衣服/头发/裙子物理模拟 |
| 801 | `Live2DRenderer.LateUpdate()` | 覆盖物理重置参数、空闲动画、交互反馈 |

### 闭环具身智能数据流

```
MotionAgent ──→ MotionTranslator ──→ MotionPlanner ──→ MotionGenerator
(感知/决策)    (DeepSeek 关键帧)     (10 模板/6 曲线)   (插值播放)
     ↑                                                      │
     │    MotionMemoryManager ←── DualModelValidator ←──────┘
     │    (30 条 + 负反馈)      (GLM-4V 多帧拼图评分·单模型)
     └────────────────────────────────────────────────────────┘
```

---

## ✨ 功能详解

### 🤖 AI 对话系统

基于 **DeepSeek Chat API** 的 Function Calling 体系，通过 `ToolEngine/IPetTool` 插件接口自动发现并注册 **52 个工具**（10 个工具文件），支持最多 **10 轮** 回环调用。

| 类别 | 工具（真实注册名） | 能力 |
|------|------|------|
| 🔭 观星术 | `search_web`, `open_url`, `search`, `openclaw_search` | 联网搜索（OpenClaw Bridge / 传统搜索） |
| 📷 摄形术 | `take_screenshot` | 截图 + GLM-4V 视觉分析 |
| 🎵 调音术 | `set_volume`, `mute` | 系统音量调节 |
| 🔒 封印术 | `lock_screen`, `power` | 锁屏/关机/重启/睡眠（需用户确认） |
| 📢 传音术 | `notify`, `get_clipboard`, `set_clipboard` | 推送 + 剪贴板 |
| 🔍 洞观术 | `get_system_info`, `search_files`, `search_file`, `list_files` | 系统状态 + Everything 毫秒级搜索 |
| 🚀 开阵术 | `open_app`, `open_folder` | 启动应用、打开文件夹 |
| 🏃 行动术 | `launch_pogget`, `pogget_agent` | 启动 Pogget / 与 Pogget Agent IPC |
| 📋 卜算记事簿 | `set_reminder`, `query_reminders`, `mark_reminder_done`, `delete_reminder` | 便签增删改查（重复规则 + 三级推送） |
| 📚 卜算传讯 | `query_exams`, `query_scores`, `query_schedule`, `query_user_status` | 课表、成绩、考试、学业概览查询 |
| 📜 问天录 | `compile_latex` | LaTeX 编译（经 OpenClawBridge） |
| 🎭 演武术式 | `play_action`, `stop_action`, `generate_motion` | 预设/LLM 生成动作 |
| 🧠 忆境术 | `inspect_personality`, `inspect_motion_memory` | 人格自省 + 演武心经查看 |
| 📚 藏书阁 | `knowledge_search`, `knowledge_index` | 本地 RAG 语义检索 |
| 🕵️ 自省术 | `explore_body`, `explore_body_vision`, `self_review`, `run_verification`, `vis_verify` | 参数探索 + 视觉验证 |
| 💬 言出法随 | `【表情:开心】` / `【动作:伸懒腰】` | 回复文本内嵌动作标记 |

**核心机制**：
- **意图过滤**：本地 Ollama 分类（chat/emotion/command/knowledge/operation），chat/emotion 不发工具，其余按白名单过滤
- **看门狗**：单次请求总超时 600s，覆盖多轮工具链（含 GLM-4V 180s）
- **自动重试**：最多 3 次，400/401/403 不重试
- **历史裁剪**：最多保留 60 条
- **打字机逐句显示**（2.5s 间隔）、**消息队列**（等待时不丢输入）
- **上下文注入**：时间/天气/前台活动/多窗口/浏览器标签/记忆/人格/身体参数知识/闭环能力/演武心经

---

### 🎨 Live2D 渲染

| 特性 | 说明 |
|------|------|
| **Cubism SDK 5-r.4** | 参数化变形 + 实时物理模拟（裙/发/法盘惯性跟随） |
| **DWM 透明窗口** | `DwmExtendFrameIntoClientArea` 无缝融合桌面，无绿边 |
| **Perlin 噪声微动** | 7 通道独立噪声：呼吸/身体晃(3)/头部/眼球(2) |
| **参数映射** | 80+ 参数，按部位分组 → 语义名双向映射 |
| **天气↔表情联动** | ☀️微笑 / 🌧委屈 / ❄️好奇 / 🌙垂眼 |
| **3D 模式** | `HybridRenderer` 3D 分支为 TODO，**当前恒走 Live2D** |

---

### 🏃 物理与交互

| 特性 | 细节 |
|------|------|
| **桌面物理** | 重力加速度、碰撞弹跳衰减、摩擦阻尼 |
| **拖拽抛掷** | 5 帧速度缓冲平均 → 物理抛掷 + 挣扎动画 |
| **分区点击** | 🐱头=摸头眯眼 / 👗身=戳胸惊讶 / 🦵腿=害羞踢腿 |
| **鼠标跟随** | 眼球 150px 触发平滑追踪 |
| **屏幕碰撞** | 边缘撞墙动画 + 反弹物理 |
| **地面状态机** | 5 种行为：左/右行走（边缘/定时）、停留，加权随机切换 |
| **多显示器** | `WindowOverlay.isMultiMonitor` **恒为 false**（仅用 SM_CXSCREEN，跨屏行走为规划中能力） |

---

### 🎭 动作系统 — ActionAgent（16 组件）

#### 空闲动作
9 种 JSON 配置驱动（id 1--9：歪头/微笑/挑眉/星辉*/伸懒腰/委屈/法阵*/害羞/困惑，*为硬编码特效），天气/时段动态调权。

#### MotionAgent — 自主动作决策引擎
| 密度档 | 间隔 | 触发条件 |
|--------|------|---------|
| High | max(base×0.5, 4s) | 空闲短 / 测试模式 |
| Med | base | 常规空闲 |
| Low | min(base×2, 15s) | 深夜 / 专注进程 |
| Sleep | 30s | 长时间无交互 |

- **决策循环**：等待 → ShouldDecide → GatherContext → DecideWithLLM / FallbackDecide → ExecuteDecision
- **本地 LLM**：Ollama Qwen2.5，连续 3 次失败回退概率模式
- **GPU 监控**：检测游戏自动暂停，退出后 30s 冷却恢复
- ⚠️ **已知 Bug**：`IsSleepTime()` 硬编码返回 false，睡眠时段调度失效（待修）

#### MotionTranslator — LLM 动作翻译
自然语言 → Live2D 关键帧序列（DeepSeek, temp=0.3）
- **10 条规则 + 10 种特殊模式（9 种姿势，捂脸重复）**
- 身体分组 Schema：HEAD/EYES/BROWS/MOUTH/ARMS/HANDS/FINGERS/LEGS/BODY
- 参数富化：自动补全关联肢体参数
- VERIFIED FEEDBACK 注入：上次失败案例自动反馈给 LLM

#### MotionPlanner + MotionGenerator
- 10 种硬编码模板（挥手/点头/摇头/鞠躬/伸懒腰/叉腰/捂脸/指/招手/合十）
- 6 种插值曲线（Linear/EaseIn/EaseOut/EaseInOut/Cosine/Sine + `Bounce`）
- 3 阶段计划：淡入 / 保持 / 回归
- 6 种表情模板

#### 闭环视觉验证
- **DualModelValidator**：GLM-4V 对动作 20/40/60/80% 截图合成 2×2 拼图评分（1-5 分）
  - ⚠️ 名为「双模型」实为**单 GLM-4V**（Qwen-VL 已移除），passThreshold=3，拼图存 `glm_collages/`（上限 50）
- **自评闭环**：`generate_motion` 播放完毕自动截图 → GLM 评分 → 追加到回复

#### MotionMemoryManager — 闭环学习核心
- 上限 30 条，高分覆盖低分，自动淘汰最低分/最久远
- 负反馈（≤2 分记入反例，最多 10 条）
- 无望检测（≥5 次且最高分 ≤2 优先淘汰）
- 冷却 120s 防复读

#### SafetyValidator
- 互斥组（2 组） / 对称对（5 对） / 极端值 3 类保护，防止 LLM 生成极限值损坏模型

#### 人格与情绪
- **PersonalityManager**：五维特质（勤奋0.5/温暖0.6/活泼0.5/自信0.5/好奇0.6）+ 三维关系（信任0.3/亲密0.2/熟悉0.1），learningRate=0.01
- **EmotionState**：valence/arousal/warmth/energy，120s 半衰期衰减，7 种主导情绪
- ⚠️ `PersonalityManager.DriftTowardNeutral()`（无交互回归）在 ActionAgent 内无调用者

---

### 🧠 感知与记忆

| 系统 | 方式 | 用途 |
|------|------|------|
| 法眼 | 2s 轮询前台窗口 | 8 类分类：编程/游戏/学习/浏览/娱乐/通讯/空闲/其他 |
| 多窗口 | EnumWindows 扫描 | 环境感知（注入 prompt） |
| 浏览器标签 | UIA 反射 | 读取 6 种浏览器标签 |
| 长期记忆 | 3 层 JSON 持久化 | 存储 entries+coreFacts+conversationSummary；**输出为 4 层**（核心事实→近日印象→Top5→最近3条） |
| 知识库 | Ollama 嵌入语义检索 | `nomic-embed-text` + 余弦 TopK，25+ 文件类型分块索引 |
| 人格 | 五维 + 三维关系 | 跨会话连续演化 |
| 反思 | ⚠️ **未接线** | `ChatManager.OnReflectRequest` 回调恒 null，反思机制实际未驱动 |

### 🧬 人格演化系统

| 维度 | 范围 | 描述 | 正触发 |
|------|------|------|--------|
| **diligence** | 0~1 | 勤勉 vs 慵懒 | 工作/学习/编码 |
| **warmth** | 0~1 | 温暖 vs 高冷 | 用户感谢/称赞 |
| **playfulness** | 0~1 | 活泼 vs 稳重 | 游戏/表情/语气词 |
| **confidence** | 0~1 | 自信 vs 谦逊 | 工具调用成功 |
| **curiosity** | 0~1 | 求知 vs 淡然 | 搜索/提问 |

**初始值**：勤奋0.5/温暖0.6/活泼0.5/自信0.5/好奇0.6；**学习率** 0.01
**三维关系**：信任 / 亲密 / 熟悉度
**人格↔情绪联动**：五维 × 权重 → EmotionState 四维基线偏移
**持久化**：`pet_personality.json`，跨会话连续演化

---

### 🌤️ 时间与天气

| 维度 | 实现 |
|------|------|
| 昼夜感知 | 日/夜/深夜三档，犯困表情联动 |
| 天气源 | **默认 wttr.in**（cityCode="Nanjing"），QWeather 可选（有 Key 才用） |
| AI 语录 | DeepSeek 批量生成符玄风格台词（6 行） |
| 表情联动 | ☀️微笑 / 🌧委屈 / ❄️好奇 / ⚡警惕 |

---

### 📋 便签与提醒

- 本地 JSON 持久化，优先级：low / normal / high
- 重复规则：一次性 / 每日 / 工作日 / 每周
- **三级推送**：气泡 → Windows Toast（AppId '符玄'）→ Server酱³ 手机
- 服务端同步（轮询 localhost:3000）+ AI 语音创建（"提醒我…"）
- 去重机制：关键词检查已有同类待办
- 考试提醒：前 3 天 19:00 + 考前 2h

---

### 🖥️ 交互界面

| 元素 | 触发 | 功能 |
|------|------|------|
| 🟣 **悬浮球 BallPanel** | 右下角粉✦ | 420×580px 辐射菜单（设置/报告/便签） |
| 📋 **右侧面板 RightPanel** | `~`键 / 鼠标划过右边缘 | 220px Widgets（聊/设/签/告） |
| 💬 **古风气泡 ChatBubble** | AI 回复/提醒/闲话 | OnGUI 手绘圆角 + 12 星点 |
| 📥 **底部输入栏** | 自动显示 | Windows 搜索风格 0.5s 淡入 |

**优先级系统**：High(AI 回复) > Normal(提醒) > Low(闲话)

---

### 🖥️ 窗口与系统集成

| 特性 | 技术 |
|------|------|
| 透明窗口 | DWM `DwmExtendFrameIntoClientArea` + WS_POPUP + WS_EX_LAYERED |
| 点击穿透 | 动态切换 WS_EX_TRANSPARENT（依据 BallPanel/RightPanel 区域） |
| 多显示器 | ⚠️ `WindowOverlay.isMultiMonitor` 恒为 false，仅使用主屏尺寸（跨屏行走为规划中能力） |
| 睡眠恢复 | 帧间间隙检测 → DWM 重建 → 物理重置 |
| DWM 安全 | 连续 5 次崩溃跳过重建 |
| 托盘 | Shell_NotifyIcon（左/右键菜单） |
| 开机自启 | 注册表 HKCU\Run |
| 进程互斥 | Mutex "DesktopPet_Unity_SingleInstance" 防止多开 |
| 运行时日志 | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\desktop pet\Player.log` |

---

### ⚡ 性能优化

| 档位 | FPS | RT 比例 | 触发条件 |
|------|-----|---------|---------|
| High | 60 | 100% | 正常 |
| Normal | 40 | 75% | 中等负载 |
| Low | 20 | 50% | CPU/GPU > 92% / 内存 > 93% |

- 升档拦截：CPU/GPU > 80% 阻止升档
- 内存管理：800MB GC / 1.2GB 强制 GC
- 崩溃日志自动捕获（>2MB 截断）
- GpuLoadMonitor：检测游戏时暂停 LLM 动作决策，退出后 30s 冷却恢复
- ⚠️ `PerformanceMonitor.GetResolutionScale()` 恒返回 1.0f（分辨率缩放未实现）

---

## 🏗️ 项目结构

```
D:\Unity\projects\Desktop_per_pro\            # 项目仓库根
├── build.ps1 / build.cmd                     # 构建入口
├── CHANGELOG.md / README.md
│
├── Build/                                    # 构建产物
│   ├── DesktopPet.exe
│   └── DesktopPet_Data/
│       ├── Managed/                          # C# DLL
│       ├── StreamingAssets/Live2D/           # Live2D 模型
│       └── Plugins/
│
├── code/desktop_unity/                       # Unity 项目根
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── *.cs                          # 顶层 33 个 + 子目录约 50 个脚本
│   │   │   ├── ToolEngine/                   # 插件式工具系统（10+ 文件，52 工具）
│   │   │   │   ├── ITool.cs / ToolSchema.cs  # 接口 + 参数 Schema
│   │   │   │   ├── ToolRegistry.cs           # 反射发现 + 调度
│   │   │   │   ├── ToolHelpers.cs            # 公用辅助
│   │   │   │   ├── AsyncToolBase.cs          # 异步工具基类
│   │   │   │   └── *Tools.cs                 # Web/Vision/Memory/Reminder/Knowledge/Body/Pogget/Latex
│   │   │   ├── ActionAgent/                  # 闭环动作引擎（16 文件）
│   │   │   ├── Editor/                       # 编辑器工具（7 文件）
│   │   │   └── Live2DFramework/
│   │   │       ├── ActionPresets/            # 预设（6 文件）
│   │   │       ├── PersonalityManager.cs     # 人格演化
│   │   │       ├── KnownParameterPatterns.cs # 共享参数常量
│   │   │       ├── Live2DParameterMapper.cs  # 语义→ID 映射
│   │   │       ├── ParameterKnowledgeProvider.cs
│   │   │       ├── ModelBodySchema.cs
│   │   │       └── ParamMaps/
│   │   ├── Resources/                        # SystemPrompt + ParamMaps
│   │   └── StreamingAssets/Live2D/           # Cubism 模型
│   ├── openclaw_bridge.js                    # 搜索网关（HTTP 19876 / WS 18789）
│   └── ProjectSettings/ / Packages/
│
├── file/符玄/                                # Live2D 模型源文件
├── project_brief/                            # 技术文档（含 LaTeX 报告）
│   └── code-truth-architecture.md            # ⭐ 代码真相架构审计（权威参照）
├── record/                                   # 开发记录
│   ├── Latex/report.tex
│   └── log/build_workflow.md
├── scripts/param_mapper/                     # 参数映射工具
├── tools/                                    # 诊断/搜索工具
│   ├── pet_diag.py                           # 自动化诊断
│   ├── find_file.py                          # 文件搜索
│   └── pdf_extract.py
└── CHANGELOG.md
```

### 配置全景

```
环境变量:
  DEEPSEEK_API_KEY → ChatManager / IdleChatGenerator / MotionTranslator
  GLM_API_KEY       → ToolEngine / DualModelValidator / VisionMotionVerifier
  QWEATHER_API_KEY  → TimeWeatherController（可选，默认 wttr.in）
  DESKTOP_TOKEN     → OpenClaw Bridge 认证（可选）

JSON 持久化 (D:\DesktopPetData\，DataPathConfig.cs 硬编码):
  pet_config.json  pet_memory.json  reminders.json  motion_memory.json
  pet_personality.json  activity_log.json  knowledge_base.json
  Documents/（LaTeX 输出）  ActionRefs/（512×512 PNG）  glm_collages/（2×2 拼图，上限 50）
```

> ⚠️ 旧文档提到的 `validation_log.json` **不存在于当前代码**，已移除。

---

## ⚠️ 已知问题清单（代码真相版）

详见 [`project_brief/code-truth-architecture.md`](project_brief/code-truth-architecture.md)：

1. 工具回环为 **10 轮**（非 5 轮）；工具 **52 个 / 10 文件**（非 40+ / 6 文件）
2. ActionAgent 为 **16 文件**（非 15）；MotionTranslator 为 **10 规则 + 10 特殊**（非 11+12）
3. 插值曲线为 **Bounce**（非 BounceEaseOut）；天气默认 **wttr.in**（非 QWeather 优先）
4. **DualModelValidator 仅 GLM-4V**（Qwen-VL 已删，qwenScore 恒 0）；`OnReflectRequest` 回调恒 null（反思未驱动）
5. `KnowledgeBaseManager.GetFormattedContext()` 为 STUB 恒返回 ""；`GetResolutionScale` 恒 1.0；`isMultiMonitor` 恒 false；3D 渲染不可用；`AutoMotionCollector` 为 [Obsolete] 死代码
6. 已确认 **9 个 Bug**（BUG-1~BUG-9），见审计文档第 5 节

---

## 📜 版本历史

完整历史见 [`CHANGELOG.md`](CHANGELOG.md)。当前版本 **N37**：

| 版本 | 日期 | 亮点 |
|------|------|------|
| **N37** | 2026-07-26 | 言出法随（文本内嵌动作标记）、ToolEngine 插件化、OpenClaw Bridge 搜索网关、Pogget 集成 |
| N36 | 2026-07 | ToolCallInvoker 4065 行 → 插件化 ToolEngine，动态工具发现/注册 |
| N35 | 2026-07 | 移除 Qwen-VL，GLM-4V 多帧拼图视觉验证定型 |
| N34 | 2026-07 | MotionTranslator SPECIAL PATTERNS 优化 |
| N33 | 2026-07 | MotionVerifier + 人格系统 V3（五维 + 三维关系） |
| N32 | 2026-07 | VisionMotionVerifier 闭环视觉验证 |
| N18 | 2026-06-22 | 已完成任务视图 + 系统内存监控 |
| N17 | 2026-06-22 | 提醒去重 + 服务器推送去重 |
| N16 | 2026-06-20 | Everything 毫秒级搜索 |
| N14~N15 | 2026-06 | 双模型验证 + 工具调用修复 |
| N7~N13 | 2026-06 | 便签/服务端/学业数据/多轮修复 |
| v0.2 | 2026-05 | 初始 Unity 版本 |

---

## ⚖️ 许可证 & 致谢

**许可证：** 本项目为个人学习与娱乐用途，角色「符玄」版权属于 miHoYo / HoYoverse。代码部分参考 Apache License 2.0 精神开源。

**致谢：** [Live2D Cubism](https://www.live2d.com/) · [DeepSeek](https://platform.deepseek.com/) · [智谱 GLM](https://open.bigmodel.cn/) · [Ollama](https://ollama.com/) · [和风天气](https://www.qweather.com/) · [wttr.in](https://wttr.in/) · [Server酱³](https://sc3.ft07.com/) · [Everything](https://www.voidtools.com/) · [OpenClaw](https://github.com/open-claw/open-claw)
