using UnityEngine;

/// <summary>
/// Runtime arbiter for externally requested Live2D input.
/// A single global lease is intentional: resource-level composition is not
/// enabled until it has been separately verified.
/// </summary>
[DisallowMultipleComponent]
public sealed class Live2DInputCoordinatorHost : MonoBehaviour
{
    private readonly Live2DInputCoordinator _coordinator = new Live2DInputCoordinator();
    private readonly System.Collections.Generic.Dictionary<int, string> _behaviorExecutions =
        new System.Collections.Generic.Dictionary<int, string>();
    private string _physicsExecutionId;
    private int _lastPhysicsFrame = -1;
    private float _nextLifeActionRenewalTime;

    internal Live2DInputCoordinator Coordinator => _coordinator;

    private void Awake()
    {
        _coordinator.OnLeaseStarting = RegisterBehaviorExecution;
        _coordinator.OnLeaseEnded = CompleteBehaviorExecution;
    }

    private bool RegisterBehaviorExecution(Live2DInputLease lease)
    {
        // A frame-scoped physics session must finish before the next body's
        // ActionStarted event, otherwise its delayed terminal hides that action.
        if (lease.Kind != Live2DInputKind.DesktopPhysics && _physicsExecutionId != null)
            CompletePhysicsExecution("physics-interrupted");
        // These two owners register after playback or certified admission succeeds.
        if (lease.Kind == Live2DInputKind.Expression
            || lease.Kind == Live2DInputKind.CandidateTest
            || (lease.Kind == Live2DInputKind.GeneratedMotion
                && lease.Owner.StartsWith("certified-motion:", System.StringComparison.Ordinal)))
            return true;

        var renderer = GetComponent<Live2DRenderer>();
        var behavior = renderer == null ? null : renderer.BehaviorCoordinator;
        if (behavior == null) return false;
        if (lease.Kind == Live2DInputKind.DesktopPhysics && _physicsExecutionId != null)
        {
            _lastPhysicsFrame = Time.frameCount;
            return true;
        }
        var execution = behavior.RegisterInputExecution(
            lease.WriterId.Replace('-', '_'), lease.WriterId, "input-lease-accepted",
            "input-lease-" + lease.RequestId, lease.Owner, lease, System.DateTime.UtcNow,
            lease.Kind != Live2DInputKind.DesktopPhysics);
        if (execution == null) return false;
        if (lease.Kind == Live2DInputKind.DesktopPhysics)
        {
            _physicsExecutionId = execution.Handle.ExecutionId;
            _lastPhysicsFrame = Time.frameCount;
        }
        else _behaviorExecutions[lease.RequestId] = execution.Handle.ExecutionId;
        return true;
    }

    private void CompleteBehaviorExecution(Live2DInputLease lease, string reason)
    {
        if (lease.Kind == Live2DInputKind.DesktopPhysics)
        {
            if (reason != "physics-update-complete" && reason != "frame-boundary")
                CompletePhysicsExecution(reason);
            return;
        }
        if (!_behaviorExecutions.TryGetValue(lease.RequestId, out string executionId)) return;
        _behaviorExecutions.Remove(lease.RequestId);
        var renderer = GetComponent<Live2DRenderer>();
        var behavior = renderer == null ? null : renderer.BehaviorCoordinator;
        if (behavior == null) return;
        bool completed = reason == "action-completed" || reason == "drag-release"
            || reason == "idle-action-reset" || reason == "walking-stopped"
            || reason == "walking-settled" || reason == "physics-completed"
            || reason == "generated-motion-completed"
            || reason == "vision-verification-motion-completed";
        var status = reason != null && reason.Contains("timeout")
            ? BehaviorExecutionStatus.Expired
            : completed ? BehaviorExecutionStatus.Completed : BehaviorExecutionStatus.Cancelled;
        behavior.RecordTerminal(executionId, status, reason, System.DateTime.UtcNow);
    }

    private void LateUpdate()
    {
        if (_physicsExecutionId != null && Time.frameCount - _lastPhysicsFrame > 1)
            CompletePhysicsExecution("physics-idle");

        if (Time.unscaledTime < _nextLifeActionRenewalTime) return;

        Live2DInputLease activeLease = _coordinator.ActiveLease;
        if (!activeLease.IsValid || activeLease.Kind == Live2DInputKind.DesktopPhysics
            || !_behaviorExecutions.TryGetValue(activeLease.RequestId, out string executionId))
            return;

        var renderer = GetComponent<Live2DRenderer>();
        if (renderer?.BehaviorCoordinator?.RenewInputExecution(executionId,
            System.DateTime.UtcNow, System.TimeSpan.FromSeconds(30)) == true)
            _nextLifeActionRenewalTime = Time.unscaledTime + 10f;
    }

