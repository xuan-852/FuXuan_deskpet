# L3 Hiyori m06 候选认证

`external_Hiyori_Hiyori_m06` 是一条 5.37 秒的外部 `LegacyCandidate`。它在独立 `Live2DProbe.exe` 中以隔离数据根回放 18 帧：峰值像素差 8.625、相邻帧最大差异 7.577、复位差 0、`resetStable=true`。主代理审看关键帧后，将语义边界限定为“头部侧倾并伴随眨眼、视线与表情变化，随后回到基线；不是手臂动作或位移动作”。

冻结评审包 SHA-256 为 `1c5b4f6edb705658b1648a3b5a1b2de5cce5d764183d47b21637ca0cb1b29c74`。经用户明确授权，仅将六张隔离模型帧和语义边界分别交给 DeepSeek `deepseek-v4-flash` 与 GLM `glm-4.5v`：两者均判定 `supported`、时序连续、复位稳定、无明显视觉故障、达到步行基线且高置信；自然度分别为 82 与 92。最保守分数 82 满足 `NaturalnessGate.MinimumScore`。

该候选已作为数据层认证技能登记至 `CertifiedMotionLibrary`，资源为 `Face|Body`。原始/派生曲线与评审缓存均保留在隔离或本地许可目录，不进入版本控制；本次不改参数映射、不启用自主行为，也不开放原始参数控制。`build.ps1 -Quick` 和 `build.ps1 -RunTests` 均通过；后者为 EditMode 220 total、219 passed、0 failed、1 ignored。
