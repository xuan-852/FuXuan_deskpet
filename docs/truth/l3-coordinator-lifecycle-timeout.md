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

- `EmbodiedRuntimeAdmission` 与 `Live2DRenderer` 尚未在帧循环调用 `ExpireDue`；当前执行器仍以自身生命周期收束为超时兜底。
- 完整 `PoseState`、优先级抢占队列与所有旧动作入口迁移仍是后续任务。
