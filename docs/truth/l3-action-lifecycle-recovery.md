# L3 动作生命周期与安全收束（最小集）

> **证据日期**：2026-09-17
> **任务包**：`l3-action-lifecycle-recovery-v1`（上位指导：[L3 具身技能认证](../guides/approved/l3-embodied-skill-certification.md) §3 生命周期与 C-L3-07）
> **范围**：具身写入还原记录（PoseState 最小集）+ 统一安全收束日志面；完整虚拟骨架 PoseState 属后续阶段。

## 已验证事实

`Assets/Scripts/Embodied/EmbodiedPoseState.cs`：

- 跟踪已认证技能执行器的参数写入（参数、最新值、还原基线）；`RestoreAll(apply)` 对每个待还原参数施加基线值并清空集合，**重复调用幂等**；空参数名拒绝。
- 它不是完整 PoseState：只覆盖具身执行器实际写入的参数（当前为 `Param94`）；Root/躯干/头脸等骨架节点状态仍由 L2 物理与既有系统管理。

`Live2DRenderer` 接入：

- 手势协程每次写入经 `_embodiedPoseState.RecordWrite("Param94", value, 0f)` 登记；
- 统一收束点 `FinishTestParam94Gesture` 顺序执行：姿势还原（`[EmbodiedSafeRecovery] pose-restored`）→ 释放输入租约 → 释放准入请求 → 解除动作移动锁；完成、取消、渲染器禁用、应用退出全部经此收束；
- `PrepareForTestExit` 追加统一恢复日志 `[EmbodiedSafeRecovery] recovered: test-exit`，与既有清理序列共同构成可审计的退出收束。

## 可复核证据（2026-09-17）

- `build.ps1 -RunTests`：EditMode 204 用例、203 passed、**failed=0**、1 ignored，含新增 `EmbodiedRecoveryTests` 4 项（记录还原幂等、多参数各自基线、空记录不回调、空参数名拒绝）；
- 隔离真机驱动（完整构建后 Player，`Assembly-CSharp.dll` 实测含新类型、mtime 01:46）：
  - `param94_gesture_drive.cjs`：`admitted` → `pose-restored: Param94 (candidate-test-completed)` → `released` → `recovered: test-exit`；
  - `param94_cancel_recovery_drive.cjs`：`admitted` → `pose-restored: Param94 (candidate-test-cancelled)` → `released` → 取消后行走恢复确认；
- 运行时视觉对照（收 AC-L3-04 运行时复现证据）：主代理亲自核对驱动采集帧——基线帧手臂自然下垂、峰值帧画面右侧手臂外展抬起约水平、复位帧回到基线，与候选评审包描述一致。序列绝对像素指标被运行时快照叠加层干扰，不作数值对比，以帧审查为准。

## 已知工具问题（不属本任务包修复范围）

- `build.ps1` 完整构建门禁以 `DesktopPet.exe` 时间戳判定产物新鲜度，但引导器 exe 在增量构建中不重写（托管代码在 `DesktopPet_Data/Managed/Assembly-CSharp.dll`），导致误报「构建未产出本次 exe」；建议后续任务改为校验 `Assembly-CSharp.dll` 时间戳与内容指纹。
- 新增 `.cs` 文件后首次完整构建偶发 Bee 增量 DAG 陈旧（CS0246），第二次运行自愈；如连续失败按构建工作流使用 `-CleanBeeCache`。

## 未完成项

- 完整虚拟骨架 `PoseState`（全部骨架节点、表情、附属模块、资源占用镜像）；
- 协调器独立超时计时器（当前由执行器生命周期兜底）；
- 生产 LLM/主动行为开放身体技能（独立任务包）。
