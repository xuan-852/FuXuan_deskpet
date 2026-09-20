# L3 动作生命周期与安全收束（最小集）

> **证据日期**：2026-09-19
> **任务包**：`l3-action-lifecycle-recovery-v1`（上位指导：[L3 具身技能认证](../guides/approved/l3-embodied-skill-certification.md) §3 生命周期与 C-L3-07）
> **范围**：具身写入还原记录（PoseState 最小集）+ 统一安全收束日志面；完整虚拟骨架 PoseState 属后续阶段。

## 已验证事实

`Assets/Scripts/Embodied/EmbodiedPoseState.cs`：

- 跟踪已认证技能执行器的参数写入（参数、最新值、还原基线）；`RestoreAll(apply)` 对每个待还原参数施加基线值。回调异常时继续尝试其余参数，仅保留失败参数待下一次恢复；成功项立即移除，重复调用幂等；空参数名拒绝。
- 它不是完整 PoseState：只覆盖具身执行器实际写入的参数（当前为 `Param94`）；Root/躯干/头脸等骨架节点状态仍由 L2 物理与既有系统管理。

`Live2DRenderer` 接入：

- 手势协程每次写入经 `_embodiedPoseState.RecordWrite("Param94", value, 0f)` 登记；
- 统一收束点 `FinishTestParam94Gesture` 顺序执行：姿势还原（`[EmbodiedSafeRecovery] pose-restored`）→ 释放输入租约 → 释放准入请求 → 解除动作移动锁；完成、取消、渲染器禁用、应用退出全部经此收束；
- `PrepareForTestExit` 追加统一恢复日志 `[EmbodiedSafeRecovery] recovered: test-exit`，与既有清理序列共同构成可审计的退出收束。

## 可复核证据（2026-09-17）

- `build.ps1 -Quick`：2026-09-19 通过，`[OK] Build succeeded!`；随后独立重跑 `build.ps1 -RunTests`，新生成 EditMode 结果为 `total=249`、`passed=248`、`failed=0`、`ignored=1`。其中包含认证协调器拒绝终态、终态幂等、抢占、PoseState 异常恢复及表情/输入租约生命周期测试。
- 隔离真机驱动（完整构建后 Player，`Assembly-CSharp.dll` 实测含新类型、mtime 01:46）：
  - `param94_gesture_drive.cjs`：`admitted` → `pose-restored: Param94 (candidate-test-completed)` → `released` → `recovered: test-exit`；
  - `param94_cancel_recovery_drive.cjs`：`admitted` → `pose-restored: Param94 (candidate-test-cancelled)` → `released` → 取消后行走恢复确认；
- 运行时视觉对照（收 AC-L3-04 运行时复现证据）：主代理亲自核对驱动采集帧——基线帧手臂自然下垂、峰值帧画面右侧手臂外展抬起约水平、复位帧回到基线，与候选评审包描述一致。序列绝对像素指标被运行时快照叠加层干扰，不作数值对比，以帧审查为准。

## 本轮生命周期收束补充（2026-09-19）

- `EmbodiedCoordinator` 的所有可解释拒绝分支均写入 `Rejected` 和 `TerminalReason`；完成、取消、超时和抢占后的重复收束保持幂等，不重复释放资源。
- `EmbodiedPoseState.RestoreAll` 在恢复回调抛异常时继续处理其他参数，成功项立即清除，失败项保留并可重试；`FinishCertifiedMotion` 将停止协程、姿态恢复、终态记录、输入租约、准入和移动锁清理置于同一尽力收束路径。
- 生成动作调用方已统一使用 `try/finally` 释放 AI 控制锁与 `EndGeneratedMotion` 输入租约，覆盖工具、MotionAgent 的自主表情、普通动作和组合动作。

## 表情与 Renderer 停止网关补充（2026-09-19）

- `Live2DRenderer.StopAllActionsAndExpressions` 统一停止 Renderer 所拥有的表情与旧动作状态，并在停止表情时处理延迟释放、generation 和输入租约；`StopActionTool`、生成动作前置自检和测试命令均经由该网关。
- `PlayAction` 先申请 `LegacyAction` 全局租约，申请失败时不会再停止当前表情；申请成功后才以零淡出收束表情，避免被拒绝的旧动作破坏当前输入所有者。
- 隔离 Player `AC-EXPRESSION-LIFECYCLE` 与 `AC-INPUT-04` 已验证未知表情拒绝、停止后复用、表情与生成/旧动作双向冲突、旧动作完成释放和 `test-exit` 清理。该证据只覆盖表情/旧动作/生成动作的当前单全局租约生命周期，不代表完整骨架 PoseState、步行、物理、拖拽或 Renderer 重建路径已经统一。

