# 改动日志 — 太卜司符玄桌面灵伴

> 版本命名：`N<数字>` 为迭代号，`v<数字>` 为早期版本。

---

## N41 (2026-08-09)

### 🧩 像素表情包（`RightPanel.cs`）

- **9 种脸部表情帧**：`LoadMascotEmote` 在 17×24 像素形象上重绘脸部（happy 眯眼笑 / angry 怒眉抿嘴 / sad 泪滴 / surprise 大眼 O 嘴 / confused 挑眉歪嘴 / sleepy 闭眼哈欠 / blush 大红脸 / love 微笑腮红 / tear 泪汪汪），×4 Point 放大显示
- **坐标修复**：所有表情帧 `Set(列,行)` 坐标反写 bug 修正（之前 `Set(行,列)` 导致修改落在头发区），Python 复刻脚本（`tools/gen_mascot_emotes.py`）diff 验证全部落在脸部
- **徽章方向修复**：`GetEmblemTex` 手工点阵写入纹理时行序与 PNG 相反导致 GUI 上下颠倒——`SetPixel(x, rows.Length-1-y)` 行反转后爱心/感叹号/问号/Z 方向正确
- **love 删爱心眼**：6px 爱心眼太小糊脸挡眼，删除，只保留微笑嘴 + 脸颊粉，爱心由右上角徽章表达

### 🚫 颜文字禁绝（`SystemPrompt.txt` + `ChatManager.cs`）

- SystemPrompt 明令禁止输出颜文字/emoji（如 `(T_T)`、`(^▽^)`、QAQ），情绪一律用 `【表情:xxx】` 表达
- **代码兜底**：即便模型违规输出，`StripKaomoji` 将常见颜文字翻译为 Live2D 脸部表情（`PlayExpression`）+ 像素表情帧（`OnExpressionTag`）并剥除文本，气泡只显示纯净话语
- 覆盖所有显示路径：逐句流式 + 完整回复均经 `StripAndExecuteActions` 处理

---

## N40 (2026-08-05 ~ 08-08)

### 🔒 安全加固（08-05，`b17ad84` + `91d2c32`）

- **危险工具确认机制**：高风险工具统一走 `ToolConfirmManager`（点击宠物允许 / ESC 拒绝 / 60s 超时拒绝）
- **命令与路径白名单收紧**（`ToolHelpers.cs`）；隐私脱敏；构建防锁
- **Bridge 鉴权**：OpenClawBridge 移除内置旧 Token 回退——**只读环境变量 `BRIDGE_TOKEN`**（与 `openclaw_bridge.js` / PM2 同源），未配置时禁用桥接；旧 Token 已泄漏并轮换
- **桥修复**：Gateway final 事件 `message.content` 是数组——展开为字符串再校验，修复骨架阶段误判元数据失败；分块生成健壮化（拦截元数据 JSON 混入/响应错位丢节）

### ⚡ Token 优化 T1–T8 全部完成（08-07~08，`a78e62c` + `52f9cec` + `bad7487` + `5f0a048` + `77645d4` + `807290e` + `0b7480c`）

> 依据 `docs/token-optimization-plan.md`，2026-08-07 token 审计后按 ROI 分批执行。

| # | 优化 | 实测效果 |
|---|---|---|
| T1 | 时间戳移到 SystemPrompt 末尾（固定前缀命中缓存） | 缓存命中率 23.9%→33.9%→**98.6%** |
| T2 | MotionTranslator body schema 按描述裁剪部位 | 2543~5435 字符（原 ~13k tokens，降 60-80%） |
| T3 | `max_tokens:1200` + 超时 30→60s + **`thinking` 显式禁用**（deepseek-v4-flash 推理模型默认 thinking 占满 max_tokens 致 `content=""`） | 40 次超时全消灭，翻译恢复，completion 394-560 |
| T4 | 工具回环按意图注入子集（首轮 55→27，回环轮 31） | 首轮 -51%，多轮对话 -60% |
| T5 | 历史 15000 字符预算 + Ollama 旧史摘要注入【旧事纪要】 | 对话中段收敛，防 tool_calls↔tool 配对切断（API 400） |
| T6 | SystemPrompt 5012→2972 字符（-41%），删重复段（当前时刻/经典台词/闭环演武并入代码版） | 静态段瘦身 |
| T7 | Speculative Multi-Action（UFO² 思路）：一次返回多个独立 tool_call | 单次响应双工具（completion=7902） |
| T8 | `usage.prompt_cache_hit_tokens` 打日志 | 命中率可观测 |

