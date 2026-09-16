using NUnit.Framework;

public class Live2DInputCoordinatorTests
{
    [Test]
    public void SecondRequest_IsRejectedWhileFirstLeaseIsActive()
    {
        var coordinator = new Live2DInputCoordinator();

        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out var first), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch", out var second), Is.False);

        Assert.That(first.IsValid, Is.True);
        Assert.That(second.IsValid, Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(first.RequestId));
    }

    [Test]
    public void NonOwnerRelease_CannotClearCurrentLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.GeneratedMotion, "wave", out var active), Is.True);

        var stale = new Live2DInputLease(active.RequestId + 1, Live2DInputKind.Expression, "stale");
        Assert.That(coordinator.Release(stale, "stale-release"), Is.False);
        Assert.That(coordinator.HasActiveLease, Is.True);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(active.RequestId));
    }

    [Test]
    public void Release_AllowsNextRequestAndKeepsMonotonicRequestId()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out var first), Is.True);
        Assert.That(coordinator.Release(first, "completed"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch", out var second), Is.True);

        Assert.That(second.RequestId, Is.GreaterThan(first.RequestId));
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(second.RequestId));
    }

    [Test]
    public void ReleaseAll_ClearsActiveLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch", out _), Is.True);

        coordinator.ReleaseAll("renderer-destroyed");

        Assert.That(coordinator.HasActiveLease, Is.False);
    }
}
