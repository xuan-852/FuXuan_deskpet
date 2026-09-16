using System;

public enum CandidateEvidenceStatus
{
    Missing,
    Supporting,
    PendingReview,
    Passed,
    Rejected
}

public sealed class CandidateEvidenceReference
{
    public string Id;
    public CandidateEvidenceStatus Status;

    public bool IsPresent => !string.IsNullOrWhiteSpace(Id) && Status != CandidateEvidenceStatus.Missing;
    public bool IsPassed => IsPresent && Status == CandidateEvidenceStatus.Passed;
}

public sealed class CandidateSkillDefinition
{
    public string SkillId;
    public string SemanticDescription;
    public string RequiredSkeletonNodeId;
    public EmbodiedResource Resources;
    public TimeSpan Duration;
    public bool RequiresStationary;
    public CandidateEvidenceReference MechanicalEvidence;
    public CandidateEvidenceReference VisualEvidence;
    public CandidateEvidenceReference SemanticEvidence;
    public CandidateEvidenceReference NaturalnessEvidence;

    public bool IsReadyForCertificationReview(out string reason)
    {
        if (string.IsNullOrWhiteSpace(SkillId) || string.IsNullOrWhiteSpace(SemanticDescription)) { reason = "missing-semantic-definition"; return false; }
        if (string.IsNullOrWhiteSpace(RequiredSkeletonNodeId) || Resources == EmbodiedResource.None) { reason = "missing-skeleton-or-resource"; return false; }
        if (Duration <= TimeSpan.Zero || !RequiresStationary) { reason = "missing-safe-lifecycle"; return false; }
        if (!HasAllEvidenceReferences()) { reason = "four-layer-evidence-incomplete"; return false; }
        reason = "ready-for-review"; return true;
    }

    // Candidate material may be queued for review, but it must never be mistaken for a certification result.
    public bool HasPassedAllEvidenceLayers()
    {
        return MechanicalEvidence != null && MechanicalEvidence.IsPassed
            && VisualEvidence != null && VisualEvidence.IsPassed
            && SemanticEvidence != null && SemanticEvidence.IsPassed
            && NaturalnessEvidence != null && NaturalnessEvidence.IsPassed;
    }

    private bool HasAllEvidenceReferences()
    {
        return MechanicalEvidence != null && MechanicalEvidence.IsPresent
            && VisualEvidence != null && VisualEvidence.IsPresent
            && SemanticEvidence != null && SemanticEvidence.IsPresent
            && NaturalnessEvidence != null && NaturalnessEvidence.IsPresent;
    }
}

public static class CandidateSkillCatalog
{
    // 四层状态依据 2026-09-16 双模型离线复核一致（DeepSeek 85/100 与 GLM glm-4.5v 92/100，
    // 均为 high 置信、无分歧）后由主代理复核提升；机械/视觉依据见 docs/truth/l3-capability-census-mechanical.md。
    public static CandidateSkillDefinition ScreenSideArmRaise => new CandidateSkillDefinition
    {
        SkillId = "screen_side_arm_raise",
        SemanticDescription = "Raise and lower the evidenced screen-side arm; this is not a greeting or wave.",
        RequiredSkeletonNodeId = "arm.screen-side-raise",
        Resources = EmbodiedResource.RightArm,
        Duration = TimeSpan.FromSeconds(2.4),
        RequiresStationary = true,
        MechanicalEvidence = new CandidateEvidenceReference { Id = "Param94-dynamic-2026-09-16", Status = CandidateEvidenceStatus.Passed },
        VisualEvidence = new CandidateEvidenceReference { Id = "Param94-dynamic-2026-09-16", Status = CandidateEvidenceStatus.Passed },
        SemanticEvidence = new CandidateEvidenceReference { Id = "packet-b7045039-dual-deepseek-glm-2026-09-16", Status = CandidateEvidenceStatus.Passed },
        NaturalnessEvidence = new CandidateEvidenceReference { Id = "packet-b7045039-dual-deepseek-glm-2026-09-16", Status = CandidateEvidenceStatus.Passed }
    };
}
