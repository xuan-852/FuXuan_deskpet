# L3 躯干 Z 运行时连续性验证

> **验证日期**：2026-09-17  
> **状态**：测试闭环通过；候选仍为 `Supporting`，未认证、未注册、未向 AI 开放。

仅在隔离 `.test_mode` 下，`gesture:torso-z` 以 1.2 秒保守曲线执行 `0 → 0.75 → 0`。该测试路径使用输入租约、动作移动锁与姿态恢复，但不进入认证技能注册表。

- 正常执行：启动与 `torso-z-candidate-completed` 清理日志均出现。
- 行走门禁：桌宠行走时请求被 `rejected-static-gate` 拒绝。
- 取消恢复：中段取消产生 `torso-z-candidate-cancelled` 清理日志，随后强制向右走成功，证明未遗留动作锁。
- `build.ps1 -Quick` 与 `build.ps1 -RunTests` 通过；EditMode 0 失败。
