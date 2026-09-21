# 统一生命控制闭环 Phase B-4：桌面身体状态边界

> **证据日期**：2026-09-21
> **任务包**：`unified-life-control-loop-phase-b-desktop-state-v1`  
> **状态**：已建立 PhysicsRoot 的只读快照；步行、桌面物理和真实 DragHandler 模拟拖拽已完成隔离 Player 验证，并保持与认证动作的单一全局租约边界；资源级并行和抢占仍未引入。

## 已验证事实

- `DesktopBodyState` / `DesktopBodySnapshot` 单独记录桌面 `X/Y`、速度、落地、暂停、拖拽、动作移动锁和地面任务；其类型中没有 Live2D 参数 ID 或参数值。
- `DesktopBodyMode` 按拖拽、动作移动锁、暂停、空中、行走、静止的优先级提供可复核的当前语义状态。
- `DesktopPet` 在每次向渲染器发送普通或拖拽帧状态前，先发布当前 `PhysicsRoot` 快照，并通过 `DesktopBodySnapshot` 只读公开给后续命令仲裁层。
- 原 `IPetRenderer.OnPetUpdate` 接口、桌面位置计算和 Live2D 参数写入均未改变；该边界不创建新的参数写入路径。

## 验证证据

- `build.ps1 -Quick`：通过，输出 `[OK] Build succeeded!`。
- `build.ps1 -RunTests`：新鲜结果为 `total=287`、`passed=283`、`failed=3`、`skipped=1`；LifeState 迟到中断断言已通过，剩余 3 个失败均为本机缺少 `es.exe` 的 Everything 环境依赖，不能记为完整 EditMode 全绿。
- 使用全新隔离输出目录 `Build/phaseb-validation-20260921-011750/DesktopPet.exe` 完成 Player 生命周期验收。初始状态为 `Idle`（version 104）；强制行走后为 `Walking`、`velocityX=1`（version 141）；`@@sim:pause:0` 后为 `Paused`、`paused=true`（version 166），暂停期间版本冻结；`@@sim:resume` 后恢复 `Idle`、`paused=false`（version 168）。
- 同一 Player 日志观察到 walking 租约活动时 `desktop-physics` 被拒绝，但 PhysicsRoot 仍继续推进：`Rejected DesktopPhysics/physics-update: active=Walking/walking-state#109`，随后仍有 `DesktopState ... mode=Walking ... velocity=(1,0)`。
- 退出前查询保留了脱敏的 `life-state`/`life-timeline` marker；退出日志出现 `[EmbodiedSafeRecovery] recovered: test-exit` 与 Renderer test-exit cleanup，且未出现 `NullReferenceException`、`AssertionException` 或 event rejected。
- 真实 `DragHandler` 隔离 Player 驱动完成模拟拖拽的 admission、拖拽中状态、generated-motion 冲突拒绝、抛掷速度、落地和 walking handoff；退出日志出现 test-exit safe recovery，未发现异常日志。该证据使用测试模式 `@@sim:drag`，不等同于真实 OS 鼠标输入。
- 新鲜隔离 Player 完成同实例 action handoff：walking 中认证动作被稳定静止门禁拒绝，发送 `@@sim:walk:stop` 后恢复静止并成功启动 `external_Hiyori_Hiyori_m06`；动作自然完成后 pose、准入、租约和 cleanup 均收束。独立退出驱动验证动作中 test-exit 的取消与安全恢复；临时 Player 构建目录不固化在 truth 文档中。

## 明确未完成

- 仍需单独设计并验证物理命令、拖拽优先级、落地恢复和与认证动作的双向仲裁；renderer disable/destroy/rebuild、overlay RT/camera rebuild、Windows application pause callback 顺序、Windows 关机/注销、真实 OS 鼠标拖拽、完整认证动作双向 handoff 仍未完成。
- `drag-response` 仍未迁移，资源级并行或抢占仍未引入；不能因本阶段步行/物理租约和快照证据宣称“身体控制已统一”。
