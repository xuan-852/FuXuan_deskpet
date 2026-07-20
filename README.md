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

基于 **Unity（团结引擎）+ Live2D Cubism SDK** 的 Windows 桌面宠物。角色为《崩坏：星穹铁道》中的 **符玄**（仙舟「罗浮」太卜司之首），具备完整的 **AI 对话、Live2D 表情动作、物理交互、天气感知、日程管理** 以及 **具身智能闭环**。

> 从感知 → 决策 → 执行 → 验证 → 记忆，形成完整的闭环学习系统。

---

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

### 2. 构建 & 运行

```powershell
.\build.ps1          # 完整构建
.\build.ps1 -Quick   # 仅编译验证
.\Build\DesktopPet.exe   # 运行
```

首次运行后可从系统托盘右键菜单启用开机自启。

### 环境依赖

| 依赖 | 说明 |
|------|------|
| 引擎 | Tuanjie 2022.3.62t7 |
| Live2D SDK | CubismSdkForUnity-5-r.4 |
| DeepSeek API Key | `DEEPSEEK_API_KEY`（必需） |
| GLM API Key | `GLM_API_KEY`（必需：视觉 + 动作评分） |
| Ollama | 本地 LLM 决策（可选，推荐 qwen2.5:0.5b） |
| 系统 | Windows 10/11 64 位 |

---

## 🧠 核心架构

```
┌────────────────────────────────────────────────────────────┐
│                       Windows 桌面                          │
│  ┌──────────── DWM 透明窗口 (WS_EX_LAYERED) ────────────┐  │
│  │                                                        │  │
│  │  ┌─ 物理系统 ────────────────────────────────────┐    │  │
│  │  │ DesktopPet (物理引擎 + 状态机 + 崩溃监控)       │    │  │
│  │  │ DragHandler (拖拽 + 穿透管理 + 挣扎动画)        │    │  │
│  │  └──────────────────────────────────────────────┘    │  │
│  │                                                        │  │
│  │  ┌─ 交互界面 ────────────────────────────────────┐    │  │
│  │  │ BallPanel (悬浮球：设置/报告/便签)              │    │  │
│  │  │ RightPanel (右侧 Widgets：聊/设/签/告)          │    │  │
│  │  │ ChatBubble (古风逐句气泡)                       │    │  │
│  │  └──────────────────────────────────────────────┘    │  │
│  │                                                        │  │
│  │  ┌─ AI 系统 ─────────────────────────────────────┐    │  │
│  │  │ ChatManager → DeepSeek API (多轮 Function Call)│    │  │
│  │  │ ToolEngine/ (插件式工具，自动注册)             │    │  │
│  │  │ IdleChatGenerator (闲话预生成)                  │    │  │
│  │  └──────────────────────────────────────────────┘    │  │
│  │                                                        │  │
│  │  ┌─ 动作系统 ────────────────────────────────────┐    │  │
│  │  │ Live2DRenderer (Cubism SDK + Perlin 微动)      │    │  │
│  │  │ ActionAgent/ (闭环具身引擎)                     │    │  │
│  │  │ ActionPresets/ (硬编码预设)                     │    │  │
│  │  └──────────────────────────────────────────────┘    │  │
│  │                                                        │  │
│  │  ┌─ 感知系统 ────────────────────────────────────┐    │  │
│  │  │ ActivityTracker (前台 2s 轮询)                  │    │  │
│  │  │ BrowserTabReader (UIA 浏览器标签)               │    │  │
│  │  │ PetMemory (三层记忆) · KnowledgeBaseManager     │    │  │
│  │  │ TimeWeatherController (双源天气)                 │    │  │
│  │  └──────────────────────────────────────────────┘    │  │
│  └────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────┘
```

### 闭环具身智能数据流

```
MotionAgent ──→ MotionTranslator ──→ MotionPlanner ──→ MotionGenerator
(感知/决策)    (DeepSeek 关键帧)     (11 模板/6 曲线)   (插值播放)
     ↑                                                      │
     │    MotionMemoryManager ←── DualModelValidator ←──────┘
     │    (30 条 + 负反馈)      (GLM-4V 多帧拼图评分)
     └────────────────────────────────────────────────────────┘
```

