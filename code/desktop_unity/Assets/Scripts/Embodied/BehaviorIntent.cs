using System;
using System.Collections.Generic;

public enum BehaviorExecutionStatus { Rejected, Pending, Executing, Completed, Cancelled, Expired, RecoveryFailed }
public sealed class SkillExecutionHandle
{
    public string ExecutionId { get; set; }
    public string IntentId { get; set; }
    public string SkillId { get; set; }
    public long RequestId { get; set; }
    public EmbodiedResource Resources { get; set; }
    public BehaviorExecutionStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime ExpectedEndAtUtc { get; set; }
    public EmbodiedActionRequest Request { get; set; }
}

public sealed class BehaviorEvent
{
    public string EventId { get; private set; }
    public string Source { get; private set; }
    public string EventType { get; private set; }
    public string Reason { get; private set; }
    public string CorrelationId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public BehaviorEvent(string eventId, string source, string eventType, string reason, string correlationId, DateTime occurredAtUtc, TimeSpan? ttl = null)
    {
        if (occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(occurredAtUtc));
        EventId = eventId;
        Source = source;
        EventType = eventType;
        Reason = reason;
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc;
        ExpiresAtUtc = occurredAtUtc.Add(ttl ?? TimeSpan.FromSeconds(8));
    }

    public string ValidationError(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) return "now-utc-required";
        string error = ValidateToken(EventId, "event-id-required");
        if (error != null) return error;
        error = ValidateToken(Source, "event-source-required");
        if (error != null) return error;
        error = ValidateToken(EventType, "event-type-required");
        if (error != null) return error;
        error = ValidateToken(Reason, "event-reason-required");
        if (error != null) return error;
        error = ValidateToken(CorrelationId, "event-correlation-required");
        if (error != null) return error;
        if (ExpiresAtUtc <= OccurredAtUtc) return "event-ttl-required";
        return null;
    }

    public bool IsExpired(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(nowUtc));
        return nowUtc > ExpiresAtUtc;
    }

    private static string ValidateToken(string value, string emptyReason)
    {
        if (string.IsNullOrWhiteSpace(value)) return emptyReason;
        if (value.Length > 96 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            return emptyReason + "-invalid";
        return null;
    }
}

public sealed class BehaviorIntent
{
    public string IntentId { get; internal set; }
    public string IntentType { get; internal set; }
    public string Source { get; internal set; }
    public string Reason { get; internal set; }
    public string CorrelationId { get; internal set; }
    public int Priority { get; internal set; }
    public string Attention { get; internal set; }
    public string BodySkill { get; internal set; }
    public string SpeechMode { get; internal set; }
    public string EmotionBias { get; internal set; }
    public SkillExecutionHandle ExecutionHandle { get; internal set; }
    public bool Interruptible { get; internal set; }
    public DateTime CreatedAtUtc { get; internal set; }
    public DateTime ExpiresAtUtc { get; internal set; }
}

public sealed class BehaviorExecution
{
    public BehaviorIntent Intent { get; internal set; }
    public SkillExecutionHandle Handle { get; internal set; }
    public BehaviorExecutionStatus Status { get; internal set; }
    public string Reason { get; internal set; }
    public DateTime UpdatedAtUtc { get; internal set; }
    public bool PublishLifeAction { get; internal set; } = true;
}

public sealed class BehaviorCoordinatorSnapshot
{
    public List<BehaviorExecution> Executions { get; private set; }
    public string LastResult { get; private set; }
    internal BehaviorCoordinatorSnapshot(List<BehaviorExecution> executions, string lastResult)
    { Executions = executions; LastResult = lastResult; }
}

public interface IBehaviorAdapter
{
    bool TryStart(BehaviorIntent intent, out string reason);
    bool TryCancel(BehaviorIntent intent, string reason);
    bool TryComplete(BehaviorIntent intent, string reason);
    SkillExecutionHandle GetHandle(BehaviorIntent intent);
}

