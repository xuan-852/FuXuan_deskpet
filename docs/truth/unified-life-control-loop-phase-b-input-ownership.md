# 统一生命控制闭环 Phase B-1：输入写入者所有权

> **证据日期**：2026-09-22
> **任务包**：`unified-life-control-loop-phase-b-input-ownership-v1`  
> **状态**：认证、表情、旧动作、生成动作、空闲、步行、桌面物理和拖拽响应均已绑定到注册写入者；拖拽接管动作与落地恢复已完成代码级及隔离 Player 验证，仍采用全局单租约。

## 已验证事实

- `Live2DInputCoordinator` 在保持“一次只允许一个输入租约”的原有语义下，要求每个新租约解析到 `BodyWriterInventory` 的已登记写入者；未知写入者被拒绝且不会取得租约。
- `Live2DInputLease` 现公开 `WriterId`、声明资源和控制等级，用于审计与后续迁移，不提供参数写入能力。
- `PlayCertifiedMotion` 使用显式 `certified-motion` 写入者，而不是泛化为 `generated-motion`；其资源和认证协调控制等级可由租约读取。
- 表情、旧预设和生成动作继续走既有默认映射，分别保留为 `InputLeaseOnly`。它们尚未迁入 `EmbodiedCoordinator`，不能声称已完成单一身体仲裁。
- `DesktopPet` 的 `desktop-physics` 写入者已登记为 `InputLeaseOnly`，资源为 `Movement | Body | Effect`，恢复所有者为 `physics-state`。它通过与 `Live2DRenderer` 共享的 `Live2DInputCoordinatorHost` 取得全局单租约；租约取得失败时仍执行 PhysicsRoot 的既有物理步进，因此不引入资源级并行或抢占，也不把桌面物理变成 Live2D 参数命令入口。
- `drag-response` 已登记为 `InputLeaseOnly`，由 `DragHandler` 持有其租约并在正常释放、失焦、禁用、销毁和退出路径幂等清理；walking 租约活动时，拖拽阈值会通过 Renderer/Coordinator 的显式原子交接将当前 walking lease 替换为 drag-response。渲染器自有 idle/legacy/certified/test action 在阈值触发后先取消、恢复姿态并清除 action/movement lock，再取得 DragResponse；动作清理至准入之间由 renderer 屏障跳过普通动作写入，并清理星辉右臂、剑指/手指和手部图层参数；拖拽阶段先清理旧动作、再写入 DragResponse，并同步 CubismParameterStore。全新隔离 Player 参数回归已确认星辉抬手基线不再出现在拖拽快照中，落地后相关参数归零；该证据使用 `@@sim:*`，不等同真实 OS 鼠标动作中断。

- 直接交互现在通过 `DragHandler.OnInteraction` → `AttentionReactionAdapter` 进入生命状态链；click、成功 drag-start、drag-end 和 drag-abort 使用短 correlation 写入有界 `EmbodiedEvent`/`LifeTimeline`/`LifeState.DirectInteraction`，并调用 `MotionAgent.NotifyInteraction()`。该适配层不取得新输入租约，也不写 Live2D 参数；隔离 Player 事件链和真实 OS 鼠标专项仍需单独验收。

