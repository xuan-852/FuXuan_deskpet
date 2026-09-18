# 统一生命控制闭环 Phase A：控制面基线

> **证据日期**：2026-09-18  
> **任务包**：`unified-life-control-loop-phase-a-v1`  
> **状态**：Phase A 的认证路径控制面与写入者清册已验证；旧路径尚未迁入协调器。

## 已验证事实

- `EmbodiedActionRequest` 现带有只读审计用途的 `Source` 与 `CorrelationId`；`EmbodiedRuntimeAdmission` 为认证生产路径填入 `certified-runtime` 和每次请求的关联 ID。它们不提供参数写入接口。
- `EmbodiedPoseSnapshot` 现输出版本、活动请求 ID、来源、关联 ID、技能、资源、终态和终态原因；`EmbodiedPoseState` 在动作开始清除旧终态，在收束后清除活动所有者并保留终态原因。
- `BodyWriterInventory` 明确登记九类生产写入路径及其当前控制等级：`certified-motion` 是 `CertifiedCoordinator`；表达、旧动作、生成动作是 `InputLeaseOnly`；空闲、步行、桌面物理、鼠标视线和拖拽响应仍是 `LegacyUnmanaged`。该清册是迁移与审计依据，**不是**参数白名单、能力注册表或“已经统一控制”的声明。
- `EmbodiedRecoveryTests` 覆盖认证快照的所有者/关联 ID/终态原因，以及清册中“认证受控”和“待迁移”路径的区分。

## 验证证据

- `build.ps1 -Quick`：通过，`[OK] Build succeeded!`。
- `build.ps1 -RunTests`：新生成的 EditMode 结果为 `total=232`、`passed=231`、`failed=0`、`ignored=1`。
- `node scripts/docs/generate_document_map.cjs`：待本文件写入后重新执行。

## 明确未完成

- `InputLeaseOnly` 与 `LegacyUnmanaged` 不等于经过 `EmbodiedCoordinator` 仲裁；它们仍可能在资源、优先级和状态语义上与认证路径竞争。
- Phase B 才逐类迁入表情、旧动作、空闲、步行、物理、视线和拖拽，并以隔离 Player 验证取消、抢占、退出与渲染重建。
- 事件时间线、注意力状态、具身经验、情绪因果与主动策略属于 Phase C/D，尚未实现。
