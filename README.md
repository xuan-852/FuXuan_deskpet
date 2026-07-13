# 符玄桌面宠物 — Fu Xuan Desktop Pet

<div align="center">

![版本](https://img.shields.io/badge/版本-V2.3%2B-blue)
![引擎](https://img.shields.io/badge/引擎-Tuanjie%202022.3.62t7-purple)
![平台](https://img.shields.io/badge/平台-Windows%2064位-green)
![Live2D](https://img.shields.io/badge/Live2D-Cubism%205--r.4-orange)
![AI](https://img.shields.io/badge/AI-DeepSeek%20Chat%20%7C%20GLM--4V-red)
![本地LLM](https://img.shields.io/badge/本地LLM-Ollama%20Qwen2.5-yellow)

</div>

## 📖 项目简介

**符玄桌面宠物** 是一个基于 Unity（团结引擎）和 Live2D Cubism SDK 构建的 Windows 桌面宠物应用。角色为《崩坏：星穹铁道》中的 **符玄**（仙舟「罗浮」太卜司之首），拥有完整的 AI 对话、Live2D 表情动作、物理交互、天气感知、日程管理以及 **具身智能闭环** 等高级能力。

项目已迭代至 **V2.3+**，在前代悬浮球 + 辐射菜单交互体系基础上，引入了 MotionAgent 自主动作决策引擎、GLM-4V 多帧拼图视觉验证、闭环学习系统、本地 LLM 实时决策等深度 AI 能力，实现从感知→决策→执行→验证→记忆的完整具身智能闭环。

## ✨ 核心功能

### 🎨 Live2D 渲染
- **Live2D Cubism SDK 5-r.4** — 参数化变形 + 实时物理模拟
- **DWM 透明窗口** — 通过 `DwmExtendFrameIntoClientArea` 实现无缝桌面融合，无绿边
- **Perlin 噪声驱动** — 呼吸、身体微晃、头部微动、眼球转动，7 通道独立噪声
- **RenderTexture 叠层** — Layer 31 专用渲染层，支持透明叠加
- **3D 模型骨架** — `HybridRenderer` + `Model3DRenderer` 支持 Live2D/3D 混合渲染（0.3s 交叉淡入淡出）

### 🤖 AI 对话系统
- **DeepSeek Chat API** — 完整 Function Calling 支持（35+ 法术工具）
- **工具分类**：
  - 🔭 **观星术** — 打开网页、Bing 联网搜索
  - 📷 **摄形术** — 截图 + GLM-4V 视觉分析
  - 🎵 **调音术** — 调节音量、静音切换
  - 🔒 **封印术** — 锁屏、关机、重启、睡眠（需确认）
  - 📢 **传音术** — 桌面通知、剪贴板读写、Server酱³ 推送
  - 🔍 **洞观术** — 系统信息、Everything 毫秒级文件搜索
  - 🚀 **开阵术** — 启动应用（PATH/StartMenu）、打开文件夹
  - 📋 **卜算记事簿** — 便签增删改查
  - 📚 **卜算传讯** — 课表、成绩、考试、用户状态查询
  - 🎭 **演武术式** — 播放表情/动作、LLM 生成新动作
  - 🧠 **忆境术** — 读取/写入长期记忆
- **多轮工具调用** — 最多 5 轮回环，支持链式操作
- **句子队列逐句显示** — 长回复打字机效果，2.5s 间隔
- **自动闲聊** — 无操作一段时间后主动搭话，`IdleChatGenerator` 批量预生成
- **角色设定** — 从 `Resources/SystemPrompt.txt` 加载，每次对话动态注入时间/记忆/感知上下文

### 🏃 物理与交互
- **桌面物理引擎** — 重力（gravity=1）、碰撞、地面检测、弹跳衰减、摩擦阻尼
- **任意位置拖拽 + 抛掷** — 多帧速度缓冲平均（`throwScale=0.5`，`maxThrowSpeed=12`），带挣扎动画
- **拖拽挣扎动画** — 双臂划水、双腿交替、身体扭动、慌张表情，20+ 头发/裙子参数绕过物理延迟直接驱动
- **分区点击反馈** — 头部（摸头眯眼）/ 身体（戳胸惊讶）/ 腿部（害羞踢腿），通过 `CubismRaycaster` 检测
- **鼠标眼睛跟随** — 眼球平滑追踪鼠标位置（150px 触发距离）
- **屏幕边缘碰撞反弹** — 撞墙动画 + 反弹物理
- **地面任务状态机** — 5 种行为加权随机切换（左右边缘移动 / 左右计时移动 / 停止）

### 🎭 动作系统（Live2DFramework/ActionAgent — 15 组件）

#### 空闲动作
- **12+ 种空闲动作** — JSON 配置驱动（歪头/微笑/挑眉/星辉/伸懒腰/委屈/法阵/害羞/困惑/爱心/哭/心跳）
- **IdleActionScheduler** — 加权随机选择，时段/天气动态调整（夜晚降微笑权重，下雨增哭权重等）
- **走路动画** — 侧面转体 + 步态摆臂 + 呼吸加深
- **犯困表情** — 夜间眼皮渐沉、低头、微张嘴
- **天气表情联动** — 晴→微笑 / 雨→委屈 / 雪→好奇
- **法阵特效** — 弹簧-阻尼浮游物理 + Perlin 噪声

#### LLM 动作生成（MotionTranslator）
- **自然语言 → Live2D 关键帧序列** — DeepSeek (temperature=0.3, timeout=30s)
- 11 条规则 System Prompt + 12 种特殊模式
- 身体部位分组 Schema（HEAD/EYES/BROWS/MOUTH/ARMS/HANDS/FINGERS/LEGS/BODY）
- 从 `fuxuan_map.json` 加载全部参数的中文名和语义分类
- 视觉标定缓存 + 运动记忆注入（正例 + 反例）
- 参数富化：纯头面参数自动注入肢体参数

#### MotionAgent（自主动作决策引擎）
- **四档密度级别**：High（4s）/ Med（8s）/ Low（15s）/ Sleep（30s）
- **决策循环**：等待间隔 → `ShouldDecide()` → `GatherContext()` → `DecideWithLLM()` / `FallbackDecide()` → `ExecuteDecision()`
- **本地 LLM 双模式**：Ollama Qwen2.5（0.5B/3B）→ 连续失败 3 次后回退到概率模式
- **自适应密度**：基于空闲时长、睡眠时间、用户专注进程自动调节
- **情绪状态机**（EmotionState）：四维模型（Valence/Arousal/Warmth/Energy），指数衰减回归基线
- **全链路报告**：`GetPipelineReport()` — 失败/成功统计、Top-8 失败动作

#### 动作规划与播放（MotionPlanner + MotionGenerator）
- **11 种硬编码模板** — 挥手/点头/摇头/鞠躬/伸懒腰/叉腰/捂脸/指/招手/合十
- **6 种插值曲线** — Linear / Smooth / EaseOut / EaseIn / Hold / Bounce（含 BounceEaseOut 实现）
- **3 阶段计划** — 淡入 → 保持 → 回到默认
- **6 个表情模板** — happy/sad/angry/surprised/sleepy/blush

#### 闭环视觉验证
- **DualModelValidator** — GLM-4V 单模型评分（Qwen-VL 已移除）
- **多帧拼图** — `ComposeCollage()` 在 20%/40%/60%/80% 进度截图，合成 2×2 拼图
- **评分标准** — 1-5 分，≥3 通过；≤2 触发负反馈
- **盲探索** — `CallGlmVisionDescribe()` GLM 描述当前姿态像什么动作

#### 运动记忆（MotionMemoryManager — 闭环学习核心）
- **上限 30 条** — 按动作名索引，高分覆盖低分，自动淘汰最低分/最久远
- **负反馈系统** — ≤2 分记入反例（最多 10 条），自动淘汰引用最少 + 最旧
- **无望检测** — 尝试 ≥5 次且最高分 ≤2，标记为无望动作优先淘汰
- **`GetFailurePenalty()`** — 持续低分降权（0.3/0.4/0.7/1.0）
- **冷却机制** — 动作执行后 120s 内不注入 prompt，防止复读

#### 安全与 GPU 监控
- **SafetyValidator** — 3 类规则：互斥组（睁眼↔笑眼）、对称对（5 对眉毛/眼睛）、极端值保护
- **GpuLoadMonitor** — 通过 ActivityTracker 检测游戏状态，检测到游戏时自动暂停本地 LLM
- **LocalLLMClient.Paused** — 游戏退出后 30s 冷却自动恢复

#### 具身验证工具
- **VisionMotionVerifier** — 10 个视觉测试 T1-T10
- **MotionVerifier** — 三级测试：Level 1 对照组 / Level 2 测试组 / Level 3 边界测试
- **ActionReferenceManager** — 持久化动作标准截图供 self-review

### 🧠 感知与记忆
- **法眼活动追踪（ActivityTracker）** — 2s 轮询前台窗口，分类为编程/游戏/学习/浏览等
- **多窗口环境感知** — `EnumWindows` 扫描所有可见顶层窗口
- **浏览器标签深度感知（BrowserTabReader）** — UIA 反射读取 Chrome/Edge/Firefox/Opera/Brave/Vivaldi，无需插件
- **分层长期记忆（PetMemory）** — 核心事实始终保留 + Top-5 重要记忆 + 近期琐事，上限 30 条，JSON 持久化
- **反思反射** — 累积重要性 ≥ 30 时触发 LLM 提炼高层次洞察
- **话题冷却** — 相同话题 120s 冷却，防止重复记录

### 🌤️ 时间与天气
- **昼夜感知** — 自动检测系统时间，夜间/深夜特化表情
- **双天气源** — 和风天气（需 `QWEATHER_API_KEY`）优先，失败自动回退 wttr.in（无需 Key）
- **AI 天气语录** — DeepSeek 生成符玄风格天气台词（每次 6 条，按天气类型缓存）

### 📋 便签与提醒
- **本地 JSON 持久化** — 增删改查，支持优先级（low/normal/high）
- **重复规则** — 一次性 / 每日 / 工作日 / 每周
- **三级推送链路** — 气泡显示 → Windows Toast 通知 → Server酱³ 手机推送
- **服务端同步** — 与课表小程序联动（轮询推送消息）
- **AI 创建便签** — 对话中说"提醒我…"即可
- **已完成任务独立视图** — 待办/已完成可切换查看

### 🖥️ 交互界面
- **悬浮球（BallPanel）** — 桌面右下角粉色 ✦ 悬浮球，单击展开辐射菜单
  - ⚙️ **设置面板**：任务权重滑块 + 保存/清空
  - 📊 **报告面板**：MotionMemoryManager 演武心经学习报告
  - 📋 **便签面板**：待办/已完成管理，CRUD 操作
  - 420×580px 浮动窗口，可拖拽标题栏，右键关闭
- **右侧面板（RightPanel）** — Windows 11 Widgets 风格
  - `~` 键切换展开/收起，鼠标划过右边缘自动展开（1s 自动隐藏延迟）
  - 展开 220px / 收起 8px，滑动速度 10f/s
  - 4 个工具按钮：聊（聚焦输入框）/ 设（设置）/ 签（便签）/ 告（报告）
  - 底部输入栏集成，自定义悬停背景 + 紫光描边
- **古风聊天气泡（ChatBubble）** — OnGUI 渲染，圆角 + 拖尾 + 12 星点装饰
- **优先级气泡系统** — High（AI 回复）> Normal（提醒）> Low（闲话问候）

### 🖥️ 窗口与系统集成
- **DWM 透明窗口（WindowOverlay）** — WS_POPUP + WS_EX_LAYERED + `DwmExtendFrameIntoClientArea`
- **点击穿透** — DragHandler 每帧根据 BallPanel/RightPanel 区域动态切换 `WS_EX_TRANSPARENT`
- **多显示器支持** — 通过 VirtualScreen 获取完整桌面区域
- **睡眠唤醒恢复** — 帧间时间间隙检测 + 延迟重建 DWM + 物理重置
- **DWM 崩溃安全模式** — 连续 5 次重建失败跳过 DwmExtendFrameIntoClientArea
- **系统托盘（SystemTrayManager）** — `Shell_NotifyIcon`，左键隐藏/显示，右键菜单（开机自启/退出）
- **开机自启** — 注册表 `HKCU\...\Run`
- **进程互斥** — `Mutex("DesktopPet_Unity_SingleInstance")` 防止多实例

### ⚡ 性能优化
- **智能性能监控（PerformanceMonitor）** — 滚动 90 帧采样，三级自适应：
  - **High**：60fps，RT 100%
  - **Normal**：40fps，RT 75%
  - **Low**：20fps，RT 50%
- **CPU 监控** — Win32 `GetSystemTimes`
- **GPU 监控** — NVML（NVIDIA Management Library）
- **升档拦截** — CPU/GPU 占用 > 80% 阻止升档，> 92% 紧急降 Low
- **系统内存监控** — `GlobalMemoryStatusEx`，85% 预警 GC，93% 紧急降质
- **应用内存管理** — 800MB 触发 GC，1.2GB 强制 GC
- **崩溃日志** — `Application.logMessageReceived` 自动捕获，> 2MB 自动截断

## 🏗️ 项目结构

```
Desktop_per_pro/
├── build.ps1                        # 标准构建脚本（支持 -Quick 编译验证）
├── CHANGELOG.md                     # 版本更新日志
├── README.md                        # 本文件
├── Build/                           # 构建输出
│   ├── DesktopPet.exe               # 可执行文件
│   ├── DesktopPet_Data/             # 运行时资源
│   └── MonoBleedingEdge/            # Mono 运行时
├── code/
│   └── desktop_unity/               # Unity 项目根目录
│       ├── Assets/
│       │   ├── Scripts/             # 核心 C# 脚本（27 文件）
│       │   │   ├── DesktopPet.cs              # 物理引擎 + 地面状态机 + 崩溃监控
│       │   │   ├── Live2DRenderer.cs          # Live2D 渲染（~2500 行）
│       │   │   ├── DragHandler.cs             # 拖拽 + BallPanel/RightPanel 穿透管理
│       │   │   ├── BallPanel.cs               # 悬浮球辐射菜单
│       │   │   ├── RightPanel.cs              # 右侧 Widgets 面板
│       │   │   ├── WindowOverlay.cs           # DWM 透明窗口
│       │   │   ├── ChatManager.cs             # DeepSeek AI 对话
│       │   │   ├── ToolCallInvoker.cs         # 35+ 法术工具执行（Newtonsoft.Json）
│       │   │   ├── ChatBubble.cs              # 古风聊天气泡
│       │   │   ├── AutoChat.cs                # 自动问候与交互事件
│       │   │   ├── IdleChatGenerator.cs       # 闲话批量预生成
│       │   │   ├── TimeWeatherController.cs   # 时间与天气
│       │   │   ├── ActivityTracker.cs         # 法眼活动追踪
│       │   │   ├── PetMemory.cs               # 分层长期记忆 + 反思
│       │   │   ├── PetConfig.cs               # 天机簿配置
│       │   │   ├── ChatConfig.cs              # API Key 环境变量
│       │   │   ├── ReminderManager.cs         # 便签提醒 + Server酱³
│       │   │   ├── ServerPollService.cs       # 服务端推送轮询
│       │   │   ├── PerformanceMonitor.cs      # 性能自适应（FPS/CPU/GPU/内存）
│       │   │   ├── SystemTrayManager.cs       # 系统托盘
│       │   │   ├── DebugWindow.cs             # 调试调参面板
│       │   │   ├── ApiClient.cs               # 共享 HTTP 客户端（Newtonsoft.Json）
│       │   │   ├── BrowserTabReader.cs        # 浏览器标签读取
│       │   │   ├── MainThreadDispatcher.cs    # 主线程调度器
│       │   │   ├── IPetRenderer.cs            # 渲染器接口
│       │   │   ├── HybridRenderer.cs          # Live2D/3D 混合渲染
│       │   │   └── Model3DRenderer.cs         # 3D 模型渲染
│       │   │   └── Live2DFramework/
│       │   │       ├── ActionAgent/           # 15 文件 - 闭环动作系统
│       │   │       │   ├── MotionAgent.cs         # 自主动作决策引擎
│       │   │       │   ├── MotionTranslator.cs    # LLM 动作翻译（DeepSeek）
│       │   │       │   ├── MotionPlanner.cs       # 动作规划器（11 模板）
│       │   │       │   ├── MotionGenerator.cs     # 动作生成器（6 曲线插值）
│       │   │       │   ├── MotionMemoryManager.cs # 运动记忆引擎（30条+负反馈）
│       │   │       │   ├── DualModelValidator.cs  # GLM-4V 多帧拼图评分
│       │   │       │   ├── VisionMotionVerifier.cs # 视觉具身验证
│       │   │       │   ├── MotionVerifier.cs      # 三级测试验证
│       │   │       │   ├── LocalLLMClient.cs      # Ollama 本地 LLM 客户端
│       │   │       │   ├── GpuLoadMonitor.cs      # 游戏检测 + LLM 暂停
│       │   │       │   ├── EmotionState.cs        # 四维情绪模型
│       │   │       │   ├── SafetyValidator.cs     # 参数安全校验
│       │   │       │   ├── IdleActionScheduler.cs # 空闲动作调度
│       │   │       │   ├── ActionReferenceManager.cs # 动作参考图
│       │   │       │   └── AutoMotionCollector.cs  # [已废弃]
│       │   │       ├── ActionPresets/         # 6 文件 - 动作/表情预设
│       │   │       │   ├── Live2DActionController.cs
│       │   │       │   ├── ActionPreset.cs / ActionPresetPlayer.cs
│       │   │       │   ├── ExpressionManager.cs / ExpressionPreset.cs
│       │   │       │   └── Live2DActionTester.cs
│       │   │       └── 参数知识层             # 8 文件 - 参数语义映射
│       │   │           ├── ModelBodySchema.cs / RuntimeModelAnalyzer.cs
│       │   │           ├── Live2DModelAnalyzer.cs
│       │   │           ├── Live2DParameterMapper.cs
│       │   │           ├── ParameterKnowledgeProvider.cs
│       │   │           ├── ParameterRelationDetector.cs
│       │   │           ├── Live2DMotionTemplates.cs
│       │   │           └── KnownParameterPatterns.cs
│       │   ├── Editor/                 # 7 编辑器工具
│       │   │   ├── BuildScript.cs
│       │   │   ├── Live2DParameterVerifier.cs
│       │   │   ├── ParameterVisionScanner.cs
│       │   │   ├── Phase1Verifier.cs
│       │   │   ├── SelfTrainingManager.cs
│       │   │   ├── VisionVerifyWindow.cs
│       │   │   └── VisualActionTester.cs
│       │   ├── Resources/
│       │   │   ├── SystemPrompt.txt           # 符玄角色设定
│       │   │   └── Live2D/ParamMaps/          # 参数映射文件
│       │   ├── Scenes/scene.scene             # 主场景
│       │   └── StreamingAssets/               # Live2D 模型文件
│       ├── Packages/manifest.json              # 依赖管理
│       └── ProjectSettings/                    # Unity 项目设置
├── file/符玄/                                  # Live2D 模型源文件
├── project_brief/                              # 项目文档（LaTeX + PDF）
├── record/                                     # 开发记录
└── tools/                                      # 工具脚本
```

## 🔧 环境要求

| 依赖 | 说明 |
|------|------|
| 引擎 | Tuanjie 2022.3.62t7（`D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe`）|
| Live2D SDK | CubismSdkForUnity-5-r.4 |
| DeepSeek API Key | 环境变量 `DEEPSEEK_API_KEY`（必需） |
| GLM API Key | 环境变量 `GLM_API_KEY`（视觉分析 + 动作自评） |
| 和风天气 Key | 环境变量 `QWEATHER_API_KEY`（可选） |
| Ollama | 本地 LLM 决策（可选，推荐 qwen2.5:0.5b） |
| 系统 | Windows 10/11 64 位 |

## 🚀 快速开始

### 1. 配置环境变量

```powershell
# DeepSeek（必需）
setx DEEPSEEK_API_KEY "sk-your-key-here"

# 智谱 GLM（视觉分析 + 动作自评）
setx GLM_API_KEY "your-glm-key-here"

# 和风天气（可选）
setx QWEATHER_API_KEY "your-qweather-key-here"
```

> ⚠ 设置后需重新登录或重启电脑使变量生效。

### 2. 构建

```powershell
# 完整构建
.\build.ps1

# 仅验证编译（快速）
.\build.ps1 -Quick
```

### 3. 运行

```powershell
.\Build\DesktopPet.exe
```

首次运行后可从系统托盘右键菜单启用开机自启。

## 🏗️ 架构概览

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          Windows 桌面                                    │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │               DWM 透明窗口 (WS_EX_LAYERED)                         │  │
│  │                                                                    │  │
│  │  DesktopPet (物理引擎 + 地面状态机 + 崩溃监控)                     │  │
│  │     │                                                              │  │
│  │     ├── DragHandler (拖拽 + 穿透管理 + 挣扎动画)                  │  │
│  │     │    ├── BallPanel 区域 → 关闭穿透                             │  │
│  │     │    ├── RightPanel 区域 → 关闭穿透                            │  │
│  │     │    └── 释放 → 抛掷物理                                      │  │
│  │     │                                                              │  │
│  │     ├── BallPanel (悬浮球辐射菜单)                                 │  │
│  │     │    ├── ⚙ 设置 → PetConfig 持久化                            │  │
│  │     │    ├── 📊 报告 → MotionMemoryManager                        │  │
│  │     │    └── 📋 便签 → ReminderManager CRUD                       │  │
│  │     │                                                              │  │
│  │     ├── RightPanel (右侧 Widgets 面板)                             │  │
│  │     │    ├── 聊/设/签/告 工具按钮                                  │  │
│  │     │    └── 底部输入栏集成                                        │  │
│  │     │                                                              │  │
│  │     ├── AI 系统                                                    │  │
│  │     │    ├── ChatManager → DeepSeek API (Function Calling)         │  │
│  │     │    │    ├── 多轮工具回环 (≤5)                                │  │
│  │     │    │    └── 句子队列 → ChatBubble 逐句显示                   │  │
│  │     │    ├── ToolCallInvoker (35+ 工具, Newtonsoft.Json)          │  │
│  │     │    └── IdleChatGenerator (闲话预生成)                        │  │
│  │     │                                                              │  │
│  │     ├── 动作系统 (Live2DFramework)                                 │  │
│  │     │    ├── Live2DRenderer (Cubism SDK)                           │  │
│  │     │    │    ├── Perlin 微动 (7 通道) + 眨眼/呼吸/表情            │  │
│  │     │    │    ├── 走路/拖拽/点击/撞墙动画                          │  │
│  │     │    │    ├── IdleActionScheduler (12+ 空闲动作)               │  │
│  │     │    │    ├── 法阵/星辉硬编码特效                              │  │
│  │     │    │    └── 天气/时段表情联动                                │  │
│  │     │    ├── ActionAgent/ (闭环具身引擎)                           │  │
│  │     │    │    ├── MotionAgent (4 级密度 + 情绪状态机)              │  │
│  │     │    │    │    ├── 本地 LLM 决策 (Ollama Qwen2.5)             │  │
│  │     │    │    │    └── 失败回退 → 概率模式                        │  │
│  │     │    │    ├── MotionTranslator (DeepSeek 关键帧翻译)           │  │
│  │     │    │    ├── MotionPlanner (11 模板 + 6 曲线)                 │  │
│  │     │    │    ├── MotionGenerator (6 曲线插值播放)                  │  │
│  │     │    │    ├── DualModelValidator (GLM-4V 多帧拼图评分)         │  │
│  │     │    │    ├── MotionMemoryManager (30 条 + 负反馈 + 无望检测)  │  │
│  │     │    │    ├── SafetyValidator (互斥/对称/极端保护)             │  │
│  │     │    │    ├── GpuLoadMonitor (游戏检测 → 暂停 LLM)             │  │
│  │     │    │    ├── EmotionState (四维情绪模型)                      │  │
│  │     │    │    └── VisionMotionVerifier (10 测试)                   │  │
│  │     │    └── ActionPresets/ (6 组件)                               │  │
│  │     └── 感知系统                                                   │  │
│  │         ├── ActivityTracker (法眼, 2s 轮询)                        │  │
│  │         ├── BrowserTabReader (UIA 标签读取)                        │  │
│  │         ├── PetMemory (忆境, 3 层 30 条)                          │  │
│  │         └── TimeWeatherController (天象, 双源)                     │  │
│  │                                                                    │  │
│  └────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
```

### 闭环具身智能数据流

```
 ┌──────────┐    ┌──────────────┐    ┌──────────────┐    ┌─────────────┐
 │ MotionAgent │───▶ MotionTranslator ───▶ MotionPlanner ───▶ MotionGenerator
 │ (感知/决策) │    │ (DeepSeek 翻译) │    │ (11 模板/6 曲线)│    │ (关键帧插值) │
 └──────┬───────┘    └──────────────┘    └──────────────┘    └──────┬──────┘
        │                                                            │
        │   ┌──────────────────┐    ┌──────────────────────┐        │
        └───│ MotionMemoryManager │◀───│ DualModelValidator     │◀──────┘
            │ (30 条 + 负反馈) │    │ (GLM-4V 多帧拼图评分) │
            │ (无望检测 + 冷却) │    │ (20/40/60/80% 截图)    │
            └──────────────────┘    └──────────────────────┘
```

### 执行顺序

```
DesktopPet.Update(0)            → 物理更新、状态转换
CubismPhysicsController(800)    → 衣服/头发物理模拟
Live2DRenderer.LateUpdate(801)  → 覆盖被物理重置的参数 + 空闲动画 + 交互反馈
```

### 配置体系

```
环境变量:
  DEEPSEEK_API_KEY  ──▶ ChatManager / IdleChatGenerator / MotionTranslator
  GLM_API_KEY       ──▶ ToolCallInvoker (截图分析) / DualModelValidator
  QWEATHER_API_KEY  ──▶ TimeWeatherController

JSON 持久化 (Application.persistentDataPath):
  pet_config.json   ──▶ PetConfig (API 地址/模型/权重)
  pet_memory.json   ──▶ PetMemory (长期记忆)
  reminders.json    ──▶ ReminderManager (便签)
  motion_memory.json──▶ MotionMemoryManager (运动记忆)
  validation_log.json──▶ DualModelValidator (验证日志)
```

## 📝 环境变量

| 变量 | 必需 | 说明 |
|------|------|------|
| `DEEPSEEK_API_KEY` | ✅ | DeepSeek Chat API |
| `GLM_API_KEY` | ✅ | 智谱 GLM-4V 视觉分析 |
| `QWEATHER_API_KEY` | ❌ | 和风天气（回退 wttr.in） |
| `SERVERCHAN_KEY` | ❌ | Server酱³ 推送 |

## 📜 版本历史

详见 [CHANGELOG.md](CHANGELOG.md)

| 版本 | 日期 | 亮点 |
|------|------|------|
| **V2.3+** | 2026-07-13 | 移除 Qwen-VL + 多帧拼图评分；闭环学习 P0 修复；Newtonsoft.Json 迁移 |
| V2.3 | 2026-07 | 报告面板复制按钮；MotionAgent 自主动作决策 |
| V2.2 | 2026-07 | 悬浮球辐射菜单 + BallPanel 面板系统 |
| **V2.1** | 2026-07-09 | **悬浮球 + 辐射菜单** — BallPanel/RightPanel 取代右键菜单 |
| N18 | 2026-06-22 | 已完成任务视图 + 系统内存监控 |
| N17 | 2026-06-22 | 提醒去重 + 服务器推送去重 |
| N16 | 2026-06-20 | Everything 毫秒级文件搜索 |
| N15 | 2026-06-20 | DualModelValidator 双模型验证 |
| N14~N7 | 2026-06 | 多轮修复与迭代 |
| v0.2 | 2026-05 | 初始 Unity 版本 |

## 🤝 许可证

本项目为个人学习与娱乐用途，角色「符玄」版权属于 miHoYo / HoYoverse。

## 📝 致谢

- [Live2D Cubism SDK](https://www.live2d.com/sdk/about/cubism/)
- [DeepSeek API](https://platform.deepseek.com/)
- [智谱 GLM API](https://open.bigmodel.cn/)
- [Ollama](https://ollama.com/)
- [和风天气](https://www.qweather.com/)
- [wttr.in](https://wttr.in/)
- [Server酱³](https://sc3.ft07.com/)
- [Everything](https://www.voidtools.com/)