- **T4 竞态修复**（`0b7480c`，08-08）：工具子集构建加竞态保护 + 第二批实测验证 + 测试模式开关

**工具数核实**：注册工具全量清点为 **55 个（9 个工具文件 + 7 个基础设施 .cs）**，随 N40 文档修订统一更新。

---

## N39 (2026-08-02)

### 🔧 代码修复（N38 审计出的 9 个 Bug + 2 项结构性偏差）

> 按 `docs/code-truth-architecture.md` 第 10 节修复优先级执行，`build.ps1 -Quick` 编译验证通过。

**P0 — 睡眠调度与锁时序**
- **BUG-1** `MotionAgent.IsSleepTime()`：删除调试期硬编码 `return false`，恢复真实判断（凌晨 1~7 点为睡眠时段），保留 `testMode` 测试保护
- **BUG-2** `MotionAgent.ExecuteCombo`：AI 控制锁 `SetAiControlLock` 移到 `PlayAsync` **之前**加锁，与 `ExecuteMotion` 锁序一致（连招期间空闲动画不再争抢参数）

**P1 — 反思 / 知识库 / 验证器名实相符**
- **#9 反思机制**：审计澄清——反思实际已由 `ChatManager.SendRequestCoroutine → CheckReflection → DoReflection`（DeepSeek 提炼）→ `CommitReflection` 接线；删除从未被调用的 `OnReflectRequest` 死字段（`PetMemory` + `ChatManager` 死注册）
- **#8 知识库**：`KnowledgeBaseManager.GetFormattedContext()` 不再恒返回 ""，新增 `LastFormattedContext` 缓存（由协程 `SearchAndFormat` 填充），同步 API 返回最近一次检索结果
- **BUG-5** `DualModelValidator.ValidateAsync`：回调签名从 7 参简化为 4 参 `(isConsensus, avgScore, glmScore, glmReview)`（删除恒 0 的 qwen 参数），同步更新 `MotionAgent` / `MotionCoroutineTools` 两处调用方；类注释明确单 GLM-4V

**P2 — 其余 Bug 与死代码**
- **BUG-3** `MotionAgent.GatherContext()`：用 .NET 内置 `ChineseLunisolarCalendar` 计算真实农历日并映射月相（新月/蛾眉/上弦/满月/下弦/残月/晦月），不再谎称农历实则用公历日
- **BUG-4** 交互时间：`ActivityTracker` 新增 `LastActivityTime`（前台窗口切换时更新时间戳），`MotionAgent.UpdateInteractionTime()` 的空分支接线为读取该时间戳
- **BUG-6** 删除 `AutoMotionCollector.cs`（331 行 `[Obsolete]` 死代码）+ `.meta` + `DesktopPet` 中的组件自动添加逻辑
- **BUG-7** `MotionTranslator` "excited wave" 示例：`arm_right_upper: 0.8` 改为角度值（20~28，与 SPECIAL PATTERNS 招手规则一致），消除对 LLM 的误导
- BUG-8 / BUG-9 为文档错误，已随 N38 文档重写修正，代码本身无需改动

**保持现状（规划中能力，勿在文档中宣称可用）**：3D 渲染模式（HybridRenderer TODO）、多屏支持（`isMultiMonitor`）、动态分辨率降级（`GetResolutionScale`）。

---

## N38 (2026-08-02)

### 📚 代码真相审计与文档重写

> 以 `code/desktop_unity/Assets/Scripts/` 下真实代码为准，对全部文档做了审计与重写。
> 权威结论见 [`docs/code-truth-architecture.md`](docs/code-truth-architecture.md)。

**审计确认的真相数字**（修正此前文档偏差）：
- 工具回环：**10 轮**（原文档误写 5 轮）；注册工具 **52 个 / 10 文件**（原误写 40+/6 文件）
- ActionAgent：**16 文件**（原误写 15）；MotionTranslator：**10 规则 + 10 特殊模式**（原误写 11+12）
- MotionPlanner：**10 模板 + 6 曲线**（含 `Bounce`，原误写 BounceEaseOut）
- **DualModelValidator 仅 GLM-4V**（Qwen-VL 已删，qwenScore 恒 0），非双模型
- 天气源：**默认 wttr.in**（cityCode="Nanjing"），QWeather 为可选
- 反射机制：`OnReflectRequest` 回调恒 null，反思未驱动
- `KnowledgeBaseManager.GetFormattedContext()` 为 STUB 恒返回 ""
- `isMultiMonitor` 恒 false；`GetResolutionScale` 恒 1.0f；3D 渲染不可用；`AutoMotionCollector` 死代码

