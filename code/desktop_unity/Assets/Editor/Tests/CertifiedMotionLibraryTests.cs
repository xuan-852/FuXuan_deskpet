using System;
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
            Assert.GreaterOrEqual(motion.NaturalnessScore, NaturalnessGate.MinimumScore, motion.SkillId);
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
        StringAssert.Contains("screen_side_arm_raise", prompt);
        StringAssert.Contains("原始参数", prompt);
        StringAssert.DoesNotContain("当前没有可调用", prompt);
        foreach (var motion in CertifiedMotionLibrary.Entries)
            StringAssert.Contains(motion.SkillId, prompt);
    }
}
