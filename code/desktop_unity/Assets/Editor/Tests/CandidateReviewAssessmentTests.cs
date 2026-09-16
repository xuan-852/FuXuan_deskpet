using NUnit.Framework;

public class CandidateReviewAssessmentTests
{
    private const string Packet = "b7045039e5b9edf25e4431dccf705bb6479020252f0a45e72bf0984acdade091";

    [Test] public void 缺少复核包指纹不得贡献认证证据()
    {
        var assessment = PassingAssessment(); assessment.PacketSha256 = "";
        Assert.IsFalse(assessment.CanContributeToCertification(CandidateSkillCatalog.ScreenSideArmRaise, out var reason));
        Assert.AreEqual("review-packet-fingerprint-required", reason);
    }

    [Test] public void 高分但低于步行基线不得贡献认证证据()
    {
        var assessment = PassingAssessment(); assessment.Naturalness.MeetsWalkBaseline = false; assessment.Naturalness.ReviewerScore = 100;
        Assert.IsFalse(assessment.CanContributeToCertification(CandidateSkillCatalog.ScreenSideArmRaise, out var reason));
        Assert.AreEqual("below-walk-baseline", reason);
    }

    [Test] public void 合格离线评审仍不等同候选已认证()
    {
        var skill = CandidateSkillCatalog.ScreenSideArmRaise;
        Assert.IsTrue(PassingAssessment().CanContributeToCertification(skill, out var reason));
        Assert.AreEqual("assessment-can-contribute", reason);
        // 评审只贡献证据：注册表默认为空，不存在自动注册副作用。
        Assert.AreEqual(0, new CertifiedSkillRegistry().Count);
    }

    private static CandidateReviewAssessment PassingAssessment()
    {
        return new CandidateReviewAssessment
        {
            SkillId = "screen_side_arm_raise", PacketSha256 = Packet, Reviewer = "test-reviewer", ReviewerModel = "test-model",
            SemanticVerdict = OfflineReviewVerdict.Passed, NaturalnessVerdict = OfflineReviewVerdict.Passed,
            Naturalness = new NaturalnessEvidence { EvidenceId = "test-naturalness", SequenceContinuous = true, ResetStable = true, NoResourceConflict = true, MeetsWalkBaseline = true, ReviewerScore = 75 }
        };
    }
}
