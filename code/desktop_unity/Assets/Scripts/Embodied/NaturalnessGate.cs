public sealed class NaturalnessEvidence
{
    public bool SequenceContinuous;
    public bool ResetStable;
    public bool NoResourceConflict;
    public bool MeetsWalkBaseline;
    public int ReviewerScore;
    public string EvidenceId;
}

public static class NaturalnessGate
{
    public const int MinimumScore = 75;
    public static bool Passes(NaturalnessEvidence evidence, out string reason)
    {
        if (evidence == null || string.IsNullOrWhiteSpace(evidence.EvidenceId)) { reason = "missing-naturalness-evidence"; return false; }
        if (!evidence.SequenceContinuous) { reason = "sequence-not-continuous"; return false; }
        if (!evidence.ResetStable) { reason = "reset-not-stable"; return false; }
        if (!evidence.NoResourceConflict) { reason = "resource-conflict"; return false; }
        if (!evidence.MeetsWalkBaseline) { reason = "below-walk-baseline"; return false; }
        if (evidence.ReviewerScore < MinimumScore) { reason = "score-below-minimum"; return false; }
        reason = "naturalness-passed"; return true;
    }
}