/// <summary>
/// Legacy 路径目前没有统一执行器。显式拒绝比创建没有真实句柄的 Executing 记录安全。
/// </summary>
public sealed class LegacyBehaviorAdapter : IBehaviorAdapter
{
    public bool TryStart(BehaviorIntent intent, out string reason) { reason = "legacy-adapter-unsupported"; return false; }
    public bool TryCancel(BehaviorIntent intent, string reason) { return false; }
    public bool TryComplete(BehaviorIntent intent, string reason) { return false; }
    public SkillExecutionHandle GetHandle(BehaviorIntent intent) { return null; }
}

public sealed class CertifiedSkillBehaviorAdapter : IBehaviorAdapter
{
    private readonly Func<string, bool> _isAdmissible;
    private readonly Func<string, string> _skillForIntent;
    private readonly Func<string, SkillExecutionHandle> _startSkill;
    private readonly Func<SkillExecutionHandle, string, bool> _cancelSkill;
    private readonly Func<SkillExecutionHandle, string, bool> _completeSkill;
    public static CertifiedSkillBehaviorAdapter Runtime(Func<string, string> skillForIntent = null)
    {
        return new CertifiedSkillBehaviorAdapter(EmbodiedRuntimeAdmission.IsSkillAdmissible, skillForIntent,
            id => { EmbodiedActionRequest request; string reason; return EmbodiedRuntimeAdmission.TryBeginSkill(id, out request, out reason) ? CreateHandle(request) : null; },
            (handle, reason) => { if (handle == null) return false; EmbodiedRuntimeAdmission.CancelSkill(handle.Request, reason); return true; },
            (handle, reason) => { if (handle == null) return false; EmbodiedRuntimeAdmission.CompleteSkill(handle.Request, reason); return true; });
    }
    private static SkillExecutionHandle CreateHandle(EmbodiedActionRequest request)
    {
        return new SkillExecutionHandle { ExecutionId = "execution-" + request.RequestId, SkillId = request.SkillId, RequestId = request.RequestId, Resources = request.Resources, Request = request, Status = BehaviorExecutionStatus.Executing, StartedAtUtc = request.StartedAtUtc, ExpectedEndAtUtc = request.StartedAtUtc.Add(request.Timeout) };
    }
    public CertifiedSkillBehaviorAdapter(Func<string, bool> isAdmissible, Func<string, string> skillForIntent = null, Func<string, SkillExecutionHandle> startSkill = null, Func<SkillExecutionHandle, string, bool> cancelSkill = null, Func<SkillExecutionHandle, string, bool> completeSkill = null) { _isAdmissible = isAdmissible ?? throw new ArgumentNullException(nameof(isAdmissible)); _skillForIntent = skillForIntent ?? (type => null); _startSkill = startSkill ?? (id => null); _cancelSkill = cancelSkill ?? ((handle, reason) => true); _completeSkill = completeSkill ?? ((handle, reason) => true); }
    public bool TryStart(BehaviorIntent intent, out string reason)
    { var skillId = _skillForIntent(intent.IntentType); if (string.IsNullOrWhiteSpace(skillId) || !_isAdmissible(skillId)) { reason = "skill-not-certified"; return false; } var handle = _startSkill(skillId); if (handle == null) { reason = "renderer-not-ready"; return false; } intent.BodySkill = skillId; intent.ExecutionHandle = handle; handle.IntentId = intent.IntentId; if (handle.Request != null) handle.Request.BehaviorExecutionId = handle.ExecutionId; reason = "certified-skill-admitted"; return true; }
    public bool TryCancel(BehaviorIntent intent, string reason) { return intent != null && _cancelSkill(intent.ExecutionHandle, reason); }
    public bool TryComplete(BehaviorIntent intent, string reason) { return intent != null && _completeSkill(intent.ExecutionHandle, reason); }
    public SkillExecutionHandle GetHandle(BehaviorIntent intent) { return intent == null ? null : intent.ExecutionHandle; }
}

