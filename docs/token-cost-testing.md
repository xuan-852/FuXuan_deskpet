# Token 消耗与测试指南（AI 第一优先阅读）

> **测试脚本注意**：`scripts/test/runtime_smoke.cjs` 未显式设置 `PLAYER_LOG` 时，读取测试隔离目录 `<FU_XUAN_TEST_DATA>/logs/player_log.txt`（应用日志镜像），避免误读旧的默认 Unity `Player.log` 导致冒烟测试假失败。

> **文档作用**: 让 AI **第一时间**了解三件事——① 常规运行（生产）与测试模式在 Token 消耗上的**本质区别**；② 测试时关于消耗的**铁律与常见误判**；③ 当前**未解决的痛点**。任何涉及"改测试逻辑 / 改云端调用 / 排查烧钱 / 写测试"的工作，先读本文再动手。
> **基本架构**: 观测链路 = `ApiClient`（云端调用点）→ `UsageStats`（内存，面板实时）→ `UsageLogger`（JSONL 落盘，跨重启）；拦截开关 = `ApiClient.BlockCloudInTestMode`（默认跟随测试模式）。
> **开发历史迭代**: 2026-08-15 本地模型优先（N43）；2026-08-16 测试模式禁云端（N44，`51cbecb`）+ UsageLogger 持久化（`08494dd`）。
> **编写注意事项**: 本文记录的是**已验证的代码真相** + 痛点现状；痛点状态变化时（如 ¥5/天来源已定位）必须同步更新第五节。

---

## 一、消耗现状速览（2026-08-16 实测）

| 项 | 值 | 备注 |
|----|----|----|
| 云端主模型 | DeepSeek `deepseek-v4-flash` | 按量付费 |
| 本地模型 | Ollama `qwen2.5:3b` | 免费，本地优先 |
| 视觉模型 | GLM-4V 系列 | 动作验证/法眼/内观 |
| 价格 | 输入 miss ¥2/M、命中 ¥0.5/M、输出 ¥3/M | DeepSeek 非高峰；**2026-08-17 起峰谷涨价，峰值输出最高 ¥27/M** |
| 缓存命中率 | 目标 98.6% | T1 时间戳挪尾部实现；动态内容放尾部是铁律 |
| 一次普通问候 | ≈11k 输入 tokens | 实测 `chat: prompt=11000, hit=2560, comp=166, ¥0.0187` |
| 观测手段 | `usage_log.jsonl` + 面板「消耗」+ Player.log | 详见第四节 |

## 二、生产 vs 测试模式 —— 本质区别（速查表）

| 维度 | 生产（正常跑，`D:\DesktopPetData`） | 测试（`FU_XUAN_DATA` 隔离 + `.test_mode`） |
|------|------------------------------------|--------------------------------------------|
| 数据目录 | `D:\DesktopPetData` | 隔离目录（smoke 用 `%TEMP%\fuxuan_smoke_test`，手动指定 `FU_XUAN_DATA`） |
| **云端调用（DeepSeek/GLM）** | **正常，真烧钱** | **全部拦截**（`BlockCloudInTestMode` 默认 true，`ShouldBlockCloudPublic()` 短路） |
| 本地 Ollama | 本地优先，失败**回退云端** | 照常跑（免费），失败**不回退云端**（直接放弃/降级） |
| 记忆/人格/动作记忆落盘 | 正常写入 | **不写**（`IsTestMode` 防污染） |
| `usage_log.jsonl` | 生产消耗真相 | 只记 `src=local` 免费行，**不代表生产消耗** |
| Player.log 留痕 | 正常 usage 摘要 | `🛡 测试模式：已拦截云端调用（<source>）` |
| inbox 终端链路 | 不可用 | 可用（`@@view/@@emote/@@approval/@@view:extclick`） |
| GLM 视觉工具 | 正常 | 拦截：动作验证评分固定 0/5；法眼/内观返回「已拦截」文案 |

> ⚠️ **一句话结论：测试模式 = 零云端消耗，但永远验证不到真实云端链路**（缓存命中率、真实响应、价格估算都看不到）。

## 三、测试时 Token 消耗注意点（AI 铁律）

1. **默认云端全拦截** → 测试时"AI 没反应 / 回复为空 / 工具说被拦截"，先查是不是拦截（Player.log 找 `🛡`），**不是 bug**。
2. **别拿测试的 usage_log 推断生产消耗**：隔离目录的 `usage_log.jsonl` 只有 `src=local` 行；看生产消耗要看 `D:\DesktopPetData\usage_log.jsonl`。
3. **本地模型是测试里唯一还在"记消耗"的调用**：`src=local` 每行 cost=0，用来确认本地链路活着。
4. **验证"测试没烧钱"的标准流程**：隔离目录 + `.test_mode` 跑一段 → 检查隔离目录 `usage_log.jsonl` 应只有 `local` 行 → Player.log 应有拦截留痕（`已拦截云端调用`）。
5. **唯一"真烧钱"的测试路径**：需验证真实云端（缓存命中率/价格）时，临时把 `ApiClient.BlockCloudInTestMode` 置 false **且**仍用隔离目录 + `.test_mode`（防记忆污染），测完必须恢复。**这条路径每次都要向用户确认后再跑**。
6. **GLM 视觉在测试模式固定失败是预期**：动作验证 `GLM=0/5`、`法眼/内观/动作评价` 返回拦截文案 —— 别当成回归去"修"。
7. **测试进程若没继承 `DEEPSEEK_API_KEY` / GLM key 环境变量** → 会先走「Key 未配置，跳过」分支（比拦截更早），日志里是 `API Key 未配置` 而不是 `已拦截`，别误判成拦截没生效。
8. **缓存命中率观测只能在生产**（或第 5 条放开拦截的路径）跑，测试日志里没有 `usage` 字段可看。
9. **AutoChat 问候是纯气泡**（不调 LLM，免费）——测试时不会被拦也不用拦；真正会烧钱的定时源是 IdleChatGenerator/ServerPoll/ProactiveMessageScheduler（走 `ChatManager.SendMessage` → ApiClient）。