**已确认 9 个 Bug**（BUG-1~BUG-9，详见审计文档第 5 节）：
1. BUG-1 `MotionAgent.IsSleepTime()` L369 硬编码 `return false`
2. BUG-2 `ExecuteCombo` 播放后才加 AI 锁（L1062 vs ExecuteMotion L985）
3. BUG-3 `GatherContext()` 注释称农历月相，实际用 `now.Day` 公历
4. BUG-4 `ActivityTracker.UpdateInteractionTime()` 空实现
5. BUG-5 DualModelValidator 名不副实（仅单模型）
6. BUG-6 AutoMotionCollector 死代码 331 行
7. BUG-7 MotionTranslator 示例 `arm_right_upper: 0.8` 与规则 15-25° 矛盾
8. BUG-8 规则数文档 11+12 与实际 10+10 不符
9. BUG-9 曲线文档 BounceEaseOut 与实际 Bounce 不符

**重写文档**：README.md、CHANGELOG.md、docs/report.md 、docs/task-inventory.md、docs/report.tex、record/Latex/report.tex 等 12 份文档全部按代码真相重写或修订。

---

## N37 (2026-07-26)

### ✨ 新功能
- **言出法随 — 内嵌动作标记系统** — AI 回复文本中嵌入 `【表情:开心】` / `【动作:伸懒腰】` / `（自然描述）` 标记，无需 tool_call 即可同步触发表情动作
- **ToolEngine 插件架构** — 4065 行 `ToolCallInvoker.cs` 拆分为 1 接口 + 1 注册器 + 6 分类工具文件，反射自动发现
- **OpenClaw 搜索网关集成** — 通过 GatewayChatClient 实时搜索，180s 超时，自动健康检查

### 🔧 技术改进
- 修复 Unity Mono `Environment.TickCount64` 不兼容问题
- `@""` 逐字字符串 `\"` → `""` 转义修复 Unity 编译陷阱
- SystemPrompt.txt 新增「言出法随」章节

---

## N36 (2026-07-20)

### ✨ 新功能
- **ToolCallInvoker 插件化重构** — 单文件 4065 行 → 插件架构：`IPetTool` 接口 + `ToolRegistry` 反射发现 + 6 分类工具文件
  - 净减少 ~650 行代码，编译通过 ✅

---

## N35 (2026-07-13)

### ✨ 新功能
- **运动记忆自反馈** — `MotionMemoryManager`：负反馈阈值 / 绝望检测 / 冷却机制 / 黑名单
- **GLM-4V 多帧拼图评分** — 20/40/60/80% 截图合成 2×2 拼图，替代已移除的 Qwen-VL
- **GpuLoadMonitor 游戏检测** — GPU 被游戏占用时拦截 LLM 动作决策
- **SafetyValidator 参数校验** — 互斥组 / 对称对 / 极端值 3 类保护
- **异步工具框架** — 支持 `IEnumerator` 协程工具（`generate_motion` 等）

### 🔧 技术改进
- 移除 Qwen-VL-Plus（401 不可用），视觉验证统一 GLM-4V
- Newtonsoft.Json 全面替换手写 JSON 解析器
- MotionAgent 架构清理：Planner(10模板/6曲线/6表情) + Translator(DeepSeek→Live2D) + Executor(插值)
  - ⚠️ 注：Planner 实际为 **10 模板**（含 Bounce 曲线），此处历史条目与代码真相审计（N38）一致
- 闭环验证 P0 修复：确保验证反馈正确写入运动记忆
- MotionTranslator 参数保护：防止极限值损坏模型

---

## N34 (2026-07-07)

### 🔧 技术改进
- **MotionTranslator SPECIAL PATTERNS 全面优化** — 基于 vis_verify 基线：
  - 捂脸: arm_mid 升至面部 → 视觉可见
  - 捂嘴: 新增 ONE_HAND 口诀 + `mouth_open_y=0`
  - 叉腰: arm_lower 0.15→≥0.4
  - 缩团: 加 body_angle_y 弓身前倾
  - 行礼: body_angle_x → body_angle_y 修正
- **VERIFIED FEEDBACK**：告知 DeepSeek 上次失败案例
- **闭环自评**：`generate_motion` 播放完毕自动截图 → GLM 评分 → 追加回复

---

## N33 (2026-07-06)