    private void CompletePhysicsExecution(string reason)
    {
        if (_physicsExecutionId == null) return;
        var renderer = GetComponent<Live2DRenderer>();
        renderer?.BehaviorCoordinator?.RecordTerminal(_physicsExecutionId,
            reason == "physics-idle" ? BehaviorExecutionStatus.Completed : BehaviorExecutionStatus.Cancelled,
            reason, System.DateTime.UtcNow);
        _physicsExecutionId = null;
        _lastPhysicsFrame = -1;
    }

    internal bool TryBegin(
        Live2DInputKind kind,
        string owner,
        out Live2DInputLease lease)
    {
        return _coordinator.TryBegin(kind, owner, out lease);
    }

    internal bool ReleaseLease(Live2DInputLease lease, string reason)
    {
        return _coordinator.Release(lease, reason);
    }

    internal void ReleaseAllLeases(string reason)
    {
        _coordinator.ReleaseAll(reason);
    }

    // The physics bridge is lifecycle-only; it does not expose model parameters.
    internal bool TryBeginDesktopPhysics(string owner, out Live2DInputLease lease)
    {
        return TryBegin(Live2DInputKind.DesktopPhysics, owner, out lease);
    }

    internal bool ReleaseDesktopPhysics(Live2DInputLease lease, string reason)
    {
        return ReleaseLease(lease, reason);
    }

    internal void ReleaseAllDesktopPhysics(string reason)
    {
        Live2DInputLease active = _coordinator.ActiveLease;
        if (active.IsValid && active.Kind == Live2DInputKind.DesktopPhysics)
            _coordinator.Release(active, reason);
    }

    internal bool TryBeginDragResponse(string owner, out Live2DInputLease lease)
    {
        return TryBegin(Live2DInputKind.DragResponse, owner, out lease);
    }

    internal bool TryHandoffWalkingToDragResponse(
        Live2DInputLease walkingLease,
        string owner,
        out Live2DInputLease dragLease)
    {
        return _coordinator.TryHandoffWalkingToDragResponse(walkingLease, owner, out dragLease);
    }

    internal bool TryInterruptToDragResponse(
        Live2DInputLease currentLease,
        string owner,
        out Live2DInputLease dragLease)
    {
        return _coordinator.TryInterruptToDragResponse(currentLease, owner, out dragLease);
    }

    internal bool ReleaseDragResponse(Live2DInputLease lease, string reason)
    {
        return ReleaseLease(lease, reason);
    }

    internal static Live2DInputCoordinatorHost GetOrCreate(GameObject target)
    {
        if (target == null) return null;
        var host = target.GetComponent<Live2DInputCoordinatorHost>();
        return host != null ? host : target.AddComponent<Live2DInputCoordinatorHost>();
    }
}

public sealed class Live2DInputCoordinator
{
    private int _nextRequestId = 1;
    private Live2DInputLease _activeLease;

    public System.Func<Live2DInputLease, bool> OnLeaseStarting;
    public System.Action<Live2DInputLease, string> OnLeaseEnded;

    public bool HasActiveLease => _activeLease.IsValid;
    public Live2DInputLease ActiveLease => _activeLease;

    // 鼠标注视是低优先级连续叠加层，不取得全局租约；当任意受控输入
    // 正在持有租约时，它必须停止直接写眼球参数，避免覆盖表情或动作。
    public bool CanApplyLowPriorityOverlay => !_activeLease.IsValid;

    public bool TryBegin(Live2DInputKind kind, string owner, out Live2DInputLease lease)
    {
        return TryBegin(kind, DefaultWriterId(kind), owner, out lease);
    }

    // 输入租约与写入者清册在此汇合。此阶段仍保留单全局租约，故没有开放
    // 未经验证的资源并行；资源字段用于可观测性和后续迁移，不改变原互斥语义。
    public bool TryBegin(Live2DInputKind kind, string writerId, string owner, out Live2DInputLease lease)
    {
        lease = default;
        if (!BodyWriterInventory.TryGet(writerId, out var writer))
        {
            Debug.LogWarning($"[Live2DInputCoordinator] Rejected {kind}/{owner}: unknown-writer={writerId}");
            return false;
        }
        if (writer.ControlLevel == BodyWriterControlLevel.InternalOnly)
        {
            Debug.LogWarning($"[Live2DInputCoordinator] Rejected {kind}/{owner}: writer-not-leasable={writerId}");
            return false;
        }
        if (_activeLease.IsValid)
        {
            Debug.Log($"[Live2DInputCoordinator] Rejected {kind}/{owner}: active="
                + $"{_activeLease.Kind}/{_activeLease.Owner}#{_activeLease.RequestId}");
            return false;
        }

        lease = new Live2DInputLease(_nextRequestId++, kind, writer.WriterId, owner ?? "unknown", writer.Resources, writer.ControlLevel);
        if (OnLeaseStarting != null && !OnLeaseStarting(lease))
        {
            Debug.LogWarning($"[Live2DInputCoordinator] Rejected {kind}/{owner}: behavior-registration-failed");
            lease = default;
            return false;
        }
        _activeLease = lease;
        Debug.Log($"[Live2DInputCoordinator] Accepted {lease.Kind}/{lease.WriterId}/{lease.Owner}#{lease.RequestId}"
            + $" resources={lease.Resources} control={lease.ControlLevel}");
        return true;
    }

