using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

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
        public string CurveSha256;
        public int DeepSeekNaturalnessScore;
        public int GlmNaturalnessScore;
        public string DualReviewId;
        // Runtime certification and model exposure are separate decisions.
        public bool LlmExposed;

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
            SkillId = "screen_side_arm_raise",
            SemanticBoundary = "画面侧单臂平缓抬起、短暂停顿后回落；不是招手、问候、舞蹈或人体左右归属声明",
            DurationSeconds = 2.4f, Resources = EmbodiedResource.RightArm,
            PacketSha256 = "b7045039e5b9edf25e4431dccf705bb6479020252f0a45e72bf0984acdade091",
            CurveSha256 = "569e88a39a32f6fd292706039438cda886d453d6df189850ed822880614060a9",
            DeepSeekNaturalnessScore = 85, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-b7045039-2026-09-16", LlmExposed = false
        },
        new Entry
        {
            SkillId = "external_Hiyori_Hiyori_m02",
            SemanticBoundary = "头部左右轻快摇摆并伴随眨眼与表情变化，随后回到基线；不是手臂上抬、挥手或位移动作",
            DurationSeconds = 5.93f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "674bdaec06ac91bd63638f44970ead7707a837fd8debacfbea990fd186832604",
            CurveSha256 = "7f974c62e4ff3bb49f8cee40bdda1379e5d2c4686cbfb5b850fa0c73d133b390",
            DeepSeekNaturalnessScore = 85, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-674bdaec-2026-09-17", LlmExposed = true
        },
        new Entry
        {
            SkillId = "external_Hiyori_Hiyori_m05",
            SemanticBoundary = "头部转向画面左侧并伴随张嘴表情变化，随后回到基线；不是手臂动作或位移动作",
            DurationSeconds = 8.6f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "266f87ff61b162d2cabae4f2612f8f293f2f3e56e12a59885ccac2335113cd65",
            CurveSha256 = "35195a4026b0956539c56f879c0e34d7a9cb1048b965fd456fed69eb19007326",
            DeepSeekNaturalnessScore = 82, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-266f87ff-2026-09-17", LlmExposed = true
        },
        new Entry
        {
            SkillId = "external_Hiyori_Hiyori_m06",
            SemanticBoundary = "头部侧倾并伴随眨眼、视线与表情变化，随后回到基线；不是手臂动作或位移动作",
            DurationSeconds = 5.37f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "1c5b4f6edb705658b1648a3b5a1b2de5cce5d764183d47b21637ca0cb1b29c74",
            CurveSha256 = "a42b76a73f06aea945d4377b2e7030d3e91a2daa4254c4496569b80cc2914c38",
            DeepSeekNaturalnessScore = 82, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-1c5b4f6e-2026-09-17", LlmExposed = true
        },
        new Entry
        {
            SkillId = "external_Haru_haru_g_idle",
            SemanticBoundary = "轻微的头部与视线摆动及呼吸起伏的待机动作，随后回到基线；不是手臂上抬或位移动作",
            DurationSeconds = 10f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "a21ee43bd77d68d832ddff7c02fd7bd052ec5214402ae6639a2070ab0c2a0bcb",
            CurveSha256 = "8ceb051d1b6584ab7d0021ac760f81f7c2015901fc2e4f7d696d061382f40a8e",
            DeepSeekNaturalnessScore = 85, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-a21ee43b-2026-09-17", LlmExposed = true
        },
        new Entry
        {
            SkillId = "external_Haru_haru_g_m10",
            SemanticBoundary = "头部轻摆伴随闭眼微笑的表情变化，随后回到基线；不是手臂上抬、挥手或位移动作",
            DurationSeconds = 5.5f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "546596eb0fbb689332df49fb511f746d3938497424956f536c5c42be9a1e1428",
            CurveSha256 = "e0b49e9d3659a76c63e6f676379df837e295a3b66c2d1121e6d732bd179c19c5",
            DeepSeekNaturalnessScore = 85, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-546596eb-2026-09-17", LlmExposed = true
        },
        new Entry
        {
            SkillId = "external_Haru_haru_g_m20",
            SemanticBoundary = "头部转向画面右侧并伴随微笑表情，随后回到基线；不是手臂上抬、挥手或位移动作",
            DurationSeconds = 6f, Resources = EmbodiedResource.Face | EmbodiedResource.Body,
            PacketSha256 = "292f8c8b4cc802b7337d9a09285cda9397811b095db31c24b78d18d8667561a3",
            CurveSha256 = "996ad79a052b580f4d40df4f853471e6792057f3d4303fe850179c56f2bc1825",
            DeepSeekNaturalnessScore = 88, GlmNaturalnessScore = 92, DualReviewId = "dual-packet-292f8c8b-2026-09-17", LlmExposed = true
        },
    };

    public static bool TryGet(string skillId, out Entry entry)
    {
        entry = null;
        foreach (var item in Entries)
            if (item.SkillId == skillId) { entry = item; return true; }
        return false;
    }

    public static bool TryVerifyCurveFile(Entry entry, string path, out string reason)
    {
        reason = null;
        if (entry == null || string.IsNullOrWhiteSpace(entry.CurveSha256) || entry.CurveSha256.Length != 64)
        {
            reason = "missing-curve-hash";
            return false;
        }

        try
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha256.ComputeHash(stream);
                string actual = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                if (string.Equals(actual, entry.CurveSha256, StringComparison.OrdinalIgnoreCase)) return true;
                reason = "curve-hash-mismatch";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = "curve-hash-read-failed:" + ex.GetType().Name;
            return false;
        }
    }

    public static bool IsLlmExposed(string skillId)
    {
        return TryGet(skillId, out var entry) && entry.LlmExposed;
    }

    public static IEnumerable<Entry> LlmExposedEntries
    {
        get
        {
            foreach (var entry in Entries)
                if (entry.LlmExposed) yield return entry;
        }
    }
}
