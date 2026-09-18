# 统一生命控制闭环 Phase B-1：输入写入者所有权

> **证据日期**：2026-09-18  
> **任务包**：`unified-life-control-loop-phase-b-input-ownership-v1`  
> **状态**：认证、表情、旧动作和生成动作的输入租约已绑定到注册写入者；资源级并行和全路径迁移未完成。

## 已验证事实

- `Live2DInputCoordinator` 在保持“一次只允许一个输入租约”的原有语义下，要求每个新租约解析到 `BodyWriterInventory` 的已登记写入者；未知写入者被拒绝且不会取得租约。
- `Live2DInputLease` 现公开 `WriterId`、声明资源和控制等级，用于审计与后续迁移，不提供参数写入能力。
- `PlayCertifiedMotion` 使用显式 `certified-motion` 写入者，而不是泛化为 `generated-motion`；其资源和认证协调控制等级可由租约读取。
- 表情、旧预设和生成动作继续走既有默认映射，分别保留为 `InputLeaseOnly`。它们尚未迁入 `EmbodiedCoordinator`，不能声称已完成单一身体仲裁。

## 验证证据

- `build.ps1 -Quick`：通过，`[OK] Build succeeded!`。
- `build.ps1 -RunTests`：新生成的 EditMode 结果为 `total=234`、`passed=233`、`failed=0`、`ignored=1`。
- `Live2DInputCoordinatorTests` 覆盖认证写入者资源/控制等级，以及未知写入者拒绝且不取得租约。

## 明确未完成

- 当前全局单租约仍比资源模型保守；尚未启用面部与移动等非冲突层并行。
- 空闲、步行、物理、视线、拖拽仍为 `LegacyUnmanaged`，也没有迁入该入口。
- 抢占、取消传播、渲染重建和 Player 实时验证将在后续 Phase B 子任务处理。
