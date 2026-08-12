# AI 对话系统 — ChatManager 与 Token 优化

> **文档作用**: 本模块文档描述桌宠「AI 对话」子系统的**代码真相**——ChatManager 对话循环、ApiClient 流式请求、LocalLLMAgentService 本地离线能力、言出法随标记，以及 2026-08-07 的 Token 消耗优化（T1-T8）完整历史。改对话/意图过滤/上下文注入/Token 开销相关代码前必读。
> **基本架构**: 用户输入 → `ChatManager`（10 轮回环 / 意图过滤 / 600s 看门狗）→ `ApiClient`（DeepSeek SSE 流式，Function Calling）→ `ToolEngine/` 插件调度；`LocalLLMAgentService`（Ollama qwen2.5:3b）提供 4 项离线能力兜底；`IdleChatGenerator` + `ProactiveMessageScheduler` 驱动自动闲聊。关键文件：`Assets/Scripts/ChatManager.cs`（1,595 行）、`ApiClient.cs`、`LocalLLMAgentService.cs`、`LocalLLMClient.cs`。
> **开发历史迭代**: N31-N37 建立意图过滤与本地 LLM；N39 修复反思链路与知识库上下文注入；N40（2026-08-07）完成 T1-T8 Token 优化（缓存命中 98.6%、工具子集 55→27、SystemPrompt -41%）；2026-08-08 修复 T4 竞态、新增 `IsTestMode` 防污染。
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
      → 意图过滤 (LocalLLMAgentService.ClassifyIntent, 异步非阻塞)
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

1. 基础人格（符玄人设）
2. `ActivityTracker.GetSummary()` 活动摘要
3. ★ 当前前台窗口（法眼实时观测）
4. ★ 多窗口环境摘要 `GetVisibleWindowsSummary()`
5. ★ 浏览器标签页深度感知 `GetBrowserTabsSummary()`
6. `InjectParameterKnowledge()` → `ParameterKnowledgeProvider.GenerateKnowledgePrompt()`（身体参数知识）
7. `InjectClosedLoopCapability()` → 闭环演武系统说明（GLM-4V 自评、演武心经、30 上限、无望淘汰；「勿提及评分」铁则在此代码版）
8. `MotionMemoryManager.GetFormattedMemories()` 演武心经经验

> ⚠️ 铁则：**动态内容（时间戳等）只允许出现在尾部固定段之后或 prompt 末尾**——T1 把 `{current_time}` 从开头挪到尾部后缓存命中率 23.9%→98.6%（50x 差价：¥1/M → ¥0.02/M）。

### 2.4 言出法随 — 内嵌动作标记（StripAndExecuteActions）

| 格式 | 示例 | 效果 |
|------|------|------|
| `【表情:开心】` | `【表情:开心】今天天气真好` | PlayExpression |
| `【动作:伸懒腰】` | `【动作:伸懒腰】活动一下~` | ForceAction |
| `（自然描述）` | `（害羞捂脸）人家才没有` | TryMatchKnown 模糊匹配 |

### 2.5 本地 LLM 能力（LocalLLMAgentService，Ollama qwen2.5:3b @ 127.0.0.1:11434）

| 能力 | 方法 | 作用 |
|------|------|------|
| 意图/情绪分类 | `ClassifyIntent` | 异步非阻塞，写 `_lastIntent` 供工具过滤 |
| 兜底回复 | `GenerateFallbackReply` | API 挂时的兜底 |
| 对话摘要 | `SummarizeConversation` | T5 旧史压缩【旧事纪要】 |
| 记忆提取 | `ExtractMemory` | 记忆提炼 |

> ★ `LocalLLMClient.cs` 是**手写 JSON 构造**（非 Newtonsoft/JsonUtility），重试 2 次。

### 2.6 工具子集（N40 T4，BuildToolSubsetForRound）

- 首轮**有意图** → 意图候选子集（**27 个**）
- 首轮**无意图** → 纯对话（不发 tools）
- 回环轮 → 已用工具 ∪ 意图候选 ∪ `CoreToolSubset`（play_action/set_expression/stop_action/generate_motion/get_system_info/get_mouse_pos），只保留 `ToolRegistry.HasTool` 存在项
- 竞态防护：`_intentReady` 标志 + 3s 超时兜底全量（首轮等待分类结果，防上轮 command 意图跨消息残留污染）

### 2.7 测试模式（IsTestMode）

存在 `D:\DesktopPetData\.test_mode` 标记文件 → 跳过记忆/人格/反思全部持久化写入。**自动化测试必须开启**，否则污染忆境与人格演化计数。

## 三、开发历史迭代

| 版本 | 日期 | 变更 | 提交 |
|------|------|------|------|
| N31-N37 | — | 意图过滤（IntentToolMap）、本地 LLM 4 能力接入 | — |
| N39 | 2026-08-02 | 反思链路接线（CheckReflection→DoReflection→CommitReflection），删除 `OnReflectRequest` 死回调；知识库上下文 `GetFormattedContext()` 返回缓存真正注入 | — |
| N40 T1 | 2026-08-07 | 时间戳挪 system prompt 尾部 → DeepSeek 缓存命中 | `a78e62c` |
| N40 T2 | 2026-08-07 | MotionTranslator body schema 按描述裁剪部位（~13k→~5k tokens） | `a78e62c` |
| N40 T3 | 2026-08-07 | `max_tokens:1200` + 超时 30→60s；**追加**：显式 `"thinking":{"type":"disabled"}`（deepseek-v4-flash 推理模式吃满 max_tokens 致 `content=""`） | `52f9cec` |
| N40 T8 | 2026-08-07 | `usage.prompt_cache_hit_tokens` 打日志（可观测缓存命中率） | `a78e62c` |
| N40 T4 | 2026-08-07 | 工具回环只带相关工具子集（首轮 55→27，-51%） | `bad7487` |
| N40 T5 | 2026-08-07 | 历史 15000 字符预算 + 被裁旧史 Ollama 摘要注入【旧事纪要】 | `5f0a048` |
| N40 T7 | 2026-08-07 | Speculative Multi-Action（一次预测 2-3 步 tool_call，实测一次响应双工具） | `77645d4` |
| N40 T6 | 2026-08-07 | SystemPrompt.txt 5012→2972 字符（-41%，工具表与硬性铁则原样保留） | `77645d4` |
| N40 | 2026-08-08 | **T4 竞态修复**（`_intentReady` + 3s 兜底）；**新增 IsTestMode** 防测试污染（实测 3 条测试消息后 pet_memory 28 条无新增、人格 totalInteractions 56 无变化） | — |

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
