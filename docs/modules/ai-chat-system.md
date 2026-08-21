# AI 对话系统 — ChatManager 与 Token 优化

> **文档作用**: 本模块文档描述桌宠「AI 对话」子系统的**代码真相**——ChatManager 对话循环、ApiClient 流式请求、LocalLLMAgentService 本地离线能力、言出法随标记，以及 2026-08-07 的 Token 消耗优化（T1-T8）完整历史。改对话/意图过滤/上下文注入/Token 开销相关代码前必读。
> **基本架构**: 用户输入 → `ChatManager`（本地优先 / 10 轮云端回环 / 意图过滤 / 看门狗）；云端路径使用 `ApiClient`（DeepSeek SSE + Function Calling）→ `ToolEngine/` 插件调度；本地路径使用 `LocalToolRouter`（轻量模型 JSON 规划 + 白名单 + 同一 ToolEngine 执行）→ `LocalLLMAgentService` / `qwen3:8b` 生成最终回复。动作/分类/摘要/工具规划使用轻量 `qwen2.5:3b`，聊天单独使用质量优先的 `qwen3:8b`（均可由环境变量覆盖）；云端与本地聊天都经过同一 `PetMemory` 相关性检索，本地只使用更紧凑的忆境预算；`IdleChatGenerator` + `ProactiveMessageScheduler` 驱动自动闲聊。关键文件：`Assets/Scripts/ChatManager.cs`、`LocalToolRouter.cs`、`ApiClient.cs`、`LocalLLMAgentService.cs`、`LocalLLMClient.cs`。
> **开发历史迭代**: N31-N37 建立意图过滤与本地 LLM；N39 修复反思链路与知识库上下文注入；N40（2026-08-07）完成 T1-T8 Token 优化（缓存命中 98.6%、工具子集 55→27、SystemPrompt -41%）；2026-08-08 修复 T4 竞态、新增 `IsTestMode` 防污染；2026-08-12 P4 注入链新增偏好（PreferencesManager）与剪贴板感知（ClipboardMonitor），均置于【当前时刻】之前不破坏上下文缓存前缀；2026-08-12 P5 注入链新增任务轨迹（TaskTrajectoryManager，太卜手札）与任务模板（TaskTemplateManager，太卜阵法图），同样置于【当前时刻】之前；2026-08-18 成本闸门接入全部主要云端直连点，新增 `PromptContextBudget` / `ToolResultBudget` / `QualityTelemetry`，EditMode 99/99 通过。
> **编写注意事项**: ①测试必须开测试模式（`D:\DesktopPetData\.test_mode`）否则污染 pet_memory/pet_personality；②`{current_time}` 等动态内容**必须放 system prompt 尾部**（放开头会摧毁 DeepSeek 缓存命中，全价 ¥1/M）；③deepseek-v4-flash 是推理模型，必须显式 `"thinking":{"type":"disabled"}` + `max_tokens:1200` 否则 `content=""`；④历史裁剪须按字符预算且向前对齐最近 user 消息，防止切断 tool_calls↔tool 配对导致 API 400。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及对话主链路（`ChatManager.cs`）、意图过滤、工具子集、上下文注入、自动闲聊、Token 成本优化的改动。
- **回答的问题**:
  - 对话请求是怎么发出去的？超时/重试规则是什么？
  - 工具回环最多几轮？每轮带哪些工具定义？
  - system prompt 注入了哪些内容？顺序是什么？
  - 本地 Ollama 承担什么角色？挂了会怎样？
  - Token 为什么这么贵？T1-T8 分别做了什么？
- **关联文档**: `code-truth-architecture.md` 三章（AI 核心层真相）｜`modules/tool-engine.md`（工具调度端）｜`modules/memory-personality.md`（记忆/人格注入）｜`modules/action-agent.md`（闭环演武注入）｜`desktop-assistant-roadmap.md` Phase 5（省钱与自学习）

