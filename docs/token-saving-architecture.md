# 省 Token 基础架构 — 请求分级、上下文预算与成本闸门

> **文档作用**：定义符玄桌宠后续 Token 成本优化的基础架构。本文是设计基线，不把尚未验证的节省比例写成事实；改动 `ApiClient`、`ChatManager`、本地模型路由、后台主动行为或用量统计前应先阅读。
> **当前状态**：v0.4（2026-08-18）。请求预算、动态上下文分层预算、工具结果回填压缩和本地/模板/云端质量遥测均已实现并通过 99/99 EditMode 测试；后续根据运行数据推进质量闸门。
> **关联文档**：[`docs/token-cost-testing.md`](token-cost-testing.md)、[`docs/modules/ai-chat-system.md`](modules/ai-chat-system.md)、[`docs/development-standards.md`](development-standards.md)。

---

## 一、目标与边界

### 1.1 目标

1. 降低真正发往 DeepSeek / GLM 的请求次数，优先消除后台重复调用和失败重试。
2. 保持固定 SystemPrompt 前缀稳定，保护 DeepSeek 上下文缓存命中率。
3. 让每次云端消耗可按 `source` 统计、限流和解释。
4. 所有成本控制都可以在测试模式下本地验证，测试不得触发真实云端。

### 1.2 非目标

- 不通过粗暴缩短角色设定破坏角色行为或缓存前缀。
- 不给正常用户聊天设置不可解释的硬性拒绝。
- 不把本地 Ollama 请求算成云端成本，也不限制本地模型的正常使用。
- 不在没有 `usage_log.jsonl` 数据支撑时重构整个上下文协议。

---

## 二、总体架构

```text
用户输入 / 定时触发 / 动作验证
              │
              ▼
       Local Request Router
       本地意图与来源分类
       本地工具规划（仅相关小目录）
              │
              ▼
       Token Budget Manager
       频率、冷却、来源预算
              │ 允许
              ▼
       Context Materializer
       固定前缀 + 按需上下文 + 动态尾部
              │
              ▼
       ApiClient / 直连云端调用
              │
              ├─ usage_log.jsonl（source / prompt / hit / completion / cost）
              └─ 本地摘要、任务轨迹、缓存结果
```

### 2.1 请求来源分级

| 来源 | 典型调用 | 第一阶段策略 |
|---|---|---|
| `chat` | 用户主动聊天、工具回环 | 只观测，不做硬限流 |
| `idle` | 闲话、主动问候 | 4 次/小时，最短冷却 10 分钟 |
| `reflect` | 对话后记忆反思 | 2 次/小时，最短冷却 20 分钟 |
| `weather` | 天气语录云端回退 | 4 次/小时，最短冷却 10 分钟 |
| `motion` | 动作翻译云端回退 | 12 次/小时，最短冷却 2 分钟 |
| `glm` | 视觉验证/动作评分 | 12 次/小时，最短冷却 5 分钟 |
| `server` | 服务端推送触发 | 后续接入，先保留统计 |
| `local` | Ollama 本地调用 | 不进入云端预算 |

普通 `chat` 不限流，但仍必须记录真实用量；后台来源被拒绝时返回本地可识别的成本闸门错误，禁止自动重试。

### 2.2 上下文分层原则

请求上下文按以下顺序组织：

1. 固定角色与协议前缀：保持稳定，不放当前时间、活动状态等动态数据。
2. 稳定的长期摘要：人格、偏好、经过压缩的记忆。
3. 按意图加载内容：知识库、动作经验、任务轨迹只在相关请求中注入。
4. 当前请求工具 Schema：只发送意图候选工具，不发送无关工具。
5. 动态尾部：当前时间、窗口、活动、剪贴板等实时信息。

动态内容不得移动到固定前缀之前。任何上下文裁剪都必须保持 `assistant tool_calls` 与对应 `tool` 消息成对。

### 2.3 工具结果原则

原始结果保存在本地，模型只接收摘要和引用：

```text
原始网页 / 文件 / 命令输出 → 本地保存
                         → 摘要、关键字段、引用 ID → 回填模型
```

后续可按需读取引用 ID，避免把完整网页、文件列表或任务轨迹重复塞回主会话。

---

