# 统一生命控制闭环 Phase B-1：输入写入者所有权

> **证据日期**：2026-09-21
> **任务包**：`unified-life-control-loop-phase-b-input-ownership-v1`  
> **状态**：认证、表情、旧动作、生成动作、步行和桌面物理的输入租约已绑定到注册写入者；资源级并行和全路径迁移未完成。

## 已验证事实

- `Live2DInputCoordinator` 在保持“一次只允许一个输入租约”的原有语义下，要求每个新租约解析到 `BodyWriterInventory` 的已登记写入者；未知写入者被拒绝且不会取得租约。
- `Live2DInputLease` 现公开 `WriterId`、声明资源和控制等级，用于审计与后续迁移，不提供参数写入能力。
- `PlayCertifiedMotion` 使用显式 `certified-motion` 写入者，而不是泛化为 `generated-motion`；其资源和认证协调控制等级可由租约读取。
- 表情、旧预设和生成动作继续走既有默认映射，分别保留为 `InputLeaseOnly`。它们尚未迁入 `EmbodiedCoordinator`，不能声称已完成单一身体仲裁。
- `DesktopPet` 的 `desktop-physics` 写入者已登记为 `InputLeaseOnly`，资源为 `Movement | Body | Effect`，恢复所有者为 `physics-state`。它通过与 `Live2DRenderer` 共享的 `Live2DInputCoordinatorHost` 取得全局单租约；租约取得失败时仍执行 PhysicsRoot 的既有物理步进，因此不引入资源级并行或抢占，也不把桌面物理变成 Live2D 参数命令入口。

## 验证证据

- `build.ps1 -Quick`：通过，`[OK] Build succeeded!`。
- Quick 阶段报告 `[OK] Build succeeded!`；`logs/build/test_results.xml` 仍是修复 LifeState 断言前的旧结果（`total=287`、`passed=282`、`failed=4`、`skipped=1`），因此本轮不能把完整 EditMode 记为通过。LifeState 源测试已改为符合当前生产契约的拒绝语义，修复后的完整结果仍待重新生成。
- 新鲜隔离 Player `Build/phaseb-validation-20260921-011750/DesktopPet.exe` 观察到 walking 期间的真实冲突日志：`Rejected DesktopPhysics/physics-update: active=Walking/walking-state#109`；同一时段 `DesktopState` 仍为 `Walking` 且 `velocity=(1,0)`，证明 lease 冲突不阻塞 PhysicsRoot。
- 同一 Player 的生命周期脚本验证了 `Idle → Walking → Paused → Idle`，暂停期间桌面快照 version 冻结，退出前存在脱敏 `life-state`/`life-timeline` marker；退出日志包含 `[EmbodiedSafeRecovery] recovered: test-exit` 和 Renderer test-exit cleanup，未出现 `NullReferenceException`、`AssertionException` 或 event rejected。
- 新鲜隔离 Player 使用 `external_Hiyori_Hiyori_m02` 完成认证动作 handoff：先在 walking 状态被稳定静止门禁拒绝且未出现 `started`，随后停止 walking 后同一实例成功取得 `certified-motion` lease/admission，动作自然完成并记录 pose restored、admission released 和 cleanup。独立退出驱动还验证动作中 `@@test:quit` 的准入取消、租约释放和 test-exit recovery；真实 DragHandler 驱动另验证 `drag-response` admission 与 generated-motion 冲突拒绝，未改变单一 global active lease；临时 Player 构建目录不固化在 truth 文档中。

## 明确未完成

- 当前全局单租约仍比资源模型保守；尚未启用面部与移动等非冲突层并行。
- `desktop-physics` 已不再是 `LegacyUnmanaged`，但 `drag-response` 仍为 `LegacyUnmanaged`；桌面 `PhysicsRoot` 仍与 Live2D BodyRoot/Cubism 参数状态分离，拖拽位置写入仍由 `DesktopPet` / `DragHandler` 负责。
- `Live2DRenderer` 的行走与桌面物理租约已完成代码级、EditMode 和新鲜 Player 生命周期验证；真实 DragHandler 的模拟拖拽、释放、落地与 walking handoff 已完成隔离 Player 验证。renderer 级停止淡出时间推进、renderer disable/destroy/rebuild、overlay RT/camera rebuild、Windows application pause callback 顺序、Windows 关机/注销、真实 OS 鼠标拖拽，以及完整 action handoff 仍未独立覆盖。参数级写入仍是内部硬编码路径并经 `ParameterCommitBridge` 提交。
- `drag-response` 仍为 `LegacyUnmanaged`；资源级并行或抢占仍未引入。
- 真实 OS 鼠标、renderer 重建和 Windows 关机/注销等边界仍需后续专项验证。
