# 符玄桌宠 → 桌面助手：开源调研与演进路线图

> **版本**: v0.1 (2026-08-05)
> **定位**: 基于现有 N39 代码真相（`code-truth-architecture.md`），以「任务外包给 OpenClaw」为核心的后续开发计划
> **基准**: 六层架构 + ToolEngine 55 工具 + OpenClaw Bridge（`openclaw_bridge.js` @ 19876）

---

## 一、开源项目调研结论（2026-08-05 实查）

### 1.1 微软 UFO² / UFO³ — 最值得借鉴的 Windows GUI Agent ⭐

**仓库**: `microsoft/UFO`（9.4k stars，MIT，UFO² 已 LTS）
**核心设计**（arXiv 2504.14603 / 2511.11332）:

| 设计 | 说明 | 对本项目的借鉴 |
|---|---|---|
| **Hybrid GUI + API Actions** | 能点 GUI 控件，也能直接走应用 API，二者按成本/可靠度择优 | 桌宠同理：**本地工具优先**，GUI 自动化才推给 OpenClaw |
| **Speculative Multi-Action** | LLM 一次预测多步动作，批量执行 → **减少 51% LLM 调用** | 直接解决成本问题：桌宠工具循环 10 轮 → 批量化可省一半 token |
| **Visual + UIA 混合控件检测** | 截图视觉 + UIA 树双通道，失败互备 | OpenClaw browser 已有 snapshot(ARIA)+screenshot 双通道，**不用自研** |
| **Knowledge Substrate** | RAG 存储文档/demo/**执行轨迹**，供 Agent 参考 | 桌宠闭环已有 MotionMemory，可扩展为「任务执行轨迹库」 |
| **HostAgent + AppAgent 双层** | 总控 Agent 分派 → 各应用 Agent 执行 | 映射到本项目 = 桌宠(ChatManager) → OpenClaw(执行) |

> UFO² 的结论：**GUI 自动化是重活，微软都做成了独立框架**；桌宠不该自研 `SendInput`，应把这类任务交给已具备 browser 工具的 OpenClaw。

### 1.2 OpenClaw（本项目已集成）— 官方源码确认的现成能力 ⭐

**仓库**: `openclaw/openclaw`。从 `docs/gateway/config-tools.md` + `extensions/browser/` 源码核实：

| 内置工具 | 能力 | 当前桥接是否暴露 |
|---|---|---|
| `browser` | snapshot/act(click/type/fill/hover/drag/press)/screenshot/navigate，支持 **`profile="user"` 附加用户真实浏览器**（带登录态） | ❌ 未暴露 |
| `cron` | 定时任务调度 | ❌ 未暴露 |
| `computer` / `nodes` | 系统级操作 | ❌ 未暴露 |
| `exec` / `process` | 命令/进程执行（有审批流 `permissions_respond`） | ❌ 未暴露 |
| `tts` / `image_generate` / `music_generate` / `video_generate` | 媒体生成 | ❌ 未暴露 |
| `memory` / `ask_user` / `agents` | 记忆、问用户、子代理 | ❌ 未暴露 |
| `bundle-mcp` | 挂载任意 MCP 服务器 | ❌ 未暴露 |

> **结论**：OpenClaw 是一个完整的「执行型 Agent」，当前桥接只用了 `chat.send` 一句话问答（约 2% 能力）。**正确姿势 = 把它作为桌宠的「手」**，通过 `/task` 通用端点把浏览器操作、定时任务、多步研究外包出去。

### 1.3 AutoGPT — 平台化与工作流可视化的借鉴

**仓库**: `Significant-Gravitas/AutoGPT`（185k stars）
- **AutoPilot**：自然语言 → 自动构建 agent（可借鉴：桌宠收到复杂指令自动转 `openclaw_task`）
- **可视化 Build 画布**：拖拽 block 组成工作流（可借鉴：`compile_latex` 的分块流程已类似，未来任务模板可可视化）
- **调度触发**：on demand / schedule / trigger（对应本项目 P2 定时动作框架）
- 45+ 平台集成（对应本项目 ReminderAcademicTools 学业集成，方向一致）

### 1.4 pixi-live2d-display — 桌宠本体的交互借鉴

**仓库**: `guansss/pixi-live2d-display`（1.5k stars，640 dependents）
- **分区 hit-testing**：`model.on('hit', areas => ...)` 身体分区点击 → 本项目 `DragHandler` 已实现类似（头/身/腿）
- **Enhanced motion reserving**：动作排队逻辑比官方框架好 → 本项目 `MotionAgent` 锁序修复（N39 BUG-2）思路一致
- **结论**：桌宠本体交互已不落后，此项目仅作对照参考

### 1.5 Open Interpreter — 反面参考

- 代码执行型 OS Agent，但其「让 LLM 写任意 Python 操作电脑」模型**不适合直接引入**：
  - 本项目 `run_command` 刻意白名单化（ToolHelpers.cs 有安全注释）——**该约束应保持**
  - 其 sandbox/审批思路可借鉴（对应 OpenClaw 的 `permissions_respond` 审批流，桥接层应透传）

### 1.6 调研总纲

| 可借鉴点 | 来源 | 落点 |
|---|---|---|
| 任务外包给现成执行 Agent（不重造轮子） | UFO² / OpenClaw | 桥接层 `/task` 泛化 |
| Speculative Multi-Action 省 51% LLM 调用 | UFO² | ChatManager 工具循环批量化 |
| 本地模板/工具优先，API/GUI 兜底 | UFO² Hybrid | 任务路由策略 |
| cron 定时 + 审批流 | OpenClaw | P2 定时动作框架 |
| 执行轨迹 RAG（Knowledge Substrate） | UFO² | 任务记忆库（远期） |
| 可视化工作流模板 | AutoGPT | 远期可选 |

---

## 二、目标架构：桌宠当「脸和嘴」，OpenClaw 当「手」

```
┌────────────── 桌宠（Unity, 表现/人格/感知）─────────────────┐
│  ChatManager(意图过滤→工具循环)                             │
│   ├─ 本地工具（55个: 文件/音量/提醒/动作/截图…）<1s 即时     │
│   └─ openclaw_task ★新增 ── 判定"重活"→外包                 │
│  感知: 前台窗口/浏览器标签/活动/天气/性能 (演出的素材)        │
│  人格/记忆/表情/动作/气泡 (存在的意义)                       │
└──────────────────────────┬─────────────────────────────────┘
                           │ HTTP :19876 (x-bridge-token)
┌──────────────────────────▼─────────────────────────────────┐
│  openclaw_bridge.js (桥, 泛化为任务网关)                     │
│   /search (现状)  /compile_latex (现状)                     │
│   /task ★提交 /task/{id} ★轮询 /task/{id}/cancel ★取消      │
│   串行锁保留(同一时刻一个在途任务) → 队列化                   │
└──────────────────────────┬─────────────────────────────────┘
                           │ WebSocket Gateway
┌──────────────────────────▼─────────────────────────────────┐
│  OpenClaw (执行型 Agent)                                    │
│   browser(点/填/截图/profile=user) · cron · exec            │
│   memory · MCP 服务器 · 子代理 · 审批流                      │
└────────────────────────────────────────────────────────────┘
```

### 任务路由判定（ChatManager 内新增）

```
用户请求 → 本地意图过滤
  ├─ chat/emotion          → 纯对话，不发工具
  ├─ 本地工具能覆盖(<1s)    → 走本地（音量/文件/提醒/打开应用…）
  ├─ 复杂/多步/跨应用/浏览器 → openclaw_task 外包
  │     ├─ 需登录态浏览器    → OpenClaw browser profile="user"
  │     ├─ 需审批的命令      → OpenClaw 审批流 → 桌宠 ToolConfirmManager 透传
  │     └─ 长时间任务(>5min) → 提交后转 ReminderManager 异步通知
  └─ 都不行                → 诚实回复"做不到"，不编造
```

---

## 三、分阶段路线图

### Phase 1 — 桥接泛化：把「手」接上（约 1~2 天）

**目标**: 桌宠能把任意任务推给 OpenClaw 执行并拿回结果

| # | 任务 | 改动文件 | 说明 |
|---|---|---|---|
| 1.1 | 桥新增 `/task` POST 端点 | `openclaw_bridge.js` | 请求体 `{task, mode:"agent"|"browser", sessionKey?, timeoutMs?}`；复用 `sendChatAndWait` 串行锁；响应含结构化结果 JSON |
| 1.2 | 桥新增 `/task/{id}` 轮询 + `/task/{id}/cancel` | `openclaw_bridge.js` | 长任务（如多步浏览器流程）提交后立即返回 task_id，后台跑 |
| 1.3 | 结果规范化 | `openclaw_bridge.js` | 复用 `cleanLatexFence` 思路：剥离 gateway 元数据 JSON、错误特征检测 |
| 1.4 | C# 侧扩展 `OpenClawBridge.cs` | `OpenClawBridge.cs` | `SubmitTaskAsync(task, timeoutMs)` / `PollTaskAsync(taskId)` / `CancelTaskAsync`；新增 `IsBusy` 标志 |
| 1.5 | 新工具 `openclaw_task` | `ToolEngine/VisionKnowledgeTools.cs` 或新文件 | 进 knowledge/command 白名单；参数 `{task, timeoutMs?}`；加入 `IntentToolMap` |
| 1.6 | 任务确认 | `ToolConfirmManager.cs` | openclaw_task 中可能含命令/浏览器操作 → 默认需确认（复用现有 6 危险工具确认机制） |
| 1.7 | 健康检查扩展 | `OpenClawBridge.CheckHealthAsync` | 增加 `/task` 可用性探测，`IsAvailable` 语义从"可搜索"升级为"可执行" |

**验收**: 桌宠说「帮我查一下 B 站关注的更新」→ 转发 OpenClaw browser → 返回结果文本在气泡显示；全程有确认、有超时、有错误兜底。

### Phase 2 — 定时动作框架：让桌宠「到时办事」（约 1 天）

**目标**: 提醒升级为「到时执行动作」

| # | 任务 | 改动文件 | 说明 |
|---|---|---|---|
| 2.1 | `Reminder` 模型加 `action` 字段 | `ReminderManager.cs` | `{type:"local_tool"\|"openclaw_task", toolName, args}`；兼容旧数据（无 action 则纯提醒） |
| 2.2 | 到期触发动作 | `ReminderManager.cs` | 现有 5 件事之后：有 action → 调 `ToolCallInvoker` 执行本地工具或 `openclaw_task` |
| 2.3 | 重复规则与动作 | `ReminderManager.cs` | daily/weekly 重复提醒可带动作（如每天 8:00 open_app 网课） |
| 2.4 | 新增 `set_reminder` 参数 | `ReminderAcademicTools.cs` | schema 增加可选 `action` 字段 |
| 2.5 | 参照 OpenClaw `cron` | 设计评审 | 若未来需要更复杂调度，可直接让 OpenClaw cron 承担，桌宠只做"注册" |

**验收**: 「每天 8:00 打开某应用」可配置、到期真实执行、失败有通知。

### Phase 3 — 语音双向：让桌宠「开口说话」（约 1~1.5 天）

**目标**: 打字输入 → 语音输入输出

| # | 任务 | 说明 |
|---|---|---|
| 3.1 | **TTS 输出**（先做，零成本） | Windows 自带 `System.Speech.Synthesis`（C#，无依赖）；或 `edge-tts`（更自然，免费）；接 `ChatBubble` 显示 + 说话 |
| 3.2 | 嘴型同步（可选） | Live2D SDK `CubismAudioMouthInput` 组件已存在但未接线 → 音量驱动 `ParamMouthOpenY` |
| 3.3 | **ASR 输入**（后做） | 方案 A：本地 whisper（`faster-whisper`，CPU 可用）；方案 B：云端（GLM-4V 同源无，需另配）；方案 C：Windows 语音识别 API |
| 3.4 | 唤醒词（远期） | 「符玄」唤醒 → 参考 OpenClaw 语音通道 |

### Phase 4 — 感知与个性化补强（约 1~1.5 天）

| # | 任务 | 改动文件 | 说明 |
|---|---|---|---|
| 4.1 | **剪贴板监听** | 新 `ClipboardWatcher.cs` | `AddClipboardFormatListener` 挂窗口消息；变化 → 写入感知上下文（"用户复制了 X"）|
| 4.2 | **偏好结构化存储** | `PetMemory.cs` | 新增 `preferences[]`（`{key,value,source,confidence}`），system prompt 注入 + `set_preference` 工具 |
| 4.3 | 偏好→行为绑定（可选） | `ChatManager.cs` | 对明确偏好（如"别主动提游戏"）加确定性过滤，不依赖 LLM 心情 |
| 4.4 | 多屏感知（顺手） | `WindowOverlay.cs` | `isMultiMonitor` 真实计算（EnumDisplayMonitors），至少让"当前屏"注入上下文 |

### Phase 5 — 省钱与自学习（持续优化，借鉴 UFO²）

| # | 任务 | 借鉴 | 说明 |
|---|---|---|---|
| 5.1 | **Speculative Multi-Action** | UFO²（-51% LLM 调用） | ChatManager 工具循环：分析"上一轮结果 → 预测接下来 2-3 步工具"一次请求返回，减少往返 |
| 5.2 | **任务执行轨迹库** | UFO² Knowledge Substrate | 把成功/失败的 `openclaw_task` 记录存入 RAG，后续同类任务直接参考（类似 MotionMemory 对动作的机制） |
| 5.3 | 任务模板 | AutoGPT Build | 高频任务（查成绩/看更新/编译论文）预置模板，`openclaw_task` 只传模板名 + 参数 |

---

## 四、安全边界（必须保持）

| 原则 | 说明 |
|---|---|
| `run_command` 白名单**不放开** | ToolHelpers.cs 的安全设计是正确决策；OpenClaw 的 `exec` 走其自身审批流，桥接层**不透传任意命令** |
| 危险动作确认 | `openclaw_task` 中隐含的浏览器提交/命令执行 → 必须走 `ToolConfirmManager`（点击宠物允许 / ESC 拒绝 / 60s 超时拒绝） |
| 串行锁保留 | 桥 `requestChain` 同一时刻一个在途任务；桌宠侧 `IsBusy` 时排队或拒绝 |
| 令牌 | `BRIDGE_TOKEN` / `GATEWAY_TOKEN` 环境变量，不留默认值（历史已轮换过） |
| 登录态浏览器 | `profile="user"` 操作需用户明示同意（高风险能力，默认关） |

---

## 五、工作量与依赖总览

| Phase | 内容 | 估时 | 依赖 |
|---|---|---|---|
| P1 | 桥接泛化 `/task` | 1~2 天 | OpenClaw 运行中 |
| P2 | 定时动作框架 | 1 天 | P1（动作可能外包） |
| P3 | 语音双向 | 1~1.5 天 | 无 |
| P4 | 剪贴板/偏好/多屏 | 1~1.5 天 | 无 |
| P5 | 省钱与轨迹库 | 持续 | P1 |

**推荐顺序**: P1 → P2 → P3.1(TTS) → P4 → P3.3(ASR) → P5

---

## 六、风险与决策点

| 风险 | 应对 |
|---|---|
| OpenClaw 任务质量不可控 | 结果走 `cleanLatexFence` 式校验；失败自动降级为本地兜底回复 |
| 长任务占住串行锁 | `/task/{id}` 轮询分离；队列化；用户可取消 |
| token 成本上升 | P5.1 批量化；任务模板；只对高价值任务开放 |
| `profile="user"` 误操作 | 默认关闭，需显式开启 + 每次确认 |
| 语音 ASR 中文识别率 | 优先本地 whisper（中文模型），再评估云端 |
