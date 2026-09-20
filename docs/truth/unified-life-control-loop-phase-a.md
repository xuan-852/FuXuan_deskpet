# 统一生命控制闭环 Phase A：控制面基线

> **证据日期**：2026-09-20
> **任务包**：`unified-life-control-loop-phase-a-v1`  
> **状态**：Phase A 的认证路径控制面与写入者清册已验证；旧路径尚未迁入协调器。

## 已验证事实

- `EmbodiedActionRequest` 现带有只读审计用途的 `Source` 与 `CorrelationId`；`EmbodiedRuntimeAdmission` 为认证生产路径填入 `certified-runtime` 和每次请求的关联 ID。它们不提供参数写入接口。
- `EmbodiedCoordinator` 的拒绝分支均写入 `Rejected` 和 `TerminalReason`；完成、取消、超时和抢占后的重复收束保持幂等并只释放所属资源。
- `EmbodiedPoseSnapshot` 现输出版本、活动请求 ID、来源、关联 ID、技能、资源、终态和终态原因；`EmbodiedPoseState` 在动作开始清除旧终态，在收束后清除活动所有者并保留终态原因。恢复回调异常时会继续尝试其他参数，失败项保留供重试。
- `BodyWriterInventory` 明确登记九类生产写入路径及其当前控制等级：`certified-motion` 是 `CertifiedCoordinator`；表达、旧动作、生成动作、空闲和鼠标视线是 `InputLeaseOnly`；步行、桌面物理和拖拽响应仍是 `LegacyUnmanaged`。该清册是迁移与审计依据，**不是**参数白名单、能力注册表或“已经统一控制”的声明。
- `EmbodiedRecoveryTests` 覆盖认证快照的所有者/关联 ID/终态原因，以及清册中“认证受控”和“待迁移”路径的区分。
- `PhaseBObservation.cs` 新增进程内有界 `EmbodiedEventStore`、只读组合 `BodyStateSnapshot/BodyStateStore` 和只读 `ExecutionMonitor`；事件字段只允许短单行标识与摘要哈希，不写磁盘、不联网、不保存截图、原文或窗口信息。姿态层与桌面 PhysicsRoot 状态仍保持分层。
- `LifeState.cs` 新增 LifeState v1 影子 reducer：以有界 `LifeEvent` 汇总用户在场/活动、动作生命周期、身体观测和最小情绪摘要，生成带版本、TTL、来源、置信度和原因的只读快照；事件仅进程内保存，不获得 Live2D 写入权。认证动作阶段使用关联键与阶段类型组合去重，允许合法生命周期链并拒绝重复终态；`@@sim:life-state` 输出 LifeState、BodyState 和 ExecutionMonitor 的脱敏健康摘要，不触发动作。
- 活动分类已按生命语义映射：`idle` 进入 `UserInactive`/`Idle`，coding、studying、browsing、gaming、entertainment 和 other 进入 `UserWorking`/`Working`，communication 进入 `UserInteracting`/`Interacting`；原始窗口标题和敏感内容不进入 LifeState。
- 动作终态已收敛：`Completed` 不再被迟到的普通 `Interrupted` 覆盖，`RecoveryFailed` 优先于迟到的普通中断；相同阶段仍由关联键去重，认证动作继续使用稳定 correlation。
- 表情入口增加 `TryPlayExpression` 成功语义：未知表情不会在验证失败后遗留输入租约；Renderer 禁用和应用退出共用幂等外部输入清理，表情租约与延迟回调会被收束。`StopAllActionsAndExpressions` 作为 Renderer 层停止网关，工具、生成动作前置和测试命令通过该入口停止 Renderer 所有者持有的表情/旧动作状态；生成动作租约仍由生成动作调用方持有和释放。
- 第三阶段新增进程内有界 `LifeTimelineStore`：以来源、事件类型、关联 ID、状态、原因和脱敏摘要组成去重键，保存有序版本和摘要 SHA-256；`Live2DRenderer` 只发布稳定语义的生命、身体与执行健康摘要，避免把逐帧计数写成事件。`@@sim:life-timeline` 仅查询快照，不触发动作，也不写入磁盘或网络。

## 验证证据

- `build.ps1 -Quick`：2026-09-20 通过；LifeStateTests 当前 7 项全部通过。完整结果为 `total=258`、`passed=254`、`failed=3`、`ignored=1`；3 个既有 `ToolEngineTests` 因本机缺少 `es.exe` 失败，非本次 LifeState 改动回归。
- `node --check scripts/test/life_state_shadow_drive.cjs`：通过；使用当前源码隔离 Player `.artifacts/player-current/DesktopPet.exe` 驱动通过。两次 `@@sim:life-state` 快照的 `version/events/bodyVersion` 分别为 `20/20/224`、`47/47/539`，均单调增长；健康状态从启动期 `Degraded` 收敛为 `Healthy`，无 `[LifeState] event rejected`、`NullReferenceException` 或 `AssertionException`。验证使用临时 `FU_XUAN_DATA` 与 `.test_mode`，退出后临时目录清理，生产记忆文件未变化。
- `node --check scripts/test/certified_motion_runtime_drive.cjs`：通过；使用隔离 `screen_side_arm_raise` 曲线验证认证动作生命周期，LifeState 快照从 `action=Active`（`version/events=31/31`）到 `action=Completed`（`version/events=52/52`），并观察到 admission、pose restore、release 与 cleanup 日志；退出后 Player 清理完成。
- `node scripts/docs/generate_document_map.cjs`：本次文档更新后执行。
- 第三阶段隔离 Player：使用当前源码构建的 `.artifacts/player-timeline/DesktopPet.exe`，在临时 `FU_XUAN_DATA` 与 `.test_mode` 下执行 `@@sim:life-state`、`@@sim:life-timeline` 和自然退出；时间线快照为 4 条有序记录，摘要哈希均为 64 位小写十六进制，查询后版本/事件/身体观测/执行观测计数均单调增长，无 `NullReferenceException` 或 `AssertionException`，生产记忆文件未变化。

## 明确未完成

- `InputLeaseOnly` 与 `LegacyUnmanaged` 不等于经过 `EmbodiedCoordinator` 仲裁；它们仍可能在资源、优先级和状态语义上与认证路径竞争。
- Phase B 才逐类迁入表情、旧动作、空闲、步行、物理、视线和拖拽，并以隔离 Player 验证取消、抢占、退出与渲染重建。
- 注意力状态、具身经验、情绪因果与主动策略属于后续阶段；本阶段仅交付已验证的只读生命事件时间线，不开放主动行为或原始参数控制。
