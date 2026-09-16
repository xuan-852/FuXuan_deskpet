using System;
using NUnit.Framework;

public class CertifiedSkillFoundationTests
{
    [Test] public void 未经四层认证不得注册()
    { var r = new CertifiedSkillRegistry(); Assert.IsFalse(r.TryRegister(new SkillCertificationRecord { SkillId="candidate", ModelVersion="m", MappingVersion="p", MechanicalPassed=true }, out var why)); Assert.AreEqual("four-layer-certification-required", why); Assert.AreEqual(0, r.Count); }
    [Test] public void 认证技能只能以语义请求占用声明资源()
    { var r=new CertifiedSkillRegistry(); Assert.IsTrue(r.TryRegister(new SkillCertificationRecord { SkillId="test.skill", ModelVersion="m", MappingVersion="p", MechanicalPassed=true, VisualPassed=true, SemanticPassed=true, NaturalnessPassed=true, NaturalnessScore=75, Resources=EmbodiedResource.RightArm }, out _)); var c=new EmbodiedCoordinator(r); var q=new EmbodiedActionRequest { SkillId="test.skill", SemanticTarget="test semantic", Resources=EmbodiedResource.RightArm, Timeout=TimeSpan.FromSeconds(1) }; Assert.AreEqual(EmbodiedActionStatus.Executing,c.TryBegin(q,out _)); Assert.AreEqual(EmbodiedActionStatus.Rejected,c.TryBegin(q,out var why)); Assert.AreEqual("resource-busy",why); c.Complete(q); }
}
