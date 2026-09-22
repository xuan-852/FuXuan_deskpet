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
    public void WalkingLease_UsesWalkPoseWriterMetadata()
    {
        var coordinator = new Live2DInputCoordinator();

        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "ground-task", out var lease), Is.True);

        Assert.That(lease.WriterId, Is.EqualTo("walk-pose"));
        Assert.That(lease.Resources & EmbodiedResource.Movement, Is.Not.EqualTo(EmbodiedResource.None));
        Assert.That(lease.Resources & EmbodiedResource.Body, Is.Not.EqualTo(EmbodiedResource.None));
        Assert.That(lease.ControlLevel, Is.EqualTo(BodyWriterControlLevel.InputLeaseOnly));
    }

    [Test]
    public void WalkingLease_IsMutuallyExclusiveWithOtherInputs()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "ground-task", out var walking), Is.True);

        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out _), Is.False);
        Assert.That(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch", out _), Is.False);
        Assert.That(coordinator.TryBegin(Live2DInputKind.GeneratedMotion, "wave", out _), Is.False);
        Assert.That(coordinator.CanApplyLowPriorityOverlay, Is.False);

        Assert.That(coordinator.Release(walking, "ground-task-stopped"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out var expression), Is.True);
        Assert.That(coordinator.Release(expression, "expression-stopped"), Is.True);
    }

    [Test]
    public void StaleWalkingLease_CannotReleaseReplacementLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "first", out var oldLease), Is.True);
        Assert.That(coordinator.Release(oldLease, "stopped"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "second", out var currentLease), Is.True);

        Assert.That(coordinator.Release(oldLease, "stale-stop"), Is.False);
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

    [Test]
    public void DesktopPhysicsLease_ExposesDeclaredMetadata()
    {
        var coordinator = new Live2DInputCoordinator();

        Assert.That(coordinator.TryBegin(Live2DInputKind.DesktopPhysics, "physics-update", out var lease), Is.True);
        Assert.That(lease.WriterId, Is.EqualTo("desktop-physics"));
        Assert.That(lease.Resources, Is.EqualTo(EmbodiedResource.Movement | EmbodiedResource.Body | EmbodiedResource.Effect));
        Assert.That(lease.ControlLevel, Is.EqualTo(BodyWriterControlLevel.InputLeaseOnly));
    }

    [Test]
    public void DesktopPhysicsLease_IsMutuallyExclusiveWithWalkingAndReacquiresAfterRelease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.DesktopPhysics, "physics-update", out var physics), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking), Is.False);
        Assert.That(walking.IsValid, Is.False);

        Assert.That(coordinator.Release(physics, "physics-complete"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out walking), Is.True);
        Assert.That(walking.RequestId, Is.GreaterThan(physics.RequestId));
    }

    [Test]
    public void StaleDesktopPhysicsRelease_CannotReleaseReplacementLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.DesktopPhysics, "first", out var oldLease), Is.True);
        Assert.That(coordinator.Release(oldLease, "complete"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.DesktopPhysics, "second", out var currentLease), Is.True);

        Assert.That(coordinator.Release(oldLease, "stale"), Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(currentLease.RequestId));
        coordinator.ReleaseAll("teardown");
        Assert.That(coordinator.HasActiveLease, Is.False);
    }

    [Test]
    public void DragResponseLease_ExposesDeclaredMetadata()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.DragResponse, "drag-response", out var lease), Is.True);
        Assert.That(lease.WriterId, Is.EqualTo("drag-response"));
        Assert.That(lease.Resources & EmbodiedResource.Movement, Is.Not.EqualTo(EmbodiedResource.None));
        Assert.That(lease.ControlLevel, Is.EqualTo(BodyWriterControlLevel.InputLeaseOnly));
        Assert.That(BodyWriterInventory.TryGet("drag-response", out var writer), Is.True);
        Assert.That(writer.RecoveryOwner, Is.EqualTo("ReleaseDragResponseInputLease"));
    }

    [Test]
    public void DragResponseLease_IsExclusiveAndCanReacquireAfterRelease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.DragResponse, "drag-response", out var drag), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking), Is.False);
        Assert.That(walking.IsValid, Is.False);
        Assert.That(coordinator.Release(drag, "drag-release"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out walking), Is.True);
        Assert.That(coordinator.Release(walking, "walking-stop"), Is.True);
    }

    [Test]
    public void StaleDragResponseRelease_CannotReleaseReplacementLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.DragResponse, "drag-response", out var oldLease), Is.True);
        Assert.That(coordinator.Release(oldLease, "drag-release"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.DragResponse, "drag-response", out var currentLease), Is.True);
        Assert.That(coordinator.Release(oldLease, "stale-abort"), Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(currentLease.RequestId));
        coordinator.ReleaseAll("teardown");
    }

    [Test]
    public void WalkingLease_RemainsActiveUntilExplicitStopRelease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking), Is.True);

        // The renderer owns the lease through its stop blend; the coordinator must
        // not infer a release merely because another caller is waiting.
        Assert.That(coordinator.HasActiveLease, Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out _), Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(walking.RequestId));

        Assert.That(coordinator.Release(walking, "walking-stopped-after-blend"), Is.True);
        Assert.That(coordinator.HasActiveLease, Is.False);
    }

    [Test]
    public void WalkingLease_CanBeReacquiredWithNewRequestAfterStop()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "first-walk", out var first), Is.True);
        Assert.That(coordinator.Release(first, "walking-stopped-after-blend"), Is.True);

        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "second-walk", out var second), Is.True);
        Assert.That(second.RequestId, Is.GreaterThan(first.RequestId));
        Assert.That(second.WriterId, Is.EqualTo("walk-pose"));
        Assert.That(coordinator.Release(second, "walking-stopped-after-blend"), Is.True);
    }

    [Test]
    public void WalkingLease_CanAtomicallyHandoffToDragResponse()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking), Is.True);

        Assert.That(coordinator.TryHandoffWalkingToDragResponse(
            walking, "drag-response", out var drag), Is.True);
        Assert.That(drag.IsValid, Is.True);
        Assert.That(drag.Kind, Is.EqualTo(Live2DInputKind.DragResponse));
        Assert.That(drag.RequestId, Is.GreaterThan(walking.RequestId));
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(drag.RequestId));

        Assert.That(coordinator.Release(walking, "stale-walking-release"), Is.False);
        Assert.That(coordinator.Release(drag, "drag-release"), Is.True);
    }

    [Test]
    public void WalkingHandoff_DoesNotPreemptNonWalkingLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.GeneratedMotion,
            "certified-motion", "motion", out var active), Is.True);

        var fakeWalking = new Live2DInputLease(active.RequestId,
            Live2DInputKind.Walking, "walk-pose", "walking-state",
            EmbodiedResource.Movement, BodyWriterControlLevel.InputLeaseOnly);
        Assert.That(coordinator.TryHandoffWalkingToDragResponse(
            fakeWalking, "drag-response", out var drag), Is.False);
        Assert.That(drag.IsValid, Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(active.RequestId));
        Assert.That(coordinator.Release(active, "motion-finished"), Is.True);
    }

    [Test]
    public void StaleWalkingHandoff_CannotReplaceCurrentLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var oldWalking), Is.True);
        Assert.That(coordinator.Release(oldWalking, "walking-stopped"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var currentWalking), Is.True);

        Assert.That(coordinator.TryHandoffWalkingToDragResponse(
            oldWalking, "drag-response", out var drag), Is.False);
        Assert.That(drag.IsValid, Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(currentWalking.RequestId));
        Assert.That(coordinator.Release(currentWalking, "walking-stopped"), Is.True);
    }

    [Test]
    public void WalkingHandoff_DragReleaseAllowsWalkingToReacquire()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking), Is.True);
        Assert.That(coordinator.TryHandoffWalkingToDragResponse(
            walking, "drag-response", out var drag), Is.True);
        Assert.That(coordinator.Release(drag, "drag-release"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking,
            "walking-state", out var resumedWalking), Is.True);
        Assert.That(resumedWalking.RequestId, Is.GreaterThan(drag.RequestId));
        Assert.That(coordinator.Release(resumedWalking, "walking-resumed-stop"), Is.True);
    }

    [Test]
    public void WalkingHandoff_RequiresCurrentWalkingLease()
    {
        var coordinator = new Live2DInputCoordinator();
        var invalid = default(Live2DInputLease);

        Assert.That(coordinator.TryHandoffWalkingToDragResponse(
            invalid, "drag-response", out var drag), Is.False);
        Assert.That(drag.IsValid, Is.False);
        Assert.That(coordinator.HasActiveLease, Is.False);
    }

    [Test]
    public void InterruptToDragResponse_ReplacesCurrentCancellableLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.LegacyAction,
            "legacy-action", out var action), Is.True);

        Assert.That(coordinator.TryInterruptToDragResponse(
            action, "drag-response", out var drag), Is.True);
        Assert.That(drag.Kind, Is.EqualTo(Live2DInputKind.DragResponse));
        Assert.That(drag.RequestId, Is.GreaterThan(action.RequestId));
        Assert.That(coordinator.Release(action, "stale-action-release"), Is.False);
        Assert.That(coordinator.Release(drag, "drag-release"), Is.True);
    }

    [Test]
    public void InterruptToDragResponse_RequiresCurrentLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression,
            "expression", out var current), Is.True);
        var stale = new Live2DInputLease(current.RequestId + 1,
            Live2DInputKind.LegacyAction, "legacy-action", "stale",
            EmbodiedResource.Body, BodyWriterControlLevel.InputLeaseOnly);

        Assert.That(coordinator.TryInterruptToDragResponse(
            stale, "drag-response", out var drag), Is.False);
        Assert.That(drag.IsValid, Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(current.RequestId));
        Assert.That(coordinator.Release(current, "expression-stopped"), Is.True);
    }

    [Test]
    public void WalkingLease_IsRejectedWhileAnotherInputOwnsCoordinator()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch", out var action), Is.True);

        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking), Is.False);
        Assert.That(walking.IsValid, Is.False);
        Assert.That(coordinator.ActiveLease.RequestId, Is.EqualTo(action.RequestId));

        Assert.That(coordinator.Release(action, "action-finished"), Is.True);
        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out walking), Is.True);
        Assert.That(coordinator.Release(walking, "walking-stopped"), Is.True);
    }

    [Test]
    public void WalkingLease_RetryAfterBlockingInputReleaseGetsNewLease()
    {
        var coordinator = new Live2DInputCoordinator();
        Assert.That(coordinator.TryBegin(Live2DInputKind.Expression, "expression", out var expression), Is.True);

        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var blocked), Is.False);
        Assert.That(blocked.IsValid, Is.False);
        Assert.That(coordinator.Release(expression, "expression-transition"), Is.True);

        Assert.That(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking), Is.True);
        Assert.That(walking.RequestId, Is.GreaterThan(expression.RequestId));
        Assert.That(coordinator.Release(walking, "walking-stopped"), Is.True);
    }
}
