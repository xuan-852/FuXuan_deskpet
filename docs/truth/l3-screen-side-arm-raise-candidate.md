# L3 画面侧单臂上抬候选技能

> **状态**：四层证据已通过（2026-09-16 双模型一致复核）；首个认证记录已注册；仍不可由 LLM 或运行时调用。

`CandidateSkillCatalog.ScreenSideArmRaise` 固定了首个候选技能：`screen_side_arm_raise`。

- 语义边界：仅“已取证画面侧单臂上抬再回落”，不是挥手、问候或人体左右臂命名；
- 前置：静止；时长 2.4 秒；资源为 `RightArm`（这是资源槽名，不是人体左右结论）；
- 已关联机械/视觉证据：`Param94-dynamic-2026-09-16`；
- 语义与时序自然度已由独立双模型离线复核一致（见下节）。

隔离 EditMode 测试通过（failed=0）：移除任一四层证据引用会使候选不能进入复核。该测试不改变候选的 `Candidate` 状态，也不执行 Live2D 参数。

候选证据现以结构化状态保存，不能再靠带有 `pending` 字样的字符串推断状态：机械、视觉、语义和自然度目前均为 `Supporting`。`IsReadyForCertificationReview()` 只表示四层资料都已具备可追踪引用；`HasPassedAllEvidenceLayers()` 必须四层均为 `Passed` 才会成立。当前候选后者为 false，且认证注册表仍额外要求自然度门禁和版本证据。

本次隔离 EditMode 测试还验证了：支撑证据不会被当作 `Passed`。

为使后续视觉评审可复核，`scripts/live2d-probe/build_candidate_review_packet.cjs` 会只在包含 `.test_mode` 的隔离采集根目录内，把按时间排序的 PNG 序列固化成 `candidate-review-packet-<skillId>.json`。该包记录顺序、基线/中间/复位阶段、文件大小和每帧 SHA-256，以及整包指纹；它不上传图片、不调用模型、不改变候选或认证状态。已对 `fuxuan_param94_baseline_20260916` 的 14 帧序列生成包，整包 SHA-256 为 `b7045039e5b9edf25e4431dccf705bb6479020252f0a45e72bf0984acdade091`。

若进行后续离线视觉评审，结果必须以 `CandidateReviewAssessment` 绑定上述整包指纹，并同时提供语义结论、自然度结论、评审者与模型归属。评审通过仍只可作为候选的证据输入，不能自动登记为 `Certified`。

## DeepSeek 离线复核（2026-09-16）

在用户明确授权后，`review_candidate_packet_deepseek.cjs` 先逐帧验证 14 个 SHA-256，再从固定序列选取第 0、3、6、9、12 与复位帧共 6 张模型截图发送给 `deepseek-v4-flash`；无桌面内容、用户内容或模型参数被外发。响应已缓存于隔离目录，使用量为 prompt 1390、completion 115、total 1505 tokens。

模型返回：语义边界 `supported`；观察到“画面右侧手臂从自然下垂逐渐抬起至约水平，随后回落”；序列连续、复位稳定、无明显视觉故障、不低于步行基线，自然度 85/100、置信度 high。主代理复核后当时只将语义与自然度证据记录为 `Supporting`，原因是缺少第二模型交叉判。

## GLM 交叉复核与四层提升（2026-09-16）

- `glm-4.6v-flash` 复核同一包时连续返回 HTTP 429（“该模型当前访问量过大”）；按用户明确授权，改用同 Key 付费档 `glm-4.5v` 作为 GLM 副评替代。脚本 `review_candidate_packet_glm.cjs` 与 DeepSeek 版使用同 6 帧、同提示词、同包指纹校验，缓存于隔离数据根 `cloud_review/screen_side_arm_raise-glm-301c9d1d9f7d….json`，单次调用 1548 tokens。
- GLM 返回：语义边界 `supported`；观察“画面右侧单臂从基线向外侧抬起后平滑回落，无重复摆动或挥手特征”；序列连续、复位稳定、无视觉故障、不低于步行基线，自然度 92/100、置信度 high（复核 ID `glm-packet-301c9d1d9f7d-2026-09-16`）。
- 两模型在语义边界、连续性、复位稳定性、视觉故障、步行基线五个字段上完全一致，无分歧需仲裁；自然度 85/92 均高于门槛 75。按 [首版默认阈值](../decisions/default-operational-thresholds.md)，主代理据此裁决成立。
- 四层证据随即提升为 `Passed`：机械/视觉依据 `Param94-dynamic-2026-09-16`（三轮写入/读回/复位 + 保守动态扫动 + 组合帧人工审查），语义/自然度依据 `packet-b7045039-dual-deepseek-glm-2026-09-16`（双模型一致复核）。
- `ScreenSideArmRaiseCertification.CreateRecord()` 据此生成首个生产认证记录：自然度取双模型较低分 85；版本证据为 `fuxuan-moc3-b4ddf3fbd6cd7f3e6eab7e82032548cd2feb3efe296bf3d63e20b97bfcab0ed4` 与 `fuxuan-map-src-2f80682ce6f798e4cc6d8d7122bbc165393d86d16c6bddd489b0e7118f3f4e10`。记录已可被 `CertifiedSkillRegistry.TryRegister` 接受（隔离 EditMode `FirstSkillCertificationTests` 通过，failed=0）。
- 本节不改变运行时行为：协调器与注册表尚未接入生产准入，`LLM` 依旧没有任何身体控制工具；真实执行该技能还需后续运行时准入任务包与执行层实现。