## 二、基本架构

### 2.1 对话主链路

```
用户输入
  → ChatManager.SendMessage (消息入队 _messageQueue，等待时不丢)
    → SendRequestCoroutine
      ├─ 本地模式 → DoOllamaOnlyReply
      │   → qwen2.5:3b 意图分类 / LocalToolRouter JSON 规划
      │   → 白名单筛选 + ToolConfirmManager + ToolEngine 执行
      │   → 工具结果压缩后交给 qwen3:8b 生成角色回复
      └─ 云端模式 → 意图过滤 (LocalLLMAgentService.ClassifyIntent, 异步非阻塞)
          → ApiClient 请求 DeepSeek Chat API (SSE 流式 + Function Calling)
            → 若响应含 tool_calls → DoToolLoop (最多 MAX_TOOL_ROUNDS=10 轮)
            → 工具执行结果回填历史 → 再请求
      → 逐句队列显示 (2.5s/句, SentenceVersionId 支持重播)
  → CheckReflection → DoReflection (DeepSeek 提炼) → CommitReflection (N39 接线)
```

### 2.2 可靠性参数（ChatManager 常量，代码真相）

| 参数 | 值 | 说明 |
|------|-----|------|
| `MAX_TOOL_ROUNDS` | **10** | 工具回环上限（注释"5->10: 复杂任务需多轮"） |
| `MAX_HISTORY_ENTRIES` | **60** | 历史条数上限 |
| `HISTORY_CHAR_BUDGET` | **15000** | 历史字符预算（T5，超预算裁旧 + Ollama 摘要） |
| `REQUEST_TIMEOUT` | **600s** | 看门狗（覆盖 GLM-4V 180s） |
| API 自动重试 | 最多 **3 次** | `ShouldRetry`：400/401/403 **不重试** |
| 句子间隔 | 2.5s/句 | 流式 + 逐句队列 |

### 2.3 系统 Prompt 注入链（BuildSystemPrompt 真实顺序）

1. 基础人格模板（`Resources/SystemPrompt.txt`，兜底"你是符玄…"）
2. `PetMemory.GetFormattedMemories(currentUserQuery)` 长期记忆（云端相关命中最多 3 条，忆境段最多 1400 字符；核心事实最多 3 条）
3. `PersonalityManager.FormatForPrompt()` 人格特质与关系
4. `PreferencesManager.FormatForPrompt()` 主人偏好【本座谨记】（P4.2）
5. `TaskTrajectoryManager.FormatForPrompt()` 任务轨迹【太卜手札】（P5.2，空库返回空串）
6. `TaskTemplateManager.FormatForPrompt()` 任务模板【太卜阵法图】（P5.3，含使用说明）
7. 知识库上下文 `_cachedKnowledgeContext`（藏书阁检索缓存）
8. `ActivityTracker.GetSummary()` 活动摘要
9. ★ 当前前台窗口（法眼实时观测）
10. ★ 多窗口环境摘要 `GetVisibleWindowsSummary()`
11. ★ 浏览器标签页深度感知 `GetBrowserTabsSummary()`
12. `InjectParameterKnowledge()` → 身体参数知识
13. `InjectClosedLoopCapability()` → 闭环演武系统说明
14. `InjectMultiActionCapability()` → 多步并行施法（T7）
15. `MotionMemoryManager.GetFormattedMemories()` 演武心经经验
16. `ClipboardMonitor.GetRecentClipboardSummary()` 剪贴板感知（P4.1，30 分钟时效）
17. 【当前时刻】固定尾部（命中 DeepSeek 上下文缓存）

> ⚠️ 铁则：**动态内容（时间戳等）只允许出现在尾部固定段之后或 prompt 末尾**——T1 把 `{current_time}` 从开头挪到尾部后缓存命中率 23.9%→98.6%（50x 差价：¥1/M → ¥0.02/M）。P4 新增注入（偏好/剪贴板）均置于【当前时刻】之前，不影响静态前缀缓存。

