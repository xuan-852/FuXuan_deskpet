# 改动日志

## V2.3+ (2026-07-13)

### ✨ 新功能
- **运动记忆自反馈** — `MotionMemoryManager` 新增负面反馈阈值、绝望检测、冷却机制、PhysicallyImpossibleActions 黑名单
- **多帧拼图评分** — GLM-4V 单模型对动作视频的多帧拼图进行一致性评分，替代移除的 Qwen-VL
- **GpuLoadMonitor 游戏检测** — 检测 GPU 被游戏占用时拦截 LLM 动作决策，避免影响游戏性能
- **SafetyValidator 参数校验** — 校验 LLM 生成的动作参数不超出物理安全阈值

### 🔧 技术改进
- **移除 Qwen-VL-Plus** — 因 401 Unauthorized 不可用，视觉验证统一为 GLM-4V 单模型 + 多帧拼图
- **Newtonsoft.Json 迁移** — 全面替换手写 JSON 解析器，提升稳定性
- **MotionAgent 架构清理** — MotionPlanner(11模板/6曲线/6表情)、MotionTranslator(LLM→Live2D参数)、MotionExecutor(插值执行)
- **闭环验证 P0 修复** — 修复数据流断裂，确保验证反馈正确写入运动记忆
- **MotionTranslator 参数保护** — 防止 LLM 生成极限参数值损坏 Live2D 模型

## V2.3 (2026-07)

### ✨ 新功能
- **报告面板复制按钮** — RightPanel 报告标签页支持一键复制内容
- **MotionAgent 自主动作决策** — AI 驱动的动作生成全链路（规划→翻译→执行→验证→记忆）

## V2.2 (2026-07)

### ✨ 新功能
- **悬浮球 + 辐射菜单完善** — BallPanel 拖拽交互优化 + RightPanel 多标签页（设置/报告/便签）
- **动作系统重构** — 拆分为 Live2DFramework（参数映射/物理）、ActionAgent（动作选择/空闲动作管理）

## N18 (2026-06-22)

### ✨ 新功能
- **已完成任务独立视图** — 便签页新增「✅ 已完成」按钮，已完成的任务单独显示，不混在待办列表里。待办/已完成可自由切换查看
- **系统总内存监控** — 通过 `GlobalMemoryStatusEx` 监控物理内存占用，85% 预警 GC，93% 紧急降档保命，防止 VS Code 与桌面宠物抢内存导致被杀进程
- **`GetDoneReminders()`** — ReminderManager 新增已完成提醒查询方法

### 🐛 修复
- 系统内存 > 93% 时自动强制 GC + 通知 PerformanceMonitor 降档至 Low（20fps, 50% RT），减少内存压力，避免被系统自动关掉

---

## N17 (2026-06-22)

### ✨ 新功能
- **提醒去重机制** — `ReminderManager` 新增 `HasPendingReminderContaining()` 和 `DeletePendingRemindersContaining()` 方法，支持按关键词检查/删除重复待办
- **服务器推送去重** — `HandleExamReminder` 添加考试复习提醒前，先用课程名检查 `ReminderManager` 是否已有同类待办，避免 AI 手动设置 + 服务器推送的重复

### 🐛 修复
- 考试提醒与用户手动设置（如「提醒我高数考试」）不再重复。保留用户先设的提醒，服务器推送的同类提醒自动跳过

---

## N16 (2026-06-20)

### ✨ 新功能
- **Everything 毫秒级文件搜索** — 集成 Everything CLI（es.exe），`search_files` 工具优先调用 Everything 实现全盘毫秒级搜索，未安装时自动回退递归搜索
- **Everything CLI 自动检测** — 启动时自动扫描 Program Files、LocalAppData 及 PATH，智能定位 es.exe

### 🐛 修复
- **文件搜索 3 秒超时** — 根因：AI 使用 `run_command` 的 `dir /s` 搜索文件，但 `p.WaitForExit(3000)` 仅 3 秒即 `p.Kill()`
  - 创建专用 `search_files` 工具，Task.Run 异步执行 + 10 秒超时
  - 重写为 Everything CLI 优先（毫秒级），回退递归搜索
  - 系统提示词新增「搜文件铁则」引导 AI 使用正确工具

---

## N15 (2026-06-20)

### 🐛 修复
- **逐句显示 bug** — 多句回复逐句播完后不再将气泡替换为完整全文。之前最后一句播完后 `OnSentenceChanged` 会额外触发一次将气泡设为 `_fullReplyText`，导致"一句一句→突然全文又出来了"的怪异表现。

---

## N14 (2026-06-20)

### ✨ 新功能
- **小程序数据打通** — 符玄现在可以实时查询课表小程序服务端的学业数据：
  - `query_scores` — 查询各科成绩（分学期，含分数/学分/课程属性）
  - `query_schedule` — 查询课表（可选周次，含教师/教室/节次）
  - `query_user_status` — 查询学业概览（学号/学期/数据统计）
  - 配合已有的 `query_exams` 实现完整学业数据查询

### 🔧 技术
- **服务端 API** — `pet.js` 新增 3 个端点：`GET /scores`、`GET /schedule?week=N`、`GET /user/status`
- **ServerPollService** — 新增 3 个异步查询方法 + JSON 反序列化模型
- **ToolCallInvoker** — 工具注册从 21 个扩展到 24 个
- **ChatManager** — 系统提示词新增「第 9 条术式：卜算传讯」章节

---

## N13 (2026-06-20)

### ✨ 新功能
- **服务端推送轮询** — `ServerPollService` 定期轮询 Node.js 服务端获取推送队列
- **Server酱³ 推送** — `ReminderManager` 到期提醒通过 Server酱³ 推送到手机
- **桌面开机通知** — 宠物启动时服务端推送桌面已开机通知