    private static string DefaultWriterId(Live2DInputKind kind)
    {
        return kind switch
        {
            Live2DInputKind.Expression => "expression",
            Live2DInputKind.LegacyAction => "legacy-action",
            Live2DInputKind.GeneratedMotion => "generated-motion",
            Live2DInputKind.Walking => "walk-pose",
            Live2DInputKind.DesktopPhysics => "desktop-physics",
            Live2DInputKind.DragResponse => "drag-response",
            _ => "generated-motion"
        };
    }

    public bool Release(Live2DInputLease lease, string reason)
    {
        if (!lease.IsValid || !_activeLease.IsValid || lease.RequestId != _activeLease.RequestId)
            return false;

        Live2DInputLease ended = _activeLease;
        Debug.Log($"[Live2DInputCoordinator] Released {_activeLease.Kind}/{_activeLease.WriterId}/{_activeLease.Owner}"
            + $"#{_activeLease.RequestId}: {reason}");
        _activeLease = default;
        OnLeaseEnded?.Invoke(ended, reason);
        return true;
    }

    public bool TryHandoffWalkingToDragResponse(
        Live2DInputLease walkingLease,
        string owner,
        out Live2DInputLease dragLease)
    {
        dragLease = default;
        if (!walkingLease.IsValid || !_activeLease.IsValid
            || walkingLease.RequestId != _activeLease.RequestId
            || _activeLease.Kind != Live2DInputKind.Walking)
        {
            return false;
        }

        return TryReplaceActiveLeaseWithDragResponse(walkingLease, owner, out dragLease);
    }

    public bool TryInterruptToDragResponse(
        Live2DInputLease currentLease,
        string owner,
        out Live2DInputLease dragLease)
    {
        dragLease = default;
        if (!currentLease.IsValid || !_activeLease.IsValid
            || currentLease.RequestId != _activeLease.RequestId
            || _activeLease.Kind == Live2DInputKind.DragResponse)
        {
            return false;
        }

        return TryReplaceActiveLeaseWithDragResponse(currentLease, owner, out dragLease);
    }

    private bool TryReplaceActiveLeaseWithDragResponse(
        Live2DInputLease currentLease,
        string owner,
        out Live2DInputLease dragLease)
    {
        dragLease = default;
        if (!BodyWriterInventory.TryGet("drag-response", out var writer))
        {
            Debug.LogWarning("[Live2DInputCoordinator] Rejected DragResponse/" + owner
                + ": unknown-writer=drag-response");
            return false;
        }

        Debug.Log($"[Live2DInputCoordinator] Handoff {_activeLease.Kind}/{_activeLease.WriterId}/{_activeLease.Owner}"
            + $"#{_activeLease.RequestId} -> DragResponse/{writer.WriterId}/{owner}");
        dragLease = new Live2DInputLease(_nextRequestId++, Live2DInputKind.DragResponse,
            writer.WriterId, owner ?? "unknown", writer.Resources, writer.ControlLevel);
        if (OnLeaseStarting != null && !OnLeaseStarting(dragLease))
        {
            dragLease = default;
            return false;
        }
        Live2DInputLease ended = _activeLease;
        _activeLease = dragLease;
        OnLeaseEnded?.Invoke(ended, "drag-handoff");
        Debug.Log($"[Live2DInputCoordinator] Accepted {dragLease.Kind}/{dragLease.WriterId}/{dragLease.Owner}"
            + $"#{dragLease.RequestId} resources={dragLease.Resources} control={dragLease.ControlLevel}");
        return true;
    }

    public void ReleaseAll(string reason)
    {
        if (_activeLease.IsValid)
            Release(_activeLease, reason);
    }
}

public enum Live2DInputKind
{
    Expression,
    LegacyAction,
    GeneratedMotion,
    Walking,
    CandidateTest,
    DesktopPhysics,
    DragResponse,
}

public readonly struct Live2DInputLease
{
    public readonly int RequestId;
    public readonly Live2DInputKind Kind;
    public readonly string WriterId;
    public readonly string Owner;
    public readonly EmbodiedResource Resources;
    public readonly BodyWriterControlLevel ControlLevel;

    public bool IsValid => RequestId > 0;

    public Live2DInputLease(int requestId, Live2DInputKind kind, string owner)
        : this(requestId, kind, "legacy/unknown", owner, EmbodiedResource.None, BodyWriterControlLevel.LegacyUnmanaged)
    {
    }

    public Live2DInputLease(int requestId, Live2DInputKind kind, string writerId, string owner,
        EmbodiedResource resources, BodyWriterControlLevel controlLevel)
    {
        RequestId = requestId;
        Kind = kind;
        WriterId = writerId;
        Owner = owner;
        Resources = resources;
        ControlLevel = controlLevel;
    }
}
