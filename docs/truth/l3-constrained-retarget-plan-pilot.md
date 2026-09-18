# L3 受约束重定向计划试点

> **验证日期**：2026-09-18  
> **范围**：已审计的本地官方 Hiyori `m07` 仅作为离线参考；产物不进入运行时。

## 已完成的证据链

1. `l3-hiyori-m07-reference-manifest.json` 绑定了本地来源、许可证证据、用途和源文件 SHA-256 `3f06cb70f5654eed3389978885e4dff151b6cf9e8d59248680addcd267ff793a`。
2. `extract_motion_features.cjs` 在带 `.test_mode` 的临时目录提取了 12 个只读特征通道。
3. `build_constrained_retarget_plan.cjs` 生成了 `l3-constrained-retarget-plan/v1`：它选择活动量最高的抽象手臂通道 `arm.right.upper`，记录抬起峰值相位 `0.403684`，并输出“单臂抬起—短暂停顿—回落”的受限语义边界。

离线断言确认计划状态为 `NeedsEvidence`、目标映射为 `unassigned`，且明确禁止曲线生成和 Live2D 目标参数 ID。计划中没有任何原始符玄参数值或播放资产。

## 不能由本试点证明的事项

- 参考动作的真实语义、画面侧与人体侧；
- 源模型到符玄模型的手臂映射、幅度、速度、加速度和 jerk 上限；
- 候选曲线的机械安全、视觉自然度、运行时恢复；
- 新认证动作或 LLM 可调用能力。

因此它是下一轮候选重建的输入，不是候选曲线，更不是“招手”或已认证技能。若继续，必须先在隔离 Probe 中为既有 `screen-side-arm-raise` 能力建立局部幅度/速度/jerk 证据，再人工审查候选曲线并走完 L3 四层认证与真人签字。
