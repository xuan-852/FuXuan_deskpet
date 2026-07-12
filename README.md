# 符玄桌面宠物 — Fu Xuan Desktop Pet

<div align="center">

![版本](https://img.shields.io/badge/版本-V2.1-blue)
![引擎](https://img.shields.io/badge/引擎-Tuanjie%202022.3.62t7-purple)
![平台](https://img.shields.io/badge/平台-Windows%2064位-green)
![Live2D](https://img.shields.io/badge/Live2D-Cubism%205--r.4-orange)
![AI](https://img.shields.io/badge/AI-DeepSeek%20Chat%20%7C%20GLM--4V-red)

</div>

## 📖 项目简介

**符玄桌面宠物** 是一个基于 Unity（团结引擎）和 Live2D Cubism SDK 构建的 Windows 桌面宠物应用。角色为《崩坏：星穹铁道》中的 **符玄**（仙舟「罗浮」太卜司之首），拥有完整的 AI 对话、Live2D 表情动作、物理交互、天气感知、日程管理等能力。

项目迭代至 **V2.1** 版本，引入了全新的悬浮球 + 右侧面板交互体系，取代了旧版右键菜单。

## ✨ 核心功能

### 🎨 Live2D 渲染
- **Live2D Cubism SDK 5-r.4** — 参数化变形 + 实时物理模拟
- **DWM 透明窗口** — 通过 `DwmExtendFrameIntoClientArea` 实现无缝桌面融合，无绿边
- **Perlin 噪声驱动** — 呼吸、身体微晃、头部微动、眼球转动，7 通道独立噪声
- **RenderTexture 叠层** — Layer 31 专用渲染层，支持透明叠加
- **3D 模型骨架** — `Model3DRenderer` + `Animator` 预留，支持 Live2D/3D 混合渲染（0.3s 交叉淡入淡出）

### 🤖 AI 对话系统
- **DeepSeek Chat API** — 完整 Function Calling 支持（37+ 法术工具）
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
- **桌面物理引擎** — 重力（gravity=1）、碰撞、地面检测、弹跳衰减
- **任意位置拖拽** — 拖拽 + 抛掷物理（多帧速度缓冲平均，`throwScale=0.5`，`maxThrowSpeed=12`）
- **拖拽挣扎动画** — 双臂划水、双腿交替、身体扭动、慌张表情，20+ 头发/裙子参数直接驱动绕过物理延迟
- **分区点击反馈** — 头部（摸头眯眼）/ 身体（戳胸惊讶）/ 腿部（害羞踢腿），通过 `CubismRaycaster` 检测
- **鼠标眼睛跟随** — 眼球平滑追踪鼠标位置（150px 触发距离）
- **屏幕边缘碰撞反弹** — 撞墙动画 + 反弹物理
- **地面任务状态机** — 5 种行为加权随机切换（MoveLeftEdge/MoveRightEdge/MoveLeftTime/MoveRightTime/StopTime）

### 🎭 动作系统（Live2DFramework/ActionAgent）
- **12 种空闲动作** — JSON 配置驱动（歪头/微笑/挑眉/星辉/伸懒腰/委屈/法阵/害羞/困惑/爱心/哭/心跳）
- **IdleActionScheduler** — 加权随机选择，支持时段/天气权重调整
- **走路动画** — 侧面转体 + 步态摆臂 + 呼吸加深，`WALK_SPEED_FACTOR=24f`
- **犯困表情** — 夜间眼皮渐沉、低头、微张嘴
- **天气表情联动** — 晴→微笑 / 雨→委屈 / 雪→好奇
- **法阵特效** — 弹簧-阻尼浮游物理 + Perlin 噪声
- **LLM 动作生成（MotionTranslator）** — 自然语言 → Live2D 关键帧序列（DeepSeek, temperature=0.3, timeout=30s）
- **6 种插值曲线** — Linear / Smooth / EaseOut / EaseIn / Hold / Bounce
- **MotionAgent** — 自主动作决策，4 级密度（High/Med/Low/Sleep），情绪状态机，焦点进程抑制
- **闭环自评** — DualModelValidator（GLM-4V 主评 + Qwen-VL-Plus 日志），`MotionMemoryManager` 上限 30 条，无望检测

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

### 🖥️ 交互界面（V2.1 全新）
- **悬浮球（BallPanel）** — 桌面右下角粉色 ✦ 悬浮球，单击展开辐射菜单
  - ⚙️ **设置面板**：任务权重滑块 + 保存/清空
  - 📊 **报告面板**：MotionMemoryManager 演武心经学习报告
  - 📋 **便签面板**：待办/已完成管理，CRUD 操作
  - 420×580px 浮动窗口，可拖拽标题栏，右键关闭
- **右侧面板（RightPanel）** — Windows 11 Widgets 风格
  - `~` 键切换展开/收起，鼠标划过右边缘自动展开（1s 自动隐藏延迟）
  - 展开 220px / 收起 8px，滑动速度 10f/s
  - 4 个工具按钮：聊（聚焦输入框）/ 设（设置）/ 签（便签）/ 告（报告）
  - 底部输入栏集成
- **底部输入栏（BottomInputBar）** — 370×72px，Windows 搜索风格，淡入动画
- **古风聊天气泡（ChatBubble）** — OnGUI 渲染，圆角 + 拖尾 + 12 星点装饰，符玄紫灰色主题
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
│       │   ├── Scripts/             # 核心 C# 脚本
│       │   │   ├── DesktopPet.cs              # 物理引擎 + 地面状态机 + 崩溃监控
│       │   │   ├── Live2DRenderer.cs          # Live2D 渲染（~2500 行）
│       │   │   ├── DragHandler.cs             # 拖拽 + BallPanel/RightPanel 穿透管理
│       │   │   ├── BallPanel.cs               # 🆕 悬浮球辐射菜单
│       │   │   ├── RightPanel.cs              # 🆕 右侧 Widgets 面板
│       │   │   ├── WindowOverlay.cs           # DWM 透明窗口
│       │   │   ├── ChatManager.cs             # DeepSeek AI 对话
│       │   │   ├── ToolCallInvoker.cs         # 37+ 法术工具执行
│       │   │   ├── ChatBubble.cs              # 古风聊天气泡
│       │   │   ├── BottomInputBar.cs          # 底部输入栏
│       │   │   ├── AutoChat.cs                # 自动问候与交互事件
│       │   │   ├── IdleChatGenerator.cs       # 闲话批量预生成
│       │   │   ├── TimeWeatherController.cs   # 时间与天气
│       │   │   ├── ActivityTracker.cs         # 法眼活动追踪
│       │   │   ├── PetMemory.cs               # 分层长期记忆 + 反思
│       │   │   ├── PetConfig.cs               # 天机簿配置
│       │   │   ├── ChatConfig.cs              # API Key 环境变量
│       │   │   ├── ReminderManager.cs         # 便签提醒 + Server酱³
│       │   │   ├── ServerPollService.cs       # 服务端推送轮询
│       │   │   ├── PerformanceMonitor.cs      # 性能自适应
│       │   │   ├── SystemTrayManager.cs       # 系统托盘
│       │   │   ├── DebugWindow.cs             # 调试调参面板
│       │   │   ├── HybridRenderer.cs          # Live2D/3D 混合
│       │   │   ├── Model3DRenderer.cs         # 3D 模型渲染
│       │   │   ├── ApiClient.cs               # 共享 HTTP 客户端
│       │   │   ├── BrowserTabReader.cs        # 浏览器标签读取
│       │   │   ├── MainThreadDispatcher.cs    # 主线程调度器
│       │   │   ├── IPetRenderer.cs            # 渲染器接口
│       │   │   └── Live2DFramework/           # 动作系统
│       │   │       ├── ActionAgent/           # 15 个动作代理组件
│       │   │       ├── ActionPresets/         # 6 个动作预设组件
│       │   │       └── 参数知识层             # 6 个参数语义组件
│       │   ├── Editor/
│       │   │   └── BuildScript.cs             # 构建脚本
│       │   ├── Resources/
│       │   │   └── SystemPrompt.txt           # 符玄角色设定
│       │   ├── Scenes/
│       │   │   └── scene.scene                # 主场景
│       │   └── StreamingAssets/              # Live2D 模型文件
│       ├── Packages/
│       │   └── manifest.json                  # 依赖管理
│       └── ProjectSettings/                   # Unity 项目设置
├── file/符玄/                                 # Live2D 模型源文件
├── project_brief/                             # 项目文档（LaTeX + PDF）
├── record/                                    # 开发记录
└── tools/                                     # 工具脚本
```

## 🔧 环境要求

| 依赖 | 说明 |
|------|------|
| 引擎 | Tuanjie 2022.3.62t7（`D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe`）|
| Live2D SDK | CubismSdkForUnity-5-r.4 |
| DeepSeek API Key | 环境变量 `DEEPSEEK_API_KEY`（必需） |
| GLM API Key | 环境变量 `GLM_API_KEY`（可选，视觉分析 + 动作自评） |
| 和风天气 Key | 环境变量 `QWEATHER_API_KEY`（可选） |
| 系统 | Windows 10/11 64 位 |

## 🚀 快速开始

### 1. 配置环境变量

```powershell
# DeepSeek（必需）
setx DEEPSEEK_API_KEY "sk-your-key-here"

# 智谱 GLM（可选）
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
┌───────────────────────────────────────────────────────────────────────┐
│                         Windows 桌面                                  │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │                DWM 透明窗口 (WS_EX_LAYERED)                     │  │
│  │                                                                 │  │
│  │  DesktopPet (物理引擎 + 地面状态机 + 崩溃监控)                  │  │
│  │     │                                                           │  │
│  │     ├── DragHandler (拖拽 + 穿透管理)                          │  │
│  │     │    ├── BallPanel 区域 → 关闭穿透                         │  │
│  │     │    ├── RightPanel 区域 → 关闭穿透                        │  │
│  │     │    ├── 面板关闭 → 强制重置穿透                           │  │
│  │     │    └── 释放 → 抛掷物理                                   │  │
│  │     │                                                           │  │
│  │     ├── BallPanel (悬浮球辐射菜单)                              │  │
│  │     │    ├── ⚙ 设置 → PetConfig 持久化                        │  │
│  │     │    ├── 📊 报告 → MotionMemoryManager                    │  │
│  │     │    └── 📋 便签 → ReminderManager CRUD                   │  │
│  │     │                                                           │  │
│  │     ├── RightPanel (右侧 Widgets 面板)                         │  │
│  │     │    ├── 聊/设/签/告 工具按钮                              │  │
│  │     │    └── 底部输入栏                                         │  │
│  │     │                                                           │  │
│  │     ├── AI 系统                                                 │  │
│  │     │    ├── ChatManager → DeepSeek API (Function Calling)      │  │
│  │     │    │    ├── 多轮工具回环 (≤5)                            │  │
│  │     │    │    └── 句子队列 → ChatBubble 逐句显示               │  │
│  │     │    ├── ToolCallInvoker (37+ 工具)                        │  │
│  │     │    └── IdleChatGenerator (闲话预生成)                    │  │
│  │     │                                                           │  │
│  │     ├── 动作系统                                                 │  │
│  │     │    ├── Live2DRenderer (Cubism SDK)                       │  │
│  │     │    │    ├── Perlin 微动 (7 通道) + 眨眼/呼吸/表情        │  │
│  │     │    │    ├── 走路/拖拽/点击/撞墙动画                      │  │
│  │     │    │    ├── IdleActionScheduler (12 种空闲)              │  │
│  │     │    │    ├── 法阵/星辉硬编码特效                          │  │
│  │     │    │    └── 天气/时段表情联动                            │  │
│  │     │    └── ActionAgent/                                      │  │
│  │     │         ├── MotionAgent (4 级密度 + 情绪状态机)          │  │
│  │     │         ├── MotionTranslator (LLM 关键帧翻译)            │  │
│  │     │         ├── MotionGenerator (6 曲线插值播放)              │  │
│  │     │         └── DualModelValidator (GLM-4V 评分)              │  │
│  │     │                                                           │  │
│  │     ├── 感知系统                                                 │  │
│  │     │    ├── ActivityTracker (法眼, 2s 轮询)                   │  │
│  │     │    ├── BrowserTabReader (UIA 标签读取)                   │  │
│  │     │    ├── PetMemory (忆境, 3 层 30 条)                     │  │
│  │     │    └── TimeWeatherController (天象, 双源)                │  │
│  │     │                                                           │  │
│  │     ├── 服务系统                                                 │  │
│  │     │    ├── ReminderManager (三级推送链)                      │  │
│  │     │    ├── ServerPollService (课表轮询)                      │  │
│  │     │    └── PerformanceMonitor (FPS/CPU/GPU 自适应)           │  │
│  │     │                                                           │  │
│  │     └── 系统集成                                                 │  │
│  │          ├── WindowOverlay (DWM + 多显示器 + 睡眠恢复)         │  │
│  │          └── SystemTrayManager (托盘 + 开机自启)               │  │
│  │                                                                 │  │
│  └─────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────┘
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
```

## 📜 版本历史

详见 [CHANGELOG.md](CHANGELOG.md)

| 版本 | 日期 | 主要变更 |
|------|------|---------|
| **V2.1** | 2026-07-09 | **悬浮球 + 辐射菜单** — BallPanel/RightPanel 取代右键菜单 |
| N32d | 2026-07-07 | 闭环具身智能 + GLM-4V 视觉验证 + 运动记忆 |
| N18  | 2026-06-22 | 已完成任务独立视图 + 系统总内存监控 |
| N17  | 2026-06-22 | 提醒去重 + 服务器推送去重 |
| N16  | 2026-06-20 | Everything 毫秒级文件搜索 |
| N15  | 2026-06-20 | 修复逐句显示 bug |
| N14  | 2026-06-20 | 课表小程序数据打通 + 3 个学业查询工具 |
| N13  | 2026-06-20 | 服务端推送轮询 + Server酱³ |
| N12  | 2026-06-19 | 性能监控 + 开机自启 |
| N11  | 2026-06-18 | 优先级气泡 + AI 回复稳定 |
| N10  | 2026-06-18 | 点击穿透 + 右键菜单 + 多轮工具调用 |
| v0.9 | 2026-06-18 | DWM 玻璃层 + 底部输入栏 |
| v0.8 | 2026-06-17 | 物理直接驱动 + 分区点击 + 挣扎动画 + 天气 |
| v0.7 | 2026-06-17 | 修复物理覆盖 + 执行顺序修正 |
| v0.6 | 2026-06-13 | LaTeX 文档 + 右键菜单 |
| v0.5 | — | 动作锁定 + 走路淡入 |
| v0.4 | — | 右键菜单 + 权重编辑 |
| v0.3 | — | 10 种空闲动作 + 加权随机 |
| v0.2 | — | PNG 渲染 + API Key 环境变量 |

## 🤝 许可证

本项目为个人学习与娱乐用途，角色「符玄」版权属于 miHoYo / HoYoverse。

## 📝 致谢

- [Live2D Cubism SDK](https://www.live2d.com/sdk/about/cubism/)
- [DeepSeek API](https://platform.deepseek.com/)
- [智谱 GLM API](https://open.bigmodel.cn/)
- [和风天气](https://www.qweather.com/)
- [wttr.in](https://wttr.in/)
- [Server酱³](https://sc3.ft07.com/)
- [Everything](https://www.voidtools.com/)
