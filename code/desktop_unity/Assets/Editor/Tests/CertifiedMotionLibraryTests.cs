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

    [Test] public void request_body_skill已注册且在操作意图白名单()
    {
        ToolRegistry.Initialize();
        Assert.IsTrue(ToolRegistry.HasTool("request_body_skill"));
        Assert.IsFalse(ToolRegistry.IsDangerous("request_body_skill"));
        Assert.IsTrue(LocalToolRouter.TryGetStrictIntentTools("operation", out var allowed));
        CollectionAssert.Contains(allowed, "request_body_skill");
    }
}
