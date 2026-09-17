# L3 运行时超时安全收束接线

> **证据日期**：2026-09-17
> **任务包**：`l3-runtime-timeout-safe-recovery-v1`

认证动作活跃时，`Live2DRenderer.Update` 调用 `EmbodiedRuntimeAdmission.ExpireDue(System.DateTime.UtcNow)`。到期请求被协调器标记为 `TimedOut/timeout` 并释放准入资源；渲染器随即调用既有 `FinishCertifiedMotion("certified-motion-timeout")`，复用姿势还原、输入租约释放、移动锁解除与清理日志。

`RuntimeAdmissionTests.到期准入会标记超时并释放资源` 已验证：到期请求进入超时终态，资源随后可再次准入。`build.ps1 -Quick` 与 `build.ps1 -RunTests` 均通过；EditMode **217 total、216 passed、failed=0、1 ignored**。

本任务不迁移旧动作路径、不开放主动行为；完整帧级视觉超时复现和完整 PoseState 仍属后续任务。
