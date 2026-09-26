# L3 SoulLink 离线批量动作 PoC

> **状态**：离线 fixture 链路已实现并通过隔离 Node smoke；真实 `@soullink-emotion/engine` 集成仍未验证。
> **范围**：只生成可审查的离线时间线、曲线候选、特征包和本地 survivor 白名单，不写入 Unity 运行时。

## 已验证的链路

PoC 入口位于 `scripts/live2d-probe/`，由以下步骤组成：

1. `soullink_profile_check.cjs` 使用 `capability-catalog/v1` 审计 `soullink-fuxuan-profile/v1`。报告 `soullink-profile-audit/v1` 固定输出 `parameterCoverage`、`missingParameters`、`reservedParameters`、`safeEmotionChannels` 和 `safeBodyChannels`，并拒绝未知、重复、越界、非 reset-stable、口型/下颌保留参数及不安全约束。
2. `soullink_engine_scan.cjs` 要求绝对 profile/catalog/output 路径，输出根必须位于系统临时目录并带 `.test_mode`。矩阵按 emotion/intensity/seed 产生确定性 case；每个 case 创建新的 `createManualClock()`，运行两遍并比较规范化哈希。失败 case 会保留在 `batch-report.json`，不会伪装成成功。
3. `soullink_timeline_to_candidate.cjs` 将时间线转换为 `l3-soullink-motion-candidate/v1`。采用 Cubism `type 0` linear segments；时间线中曾出现的每个参数都建立完整曲线，并在总时长末尾显式回填 profile neutral/default 或 catalog baseline。候选固定为 `uncertified`、`productionStatus=not-certified`、`mapWriteAllowed=false`、`llmExposureAllowed=false`。
4. `soullink_extract_features.cjs` 把候选包成隔离的 motion3 envelope，调用既有 `extract_motion_features.cjs`，保留 evidence-only 限制和 candidate/envelope/timeline 哈希 provenance。
5. `soullink_select_candidates.cjs` 在本地按固定相位网格、范围归一化距离、缺失通道惩罚和 duration 项进行 exact content dedupe 与近邻筛选；只写 `cloud-review-survivors.json`，且 `cloudRequestsMade=0`、后续必须先做隔离 Player capture。

隔离回归命令：

```text
node scripts/live2d-probe/test/soullink_offline_poc_smoke.cjs
node scripts/live2d/test/platform_cli_smoke.cjs
node scripts/live2d/test/experiment_cli_smoke.cjs
node scripts/live2d/test/roi_analysis_smoke.cjs
node scripts/test/capability_catalog_smoke.cjs
node scripts/test/sequence_candidate_report_smoke.cjs
```

本轮 fixture smoke 与上述 Node 回归均通过，未运行 C# 构建，因为改动仅涉及 Node/JSON 工具。

## 真实 engine 边界

当前扫描器的真实包适配器只接受同时提供 `SoullinkRuntime` 与可 `set` 的 `createManualClock()` 的 `@soullink-emotion/engine`。本地 registry 可取得的公开包版本和导出边界尚未证明满足该接口；因此 fixture smoke 只证明适配器契约和离线流水线，不证明 SoulLink 真实 runtime 的情绪语义、自然度或生产兼容性。不得把该 PoC 候选复制到 `Resources`、`CertifiedMotionLibrary`、正式参数映射或生产 LLM 工具列表。

## 明确非目标

PoC 不接 TTS/口型、PIXI、`runtime-core` 会话、在线 Unity 写入或 `LlmExposed`；不允许自由身体动作。云端评审若后续执行，只能消费 survivor 白名单，并且每项先取得隔离 Player capture 与独立授权。
