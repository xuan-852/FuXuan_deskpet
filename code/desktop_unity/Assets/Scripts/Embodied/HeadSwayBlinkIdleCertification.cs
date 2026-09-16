using System;

// 首个外部动作候选认证记录：官方示例 Hiyori_m02 重定向到符玄模型后的
// 「头部摇摆+眨眼」待机动作。证据：候选包 674bdaec…（18 帧、峰值差 9.98、
// 复位差 0.000）双模型一致 supported（DeepSeek 85/100 + GLM glm-4.5v 92/100，
// 均 high 置信）；机械层依据全参数普查（23 条曲线参数全部通过写/读/复位）。
// 回放取证在隔离探针完成，尚未有生产执行器；LLM 工具面仍为零。
public static class HeadSwayBlinkIdleCertification
{
    public const string SkillId = "external_Hiyori_Hiyori_m02";
    public const string PacketSha256 = "674bdaec06ac91bd63638f44970ead7707a837fd8debacfbea990fd186832604";
    public const string SemanticBoundary = "头部左右轻快摇摆并伴随眨眼与表情变化，随后回到基线；不是手臂上抬、挥手或位移动作";
    public const string DualReviewId = "dual-packet-674bdaec-2026-09-17";
    public const int DeepSeekNaturalnessScore = 85;
    public const int GlmNaturalnessScore = 92;
    public const float DurationSeconds = 5.93f;
    public const string ModelVersion = ScreenSideArmRaiseCertification.ModelVersion;
    public const string MappingVersion = ScreenSideArmRaiseCertification.MappingVersion;

    // 自然度取双模型中的较低分（保守）；资源按曲线实际触达的脸部+身体槽位声明。
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
            Resources = EmbodiedResource.Face | EmbodiedResource.Body
        };
    }
}
