# 符玄编译与本地质量采样测试说明

> **用途**：给开发者执行一次可复现的 Ollama 本地运行测量，确认运行的确实是最新编译产物，并收集聊天与动作质量数据。
> **当前基线**：2026-08-18；质量日志由 `QualityTelemetry` 写入，统计脚本为 `scripts/log-analysis/summarize_quality.cjs`。
> **重要边界**：本说明的首轮采样只测本地模式，不发起 DeepSeek/GLM 云端请求；需要严格差值时，继续阅读 [`quality-comparison-test-guide.md`](quality-comparison-test-guide.md)。

关联文档：[`build-workflow.md`](build-workflow.md)、[`token-cost-testing.md`](token-cost-testing.md)、[`token-saving-architecture.md`](token-saving-architecture.md)、[`project-bugs-and-acceptance.md`](project-bugs-and-acceptance.md)。

## 一、测试前检查

1. 关闭已有的 `DesktopPet.exe`，避免构建锁定旧输出，也避免两个实例同时写日志。
2. 确认 Ollama 已启动，并确认模型存在：

```powershell
Invoke-RestMethod http://127.0.0.1:11434/api/tags
ollama list
```

当前默认动作/对话模型是 `qwen2.5:3b`。如果列表中没有它，先执行 `ollama pull qwen2.5:3b`。

3. 测量目录可以使用独立目录，避免污染生产记忆。**不要在测量目录创建 `.test_mode`**：本地模式本身已经通过 `--ollama` / `FU_XUAN_OLLAMA=1` 禁用云端，`.test_mode` 会额外改变记忆和 UI 行为。

## 二、编译并确认不是旧版本

在仓库根目录执行：

```powershell
.\build.ps1 -Quick
.\build.ps1
```

`-Quick` 只验证 C# 编译，不更新 `Build/DesktopPet.exe`；必须执行完整构建后才能启动桌宠测量。

构建结束后执行下面的验收脚本。它同时检查 exe、`Assembly-CSharp.dll` 的时间、以及本轮新增的 `QualityTelemetry` 类型标记：

```powershell
$exe = Get-Item .\Build\DesktopPet.exe -ErrorAction Stop
$dll = Get-Item .\Build\DesktopPet_Data\Managed\Assembly-CSharp.dll -ErrorAction Stop
$latestSource = Get-ChildItem .\code\desktop_unity\Assets\Scripts -Recurse -Filter *.cs |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$dllText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($dll.FullName))

"exe: $($exe.LastWriteTime)"
"dll: $($dll.LastWriteTime)"
"latest source: $($latestSource.LastWriteTime)"
if ($dll.LastWriteTime -lt $latestSource.LastWriteTime) { throw '拒绝测量：运行时 DLL 早于源码，仍是旧构建' }
if (-not $dllText.Contains('QualityTelemetry')) { throw '拒绝测量：运行时 DLL 不含 QualityTelemetry，未包含最新代码' }
Write-Host '[OK] 构建产物包含质量遥测代码，可以开始测量。'
```

如果 `build.ps1` 长时间无日志或上述检查失败，不要启动桌宠。按 [`build-workflow.md`](build-workflow.md) 的“构建卡死处理”先恢复 Tuanjie 环境；否则看到的是旧 exe，数据没有比较价值。

## 三、启动本地采样实例

推荐首轮使用一个全新的采样目录。这样 `quality_log.jsonl` 从零开始，统计不会混入过去的运行数据：

```powershell
$sampleRoot = 'D:\DesktopPetData\measure_ollama_20260818'
New-Item -ItemType Directory -Force -Path $sampleRoot | Out-Null
Remove-Item -LiteralPath (Join-Path $sampleRoot '.test_mode') -Force -ErrorAction SilentlyContinue
$env:FU_XUAN_DATA = $sampleRoot
$env:FU_XUAN_OLLAMA = '1'
Start-Process -FilePath (Resolve-Path '.\Build\DesktopPet.exe') -ArgumentList '--ollama'
```