public sealed class BehaviorCoordinator
{
    private readonly LifeStateStore _lifeState;
    private readonly IBehaviorAdapter _adapter;
    private readonly HashSet<string> _eventIds = new HashSet<string>();
    private readonly List<BehaviorExecution> _executions = new List<BehaviorExecution>();
    private const int MaxRetainedExecutions = 512;
    private int _executionBaseIndex;
    private long _sequence;
    private string _activeCorrelation;
    private string _lastResult;

    public BehaviorCoordinator(LifeStateStore lifeState, IBehaviorAdapter adapter = null) { _lifeState = lifeState ?? throw new ArgumentNullException(nameof(lifeState)); _adapter = adapter ?? new LegacyBehaviorAdapter(); }
    public BehaviorCoordinatorSnapshot Snapshot
    {
        get { return new BehaviorCoordinatorSnapshot(new List<BehaviorExecution>(_executions), _lastResult); }
    }
    public int ExecutionCount => _executionBaseIndex + _executions.Count;
    public int OldestExecutionIndex => _executionBaseIndex;
    public BehaviorExecution GetExecutionAt(int index)
    { int local = index - _executionBaseIndex; return local >= 0 && local < _executions.Count ? _executions[local] : null; }
    public BehaviorExecution Get(string intentId)
    { return _executions.Find(x => x.Intent.IntentId == intentId); }

    public BehaviorExecution Handle(BehaviorEvent value, DateTime nowUtc)
    {
        if (value == null) return Rejected("event-required", nowUtc);
        string validationError = value.ValidationError(nowUtc);
        if (validationError != null) return Rejected(validationError, nowUtc);
        if (value.IsExpired(nowUtc)) return Rejected("event-expired", nowUtc);
        if (_eventIds.Contains(value.EventId)) return Rejected("duplicate-event", nowUtc);
        if (_executions.Exists(x => x.Intent != null && x.Intent.CorrelationId == value.CorrelationId && IsActive(x.Status)))
            return Rejected("duplicate-correlation", nowUtc);

        var intent = new BehaviorIntent {
            IntentId = "intent-" + (++_sequence).ToString(), IntentType = value.EventType == "brief_arm_raise" ? "brief_arm_raise" : "acknowledge_user", Source = value.Source,
            Reason = value.Reason, CorrelationId = value.CorrelationId, Priority = 60, Attention = "user",
            BodySkill = "legacy_interaction", SpeechMode = "warm_short", EmotionBias = "positive", Interruptible = true,
            CreatedAtUtc = nowUtc, ExpiresAtUtc = value.ExpiresAtUtc
        };
        string adapterReason;
        try
        {
            if (!_adapter.TryStart(intent, out adapterReason)) return Rejected("adapter-rejected:" + adapterReason, nowUtc);
        }
        catch (Exception error)
        {
            return Rejected("adapter-error:" + error.GetType().Name, nowUtc);
        }
        var handle = _adapter.GetHandle(intent);
        if (handle == null || string.IsNullOrWhiteSpace(handle.ExecutionId))
        {
            try { _adapter.TryCancel(intent, "missing-execution-handle"); } catch { }
            return Rejected("adapter-rejected:execution-handle-required", nowUtc);
        }
        handle.IntentId = intent.IntentId;
        handle.Status = BehaviorExecutionStatus.Executing;
        intent.ExecutionHandle = handle;
        var execution = new BehaviorExecution { Intent = intent, Handle = handle, Status = BehaviorExecutionStatus.Executing, Reason = adapterReason, UpdatedAtUtc = nowUtc };
        if (!AppendLife(LifeEventType.ActionStarted, intent.IntentId, "accepted", value.CorrelationId, nowUtc))
        {
            try { _adapter.TryCancel(intent, "life-state-rejected"); } catch { }
            return Rejected("life-state-rejected", nowUtc);
        }
        _executions.Add(execution);
        PruneTerminalExecutions();
        _eventIds.Add(value.EventId);
        _activeCorrelation = value.CorrelationId;
        return execution;
    }

