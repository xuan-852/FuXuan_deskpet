# L3 不可执行候选的 AI 暴露修正

> **验证日期**：2026-09-18

`screen_side_arm_raise` 具有隔离测试执行器和认证记录，但当前生产 `PlayCertifiedMotion` 只加载 `CertifiedMotionLibrary` 中、且具备已绑定曲线哈希的条目。该候选没有生产曲线资产，因此不能由生产执行器播放。

修正后，`ChatManager.BuildCertifiedBodySkillBoundary` 和 `RequestBodySkillTool` 均只枚举 `CertifiedMotionLibrary.LlmExposedEntries`；不再把 `screen_side_arm_raise` 提示或接受为 AI 可调用技能。它仍保留在认证准入和隔离测试中，以便后续完成正式曲线部署、运行时验证和人工签字。

隔离 EditMode 回归通过：228 total、227 passed、0 failed、1 ignored。专项断言确认候选仍可由准入注册表识别，但不在 LLM 提示词或工具描述中出现。