### 执行顺序

```
DesktopPet.Update(0)            → 物理更新、状态转换
CubismPhysicsController(800)    → 衣服/头发物理模拟
Live2DRenderer.LateUpdate(801)  → 覆盖物理重置参数 + 空闲动画 + 交互反馈
```

---

## ✨ 详细功能

### 🤖 AI 对话系统

基于 **DeepSeek Chat API** 的 Function Calling 体系，注册 40+ 法术工具（通过 `ToolEngine/IPetTool` 插件接口自动发现），支持最多 5 轮回环调用。

| 类别 | 术式 | 能力 |
|------|------|------|
| 🔭 观星术 | `search_web` | **联网搜索**（通过 OpenClaw Bridge 或传统搜索） |
| 📷 摄形术 | `capture_screen` | 截图 + GLM-4V 视觉分析 |
| 🎵 调音术 | `set_volume` / `toggle_mute` | 音量调节、静音 |
| 🔒 封印术 | `lock_screen` / `power_action` | 锁屏、关机、重启、睡眠（需确认） |
| 📢 传音术 | `send_notification` | 桌面通知、剪贴板读写、Server酱³ 推送 |
| 🔍 洞观术 | `get_system_info` / `search_files` | 系统信息、Everything 毫秒级文件搜索 |
| 🚀 开阵术 | `launch_app` / `open_folder` | 启动应用、打开文件夹 |
| 📋 卜算记事簿 | `reminder_*` | 便签增删改查（支持重复规则 + 三级推送） |
| 📚 卜算传讯 | `query_*` | 课表、成绩、考试、学业概览查询 |
| 🎭 演武术式 | `play_*` / `generate_motion` | 表情动作播放、LLM 生成新动作 |
| 🧠 忆境术 | `memory_*` | 长期记忆读写、人格自省 |
| 📚 藏书阁 | `knowledge_*` | 本地 RAG 知识库语义检索 |

**对话特性：**
- 长回复逐句打字机效果（2.5s 间隔）
- 无操作自动搭话（IdleChatGenerator 批量预生成）
- 每次对话动态注入：时间/天气/前台活动/长期记忆/人格状态/知识库上下文

### 🎨 Live2D 渲染

- **Cubism SDK 5-r.4** — 参数化变形 + 实时物理模拟
- **DWM 透明窗口** — `DwmExtendFrameIntoClientArea` 无缝融合桌面，无绿边
- **Perlin 噪声** — 呼吸、身体微晃、头部微动、眼球转动（7 通道独立）
- **3D 骨架就绪** — `HybridRenderer` 框架支持 Live2D/3D 交叉淡入淡出（0.3s），当前恒走 Live2D 分支

### 🏃 物理与交互

- **桌面物理** — 重力、碰撞、弹跳衰减、摩擦阻尼
- **拖拽抛掷** — 多帧速度缓冲平均，带挣扎动画（双臂划水 + 双腿交替 + 慌张表情）
- **分区点击反馈** — 头（摸头眯眼）/ 身（戳胸惊讶）/ 腿（害羞踢腿）
- **鼠标跟随** — 眼球 150px 触发平滑追踪
- **屏幕碰撞** — 边缘撞墙动画 + 反弹物理
- **地面状态机** — 5 种行为加权随机切换

### 🎭 动作系统（ActionAgent — 15 组件）

#### 空闲动作
12+ 种 JSON 配置驱动（歪头/微笑/挑眉/星辉/伸懒腰/委屈/法阵/害羞/困惑/爱心/哭/心跳），天气/时段动态调整权重。

#### MotionAgent（自主动作决策引擎）
- **四档密度**：High(4s) / Med(8s) / Low(15s) / Sleep(30s)，基于空闲时长、睡眠时间、用户专注进程自动调节
- **决策循环**：等待 → ShouldDecide → GatherContext → DecideWithLLM / FallbackDecide → ExecuteDecision
- **本地 LLM 双模式**：Ollama Qwen2.5(0.5B/3B) → 连续 3 次失败回退概率模式
- **GPU 监控**：检测到游戏时自动暂停本地 LLM，游戏退出后 30s 冷却恢复

