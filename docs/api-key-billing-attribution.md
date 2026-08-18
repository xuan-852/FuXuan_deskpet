# API Key 归属与官方账单核对

## 目的

桌宠不能实时读取 DeepSeek/GLM 官方平台账单，因此 `usage_log.jsonl` 中的费用只是本地估算，不能单独证明某笔费用已经计入哪个平台 Key。

## 代码约定

- `ChatConfig` 只从 `DEEPSEEK_API_KEY`、`GLM_API_KEY` 读取密钥，不在代码中保存完整 Key。
- 桌宠启动时由 `UsageLogger.RecordRuntimeIdentity()` 写入一条 `kind=runtime_identity` 记录。
- 每条用量记录写入 `key_id`、不可逆的 `key_hash` 和 `billing_attribution`。
- `key_id` 只保留前缀和后四位，例如 `sk-3707b*****52ca`；完整密钥、Authorization 头和请求体不得写入日志。
- `billing_attribution=manual_platform_check_required` 表示必须到官方平台人工核对；`local` 请求为 `not_applicable`。

## 测试与排查规则

1. 启动云端测试前先检查运行环境中的 Key ID，不要只看“DEEPSEEK key: configured”。
2. 测试完成后查看 `<DataRoot>/usage_log.jsonl` 的 `runtime_identity` 和各条用量记录，确认 Key ID 一致。
3. 如果日志中的 Key ID 与目标专属 Key 不一致，该批数据可以用于功能/质量分析，但不能用于该专属 Key 的官方费用归属结论。
4. 官方账单、后台 Key 名称和本地 `key_id` 不一致时，以官方平台为准，并保留本地日志作为排查依据。

## 安全边界

Key ID 和 SHA-256 仅用于归属核对，不是认证凭据。任何日志、报告、提交或错误输出都不得包含完整 API Key。
