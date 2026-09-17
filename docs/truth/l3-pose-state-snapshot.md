# L3 姿势状态快照基础

> **证据日期**：2026-09-17
> **任务包**：`l3-pose-state-snapshot-v1`

`EmbodiedPoseState` 已从待还原参数记录扩展为版本化只读快照：当前认证技能、声明资源、动作状态与待还原参数 ID 均可由 `CaptureSnapshot()` 查询。它不提供参数写入入口，也不会推断完整骨架、表情或移动状态。

`EmbodiedRecoveryTests.快照记录动作资源与待还原参数` 验证开始动作、记录写入、恢复和完成后的快照状态与版本推进。`build.ps1 -RunTests` 通过：EditMode **218 total、217 passed、failed=0、1 ignored**。

认证动作执行器已在准入成功后调用 `BeginAction`，并在恢复姿势、释放输入租约后调用 `FinishAction`。完成、取消、超时分别保持 `Completed`、`Cancelled`、`TimedOut` 终态；只读入口为 `Live2DRenderer.CertifiedPoseSnapshot`，不提供任何参数写入能力。详见 [L3 姿势状态快照运行时接线](l3-pose-state-runtime-wiring.md)。