#### MotionTranslator（LLM 动作翻译）
自然语言 → Live2D 关键帧序列（DeepSeek, temperature=0.3），11 条规则 + 12 种特殊模式，身体部位分组 Schema（HEAD/EYES/BROWS/MOUTH/ARMS/HANDS/FINGERS/LEGS/BODY），参数富化自动补肢体参数。

#### MotionPlanner + MotionGenerator
10 种硬编码模板（挥手/点头/摇头/鞠躬等），6 种插值曲线（含 BounceEaseOut），3 阶段计划（淡入/保持/回归），6 种表情模板。

#### 闭环视觉验证
- **DualModelValidator** — GLM-4V 对动作视频 20/40/60/80% 截图合成 2×2 拼图评分（1-5 分）
- **盲探索** — GLM 描述当前姿态像什么动作

#### MotionMemoryManager（闭环学习核心）
- 上限 30 条，高分覆盖低分，自动淘汰最低分/最久远
- 负反馈（≤2 分记入反例，最多 10 条）
- 无望检测（≥5 次尝试且最高分 ≤2 优先淘汰）
- 冷却 120s 防复读

#### 安全
- SafetyValidator：互斥组、对称对、极端值 3 类保护

### 🧠 感知与记忆

| 系统 | 方式 | 用途 |
|------|------|------|
| 法眼 | 2s 轮询前台窗口 | 分类编程/游戏/学习/浏览 |
| 多窗口 | EnumWindows 扫描 | 环境感知 |
| 浏览器标签 | UIA 反射 | 读取 Chrome/Edge/Firefox/Opera/Brave/Vivaldi 标签 |
| 长期记忆 | 3 层 30 条 JSON 持久化 | 核心事实 + 重要记忆 + 近期琐事 |
| 知识库 | Ollama 嵌入语义检索 | 25+ 文件类型分块索引 |
| 人格记忆 | 五维人格 + 三维关系 | 跨会话人格连续演化 |
| 反思 | 重要性 ≥ 30 触发 LLM 提炼 | 高层次洞察 |

### 🧬 人格演化系统

- **五维人格** — 勤勉/亲和/活泼/自信/求知（0~1 渐变），交互驱动微调
- **三维关系** — 信任/亲密/熟悉度，随交互累积
- **人格↔情绪联动** — 基线影响 EmotionState 四维（Valence/Arousal/Warmth/Energy）
- **无交互回归** — 300s 无人时缓慢回归中性
- **里程碑** — 每 20 次交互 / 亲密度突破阈值自动纪录
- **持久化** — `pet_personality.json`，跨会话连续演化

### 🌤️ 时间与天气

- **昼夜感知** — 夜间/深夜特化表情（犯困眨眼、低头、低头微张嘴）
- **双天气源** — 和风天气（需 Key）优先，失败自动回退 wttr.in
- **AI 天气语录** — DeepSeek 生成符玄风格台词（每次 6 条，按天气类型缓存）
- **天气→表情联动** — 晴微笑 / 雨委屈 / 雪好奇

### 📋 便签与提醒

- 本地 JSON 持久化，支持优先级（low/normal/high）、重复规则（一次性/每日/工作日/每周）
- 三级推送链路：气泡 → Windows Toast → Server酱³ 手机推送
- 服务端轮询同步 + AI 语音创建（"提醒我…"）

### 🖥️ 交互界面

- **悬浮球 BallPanel** — 右下角粉✦，单击展开 420×580px 辐射菜单（设置/报告/便签）
- **右侧面板 RightPanel** — ~键切换 / 鼠标划过右边缘展开，220px Widgets 面板（聊/设/签/告 + 输入栏）
- **古风气泡 ChatBubble** — OnGUI 手绘圆角 + 拖尾 + 12 星点装饰
- **优先级气泡** — High(AI 回复) > Normal(提醒) > Low(闲话)

### 🖥️ 窗口与系统集成

- DWM 透明窗口（WS_POPUP + WS_EX_LAYERED）
- 动态点击穿透（根据 BallPanel/RightPanel 区域切换 `WS_EX_TRANSPARENT`）
- 多显示器支持（VirtualScreen）
- 睡眠唤醒恢复（帧间间隙检测 + DWM 重建 + 物理重置）
- DWM 崩溃安全模式（连续 5 次失败跳过）
- 系统托盘（Shell_NotifyIcon）、开机自启（注册表 Run）、进程互斥（Mutex）

