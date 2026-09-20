# 统一生命控制闭环 Phase B-4：桌面身体状态边界

> **证据日期**：2026-09-18  
> **任务包**：`unified-life-control-loop-phase-b-desktop-state-v1`  
> **状态**：已建立 PhysicsRoot 的只读快照；尚未把物理、行走或拖拽迁入认证动作协调器。

## 已验证事实

- `DesktopBodyState` / `DesktopBodySnapshot` 单独记录桌面 `X/Y`、速度、落地、暂停、拖拽、动作移动锁和地面任务；其类型中没有 Live2D 参数 ID 或参数值。
- `DesktopBodyMode` 按拖拽、动作移动锁、暂停、空中、行走、静止的优先级提供可复核的当前语义状态。
- `DesktopPet` 在每次向渲染器发送普通或拖拽帧状态前，先发布当前 `PhysicsRoot` 快照，并通过 `DesktopBodySnapshot` 只读公开给后续命令仲裁层。
- 原 `IPetRenderer.OnPetUpdate` 接口、桌面位置计算和 Live2D 参数写入均未改变；该边界不创建新的参数写入路径。

## 验证证据

- `build.ps1 -Quick`：测试执行至结果保存，日志中无 C# 编译错误。
- `build.ps1 -RunTests`：最新 EditMode 结果为 `total=235`、`passed=234`、`failed=0`、`ignored=1`。
- `EmbodiedRecoveryTests.桌面身体快照与Live2D参数状态分离且模式可复核` 覆盖位置/速度/任务快照、拖拽优先级和动作移动锁优先级。
- 第三阶段隔离 Player 使用当前源码构建的 `.artifacts/player-timeline/DesktopPet.exe` 验证了 `@@sim:life-timeline` 查询边界：时间线快照为 4 条有序记录，查询后各观测计数未回退，临时数据根目录退出后清理，生产记忆文件未变化。

## 明确未完成

- `walk-pose`、`desktop-physics` 与 `drag-response` 在 `BodyWriterInventory` 中仍是 `LegacyUnmanaged`；它们尚未取得输入租约，也没有资源级并行仲裁。
- 快照目前是观察边界而非命令入口。拖拽位置写入、地面任务和物理步进仍由既有 `DesktopPet` / `DragHandler` 路径执行。
- 还需单独设计并验证物理命令、拖拽优先级、落地恢复和与认证动作的双向仲裁，不能因本阶段快照存在而宣称“身体控制已统一”。
