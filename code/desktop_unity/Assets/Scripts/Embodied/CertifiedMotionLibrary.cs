using System;
using System.Collections.Generic;

// 认证动作技能库（数据层）：登记已完成四层认证的外部动作候选元数据。
// 曲线数据不入版本库（许可约束），运行时从数据根 certified_motions/<skillId>.json 加载。
// 新认证动作在 Entries 中登记后即可被准入汇点与生产执行器使用。
public static class CertifiedMotionLibrary
{
    public sealed class Entry
    {
        public string SkillId;
        public string SemanticBoundary;
        public float DurationSeconds;
        public EmbodiedResource Resources;
        public string PacketSha256;
        public int DeepSeekNaturalnessScore;
        public int GlmNaturalnessScore;
        public string DualReviewId;

        public int NaturalnessScore => Math.Min(DeepSeekNaturalnessScore, GlmNaturalnessScore);
        public SkillCertificationRecord CreateRecord() => new SkillCertificationRecord
        {
            SkillId = SkillId,
            ModelVersion = ScreenSideArmRaiseCertification.ModelVersion,
            MappingVersion = ScreenSideArmRaiseCertification.MappingVersion,
            MechanicalPassed = true, VisualPassed = true, SemanticPassed = true, NaturalnessPassed = true,
            NaturalnessScore = NaturalnessScore,
            Resources = Resources
        };
    }

    public static readonly Entry[] Entries =
    {
        new Entry
        {
            SkillId = "external_Hiyori_Hiyori_m02",
            SemanticBoundary = "头部左右轻快摇摆并伴随眨眼与表情变化，随后回到基线；不是手臂上抬、挥手或位移动作",
            DurationSeconds = 5.93f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "674bdaec06ac91bd63638f44970ead7707a837fd8debacfbea990fd186832604",
            DeepSeekNaturalnessScore = 85, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-674bdaec-2026-09-17"
        },
        new Entry
        {
            SkillId = "external_Hiyori_Hiyori_m05",
            SemanticBoundary = "头部转向画面左侧并伴随张嘴表情变化，随后回到基线；不是手臂动作或位移动作",
            DurationSeconds = 8.6f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "266f87ff61b162d2cabae4f2612f8f293f2f3e56e12a59885ccac2335113cd65",
            DeepSeekNaturalnessScore = 82, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-266f87ff-2026-09-17"
        },
        new Entry
        {
            SkillId = "external_Haru_haru_g_idle",
            SemanticBoundary = "轻微的头部与视线摆动及呼吸起伏的待机动作，随后回到基线；不是手臂上抬或位移动作",
            DurationSeconds = 10f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "a21ee43bd77d68d832ddff7c02fd7bd052ec5214402ae6639a2070ab0c2a0bcb",
            DeepSeekNaturalnessScore = 85, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-a21ee43b-2026-09-17"
        },
        new Entry
        {
            SkillId = "external_Haru_haru_g_m10",
            SemanticBoundary = "头部轻摆伴随闭眼微笑的表情变化，随后回到基线；不是手臂上抬、挥手或位移动作",
            DurationSeconds = 5.5f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "546596eb0fbb689332df49fb511f746d3938497424956f536c5c42be9a1e1428",
            DeepSeekNaturalnessScore = 85, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-546596eb-2026-09-17"
        },
        new Entry
        {
            SkillId = "external_Haru_haru_g_m20",
            SemanticBoundary = "头部转向画面右侧并伴随微笑表情，随后回到基线；不是手臂上抬、挥手或位移动作",
            DurationSeconds = 6f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "292f8c8b4cc802b7337d9a09285cda9397811b095db31c24b78d18d8667561a3",
            DeepSeekNaturalnessScore = 88, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-292f8c8b-2026-09-17"
        },
    };

    public static bool TryGet(string skillId, out Entry entry)
    {
        entry = null;
        foreach (var item in Entries)
            if (item.SkillId == skillId) { entry = item; return true; }
        return false;
    }
}
