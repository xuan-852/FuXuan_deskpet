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

## 未完成项

- 完整虚拟骨架 `PoseState`（全部骨架节点、表情、附属模块、资源占用镜像）；
- 协调器独立超时计时器（当前由执行器生命周期兜底）；
- 生产 LLM/主动行为开放身体技能（独立任务包）。
