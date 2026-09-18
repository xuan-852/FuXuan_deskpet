using System;
using System.Text;
using NUnit.Framework;

public class CertifiedMotionLibraryTests
{
    [Test] public void 库内条目唯一且基本有效()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var motion in CertifiedMotionLibrary.Entries)
        {
            Assert.IsTrue(seen.Add(motion.SkillId), "duplicate skill id: " + motion.SkillId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(motion.SemanticBoundary), "missing semantic: " + motion.SkillId);
            Assert.Greater(motion.DurationSeconds, 0f, motion.SkillId);
            Assert.AreNotEqual(EmbodiedResource.None, motion.Resources, motion.SkillId);
            Assert.AreEqual(64, motion.PacketSha256.Length, motion.SkillId);
            Assert.AreEqual(64, motion.CurveSha256.Length, "missing curve hash: " + motion.SkillId);
            Assert.GreaterOrEqual(motion.NaturalnessScore, NaturalnessGate.MinimumScore, motion.SkillId);
        }
    }

    [Test]
    public void 被篡改的认证曲线必须被完整性校验拒绝()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("external_Hiyori_Hiyori_m05", out var motion));
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "tampered-curve");
            Assert.IsFalse(CertifiedMotionLibrary.TryVerifyCurveFile(motion, path, out var reason));
            Assert.AreEqual("curve-hash-mismatch", reason);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void 自制单臂抬起评审曲线必须与登记哈希一致()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("screen_side_arm_raise", out var motion));
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "{\n  \"candidateId\": \"screen_side_arm_raise\",\n  \"durationSeconds\": 2.4,\n  \"curves\": [\n    {\n      \"parameterId\": \"Param94\",\n      \"segments\": [\n        0,\n        0,\n        1,\n        0.24,\n        0,\n        0.64,\n        15,\n        0.97,\n        15,\n        2,\n        1.2,\n        15,\n        1,\n        1.52,\n        15,\n        2.16,\n        0,\n        2.4,\n        0\n      ]\n    }\n  ]\n}\n", new UTF8Encoding(false));
            Assert.IsTrue(CertifiedMotionLibrary.TryVerifyCurveFile(motion, path, out var reason), reason);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test] public void 库内全部技能都已注册进准入()
    {
        foreach (var motion in CertifiedMotionLibrary.Entries)
            Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible(motion.SkillId), motion.SkillId);
        Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible("screen_side_arm_raise"));
    }

    [Test] public void HiyoriM06只以已评审的头面技能语义登记()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("external_Hiyori_Hiyori_m06", out var motion));
        Assert.AreEqual(EmbodiedResource.Face | EmbodiedResource.Body, motion.Resources);
        Assert.AreEqual("1c5b4f6edb705658b1648a3b5a1b2de5cce5d764183d47b21637ca0cb1b29c74", motion.PacketSha256);
        Assert.AreEqual(82, motion.NaturalnessScore);
        StringAssert.Contains("不是手臂动作或位移动作", motion.SemanticBoundary);
    }

    [Test] public void request_body_skill已注册且仅在身体意图白名单()
    {
        ToolRegistry.Initialize();
        Assert.IsTrue(ToolRegistry.HasTool("request_body_skill"));
        Assert.IsFalse(ToolRegistry.IsDangerous("request_body_skill"));
        Assert.IsTrue(LocalToolRouter.TryGetStrictIntentTools("body", out var allowed));
        CollectionAssert.Contains(allowed, "request_body_skill");
        Assert.IsFalse(LocalToolRouter.IsAllowed("request_body_skill", "operation"));
    }

    [Test] public void 身体提示词只声明认证技能且禁止原始参数()
    {
        string prompt = ChatManager.BuildCertifiedBodySkillBoundary();
        StringAssert.Contains("request_body_skill", prompt);
        StringAssert.Contains("原始参数", prompt);
        StringAssert.DoesNotContain("当前没有可调用", prompt);
        StringAssert.DoesNotContain("screen_side_arm_raise", prompt);
        foreach (var motion in CertifiedMotionLibrary.LlmExposedEntries)
            StringAssert.Contains(motion.SkillId, prompt);
    }

    [Test] public void 无生产曲线执行器的候选不得暴露给AI()
    {
        Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible("screen_side_arm_raise"));
        Assert.IsFalse(CertifiedMotionLibrary.IsLlmExposed("screen_side_arm_raise"));
        var tool = new RequestBodySkillTool();
        StringAssert.DoesNotContain("screen_side_arm_raise", tool.ToolDescription);
    }

    [Test]
    public void 认证与AI暴露必须是两道独立白名单()
    {
        var hidden = new CertifiedMotionLibrary.Entry { SkillId = "internal_candidate" };
        Assert.IsFalse(hidden.LlmExposed);
        Assert.IsFalse(CertifiedMotionLibrary.IsLlmExposed("not_registered"));
        foreach (var motion in CertifiedMotionLibrary.Entries)
        {
            if (motion.SkillId == "screen_side_arm_raise")
            {
                Assert.IsFalse(motion.LlmExposed, "human-review candidate must remain hidden");
                Assert.IsFalse(CertifiedMotionLibrary.IsLlmExposed(motion.SkillId));
            }
            else
            {
                Assert.IsTrue(motion.LlmExposed, "existing behavior must remain explicit: " + motion.SkillId);
                Assert.IsTrue(CertifiedMotionLibrary.IsLlmExposed(motion.SkillId));
            }
        }
    }
}