### 🔧 技术
- 新增 `ServerPollService.cs` — HTTP 轮询 + 推送队列消费 + 自动重试
- `ReminderManager.cs` 重构 — 分离本地提醒和服务端推送逻辑
- `DesktopPet.cs` 启动链中集成服务端轮询

---

## N12 (2026-06-19)

### ✨ 新功能
- **性能监控** — `PerformanceMonitor` 实时监控 FPS/CPU/内存，FPS 过低时通知动画系统降低频率
- **桌面宠物开机自启** — 系统托盘菜单支持一键设置开机自启（VBS 脚本）

### 🔧 技术
- `PerformanceMonitor.cs` 新增 — 基于 `PerformanceCounter` 的 CPU/内存监控
- `SystemTrayManager.cs` 新增开机自启管理

---

## N11 (2026-06-18)

### 🐛 修复
- **AI 回复与自动闲聊抢占** — 引入优先级气泡系统（`MsgPriority` 枚举）：
  - `High` = AI 主动回复，不可被低优先级消息覆盖
  - `Normal` = 提醒、交互回应
  - `Low` = 闲话、定时问候
- **AI 输出稳定版** — 修复 DeepSeek 回复时被自动问候打断的问题
- **ToolCallInvoker 工具注册防抖** — 避免重复注册导致的工具调用异常

### 🔧 技术
- `ChatBubble.cs` 重构 — 新增 `MsgPriority` 枚举 + 优先级比较逻辑
- `AutoChat.cs` — 更新 `HandleSentenceChanged` 使用高优先级显示 AI 回复

---

## N10 (2026-06-18)

### ✨ 新功能
- **启动点击穿透** — 宠物启动时鼠标可穿透到桌面，无需先拖拽"激活"
- **右键菜单穿透管理** — 菜单打开时区域内可交互、区域外穿透
- **Server酱³ 推送集成** — 便签到期可通过 Server酱³ 推送到手机

### 🐛 修复
- **AI 多轮工具调用** — DeepSeek Function Calling 多次工具调用回环支持（最多 5 轮）
- **右键菜单穿透** — 修复菜单打开时点击穿透逻辑
- **底部输入栏序列化** — 修复 `GUI.FocusControl` 在构建版中的竞态条件

---

## v0.9 (2026-06-18)

### ✨ 新功能
- **DWM 玻璃层透明** — 窗口透明从色键（#00FF00）切换为 DWM 玻璃层扩展（`DwmExtendFrameIntoClientArea`），彻底解决半透明绿边问题
- **简化聊天 UI** — 移除消息历史/状态栏/工具栏，仅保留输入框 + 发送按钮
- **底部输入栏** — Windows 搜索风格（白底 + 浅灰输入框 + 蓝按钮），固定坐标系统（`BAR_LEFT/RIGHT/TOP/BOTTOM`），淡入动画 0.5s

### 🔧 技术
- Camera 背景统一改为纯黑 (0,0,0,0)
- `BuildScript.cs` 重建（自动构建脚本）
- 移除 Live2D 全部视觉特效参数（星辉/法阵/数钱的 Param121/132/137 等）
- 更新完整 LaTeX 技术文档

---

## v0.8 (2026-06-17)

### ✨ 新功能
- **头发/裙子/法盘直接物理驱动** — 20+ 输出参数绕过 CubismPhysics 延迟（0.8s → 即时）
- **分区点击反馈** — 头部戳脸 / 身体害羞捂胸 / 腿部踢腿
- **拖拽挣扎动画** — 双臂划水 + 双腿交替 + 身体扭动 + 慌张表情
- **时间/天气响应系统** — wttr.in API 获取天气，昼夜犯困眼皮，天气表情联动
- **第 11 个空闲动作** — 困惑（AI 触发专用）
- **待机气泡** — 支持时间/天气特化内容

### 🐛 修复
- 行走时衣服不随体态飘动 — 体态参数提前到 `Update()` 设置
- 手臂幅度过大 — 分离大/小范围手臂参数常量
- 物理覆盖手臂参数 — `ForceUpdateNow()` 强制网格重算

---

## v0.7 (2026-06-17)

### 🐛 修复
- **行走手臂被物理覆盖** — 添加 `[DefaultExecutionOrder(801)]` 确保 LateUpdate 跑在 `CubismPhysicsController` 之后
- **体态参数时序** — 提前到 `Update()` 设置供物理驱动衣服
- **手臂 clamp 满幅** — 分离大/小范围手臂参数常量

---

## v0.6 (2026-06-13)

### 📚 文档
- 创建 LaTeX 技术文档 `project_brief/report.tex`
- 完善 README.md

### 🎮 新功能
- 右键菜单 UI（设置/动作 2 标签）
- 动作锁定机制 + 走路淡入过渡（0.3s）

---

## v0.5

### ✨ 新功能
- 动作锁定机制（播放期间不被走路覆盖）
- 走路淡入过渡

---

## v0.4

### ✨ 新功能
- 右键菜单 UI
- 权重编辑（5 种地面任务权重滑块）

---

## v0.3

### ✨ 新功能
- 10 种空闲动作循环
- 强制动作系统
- 加权随机动作选择（替代顺序循环）

---

## v0.2

### ✨ 新功能
- 地面状态机（MoveLeftEdge / MoveRightEdge / MoveLeftTime / MoveRightTime / StopTime）
- 走路动画（转体 + 抬腿摆臂 + 垂直颠簸）
- 像素物理引擎（重力 + 边界碰撞 + 抛掷）

---

## v0.1

### ✨ 新功能
- 透明窗口（Win32 API: DWM + WS_EX_LAYERED）
- 基础像素物理
- Live2D 模型加载与渲染
- 点击穿透动态管理
- 拖拽移动 + 抛掷