    /// <summary>
    /// 登记一个已经由 Renderer 完成唯一准入并准备启动的真实执行。
    /// 此方法不再次调用认证准入，避免 Coordinator 与 Renderer 双重占用资源。
    /// </summary>
    public BehaviorExecution RegisterAdmittedExecution(string intentType, string source, string reason, string correlationId,
        string bodySkill, EmbodiedActionRequest request, DateTime nowUtc, int priority = 60, bool interruptible = true)
    {
        if (request == null || request.Status != EmbodiedActionStatus.Executing || request.RequestId <= 0) return null;
        if (nowUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(intentType) || string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(correlationId) || string.IsNullOrWhiteSpace(bodySkill)) return null;
        if (_executions.Exists(x => x.Intent != null && x.Intent.CorrelationId == correlationId && IsActive(x.Status))) return null;
        if (_executions.Exists(x => x.Handle != null && x.Handle.Request != null
            && (x.Handle.RequestId == request.RequestId || x.Handle.ExecutionId == request.BehaviorExecutionId))) return null;

        string executionId = string.IsNullOrWhiteSpace(request.BehaviorExecutionId)
            ? "execution-" + request.RequestId.ToString()
            : request.BehaviorExecutionId;
        var intent = new BehaviorIntent {
            IntentId = "intent-" + (++_sequence).ToString(), IntentType = intentType, Source = source,
            Reason = reason ?? "admitted-body-skill", CorrelationId = correlationId, Priority = priority,
            Attention = "user", BodySkill = bodySkill, SpeechMode = "none", EmotionBias = "neutral",
            Interruptible = interruptible, CreatedAtUtc = nowUtc,
            ExpiresAtUtc = request.StartedAtUtc.Add(request.Timeout)
        };
        var handle = new SkillExecutionHandle {
            ExecutionId = executionId, IntentId = intent.IntentId, SkillId = request.SkillId,
            RequestId = request.RequestId, Resources = request.Resources, Request = request,
            Status = BehaviorExecutionStatus.Executing, StartedAtUtc = request.StartedAtUtc,
            ExpectedEndAtUtc = request.StartedAtUtc.Add(request.Timeout)
        };
        request.BehaviorExecutionId = executionId;
        intent.ExecutionHandle = handle;
        var execution = new BehaviorExecution { Intent = intent, Handle = handle, Status = BehaviorExecutionStatus.Executing,
            Reason = intent.Reason, UpdatedAtUtc = nowUtc };
        if (!AppendLife(LifeEventType.ActionStarted, intent.IntentId, bodySkill, correlationId, nowUtc))
        {
            request.BehaviorExecutionId = null;
            return null;
        }
        _executions.Add(execution);
        PruneTerminalExecutions();
        _activeCorrelation = correlationId;
        return execution;
    }

