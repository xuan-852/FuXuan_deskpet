# 桌宠 Token 消耗优化计划

> **版本**: v1.0 (2026-08-07)
> **背景**: 2026-08-07 token 审计发现——烧钱主因不是调用频率，而是**每次请求反复重发的固定载荷**（system prompt 4621 tokens / 全量 55 工具定义 / 60 条历史 / MotionTranslator 全量 230 参数 body schema），且 DeepSeek Context Caching 因时间戳插在 system prompt 开头而**永远无法命中**（全价 ¥1/M 付费）。
> **基准数据**（Player.log 134.5 万行）: 翻译成功 47 / 翻译失败 40（全为 30s 超时）/ 本地模板命中 134 / 决策 268（本地免费）/ GLM 镜鉴 182（免费）/ 问候生成 15 / 天气 1 / 反思 0

---

## 一、核心发现（审计结论）

| # | 问题 | 位置 | 影响 |
|---|---|---|---|
| 1 | `{current_time}` 替换发生在 system prompt **开头**，前缀每分变化 → DeepSeek 缓存永远 miss | `ChatManager.cs:98` | 主对话输入全价 ¥1/M，本可 ¥0.02/M（50x 差价） |
| 2 | MotionTranslator body schema 全量 230 参数（排除微动后 ~169 条），每条带范围/中文名/视觉描述 | `MotionTranslator.BuildBodySchema` | 请求体 ~16k tokens/次 |
| 3 | MotionTranslator **无 max_tokens** + 30s 超时 | `BuildRequestBody` / `TIMEOUT` | 40 次超时 = 64 万 tokens 白烧（输入已计费、动作失败） |
| 4 | 工具回环后续轮次重发**全量 55 工具定义** | `ChatManager` 工具循环 | 多轮对话每轮重复数千 tokens |
| 5 | 历史 60 条无差别全塞，无 token 预算 | `ChatManager` 历史构建 | 对话中段膨胀 |
| 6 | 无缓存命中率观测 | 所有 DeepSeek 调用点 | 无法量化优化效果 |

## 二、优化方案（四层，综合开源实践）

```
┌─ 第 1 层：缓存层 ──────── DeepSeek Context Caching（零成本）
│    固定前缀命中 → 输入 ¥1/M → ¥0.02/M（50x）
├─ 第 2 层：载荷瘦身层 ────── LLMLingua "只留关键信息" 思路
│    每次请求只带该带的 → 体积 -60~80%
├─ 第 3 层：请求架构层 ────── UFO² Speculative Multi-Action（-51% 调用）
│    少请求 → 每轮省固定成本
└─ 第 4 层：路由层 ────────── LettA/MemGPT 分层记忆 + 本地免费优先
    历史按 token 预算 + 旧消息本地摘要
```

## 三、任务清单（按 ROI 排序）

### 第一批（立竿见影，改动集中 2 个文件）

| # | 任务 | 改动文件 | 说明 | 预期收益 |
|---|---|---|---|---|
| **T1** | 时间戳挪到 system prompt 尾部 | `ChatManager.cs` | 静态模板保持前缀不变 → 触发 DeepSeek 缓存命中 | 主对话输入 **50x 降价** |
| **T2** | body schema 按动作描述裁剪部位 | `MotionTranslator.cs` | 按描述关键词只发相关部位子集（如"挥手"→只发 arm/hand/finger） | 请求体 -70%（~16k→~5k） |
| **T3** | 加 `max_tokens:1000` + 超时 30→60s | `MotionTranslator.cs` | 输出可控、减少超时 | 消灭 40 次超时白烧 |
| **T8** | `usage.prompt_cache_hit_tokens` 打日志 | 所有 DeepSeek 调用点 | 解析响应 usage 字段记录命中 | 可观测缓存命中率，验证 T1 |

### ✅ 第一批完成情况（2026-08-07 已提交 `a78e62c` + `52f9cec`）

| # | 结果 | 实测数据（Player.log） |
|---|---|---|
| T1 | ✅ 生效 | 主聊天缓存命中率 23.9%→33.9%→**98.6%**→98.5% |
| T2 | ✅ 生效 | schema 2543~5435 字符（原 ~13k tokens，降 60-80%）；翻译成功 |
| T3 | ✅ 生效 | 无超时失败；**追加修复**：deepseek-v4-flash 是推理模型，`thinking` 默认开启会占满 max_tokens 导致 `content=""` → MotionTranslator 请求体显式 `"thinking":{"type":"disabled"}` + `max_tokens:1200`（`52f9cec`），翻译全部恢复，completion 394-560 |
| T8 | ✅ 生效 | usage 日志正常输出命中率 |

