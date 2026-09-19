# 统一生命控制闭环 Phase A：控制面基线

> **证据日期**：2026-09-19
> **任务包**：`unified-life-control-loop-phase-a-v1`  
> **状态**：Phase A 的认证路径控制面与写入者清册已验证；旧路径尚未迁入协调器。

## 已验证事实

- `EmbodiedActionRequest` 现带有只读审计用途的 `Source` 与 `CorrelationId`；`EmbodiedRuntimeAdmission` 为认证生产路径填入 `certified-runtime` 和每次请求的关联 ID。它们不提供参数写入接口。
- `EmbodiedCoordinator` 的拒绝分支均写入 `Rejected` 和 `TerminalReason`；完成、取消、超时和抢占后的重复收束保持幂等并只释放所属资源。
- `EmbodiedPoseSnapshot` 现输出版本、活动请求 ID、来源、关联 ID、技能、资源、终态和终态原因；`EmbodiedPoseState` 在动作开始清除旧终态，在收束后清除活动所有者并保留终态原因。恢复回调异常时会继续尝试其他参数，失败项保留供重试。
- `BodyWriterInventory` 明确登记九类生产写入路径及其当前控制等级：`certified-motion` 是 `CertifiedCoordinator`；表达、旧动作、生成动作、空闲和鼠标视线是 `InputLeaseOnly`；步行、桌面物理和拖拽响应仍是 `LegacyUnmanaged`。该清册是迁移与审计依据，**不是**参数白名单、能力注册表或“已经统一控制”的声明。
- `EmbodiedRecoveryTests` 覆盖认证快照的所有者/关联 ID/终态原因，以及清册中“认证受控”和“待迁移”路径的区分。
- `PhaseBObservation.cs` 新增进程内有界 `EmbodiedEventStore`、只读组合 `BodyStateSnapshot/BodyStateStore` 和只读 `ExecutionMonitor`；事件字段只允许短单行标识与摘要哈希，不写磁盘、不联网、不保存截图、原文或窗口信息。姿态层与桌面 PhysicsRoot 状态仍保持分层。
- 表情入口增加 `TryPlayExpression` 成功语义：未知表情不会在验证失败后遗留输入租约；Renderer 禁用和应用退出共用幂等外部输入清理，表情租约与延迟回调会被收束。`StopAllActionsAndExpressions` 作为 Renderer 层停止网关，工具、生成动作前置和测试命令通过该入口停止 Renderer 所有者持有的表情/旧动作状态；生成动作租约仍由生成动作调用方持有和释放。

## 验证证据

- `build.ps1 -Quick`：2026-09-19 通过，`[OK] Build succeeded!`。
- `build.ps1 -RunTests`：本轮新生成结果为 `total=249`、`passed=248`、`failed=0`、`ignored=1`；含新增认证拒绝终态、终态幂等、抢占、PoseState 异常恢复、Phase B 观测基础测试及表情/输入租约生命周期测试。
- `node scripts/docs/generate_document_map.cjs`：待本轮文档写入后重新执行。

## 明确未完成

- `InputLeaseOnly` 与 `LegacyUnmanaged` 不等于经过 `EmbodiedCoordinator` 仲裁；它们仍可能在资源、优先级和状态语义上与认证路径竞争。
- Phase B 才逐类迁入表情、旧动作、空闲、步行、物理、视线和拖拽，并以隔离 Player 验证取消、抢占、退出与渲染重建。
- 事件时间线、注意力状态、具身经验、情绪因果与主动策略属于 Phase C/D，尚未实现。