### 2.4 言出法随 — 内嵌动作标记（StripAndExecuteActions）

| 格式 | 示例 | 效果 |
|------|------|------|
| `【表情:开心】` | `【表情:开心】今天天气真好` | PlayExpression |
| `【动作:伸懒腰】` | `【动作:伸懒腰】活动一下~` | ForceAction |
| `（自然描述）` | `（害羞捂脸）人家才没有` | TryMatchKnown 模糊匹配 |

### 2.5 本地 LLM 能力（LocalLLMAgentService，Ollama 分层模型 @ 127.0.0.1:11434）

| 能力 | 方法 | 作用 |
|------|------|------|
| 意图/情绪分类 | `ClassifyIntent` | 异步非阻塞，写 `_lastIntent` 供工具过滤 |
| 本地工具规划 | `PlanLocalTool` | qwen2.5:3b 输出单个 JSON 计划，不直接执行工具 |
| 本地角色回复 | `GenerateFallbackReply` | qwen3:8b 结合忆境和实际工具结果生成最终回复 |
| 对话摘要 | `SummarizeConversation` | T5 旧史压缩【旧事纪要】 |
| 记忆提取 | `ExtractMemory` | 仅对明确长期信号做结构化筛选；importance/confidence/type/原话交叉闸门 |

模型路由：`LocalLLMClient.ModelName` 默认用于动作、意图分类、摘要和记忆提取（`qwen2.5:3b`）；`LocalLLMClient.ChatModelName` 只用于聊天回退（`qwen3:8b`）。这样把质量模型的耗时限制在用户真正等待的聊天请求中，不拖慢动作循环。`FU_XUAN_LOCAL_CHAT_MODEL` 可单独覆盖聊天模型，`FU_XUAN_LOCAL_MODEL` 可同时覆盖两类模型。

> ★ `LocalLLMClient.cs` 是**手写 JSON 构造**（非 Newtonsoft/JsonUtility），重试 2 次。

### 2.6 工具子集（N40 T4，BuildToolSubsetForRound）

- 首轮**有意图** → 意图候选子集（**27 个**）
- 首轮**无意图** → 纯对话（不发 tools）
- 回环轮 → 已用工具 ∪ 意图候选 ∪ `CoreToolSubset`（play_action/set_expression/stop_action/generate_motion/get_system_info/get_mouse_pos），只保留 `ToolRegistry.HasTool` 存在项
- 竞态防护：`_intentReady` 标志 + 3s 超时兜底全量（首轮等待分类结果，防上轮 command 意图跨消息残留污染）

### 2.7 本地工具路由（2026-08-22）

本地 Ollama 的 OpenAI 兼容接口当前不依赖模型原生 `tools` 字段，而采用两阶段本地链路：

```text
用户请求
  → qwen2.5:3b ClassifyIntent
  → LocalToolRouter 按 command/knowledge/operation 选择小型工具目录
  → qwen2.5:3b 输出 {action, tool, arguments, reason}
  → ChatManager 校验工具名、意图白名单和参数 JSON
  → ToolConfirmManager（危险工具）
  → ToolCallInvoker / ToolRegistry 执行
  → ToolResultBudget.Compact
  → qwen3:8b 根据真实结果生成符玄回复
```

- 本地规划器每次只看到当前意图的工具目录，不接收全部 65 个工具，控制上下文和误调用概率。
- `LocalToolRouter` 当前覆盖常用的打开、搜索、读取、查询、办公生成、动作和系统信息工具；
  `run_command`、`openclaw_task` 等危险/高影响工具仍会进入既有确认流程。
- 本地模型只能调用当前目录中的工具；未知工具、未授权工具或格式错误计划会被拒绝，不会直接执行。
- 普通闲聊不会额外调用规划模型；只有意图为 command/knowledge/operation，或消息包含明确操作关键词时才规划。
- 测试模式下可以执行只读本地工具，但所有记忆、人格和反思持久化仍被阻断。

