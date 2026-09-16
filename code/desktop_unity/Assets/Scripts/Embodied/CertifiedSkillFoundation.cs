using System;
using System.Collections.Generic;

[Flags]
public enum EmbodiedResource { None = 0, Movement = 1, Body = 2, Face = 4, LeftArm = 8, RightArm = 16, Effect = 32 }
public enum CertificationStage { Candidate, Mechanical, Visual, Semantic, Naturalness, Certified, Rejected }
public enum EmbodiedActionStatus { Rejected, Queued, Executing, Completed, Cancelled, TimedOut }

public sealed class SkillCertificationRecord
{
    public string SkillId;
    public string ModelVersion;
    public string MappingVersion;
    public bool MechanicalPassed, VisualPassed, SemanticPassed, NaturalnessPassed;
    public int NaturalnessScore;
    public EmbodiedResource Resources;
    public CertificationStage Stage => MechanicalPassed && VisualPassed && SemanticPassed && NaturalnessPassed ? CertificationStage.Certified : CertificationStage.Candidate;
}

public sealed class CertifiedSkillRegistry
{
    private readonly Dictionary<string, SkillCertificationRecord> _skills = new Dictionary<string, SkillCertificationRecord>();
    public bool TryRegister(SkillCertificationRecord record, out string reason)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.SkillId)) { reason = "missing-skill-id"; return false; }
        if (record.Stage != CertificationStage.Certified || record.NaturalnessScore < 75) { reason = "four-layer-certification-required"; return false; }
        if (string.IsNullOrWhiteSpace(record.ModelVersion) || string.IsNullOrWhiteSpace(record.MappingVersion)) { reason = "version-evidence-required"; return false; }
        _skills[record.SkillId] = record; reason = "registered"; return true;
    }
    public bool TryGet(string skillId, out SkillCertificationRecord record) => _skills.TryGetValue(skillId ?? "", out record);
    public int Count => _skills.Count;
}

public sealed class EmbodiedActionRequest
{
    public string SkillId;
    public string SemanticTarget;
    public int Priority;
    public TimeSpan Timeout;
    public EmbodiedResource Resources;
}

public sealed class EmbodiedCoordinator
{
    private readonly CertifiedSkillRegistry _registry;
    private EmbodiedResource _occupied;
    public EmbodiedCoordinator(CertifiedSkillRegistry registry) { _registry = registry ?? throw new ArgumentNullException(nameof(registry)); }
    public EmbodiedActionStatus TryBegin(EmbodiedActionRequest request, out string reason)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.SkillId) || string.IsNullOrWhiteSpace(request.SemanticTarget)) { reason = "semantic-request-required"; return EmbodiedActionStatus.Rejected; }
        if (!_registry.TryGet(request.SkillId, out var skill)) { reason = "skill-not-certified"; return EmbodiedActionStatus.Rejected; }
        if (request.Resources != skill.Resources || request.Resources == EmbodiedResource.None) { reason = "resource-declaration-mismatch"; return EmbodiedActionStatus.Rejected; }
        if ((_occupied & request.Resources) != 0) { reason = "resource-busy"; return EmbodiedActionStatus.Rejected; }
        _occupied |= request.Resources; reason = "accepted"; return EmbodiedActionStatus.Executing;
    }
    public void Complete(EmbodiedActionRequest request) { if (request != null) _occupied &= ~request.Resources; }
}