### ✨ 新功能
- **具身智能验证协议** — `MotionVerifier.cs`：自动测试套件（对照组 5 + 测试组 10 + 边界 4）
- **闭环数据流** — MotionAgent → Translator → Planner → Generator → Validator → Memory
- **人格演化系统 V3** — `PersonalityManager.cs`：五维人格 + 三维关系 + 里程碑
- **MotionMemoryManager** — 学习核心：30 条上限 + 负反馈 + 无望检测 + 冷却

---

## N32 (2026-07-06)

### ✨ 新功能
- **GLM-4V 视觉验证** — `VisionMotionVerifier.cs`：截图→GLM 评分
- **盲探索** — GLM 描述当前姿态，发现模型能做到的动作
- **`inspect_personality` 工具** — #46 人格自省
- **SystemPrompt 注入闭环能力说明**

### 🔧 技术改进
- MotionTranslator 规则从 5→10，特殊模式从 6→10
  - ⚠️ 注：审计（N38）确认实际为 **10 规则 + 10 特殊模式（9 种姿势，捂脸重复）**，此处按代码真相修正

---

## N28 (2026-07-03)

### ✨ 新功能
- **通用动作代理系统** — 七阶段路线图完成 Phase 1-3
- **ParameterVisionScanner** — Editor 窗口，GLM-4V 参数视觉扫描
- **ParameterKnowledgeProvider** — 参数知识注入 ChatManager
- **MotionPlanner** — 10 种硬编码模板 + 6 种插值曲线
- **MotionGenerator** — 协程插值播放引擎
- **IdleActionScheduler** — 加权随机空闲动作调度
- **SafetyValidator** — 物理安全校验

---

## N27 (2026-06-28)

### 🔧 技术改进
- 构建流水线标准化：`build.ps1` 支持 `-Quick` / `-RunTests`
- 针对 Tuanjie 路径拼接 Bug 的 subst 替代方案

---

## N26 (2026-06-25)

### ✨ 新功能
- **OpenClaw Bridge** — 搜索网关，基于 WebSocket GatewayChatClient 实时搜索
- **V2.0 参数知识体系** — fuxuan_map.json v2 含分组/域/关联/前提

---

## N25 (2026-06-24)

### ✨ 新功能
- **Multi-Display 支持** — VirtualScreen 跨屏
  - ⚠️ 注：审计（N38）确认 `WindowOverlay.isMultiMonitor` 当前恒为 false，跨屏行走为规划中能力
- **睡眠唤醒恢复** — DWM 重建 + 物理重置

---

## N24 (2026-06-23)

### ✨ 新功能
- **KnowledgeBaseManager** — 本地 RAG 知识库
- **LocalLLMAgentService** — Ollama 本地决策（0.5B/3B；N38 核实实际为 `qwen2.5:3b`）

---

## N23 (2026-06-22)

### ✨ 新功能
- **ProactiveMessageScheduler** — 主动消息调度
- **VisualHeartbeat** — 系统托盘心跳动画

---

## N22 (2026-06-22)

### ✨ 新功能
- **已完成任务独立视图** — 便签页「✅ 已完成」按钮
- **系统总内存监控** — `GlobalMemoryStatusEx`，85% 预警 GC，93% 紧急降档
- **GpuLoadMonitor** — 游戏检测自动降档

### 🐛 修复
- 内存 > 93% 时强制 GC + 降档 Low(20fps, 50%RT)

---

## N21 (2026-06-22)

### ✨ 新功能
- **提醒去重机制** — `HasPendingReminderContaining()` / `DeletePendingRemindersContaining()`
- **服务器推送去重** — 课程名检查避免重复

### 🐛 修复
- 考试提醒不与用户手动设置的重复

---

## N20 (2026-06-21)

### ✨ 新功能
- **MotionAgent 自主动作决策** — 四档密度(4s/8s/15s/30s)
- **DualModelValidator** — GLM-4V (裁决) + Qwen-VL (辅助)
- **`generate_motion` 工具** — DeepSeek LLM 任意动作生成
- **MotionTranslator** — 自然语言 → Live2D 关键帧

### 🔧 技术改进
- 报告面板复制按钮 — RightPanel 一键复制

---

## N19 (2026-06-21)

### ✨ 新功能
- **悬浮球 (BallPanel)** — 右下角粉✦ + 420×580px 辐射菜单
- **右侧面板 (RightPanel)** — `~`键 / 鼠标划过展开
- **动作系统重构** — 拆分为 Live2DFramework / ActionAgent
- **SystemPrompt.txt** — 独立提示词文件

---

## N18 (2026-06-20)

### ✨ 新功能
- **Everything 毫秒级文件搜索** — `search_files` CLI 优先，自动回退
- **服务端轮询** — `ServerPollService` 定时轮询
- **Server酱³ 推送** — 手机推送
- **课表/成绩/考试查询**