### 2.8 测试模式（IsTestMode）

存在 `D:\DesktopPetData\.test_mode` 标记文件 → 跳过记忆/人格/反思全部持久化写入。**自动化测试必须开启**，否则污染忆境与人格演化计数。

### 2.9 本地聊天质量护栏（2026-08-19～2026-08-21）

本地 Ollama 回退链路不直接复用云端完整 system prompt，而由 `LocalRoleplayPromptBuilder` 生成短角色卡和短句组合约束；同时调用同一 `PetMemory.GetFormattedMemories(userMessage)`，以不超过 700 字符的【相关忆境】背景注入本地 system prompt：

1. 聊天模型使用 `qwen3:8b`，保留最近两轮对话（最多 1600 字符）和当前输入（最多 900 字符），增加连贯性；长期信息由上层摘要/记忆系统承担。
2. 普通回复目标为 3～6 句、90～180 字，详细问题为 5～9 句、180～320 字；每句只表达一个意思，禁止为了凑长度重复。计划类请求必须给具体步骤，明确总时长时各段时间必须加总，详细问题先回答再追问。
3. `LocalContextAnswer` 直接处理时间、天气和符玄偏好类固定问题：时间读 `DateTime.Now`，天气只读 `TimeWeatherController.weatherFetched` 后的数据，未取得天气时明确返回未知，不让模型猜测。
4. `LocalReplyPostProcessor` 在回复入历史、显示和复核前执行零 Token 护栏：去除“将军”、限制“主人”重复、将普通自称“我”改为“本座”，但保留“自我”等固定词；用户明确要求“一句/三句”时只做确定性句数裁剪，不进行第二次模型重写。
5. 质量测试用 `FU_XUAN_LOCAL_PROMPT_VARIANT=micro_v1`，遥测模型名带 `/fu_card_v2` 角色卡版本后缀；`baseline` 和 `card_v1` 保留用于消融对照。
6. 本地和云端都只接收与当前问题相关的忆境；本地忆境段额外使用 `PromptContextBudget.LocalMemoryChars=700`，并明确提示模型记忆可能过时、无关时忽略。

### 2.10 分层符玄角色卡（2026-08-19）

`FuXuanCharacterCard.cs` 将角色卡拆成四层，避免把完整世界观重复塞进每次本地请求：

1. 常驻核心：太卜司身份、直接聪慧且实际关心他人的性格、使用“本座”、对用户使用“你/偶尔主人”、不称“将军”。
2. 按问题选择的示例：计划、情绪支持、一般判断分别使用短示例，帮助低能力模型学习回答结构。
3. 关键词 Lorebook：仅在出现青雀、景元、太卜司/穷观阵、宿命/未来等关键词时注入相关背景，最多两条。
4. 历史末尾护栏：当前问题优先，禁止复制上一轮安慰语/邀约/反问；事实解释、推荐和计划完成后不追加无关追问。

角色事实取自公开剧情与台词资料：符玄是罗浮太卜司之首，擅长推演，强调推演用于看清选择而非用宿命论逃避；青雀、景元关系作为按需背景使用。角色卡采用分层字段的做法，结构参考 SillyTavern V2 的 system prompt、示例消息与可选 character book 思路，不复制第三方完整角色卡。

最终构建后的 `qwen3:8b + micro_v1 + fu_card_v2` 本地 10 条聊天复测：10/10 成功，平均延迟 1017ms，自动质量均分 4.70/5，persona 4.60、relevance 5.00、constraint 5.00；`chat_006` 的“三句话”已被确定性护栏稳定为三句。该批次 `usage_log` 仅有 local 记录，成本 ¥0。