- `build.ps1 -Quick`：通过，`[OK] Build succeeded!`。
- `build.ps1 -RunTests`：本轮新鲜结果为 `failed=0`；Everything 集成测试在本机缺少 `es.exe`/IPC 时明确 `skipped`，而 Everything 不可用时的安全回退测试仍执行通过。
- 真实 Windows 鼠标验收（2026-09-22）已在隔离 Player 中取得有效 DragHandler 证据：日志记录 `Handoff Walking/... -> DragResponse/...`、`[DragHandoff] walking-to-drag accepted`、`[DragHandler] 拖动已启动`、`[DragHandler] 抛掷: (12, -6)`、DragResponse release 和随后 `[DesktopPet] 落地`。第二次鼠标拖拽也成功取得 DragResponse 并落地，但当时日志为 `action-to-drag accepted: no-active-input`，没有证明动作正在播放时被中断；动作中断场景仍需在动作租约明确活跃的窗口内单独复测。
- 新鲜隔离 Player `Build/phaseb-validation-20260921-011750/DesktopPet.exe` 观察到 walking 期间的真实冲突日志：`Rejected DesktopPhysics/physics-update: active=Walking/walking-state#109`；同一时段 `DesktopState` 仍为 `Walking` 且 `velocity=(1,0)`，证明 lease 冲突不阻塞 PhysicsRoot。
- 同一 Player 的生命周期脚本验证了 `Idle → Walking → Paused → Idle`，暂停期间桌面快照 version 冻结，退出前存在脱敏 `life-state`/`life-timeline` marker；退出日志包含 `[EmbodiedSafeRecovery] recovered: test-exit` 和 Renderer test-exit cleanup，未出现 `NullReferenceException`、`AssertionException` 或 event rejected。
- 新鲜隔离 Player `Build/walking-drag-handoff-validation/DesktopPet.exe` 直接在 `Walking` 状态跨越模拟拖拽阈值，日志记录 `Handoff Walking/walk-pose -> DragResponse/drag-response`、拖拽启动、generated-motion 冲突拒绝、抛掷、释放、落地和 `recovered: test-exit`；驱动全程使用临时 `FU_XUAN_DATA` 与 `.test_mode`，不等同于真实 OS 鼠标输入。
- 新鲜隔离 Player 使用 `external_Hiyori_Hiyori_m02` 完成认证动作 handoff：先在 walking 状态被稳定静止门禁拒绝且未出现 `started`，随后停止 walking 后同一实例成功取得 `certified-motion` lease/admission，动作自然完成并记录 pose restored、admission released 和 cleanup。独立退出驱动还验证动作中 `@@test:quit` 的准入取消、租约释放和 test-exit recovery；临时 Player 构建目录不固化在 truth 文档中。

## 明确未完成

- 当前全局单租约仍比资源模型保守；尚未启用面部与移动等非冲突层并行。
- `desktop-physics` 已不再是 `LegacyUnmanaged`，`drag-response` 也已登记为 `InputLeaseOnly`；桌面 `PhysicsRoot` 仍与 Live2D BodyRoot/Cubism 参数状态分离，拖拽位置写入仍由 `DesktopPet` / `DragHandler` 负责。
- `Live2DRenderer` 的行走与桌面物理租约已完成代码级、EditMode 和新鲜 Player 生命周期验证；walking admission 现按渲染帧只执行一次，LateUpdate/空闲写入复用同一准入结果，阻塞时记录 `WalkingBlockedByInput` 并在后续帧重试，以避免桌面状态继续移动而模型姿态误入固定。真实 DragHandler 的模拟拖拽、释放、落地与 walking handoff 已完成隔离 Player 验证。renderer 级停止淡出时间推进、renderer disable/destroy/rebuild、overlay RT/camera rebuild、Windows application pause callback 顺序、Windows 关机/注销、真实 OS 鼠标拖拽，以及完整 action handoff 仍未独立覆盖。参数级写入仍是内部硬编码路径并经 `ParameterCommitBridge` 提交。
- 新鲜隔离 Player `drag_response_player_drive.cjs` 已验证 Walking→DragResponse、生成动作冲突拒绝、释放→抛掷→落地，以及 LegacyAction `stretch` 活跃期间被拖拽中断：日志包含 `actionLocked=True`、`action-to-drag accepted: action-interrupted`、旧动作租约释放、DragResponse 取得、拖拽释放、落地和退出安全恢复；动作不会从原时间点继续。该证据使用临时 `FU_XUAN_DATA` 与 `.test_mode`，不等同于真实 OS 鼠标。