    /// <summary>登记已由输入协调器受理的旧写入者；句柄不冒充认证技能准入。</summary>
    public BehaviorExecution RegisterInputExecution(string intentType, string source, string reason,
        string correlationId, string semanticName, Live2DInputLease lease, DateTime nowUtc,
        bool publishLifeAction = true)
    {
        if (!lease.IsValid || lease.Resources == EmbodiedResource.None || nowUtc.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(intentType) || string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(correlationId) || string.IsNullOrWhiteSpace(semanticName)) return null;
        if (_executions.Exists(x => x.Intent != null && x.Intent.CorrelationId == correlationId && IsActive(x.Status))) return null;
        string executionId = "input-" + lease.Kind.ToString().ToLowerInvariant() + "-" + lease.RequestId;
        if (_executions.Exists(x => x.Handle != null && x.Handle.ExecutionId == executionId)) return null;
        var intent = new BehaviorIntent {
            IntentId = "intent-" + (++_sequence), IntentType = intentType, Source = source,
            Reason = reason, CorrelationId = correlationId, Priority = 0,
            Attention = "user", BodySkill = semanticName, SpeechMode = "none", EmotionBias = "neutral",
            Interruptible = true, CreatedAtUtc = nowUtc, ExpiresAtUtc = DateTime.MaxValue
        };
        var handle = new SkillExecutionHandle {
            ExecutionId = executionId, IntentId = intent.IntentId, SkillId = null,
            RequestId = 0, Resources = lease.Resources, Request = null,
            Status = BehaviorExecutionStatus.Executing, StartedAtUtc = nowUtc,
            ExpectedEndAtUtc = DateTime.MaxValue
        };
        intent.ExecutionHandle = handle;
        var execution = new BehaviorExecution { Intent = intent, Handle = handle,
            Status = BehaviorExecutionStatus.Executing, Reason = reason, UpdatedAtUtc = nowUtc,
            PublishLifeAction = publishLifeAction };
        if (publishLifeAction && !AppendLife(LifeEventType.ActionStarted, intent.IntentId, semanticName, correlationId, nowUtc)) return null;
        _executions.Add(execution);
        PruneTerminalExecutions();
        _activeCorrelation = correlationId;
        return execution;
    }

    public bool RenewInputExecution(string executionId, DateTime nowUtc, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(executionId) || nowUtc.Kind != DateTimeKind.Utc
            || ttl <= TimeSpan.Zero || _activeCorrelation == null)
            return false;

        var execution = _executions.Find(x => x.Handle != null && x.Handle.ExecutionId == executionId);
        if (execution == null || execution.Status != BehaviorExecutionStatus.Executing
            || execution.Handle.Request != null || !execution.PublishLifeAction
            || execution.Intent == null || execution.Intent.CorrelationId != _activeCorrelation)
            return false;