### ⚡ 性能优化

| 级别 | FPS | RenderTexture | 触发条件 |
|------|-----|--------------|---------|
| High | 60 | 100% | 正常状态 |
| Normal | 40 | 75% | 中等负载 |
| Low | 20 | 50% | CPU/GPU > 92% 或内存 > 93% |

- **升档拦截**：CPU/GPU > 80% 阻止升档
- **应用内存**：800MB GC / 1.2GB 强制 GC
- **崩溃日志**：自动捕获，> 2MB 截断

---

## 🏗️ 项目结构

```
Desktop_per_pro/
├── build.ps1 / build.cmd          # 构建脚本
├── CHANGELOG.md / README.md
├── Build/                          # 构建输出
│   ├── DesktopPet.exe
│   └── DesktopPet_Data/
├── code/desktop_unity/
│   ├── Assets/
│   │   ├── Scripts/                # 核心脚本
│   │   │   ├── *.cs                # 渲染/交互/对话/感知/性能
│   │   │   ├── ToolEngine/         # 插件式工具系统（IPetTool + 6 工具类）
│   │   │   ├── Editor/             # 编辑器工具（7 文件）
│   │   │   └── Live2DFramework/
│   │   │       ├── ActionAgent/    # 闭环动作系统（15 文件）
│   │   │       ├── ActionPresets/  # 动作/表情预设（6 文件）
│   │   │       ├── PersonalityManager.cs
│   │   │       └── 参数知识层       # 8 文件
│   │   ├── Resources/              # SystemPrompt + ParamMaps
│   │   └── StreamingAssets/        # Live2D 模型
│   ├── Packages/manifest.json
│   └── ProjectSettings/
├── file/符玄/                       # Live2D 模型源文件
├── project_brief/                   # 项目文档（LaTeX PDF）
├── record/ / tools/                 # 开发记录 & 辅助脚本
```

### 配置体系

```
环境变量:
  DEEPSEEK_API_KEY → ChatManager / IdleChatGenerator / MotionTranslator
  GLM_API_KEY       → ToolEngine (截图分析) / DualModelValidator
  QWEATHER_API_KEY  → TimeWeatherController

JSON 持久化 (D:\DesktopPetData\):
  pet_config.json / pet_memory.json / reminders.json / motion_memory.json
  validation_log.json / activity_log.json / pet_personality.json
  ActionRefs/ / glm_collages/ / knowledge_base.json
```

---

## 📜 版本历史

| 版本 | 日期 | 亮点 |
|------|------|------|
| **V2.3+** | 2026-07-13 | 多帧拼图评分；闭环学习 P0 修复；Newtonsoft.Json 全面迁移 |
| V2.3 | 2026-07 | MotionAgent 全链路自主决策 |
| V2.2 | 2026-07 | 悬浮球辐射菜单 + 面板系统 |
| **V2.1** | 2026-07-09 | BallPanel/RightPanel 取代右键菜单 |
| N18 | 2026-06-22 | 已完成任务视图 + 系统内存监控 |
| N17 | 2026-06-22 | 提醒去重 + 服务器推送去重 |
| N16 | 2026-06-20 | Everything 毫秒级搜索 |
| N14~N15 | 2026-06 | 双模型验证 + 工具调用修复 |
| N7~N13 | 2026-06 | 便签/服务端/学业数据/多轮修复 |
| v0.2 | 2026-05 | 初始 Unity 版本 |

---

## ⚖️ 许可证 & 致谢

**许可证：** 本项目为个人学习与娱乐用途，角色「符玄」版权属于 miHoYo / HoYoverse。

**致谢：** [Live2D Cubism](https://www.live2d.com/) · [DeepSeek](https://platform.deepseek.com/) · [智谱 GLM](https://open.bigmodel.cn/) · [Ollama](https://ollama.com/) · [和风天气](https://www.qweather.com/) · [wttr.in](https://wttr.in/) · [Server酱³](https://sc3.ft07.com/) · [Everything](https://www.voidtools.com/)