## 四、消耗观测手段（怎么查"钱花哪了"）

### 4.1 `usage_log.jsonl`（权威持久化）
位置：`<DataRoot>/usage_log.jsonl`（生产 = `D:\DesktopPetData\`，测试 = 隔离目录）。每行一条：
```json
{"t":"2026-08-16 20:17:58","src":"chat","model":"deepseek-v4-flash","prompt":11000,"hit":2560,"comp":166,"cost":0.0187}
```
`src` 来源约定：

| src | 含义 |
|-----|------|
| `chat` | ChatManager 日常对话（含工具调用） |
| `motion` | MotionTranslator DeepSeek 兜底（本地失败后） |
| `idle` | IdleChatGenerator 闲话/问候回退 |
| `weather` | TimeWeatherController 天气语录回退 |
| `reflect` | PetMemory 记忆提炼 |
| `glm` | GLM 视觉/镜鉴调用 |
| `local` | Ollama 本地（免费，记录对比用） |

上限：2MB / 2 万行，超限从头部截断保留最新。跨重启保留，启动时 `LoadHistoryIntoUsageStats` 回灌面板「累计（跨重启）」。

### 4.2 面板「消耗」子面板（`@@view:usage`，仅测试模式）
近 1 小时 + 累计（跨重启）两个口径 + 「来源明细」分源统计。

### 4.3 Player.log
- 云端成功响应：`ExtractUsageSummary` 打 `usage` 摘要（含 `prompt_cache_hit_tokens`，看命中率）。
- 测试拦截：`[ApiClient] 🛡 测试模式：已拦截云端调用（<source>）`。

### 4.4 快速统计命令（PowerShell）
```powershell
# 按来源汇总生产消耗（调用次数 + 估算费用）
Get-Content D:\DesktopPetData\usage_log.jsonl | ForEach-Object { $_ | ConvertFrom-Json } |
  Group-Object src | ForEach-Object { [pscustomobject]@{ src=$_.Name; calls=$_.Count;
    cost=($_.Group | Measure-Object cost -Sum).Sum } } | Format-Table
```

## 五、未解决痛点（2026-08-16 记录，状态会变）

1. **¥5/天消耗源头未定位（最优先）**：pet 侧 DeepSeek 调用量实测很小（1-2 次/会话），但用户报 ¥5/天。候选嫌疑：① 崩溃反复重启（退出崩溃计数已达 120+，每次启动的问候/初始化都烧一次）；② Ollama 未就绪时本地优先回退云端（开机瞬间尤甚）；③ 定时触发源（IdleChat/ServerPoll/ProactiveMessageScheduler）在后台持续调用。**对策：持久化 usage_log 已上线，需生产环境连续跑数天收集分源数据后再定位**——不要凭猜测下结论。
2. **测试模式一刀切**：禁云端后测试里无法验证真实云端链路（缓存命中率/价格/响应质量），也没有"受控单次放开"的通道（目前只能改代码开关 + 用户确认，见铁律 5）。
3. **测试与生产行为偏差**：测试里"本地失败 = 功能缺失"而非"回退云端"，动作翻译/天气语录/闲话在本地模型不健康时表现与生产不同——排查 bug 时注意区分"被拦/本地挂"与"真 bug"。
4. **destroyTJDevice 退出崩溃**：引擎退出时崩溃计数持续增长（105→108→120），已按 DisableExternalMode→Shutdown→释放 RT 顺序修复，但退出时引擎崩溃是否彻底消失**未最终确认**（退出时崩溃不影响外部交互，但会触发重启 → 叠加痛点 1）。
5. **schannel TLS 全坏（系统级）**：`SEC_E_NO_CREDENTIALS (0x8009030e)`，Node/curl/.NET 全部 HTTPS 失败（浏览器 OK，因 BoringSSL），连 baidu.com 都连不上。影响 DSH harness 切 GPT（`dsh-codex-auth` 已装但连不上）。修复需用户操作：重启 → `sfc /scannow` → `DISM /Online /Cleanup-Image /RestoreHealth` → 卸 SteamTools MITM 证书。**注意：codex CLI（Rust/rustls）不受 schannel 影响，可直接用**。
6. **缓存即记忆（成本优化构想）未实施**：复用 LLM 上下文缓存做记忆、把非文档内容放进同一滚动历史、消除 tool schema 重复发送——预期再降输入 tokens 50%+。**前提：先用 usage_log 收集到真实分源数据**，验证当前主要消耗在哪，再动上下文结构（动 system prompt 前缀会摧毁 98.6% 缓存命中，须谨慎）。
7. **测试模式禁云端的开关粒度**：`BlockCloudInTestMode` 是全局布尔，没有按 source 粒度（如"只拦 chat 放行 idle"）或按时间窗口的开关——后续如需精细化可扩展。

---

## 相关文档
- [`modules/ai-chat-system.md`](modules/ai-chat-system.md) — ChatManager/ApiClient/Token 优化历史（N40 T1-T8、N43、N44）
- [`modules/chat-ui.md`](modules/chat-ui.md) — 「消耗」面板实现细节
- [`AGENTS.md`](../AGENTS.md) — 测试模式隔离铁律（铁律 1/4）
- `scripts/test/runtime_smoke.cjs` — 隔离冒烟测试（内含生产记忆 mtime 零污染断言）