## 三、第一阶段实现方案（v0.1）

### 3.1 `TokenBudgetManager`

位置：`Assets/Scripts/TokenBudgetManager.cs`

- 纯内存静态组件，不写生产文件，不改变记忆数据。
- 使用 UTC 时间和滑动窗口记录已放行请求。
- 记录来源的调用次数与最近放行时间。
- `chat`、未知来源和 `local` 默认只观测，不拒绝。
- 后台来源按本节 2.1 的默认策略限流。
- 被拒绝时调用方收到明确错误，并且不进入网络请求、不触发自动重试。
- 提供带显式时间参数的 API，EditMode 测试不需要等待真实时间。

### 3.2 接入点

已接入：

- `ApiClient.PostRequest` / `ApiClient.StreamRequest`：覆盖 `idle`、`reflect` 等统一客户端调用。
- `TimeWeatherController` 的 DeepSeek 回退：覆盖绕过 `ApiClient` 的天气直连。
- `ChatManager` 的反思请求显式标记 `reflect`。
- `MotionTranslator` 的 DeepSeek 回退：覆盖动作翻译直连。
- `DualModelValidator`、`VisionMotionVerifier` 和 `MotionCoroutineTools`：覆盖 GLM 评分、视觉描述、内观和动作自评直连。

所有直连点都在真正创建网络请求前获取预算；余额 fallback 的每次模型请求分别计数，预算拒绝不会继续尝试另一个模型。

### 3.3 失败语义

```text
预算允许 → 发起云端请求 → 记录 usage
预算拒绝 → 本地 warning + onError → 不发请求 → 不自动重试
```

预算拒绝不是网络错误，不应触发 `ChatManager.ShouldRetry`。

### 3.4 `PromptContextBudget`

位置：`Assets/Scripts/PromptContextBudget.cs`

- 对长期记忆、知识库、活动摘要、多窗口、浏览器标签、身体参数、动作记忆、剪贴板和任务轨迹分别设置字符上限。
- 超限时保留头部与尾部，中间插入明确的裁剪标记。
- 只裁剪对应上下文段，不改变固定 Prompt 前缀、段落顺序和【当前时刻】动态尾部。
- 提供纯函数式文本接口，测试不依赖 Unity 场景或网络。

### 3.5 `ToolResultBudget`

位置：`Assets/Scripts/ToolResultBudget.cs`

- 工具执行结果和 UI 展示保持原文。
- 只有写入 `ChatManager` 历史、下一轮将回填云端的副本按工具类型压缩。
- 文件读取、搜索、OpenClaw 任务、视觉结果使用较高预算；普通工具使用默认预算。
- 当前策略是头尾保留，后续再增加本地摘要或引用 ID，不在本阶段增加额外 LLM 调用。

### 3.6 `QualityTelemetry`

位置：`Assets/Scripts/QualityTelemetry.cs`，文件为 `DataRoot/quality_log.jsonl`。

每次记录只包含聚合指标，不写用户原文、完整回复、动作描述、密钥或参数快照：

| 字段 | 含义 |
|---|---|
| `task` | `chat` / `motion_decision` / `motion_translation` / `motion_validation` |
| `src` | `local` / `template` / `cloud` / `fallback` |
| `model` | 实际模型或 `rules` |
| `ok` / `accepted` | 请求/解析是否成功，以及结果是否被采用 |
| `parse` / `safe` | 结构解析和安全校验结果 |
| `score` | GLM 动作评分；非评分事件为 `-1` |
| `latency_ms` | 端到端耗时 |
| `reason` | 固定短标签，如 `local_parse_ok`、`cloud_error_fallback` |

统计命令：

```powershell
node scripts/log-analysis/summarize_quality.cjs D:\DesktopPetData
```

### 3.7 本地工具路由（2026-08-22）

本地聊天新增 `LocalToolRouter`，解决“本地模型只能说、不能做”的架构缺口，同时不把云端的完整工具协议复制到每次本地聊天：