### 第二批（结构性，本周内）

| # | 任务 | 改动文件 | 说明 | 预期收益 |
|---|---|---|---|---|
| **T4** | 工具回环只带相关工具子集 | `ChatManager.cs` | 后续轮次按已用工具+候选，不再全量 55 | 多轮对话 -60% |
| **T5** | 历史按 token 预算裁剪 + Ollama 摘要 | `ChatManager.cs` | 旧消息先本地摘要再入上下文 | 对话中段收敛 |
| **T7** | Speculative Multi-Action 批量化 | `ChatManager.cs` | 一次预测 2-3 步工具调用 | 工具循环 -51% |
| **T6** | 固定段 LLMLingua 离线压缩（兜底） | 静态 prompt | 视第一批效果再定 | 4621→~1500 |

### ✅ 第二批完成情况（2026-08-07 已提交 `bad7487` + `5f0a048` + `77645d4`）

| # | 结果 | 说明 |
|---|---|---|
| T4 | ✅ 生效 | `BuildToolSubsetForRound()`：首轮有意图→意图候选（空=纯对话）；首轮无意图→全量；后续回环→已用工具∪意图候选∪CoreToolSubset（play_action/set_expression/stop_action/generate_motion/get_system_info/get_mouse_pos），只保留 `ToolRegistry.HasTool` 存在项 |
| T5 | ✅ 生效 | `TrimHistory()` 双策略：①60 条上限 ②`HISTORY_CHAR_BUDGET=15000` 字符预算，cutEnd 向前对齐最近 user 消息（防切断 tool_calls↔tool 配对→API 400）；被裁旧史取最近 8 条 user 消息经 Ollama `SummarizeConversation` 摘要后以【旧事纪要】system 消息注入 |
| T7 | ✅ 生效 | `InjectMultiActionCapability()` 注入 system prompt（独立子任务一次并行返回多个 tool_call）；接收端 `ApiClient` 早已支持多 index 累积（`toolCallIndex` 补空累加器），`ChatManager.DoToolLoop` 的 `foreach` 逐个执行并按 `tool_call_id` 记历史 — 全链路已通 |
| T6 | ✅ 生效 | `SystemPrompt.txt` 5012→2972 字符（-41%）：删【当前时刻】重复段、【经典台词参考】、【闭环演武】重复段（该段「勿提及评分」铁则已并入 `ChatManager.InjectClosedLoopCapability()` 代码版）；压缩性格/风格/须知/铁则修辞；**所有工具表与 ⚠️ 硬性铁则原样保留** |

### 第二批实测（Player.log 观测要点）

- T4：回环轮请求体 tools 数组应从 55 → 个位数（已用+候选+核心 6）
- T5：对话 15k 字符后 `_historySummary` 非空，请求体出现【旧事纪要】system 消息
- T7：多独立子任务时单次响应含 ≥2 个 `tool_calls`（`⚡ 施法: xxx` 连续打出），工具轮次下降

## 四、验证方法

1. **构建**: `build.ps1`（或 build-current 任务）重建桌宠
2. **重启**: 带环境变量 `DESKTOP_TOKEN` / `BRIDGE_TOKEN` 启动新 exe
3. **观测**: 跑几次对话 + 动作后，查 Player.log:
   - `prompt_cache_hit_tokens` > 0 且占比高 → T1 生效
   - `[MotionTranslator] API 请求失败` 不再出现 → T2/T3 生效
   - 请求体大小日志（T2 加打点）确认体积下降

## 五、风险与回滚

| 风险 | 应对 |
|---|---|
| T2 部位裁剪误伤动作质量 | 保留"不匹配关键词则发全量"兜底分支；GLM 镜鉴验证仍兜底 |
| 缓存命中率不及预期 | 日志 T8 量化后调整；DeepSeek 缓存是 best-effort |
| 改动破坏现有行为 | 每步改动前 git 提交存档；可随时回滚 |

## 六、与 roadmap 的关系

本计划是 `desktop-assistant-roadmap.md` Phase 5（省钱与自学习）的落地子集：
- T7 ↔ roadmap 5.1 Speculative Multi-Action
- T5 记忆摘要 ↔ roadmap 5.2 任务执行轨迹库（思路一致）
- 建议优化完成后将本节合并回 roadmap Phase 5 避免分叉
