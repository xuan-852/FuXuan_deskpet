# 符玄桌面灵伴 — 技术报告

> 文档版本: N40 · 最后更新: 2026-08-08（**按代码真相重写版**，含 N39 修复与 T1-T8 Token 优化）
> 本报告全部数字与表述均以 `code/desktop_unity/Assets/Scripts/` 下的真实代码为准，
> 旧版报告的过时描述（40+ 工具、5 轮回环、15 组件、双模型等）已全部修正。

---

## 摘要

本文档描述 **符玄桌面灵伴 (Fu Xuan Desktop Pet)** 项目的完整技术架构。项目基于 Unity（团结引擎 Tuanjie 2022.3.62t7）+ Live2D Cubism SDK 5-r.4，构建运行于 Windows 桌面的 AI 驱动虚拟角色。深度集成 DeepSeek Chat（含 Function Calling）、GLM-4V 多模态视觉模型、多层级感知与记忆系统、以及具身智能闭环（感知→决策→执行→验证→记忆）。

---

## 目录

1. [项目概述](#1-项目概述)
2. [系统架构](#2-系统架构)
3. [AI 对话系统](#3-ai-对话系统)
4. [工具系统 ToolEngine](#4-工具系统-toolengine)
5. [具身动作系统](#5-具身动作系统)
6. [闭环验证体系](#6-闭环验证体系)
7. [渲染管道](#7-渲染管道)
8. [物理与交互系统](#8-物理与交互系统)
9. [感知系统](#9-感知系统)
10. [记忆与人格系统](#10-记忆与人格系统)
11. [便签与提醒系统](#11-便签与提醒系统)
12. [窗口与系统集成](#12-窗口与系统集成)
13. [交互界面](#13-交互界面)
14. [性能优化](#14-性能优化)
15. [构建与部署](#15-构建与部署)
16. [已知偏差与 Bug](#16-已知偏差与-bug)

---

## 1. 项目概述

### 1.1 项目目标

开发运行于 Windows 桌面的 AI 驱动 Live2D 虚拟角色桌面宠物，具备：
- 自然语言对话 + **55 个工具**调用能力（10 轮回环，首轮意图子集 27 个）
- Live2D 实时表情动作 + LLM 动态动作生成
- 环境感知 + 人格演化 + 长期记忆
- 桌面物理交互 + 系统集成

### 1.2 技术栈

| 组件 | 选型 |
|------|------|
| 引擎 | Tuanjie 2022.3.62t7（Unity 2022.3 LTS 衍生版） |
| 2D 渲染 | Live2D Cubism SDK 5-r.4（3D 分支不可用，恒走 Live2D） |
| AI 对话 | DeepSeek Chat API + Function Calling |
| 视觉模型 | GLM-4V（智谱；付费 glm-4.6v，免费 glm-4v-flash 回退） |
| 本地 LLM | Ollama Qwen2.5 (3B) + nomic-embed-text（RAG 嵌入） |
| 天气 | wttr.in（**默认**，cityCode="Nanjing"）/ QWeather（可选） |
| 搜索 | OpenClaw Bridge (HTTP 19876 / WS 18789) / Everything CLI |
| 推送 | Server酱³ |
| 窗口 | Win32 API (DWM / Shell_NotifyIcon) |
| 构建 | PowerShell + Tuanjie Batchmode |

### 1.3 核心指标（代码真相）

| 指标 | 数值 |
|------|------|
| 渲染帧率 | 20-60fps（三档自适应降档） |
| AI 响应延迟 | 1-5s（网络 + 工具轮次） |
| 注册工具 | **55 个**（9 个工具文件，插件式自动发现；N40 按意图注入子集） |
| 工具回环 | 最多 **10 轮**（`MAX_TOOL_ROUNDS=10`） |
| 内存管理 | 800MB GC / 1.2GB 强制 GC |
| 动作模板 | 10 种硬编码模板 + 6 种插值曲线（Linear/Smooth/EaseOut/EaseIn/Hold/Bounce） |
| 空闲动作 | 9 种 JSON 配置驱动（7 参数化 + 2 硬编码特效） |
| 记忆系统 | 3 层 JSON 持久化（输出 4 层格式化） |
| 人格维度 | 五维人格 + 三维关系 |

---

## 2. 系统架构

### 2.1 六层架构

```
┌──────────────────────────────────────────────────────────┐
│                     UI 表现层                             │
│   ChatBubble · BallPanel · RightPanel · DebugWindow     │
│   SystemTray · VisualHeartbeat                          │
├──────────────────────────────────────────────────────────┤
│                    AI 核心层                              │
│   ChatManager (10轮/意图过滤/600s看门狗) · ApiClient(SSE)│
│   LocalLLMAgentService(4离线能力) · IdleChatGenerator    │
│   ProactiveMessageScheduler                             │
├──────────────────────────────────────────────────────────┤
│                    感知层                                 │
│   ActivityTracker · BrowserTabReader · PetMemory         │
│   TimeWeatherController · PerformanceMonitor             │
│   GpuLoadMonitor · KnowledgeBaseManager                 │
├──────────────────────────────────────────────────────────┤
│                    具身层                                 │
│   ActionAgent/ (15 文件) · MotionAgent                  │
│   MotionTranslator · MotionPlanner · MotionGenerator     │
│   MotionMemoryManager · SafetyValidator                  │
│   DualModelValidator · VisionMotionVerifier              │
│   PersonalityManager · EmotionState · IdleActionScheduler│
├──────────────────────────────────────────────────────────┤
│                    物理与渲染层                            │
│   DesktopPet (物理引擎) · DragHandler                    │
│   HybridRenderer → Live2DRenderer (3D 分支不可用)        │
│   Live2DFramework/ (映射/物理/分析)                      │
├──────────────────────────────────────────────────────────┤
│                    系统层                                 │
│   WindowOverlay (DWM) · SystemTrayManager               │
│   ReminderManager · ServerPollService · OpenClawBridge  │
│   ToolEngine (9 工具文件 + 7 基础设施 · 55 工具) · Mutex │
└──────────────────────────────────────────────────────────┘
```

### 2.2 执行顺序

| Order | 组件 | 职责 |
|-------|------|------|
| 0 | `DesktopPet.Update()` | 物理更新、状态转换、行走相位 |
| 800 | `CubismPhysicsController.LateUpdate()` | 衣服/头发物理模拟 |
| 801 | `Live2DRenderer.LateUpdate()` | 覆盖物理重置参数 + 空闲动画 + 交互反馈 |

> 801 > 800 确保所有参数在 Cubism Physics 运算后写入，避免物理覆盖关键参数。

### 2.3 依赖注入模式

三种连接方式：
1. **Inspector 序列化引用** — DesktopPet 持有 dragHandler, renderer, chatManager 等引用
2. **全局单例** — `ChatConfig`（静态环境变量）、`PetMemory.Instance`、`PetConfig.Instance`、`MotionAgent.Instance`、`KnowledgeBaseManager.Instance`
3. **ToolEngine 插件注册** — `ToolRegistry` 反射自动发现 `IPetTool` 实现

---

## 3. AI 对话系统

### 3.1 架构

```
用户输入 → ChatManager
  → DeepSeek Chat API (Function Calling, SSE 流式)
    → ToolEngine/ (插件式工具调度, 55 工具, N40 子集注入)
      → 最多 10 轮回环 (MAX_TOOL_ROUNDS=10)
        → 600s 看门狗 (含 GLM 180s 视觉)
        → 句子队列逐句显示 (2.5s 间隔)
```

**可靠性机制**：
- 自动重试：最多 3 次，400/401/403 不重试
- 历史裁剪：`MAX_HISTORY_ENTRIES=60` + **N40 历史 15000 字符预算**（超预算用 Ollama 摘要压缩）
- 意图过滤：本地 Ollama 分类（chat/emotion/command/knowledge/operation），chat/emotion 不发工具，其余按白名单过滤
- **N40 工具子集（T4）**：首轮有意图→27 个相关工具 / 无意图→纯对话；回环轮→已用工具 ∪ 意图候选 ∪ CoreToolSubset（play_action/set_expression/stop_action/generate_motion/get_system_info/get_mouse_pos）
- **N40 Speculative Multi-Action（T7）**：回复含多段动作标记时预展开，一次请求驱动连续动作
- 消息队列：等待时不丢输入

### 3.2 言出法随 — 内嵌动作标记

AI 回复文本中嵌入标记，无需 tool_call 即同步触发表情动作：

| 格式 | 示例 | 效果 |
|------|------|------|
| `【表情:开心】` | `【表情:开心】今天天气真好` | PlayExpression |
| `【动作:伸懒腰】` | `【动作:伸懒腰】活动一下~` | ForceAction |
| `（自然描述）` | `（害羞捂脸）人家才没有` | TryMatchKnown 模糊匹配 |

实现于 `ChatManager.StripAndExecuteActions()`。

### 3.3 上下文注入（SystemPrompt 注入链）

每次对话动态注入，按顺序：
人格 → ActivityTracker 摘要 → 当前窗口 → 多窗口 → 浏览器标签 → ParameterKnowledgeProvider（Live2D 参数语义知识）→ 闭环演武说明 → MotionMemoryManager（演武心经）

> ✅ **N39 已修复**：知识库检索上下文由 `KnowledgeBaseManager.GetFormattedContext()` 提供，现返回 `LastFormattedContext` 缓存（`SearchAndFormat` 协程填充），知识库内容已实际注入对话上下文。

### 3.4 自动闲聊与反思

- `IdleChatGenerator` 批量预生成闲话/问候 → `ProactiveMessageScheduler` 调度显示。支持时间/天气特化、记忆注入。
- ✅ **N39 已修复**：反思机制已接线 `ChatManager.CheckReflection → DoReflection（DeepSeek 提炼）→ CommitReflection`，死回调 `OnReflectRequest` 已删除。

---

## 4. 工具系统 ToolEngine

### 4.1 插件架构

```
IPetTool (接口) + ToolSchema (参数 Schema)
  ↑ 实现 (9 个工具文件 + 7 个基础设施 .cs · 共 55 工具)
ToolRegistry (反射自动发现 + 调度)
  ↑ 委托
AsyncToolBase (异步/协程工具基类)
  ↑ ChatManager 调用
```

### 4.2 工具分类（真实注册名，55 个）

| 文件 | 工具数 | 代表工具 | 类别 |
|------|--------|----------|------|
| WebSystemTools.cs | 14 | `search_web`, `open_url`, `search`, `open_app`, `open_folder`, `get_system_info`, `lock_screen`, `set_volume`, `mute`, `get_mouse_pos`, `list_files`, `notify`, `run_command`, `power` | 观星/封印/洞观/开阵 |
| ClipboardFileTools.cs | 14 | `get_clipboard`, `set_clipboard`, `get_weather`, `file_open`, `file_move`, `file_copy`, `file_delete`, `file_rename`, `file_info`, `file_create`, `dir_create`, `file_read`, `search_files`, `search_file` | 传音/摄形/调音/文件 |
| ReminderAcademicTools.cs | 8 | `set_reminder`, `query_reminders`, `mark_reminder_done`, `delete_reminder`, `query_exams`, `query_scores`, `query_schedule`, `query_user_status` | 卜算记事簿/传讯 |
| Live2DSyncTools.cs | 7 | `set_expression`, `play_action`, `stop_action`, `inspect_motion_memory`, `inspect_personality`, `explore_body`, `control_body` | 演武/表情/动作 |
| VisionKnowledgeTools.cs | 4 | `take_screenshot`, `knowledge_search`, `knowledge_index`, `openclaw_search` | 摄形/藏书阁 RAG/OpenClaw |
| MotionCoroutineTools.cs | 5 | `generate_motion`, `explore_body_vision`, `run_verification`, `vis_verify`, `self_review` | 异步动作生成(协程)/视觉验证 |
| PoggetTool.cs | 1 | `launch_pogget` | 启动 Pogget |
| PoggetAgentTool.cs | 1 | `pogget_agent`（8 子命令：ping/list_containers/get_container_items/add_to_container/remove_from_container/create_container/organize_desktop/quickpanel_status） | Agent IPC |
| LatexCompileTool.cs | 1 | `compile_latex` | 问天录（经 OpenClawBridge） |

> ⚠️ 旧文档的工具名与文件（`MemoryTools.cs`/`ReminderTools.cs`/`AcademicTools.cs`/`KnowledgeTools.cs`/`BodyTools.cs`、`open_web`/`capture_screen`/`send_notification`/`reminder_*` 等）**均已不存在**，以本表为准。
> N40（T4）：ChatManager 每轮只注入意图相关工具子集（55→27），见 §3.1。

### 4.3 关键工具列表（已核实存在）

| # | 工具 ID | 能力 |
|---|---------|------|
| 1 | `search_web` / `search` / `openclaw_search` | 搜索（OpenClaw Bridge 优先，Everything 回退） |
| 2 | `open_url` | 打开网页 |
| 3 | `get_weather` | 查天气 |
| 4 | `take_screenshot` | 截图 + GLM 分析 |
| 5 | `set_volume` / `mute` | 音量 |
| 6 | `set_reminder` / `query_reminders` / `mark_reminder_done` / `delete_reminder` | 便签管理 |
| 7 | `power` | 关机/重启/睡眠 |
| 8 | `lock_screen` | 锁屏 |
| 9 | `get_clipboard` / `set_clipboard` | 剪贴板 |
| 10 | `get_system_info` / `get_mouse_pos` / `list_files` | 系统/鼠标/文件列表 |
| 11 | `search_files` / `search_file` | 文件搜索（Everything） |
| 12 | `open_app` / `open_folder` | 启动应用/打开文件夹 |
| 13 | `inspect_personality` / `inspect_motion_memory` | 自省 |
| 14 | `notify` / `run_command` | 桌面推送/执行命令 |
| 15 | `query_exams` / `query_scores` / `query_schedule` / `query_user_status` | 学业查询 |
| 16 | `file_open` / `file_move` / `file_copy` / `file_delete` / `file_rename` / `file_info` / `file_create` / `dir_create` / `file_read` | 文件操作 |
| 17 | `set_expression` / `play_action` / `stop_action` / `explore_body` / `control_body` | 表情动作 |
| 18 | `generate_motion` / `explore_body_vision` / `run_verification` / `vis_verify` / `self_review` | LLM 动作生成/自省验证 |
| 19 | `knowledge_search` / `knowledge_index` | RAG 知识库 |
| 20 | `launch_pogget` / `pogget_agent` | Pogget 集成 |
| 21 | `compile_latex` | LaTeX 编译 |

> ⚠️ **代码中不存在的旧文档工具**：`get_time`（时间由 SystemPrompt 注入，非工具）、`get_pet_status`、`get_system_status`、`show_reminder`、`send_notification`、`write_note`、`messenger`、`write_memory`、`get_memories`、`start_conversation`。

---

## 5. 具身动作系统

### 5.1 ActionAgent 架构 (15 文件)

```
ActionAgent/ (Live2DFramework/ActionAgent/, 共 15 文件)
├── MotionAgent.cs           # 自主动作决策引擎（tick 驱动）
├── MotionPlanner.cs         # 10 模板 + 6 曲线 + 3 阶段
├── MotionTranslator.cs      # LLM 自然语言→关键帧（10 规则 + 10 特殊模式）
├── MotionGenerator.cs       # 协程插值播放
├── MotionMemoryManager.cs   # 闭环学习核心
├── DualModelValidator.cs    # GLM-4V 拼图评分（单模型）
├── VisionMotionVerifier.cs  # GLM-4V 视觉验证（10 测试序列）
├── SafetyValidator.cs       # 参数安全校验
├── PersonalityManager.cs    # 人格演化
├── EmotionState.cs          # 情绪模型
├── IdleActionScheduler.cs   # 空闲动作调度
├── MotionVerifier.cs        # 参数一致性校验
├── LocalLLMClient.cs        # Ollama 本地决策回退
├── GpuLoadMonitor.cs        # GPU 负载检测
└── ActionReferenceManager.cs # 动作引用管理
```

> ✅ **N39 已删除**：`AutoMotionCollector.cs` 死代码（文件 + meta + 自动添加逻辑一并移除，现 ActionAgent 仅 15 文件）。

### 5.2 决策循环

```
MotionAgent tick (High 4s / Med 8s / Low 15s / Sleep 30s)
  → ShouldDecide (密度/空闲/专注检测)
  → GatherContext (宠物状态/情绪/时间/用户活动)
  → DecideWithLLM / FallbackDecide (Ollama 失败≤3 次回退概率)
    → ExecuteDecision
      → 动作/表情/等待
```

> ✅ **N39 已修复（BUG-1）**：`MotionAgent.IsSleepTime()` 现按真实凌晨 1~7 点判断（`testMode` 下跳过），睡眠时段调度生效。

### 5.3 MotionTranslator

自然语言 → Live2D 关键帧序列（DeepSeek, temp=0.3）：

- **10 条通用规则 + 10 种 SPECIAL PATTERNS**（9 种姿势，捂脸重复）
- 身体分组 Schema：HEAD / EYES / BROWS / MOUTH / ARMS / HANDS / FINGERS / LEGS / BODY
- 参数富化：自动补全关联肢体
- VERIFIED FEEDBACK：上次失败案例自动反馈给 LLM
- ✅ **N39 已修复（BUG-7）**：示例参数已改为角度制（20~28 度），与规则 15-25° 一致

### 5.4 MotionPlanner

10 种硬编码模板（挥手/点头/摇头/鞠躬/伸懒腰/叉腰/捂脸/指/招手/合十）+ 6 种插值曲线（`InterpolationType`）：
**Linear / Smooth / EaseOut / EaseIn / Hold / Bounce**

3 阶段计划：淡入 → 保持 → 回归；6 种表情模板（happy/sad 等）。

### 5.5 MotionAgent 密度控制

| 档位 | 间隔 | 触发 |
|------|------|------|
| High | max(base×0.5, 4s) | 空闲短 / 测试模式 |
| Med | base (8s) | 常规空闲 |
| Low | min(base×2, 15s) | 深夜 / 专注进程 |
| Sleep | 30s | 长时间无交互 |

### 5.6 空闲动作

9 种 JSON 配置驱动（id 1--9：歪头 / 微笑 / 挑眉 / 星辉* / 伸懒腰 / 委屈 / 法阵* / 害羞 / 困惑；*为硬编码特效）：
天气/时段动态调整权重：夜晚微笑×0.3 / 雨哭×1.8 / 雪微笑×1.5；冷却系统防复读。

---

## 6. 闭环验证体系

### 6.1 完整闭环

```
generate_motion
  → 播放动作
  → 截图 (20/40/60/80%)
  → 2×2 拼图 (存 glm_collages/, 上限 50)
  → GLM-4V 评分 (1-5, passThreshold=3)
  → 写入 MotionMemory
  → 下次生成参考高分案例
  → VERIFIED FEEDBACK 注入失败案例
```

### 6.2 VisionMotionVerifier（10 VISION_TESTS，55% 截图）

| 验证类型 | 方法 | 用途 |
|---------|------|------|
| 单模型评分 | **GLM-4V**（付费 glm-4.6v；Qwen-VL 已移除） | 动作质量评分 |
| 多帧拼图 | 4 帧 2×2 | 时序一致性 |
| 盲探索 | GLM 描述姿态 | 发现新动作 |
| 自评闭环 | 播放后截图 → 追加回复 | 实时反馈 |

> ✅ **N39 已修复（BUG-5）**：`DualModelValidator` 回调签名简化为 4 参，删除恒 0 的 `qwenScore` 参数（文件仍名 Dual，实际仅 GLM-4V 单模型）。

### 6.3 MotionMemoryManager

| 机制 | 参数 |
|------|------|
| 容量 | 30 条（高分覆盖低分） |
| 淘汰 | 最低分 / 最久远 |
| 负反馈 | ≤2 分记入反例（最多 10 条） |
| 无望检测 | ≥5 次且最高分≤2 优先淘汰 |
| 冷却 | 120s 防复读 |

### 6.4 验证指标 (2026-07-07, v1 历史基线)

| ID | 动作 | 分数 | 判定 |
|----|------|------|------|
| T1-T3 | 捂脸/叉腰/捂嘴 | ⭐~⭐⭐ | 不太像 |
| T4-T6 | 远望/眨眼/行礼 | ⭐⭐⭐⭐ | 基本是 ✅ |
| T7-T9 | 缩团/抬头/思考 | ⭐~⭐⭐⭐ | 需改进 |
| T10 | 合十祈祷 | ⭐⭐⭐⭐ | 基本是 ✅ |

**总体**: 通过率 60%, 平均分 2.7/5.0（考官 glm-4.6v，详见 `verification_report_2026-07-07.md`）

---

## 7. 渲染管道

### 7.1 Live2D Cubism 渲染

#### 模型加载双保险
```
AssetDatabase.LoadAssetAtPath<GameObject> (Editor)
  → 成功 → 实例化
  → 失败 → Resources.Load("Fuxuan") 降级 (Build)
```

#### 参数映射 (80+ 参数)

| 部位 | 参数 | 数量 |
|------|------|------|
| 头部 | ParamAngleX/Y/Z | 3 |
| 身体 | ParamBodyAngleX/Y/Z | 3 |
| 眼睛 | ParamEyeLOpen/ROpen, Ball, Smile | 6 |
| 眉毛 | ParamBrowRY/LY, RX/LX | 4 |
| 嘴 | ParamMouthForm, OpenY | 2 |
| 手臂 | Param31-37, 92-120 | 36+ |
| 呼吸 | ParamBreath | 1 |

核心入口 `Live2DRenderer.SetParameterValue(string, float)`（:3786），与 `Live2DParameterMapper` 双向映射。

#### Perlin 噪声微动 (7 通道)

| 参数 | 通道 | 描述 |
|------|------|------|
| ParamBreath | (t, 0) | 呼吸 |
| ParamBodyAngleX/Y/Z | (t, 1-3) | 身体三轴晃动 |
| ParamAngleX/Y | (t, 4-5) | 头部微动 |
| ParamEyeBallX/Y | (t+offset, 6-7) | 眼球微动 |

#### 天气↔表情联动

| 天气 | 表情 | 参数变化 |
|------|------|---------|
| ☀️ 晴 | 微笑 | MouthForm +0.2 |
| 🌧 雨 | 委屈 | BrowRY/LY +4, MouthForm 微嘟 |
| ⛈ 雷暴 | 警惕 | 同上 + EyeLOpen 微睁 |
| ❄️ 雪 | 好奇 | MouthOpenY +0.4, EyeLOpen +0.2 |
| 🌙 夜晚 | 困倦 | EyeLOpen 垂 +0.07 |

> ⚠️ 3D 渲染不可用：`HybridRenderer` 的 3D 分支为 TODO，恒走 Live2D；
> `Model3DRenderer` 注释称绿幕抠像，但实际渲染纯黑背景（注释与实现矛盾，3D 分支未落地故保留待处理）。
> 默认空闲表情为 "surprise"（非文档所称 "curious"）。

---

## 8. 物理与交互系统

### 8.1 桌面物理

| 特性 | 实现 |
|------|------|
| 重力 | 像素/帧² 加速度 |
| 最大下落 | maxFallSpeed = 8px/frame |
| 碰撞 | 屏幕边缘反弹 + 衰减 |
| 弹跳 | 触地弹跳 + 速度衰减 |
| 抛掷 | 5 帧速度缓冲平均 → 物理惯性 |

### 8.2 拖拽交互

| 交互 | 触发 | 反馈 |
|------|------|------|
| 拖拽 | 鼠标按住 | 挣扎动画（双臂划水+双腿交替+慌张表情） |
| 抛掷 | 释放鼠标 | 惯性物理 |
| 摸头 | 点击头部区域 | 眯眼锁定 + 表情切换 |
| 戳身 | 点击身体 | 惊讶捂胸 |
| 踢腿 | 点击腿部 | 害羞反应 |

### 8.3 地面状态机

5 种行为加权随机切换：
- `MoveLeftEdge` — 走向左边缘
- `MoveRightEdge` — 走向右边缘  
- `MoveLeftTime` — 向左走一段时间
- `MoveRightTime` — 向右走一段时间
- `StopTime` — 停留

> ⚠️ 多显示器：`WindowOverlay.isMultiMonitor` 恒为 false，仅使用主屏尺寸。

---

## 9. 感知系统

| 系统 | 方式 | 间隔 | 用途 |
|------|------|------|------|
| 法眼 | 前台窗口轮询 | 2s | 8 类分类（编程/游戏/学习/浏览/娱乐/通讯/空闲/其他） |
| 多窗口 | EnumWindows 扫描 | 10s | 环境感知（注入 prompt） |
| 浏览器标签 | UIA 反射 | 5s | 读取 6 种浏览器标签 |
| GPU 监控 | PerformanceCounter | 5s | GpuLoadMonitor 游戏检测 |
| 系统性能 | PerformanceCounter | 5s | CPU/GPU/内存监控 |

> ✅ **N39 已修复（BUG-3/4）**：`GatherContext()` 改用 `ChineseLunisolarCalendar` 计算真实农历日期→月相；`ActivityTracker.LastActivityTime` + `MotionAgent.UpdateInteractionTime()` 已接线（鼠标移动检测）。

---

## 10. 记忆与人格系统

### 10.1 PetMemory — 三层记忆

| 层级 | 容量 | 内容 | 持久化 |
|------|------|------|--------|
| 核心事实 | ≤5 | 用户基本信息 | JSON |
| 重要记忆 | Top-20 | 重要性排序 | JSON |
| 近期琐事 | ≤10 | 最近交互 | JSON |

存储结构：`entries + coreFacts + conversationSummary`；格式化输出为 4 层（核心事实→近日印象→Top5→最近3条）。

> ✅ **N39 已修复**：反思已接线——`ChatManager.CheckReflection`（L518）→ `DoReflection`（DeepSeek 提炼）→ `CommitReflection`，记忆重要性评估/反思提炼已实际驱动。

### 10.2 人格演化系统

| 维度 | 初始值 | 范围 | 描述 | 正触发 | 负触发 |
|------|--------|------|------|--------|--------|
| diligence | 0.5 | 0-1 | 勤勉 vs 慵懒 | 工作/学习 | 游戏/娱乐 |
| warmth | 0.6 | 0-1 | 温暖 vs 高冷 | 感谢/称赞 | 负面情绪 |
| playfulness | 0.5 | 0-1 | 活泼 vs 稳重 | 游戏/语气词 | 学习/工作 |
| confidence | 0.5 | 0-1 | 自信 vs 谦逊 | 工具成功 | 工具失败 |
| curiosity | 0.6 | 0-1 | 求知 vs 淡然 | 搜索/提问 | — |

**三维关系**：信任(0.3) / 亲密(0.2) / 熟悉度(0.1, 对数增长)，learningRate=0.01  
**人格↔情绪联动**：五维×权重 → EmotionState 四维偏移  
**无交互回归**：`DriftTowardNeutral()` 存在但 ActionAgent 内无调用者  
**持久化**：`pet_personality.json`

### 10.3 知识库

本地 RAG 系统：
- Ollama 嵌入（nomic-embed-text）→ 语义检索
- 25+ 文件类型分块索引
- `knowledge_base.json` 持久化
- ✅ **N39 已修复**：`GetFormattedContext()` 返回 `LastFormattedContext` 缓存（`SearchAndFormat` 协程填充），知识库内容已注入对话上下文

---

## 11. 便签与提醒系统

| 特性 | 实现 |
|------|------|
| 持久化 | 本地 JSON |
| 优先级 | low / normal / high |
| 重复规则 | 一次性 / 每日 / 工作日 / 每周 |
| 去重 | 关键词检查已有同类待办 |
| 三级推送 | 气泡 → Windows Toast（AppId '符玄'）→ Server酱³ 手机 |
| 服务端同步 | 轮询 localhost:3000 + 服务器推送队列 |
| AI 创建 | 对话中说「提醒我…」自动创建 |
| 已完成视图 | 独立标签页分离待办/已完成 |
| 考试提醒 | 前 3 天 19:00 + 考前 2h |

---

## 12. 窗口与系统集成

| 特性 | 技术 |
|------|------|
| 透明窗口 | DWM DwmExtendFrameIntoClientArea + WS_POPUP + WS_EX_LAYERED |
| 点击穿透 | 动态 WS_EX_TRANSPARENT |
| 多显示器 | ⚠️ isMultiMonitor 恒 false，规划中 |
| 睡眠恢复 | 帧间隙检测 → DWM 重建 → 物理重置 |
| DWM 安全 | 连续 5 次崩溃跳过重建 |
| 系统托盘 | Shell_NotifyIcon + 左/右键菜单 |
| 开机自启 | 注册表 HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run |
| 进程互斥 | Mutex "DesktopPet_Unity_SingleInstance" |
| 运行时日志 | `%LOCALAPPDATA%\Low\DefaultCompany\desktop pet\Player.log` |
| 崩溃日志 | 自动捕获 + 截断 (>2MB) |

---

## 13. 交互界面

| 元素 | 触发 | 尺寸 | 功能 |
|------|------|------|------|
| 悬浮球 | 右下角粉✦ | 36×36 | 展开辐射菜单 |
| BallPanel | 点击悬浮球 | 420×580px | 设置/报告/便签 |
| RightPanel | `~`键 / 划过右边缘 | 220px | 聊/设/签/告 + 输入栏 |
| ChatBubble | AI 回复 / 提醒 / 闲话 | 自适应 | 手绘圆角 + 12 星点 |
| 输入栏 | 自动显示 | 固定坐标 | Windows 搜索风格 |

**优先级**：High(AI 回复) > Normal(提醒/交互) > Low(闲话/问候)

---

## 14. 性能优化

### 14.1 自适应档位

| 档位 | FPS | RT 比例 | 触发 |
|------|-----|---------|------|
| High | 60 | 100% | 正常 |
| Normal | 40 | 75% | 中等负载 |
| Low | 20 | 50% | CPU/GPU>92% / 内存>93% |

### 14.2 内存管理

| 阈值 | 操作 |
|------|------|
| 800MB | System.GC.Collect() |
| 1.2GB | 强制 GC + 通知降档 |
| CPU/GPU>80% | 阻止升档 |

### 14.3 GpuLoadMonitor
- 检测游戏运行 → 暂停 LLM 动作决策
- 游戏退出 → 30s 冷却恢复

> ⚠️ `PerformanceMonitor.GetResolutionScale()` 恒返回 1.0f（分辨率缩放未实现）

---

## 15. 构建与部署

### 15.1 环境

| 项目 | 路径 |
|------|------|
| 引擎 | `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe` |
| 项目 | `D:\Unity\projects\Desktop_per_pro\code\desktop_unity\` |
| 输出 | `D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe` |

### 15.2 命令

```powershell
.\build.ps1                   # 完整构建
.\build.ps1 -Quick            # 编译验证
.\build.ps1 -RunTests         # Editor 测试
.\build.cmd                   # CMD 构建
```

### 15.3 注意事项

| 问题 | 解决 |
|------|------|
| Tuanjie 路径拼接 Bug | 必须 CD 到项目目录用相对路径（subst 方案） |
| 编辑器实例冲突 | Stop-Process -Name "Tuanjie" |
| exe 被锁 | Stop-Process -Name "DesktopPet" |

---

## 16. 已知偏差与 Bug（代码真相审计 2026-08-02 · N39 已修复）

> 本节为 2026-08-02 代码真相审计（详见 `code-truth-architecture.md`）的核心交付物：
> 16 项「文档 vs 代码」偏差 + 9 个已确认 Bug。**9 个 Bug 已于 N39（2026-08-02）全部修复**，
> 下列偏差记录了旧文档曾经的错误表述，供追溯。

### 16.1 文档 vs 代码偏差总表（16 项）

| # | 旧文档陈述 | 代码真相 | 严重度 | 状态 |
|---|---|---|---|---|
| 1 | 工具回环 5 轮 | `MAX_TOOL_ROUNDS = 10` | 高 | 已修正 |
| 2 | ToolEngine 6 文件 | 9 工具文件 + 7 基础设施 = 16 .cs、55 工具（N40 修正 52→55） | 高 | 已修正 |
| 3 | ActionAgent 15 组件 | 15 文件（N39 删 AutoMotionCollector 后 16→15） | 低 | 已修正 |
| 4 | "11 规则 + 12 特殊模式" | **10 规则 + 10 特殊（9 姿势，捂脸重复）** | 中 | 已修正 |
| 5 | 曲线含 "BounceEaseOut" | 实际为 `Bounce`（共 6 种：Linear/Smooth/EaseOut/EaseIn/Hold/Bounce） | 低 | 已修正 |
| 6 | 双模型校验（Qwen+GLM） | 单 GLM-4V（Qwen-VL 已删，qwenScore 已删） | 高 | N39 已修复 |
| 7 | "36 个核心脚本" | 顶层 33 + 子目录 ~50（合计 84 文件/36,356 行） | 中 | 已修正 |
| 8 | 知识库同步上下文 | `GetFormattedContext()` STUB 恒 "" → N39 已修复（返回 LastFormattedContext 缓存） | 高 | N39 已修复 |
| 9 | 反思机制驱动 | `OnReflectRequest` 恒 null → N39 已接线（CheckReflection→DoReflection） | 高 | N39 已修复 |
| 10 | 3D 渲染可用 | HybridRenderer TODO，强制 Live2D | 中 | 已修正 |
| 11 | 绿幕抠像 | 实际纯黑背景（注释与代码矛盾） | 低 | 已修正 |
| 12 | 多屏支持 | `isMultiMonitor` 恒 false | 中 | 已修正 |
| 13 | 动态分辨率降级 | `GetResolutionScale` 恒 1.0f | 低 | 已修正 |
| 14 | 默认表情 "curious" | 默认 "surprise" | 低 | 已修正 |
| 15 | 天气源 QWeather 主 | **默认 wttr.in**（cityCode="Nanjing"），QWeather 可选 | 中 | 已修正 |
| 16 | AutoMotionCollector 活动采集 | [Obsolete] 死代码 → **N39 已删除** | 中 | N39 已修复 |

### 16.2 已确认 Bug 清单（9 个，N39 全部修复）

| # | 位置 | 问题 | 影响 | 状态 |
|---|---|---|---|---|
| BUG-1 | `MotionAgent.cs` | `IsSleepTime()` 硬编码 `return false`（debug 跳过注释未删） | 睡眠时段密度永远不触发 | ✅ N39 已修复 |
| BUG-2 | `MotionAgent.cs` | `ExecuteMotion` 播放**前**加 AI 锁（L985），`ExecuteCombo` 播放**后**加锁（L1062） | 锁序不一致，连招期间 AI 可能插话 | ✅ N39 已修复 |
| BUG-3 | `MotionAgent.cs` | `GatherContext()` 注释称农历月相，实际用 `now.Day` 公历 | 月相感知名不符实 | ✅ N39 已修复 |
| BUG-4 | `ActivityTracker.cs` | `UpdateInteractionTime()` 空实现 | 交互时间未更新 | ✅ N39 已修复 |
| BUG-5 | `DualModelValidator.cs` | 名为 "Dual" 实为单 GLM-4V（Qwen-VL 已移除） | 双模型校验名存实亡 | ✅ N39 已修复 |
| BUG-6 | `AutoMotionCollector.cs` | [Obsolete] 死代码 331 行未删 | 维护负担 | ✅ N39 已删除 |
| BUG-7 | `MotionTranslator.cs` | 示例用 `arm_right_upper: 0.8`，与规则 "15-25 度" 矛盾 | 可能误导 LLM 输出 | ✅ N39 已修复 |
| BUG-8 | `MotionTranslator.cs` | 文档称 "11 规则+12 特殊"，实际 10+10 | 文档错误 | ✅ N39 已澄清 |
| BUG-9 | `MotionPlanner.cs` | 文档称曲线含 "BounceEaseOut"，实际 `Bounce` | 文档错误 | ✅ N39 已澄清 |

### 16.3 修复记录（N39，2026-08-02）

| 优先级 | 项 | 修复方式 |
|---|---|---|
| P0 | BUG-1 `IsSleepTime()` | 真实凌晨 1~7 点判断 + testMode 保护 |
| P0 | BUG-2 `ExecuteCombo` 锁序 | AI 锁移至播放前（与 ExecuteMotion 一致） |
| P1 | 偏差 #9 反思未接线 | CheckReflection→DoReflection 接线，死回调 OnReflectRequest 删除 |
| P1 | 偏差 #8 知识库 STUB | GetFormattedContext 返回 LastFormattedContext 缓存 |
| P1 | BUG-5 双模型名不副实 | 回调签名简化 4 参，删除恒 0 qwenScore |
| P2 | BUG-3/4/6/7 | 农历月相 / 交互时间接线 / 死代码删除 / 示例改角度 |
| P2 | 偏差 #10-16 | 文档如实标注能力边界 |

> **N40 后续（2026-08-07，T1-T8 Token 优化）**：时间戳缓存 98.6% 命中 / body schema 精简 / max_tokens 1200+thinking off / 工具子集 55→27 / 历史 15000 字符预算 + Ollama 摘要 / SystemPrompt -41% / Speculative Multi-Action / usage 日志。详见 `code-truth-architecture.md`。

---

## 附录

### A. 配置体系

```
环境变量:
  DEEPSEEK_API_KEY → ChatManager / IdleChatGenerator / MotionTranslator
  GLM_API_KEY       → ToolEngine / VisionMotionVerifier
  QWEATHER_API_KEY  → TimeWeatherController（可选，默认 wttr.in）
  BRIDGE_TOKEN      → OpenClawBridge 鉴权（64 字符，fallback GATEWAY_TOKEN）

JSON 持久化 (D:\DesktopPetData\):
  pet_config.json       pet_memory.json        reminders.json
  motion_memory.json    pet_personality.json   activity_log.json
  knowledge_base.json   Documents/  ActionRefs/  glm_collages/
```

### B. 执行顺序优先级

| Priority | 脚本 |
|----------|------|
| -100 | WindowOverlay, SystemTrayManager |
| 0 | DesktopPet |
| 800 | CubismPhysicsController (SDK) |
| 801 | Live2DRenderer |

### C. 闭环数据流全景

```
感知 ──→ 决策 ──→ 翻译 ──→ 执行 ──→ 验证 ──→ 记忆
  ↑                                            │
  └────────────────────────────────────────────┘

ActivityTracker / BrowserTabReader / TimeWeather
PerformanceMonitor / GpuLoadMonitor
PetMemory ──→ ContextGatherer ──→ MotionAgent
                                    │
                                    ↓
                    MotionTranslator (DeepSeek, 10 规则 + 10 特殊)
                    MotionPlanner (10 硬编码模板 + 6 曲线)
                                    │
                                    ↓
                    MotionGenerator (插值)
                                    │
                                    ↓
                    VisionMotionVerifier (GLM-4V, 10 视觉测试)
                                    │
                                    ↓
                    MotionMemoryManager (30 条上限)
                    ChatManager (自评回复)
```
