# 符玄本地 / 云端质量对照测试说明

> **用途**：用相同案例、相同构建和相同评分标准，测量 Ollama 本地模型与纯云端模型的聊天质量、动作生成质量、延迟和成本差异。
> **前置**：先阅读 [`quality-measurement-test-guide.md`](quality-measurement-test-guide.md) 完成编译产物检查。
> **安全边界**：本地组不产生云端费用；云端组会真实调用 DeepSeek/GLM，必须使用独立数据目录，并在确认 API Key 和预算后运行。

## 一、两种模式的含义

| 模式 | 启动参数 | 聊天 | 动作翻译 | 云端失败后的本地回退 |
|------|---------|------|----------|--------------------|
| 本地组 | `--ollama` | Ollama-only，无工具回环 | Ollama-only | 不回退 |
| 云端基线组 | `--cloud-baseline` | DeepSeek 工具回环 | DeepSeek-only | 不回退 |
| 默认生产组 | 无参数 | 云端为主，按实际路由 | 本地优先，失败回云端 | 有 |

本次“质量差值”只比较前两组。默认生产组是混合架构，适合测真实成本和最终体验，但不能作为纯本地/纯云端模型对照。

## 二、固定案例集

两组必须使用同一批案例，并且每次发送前先写入同一个案例编号：

| 编号 | 类型 | 输入 |
|------|------|------|
| `chat_001` | 普通聊天 | 你好，今天过得怎么样？ |
| `chat_002` | 人格回应 | 你觉得我今天看起来有点累吗？ |
| `chat_003` | 解释 | 用简单的话解释什么是缓存。 |
| `chat_004` | 规划 | 帮我安排一个今晚两小时的学习计划。 |
| `chat_005` | 多轮承接 | 我刚才说的学习计划，第一步具体怎么做？ |
| `chat_006` | 约束回复 | 用三句话告诉我如何提高专注力。 |
| `motion_001` | 简单动作 | 请点头表示同意。 |
| `motion_002` | 简单动作 | 请眨眼并露出开心的表情。 |
| `motion_003` | 肢体动作 | 请挥右手向我打招呼。 |
| `motion_004` | 肢体动作 | 请双手捂脸，表现害羞。 |
| `motion_005` | 复合动作 | 请先惊讶，再后退一步表示害怕。 |
| `motion_006` | 复合动作 | 请抬手指向前方，然后歪头思考。 |

正式结论建议至少扩展到每类 20～30 条。上表用于验证流程，不足以单独给出稳定的百分比结论。

## 三、本地组

关闭已有桌宠后，准备独立目录。目录中不要创建 `.test_mode`，否则会改变持久化和收件箱行为；`--ollama` 已经阻断云端。

```powershell
$localRoot = 'D:\DesktopPetData\compare_local_20260818'
New-Item -ItemType Directory -Force -Path $localRoot | Out-Null
Remove-Item -LiteralPath (Join-Path $localRoot '.test_mode') -Force -ErrorAction SilentlyContinue
$env:FU_XUAN_DATA = $localRoot
$env:FU_XUAN_OLLAMA = '1'
Remove-Item Env:FU_XUAN_CLOUD_BASELINE -ErrorAction SilentlyContinue
Start-Process -FilePath (Resolve-Path '.\Build\DesktopPet.exe') -ArgumentList '--ollama'
```

每个案例先设置编号，再发送输入：

```powershell
$inbox = 'D:\DesktopPetData\compare_local_20260818\inbox.txt'
Set-Content -LiteralPath $inbox -Value '@@case:chat_001' -Encoding UTF8
Start-Sleep -Milliseconds 500
Set-Content -LiteralPath $inbox -Value '你好，今天过得怎么样？' -Encoding UTF8
```

等待回复和动作完成后，再执行下一个案例。案例编号会写入 `quality_log.jsonl` 的 `case_id` 字段，不记录输入原文。

## 四、云端基线组

先关闭本地组，再使用新的独立目录。必须确认 `DEEPSEEK_API_KEY` 和需要的 `GLM_API_KEY` 已配置；不要携带 `FU_XUAN_OLLAMA=1`。

```powershell
$cloudRoot = 'D:\DesktopPetData\compare_cloud_20260818'
New-Item -ItemType Directory -Force -Path $cloudRoot | Out-Null
Remove-Item -LiteralPath (Join-Path $cloudRoot '.test_mode') -Force -ErrorAction SilentlyContinue
$env:FU_XUAN_DATA = $cloudRoot
Remove-Item Env:FU_XUAN_OLLAMA -ErrorAction SilentlyContinue
$env:FU_XUAN_CLOUD_BASELINE = '1'
Start-Process -FilePath (Resolve-Path '.\Build\DesktopPet.exe') -ArgumentList '--cloud-baseline'
```

`--cloud-baseline` 会允许隔离目录的 `inbox.txt` 驱动案例，但不会开启普通测试模式，因此云端请求不会被 `BlockCloudInTestMode` 拦截。云端组每条案例都会真实消耗 Token；达到计划样本量后立即停止，不要长时间让后台主动行为混入样本。

使用与本地组完全相同的 `@@case:<编号>` 和输入内容。不要把两组案例顺序或内容改成不同版本。

## 五、汇总与配对比较

分别汇总两组日志：

```powershell
node scripts/log-analysis/summarize_quality.cjs D:\DesktopPetData\compare_local_20260818
node scripts/log-analysis/summarize_quality.cjs D:\DesktopPetData\compare_cloud_20260818
```

再按 `task + case_id` 做逐案例配对：

```powershell
node scripts/log-analysis/compare_quality.cjs `
  D:\DesktopPetData\compare_local_20260818\quality_log.jsonl `
  D:\DesktopPetData\compare_cloud_20260818\quality_log.jsonl
```

输出中的 `local vs cloud` 是本地与云端指标，括号内是本地减云端的百分点或绝对差。只有两组同时出现的案例才会进入比较；“仅本地/仅云端”说明案例编号、启动模式或测试流程有问题。

## 六、质量评分

自动遥测只能判断调用和结构是否成功，不能完全判断语义或动作姿态。对两组同编号结果进行盲评：

| 维度 | 评分标准 |
|------|---------|
| 聊天相关性 | 0 无关，1 明显错误，2 勉强，3 合格，4 良好，5 完全贴合 |
| 回复合理性 | 是否符合符玄人格、上下文和用户约束 |
| 动作标准程度 | 0 未执行，1 完全错误，2 部分正确，3 基本正确，4 清晰自然，5 接近预期 |
| 动作完成度 | 是否完成起势、主体动作和收势，是否卡顿或残留 |

计算本地相对云端的下降比例：

```text
下降比例 = (云端平均分 - 本地平均分) / 云端平均分 × 100%
```

同时报告绝对分差、成功率、解析率、延迟和费用。样本少于 30 个配对案例时，只报告趋势，不写“下降 X%”作为最终结论。

## 七、数据有效性检查

- 两组使用同一个 `Build/DesktopPet.exe`，且 DLL 已通过 `QualityTelemetry` 标记检查。
- 本地日志的聊天/翻译来源应主要是 `local`；云端基线的对应来源应是 `cloud`。
- 本地组 `usage_log.jsonl` 的费用应为 0；云端组应能看到真实 Token 用量。
- 两组 `quality_log.jsonl` 都有 `case_id`，且配对数量足够。
- `template` 动作单独统计，不与模型生成动作混合。
- 云端组出现请求失败、Key 缺失、预算拦截或后台主动消息时，标记对应案例无效并重测，不拿失败原因当模型质量。