2026-08-19 `qwen2.5:3b` 30 条本地聊天回归：30/30 成功，平均延迟 681ms，平均回复 97.80 字，平均 3.33 句，`将军` 0 次；质量规则裁判 4.13/5。规则裁判会把“今天/幸运日”等语义误判为时间问题，不能替代人工复核。

2026-08-21 质量优先路由已落地：聊天请求使用 `qwen3:8b`、`max_tokens=640`、本地等待上限 75s，并显式关闭默认 thinking 预算；动作/分类/摘要仍使用 `qwen2.5:3b`。本机真实请求实测返回 131 个中文字符、8 个短句、约 1.84s（单次冷/热状态会波动），说明更长回复预算和分层模型路由已生效。

### 2.11 Qwen3 模型切换与推理预算（2026-08-19）

- `FU_XUAN_LOCAL_MODEL`：覆盖动作/分类/摘要等通用本地模型；未设置时保持 `qwen2.5:3b`。
- `FU_XUAN_LOCAL_CHAT_MODEL`：只覆盖聊天模型；未设置时默认使用已安装的 `qwen3:8b`。聊天请求的质量预算与动作请求分离。
- `FU_XUAN_LOCAL_REASONING_EFFORT`：支持 `none/low/medium/high/max`。未设置时，模型名以 `qwen3` 开头则自动发送 Ollama OpenAI 兼容接口的 `reasoning_effort: "none"`；其他模型保持原行为。
- 原因：Ollama `/v1/chat/completions` 对 Qwen3 默认启用 thinking，`max_tokens` 可能被思考过程消耗，导致最终 `message.content` 为空。日常聊天关闭推理，复杂任务可按路由设置 `low/medium/high`。
- `qwen3:8b` 隔离回归（`FU_XUAN_LOCAL_PROMPT_VARIANT=micro_v1`）：30/30 成功，平均 1141ms，P50 1242ms，P95 1842ms，规则质量 4.80/5；单模型驻留约 5.6GB 显存，测试结束已执行 `ollama stop qwen3:8b` 释放显存。
- 与 `qwen2.5:3b` 的同批次基线相比：平均延迟增加约 460ms，规则质量由 4.13 提升至 4.80，适合作为聊天模型候选；动作/摘要等实时能力仍应保留轻量模型。

### 2.12 模型设置页与真实本地样例（2026-08-21）

`RightPanel` 新增独立 `ModelSettings` 子面板：聊天模型可在 `qwen3:8b`、`qwen2.5:3b`、`qwen2.5:1.5b`、`qwen2.5:0.5b` 之间选择，应用后只改变 `LocalLLMClient.ChatModelName`，不改变动作/摘要模型。选择结果写入 `PetConfig.ConfigData.chatLocalModel`，重启后恢复。

模型页右侧展示的是 2026-08-21 使用临时 `FU_XUAN_DATA` + `.test_mode` 真实请求 `qwen3:8b` 得到的样例：

| 案例 | 输入 | 完成耗时 | 回复字数 | 规则评分 |
|---|---|---:|---:|---:|
| `chat_001` | 你好，今天过得怎么样？ | 4.1s | 36 | 4/5 |
| `chat_002` | 你觉得我今天看起来有点累吗？ | 1.4s | 76 | 4/5 |
| `chat_003` | 用简单的话解释什么是缓存。 | 2.0s | 153 | 5/5 |

`qwen3:8b` 的上述结果作为首屏基准样例保存在 `Assets/Scripts/LocalModelDemoData.cs`；切换到其他本地模型后，`RightPanel` 会用同一组问题在运行时重新请求当前模型，并按模型名缓存本次进程内的真实回复、耗时、字数和规则评分。未完成生成前显示“尚未完成本地测评”，不会复用 qwen3 回复冒充其他模型。样例生成只调用本地 Ollama，不写入忆境或聊天历史；本批次仍属于小样本，不能替代后续人工复核。

