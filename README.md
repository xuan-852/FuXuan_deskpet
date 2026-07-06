# 符玄桌面宠物 — Fu Xuan Desktop Pet

<div align="center">

![版本](https://img.shields.io/badge/版本-N32d-blue)
![引擎](https://img.shields.io/badge/引擎-Tuanjie%202022.3.62t7-purple)
![平台](https://img.shields.io/badge/平台-Windows%2064位-green)
![Live2D](https://img.shields.io/badge/Live2D-Cubism%205--r.4-orange)
![AI](https://img.shields.io/badge/AI-DeepSeek%20Chat%20%7C%20GLM--4V-red)

</div>

## 📖 项目简介

**符玄桌面宠物** 是一个基于 Unity（团结引擎）和 Live2D Cubism SDK 构建的 Windows 桌面宠物应用。角色为《崩坏：星穹铁道》中的 **符玄**（仙舟「罗浮」太卜司之首），拥有完整的 AI 对话、表情动作、天气感知、日程管理等能力。

> 项目迭代至 N32d 版本，历经多次架构重构和功能增强。

## ✨ 核心功能

### 🎨 渲染系统
- **Live2D 模型渲染** — Cubism SDK 5-r.4，高精度面部表情与物理模拟
- **DWM 透明窗口** — 使用 `DwmExtendFrameIntoClientArea` 实现无缝桌面融合，无绿边
- **Perlin 噪声驱动** — 呼吸、身体微晃、头部微动、眼球转动，自然不机械
- **3D 模型骨架** — 预留 `Model3DRenderer` + `Animator`，待后续启用

### 🤖 AI 对话系统
- **DeepSeek Chat API** — 完整 Function Calling 支持
- **27+ 法术工具** — 符玄可以用「法阵术式」操控电脑：
  - 🔭 **观星术**（打开网页/搜索信息）
  - 📷 **摄形术**（截取屏幕 + GLM-4V 视觉分析）
  - 🎵 **调音术**（调节音量/静音）
  - 🔒 **封印术**（锁屏/关机/重启）
  - 📢 **传音术**（桌面通知/剪贴板读写）
  - 🔍 **洞观术**（系统信息/Everything 毫秒级文件搜索）
  - 🚀 **开阵术**（启动应用/打开文件）
  - 📋 **卜算记事簿**（提醒管理）
  - 📚 **卜算传讯**（查询课表/成绩/考试）
  - 🎭 **演武术式**（播放表情/动作/生成新动作）
- **多轮工具调用** — 最多 5 轮回环，支持链式操作
- **句子队列逐句显示** — 长回复打字机效果
- **自动闲聊** — 无操作一段时间后主动搭话
- **底部输入栏** — Windows 搜索风格简洁输入

### 🏃 物理与交互
- **桌面物理引擎** — 重力、碰撞、地面检测、弹跳衰减
- **任意位置拖拽** — 拖拽 + 抛掷物理
- **拖拽挣扎动画** — 双臂划水 + 双腿交替 + 扭动 + 慌张表情
- **分区点击反馈** — 头部（摸头眯眼）/ 身体（戳胸惊讶）/ 腿部（害羞踢腿）
- **鼠标眼睛跟随** — 眼球平滑追踪鼠标位置
- **屏幕边缘碰撞反弹** — 撞墙动画 + 反弹物理

### 🎭 动作系统
- **11 种空闲动作** — JSON 配置驱动（歪头/微笑/挑眉/星辉/伸懒腰/委屈/法阵/害羞/困惑等）
- **走路动画** — 侧面转体 + 步态摆臂 + 呼吸加深
- **犯困表情** — 夜间/深夜低头、眼皮渐沉、微张嘴
- **天气表情联动** — 晴→微笑 / 雨→委屈 / 雪→好奇
- **LLM 动作生成** — `MotionTranslator` 将自然语言描述翻译为 Live2D 关键帧序列
- **闭环自评** — `VisionMotionVerifier` 调用 GLM-4V 视觉模型评估动作质量，自动优化

### 🧠 记忆与感知
- **「法眼」活动追踪** — 轮询前台窗口，按分类（编程/游戏/学习/浏览等）累计时长
- **分层长期记忆** — 核心事实始终保留 + Top-N 重要记忆 + 近期琐事
- **反思反射** — 重要性积分累计达阈值时触发 LLM 提炼洞察
- **多窗口环境感知** — 扫描所有可见窗口
- **浏览器标签深度感知** — 通过 UI Automation 读取标签页标题

### 🌤️ 时间与天气
- **昼夜感知** — 自动检测系统时间，isNight/isSleepyTime
- **双天气源** — wttr.in（无需 Key）或 和风天气（需注册）
- **AI 天气语录** — DeepSeek 生成符玄风格的天气台词
- **待机气泡联动** — 时间/天气特化文案

### 📋 便签与提醒
- **本地 JSON 持久化** — 增删改查
- **重复规则** — 每日/工作日/每周
- **到期气泡提醒** + Windows Toast 通知
- **Server酱³ 手机推送**
- **服务端同步** — 与课表小程序联动
- **AI 创建便签** — 对话中说"提醒我…"即可

### 🖥️ 窗口系统
- **DWM 透明窗口** — 纯黑 (0,0,0,0) 背景 + 玻璃层扩展
- **点击穿透** — 宠物外点击穿透到桌面，宠物内正常交互
- **多显示器支持** — 虚拟桌面全覆盖
- **睡眠唤醒恢复** — 时间间隙检测 + 延迟重建 DWM
- **DWM 崩溃安全模式** — 连续崩溃跳过重建
- **系统托盘** — 左键隐藏/显示 + 右键菜单（开机自启/退出）

### ⚡ 性能优化
- **智能性能监控** — FPS 自适应降档/升档（High 60fps / Normal 40fps / Low 20fps）
- **RenderTexture 分辨率缩放** — 性能不足时自动降低
- **系统内存监控** — 85% 预警 GC，93% 紧急降质保命
- **崩溃日志捕获** — 自动记录 + 超限截断

## 🏗️ 项目结构

```
Desktop_per_pro/
├── build.ps1                    # 标准构建脚本
├── CHANGELOG.md                 # 版本更新日志
├── README.md                    # 本文件
├── Build/                       # 构建输出目录
│   └── DesktopPet.exe           # 可执行文件
├── code/
│   └── desktop_unity/           # Unity 项目根目录
│       ├── Assets/
│       │   ├── Scripts/         # 核心 C# 脚本
│       │   │   ├── DesktopPet.cs           # 物理引擎与地面状态机
│       │   │   ├── Live2DRenderer.cs       # Live2D 渲染（~2500行核心）
│       │   │   ├── WindowOverlay.cs        # DWM 透明窗口
│       │   │   ├── DragHandler.cs          # 拖拽与抛掷
│       │   │   ├── ChatManager.cs          # DeepSeek AI 对话
│       │   │   ├── ToolCallInvoker.cs      # 27+ 法术工具
│       │   │   ├── ChatBubble.cs           # 古风聊天气泡（OnGUI）
│       │   │   ├── ContextMenu.cs          # 右键菜单
│       │   │   ├── BottomInputBar.cs       # 底部输入栏
│       │   │   ├── AutoChat.cs             # 自动问候与交互事件
│       │   │   ├── IdleChatGenerator.cs    # 闲话/问候动态生成
│       │   │   ├── TimeWeatherController.cs # 时间与天气
│       │   │   ├── ActivityTracker.cs      # 法眼活动追踪
│       │   │   ├── PetMemory.cs            # 分层长期记忆
│       │   │   ├── PetConfig.cs            # 配置持久化
│       │   │   ├── ReminderManager.cs      # 便签提醒
│       │   │   ├── ServerPollService.cs    # 服务端轮询
│       │   │   ├── PerformanceMonitor.cs   # 性能自适应
│       │   │   ├── SystemTrayManager.cs    # 系统托盘
│       │   │   ├── HybridRenderer.cs       # Live2D/3D 混合管理
│       │   │   ├── Model3DRenderer.cs      # 3D 模型渲染（预留）
│       │   │   ├── ApiClient.cs            # 共享 HTTP 客户端
│       │   │   ├── BrowserTabReader.cs     # UIA 浏览器标签读取
│       │   │   ├── DebugWindow.cs          # 调试调参面板
│       │   │   ├── IPetRenderer.cs         # 渲染器接口
│       │   │   └── Live2DFramework/        # LLM 动作系统
│       │   │       ├── ActionAgent/        # 动作代理核心
│       │   │       │   ├── MotionTranslator.cs    # LLM 动作翻译
│       │   │       │   ├── MotionGenerator.cs     # 动作播放器
│       │   │       │   ├── MotionPlanner.cs       # 动作规划器
│       │   │       │   ├── MotionVerifier.cs      # 动作验证器
│       │   │       │   ├── VisionMotionVerifier.cs # GLM-4V 视觉验证
│       │   │       │   ├── MotionMemoryManager.cs  # 动作记忆
│       │   │       │   ├── IdleActionScheduler.cs  # 空闲动作调度
│       │   │       │   └── SafetyValidator.cs      # 安全验证
│       │   │       ├── ActionPresets/      # 动作预设
│       │   │       ├── Live2DParameterMapper.cs    # 参数语义映射
│       │   │       └── ParameterKnowledgeProvider.cs # 参数知识注入
│       │   ├── Editor/
│       │   │   └── BuildScript.cs          # 自动化构建脚本
│       │   ├── Resources/
│       │   │   └── SystemPrompt.txt        # 符玄角色设定
│       │   ├── Animations/                # 3D 模型动画
│       │   ├── Models/                    # 3D FBX 模型
│       │   ├── Prefabs/                   # Prefab 预制体
│       │   └── StreamingAssets/           # Live2D 模型文件
│       └── ProjectSettings/               # Unity 项目设置
├── file/符玄/                             # Live2D 模型源文件
├── project_brief/                         # 项目文档
├── record/                                # 开发记录
└── tools/                                 # 工具脚本
```

## 🔧 环境要求

| 依赖 | 版本/路径 |
|------|-----------|
| Unity 引擎 | Tuanjie 2022.3.62t7（`D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe`）|
| Live2D SDK | CubismSdkForUnity-5-r.4 |
| DeepSeek API Key | 环境变量 `DEEPSEEK_API_KEY` |
| GLM API Key（可选） | 环境变量 `GLM_API_KEY`（视觉分析用） |
| 和风天气 Key（可选） | 环境变量 `QWEATHER_API_KEY` |
| 操作系统 | Windows 10/11 64位 |

## 🚀 快速开始

### 1. 配置 API Key

设置系统环境变量（用户变量即可）：

```powershell
# DeepSeek（必需 — AI 对话）
DEEPSEEK_API_KEY=sk-your-key-here

# 智谱 GLM（可选 — 截图视觉分析、动作自评）
GLM_API_KEY=your-glm-key-here

# 和风天气（可选 — 更准确的天气数据）
QWEATHER_API_KEY=your-qweather-key-here
```

> 设置后需重启电脑或重新登录使变量生效。

### 2. 构建

```powershell
# 完整构建
.\build.ps1

# 仅验证编译（不输出可执行文件）
.\build.ps1 -Quick
```

### 3. 运行

```powershell
.\Build\DesktopPet.exe
```

## 🏗️ 架构概览

### 核心架构图

```
┌─────────────────────────────────────────────────────────┐
│                     Windows 桌面                          │
│  ┌───────────────────────────────────────────────────┐  │
│  │          DWM 透明窗口 (WS_EX_LAYERED)              │  │
│  │  ┌──────────────────────────────────────────────┐  │  │
│  │  │  DesktopPet (物理引擎 + 状态机)                │  │  │
│  │  │  ├── 重力/碰撞/地面检测                        │  │  │
│  │  │  ├── 地面任务状态机 (行走/停止/边缘)            │  │  │
│  │  │  └── 崩溃日志/内存监控                         │  │  │
│  │  │                                                │  │  │
│  │  │  +── DragHandler (拖拽+抛掷)                    │  │  │
│  │  │  +── HybridRenderer (渲染调度)                  │  │  │
│  │  │  │   ├── Live2DRenderer (Cubism SDK)           │  │  │
│  │  │  │   │   ├── Perlin 噪声微动                   │  │  │
│  │  │  │   │   ├── 眨眼/呼吸/表情                    │  │  │
│  │  │  │   │   ├── 走路/拖拽/点击动画                │  │  │
│  │  │  │   │   ├── 空闲动作 (JSON 调度器)            │  │  │
│  │  │  │   │   ├── 法阵/星辉 (硬编码)               │  │  │
│  │  │  │   │   └── LLM 动作生成器                   │  │  │
│  │  │  │   └── Model3DRenderer (预留)               │  │  │
│  │  │  │                                                │  │  │
│  │  │  +── AI 系统                                     │  │  │
│  │  │  │   ├── ChatManager (DeepSeek API)              │  │  │
│  │  │  │   │   ├── Function Calling (27+ 工具)         │  │  │
│  │  │  │   │   ├── 多轮工具回环 (≤5)                  │  │  │
│  │  │  │   │   └── 句子队列逐句显示                    │  │  │
│  │  │  │   ├── ToolCallInvoker (法术工具执行)          │  │  │
│  │  │  │   ├── IdleChatGenerator (闲话生成)            │  │  │
│  │  │  │   └── SystemPrompt.txt (角色设定注入)         │  │  │
│  │  │  │                                                │  │  │
│  │  │  +── 感知系统                                    │  │  │
│  │  │  │   ├── ActivityTracker (法眼)                  │  │  │
│  │  │  │   ├── BrowserTabReader (浏览器标签)           │  │  │
│  │  │  │   ├── PetMemory (忆境·长期记忆)              │  │  │
│  │  │  │   └── TimeWeatherController (天象)            │  │  │
│  │  │  │                                                │  │  │
│  │  │  +── 服务系统                                    │  │  │
│  │  │  │   ├── ServerPollService (课表轮询)            │  │  │
│  │  │  │   ├── ReminderManager (卜算记事簿)            │  │  │
│  │  │  │   └── PerformanceMonitor (性能自适应)         │  │  │
│  │  │  │                                                │  │  │
│  │  │  +── UI 层                                       │  │  │
│  │  │      ├── ChatBubble (OnGUI 气泡)                 │  │  │
│  │  │      ├── ContextMenu (右键菜单)                  │  │  │
│  │  │      ├── BottomInputBar (底部输入栏)             │  │  │
│  │  │      ├── SystemTrayManager (托盘)                │  │  │
│  │  │      └── DebugWindow (调试面板)                  │  │  │
│  │  └──────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### 数据流

```
用户操作 (点击/拖拽/说话)
    │
    ▼
DragHandler / BottomInputBar
    │
    ├──▶ DesktopPet (物理更新) ──▶ HybridRenderer ──▶ Live2DRenderer
    │
    └──▶ ChatManager (AI 对话)
              │
              ├──▶ ToolCallInvoker (执行法术)
              │       ├── 打开网页/搜索
              │       ├── 截图 + GLM-4V 分析
              │       ├── 系统控制 (音量/锁屏/关机)
              │       ├── 文件搜索 (Everything)
              │       ├── 便签管理
              │       ├── 课表查询
              │       └── 动作/表情播放
              │
              └──▶ 回复文本 ──▶ ChatBubble (逐句显示)
```

## 📜 版本历史

详见 [CHANGELOG.md](CHANGELOG.md)

| 版本 | 日期 | 主要变更 |
|------|------|---------|
| N32d | 2026-07-07 | ACTION_PATTERNS 升级 + SPECIAL PATTERNS 修复 + 闭环自评 |
| N18  | 2026-06-22 | 已完成任务独立视图 + 系统总内存监控 |
| N17  | 2026-06-22 | 提醒去重 + 服务器推送去重 |
| N16  | 2026-06-20 | Everything 毫秒级文件搜索 |
| N15  | 2026-06-20 | 修复逐句显示 bug |
| N14  | 2026-06-20 | 课表小程序数据打通 + 3 个学业查询工具 |
| N13  | 2026-06-20 | 服务端推送轮询 + Server酱³ | 
| N12  | 2026-06-19 | 性能监控 + 开机自启 |
| N11  | 2026-06-18 | 优先级气泡系统 + AI 回复稳定 |
| N10  | 2026-06-18 | 点击穿透 + 右键菜单 + 多轮工具调用 |
| v0.9 | 2026-06-18 | DWM 玻璃层透明 + 底部输入栏 |
| v0.8 | 2026-06-17 | 头发/裙子直接物理驱动 + 分区点击 + 挣扎动画 |
| v0.7 | 2026-06-17 | 修复物理覆盖 + 执行顺序修正 |
| v0.6 | 2026-06-13 | LaTeX 文档 + 右键菜单 + 动作锁定 |
| v0.5 | — | 动作锁定 + 走路淡入 |
| v0.4 | — | 右键菜单 + 权重编辑 |
| v0.3 | — | 10 种空闲动作 + 加权随机 |
| v0.2 | — | PNG 渲染 + API Key 从环境变量读取 |

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
│   │   │   ├── ChatConfig.cs         # API Key + 端点集中配置（环境变量读取）
│   │   │   ├── ApiClient.cs          # 共享 HTTP/JSON 客户端（PostRequest + 解析工具）
│   │   │   ├── IdleChatGenerator.cs  # 自动闲聊（重构后使用 ApiClient）
│   │   │   ├── ActivityTracker.cs    # 法眼 — 窗口/多窗口/浏览器深度感知
│   │   │   ├── BrowserTabReader.cs   # 浏览器标签页读取（UIA 反射，零安装）
│   │   │   ├── BottomInputBar.cs     # 底部输入栏
│   │   │   ├── ContextMenu.cs        # 右键菜单（设置/动作/聊天/便签）
│   │   │   ├── ReminderManager.cs    # 便签提醒 + Server酱³ 推送
│   │   │   ├── ServerPollService.cs  # 服务端推送轮询 + 数据查询
│   │   │   ├── PerformanceMonitor.cs # FPS/CPU/内存监控
│   │   │   ├── SystemTrayManager.cs  # 系统托盘图标
│   │   │   ├── DebugWindow.cs        # 调试调参面板
│   │   │   ├── WindowOverlay.cs      # 透明窗口（DWM DwmExtendFrameIntoClientArea）
│   │   │   ├── IPetRenderer.cs       # 渲染接口
│   │   │   ├── HybridRenderer.cs     # 混合渲染器
│   │   │   ├── Model3DRenderer.cs    # 3D 渲染器
│   │   │   ├── ToolCallInvoker.cs    # AI 工具调用分发（28+ 工具，含协程异步执行）
│   │   │   ├── PetConfig.cs          # 天机簿 — 配置持久化（JSON保存/加载）
│   │   │   ├── PetMemory.cs          # 忆境 — 长期记忆系统（记忆摘要+JSON持久）
│   │   ├── Animations/               # 3D 模型动画
│   │   ├── Editor/                   # 构建脚本
│   │   ├── Live2D/Models/Fuxuan/     ← 符玄 Live2D 模型
│   │   ├── Models/Fuxuan/            # 3D FBX 模型
│   │   ├── Prefabs/                  # 预制体
│   │   ├── Scenes/SampleScene.scene  # 主场景
│   │   ├── StreamingAssets/          # 流式资源
│   │   └── Resources/                # 运行时加载资源
│   ├── Packages/
│   │   └── manifest.json             # 依赖管理
│   └── ProjectSettings/              # Unity 项目设置
├── file/                             # 原始模型文件（gitignored）
├── project_brief/                    # 设计文档 (LaTeX + PDF)
└── README.md
```

## 📜 脚本架构

```
DesktopPet (主控制器, Update order=0)
├── DragHandler          ← 鼠标交互 / 点击穿透
├── ChatBubble           ← 头顶气泡（含优先级系统）
├── ChatManager          ← AI 对话 + Function Calling + 逐句队列 + 协程调度
│   ├── 加载 Resources/SystemPrompt.txt + 注入 PetMemory 记忆
│   └── 调用 ApiClient (共享 HTTP/JSON 客户端)
├── IdleChatGenerator    ← 自动闲聊（使用 ApiClient）
├── ActivityTracker      ← 法眼 — 窗口/多窗口/浏览器深度感知
│   └── BrowserTabReader  ← UIA 反射读取标签页
├── BottomInputBar       ← 底部输入栏
├── ContextMenu          ← 右键菜单
├── TimeWeatherController ← 时间/天气（Key 从 ChatConfig 环境变量读取）
├── ReminderManager      ← 便签提醒 + Server酱³ 推送
├── ServerPollService    ← 服务端轮询 + 小程序数据查询
├── PerformanceMonitor   ← FPS/CPU/内存监控
├── SystemTrayManager    ← 系统托盘图标
├── DebugWindow          ← 调试面板
├── PetConfig            ← 天机簿 — 配置持久化（JSON保存/加载，重启时 ApplyAll）
│   └── 注：仅持久化运行时偏好，API Key 专用 ChatConfig 环境变量
├── PetMemory            ← 忆境 — 长期记忆系统（自动记录关键交互，注入 system prompt）
├── WindowOverlay        ← 透明窗口（DWM 玻璃层）
└── Live2DRenderer (IPetRenderer, Update order=801)
    └── CubismPhysicsController (order=800) ← 物理

**配置中心：**
ChatConfig (静态类) ← 环境变量（DEEPSEEK_API_KEY / GLM_API_KEY / QWEATHER_API_KEY）
├── ChatManager          ← 读取 DeepSeek BaseUrl + Key
├── TimeWeatherController ← 读取 QWeatherApiKey
└── ApiClient            ← 共享 PostRequest / 解析工具（被 ChatManager + IdleChatGenerator 使用）
```

**执行顺序：**
1. `DesktopPet.Update(0)` → 状态更新（位置、速度、行走相位）
2. `CubismPhysicsController.Update(800)` → 衣服/头发物理模拟
3. `Live2DRenderer.LateUpdate(801)` → 覆盖被物理重置的参数 + 空闲动画 + 交互反馈

### ⚡ 协程异步工具架构

AI 工具调用中，网络 IO 型工具（5 个）通过 Unity 协程异步执行，不阻塞主线程：

```
ChatManager.SendMessage()
  └─ StartCoroutine(SendRequestCoroutine())
       └─ StartCoroutine(DoToolLoop())   ← 最多 5 轮 tool_call 循环
            ├─ StartCoroutine(PostRequest())  ← HTTP 请求
            ├─ 解析 tool_calls
            ├─ IsCoroutineTool()? ═╗
            │   YES ──→ StartCoroutine(ToolCallInvoker.ExecuteCoroutine())
            │   NO  ──→ Execute() 同步执行
            └─ 继续下一轮 / 结束

ToolCallInvoker (协程工具注册表)
├── RegisterCoroutineTools()
│   ├── query_exams       → RunAsyncTool(ServerPollService.QueryUpcomingExamsAsync)
│   ├── query_scores      → RunAsyncTool(ServerPollService.QueryScoresAsync)
│   ├── query_schedule    → RunAsyncTool(ServerPollService.QueryScheduleAsync)
│   ├── query_user_status → RunAsyncTool(ServerPollService.QueryUserStatusAsync)
│   └── search_files      → RunAsyncTool(Task.Run → SearchFilesTask)
│
├── RunAsyncTool(Func<Task<string>>)
│   └── 每帧 yield return null 检查 task.IsCompleted ← 非阻塞
│
├── GetCoroutineResult()  → 获取异步执行结果
└── IsCoroutineTool()     → 判断工具是否走协程
```

### 🧠 长期记忆 + 配置持久化架构

```
PetMemory (忆境, 单例, DontDestroyOnLoad)
├── AddMemory(summary, topic)  ← OnToolResult 自动调用
│   └── 同话题冷却 120s 防重复
├── GetFormattedMemories()      → 注入到 SystemPrompt
├── Save() / Load()             → pet_memory.json
└── ClearMemories()             → 右键菜单按钮

PetConfig (天机簿, 单例, DontDestroyOnLoad)
├── CollectAll()  ← 从 ChatManager/TimeWeatherController/DesktopPet 读取
├── Save()        → pet_config.json
├── Load()        ← 启动时自动加载
└── ApplyAll()    → 写入各组件覆盖 Inspector 默认值

ChatManager (SystemPrompt 注入流程)
├── Resources/SystemPrompt.txt … 静态 prompt 模板
├── {current_time} … 运行时替换为 DateTime.Now
└── PetMemory.Instance?.GetFormattedMemories() … 追加到末尾
```

**三个系统的协作流程：**
1. 启动 → PetConfig.Load() → PetConfig.ApplyAll() 恢复权重/API/天气
2. 启动 → ChatManager.Awake() 加载 SystemPrompt.txt
3. 对话 → BuildSystemPrompt() 替换 {current_time} + 注入记忆
4. 工具调用 → OnToolResult → PetMemory.AddMemory() 自动记录
5. 右键菜单「保存配置」→ PetConfig.CollectAll() + Save()
6. 右键菜单「清空忆境」→ PetMemory.ClearMemories()

**协程 vs 同步的分界策略：**
- **同步执行**（23+ 个工具）：`open_url`、`take_screenshot`、`set_volume` 等瞬时完成的系统操作
- **协程异步执行**（5 个工具）：`query_exams`、`query_scores`、`query_schedule`、`query_user_status`、`search_files` —— 涉及 HTTP 请求或后台线程文件搜索，可能耗时数百毫秒到数秒
- `RunAsyncTool` 将 `async Task` 包装为协程，每帧检测 `IsCompleted`，不阻塞 Unity 主线程

## 🔧 开发环境

| 工具 | 版本 |
|---|---|
| Unity | 2022.3.62 LTS |
| Live2D Cubism SDK | 5-r.4 |
| .NET / C# | .NET Framework 4.x / C# 9.0 |
| Windows | 10/11 |
| IDE | Visual Studio 2022 / VS Code |

## 🚀 快速开始

1. **克隆仓库**
   ```bash
   git clone https://github.com/xuan-852/Desktop_per_pro.git
   ```

2. **导入 Live2D Cubism SDK**
   - 从 [Live2D 官网](https://www.live2d.com/sdk/about/) 下载 Cubism SDK 5-r.4
   - 导入到 Unity 项目中

3. **放置模型**
   - 将 符玄 Live2D 模型文件放到 `Assets/Live2D/Models/Fuxuan/` 目录下
   - Cubism SDK 会自动生成 Prefab

4. **在 Unity 中打开场景** `Assets/Scenes/SampleScene.scene`
   - 检查 `DesktopPet` 对象的 Inspector 中 `Live2DRenderer.modelPrefab` 是否已引用

5. **设置环境变量（API Key 安全存储，v0.12+）**
   - 打开「编辑系统环境变量」→「用户变量」→「新建」
   - `DEEPSEEK_API_KEY` = 你的 DeepSeek API Key
   - `GLM_API_KEY` = 你的智谱 GLM API Key（用于截图分析）
   - 设置后重启电脑或重新登录使变量生效
   - 参见 `Assets/Scripts/ChatConfig.cs.example`

6. **运行** → 点击 Play

## 📦 依赖

- [Live2D Cubism SDK 5-r.4](https://www.live2d.com/sdk/about/)
- Newtonsoft.Json（Unity 包管理器安装）
- Unity UI (UGUI)
- [Everything CLI (es.exe)](https://www.voidtools.com/downloads/)（可选）— 用于毫秒级全盘文件搜索，下载 `ES-1.1.0.30.x64.zip` 解压到 `%LOCALAPPDATA%\Everything\es.exe` 即可

## 📄 许可证

本项目仅用于个人学习和技术研究，严禁商业用途。

- Live2D 模型版权归 © 米哈游（崩坏：星穹铁道）所有
- Cubism SDK 版权归 © Live2D Inc. 所有

## 📚 参考

- [Live2D Cubism SDK 文档](https://docs.live2d.com/)
- [Unity 透明窗口实现](https://github.com/XJINE/Unity_TransparentWindowManager)
- 原 GDI+ 桌宠项目：`D:\C\Desktop pet\`
- 流萤 Live2D 模型来源：[B站@是依七哒](https://space.bilibili.com/457683484) / [Scighost/Firefly](https://github.com/Scighost/Firefly)

## ⚠️ 已知问题

### 1. 窗口 Z 顺序问题

**现象：** 拖动任意普通窗口（如 Edge、文件资源管理器）时，窗口会出现在底部输入栏与任务栏之间的透明间隙中。效果上违背了 `[任务栏] → [输入框] → [窗口] → [宠物]` 的期望层级。

**原因：** Unity 窗口为全屏 TOPMOST（保证宠物永远在顶层），所有非 TOPMOST 的普通窗口都渲染在 Unity 窗口之下。任务管理器等自带 TOPMOST 属性的窗口不受影响。

**尝试过的方案：**
- `SPI_GETWORKAREA` 限制窗口到工作区 → 宠物被任务栏遮挡
- 白底背景延伸到屏幕底部 → Edge 等窗口被白底"切断"，视觉更差
- 半透明遮罩延伸到屏幕底部 → 仅缓解，未从根本上解决问题

**根本矛盾：** 全屏 TOPMOST 窗口无法同时满足"普通窗口在宠物之上"和"宠物在任务栏之上"。需引入更精细的窗口管理策略（如动态调整窗口大小/位置、多窗口分层渲染、或 Hook 窗口 Z 顺序事件）方可解决。

**状态：** ⏳ 待后续研究

### 2. 空闲动作不够自然

**现象：** 部分空闲动作（尤其是过渡衔接时）生硬不自然，整体流畅度仍有提升空间。

**原因：** 当前动作系统为纯参数插值（缓入缓出曲线），缺少对 Live2D 模型自身 BlendShape 混合的精细控制。动作切换时的参数冲突（如前一个动作的参数尚未完全归零，新动作已经开始叠加）导致视觉上的"跳变"感。

**涉及动作：** 歪头、微笑、挑眉、星辉、伸懒腰、爱心眼、数钱、委屈、法阵、害羞、困惑共 11 种。

### 3. AI 活动感知与性格优化（待改进）

**现象：** AI 对用户活动的感知粗粒度（仅 8 类：coding/gaming/studying/browsing/entertainment/communication/idle/other），
窗口标题被丢弃未传给 AI；AI 回复存在说教感和攻击性，"傲娇"特质被过度放大。

**根因与改进方向：**

| 维度 | 现状 | 改进目标 |
|------|------|---------|
| **活动感知粒度** | 8 个笼统分类，用 ActivityTracker.PollForeground() 做 GetForegroundWindow 快照 | ✅ 窗口标题已实时注入 SystemPrompt，DeepSeek 可自行理解用户当前活动（如"VS Code 写 Python"、"Edge 看 B 站"） |
| **多窗口感知** | 仅追踪最顶层窗口，`EnumWindows` 未使用 | ✅ 已实现 EnumWindows 枚举所有可见窗口，每 10s 刷新多窗口摘要注入 SystemPrompt |
| **浏览器深度** | 仅识别进程名（如 msedge.exe） | ✅ 已集成 Windows UI Automation 读取浏览器标签页标题，无需安装任何插件 |
| **性格温婉约束** | SystemPrompt.txt 缺少"不要评判/说教"约束 | ✅ 已加入温婉守则章节 |
| **傲娇权重** | IdleChatGenerator 的 system prompt 中"傲娇"权重过高 | ✅ 已平衡为 3:7（傲:娇），语气更温柔 |
| **窗口标题利用** | Classify() 后丢弃标题 | ✅ 已实时注入 SystemPrompt，AI 可感知当前窗口 |

**参考开源方案：** [ActivityWatch](https://github.com/ActivityWatch/aw-watcher-window)（⭐ 14k+）是最大的开源时间追踪项目，但其 Windows 实现同样使用
`GetForegroundWindow` 单窗口追踪。改进方向可借鉴其生态中的浏览器扩展（aw-watcher-web）获取标签页 URL，
以及 aw-watcher-afk 组件检测空闲状态。

**状态：** 🔧 持续调整中 — API Key 安全化 ✅、活动感知 ✅、性格温婉约束 ✅、傲娇平衡 ✅、配置链路加固 ✅、多窗口感知 ✅、浏览器深度 ✅、API 客户端提取+配置中心化 ✅

### 4. 构建脚本编码问题

**现象：** `build.ps1` 在 Windows PowerShell 5.1 上因 UTF-8 without BOM 编码导致语法解析错误，`&` 操作符未被正确识别。

**原因：** PowerShell 5.1 默认无法正确解析不含 BOM 的 UTF-8 脚本文件。VS Code 默认以 UTF-8 without BOM 保存。

**临时方案：** 直接调用 Unity 可执行文件绕过脚本。

### 3. 视觉特效已移除

**现象：** 动作 4（星辉环绕）和动作 9（法阵显现）的视觉特效（紫环旋转、黑幕、眼镜发光、星星闪烁、七星盘、白圈）已被移除，仅保留动作时长和空壳函数。动作 7（数钱）的"双眼放光"参数也可能受影响。

**原因：** 过渡到 DWM 黑色透明方案后，半透明特效像素与黑色背景不兼容。在 RGBA 渲染中，半透明特效像素与黑色 (0,0,0) 背景混合后会变暗或消失，无法正确显示。

**影响范围：**
- 动作 4（星辉）：紫环旋转、黑幕、眼镜发光、星星闪烁 → 全部移除
- 动作 9（法阵）：黑幕、白圈、七星盘、眼镜发光、镜头缩放 → 全部移除，仅保留五阶段手势变化
- 动作 7（数钱）：Param121/137/132 双眼放光 → 显示效果受限

**解决方向：** 需要实现独立的半透明叠加层渲染（不依赖 Unity 主窗口的黑色透明），或在 DWM 外部另建一个半透明覆盖窗口来渲染特效。

**状态：** ⏳ 待后续研究

### 4. 3D 渲染器未实现

**现象：** `Model3DRenderer.cs` 已挂载但处于 disabled 状态，3D 渲染通路仅有接口骨架，无法实际使用。

**背景：** 项目中设计了 `IPetRenderer` 接口和 `HybridRenderer` 混合渲染器，意图在 Live2D（精细表情）和 3D（走路/飞行动画）之间切换。当前仅 Live2D 渲染器活跃，所有走路/飞行状态也由 Live2D 模拟完成。

**影响：**
- `HybridRenderer` 的渲染模式 2（强制 3D）不可用
- 走路/飞行动画无法使用 3D 骨骼动画
- `Model3DRenderer` 的背景同样设为黑色以跟随 DWM 透明方案，但无实际渲染内容

**状态：** ⏳ 待后续实现

### 5. 困惑动作（11 号）权重为 0

**现象：** 困惑（Confused）动作在空闲动作循环中权重为 0，永远不会自发触发。

**原因：** 代码中 `_idleActionWeights` 数组的第 11 个元素值为 `0`。有趣的是，LaTeX 文档（`project_brief/report.tex`）的动作权重表中记录困惑权重为 `2`（占 6.5%），与代码实际值不符。困惑动作被设计为仅由外部强制调用（如 AI 对话中角色表达困惑时调用），而非自然空闲循环的一部分。

**影响：** 用户在普通使用中永远看不到歪头 + 皱眉 + 眯眼的困惑表情，除非通过 AI 聊天或调试面板强制触发。

**状态：** 🔧 可按需调整权重使其自然出现（同时需同步更新 LaTeX 文档）

### 6. Everything 未安装时文件搜索降级

**现象：** 若未安装 Everything（es.exe），`search_files` 工具将自动回退到递归目录搜索模式，速度较慢且仅能搜索桌面目录，全盘搜索不可用。

**解决：** 从 [voidtools.com](https://www.voidtools.com/downloads/) 下载 `ES-1.1.0.30.x64.zip`，将 `es.exe` 解压到 `%LOCALAPPDATA%\Everything\es.exe` 即可。

**状态：** 📦 可选依赖

### 7. 工具调用同步阻塞（待优化）

**现象：** 所有 AI 工具（`search_files`、`query_scores` 等）在当前线程同步执行，阻塞 Unity 主线程。

**影响：** Everything 模式下 `search_files` 为毫秒级，但递归搜索回退时可能阻塞主线程长达 10 秒。异步协程方案已规划但尚未实施。

**状态：** ⏳ 待后续优化
