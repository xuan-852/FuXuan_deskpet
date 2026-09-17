using System;
using NUnit.Framework;

public class CertifiedSkillFoundationTests
{
    [Test] public void 未经四层认证不得注册()
    { var r = new CertifiedSkillRegistry(); Assert.IsFalse(r.TryRegister(new SkillCertificationRecord { SkillId="candidate", ModelVersion="m", MappingVersion="p", MechanicalPassed=true }, out var why)); Assert.AreEqual("four-layer-certification-required", why); Assert.AreEqual(0, r.Count); }
    [Test] public void 认证技能只能以语义请求占用声明资源()
    { var r=new CertifiedSkillRegistry(); Assert.IsTrue(r.TryRegister(new SkillCertificationRecord { SkillId="test.skill", ModelVersion="m", MappingVersion="p", MechanicalPassed=true, VisualPassed=true, SemanticPassed=true, NaturalnessPassed=true, NaturalnessScore=75, Resources=EmbodiedResource.RightArm }, out _)); var c=new EmbodiedCoordinator(r); var q=new EmbodiedActionRequest { SkillId="test.skill", SemanticTarget="test semantic", Resources=EmbodiedResource.RightArm, Timeout=TimeSpan.FromSeconds(1) }; Assert.AreEqual(EmbodiedActionStatus.Executing,c.TryBegin(q,out _)); Assert.AreEqual(EmbodiedActionStatus.Rejected,c.TryBegin(q,out var why)); Assert.AreEqual("resource-busy",why); c.Complete(q); }
    [Test] public void 超时和取消只释放所属请求并记录终态()
    {
        var r = new CertifiedSkillRegistry();
        Assert.IsTrue(r.TryRegister(new SkillCertificationRecord { SkillId="arm", ModelVersion="m", MappingVersion="p", MechanicalPassed=true, VisualPassed=true, SemanticPassed=true, NaturalnessPassed=true, NaturalnessScore=75, Resources=EmbodiedResource.RightArm }, out _));
        Assert.IsTrue(r.TryRegister(new SkillCertificationRecord { SkillId="face", ModelVersion="m", MappingVersion="p", MechanicalPassed=true, VisualPassed=true, SemanticPassed=true, NaturalnessPassed=true, NaturalnessScore=75, Resources=EmbodiedResource.Face }, out _));
        var c = new EmbodiedCoordinator(r); var at = new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc);
        var arm = new EmbodiedActionRequest { SkillId="arm", SemanticTarget="raise", Resources=EmbodiedResource.RightArm, Timeout=TimeSpan.FromSeconds(2) };
        var face = new EmbodiedActionRequest { SkillId="face", SemanticTarget="look", Resources=EmbodiedResource.Face, Timeout=TimeSpan.FromSeconds(10) };
        Assert.AreEqual(EmbodiedActionStatus.Executing, c.TryBegin(arm, at, out _)); Assert.AreEqual(EmbodiedActionStatus.Executing, c.TryBegin(face, at, out _));
        Assert.AreEqual(1, c.ExpireDue(at.AddSeconds(2))); Assert.AreEqual(EmbodiedActionStatus.TimedOut, arm.Status); Assert.AreEqual("timeout", arm.TerminalReason);
        Assert.AreEqual(EmbodiedActionStatus.Executing, face.Status); Assert.IsTrue(c.Cancel(face, "user-stop")); Assert.AreEqual(EmbodiedActionStatus.Cancelled, face.Status); Assert.AreEqual("user-stop", face.TerminalReason); Assert.AreEqual(0, c.ActiveCount);
    }
}