模型页保留“云端对话 · DeepSeek”选项，但当前仅显示安全占位，不接收或保存明文 API Key，也不伪造云端对照文本。云端选项要在专属 Key 的安全存储、日志脱敏、归属记录和消耗核验完成后再启用。

模型策略不再对所有本地模型使用同一套预算：

| 模型 | 温度 | 最大输出 | 超时 | 历史预算 | 回复取向 |
|---|---:|---:|---:|---:|---|
| `qwen3:8b` | 0.64 | 640 | 75s | 1600 字 | 质量优先，允许完整展开 |
| `qwen2.5:3b` | 0.62 | 576 | 55s | 1400 字 | 均衡，3–5 句、先结论 |
| `qwen2.5:1.5b` | 0.58 | 384 | 45s | 900 字 | 短而完整，2–4 句 |
| `qwen2.5:0.5b` | 0.55 | 256 | 35s | 600 字 | 只答核心，1–3 句 |

策略定义位于 `Assets/Scripts/LocalChatModelProfiles.cs`，由 `LocalLLMAgentService`、`LocalRoleplayPromptBuilder` 和模型设置页样例生成共同使用。2026-08-21 最终隔离实测 `qwen2.5:3b` 三条样例耗时 0.47/0.62/0.48 秒，回复 10/69/40 字，规则评分 3/4/4；这说明“时间换质量”是能力上限和预算控制，不是强制延迟，低能力模型不会因等待更久自动变成 8B。

## 三、开发历史迭代

| 版本 | 日期 | 变更 | 提交 |
|------|------|------|------|
| N31-N37 | — | 意图过滤（IntentToolMap）、本地 LLM 4 能力接入 | — |
| N39 | 2026-08-02 | 反思链路接线（CheckReflection→DoReflection→CommitReflection），删除 `OnReflectRequest` 死回调；知识库上下文 `GetFormattedContext()` 返回缓存真正注入 | — |
| N40 T1 | 2026-08-07 | 时间戳挪 system prompt 尾部 → DeepSeek 缓存命中 | `a78e62c` |
| N40 T2 | 2026-08-07 | MotionTranslator body schema 按描述裁剪部位（~13k→~5k tokens） | `a78e62c` |
| N40 T3 | 2026-08-07 | `max_tokens:1200` + 超时 30→60s；**追加**：显式 `"thinking":{"type":"disabled"}`（deepseek-v4-flash 推理模式吃满 max_tokens 致 `content=""`） | `52f9cec` |
| N40 T8 | 2026-08-07 | `usage.prompt_cache_hit_tokens` 打日志（可观测缓存命中率） | `a78e62c` |
| N43 | 2026-08-15 | **本地模型优先（成本优化）**：动作翻译/闲话问候/天气语录先走本地 Ollama（qwen2.5:3b，免费），失败才回退 DeepSeek——动作翻译占 API 调用 78%（实测 677/869），改造后本地成功率 100%（num_ctx 8192 解决大 schema 截断）；DeepSeek 调用量预计降 90%+（DeepSeek 8-17 起峰谷涨价，峰值输出 ¥27/M，本次优化正当其时） | — |
| N40 T4 | 2026-08-07 | 工具回环只带相关工具子集（首轮 55→27，-51%） | `bad7487` |
| N40 T5 | 2026-08-07 | 历史 15000 字符预算 + 被裁旧史 Ollama 摘要注入【旧事纪要】 | `5f0a048` |
| N40 T7 | 2026-08-07 | Speculative Multi-Action（一次预测 2-3 步 tool_call，实测一次响应双工具） | `77645d4` |
| N40 T6 | 2026-08-07 | SystemPrompt.txt 5012→2972 字符（-41%，工具表与硬性铁则原样保留） | `77645d4` |
| N40 | 2026-08-08 | **T4 竞态修复**（`_intentReady` + 3s 兜底）；**新增 IsTestMode** 防测试污染（实测 3 条测试消息后 pet_memory 28 条无新增、人格 totalInteractions 56 无变化） | — |
| N44 | 2026-08-16 | **测试模式禁云端 + 长效消耗日志**：`ApiClient.BlockCloudInTestMode`（默认跟随 IsTestMode）短路所有云端调用，绕过 ApiClient 的直连方（GLM 视觉×6、DeepSeek 兜底×2）补 `ShouldBlockCloudPublic` 拦截，本地 Ollama 不受影响；新增 `UsageLogger` JSONL 持久化（`DataRoot/usage_log.jsonl`，2MB/2 万行封顶，按 source 记录 chat/motion/idle/weather/reflect/glm/local），面板「来源明细」按源统计——定位 ¥5/天消耗源头（实测 45s 测试运行 usage_log 零云端行，仅 local 免费调用） | `08494dd` `51cbecb` |

