# L3 协调器生命周期与超时底座

> **证据日期**：2026-09-17
> **任务包**：`l3-coordinator-lifecycle-timeout-v1`
> **范围**：认证技能协调器的请求所有权、终态与确定性超时操作；不接入渲染器运行时调度。

## 已验证事实

`EmbodiedActionRequest` 在协调器接受后带有单调 `RequestId`、UTC 开始时间、当前状态和终态原因。`EmbodiedCoordinator` 仅跟踪自己接受的请求；完成、取消或超时只释放该请求实际持有的资源。

`ExpireDue(nowUtc)` 是确定性超时扫描：到期请求进入 `TimedOut/timeout`，不相交资源的并行请求保持执行。`Cancel(request, reason)` 进入 `Cancelled` 并保留原因；重复终结不会再次释放资源。

## 可复核证据

- `build.ps1 -Quick` 通过。
- `build.ps1 -RunTests`：EditMode **216 total、215 passed、failed=0、1 ignored**；`CertifiedSkillFoundationTests.超时和取消只释放所属请求并记录终态` 覆盖并行资源、到期、取消和终态原因。

## 明确未完成项

- 2026-09-25 现状补充：`Live2DRenderer.Update()` 已在认证动作活动时调用 `EmbodiedRuntimeAdmission.ExpireDue`。隔离 Player 通过测试模式将当前请求推进到超时点，验证 Renderer 收束、`BehaviorCoordinator.Expired` 与 UI 回执；协调器层取消失败记录 `RecoveryFailed`。旧输入租约已由适配层登记到行为执行账本；原子 walking→drag 交接中，旧 walking 仍写自己的 `Cancelled` 终态，但若新 drag 已是当前关联，不再向 `LifeState` 发布迟到的旧动作终态。EditMode 319 total、315 passed、0 failed、4 skipped；隔离 Player 还确认拖拽进行中 `LifeState=Active`。真实帧停滞和设备故障仍需另行验证。
- 完整 `PoseState`、优先级抢占队列、旧写入者的资源级认证仲裁仍是后续任务。