启动后确认桌宠窗口实际出现，再开始交互。测量期间不要切换到不带 `--ollama` 的实例；不要使用 `cost_probe.ps1`，它是受控云端成本测试，不属于本次本地质量采样。

## 四、采样内容与最小样本量

建议连续运行 1～3 小时，至少完成以下输入，尽量按同一顺序记录：

| 类型 | 最小数量 | 示例方向 |
|------|---------:|---------|
| 普通聊天 | 20 | 问候、询问状态、闲聊、情绪回应 |
| 需要长回复的聊天 | 10 | 解释概念、总结一段内容、分步骤建议 |
| 简单动作 | 10 | 点头、眨眼、开心、害羞、困倦 |
| 复合/肢体动作 | 20 | 挥手、抬手、转身、庆祝、拒绝、惊讶 |
| 长时间自然运行 | 1 段 | 观察后台主动行为、重复动作和本地模型失败 |

每次只改变一个输入，记录是否出现：空回复、答非所问、过长回复、动作不完整、动作幅度异常、停在中间、明显重复、等待超时。不要把用户原文、密钥或完整回复写入仓库；质量遥测只记录来源、结果、耗时和评分字段。

## 五、结束后导出数据

关闭本次桌宠实例后，在仓库根目录执行：

```powershell
node scripts/log-analysis/summarize_quality.cjs D:\DesktopPetData\measure_ollama_20260818
```

主要文件：

- `quality_log.jsonl`：聊天、动作决策、动作翻译、动作验证的来源与结果；不含用户原文。
- `usage_log.jsonl`：Token/费用汇总。本地模式应为 `source=local`、费用为 0。
- `logs\player_log.txt`：启动、Ollama 健康检查和失败原因。

把统计命令的输出和以下摘要发回即可，不必上传原始对话日志：

```text
采样目录：
采样开始/结束时间：
模型：
普通聊天数量与失败数量：
动作决策数量与失败数量：
动作翻译 local/template 的 accepted/parse：
本地模型超时或未就绪次数：
主观发现的典型问题：
```

## 六、如何判断“下降了多少”

本轮可以得到本地模式的绝对指标，但不能单独得到“比云端下降多少”。要计算差值，后续必须用同一组聊天和动作输入，在相同构建下做一次受控云端对照，并记录：

```text
下降比例 = (云端指标 - 本地指标) / 云端指标
```

推荐使用以下指标：

- 聊天：成功率、有效回复率、人工 0～5 分相关性、平均等待时间、超长回复率。
- 动作：决策成功率、JSON 解析率、翻译 accepted 率、动作完成率、人工 0～5 分标准程度。
- 成本：`usage_log.jsonl` 的 prompt、cache hit、completion、cost；质量和成本必须分开看。

评分建议固定为：`0` 无效，`1` 明显错误，`2` 勉强可用，`3` 合格，`4` 良好，`5` 接近预期。样本不足 30 条时只报告观察结果，不下“下降 X%”的结论。

## 七、通过标准

- 编译验收脚本通过，且 DLL 含 `QualityTelemetry`。
- Ollama 健康检查通过，实际使用的模型为 `qwen2.5:3b`。
- 本地采样期间 `usage_log.jsonl` 没有云端来源和费用。
- `quality_log.jsonl` 能产生 `chat`、`motion_decision` 或 `motion_translation` 记录。
- 采样结束后能用汇总脚本输出统计；失败样本能在 `player_log.txt` 找到原因。
- 本轮只记录数据，不根据小样本直接调整模型、提示词或质量闸门。

## 八、异常处理

- 没有窗口：先确认 exe 时间和 DLL 标记，再查看 `logs\player_log.txt`；不要重复启动多个实例。
- Ollama 未就绪：检查 Ollama 服务和 `ollama list`，修复后重启桌宠再继续，不能把失败样本和正常样本混在一起。
- `quality_log.jsonl` 不存在：大概率运行的是旧构建，或 `FU_XUAN_DATA` 没有传给桌宠；重新执行第三节并检查进程环境。
- 出现云端记录：立即关闭实例，检查是否真的带了 `--ollama` 或 `FU_XUAN_OLLAMA=1`，并将该批数据标记为无效。