### 实测数据（2026-08-08，Player.log）

- 缓存命中率：23.9% → 33.9% → **98.6%** → 98.5%
- 工具子集：`🎯 round=0 意图「command」→ 仅发 27 道术式` / `round=1 → 仅发 31 道术式`
- T5 裁剪：`✂️ 历史超字符预算(15000)，裁剪 6 条旧消息` → `📜 旧史摘要: 搜索今日天气和最近考试成绩...`
- T7 双工具：单次响应 `completion=7902` 返回 2 个并行 tool_call

## 四、编写注意事项

1. **测试必须开测试模式**：建空文件 `D:\DesktopPetData\.test_mode`（防污染 pet_memory/pet_personality），测后删除 + `scripts/openclaw/clean_test_pollution.cjs` 清理（备份→删测试记忆→回退人格计数→重算 familiarity）
2. **动态内容位置铁则**：`{current_time}` 等每分钟变化的占位符**只能放 system prompt 尾部**；放在开头会摧毁 DeepSeek Context Caching（全价 ¥1/M，50x 差价）
3. **deepseek-v4-flash 是推理模型**：所有 DeepSeek 调用点必须显式 `"thinking":{"type":"disabled"}`；不加会占满 max_tokens 导致 `content=""`（曾导致 MotionTranslator 全挂）
4. **历史裁剪安全**：`TrimHistory()` 双策略（60 条上限 + 15000 字符预算），cutEnd 必须向前对齐最近 user 消息——切断 tool_calls↔tool 配对会触发 API 400
5. **意图分类竞态**：首轮工具子集依赖 `_intentReady` + 3s 超时兜底；改动 `DoToolLoop` 时保持该防护，否则上轮意图跨消息残留
6. **日志验证**：改 Token 相关代码后查 Player.log——`prompt_cache_hit_tokens` 占比高 = T1 生效；`[MotionTranslator] API 请求失败` 不出现 = T2/T3 生效
7. **构建验证**：`.\build.ps1 -Quick`（C# 编译）；重启带 `DESKTOP_TOKEN` / `BRIDGE_TOKEN` 环境变量
8. **本地 LLM 客户端**：`LocalLLMClient.cs` 是手写 JSON 构造，改字段时同步检查所有调用点，无强类型保障
9. **质量遥测**：`QualityTelemetry` 将聊天按 `local/cloud` 记录成功、采用、耗时和回退原因，写入 `DataRoot/quality_log.jsonl`，不包含用户原文。全 Ollama 模式现在支持受白名单约束的本地工具规划与执行；工具规划、执行和最终回复仍分别记录为本地调用。统计命令：`node scripts/log-analysis/summarize_quality.cjs D:\DesktopPetData`。
10. **质量对照**：`--cloud-baseline` 强制纯云端聊天并禁用云端失败后的本地回退；`@@case:<id>` 可在隔离目录给后续遥测标记案例编号。两组日志按 `task + case_id` 配对，使用 `docs/quality-comparison-test-guide.md` 和 `scripts/log-analysis/compare_quality.cjs`，不要用默认混合模式冒充纯云端基线。
