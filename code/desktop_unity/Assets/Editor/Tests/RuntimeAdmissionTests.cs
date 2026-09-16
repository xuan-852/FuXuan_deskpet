using NUnit.Framework;

public class RuntimeAdmissionTests
{
    [Test] public void 双认证技能注册且多资源请求被准入()
    {
        Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible("screen_side_arm_raise"), "arm skill missing");
        Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible(HeadSwayBlinkIdleCertification.SkillId), "motion skill missing");
        EmbodiedActionRequest held = null;
        try
        {
            Assert.IsTrue(EmbodiedRuntimeAdmission.TryBeginSkill(HeadSwayBlinkIdleCertification.SkillId, out var request, out var reason), "motion begin refused: " + reason);
            held = request;
            Assert.AreEqual(EmbodiedResource.Face | EmbodiedResource.Body, request.Resources);
            Assert.AreEqual(HeadSwayBlinkIdleCertification.DurationSeconds, (float)request.Timeout.TotalSeconds, 0.01f);
            // 脸部资源被占用期间，手臂技能不含冲突资源仍可并行准入
            Assert.IsTrue(EmbodiedRuntimeAdmission.TryBeginSkill("screen_side_arm_raise", out var armRequest, out var armReason), "arm begin refused: " + armReason);
            EmbodiedRuntimeAdmission.CompleteSkill(armRequest, "admission-test-parallel-release");
        }
        finally
        {
            EmbodiedRuntimeAdmission.CompleteSkill(held, "admission-test-cleanup");
        }
    }

    [Test] public void 未认证技能不得获得准入()
    {
        Assert.IsFalse(EmbodiedRuntimeAdmission.IsSkillAdmissible("nonexistent_skill"));
        Assert.IsFalse(EmbodiedRuntimeAdmission.TryBeginSkill("nonexistent_skill", out _, out var reason));
        Assert.AreEqual("skill-not-certified", reason);
    }

    [Test] public void 首个认证技能可准入且同一时刻仅允许一个请求()
    {
        Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible("screen_side_arm_raise"));
        EmbodiedActionRequest held = null;
        try
        {
            Assert.IsTrue(EmbodiedRuntimeAdmission.TryBeginSkill("screen_side_arm_raise", out var request, out _));
            held = request;
            Assert.IsFalse(EmbodiedRuntimeAdmission.TryBeginSkill("screen_side_arm_raise", out _, out var why));
            Assert.AreEqual("resource-busy", why);
        }
        finally
        {
            EmbodiedRuntimeAdmission.CompleteSkill(held, "admission-test-cleanup");
        }
    }

    [Test] public void 释放后资源可再次准入且请求带声明与超时()
    {
        EmbodiedActionRequest held = null;
        try
        {
            Assert.IsTrue(EmbodiedRuntimeAdmission.TryBeginSkill("screen_side_arm_raise", out var request, out _));
            Assert.AreEqual(EmbodiedResource.RightArm, request.Resources);
            Assert.Greater(request.Timeout.TotalSeconds, 0f);
            EmbodiedRuntimeAdmission.CompleteSkill(request, "admission-test-release");
            held = null;
            Assert.IsTrue(EmbodiedRuntimeAdmission.TryBeginSkill("screen_side_arm_raise", out var again, out _));
            held = again;
        }
        finally
        {
            EmbodiedRuntimeAdmission.CompleteSkill(held, "admission-test-cleanup");
        }
    }

    [Test] public void 释放非当前请求被忽略()
    {
        EmbodiedActionRequest held = null;
        try
        {
            Assert.IsTrue(EmbodiedRuntimeAdmission.TryBeginSkill("screen_side_arm_raise", out var request, out _));
            held = request;
            EmbodiedRuntimeAdmission.CompleteSkill(new EmbodiedActionRequest { SkillId = "screen_side_arm_raise" }, "admission-test-wrong-release");
            // 错误释放不得解锁：仍处于占用状态
            Assert.IsFalse(EmbodiedRuntimeAdmission.TryBeginSkill("screen_side_arm_raise", out _, out var why));
            Assert.AreEqual("resource-busy", why);
        }
        finally
        {
            EmbodiedRuntimeAdmission.CompleteSkill(held, "admission-test-cleanup");
        }
    }
}
