using System;
using System.Collections.Generic;
using NUnit.Framework;

public class EmbodiedRecoveryTests
{
    [Test] public void 桌面身体快照与Live2D参数状态分离且模式可复核()
    {
        var state = new DesktopBodyState();
        state.Update(120, 240, 3, 0, true, false, false, false, "MoveRightTime");
        var walking = state.CaptureSnapshot();
        Assert.AreEqual(120, walking.X);
        Assert.AreEqual(3, walking.VelocityX);
        Assert.AreEqual("MoveRightTime", walking.GroundTask);
        Assert.AreEqual(DesktopBodyMode.Walking, walking.Mode);

        state.Update(120, 240, 3, 0, true, true, false, false, "MoveRightTime");
        var dragging = state.CaptureSnapshot();
        Assert.Greater(dragging.Version, walking.Version);
        Assert.AreEqual(DesktopBodyMode.Dragging, dragging.Mode);

        state.Update(120, 240, 0, 0, true, false, false, true, "StopTime");
        Assert.AreEqual(DesktopBodyMode.ActionMovementLocked, state.CaptureSnapshot().Mode);
    }

    [Test] public void 记录写入后可恢复到基线且幂等()
    {
        var state = new EmbodiedPoseState();
        state.RecordWrite("Param94", 12.5f, 0f);
        state.RecordWrite("Param94", 15f, 0f);
        Assert.IsTrue(state.HasPendingRestore);
        var applied = new List<string>();
        var restored = state.RestoreAll((id, value) => { applied.Add($"{id}={value}"); });
        Assert.AreEqual(1, restored.Count);
        Assert.AreEqual("Param94", restored[0]);
        CollectionAssert.AreEqual(new[] { "Param94=0" }, applied);
        Assert.IsFalse(state.HasPendingRestore);
        var second = state.RestoreAll((id, value) => applied.Add("again"));
        Assert.AreEqual(0, second.Count);
        Assert.AreEqual(1, applied.Count);
    }

    [Test] public void 多参数分别还原到各自基线()
    {
        var state = new EmbodiedPoseState();
        state.RecordWrite("ParamA", 3f, 1f);
        state.RecordWrite("ParamB", -2f, 0f);
        var map = new Dictionary<string, float>();
        state.RestoreAll((id, value) => map[id] = value);
        Assert.AreEqual(1f, map["ParamA"]);
        Assert.AreEqual(0f, map["ParamB"]);
    }

    [Test] public void 无记录时恢复为空且不调用回调()
    {
        var state = new EmbodiedPoseState();
        var calls = 0;
        var restored = state.RestoreAll((id, value) => calls++);
        Assert.AreEqual(0, restored.Count);
        Assert.AreEqual(0, calls);
    }

    [Test] public void 空参数名被拒绝()
    {
        var state = new EmbodiedPoseState();
        Assert.Throws<ArgumentException>(() => state.RecordWrite("", 1f, 0f));
    }
    [Test] public void 快照记录动作资源与待还原参数()
    {
        var state = new EmbodiedPoseState();
        state.BeginAction(new EmbodiedActionRequest { SkillId = "certified", Resources = EmbodiedResource.Face });
        state.RecordWrite("ParamA", 2f, 0f);
        var snapshot = state.CaptureSnapshot();
        Assert.AreEqual("certified", snapshot.ActiveSkillId); Assert.AreEqual(EmbodiedResource.Face, snapshot.OccupiedResources); CollectionAssert.Contains(snapshot.PendingParameterIds, "ParamA");
        state.RestoreAll((id, value) => { }); state.FinishAction(EmbodiedActionStatus.Completed);
        var final = state.CaptureSnapshot();
        Assert.Greater(final.Version, snapshot.Version); Assert.IsNull(final.ActiveSkillId); Assert.IsEmpty(final.PendingParameterIds); Assert.AreEqual(EmbodiedActionStatus.Completed, final.ActionStatus);
    }

    [Test] public void 快照记录控制面所有者关联标识与终态原因()
    {
        var state = new EmbodiedPoseState();
        state.BeginAction(new EmbodiedActionRequest
        {
            RequestId = 42,
            Source = "certified-runtime",
            CorrelationId = "request-42",
            SkillId = "certified",
            Resources = EmbodiedResource.LeftArm
        });

        var active = state.CaptureSnapshot();
        Assert.AreEqual(42, active.ActiveRequestId);
        Assert.AreEqual("certified-runtime", active.ActiveSource);
        Assert.AreEqual("request-42", active.CorrelationId);
        Assert.IsNull(active.TerminalReason);

        state.FinishAction(EmbodiedActionStatus.Cancelled, "user-stop");
        var terminal = state.CaptureSnapshot();
        Assert.AreEqual(0, terminal.ActiveRequestId);
        Assert.IsNull(terminal.ActiveSource);
        Assert.IsNull(terminal.CorrelationId);
        Assert.AreEqual("user-stop", terminal.TerminalReason);
        Assert.AreEqual(EmbodiedActionStatus.Cancelled, terminal.ActionStatus);
    }

    [Test] public void 写入者清册明确区分认证受控与待迁移路径()
    {
        var writers = BodyWriterInventory.Snapshot();
        Assert.GreaterOrEqual(writers.Length, 9);
        var certified = Array.Find(writers, item => item.WriterId == "certified-motion");
        Assert.NotNull(certified);
        Assert.AreEqual(BodyWriterControlLevel.CertifiedCoordinator, certified.ControlLevel);
        Assert.AreEqual("EmbodiedSafeRecovery", certified.RecoveryOwner);

        var idle = Array.Find(writers, item => item.WriterId == "idle-action");
        Assert.NotNull(idle);
        Assert.AreEqual(BodyWriterControlLevel.InputLeaseOnly, idle.ControlLevel);
        Assert.That(idle.Resources & EmbodiedResource.LeftArm, Is.Not.EqualTo(EmbodiedResource.None));

        var drag = Array.Find(writers, item => item.WriterId == "drag-response");
        Assert.NotNull(drag);
        Assert.AreEqual(BodyWriterControlLevel.LegacyUnmanaged, drag.ControlLevel);
        Assert.IsFalse(string.IsNullOrWhiteSpace(drag.RecoveryOwner));
    }
}
