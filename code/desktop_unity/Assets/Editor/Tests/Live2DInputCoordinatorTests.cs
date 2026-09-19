using NUnit.Framework;
using UnityEngine.TestTools;

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

    [Test]
    public void CertifiedWriter_ExposesDeclaredResourcesAndControlLevel()
    {
        var coordinator = new Live2DInputCoordinator();

        Assert.That(coordinator.TryBegin(Live2DInputKind.GeneratedMotion, "certified-motion", "m06", out var lease), Is.True);

        Assert.That(lease.WriterId, Is.EqualTo("certified-motion"));
        Assert.That(lease.Resources & EmbodiedResource.Body, Is.Not.EqualTo(EmbodiedResource.None));
        Assert.That(lease.ControlLevel, Is.EqualTo(BodyWriterControlLevel.CertifiedCoordinator));
    }

    [Test]
    public void UnknownWriter_IsRejectedWithoutAcquiringLease()
    {
        var coordinator = new Live2DInputCoordinator();
        LogAssert.Expect(UnityEngine.LogType.Warning,
            "[Live2DInputCoordinator] Rejected GeneratedMotion/test: unknown-writer=unregistered-writer");

        Assert.That(coordinator.TryBegin(Live2DInputKind.GeneratedMotion, "unregistered-writer", "test", out var lease), Is.False);
        Assert.That(lease.IsValid, Is.False);
        Assert.That(coordinator.HasActiveLease, Is.False);
    }

    [Test]
    public void ReleaseAll_IsIdempotentAndAllowsNextExpressionLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "expression", "happy", out var first), Is.True);

        coordinator.ReleaseAll("renderer-disabled");
        coordinator.ReleaseAll("renderer-destroyed");

        Assert.That(coordinator.HasActiveLease, Is.False);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "expression", "sad", out var second), Is.True);
        Assert.That(second.RequestId, Is.GreaterThan(first.RequestId));
    }

    [Test]
    public void ExpressionLease_ExposesFaceInputMetadata()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "expression", "happy", out var lease), Is.True);

        Assert.That(lease.WriterId, Is.EqualTo("expression"));
        Assert.That(lease.Resources, Is.EqualTo(EmbodiedResource.Face));
        Assert.That(lease.ControlLevel, Is.EqualTo(BodyWriterControlLevel.InputLeaseOnly));
    }

    [Test]
    public void StaleExpressionRelease_CannotReleaseNewLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "expression", "happy", out var oldLease), Is.True);
        Assert.That(coordinator.Release(oldLease, "transition"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "expression", "sad", out var currentLease), Is.True);

        Assert.That(coordinator.Release(oldLease, "stale-delayed-callback"), Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(currentLease.RequestId));
    }

    [Test]
    public void LowPriorityOverlay_IsSuppressedWhileAnyRegisteredWriterOwnsInput()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.CanApplyLowPriorityOverlay, Is.True);

        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out var expression), Is.True);
        Assert.That(coordinator.CanApplyLowPriorityOverlay, Is.False);

        Assert.That(coordinator.Release(expression, "expression-stopped"), Is.True);
        Assert.That(coordinator.CanApplyLowPriorityOverlay, Is.True);
    }
}
