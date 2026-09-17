# L3 姿势状态快照运行时接线

认证动作在 `Live2DRenderer.PlayCertifiedMotion` 通过准入后登记 `EmbodiedPoseState.BeginAction`；结束收束先还原已写入参数，再按真实终态调用 `FinishAction`。`certified-motion-completed` 记为 `Completed`，取消路径记为 `Cancelled`，准入超时保持 `TimedOut`。

`EmbodiedRuntimeAdmission.CancelSkill` 通过协调器的取消分支释放资源并保留取消原因，不再把取消伪装为完成。`Live2DRenderer.CertifiedPoseSnapshot` 仅返回版本化快照，不能写入 Live2D 参数。

`RuntimeAdmissionTests.取消准入保留取消终态并释放资源` 覆盖取消终态、原因和资源再准入；完整 `build.ps1 -RunTests` 在隔离数据目录通过，EditMode failed=0。运行时视觉连续性仍需在真实 Live2D 窗口人工复核。
