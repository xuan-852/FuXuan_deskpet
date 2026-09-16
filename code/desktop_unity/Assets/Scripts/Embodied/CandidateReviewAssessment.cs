using System;

public enum OfflineReviewVerdict
{
    Pending,
    Passed,
    Rejected
}

// Immutable-in-spirit record for an offline reviewer result. It is evidence, not a runtime action grant.
public sealed class CandidateReviewAssessment
{
    public string SkillId;
    public string PacketSha256;
    public string Reviewer;
    public string ReviewerModel;
    public OfflineReviewVerdict SemanticVerdict;
    public OfflineReviewVerdict NaturalnessVerdict;
    public NaturalnessEvidence Naturalness;

    public bool CanContributeToCertification(CandidateSkillDefinition candidate, out string reason)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.SkillId) || candidate.SkillId != SkillId)
        {
            reason = "candidate-skill-mismatch";
            return false;
        }
        if (string.IsNullOrWhiteSpace(PacketSha256) || PacketSha256.Length != 64)
        {
            reason = "review-packet-fingerprint-required";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Reviewer) || string.IsNullOrWhiteSpace(ReviewerModel))
        {
            reason = "reviewer-attribution-required";
            return false;
        }
        if (SemanticVerdict != OfflineReviewVerdict.Passed)
        {
            reason = "semantic-review-not-passed";
            return false;
        }
        if (NaturalnessVerdict != OfflineReviewVerdict.Passed)
        {
            reason = "naturalness-review-not-passed";
            return false;
        }
        if (!NaturalnessGate.Passes(Naturalness, out reason)) return false;
        reason = "assessment-can-contribute";
        return true;
    }
}