        return _lifeState.RenewActiveAction(execution.Intent.CorrelationId, nowUtc, ttl);
    }

    private void PruneTerminalExecutions()
    {
        while (_executions.Count > MaxRetainedExecutions && !IsActive(_executions[0].Status))
        {
            _executions.RemoveAt(0);
            _executionBaseIndex++;
        }
    }

    public bool Cancel(string intentId, DateTime nowUtc, string reason)
    {
        var e = Get(intentId);
        if (e == null || e.Status != BehaviorExecutionStatus.Executing) return false;
        if (!_adapter.TryCancel(e.Intent, reason)) return RecordTerminal(e.Handle == null ? null : e.Handle.ExecutionId, BehaviorExecutionStatus.RecoveryFailed, reason + "-cancel-failed", nowUtc);
        return SetTerminal(e, BehaviorExecutionStatus.Cancelled, reason, nowUtc);
    }

    public bool Complete(string intentId, DateTime nowUtc, string reason)
    {
        var e = Get(intentId);
        if (e == null || e.Status != BehaviorExecutionStatus.Executing || !_adapter.TryComplete(e.Intent, reason)) return false;
        return SetTerminal(e, BehaviorExecutionStatus.Completed, reason, nowUtc);
    }

    public bool TakeoverByDrag(string intentId, DateTime nowUtc)
    {
        var e = Get(intentId);
        if (e == null || e.Status != BehaviorExecutionStatus.Executing || !e.Intent.Interruptible) return false;
        if (!_adapter.TryCancel(e.Intent, "user-drag-takeover")) return RecordTerminal(e.Handle == null ? null : e.Handle.ExecutionId, BehaviorExecutionStatus.RecoveryFailed, "user-drag-takeover-cancel-failed", nowUtc);
        return SetTerminal(e, BehaviorExecutionStatus.Cancelled, "user-drag-takeover", nowUtc);
    }

    public bool RecordTerminal(string executionId, BehaviorExecutionStatus status, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(executionId)) return false;
        var e = _executions.Find(x => x.Handle != null && x.Handle.ExecutionId == executionId);
        if (e == null || e.Status != BehaviorExecutionStatus.Executing) return false;
        if (status != BehaviorExecutionStatus.Completed && status != BehaviorExecutionStatus.Cancelled && status != BehaviorExecutionStatus.Expired && status != BehaviorExecutionStatus.RecoveryFailed) return false;
        return SetTerminal(e, status, reason, nowUtc);
    }

    public bool RecordDragEnded(string correlationId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(correlationId) || !_executions.Exists(x => x.Intent != null && x.Intent.CorrelationId == correlationId)) return false;
        _lastResult = "drag-ended-recovered";
        AppendLife(LifeEventType.DirectInteraction, "drag-end", _lastResult, correlationId, nowUtc);
        return true;
    }

    public int Expire(DateTime nowUtc)
    {
        var count = 0;
        foreach (var e in _executions)
        {
            if (e.Status != BehaviorExecutionStatus.Executing || nowUtc <= e.Intent.ExpiresAtUtc) continue;
            if (!_adapter.TryCancel(e.Intent, "expired"))
            {
                if (SetTerminal(e, BehaviorExecutionStatus.RecoveryFailed, "expired-cancel-failed", nowUtc)) count++;
                continue;
            }
            if (SetTerminal(e, BehaviorExecutionStatus.Expired, "expired", nowUtc)) count++;
        }
        return count;
    }

    private static bool IsActive(BehaviorExecutionStatus status)
    { return status == BehaviorExecutionStatus.Pending || status == BehaviorExecutionStatus.Executing; }

    private bool SetTerminal(BehaviorExecution execution, BehaviorExecutionStatus status, string reason, DateTime nowUtc)
    {
        if (execution == null || execution.Status != BehaviorExecutionStatus.Executing || nowUtc.Kind != DateTimeKind.Utc) return false;
        execution.Status = status;
        if (execution.Handle != null) execution.Handle.Status = status;
        execution.Reason = string.IsNullOrWhiteSpace(reason) ? status.ToString() : reason;
        execution.UpdatedAtUtc = nowUtc;
        _lastResult = execution.Reason;
        LifeEventType type = status == BehaviorExecutionStatus.Completed
            ? LifeEventType.ActionCompleted
            : status == BehaviorExecutionStatus.RecoveryFailed ? LifeEventType.ActionRecoveryFailed : LifeEventType.ActionInterrupted;
        // Atomic lease handoff can register the new owner before ending the
        // old one. Keep the old execution terminal without overwriting the
        // newer action shown by LifeState.
        if (execution.PublishLifeAction && _activeCorrelation == execution.Intent.CorrelationId)
            AppendLife(type, execution.Intent.IntentId, execution.Reason, execution.Intent.CorrelationId, nowUtc);
        if (_activeCorrelation == execution.Intent.CorrelationId) _activeCorrelation = null;
        return true;
    }

    private BehaviorExecution Rejected(string reason, DateTime nowUtc)
    {
        _lastResult = reason;
        return new BehaviorExecution { Intent = new BehaviorIntent { IntentId = "rejected-" + (++_sequence).ToString(), IntentType = "none", CreatedAtUtc = nowUtc, ExpiresAtUtc = nowUtc }, Status = BehaviorExecutionStatus.Rejected, Reason = reason, UpdatedAtUtc = nowUtc };
    }

    private bool AppendLife(LifeEventType type, string id, string summary, string correlation, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) return false;
        string safeId = Bound(id, 80);
        string safeSummary = Bound(summary, 160);
        string safeCorrelation = Bound(correlation, 96);
        try
        {
            return _lifeState.Append(new LifeEvent("behavior-" + safeId + "-" + type, type, nowUtc, nowUtc.AddSeconds(30), "behavior-coordinator", safeCorrelation, 60, safeSummary), nowUtc);
        }
        catch { return false; }
    }

    private static string Bound(string value, int max)
    {
        string normalized = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= max ? normalized : normalized.Substring(0, max);
    }
}
