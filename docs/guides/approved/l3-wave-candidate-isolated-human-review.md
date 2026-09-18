# L3 招手候选的隔离人工验收

> 状态：已批准（用户于 2026-09-18 明确要求开始人工验证）
> 上位决策：[产品方向基线](../../decisions/2026-09-15-product-direction-baseline.md) §L3、[文档治理](../../decisions/documentation-governance.md)、[通用 Live2D 参数能力探测器](generic-live2d-capability-probe.md)

## 目标与非目标

- **FR-WAVE-01**：提供只在 `.test_mode` 下可触发的完整招手候选，按“准备、抬臂、面向观者的手型、至少两次腕部往返、友好面部、回落”播放。
- **FR-WAVE-02**：候选必须通过静止门禁、输入租约、姿势还原、退出收束，并在真实可见桌宠窗口中由用户完整观看。
- **FR-WAVE-03**：记录用户结论和隔离运行证据；未获“自然且可辨识”为结论前不认证。

非目标：不新增认证技能、不写生产数据、不使用外部曲线/云端视觉、不更新正式参数映射、不向 LLM、聊天工具或空闲行为暴露候选。

## 接口与状态

- 唯一入口：测试收件箱 `@@sim:gesture:wave-candidate`；仅 `ChatManager.IsTestMode` 可接受。
- 取消入口：`@@sim:gesture:wave-candidate:cancel`；`@@test:quit` 必须同样收束。
- 状态：`Idle → Admitted → Playing → Restored → Released`；静止门禁、租约冲突、重复启动均拒绝并保留原状态。
- 涉及的候选通道仅为 `Param94`、`Param99`、`Param92`、`ParamEyeLSmile`、`ParamEyeRSmile`、`ParamMouthForm`；这些名称不构成正式语义映射。

## 硬约束

- **C-WAVE-01**：只允许隔离 `FU_XUAN_DATA` 根且根目录存在 `.test_mode`。
- **C-WAVE-02**：每帧参数写入必须经 `EmbodiedPoseState`，结束、取消、退出时恢复全部被记录参数并释放输入租约及移动锁。
- **C-WAVE-03**：候选不得复用 `CertifiedMotionLibrary`、`request_body_skill` 或任何 LLM 可见描述。
- **C-WAVE-04**：腕部振幅须保守（不超过本次运行时已探测范围 `[-20,20]`），且至少包含两次方向反转；禁止把截图或参数变化直接称为招手成功。

## 验收

| ID | 证据 | 标准 |
| --- | --- | --- |
| AC-WAVE-01 | `build.ps1 -Quick` 和 EditMode | 编译、既有回归通过。 |
| AC-WAVE-02 | 隔离 Player 日志与截图 | 静止准入、完整播放、姿势还原、租约释放、退出无残留。 |
| AC-WAVE-03 | 用户在可见窗口的完整观看 | 明确给出“自然且可辨识 / 可接受但需改进 / 不自然”之一。 |

## 实施授权边界

允许修改 `Live2DRenderer.cs`、`RuntimeInputSimulator.cs`、对应 EditMode 测试、`scripts/test/` 和验证后 `docs/truth/`、`docs/modules/action-agent.md`。禁止修改正式映射、工具系统、聊天提示词、认证库、生产数据根、安装包和 `docs/decisions/`。