- `build.ps1` 完整构建门禁以 `DesktopPet.exe` 时间戳判定产物新鲜度，但引导器 exe 在增量构建中不重写（托管代码在 `DesktopPet_Data/Managed/Assembly-CSharp.dll`），导致误报「构建未产出本次 exe」；建议后续任务改为校验 `Assembly-CSharp.dll` 时间戳与内容指纹。
- 新增 `.cs` 文件后首次完整构建偶发 Bee 增量 DAG 陈旧（CS0246），第二次运行自愈；如连续失败按构建工作流使用 `-CleanBeeCache`。

## Renderer 生命周期 SafeRecovery 补充（2026-09-19）

- `Live2DRenderer.SafeRecoverExternalActions(reason)` 是 Renderer 外部执行的幂等、尽力收束网关。`OnDisable`、`OnApplicationQuit`、`OnDestroy`（位于 `Live2DRenderer.OverlayRendering.cs`）和测试专用 `PrepareForTestExit` 均调用该网关；每个 Param94、Wave、Torso 候选与认证动作先独立尝试自身终止路径，再在 `finally` 中清理旧动作锁、协程句柄、动作移动锁、残留输入租约、运行时准入请求和待恢复姿态。一个恢复回调失败不会阻止后续资源清理，失败姿态项仍由 `EmbodiedPoseState` 保留以便重试。
- 隔离 Player 的 `test-exit` 证据已覆盖四条执行路径：Param94 候选、Wave 候选、Torso 候选和完整性校验通过的 `external_Hiyori_Hiyori_m06` 认证动作。四者均在活动期间收到 `@@test:quit` 后记录租约释放；认证动作另外记录姿态恢复、`EmbodiedRuntimeAdmission` 取消、`[CertifiedMotion] cleanup` 和 `[EmbodiedSafeRecovery] recovered: test-exit`。候选路径分别通过 `scripts/test/param94_exit_cleanup_drive.cjs` 与 `scripts/test/candidate_exit_cleanup_drive.cjs`，认证路径通过 `scripts/test/certified_motion_exit_cleanup_drive.cjs`。
- 这证明的是测试退出入口的跨层收束顺序，不证明 Windows 关机/注销、精确的 Unity 禁用与销毁回调顺序、Renderer 重建或生产退出时序。步行、桌面物理、拖拽、视线及其他未迁移写入者仍不属于统一认证协调范围；也不证明资源级并行执行或完整虚拟骨架 `PoseState`。

## 本轮候选招手生命周期补充（2026-09-20）

- `WaveCandidate` 仍是测试模式专用候选，不是认证技能或 LLM 动作；本轮只收敛其生命周期和恢复基线，没有修改招手幅度、正式参数映射、生产曲线或技能白名单。
- 候选启动时先捕获 `Param94`、`Param97`、`Param99`、`Param93`、头/腰、双眼微笑、嘴型及手部可见性伴随参数的当前模型值。`EmbodiedPoseState.RecordWrite` 只接受每个参数的首次基线，后续帧不会以 `0f` 覆盖真实进入姿态。
- 候选清理现在清空基线清册并统一释放输入租约、动作锁和桌面移动锁；取消、禁用、退出路径保持幂等。新增生命周期驱动 `scripts/test/wave_candidate_lifecycle_drive.cjs` 覆盖跨周期循环、取消后再次启动和 test-exit 收束。
- 本轮 `build.ps1 -Quick`、脚本语法检查和 `git diff --check` 已通过。真实 Player 生命周期驱动尚未在本轮重新执行，因此不把它写成已通过的运行时证据；Windows 关机/注销、Renderer 重建、精确 Unity 回调顺序仍未验证。


- 完整虚拟骨架 `PoseState`（全部骨架节点、表情、附属模块、资源占用镜像）；
- 协调器独立超时计时器（当前由执行器生命周期兜底）；
- 生产 LLM/主动行为开放身体技能（独立任务包）；
- Windows 关机/注销、Renderer 重建以及 `OnDisable`/`OnDestroy` 精确顺序的独立运行时验收；
- 步行、桌面物理、拖拽和视线等剩余写入者的统一资源仲裁。
