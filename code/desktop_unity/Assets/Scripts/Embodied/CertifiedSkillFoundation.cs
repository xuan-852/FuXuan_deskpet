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
    public long RequestId;
    // 调用来源是控制面审计字段，不提供参数写入能力。未设置时调用方应显式
    // 标记为 legacy/unknown，而不是伪装为已迁移的受控执行器。
    public string Source;
    public string CorrelationId;
    public string SkillId;
    public string SemanticTarget;
    public int Priority;
    public TimeSpan Timeout;
    public EmbodiedResource Resources;
    public EmbodiedActionStatus Status { get; internal set; }
    public DateTime StartedAtUtc { get; internal set; }
    public string TerminalReason { get; internal set; }
}

public sealed class EmbodiedCoordinator
{
    private readonly CertifiedSkillRegistry _registry;
    private EmbodiedResource _occupied;
    private readonly Dictionary<EmbodiedActionRequest, EmbodiedResource> _active = new Dictionary<EmbodiedActionRequest, EmbodiedResource>();
    private long _nextRequestId;
    public EmbodiedCoordinator(CertifiedSkillRegistry registry) { _registry = registry ?? throw new ArgumentNullException(nameof(registry)); }
    public int ActiveCount => _active.Count;
    public EmbodiedActionStatus TryBegin(EmbodiedActionRequest request, out string reason) => TryBegin(request, DateTime.UtcNow, out reason);
    public EmbodiedActionStatus TryBegin(EmbodiedActionRequest request, DateTime nowUtc, out string reason)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.SkillId) || string.IsNullOrWhiteSpace(request.SemanticTarget)) { reason = "semantic-request-required"; return EmbodiedActionStatus.Rejected; }
        if (!_registry.TryGet(request.SkillId, out var skill)) { reason = "skill-not-certified"; return EmbodiedActionStatus.Rejected; }
        if (request.Resources != skill.Resources || request.Resources == EmbodiedResource.None) { reason = "resource-declaration-mismatch"; return EmbodiedActionStatus.Rejected; }
        if (request.Timeout <= TimeSpan.Zero) { reason = "positive-timeout-required"; return EmbodiedActionStatus.Rejected; }
        if ((_occupied & request.Resources) != 0)
        {
            if (!TryPreemptConflicts(request)) { reason = "resource-busy"; return EmbodiedActionStatus.Rejected; }
        }
        request.RequestId = ++_nextRequestId;
        request.Status = EmbodiedActionStatus.Executing;
        request.StartedAtUtc = nowUtc.ToUniversalTime();
        request.TerminalReason = null;
        _occupied |= request.Resources;
        _active[request] = request.Resources;
        reason = "accepted";
        return EmbodiedActionStatus.Executing;
    }
    public bool Complete(EmbodiedActionRequest request) => Finish(request, EmbodiedActionStatus.Completed, "completed");
    public bool Cancel(EmbodiedActionRequest request, string reason) => Finish(request, EmbodiedActionStatus.Cancelled, string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason);
    public int ExpireDue(DateTime nowUtc)
    {
        var due = new List<EmbodiedActionRequest>();
        foreach (var pair in _active)
            if (nowUtc.ToUniversalTime() - pair.Key.StartedAtUtc >= pair.Key.Timeout) due.Add(pair.Key);
        foreach (var request in due) Finish(request, EmbodiedActionStatus.TimedOut, "timeout");
        return due.Count;
    }
    private bool Finish(EmbodiedActionRequest request, EmbodiedActionStatus terminalStatus, string reason)
    {
        if (request == null || !_active.TryGetValue(request, out var resources)) return false;
        _active.Remove(request);
        _occupied &= ~resources;
        request.Status = terminalStatus;
        request.TerminalReason = reason;
        return true;
    }

    // 优先级仲裁（FR-L3-02）：资源冲突时仅允许严格更高优先级的请求，经正常取消
    // 路径抢占全部相交资源持有者；任一持有者优先级不低于请求即维持拒绝。不相交
    // 资源持有者不受影响。生产准入固定 Priority=0，同优先级冲突仍拒绝，运行时行为不变。
    private bool TryPreemptConflicts(EmbodiedActionRequest request)
    {
        var conflicts = new List<EmbodiedActionRequest>();
        foreach (var pair in _active)
        {
            if ((pair.Value & request.Resources) == 0) continue;
            if (pair.Key.Priority >= request.Priority) return false;
            conflicts.Add(pair.Key);
        }
        foreach (var held in conflicts) Finish(held, EmbodiedActionStatus.Cancelled, "preempted");
        return true;
    }
}
