using System;
using NUnit.Framework;

public class PhaseBObservationTests
{
    private static EmbodiedEvent Event(int requestId)
    {
        return new EmbodiedEvent(DateTime.UtcNow, requestId, "test", "state", "skill", "Executing",
            "short-reason", 12, EmbodiedResource.Face, 3, "hash");
    }

    [Test] public void 事件存储有界且按时间顺序保留最新事件()
    {
        var store = new EmbodiedEventStore(2);
        store.Append(Event(1)); store.Append(Event(2)); store.Append(Event(3));
        Assert.AreEqual(2, store.Count);
        var events = store.Snapshot();
        Assert.AreEqual(2, events[0].RequestId);
        Assert.AreEqual(3, events[1].RequestId);
        events[0] = Event(99);
        Assert.AreEqual(2, store.Snapshot()[0].RequestId);
    }

    [Test] public void 事件拒绝疑似敏感或越界字段()
    {
        Assert.Throws<ArgumentException>(() => new EmbodiedEvent(DateTime.UtcNow, 1, "test\nsecret", "kind", "skill", "state", null, 0, EmbodiedResource.None, 0, null));
        Assert.Throws<ArgumentException>(() => new EmbodiedEvent(DateTime.UtcNow, 1, "test", "kind", "skill", "state", new string('x', 257), 0, EmbodiedResource.None, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbodiedEvent(DateTime.UtcNow, 1, "test", "kind", "skill", "state", null, -1, EmbodiedResource.None, 0, null));
    }

    [Test] public void 身体状态发布递增版本且快照只读复制()
    {
        var poseState = new EmbodiedPoseState();
        poseState.RecordWrite("ParamA", 1f, 0f);
        var desktopState = new DesktopBodyState();
        desktopState.Update(10, 20, 1, 0, true, false, false, false, "walk");
        var store = new BodyStateStore();
        var first = store.Publish(poseState.CaptureSnapshot(), desktopState.CaptureSnapshot(), true, DateTime.UtcNow);
        var second = store.Publish(poseState.CaptureSnapshot(), desktopState.CaptureSnapshot(), false, DateTime.UtcNow.AddSeconds(1));
        Assert.Greater(second.Version, first.Version);
        Assert.IsTrue(first.RendererReady);
        Assert.IsFalse(second.RendererReady);
        Assert.AreEqual(DesktopBodyMode.Walking, first.Desktop.Mode);
        first.Pose.PendingParameterIds[0] = "mutated";
        Assert.AreEqual("ParamA", store.CaptureSnapshot().Pose.PendingParameterIds[0]);
    }

    [Test] public void 执行监测暴露健康字段并累计故障()
    {
        var monitor = new ExecutionMonitor();
        Assert.AreEqual(ExecutionHealth.Unknown, monitor.Snapshot.Health);
        monitor.Observe(DateTime.UtcNow, true, true, true, "started");
        Assert.AreEqual(ExecutionHealth.Healthy, monitor.Snapshot.Health);
        monitor.Observe(DateTime.UtcNow, true, true, false, "no-progress");
        Assert.AreEqual(ExecutionHealth.Degraded, monitor.Snapshot.Health);
        Assert.AreEqual(1, monitor.Snapshot.FaultCount);
        Assert.AreEqual("no-progress", monitor.Snapshot.LastReason);
        monitor.Observe(DateTime.UtcNow, false, true, false, "renderer-unavailable");
        Assert.AreEqual(ExecutionHealth.Unhealthy, monitor.Snapshot.Health);
        Assert.AreEqual(3, monitor.Snapshot.ObservationCount);
    }
}
