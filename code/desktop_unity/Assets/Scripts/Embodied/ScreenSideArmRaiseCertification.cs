using System;

// 首个生产认证技能记录。依据：候选包 b7045039… 的独立双模型离线复核一致
// （DeepSeek deepseek-v4-flash 85/100 与 GLM glm-4.5v 92/100，均 high 置信；
// glm-4.6v-flash 因限流按用户授权以 glm-4.5v 替代）。机械/视觉证据见
// docs/truth/l3-capability-census-mechanical.md 与候选真相文档。
// 该记录只进入 CertifiedSkillRegistry；运行时准入接线与 LLM 开放属于后续任务包。
public static class ScreenSideArmRaiseCertification
{
    public const string SkillId = "screen_side_arm_raise";
    public const string PacketSha256 = "b7045039e5b9edf25e4431dccf705bb6479020252f0a45e72bf0984acdade091";
    public const string DeepSeekReviewId = "deepseek-packet-4bad8ad105f6-2026-09-16";
    public const string GlmReviewId = "glm-packet-301c9d1d9f7d-2026-09-16";
    public const int DeepSeekNaturalnessScore = 85;
    public const int GlmNaturalnessScore = 92;
    public const string ModelVersion = "fuxuan-moc3-b4ddf3fbd6cd7f3e6eab7e82032548cd2feb3efe296bf3d63e20b97bfcab0ed4";
    public const string MappingVersion = "fuxuan-map-src-2f80682ce6f798e4cc6d8d7122bbc165393d86d16c6bddd489b0e7118f3f4e10";

    // 自然度取双模型中的较低分（保守）；两分均不得低于 NaturalnessGate.MinimumScore。
    public static SkillCertificationRecord CreateRecord()
    {
        return new SkillCertificationRecord
        {
            SkillId = SkillId,
            ModelVersion = ModelVersion,
            MappingVersion = MappingVersion,
            MechanicalPassed = true,
            VisualPassed = true,
            SemanticPassed = true,
            NaturalnessPassed = true,
            NaturalnessScore = Math.Min(DeepSeekNaturalnessScore, GlmNaturalnessScore),
            Resources = EmbodiedResource.RightArm
        };
    }
}
