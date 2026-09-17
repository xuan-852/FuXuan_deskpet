# L3 协调器优先级抢占仲裁

> **证据日期**：2026-09-18  
> **任务包**：`l3-coordinator-priority-preemption-v1`  
> **状态**：协调器优先级仲裁已验证；延迟排队与旧动作入口迁移仍未实现。

`EmbodiedCoordinator.TryBegin` 在资源冲突时执行优先级仲裁：仅当请求优先级**严格高于**全部相交资源持有者时，先经正常取消路径（`Cancelled`，终态原因 `preempted`）释放它们再准入；任一持有者优先级不低于请求即维持 `resource-busy` 拒绝，不相交资源持有者不受影响。

- 生产准入 `EmbodiedRuntimeAdmission` 的请求固定 `Priority=0`，同优先级冲突仍拒绝、不被抢占，运行时行为不变。
- 抢占复用 `Finish` 单一释放路径，不产生重复释放或资源残留。

## 可复核证据

- `build.ps1 -RunTests`：EditMode **224 total、223 passed、failed=0、1 ignored**。
- 新增 `CertifiedSkillFoundationTests` 三个用例：严格更高优先级经取消路径抢占冲突持有者；同级或更低优先级冲突仍按资源忙拒绝且原请求保持 `Executing`；抢占只影响相交资源持有者（并行 `Face` 请求不受波及）。

## 明确未完成项

- 延迟排队（资源释放后自动启动排队请求，生命周期中的 `Queued` 状态）仍属后续任务。
- 完整 `PoseState` 与旧动作入口（`play_action`、`set_expression`、`IdleActionScheduler`、`MotionAgent`）迁移到 `ActionRequest`/协调器仍是后续任务。
