# 统一生命控制闭环 Phase B-2：空闲动作租约迁移

> **证据日期**：2026-09-19
> **任务包**：`unified-life-control-loop-phase-b-idle-action-lease-v1`  
> **状态**：空闲动作已接入注册输入租约并完成隔离 Player 验证；仍不是认证技能或资源级并行控制。

## 已验证事实

- `ForceIdleAction` 先收束先前的空闲动作与表情，再以 `idle-action` 写入者申请输入租约；通道被其他写入者占用时拒绝启动，不再无登记地继续写入。
- `ResetIdleAction` 释放空闲动作租约；认证动作和旧预设动作在申请自身租约前，会先收束当前空闲动作，避免交接后仍有旧空闲写入。
- `idle-action` 的资源声明覆盖身体、脸部、双臂和效果层，控制等级为 `InputLeaseOnly`。它并未被提升为认证技能，也不能供 LLM 以裸参数方式调用。
- `RuntimeInputSimulator` 新增 `.test_mode` 专用 `@@sim:idle-action:<1..9>`；该命令不会进入聊天或生产控制面。

## 验证证据

- `build.ps1 -Quick`：2026-09-19 通过，`[OK] Build succeeded!`。
- `build.ps1 -RunTests`：本轮新生成结果为 `total=238`、`passed=237`、`failed=0`、`ignored=1`。
- 本轮未重新执行隔离 Player 驱动；此前的 idle lease 驱动证据仍以 2026-09-18 记录为准。

## 明确未完成

- 步行、桌面物理、鼠标视线和拖拽仍是 `LegacyUnmanaged`。
- 当前仍使用单全局输入租约，尚未开放资源无冲突层并行或优先级抢占。
- 认证执行器、旧预设和空闲动作的统一状态快照/事件时间线尚待后续 Phase B/C。
