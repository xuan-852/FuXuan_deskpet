using UnityEngine;

/// <summary>
/// Runtime arbiter for externally requested Live2D input.
/// A single global lease is intentional: resource-level composition is not
/// enabled until it has been separately verified.
/// </summary>
public sealed class Live2DInputCoordinator
{
    private int _nextRequestId = 1;
    private Live2DInputLease _activeLease;

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
        if (_activeLease.IsValid)
        {
            Debug.Log($"[Live2DInputCoordinator] Rejected {kind}/{owner}: active="
                + $"{_activeLease.Kind}/{_activeLease.Owner}#{_activeLease.RequestId}");
            return false;
        }

        lease = new Live2DInputLease(_nextRequestId++, kind, writer.WriterId, owner ?? "unknown", writer.Resources, writer.ControlLevel);
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
            _ => "generated-motion"
        };
    }

    public bool Release(Live2DInputLease lease, string reason)
    {
        if (!lease.IsValid || !_activeLease.IsValid || lease.RequestId != _activeLease.RequestId)
            return false;

        Debug.Log($"[Live2DInputCoordinator] Released {_activeLease.Kind}/{_activeLease.WriterId}/{_activeLease.Owner}"
            + $"#{_activeLease.RequestId}: {reason}");
        _activeLease = default;
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
    CandidateTest,
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
