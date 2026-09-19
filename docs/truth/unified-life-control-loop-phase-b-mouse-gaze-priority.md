# 统一生命控制闭环 Phase B-3：鼠标注视优先级门控

> **证据日期**：2026-09-19
> **任务包**：`unified-life-control-loop-phase-b-mouse-gaze-priority-v1`  
> **状态**：鼠标注视已成为受控低优先级叠加层；未引入资源级并行。

## 已验证事实

- `Live2DInputCoordinator.CanApplyLowPriorityOverlay` 仅在没有活动租约时为真。
- `Live2DRenderer` 的鼠标眼球覆盖与回中写入同时受该门控和动作锁控制；表情、旧动作、生成动作或认证动作持有租约时，鼠标注视不能在 LateUpdate 末尾覆盖当前拥有者的眼球参数。
- 租约释放后，注视从现有平滑状态恢复，不新增原始参数入口或云端依赖。
- `mouse-gaze` 在写入者清册中已由 `LegacyUnmanaged` 升为 `InputLeaseOnly`：它服从输入租约，但本身不长期占用全局租约，避免阻塞表情和动作。

## 验证证据

- `build.ps1 -Quick`：2026-09-19 通过。
- `build.ps1 -RunTests`：本轮新生成结果为 `total=249`、`passed=248`、`failed=0`、`ignored=1`；其中包含低优先级覆盖层门控与表情/输入租约生命周期回归测试。
- 本轮未重新执行 Player 人工注视过渡审核；该测试验证仲裁条件，不替代用户可见注视过渡审核。

## 明确未完成

- 此处不是多资源混合执行；全局单租约仍保持保守互斥。
- 步行、桌面物理和拖拽仍是 `LegacyUnmanaged`。
- 该测试验证仲裁条件，不替代对用户可见注视过渡的后续 Player 人工审核。
