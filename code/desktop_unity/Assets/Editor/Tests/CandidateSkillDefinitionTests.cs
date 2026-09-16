using System;
using NUnit.Framework;

public class CandidateSkillDefinitionTests
{
    [Test] public void 单臂候选缺自然度证据不得进入认证复核()
    { var skill=CandidateSkillCatalog.ScreenSideArmRaise; Assert.IsTrue(skill.IsReadyForCertificationReview(out var reason)); Assert.AreEqual("ready-for-review",reason); skill.NaturalnessEvidence.Id=""; Assert.IsFalse(skill.IsReadyForCertificationReview(out reason)); Assert.AreEqual("four-layer-evidence-incomplete",reason); }

    [Test] public void 仅Supporting证据层不构成全部通过()
    { var partial=new CandidateSkillDefinition { SkillId="x", SemanticDescription="y", RequiredSkeletonNodeId="z", Resources=EmbodiedResource.RightArm, Duration=TimeSpan.FromSeconds(1), RequiresStationary=true, MechanicalEvidence=new CandidateEvidenceReference{Id="a",Status=CandidateEvidenceStatus.Supporting}, VisualEvidence=new CandidateEvidenceReference{Id="b",Status=CandidateEvidenceStatus.Supporting}, SemanticEvidence=new CandidateEvidenceReference{Id="c",Status=CandidateEvidenceStatus.Supporting}, NaturalnessEvidence=new CandidateEvidenceReference{Id="d",Status=CandidateEvidenceStatus.Supporting} }; Assert.IsFalse(partial.HasPassedAllEvidenceLayers()); }

    [Test] public void 双模型复核一致后四层证据方可置为通过()
    { var skill=CandidateSkillCatalog.ScreenSideArmRaise; Assert.IsTrue(skill.HasPassedAllEvidenceLayers()); Assert.AreEqual(CandidateEvidenceStatus.Passed, skill.SemanticEvidence.Status); Assert.AreEqual(CandidateEvidenceStatus.Passed, skill.NaturalnessEvidence.Status); }
}
