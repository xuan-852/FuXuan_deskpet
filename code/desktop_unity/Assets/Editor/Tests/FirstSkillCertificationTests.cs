using System;
using NUnit.Framework;

public class FirstSkillCertificationTests
{
    [Test] public void 双模型一致后首个技能可注册并被协调器接受()
    {
        var skill = CandidateSkillCatalog.ScreenSideArmRaise;
        Assert.IsTrue(skill.HasPassedAllEvidenceLayers());
        var registry = new CertifiedSkillRegistry();
        Assert.IsTrue(registry.TryRegister(ScreenSideArmRaiseCertification.CreateRecord(), out var reason));
        Assert.AreEqual("registered", reason);
        Assert.IsTrue(registry.TryGet(ScreenSideArmRaiseCertification.SkillId, out var record));
        Assert.AreEqual(CertificationStage.Certified, record.Stage);
        var coordinator = new EmbodiedCoordinator(registry);
        var request = new EmbodiedActionRequest { SkillId = ScreenSideArmRaiseCertification.SkillId, SemanticTarget = "screen-side arm raise and lower", Resources = EmbodiedResource.RightArm, Timeout = TimeSpan.FromSeconds(2.4) };
        Assert.AreEqual(EmbodiedActionStatus.Executing, coordinator.TryBegin(request, out _));
        Assert.AreEqual(EmbodiedActionStatus.Rejected, coordinator.TryBegin(request, out var why));
        Assert.AreEqual("resource-busy", why);
        coordinator.Complete(request);
        Assert.AreEqual(EmbodiedActionStatus.Executing, coordinator.TryBegin(request, out _));
        coordinator.Complete(request);
    }

    [Test] public void 认证记录缺少版本证据不得注册()
    {
        var record = ScreenSideArmRaiseCertification.CreateRecord();
        record.ModelVersion = "";
        var registry = new CertifiedSkillRegistry();
        Assert.IsFalse(registry.TryRegister(record, out var reason));
        Assert.AreEqual("version-evidence-required", reason);
    }

    [Test] public void 认证记录自然度取双模型较低分且不低于门槛()
    {
        var record = ScreenSideArmRaiseCertification.CreateRecord();
        Assert.AreEqual(Math.Min(ScreenSideArmRaiseCertification.DeepSeekNaturalnessScore, ScreenSideArmRaiseCertification.GlmNaturalnessScore), record.NaturalnessScore);
        Assert.GreaterOrEqual(record.NaturalnessScore, NaturalnessGate.MinimumScore);
    }
}
