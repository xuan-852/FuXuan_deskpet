using NUnit.Framework;

public class VirtualSkeletonCandidateLedgerTests
{
    [Test] public void 当前候选账本不向运行时开放节点()
    { var ledger=VirtualSkeletonCandidateLedger.CreateCurrentEvidenceLedger(); Assert.AreEqual(4,ledger.Count); Assert.IsTrue(ledger.TryGet("arm.screen-side-raise",out var arm)); Assert.AreEqual(SkeletonEvidenceStatus.Supporting,arm.Status); Assert.IsFalse(arm.IsRuntimeEligible); Assert.IsFalse(ledger.TryGetRuntimeNode("arm.screen-side-raise",out _)); }
    [Test] public void 只有认证节点才可作为运行时骨架节点()
    { var ledger=new VirtualSkeletonCandidateLedger(new[]{new VirtualSkeletonCandidate{NodeId="certified",Status=SkeletonEvidenceStatus.Certified}}); Assert.IsTrue(ledger.TryGetRuntimeNode("certified",out _)); }
}
