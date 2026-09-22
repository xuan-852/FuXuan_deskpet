# 统一生命控制闭环 Phase B-4：桌面身体状态边界

> **证据日期**：2026-09-22
> **任务包**：`unified-life-control-loop-phase-b-desktop-state-v1`  
> **状态**：已建立 PhysicsRoot 的只读快照；步行、桌面物理、拖拽接管、动作中断和落地恢复已完成隔离 Player 验证，并保持与动作输入的单一全局租约边界；资源级并行和抢占仍未引入。

## 已验证事实

- `DesktopBodyState` / `DesktopBodySnapshot` 单独记录桌面 `X/Y`、速度、落地、暂停、拖拽、动作移动锁和地面任务；其类型中没有 Live2D 参数 ID 或参数值。
- `DesktopBodyMode` 按拖拽、动作移动锁、暂停、空中、行走、静止的优先级提供可复核的当前语义状态。
- `DesktopPet` 在每次向渲染器发送普通或拖拽帧状态前，先发布当前 `PhysicsRoot` 快照，并通过 `DesktopBodySnapshot` 只读公开给后续命令仲裁层。
- 原 `IPetRenderer.OnPetUpdate` 接口、桌面位置计算和 Live2D 参数写入均未改变；该边界不创建新的参数写入路径。

- 直接交互事件由 `DragHandler` 在点击、成功拖拽开始、正常结束和明确中止时发出；`AttentionReactionAdapter` 仅消费这些事件更新注意力与生命观测，不改变 PhysicsRoot、拖拽租约或 Live2D 参数写入边界。

- `build.ps1 -Quick`：通过，输出 `[OK] Build succeeded!`。
- `build.ps1 -RunTests`：本轮新鲜结果为 `failed=0`；Everything 集成测试在本机缺少 `es.exe`/IPC 时明确 `skipped`，其余 EditMode 测试通过。
- 使用全新隔离输出目录 `Build/phaseb-validation-20260921-011750/DesktopPet.exe` 完成 Player 生命周期验收。初始状态为 `Idle`（version 104）；强制行走后为 `Walking`、`velocityX=1`（version 141）；`@@sim:pause:0` 后为 `Paused`、`paused=true`（version 166），暂停期间版本冻结；`@@sim:resume` 后恢复 `Idle`、`paused=false`（version 168）。
- 同一 Player 日志观察到 walking 租约活动时 `desktop-physics` 被拒绝，但 PhysicsRoot 仍继续推进：`Rejected DesktopPhysics/physics-update: active=Walking/walking-state#109`，随后仍有 `DesktopState ... mode=Walking ... velocity=(1,0)`。
- 退出前查询保留了脱敏的 `life-state`/`life-timeline` marker；退出日志出现 `[EmbodiedSafeRecovery] recovered: test-exit` 与 Renderer test-exit cleanup，且未出现 `NullReferenceException`、`AssertionException` 或 event rejected。
- 使用全新隔离输出目录 `Build/DesktopPet.exe` 完成 Player 拖拽闭环验收。驱动观察到 walking→drag、拖拽启动、generated-motion 冲突拒绝、释放、抛掷、落地、重新行走和退出安全恢复；同一驱动使用 `@@sim:drag`，不等同真实 OS 鼠标输入。
- 真实 Windows 鼠标验收（2026-09-22）另取得 walking→DragResponse→throw→land 的有效日志证据：walking handoff、真实拖动启动、抛掷速度 `(12, -6)`、DragResponse release 和落地均出现。第二次拖拽同样落地，但因 `action-to-drag accepted: no-active-input`，不计入动作中断验收；动作活跃窗口内的真实鼠标中断仍待专项复测。
- 新鲜隔离 Player `drag_response_player_drive.cjs` 进一步完成 LegacyAction `stretch` 活跃期间动作中断：`DesktopState ... actionLocked=True` 后，日志出现 `Released LegacyAction/...: renderer-stop-all`、`action-to-drag accepted: action-interrupted`、拖拽启动、抛掷、落地和后续 `actionLocked=False` 恢复；不恢复旧动作播放时间。
- 使用全新隔离 Player 完成星辉（idle action 4）动作中断回归：动作租约 `LegacyAction/idle:4` 活跃期间接收拖拽，日志出现 `action-to-drag accepted: action-interrupted`、DragResponse、释放、抛掷和落地；拖拽期间统一清理星辉右臂/手部图层写入，未再由星辉动作继续覆盖拖拽姿态。该证据使用 `@@sim:*`，不等同真实 OS 鼠标动作中断。

## 明确未完成

- 仍需单独设计并验证物理命令、renderer disable/destroy/rebuild、overlay RT/camera rebuild、Windows application pause callback 顺序、Windows 关机/注销、真实 OS 鼠标拖拽，以及完整认证动作双向 handoff。代码层拖拽优先级、动作中断、落地恢复和各 teardown 清理已补齐并通过隔离 Player 模拟证据，但资源级并行或抢占仍未引入。
- `drag-response` 仍未迁移，资源级并行或抢占仍未引入；不能因本阶段步行/物理租约和快照证据宣称“身体控制已统一”。
