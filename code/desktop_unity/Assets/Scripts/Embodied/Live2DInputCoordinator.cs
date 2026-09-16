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

    public bool TryBegin(Live2DInputKind kind, string owner, out Live2DInputLease lease)
    {
        lease = default;
        if (_activeLease.IsValid)
        {
            Debug.Log($"[Live2DInputCoordinator] Rejected {kind}/{owner}: active="
                + $"{_activeLease.Kind}/{_activeLease.Owner}#{_activeLease.RequestId}");
            return false;
        }

        lease = new Live2DInputLease(_nextRequestId++, kind, owner ?? "unknown");
        _activeLease = lease;
        Debug.Log($"[Live2DInputCoordinator] Accepted {lease.Kind}/{lease.Owner}#{lease.RequestId}");
        return true;
    }

    public bool Release(Live2DInputLease lease, string reason)
    {
        if (!lease.IsValid || !_activeLease.IsValid || lease.RequestId != _activeLease.RequestId)
            return false;

        Debug.Log($"[Live2DInputCoordinator] Released {_activeLease.Kind}/{_activeLease.Owner}"
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
    public readonly string Owner;

    public bool IsValid => RequestId > 0;

    public Live2DInputLease(int requestId, Live2DInputKind kind, string owner)
    {
        RequestId = requestId;
        Kind = kind;
        Owner = owner;
    }
}