### 🐛 修复
- 文件搜索 3s 超时：`dir /s` → `search_files` 专用异步工具
- 逐句显示：最后一句不再覆盖全文

### 🔧 技术
- 工具注册 21→24 个，新增 `query_*` 系列

---

## N17 (2026-06-20)

### ✨ 新功能
- **服务端推送轮询 + 开机通知**
- **Server酱³ → 手机推送**

### 🔧 技术
- 新增 `ServerPollService.cs`
- `ReminderManager.cs` 重构分离本地/推送逻辑

---

## N16 (2026-06-19)

### ✨ 新功能
- **PerformanceMonitor** — FPS/CPU/内存监控
- **开机自启** — 注册表 HKCU\Run 管理

---

## N15 (2026-06-18)

### ✨ 新功能
- **启动点击穿透** — 无需先拖拽"激活"
- **Server酱³ 推送集成**

### 🐛 修复
- AI 多轮工具调用（最多 5 轮；N38 核实实际 `MAX_TOOL_ROUNDS=10`）
- 右键菜单穿透逻辑
- 底部输入栏序列化竞态条件

---

## N14 (2026-06-18)

### ✨ 新功能
- **优先级气泡系统** — High(AI) > Normal(提醒) > Low(闲话)
- **工具注册防抖**

### 🐛 修复
- AI 回复被自动问候打断

---

## N13 (2026-06-18)

### ✨ 新功能
- **DWM 玻璃层透明** — 彻底解决绿边
- **底部输入栏** — Windows 搜索风格
- **聊天 UI 简化** — 仅输入框 + 发送

### 🔧 技术
- Camera 背景 (0,0,0,0)
- `BuildScript.cs` 重建
- LaTeX 文档完整更新

---

## N12 (2026-06-17)

### ✨ 新功能
- **物理直接驱动** — 20+ 参数绕过 CubismPhysics 延迟
- **分区点击反馈** — 头/身/腿
- **拖拽挣扎动画** — 双臂划水 + 慌张表情
- **时间/天气响应** — wttr.in + 表情联动
- **第 11 空闲动作** — 困惑（AI 触发）
- **待机气泡** — 时间/天气特化

### 🐛 修复
- 行走衣服不随体态飘动
- 手臂幅度过大 / 物理覆盖

---

## N11 (2026-06-17)

### 🐛 修复
- 行走手臂被物理覆盖 → `DefaultExecutionOrder(801)`
- 体态参数时序 → 提前 Update()
- 手臂 clamp 满幅 → 分离常量

---

## N10 (2026-06-13)

### ✨ 新功能
- 右键菜单 UI（设置/动作 2 标签）
- 动作锁定 + 走路淡入(0.3s)

### 📚 文档
- LaTeX 技术文档
- 完善 README.md

---

## N9 (2026-06-12)

### ✨ 新功能
- 地面状态机（5 种行为加权随机切换）
- 走路动画（转体 + 抬腿 + 垂直颠簸）
- 像素物理引擎（重力 + 碰撞 + 抛掷）

---

## N8 (2026-06-11)

### ✨ 新功能
- 10 种空闲动作循环
- 强制动作系统
- 加权随机动作选择

---

## N7 (2026-06-10)

### ✨ 新功能
- 右键菜单 UI
- 权重编辑滑块

---

## N6 (2026-06-09)

### ✨ 新功能
- Perlin 噪声空闲微动（7 通道）
- 自动眨眼
- 鼠标眼睛跟随

---

## N5 (2026-06-08)

### ✨ 新功能
- 行走犯困表情系统
- 夜间/深夜频率递增

---

## N4 (2026-06-07)

### ✨ 新功能
- 系统托盘（Shell_NotifyIcon）
- ESC 隐藏到托盘
- 进程互斥（Mutex）

---

## N3 (2026-06-06)

### ✨ 新功能
- LLM 对话集成（DeepSeek Chat + Function Calling）
- 打字机逐句显示
- 自动闲聊
- 天气 API 集成

---

## N2 (2026-06-05)

### ✨ 新功能
- 点击穿透动态管理（WS_EX_TRANSPARENT）
- 拖拽抛掷物理

---

## N1 (2026-06-04)

### ✨ 新功能
- DWM 透明窗口（WS_EX_LAYERED + DwmExtendFrameIntoClientArea）
- Live2D 模型加载（AssetDatabase / Resources 双保险）
- 基础像素物理（重力 + 地面碰撞 + 弹跳）
- 拖拽移动
