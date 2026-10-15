using NUnit.Framework;

/// <summary>
/// T-0006 bounded repeat-sequence regression for <see cref="Live2DInputCoordinator"/>.
///
/// Frozen caps and convergence criteria live in
/// .workflow/runs/drag-recovery-hardening/evidence/T-0006-repeat-sequence-criteria.md
/// and MUST NOT be relaxed after landing.
///
/// These tests drive only the public coordinator API and deliberately avoid a real
/// mouse, a running player process, and scene objects, so they are deterministic
/// under -batchmode -nographics. Every writer used is registered in
/// <see cref="BodyWriterInventory"/>, so no unexpected Warning/Error log is produced
/// and no LogAssert.Expect declaration is required.
/// </summary>
public class DragRecoveryRepeatSequenceTests
{
    // ---- Frozen caps: see evidence/T-0006-repeat-sequence-criteria.md ----
    private const int RoundCap = 64;
    private const long PerRoundCeilingMs = 250;
    private const long TotalCeilingMs = 10000;

    [Test]
    public void RepeatSequence_MixedTerminationPaths_ConvergeWithinFrozenCaps()
    {
        var coordinator = new Live2DInputCoordinator();
        var totalWatch = System.Diagnostics.Stopwatch.StartNew();
        var executedRounds = 0;
        var lastRequestId = 0;

        for (var round = 0; round < RoundCap; round++)
        {
            var roundWatch = System.Diagnostics.Stopwatch.StartNew();

            switch (round % 5)
            {
                // Path A: plain TryBegin -> Release.
                case 0:
                {
                    Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out var lease),
                        $"round {round}: Expression lease was not granted");
                    lastRequestId = AssertMonotonic(lastRequestId, lease, round);
                    Assert.IsTrue(coordinator.Release(lease, "expression-completed"),
                        $"round {round}: owner could not release its own lease");
                    break;
                }

                // Path B: ReleaseAll is idempotent and frees the coordinator.
                case 1:
                {
                    Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch", out var action),
                        $"round {round}: LegacyAction lease was not granted");
                    lastRequestId = AssertMonotonic(lastRequestId, action, round);
                    coordinator.ReleaseAll("renderer-disabled");
                    Assert.IsFalse(coordinator.HasActiveLease, $"round {round}: ReleaseAll left an active lease");
                    coordinator.ReleaseAll("renderer-destroyed");
                    Assert.IsFalse(coordinator.HasActiveLease, $"round {round}: second ReleaseAll left an active lease");
                    Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch-again", out var again),
                        $"round {round}: coordinator did not recover after ReleaseAll");
                    lastRequestId = AssertMonotonic(lastRequestId, again, round);
                    Assert.IsTrue(coordinator.Release(again, "action-finished"),
                        $"round {round}: recovered lease could not be released");
                    break;
                }

                // Path C: walking -> drag handoff, stale walking release rejected.
                case 2:
                {
                    Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.Walking, "walking-state", out var walking),
                        $"round {round}: Walking lease was not granted");
                    lastRequestId = AssertMonotonic(lastRequestId, walking, round);
                    Assert.IsTrue(coordinator.TryHandoffWalkingToDragResponse(walking, "drag-response", out var drag),
                        $"round {round}: walking -> drag handoff was refused");
                    lastRequestId = AssertMonotonic(lastRequestId, drag, round);
                    Assert.AreEqual(Live2DInputKind.DragResponse, drag.Kind,
                        $"round {round}: handoff lease kind is not DragResponse");
                    Assert.AreEqual("drag-response", drag.WriterId,
                        $"round {round}: handoff lease writer is not drag-response");

                    var beforeStale = coordinator.ActiveLease.RequestId;
                    Assert.IsFalse(coordinator.Release(walking, "stale-walking-release"),
                        $"round {round}: stale walking lease released the handoff lease");
                    Assert.AreEqual(beforeStale, coordinator.ActiveLease.RequestId,
                        $"round {round}: stale walking release changed the active lease");
                    Assert.IsTrue(coordinator.Release(drag, "drag-release"),
                        $"round {round}: drag lease could not be released");
                    break;
                }

                // Path D: interrupt a running action into a drag response.
                case 3:
                {
                    Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.GeneratedMotion, "wave", out var motion),
                        $"round {round}: GeneratedMotion lease was not granted");
                    lastRequestId = AssertMonotonic(lastRequestId, motion, round);
                    Assert.IsTrue(coordinator.TryInterruptToDragResponse(motion, "drag-response", out var drag),
                        $"round {round}: interrupt -> drag response was refused");
                    lastRequestId = AssertMonotonic(lastRequestId, drag, round);
                    Assert.AreEqual(Live2DInputKind.DragResponse, drag.Kind,
                        $"round {round}: interrupt lease kind is not DragResponse");

                    var beforeStale = coordinator.ActiveLease.RequestId;
                    Assert.IsFalse(coordinator.Release(motion, "stale-motion-release"),
                        $"round {round}: interrupted lease released the replacement drag lease");
                    Assert.AreEqual(beforeStale, coordinator.ActiveLease.RequestId,
                        $"round {round}: stale motion release changed the active lease");
                    Assert.IsTrue(coordinator.Release(drag, "drag-release"),
                        $"round {round}: drag lease could not be released");
                    break;
                }

                // Path E: a delayed callback from a finished lease cannot clear the new one.
                default:
                {
                    Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out var first),
                        $"round {round}: first Expression lease was not granted");
                    lastRequestId = AssertMonotonic(lastRequestId, first, round);
                    Assert.IsTrue(coordinator.Release(first, "expression-completed"),
                        $"round {round}: first Expression lease could not be released");
                    Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.LegacyAction, "stretch", out var current),
                        $"round {round}: replacement LegacyAction lease was not granted");
                    lastRequestId = AssertMonotonic(lastRequestId, current, round);

                    var beforeStale = coordinator.ActiveLease.RequestId;
                    Assert.IsFalse(coordinator.Release(first, "stale-delayed-callback"),
                        $"round {round}: stale delayed callback released the replacement lease");
                    Assert.AreEqual(beforeStale, coordinator.ActiveLease.RequestId,
                        $"round {round}: stale delayed callback changed the active lease");
                    Assert.IsTrue(coordinator.Release(current, "action-finished"),
                        $"round {round}: replacement lease could not be released");
                    break;
                }
            }

            AssertRoundEndState(coordinator, round);
            lastRequestId = ProbeReacquire(coordinator, lastRequestId, round);

            executedRounds++;
            roundWatch.Stop();
            AssertWithinRoundCap(round, roundWatch);
        }

        Assert.AreEqual(RoundCap, executedRounds,
            $"C1 violated: executed {executedRounds} rounds, frozen cap is {RoundCap}");
        AssertWithinTotalCap(totalWatch, nameof(RepeatSequence_MixedTerminationPaths_ConvergeWithinFrozenCaps));
    }

    [Test]
    public void RepeatSequence_StaleReleaseMatrix_IsRejectedEveryRound()
    {
        var coordinator = new Live2DInputCoordinator();
        var totalWatch = System.Diagnostics.Stopwatch.StartNew();
        var executedRounds = 0;
        var lastRequestId = 0;

        for (var round = 0; round < RoundCap; round++)
        {
            var roundWatch = System.Diagnostics.Stopwatch.StartNew();

            Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.DragResponse, "drag-response", out var oldLease),
                $"round {round}: first DragResponse lease was not granted");
            lastRequestId = AssertMonotonic(lastRequestId, oldLease, round);
            Assert.IsTrue(coordinator.Release(oldLease, "drag-release"),
                $"round {round}: first DragResponse lease could not be released");

            Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.DragResponse, "drag-response", out var currentLease),
                $"round {round}: replacement DragResponse lease was not granted");
            lastRequestId = AssertMonotonic(lastRequestId, currentLease, round);
            Assert.AreEqual(oldLease.RequestId + 1, currentLease.RequestId,
                $"round {round}: replacement lease did not consume exactly one RequestId");

            var before = coordinator.ActiveLease.RequestId;

            // Already-released lease (stale by identity).
            AssertStaleRejected(coordinator, oldLease, before, round, "stale-by-identity");

            // Stale by lower RequestId.
            var staleLow = new Live2DInputLease(currentLease.RequestId - 1,
                Live2DInputKind.Expression, "stale-low");
            AssertStaleRejected(coordinator, staleLow, before, round, "stale-low");

            // Stale by higher RequestId.
            var staleHigh = new Live2DInputLease(currentLease.RequestId + 1,
                Live2DInputKind.Expression, "stale-high");
            AssertStaleRejected(coordinator, staleHigh, before, round, "stale-high");

            // Default (invalid) lease.
            AssertStaleRejected(coordinator, default(Live2DInputLease), before, round, "stale-default");

            // Stale handoff must not replace the current lease.
            Assert.IsFalse(coordinator.TryHandoffWalkingToDragResponse(oldLease, "drag-response", out var staleHandoff),
                $"round {round}: stale handoff replaced the current lease");
            Assert.IsFalse(staleHandoff.IsValid, $"round {round}: stale handoff produced a valid lease");
            Assert.AreEqual(before, coordinator.ActiveLease.RequestId,
                $"round {round}: stale handoff changed the active lease");

            // Stale interrupt must not replace the current lease.
            Assert.IsFalse(coordinator.TryInterruptToDragResponse(oldLease, "drag-response", out var staleInterrupt),
                $"round {round}: stale interrupt replaced the current lease");
            Assert.IsFalse(staleInterrupt.IsValid, $"round {round}: stale interrupt produced a valid lease");
            Assert.AreEqual(before, coordinator.ActiveLease.RequestId,
                $"round {round}: stale interrupt changed the active lease");

            Assert.IsTrue(coordinator.Release(currentLease, "drag-release"),
                $"round {round}: current DragResponse lease could not be released");

            AssertRoundEndState(coordinator, round);
            lastRequestId = ProbeReacquire(coordinator, lastRequestId, round);

            executedRounds++;
            roundWatch.Stop();
            AssertWithinRoundCap(round, roundWatch);
        }

        Assert.AreEqual(RoundCap, executedRounds,
            $"C1 violated: executed {executedRounds} rounds, frozen cap is {RoundCap}");
        AssertWithinTotalCap(totalWatch, nameof(RepeatSequence_StaleReleaseMatrix_IsRejectedEveryRound));
    }

    [Test]
    public void ReleaseAll_IsIdempotentUnderRepeatedCalls()
    {
        var coordinator = new Live2DInputCoordinator();
        var totalWatch = System.Diagnostics.Stopwatch.StartNew();
        var executedRounds = 0;
        var lastRequestId = 0;

        for (var round = 0; round < RoundCap; round++)
        {
            var roundWatch = System.Diagnostics.Stopwatch.StartNew();

            Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.Expression, "happy", out var lease),
                $"round {round}: Expression lease was not granted");
            lastRequestId = AssertMonotonic(lastRequestId, lease, round);

            coordinator.ReleaseAll("renderer-disabled");
            Assert.IsFalse(coordinator.HasActiveLease, $"round {round}: ReleaseAll left an active lease");
            coordinator.ReleaseAll("renderer-destroyed");
            Assert.IsFalse(coordinator.HasActiveLease, $"round {round}: second ReleaseAll left an active lease");
            coordinator.ReleaseAll("teardown-repeat");
            Assert.IsFalse(coordinator.HasActiveLease, $"round {round}: third ReleaseAll left an active lease");

            Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.Expression, "sad", out var next),
                $"round {round}: coordinator did not recover after ReleaseAll");
            Assert.AreEqual(lease.RequestId + 1, next.RequestId,
                $"C6 violated at round {round}: ReleaseAll consumed a RequestId"
                + $" (expected {lease.RequestId + 1}, got {next.RequestId})");
            lastRequestId = AssertMonotonic(lastRequestId, next, round);
            Assert.IsTrue(coordinator.Release(next, "expression-completed"),
                $"round {round}: recovered lease could not be released");

            AssertRoundEndState(coordinator, round);
            lastRequestId = ProbeReacquire(coordinator, lastRequestId, round);

            executedRounds++;
            roundWatch.Stop();
            AssertWithinRoundCap(round, roundWatch);
        }

        Assert.AreEqual(RoundCap, executedRounds,
            $"C1 violated: executed {executedRounds} rounds, frozen cap is {RoundCap}");
        AssertWithinTotalCap(totalWatch, nameof(ReleaseAll_IsIdempotentUnderRepeatedCalls));
    }

    // ---- Convergence helpers: each failure message names the round and the criterion ----

    private static int AssertMonotonic(int previousRequestId, Live2DInputLease lease, int round)
    {
        Assert.IsTrue(lease.IsValid, $"C2 violated at round {round}: granted lease is not valid");
        Assert.Greater(lease.RequestId, previousRequestId,
            $"C2 violated at round {round}: RequestId {lease.RequestId} is not greater than {previousRequestId}");
        return lease.RequestId;
    }

    private static void AssertStaleRejected(Live2DInputCoordinator coordinator, Live2DInputLease stale,
        int expectedActiveRequestId, int round, string label)
    {
        Assert.IsFalse(coordinator.Release(stale, label),
            $"C5 violated at round {round}: stale release '{label}' was accepted");
        Assert.AreEqual(expectedActiveRequestId, coordinator.ActiveLease.RequestId,
            $"C5 violated at round {round}: stale release '{label}' changed the active lease");
    }

    private static void AssertRoundEndState(Live2DInputCoordinator coordinator, int round)
    {
        Assert.IsFalse(coordinator.HasActiveLease,
            $"C3 violated at round {round}: a lease is still active at round end (stuck point)");
        Assert.IsTrue(coordinator.CanApplyLowPriorityOverlay,
            $"C7 violated at round {round}: low-priority overlay stayed suppressed at round end");
    }

    private static int ProbeReacquire(Live2DInputCoordinator coordinator, int previousRequestId, int round)
    {
        Assert.IsTrue(coordinator.TryBegin(Live2DInputKind.Expression, "probe-reacquire", out var probe),
            $"C4 violated at round {round}: coordinator could not re-acquire after teardown (stuck point)");
        var requestId = AssertMonotonic(previousRequestId, probe, round);
        Assert.IsTrue(coordinator.Release(probe, "probe-released"),
            $"C4 violated at round {round}: probe lease could not be released");
        return requestId;
    }

    private static void AssertWithinRoundCap(int round, System.Diagnostics.Stopwatch roundWatch)
    {
        Assert.LessOrEqual(roundWatch.ElapsedMilliseconds, PerRoundCeilingMs,
            $"C8 violated at round {round}: {roundWatch.ElapsedMilliseconds} ms exceeds the frozen"
            + $" per-round ceiling of {PerRoundCeilingMs} ms (stuck point)");
    }

    private static void AssertWithinTotalCap(System.Diagnostics.Stopwatch totalWatch, string label)
    {
        Assert.LessOrEqual(totalWatch.ElapsedMilliseconds, TotalCeilingMs,
            $"C9 violated in {label}: {totalWatch.ElapsedMilliseconds} ms exceeds the frozen"
            + $" total ceiling of {TotalCeilingMs} ms");
    }
}