1. `qwen2.5:3b` 先做意图分类；普通闲聊不触发工具规划。
2. 根据 `command` / `knowledge` / `operation` 只生成当前意图的小型工具目录。
3. `qwen2.5:3b` 输出单个 `{action, tool, arguments, reason}` JSON 计划。
4. `ChatManager` 在 Unity 侧校验工具白名单和参数，再复用 `ToolCallInvoker` / `ToolRegistry` 执行。
5. 危险工具不因本地模式绕过 `ToolConfirmManager`；结果只以压缩副本回填给 `qwen3:8b`。

该链路的 Token 控制点是“少量本地规划 schema + 一次工具结果回注”，而不是将 65 个完整 schema 塞入聊天 Prompt；
工具原始结果仍由 UI 和本地日志保留，模型只接收 `ToolResultBudget.Compact` 后的结果。

---

## 四、后续阶段

### v0.2：上下文预算器（已完成，2026-08-18）

- 已完成各上下文段独立字符预算和确定性裁剪。
- 当前仍保持所有原有上下文段的加载顺序；按意图懒加载属于后续行为优化。

### v0.3：结果压缩器（已完成，2026-08-18）

- 已完成工具结果按类型限长并仅压缩历史回填副本。
- OpenClaw 的完整任务轨迹仍由 `task_trajectories.json` 保存，主会话只回填预算内摘要。

### v0.3.1：已知限制

- 当前工具结果是头尾裁剪，不是真正的结构化摘要；复杂结果的引用 ID 机制尚未实现。
- 上下文按意图懒加载尚未实现，避免改变已有角色与工具行为。

### v0.4：质量遥测（已完成，2026-08-18）

- 已记录本地、模板、云端和回退来源。
- 已记录聊天回退、动作 JSON 解析、关键帧数量、耗时和 GLM 评分。
- 遥测与 `usage_log.jsonl` 分离：前者回答“质量如何”，后者回答“云端花了多少 Token”。
- 后续只有在真实运行样本足够后，才根据通过率和回退率调整质量闸门。

### v0.5：任务会话隔离与复用

- 复杂任务交给 OpenClaw 独立会话。
- 高频任务优先使用 `TaskTemplateManager`。
- 成功轨迹只注入相似任务，不注入全部历史。

### v0.6：成本观测闭环

- 按 source、模型、命中率、重试次数统计成本。
- 生产环境连续运行后再调整预算值。
- 任何预算变更必须有 `usage_log.jsonl` 数据和回归测试支撑。

### v0.7：本地工具执行（已实现，2026-08-22）

- 本地模型可通过 JSON 计划调用常用 ToolEngine 工具，危险操作沿用统一确认流程。
- 普通闲聊不额外调用规划模型；本地工具规划使用轻量模型，最终措辞使用聊天质量模型。
- 已完成只读 `get_system_info` 隔离实测，测试数据目录和生产忆境均未污染。

---

## 五、已验证结果与验证标准

- 2026-08-18：脚本编译无新增错误；EditMode **99/99** 通过，其中 `QualityTelemetryTests` 3/3、`TokenBudgetManagerTests` 8/8、`PromptContextBudgetTests` 5/5、`ToolResultBudgetTests` 5/5；新增 `--cloud-baseline` 纯云端质量基线与 `case_id` 配对遥测。
- `TokenBudgetManager` EditMode 测试覆盖：窗口淘汰、最短冷却、最大次数、来源默认策略、拒绝不消耗配额和不可重试标记。
- `PromptContextBudget` / `ToolResultBudget` 测试覆盖：不超限原样、头尾保留、极小预算、工具类型预算和空输入安全性。
- 测试运行使用 `FU_XUAN_DATA` 隔离目录并启用 `.test_mode`。
- 隔离测试的 `usage_log.jsonl` 不得出现云端来源记录。
- 改 C# 后执行 `build.ps1 -Quick`；涉及逻辑时执行 `build.ps1 -RunTests`。
- 没有用户明确确认，不执行放开测试模式云端拦截的真实请求。

## 六、注意事项

1. 预算闸门是成本保护，不是安全审批；危险工具仍必须走 `ToolConfirmManager`。
2. 不得用 `DateTime.Now` 做窗口判断，统一使用 UTC，避免夏令时和系统时钟问题。
3. 不得把预算拒绝包装成可重试网络错误，否则会形成“省钱逻辑导致重复烧钱”。
4. 文档中的节省比例只能来自真实 `usage_log.jsonl`，不能用估算替代实测。
