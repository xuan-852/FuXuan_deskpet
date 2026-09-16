using NUnit.Framework;

public class NaturalnessGateTests
{
    [Test] public void 缺步行基线即使高分也不通过()
    { var e=new NaturalnessEvidence { EvidenceId="test", SequenceContinuous=true, ResetStable=true, NoResourceConflict=true, ReviewerScore=100 }; Assert.IsFalse(NaturalnessGate.Passes(e,out var why)); Assert.AreEqual("below-walk-baseline",why); }
    [Test] public void 全部门槛满足才通过()
    { var e=new NaturalnessEvidence { EvidenceId="test", SequenceContinuous=true, ResetStable=true, NoResourceConflict=true, MeetsWalkBaseline=true, ReviewerScore=75 }; Assert.IsTrue(NaturalnessGate.Passes(e,out var why)); Assert.AreEqual("naturalness-passed",why); }
}
