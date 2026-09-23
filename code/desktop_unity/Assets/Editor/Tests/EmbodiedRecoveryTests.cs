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

    [Test] public void 桌面快照不包含租约所有者或Cubism参数字段()
    {
        var snapshotType = typeof(DesktopBodySnapshot);
        Assert.IsNull(snapshotType.GetProperty("LeaseId"));
        Assert.IsNull(snapshotType.GetProperty("Owner"));
        Assert.IsNull(snapshotType.GetProperty("Param94"));
        Assert.IsNull(snapshotType.GetProperty("Parameters"));
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

    [Test] public void 重复写入保留首次真实基线()
    {
        var state = new EmbodiedPoseState();
        state.RecordWrite("Param94", 12.5f, 4.25f);
        state.RecordWrite("Param94", 15f, 0f);
        var applied = new Dictionary<string, float>();
        state.RestoreAll((id, value) => applied[id] = value);
        Assert.AreEqual(4.25f, applied["Param94"]);
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
    [Test] public void 恢复回调失败时已成功参数清除且失败参数可重试()
    {
        var state = new EmbodiedPoseState();
        state.RecordWrite("ParamA", 3f, 1f);
        state.RecordWrite("ParamB", -2f, 0f);
        var applied = new List<string>();

        Assert.Throws<InvalidOperationException>(() => state.RestoreAll((id, value) =>
        {
            if (id == "ParamA") throw new InvalidOperationException("restore failed");
            applied.Add(id);
        }));

        CollectionAssert.AreEqual(new[] { "ParamB" }, applied);
        Assert.AreEqual(1, state.PendingCount);
        CollectionAssert.AreEqual(new[] { "ParamA" }, state.CaptureSnapshot().PendingParameterIds);

        state.RestoreAll((id, value) => applied.Add(id));
        CollectionAssert.AreEqual(new[] { "ParamB", "ParamA" }, applied);
        Assert.IsFalse(state.HasPendingRestore);
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

        var walking = Array.Find(writers, item => item.WriterId == "walk-pose");
        Assert.NotNull(walking);
        Assert.AreEqual(BodyWriterControlLevel.InputLeaseOnly, walking.ControlLevel);
        Assert.AreEqual("walking-state", walking.RecoveryOwner);
        Assert.That(walking.Resources & EmbodiedResource.Movement,
            Is.Not.EqualTo(EmbodiedResource.None));
        Assert.That(walking.Resources & EmbodiedResource.Body,
            Is.Not.EqualTo(EmbodiedResource.None));
        Assert.That(walking.Resources & EmbodiedResource.LeftArm,
            Is.Not.EqualTo(EmbodiedResource.None));
        Assert.That(walking.Resources & EmbodiedResource.RightArm,
            Is.Not.EqualTo(EmbodiedResource.None));

        var idle = Array.Find(writers, item => item.WriterId == "idle-action");
        Assert.NotNull(idle);
        Assert.AreEqual(BodyWriterControlLevel.InputLeaseOnly, idle.ControlLevel);
        Assert.That(idle.Resources & EmbodiedResource.LeftArm, Is.Not.EqualTo(EmbodiedResource.None));

        var physics = Array.Find(writers, item => item.WriterId == "desktop-physics");
        Assert.NotNull(physics);
        Assert.AreEqual(BodyWriterControlLevel.InputLeaseOnly, physics.ControlLevel);
        Assert.AreEqual(BodyWriterRole.DesktopStateLease, physics.Role);
        Assert.AreEqual("physics-state", physics.RecoveryOwner);
        Assert.AreEqual(EmbodiedResource.Movement | EmbodiedResource.Body | EmbodiedResource.Effect,
            physics.Resources);

        var gaze = Array.Find(writers, item => item.WriterId == "mouse-gaze");
        Assert.NotNull(gaze);
        Assert.AreEqual(BodyWriterControlLevel.InternalOnly, gaze.ControlLevel);
        Assert.AreEqual(BodyWriterRole.LeaseGatedOverlay, gaze.Role);
        Assert.AreEqual("gaze-update", gaze.RecoveryOwner);

        var renderer = Array.Find(writers, item => item.WriterId == "renderer-parameter-commit");
        Assert.NotNull(renderer);
        Assert.AreEqual(BodyWriterControlLevel.InternalOnly, renderer.ControlLevel);
        Assert.AreEqual(BodyWriterRole.InternalParameterLayer, renderer.Role);
        Assert.AreEqual(EmbodiedResource.None, renderer.Resources);
        Assert.AreEqual("ParameterCommitBridge", renderer.RecoveryOwner);

        var drag = Array.Find(writers, item => item.WriterId == "drag-response");
        Assert.NotNull(drag);
        Assert.AreEqual(BodyWriterControlLevel.InputLeaseOnly, drag.ControlLevel);
        Assert.AreEqual("ReleaseDragResponseInputLease", drag.RecoveryOwner);
    }
}
