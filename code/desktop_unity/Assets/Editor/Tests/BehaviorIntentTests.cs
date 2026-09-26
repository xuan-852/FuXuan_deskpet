using System;
using NUnit.Framework;

public class BehaviorIntentTests
{
    private static CertifiedSkillBehaviorAdapter TestAdapter(string executionId = "exec-test", Func<SkillExecutionHandle, string, bool> cancel = null)
    {
        return new CertifiedSkillBehaviorAdapter(
            id => true,
            type => "screen_side_arm_raise",
            id => new SkillExecutionHandle
            {
                ExecutionId = executionId,
                SkillId = id,
                Status = BehaviorExecutionStatus.Executing,
                StartedAtUtc = DateTime.UtcNow,
                ExpectedEndAtUtc = DateTime.UtcNow.AddSeconds(2)
            },
            cancel ?? ((handle, reason) => true));
    }

    [Test]
    public void 点击事件生成可追踪的统一意图并完成生命周期()
    {
        var coordinator = new BehaviorCoordinator(new LifeStateStore(), TestAdapter("click-exec"));
        var at = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var result = coordinator.Handle(new BehaviorEvent("click-1", "user", "pet_click", "user_clicked_pet_after_idle", "corr-1", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Executing, result.Status);
        Assert.AreEqual("acknowledge_user", result.Intent.IntentType);
        Assert.AreEqual("corr-1", result.Intent.CorrelationId);
        Assert.IsTrue(result.Intent.Interruptible);
        Assert.IsTrue(coordinator.Complete(result.Intent.IntentId, at.AddSeconds(1), "legacy-adapter-completed"));
        Assert.AreEqual(BehaviorExecutionStatus.Completed, coordinator.Get(result.Intent.IntentId).Status);
        Assert.AreEqual("legacy-adapter-completed", coordinator.Get(result.Intent.IntentId).Reason);
    }

    [Test]
    public void 重复关联点击只生成一个意图并记录拒绝()
    {
        var c = new BehaviorCoordinator(new LifeStateStore(), TestAdapter("duplicate-exec"));
        var at = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(BehaviorExecutionStatus.Executing, c.Handle(new BehaviorEvent("e1", "user", "pet_click", "click", "same", at), at).Status);
        var duplicate = c.Handle(new BehaviorEvent("e2", "user", "pet_click", "click", "same", at.AddMilliseconds(1)), at.AddMilliseconds(1));
        Assert.AreEqual(BehaviorExecutionStatus.Rejected, duplicate.Status);
        Assert.AreEqual("duplicate-correlation", duplicate.Reason);
        Assert.AreEqual(1, c.Snapshot.Executions.Count);
    }

    [Test]
    public void 拖拽接管取消可中断意图并记录恢复结果()
    {
        var c = new BehaviorCoordinator(new LifeStateStore(), TestAdapter("drag-exec"));
        var at = DateTime.UtcNow;
        var started = c.Handle(new BehaviorEvent("e1", "user", "pet_click", "click", "corr", at), at);
        Assert.IsTrue(c.TakeoverByDrag(started.Intent.IntentId, at.AddSeconds(1)));
        var execution = c.Get(started.Intent.IntentId);
        Assert.AreEqual(BehaviorExecutionStatus.Cancelled, execution.Status);
        Assert.AreEqual("user-drag-takeover", execution.Reason);
        Assert.IsTrue(c.RecordDragEnded("corr", at.AddSeconds(2)));
        Assert.AreEqual("drag-ended-recovered", c.Snapshot.LastResult);
    }

    [Test]
    public void 未认证技能不会绕过准入且认证映射可被适配器拒绝()
    {
        var adapter = new CertifiedSkillBehaviorAdapter(id => false, type => "unregistered.skill");
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = DateTime.UtcNow;
        var result = c.Handle(new BehaviorEvent("e-skill", "user", "pet_click", "click", "skill-corr", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Rejected, result.Status);
        Assert.AreEqual("adapter-rejected:skill-not-certified", result.Reason);
    }

    [Test]
    public void 认证适配器调用执行委托并在取消时调用取消委托()
    {
        string started = null, cancelled = null;
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => "certified.skill", id => { started = id; return new SkillExecutionHandle { ExecutionId = "exec-test", SkillId = id, Status = BehaviorExecutionStatus.Executing }; }, (handle, reason) => { cancelled = handle.SkillId + ":" + reason; return true; });
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = DateTime.UtcNow;
        var result = c.Handle(new BehaviorEvent("e-run", "user", "pet_click", "click", "run-corr", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Executing, result.Status);
        Assert.AreEqual("certified.skill", started);
        Assert.IsTrue(c.TakeoverByDrag(result.Intent.IntentId, at.AddSeconds(1)));
        Assert.AreEqual("certified.skill:user-drag-takeover", cancelled);
    }

    [Test]
    public void 显式短抬臂事件保留意图类型并拒绝默认问候混淆()
    {
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => type == "brief_arm_raise" ? "screen_side_arm_raise" : null, id => new SkillExecutionHandle { ExecutionId = "exec-explicit", SkillId = id, Status = BehaviorExecutionStatus.Executing });
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = DateTime.UtcNow;
        var result = c.Handle(new BehaviorEvent("explicit", "test", "brief_arm_raise", "explicit_test", "explicit-corr", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Executing, result.Status);
        Assert.AreEqual("brief_arm_raise", result.Intent.IntentType);
        Assert.AreEqual("screen_side_arm_raise", result.Intent.BodySkill);
    }

    [Test]
    public void 认证执行句柄记录请求并终结幂等()
    {
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => "screen_side_arm_raise", id => new SkillExecutionHandle { ExecutionId = "exec-1", SkillId = id, RequestId = 42, Resources = EmbodiedResource.RightArm, Status = BehaviorExecutionStatus.Executing, StartedAtUtc = DateTime.UtcNow, ExpectedEndAtUtc = DateTime.UtcNow.AddSeconds(2) }, (handle, reason) => true);
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = DateTime.UtcNow;
        var result = c.Handle(new BehaviorEvent("handle", "test", "brief_arm_raise", "explicit_test", "handle-corr", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Executing, result.Status);
        Assert.IsNotNull(result.Handle);
        Assert.AreEqual("screen_side_arm_raise", result.Handle.SkillId);
        Assert.IsTrue(c.Complete(result.Intent.IntentId, at.AddSeconds(1), "completed"));
        Assert.IsFalse(c.Cancel(result.Intent.IntentId, at.AddSeconds(2), "late-cancel"));
        Assert.AreEqual(BehaviorExecutionStatus.Completed, c.Get(result.Intent.IntentId).Status);
    }

    [Test]
    public void 过期会先通知适配器取消并更新句柄状态()
    {
        string cancelled = null;
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => "screen_side_arm_raise", id => new SkillExecutionHandle { ExecutionId = "exec-expire", SkillId = id, Status = BehaviorExecutionStatus.Executing }, (handle, reason) => { cancelled = reason; return true; });
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var result = c.Handle(new BehaviorEvent("expire", "test", "brief_arm_raise", "test", "expire-corr", at, TimeSpan.FromSeconds(1)), at);
        Assert.AreEqual(1, c.Expire(at.AddSeconds(2)));
        Assert.AreEqual("expired", cancelled);
        Assert.AreEqual(BehaviorExecutionStatus.Expired, result.Handle.Status);
    }

    [Test]
    public void 过期取消失败必须记录恢复失败而非永久Executing()
    {
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => "screen_side_arm_raise",
            id => new SkillExecutionHandle { ExecutionId = "expire-cancel-fail", SkillId = id, Status = BehaviorExecutionStatus.Executing },
            (handle, reason) => false);
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var result = c.Handle(new BehaviorEvent("expire-cancel-fail", "test", "brief_arm_raise",
            "test", "expire-cancel-fail-corr", at, TimeSpan.FromSeconds(1)), at);

        Assert.AreEqual(1, c.Expire(at.AddSeconds(2)));
        Assert.AreEqual(BehaviorExecutionStatus.RecoveryFailed, result.Status);
        Assert.AreEqual(BehaviorExecutionStatus.RecoveryFailed, result.Handle.Status);
        Assert.AreEqual("expired-cancel-failed", result.Reason);
        Assert.AreEqual(0, c.Expire(at.AddSeconds(3)));
    }

    [Test]
    public void 输入租约表达可登记意图与终态且不冒充认证请求()
    {
        var c = new BehaviorCoordinator(new LifeStateStore());
        var at = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var lease = new Live2DInputLease(7, Live2DInputKind.Expression, "expression", "happy",
            EmbodiedResource.Face, BodyWriterControlLevel.InputLeaseOnly);
        var execution = c.RegisterInputExecution("expression", "expression", "expression-started",
            "expression-7", "happy", lease, at);

        Assert.IsNotNull(execution);
        Assert.AreEqual("input-expression-7", execution.Handle.ExecutionId);
        Assert.IsNull(execution.Handle.Request);
        Assert.AreEqual(EmbodiedResource.Face, execution.Handle.Resources);
        Assert.IsFalse(c.RecordTerminal("unknown", BehaviorExecutionStatus.Completed, "late", at.AddSeconds(1)));
        Assert.IsTrue(c.RecordTerminal(execution.Handle.ExecutionId, BehaviorExecutionStatus.Cancelled,
            "expression-stopped", at.AddSeconds(2)));
        Assert.AreEqual(BehaviorExecutionStatus.Cancelled, execution.Status);
        Assert.IsFalse(c.RecordTerminal(execution.Handle.ExecutionId, BehaviorExecutionStatus.Completed,
            "late", at.AddSeconds(3)));
    }

    [Test]
    public void Renderer终结回写按执行句柄完成且重复回写幂等()
    {
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => "screen_side_arm_raise", id => new SkillExecutionHandle { ExecutionId = "renderer-exec", SkillId = id, Status = BehaviorExecutionStatus.Executing });
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = DateTime.UtcNow;
        var result = c.Handle(new BehaviorEvent("renderer", "test", "brief_arm_raise", "renderer_test", "renderer-corr", at), at);
        Assert.IsTrue(c.RecordTerminal(result.Handle.ExecutionId, BehaviorExecutionStatus.Completed, "renderer-completed", at.AddSeconds(1)));
        Assert.IsFalse(c.RecordTerminal(result.Handle.ExecutionId, BehaviorExecutionStatus.RecoveryFailed, "late-recovery-failure", at.AddSeconds(2)));
        Assert.AreEqual(BehaviorExecutionStatus.Completed, result.Status);
        Assert.AreEqual("renderer-completed", result.Reason);
    }

    [Test]
    public void 没有真实启动委托时拒绝而不是伪造Executing()
    {
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => "screen_side_arm_raise");
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = DateTime.UtcNow;
        var result = c.Handle(new BehaviorEvent("no-start", "test", "brief_arm_raise", "test", "no-start-corr", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Rejected, result.Status);
        Assert.AreEqual("adapter-rejected:renderer-not-ready", result.Reason);
    }

    [Test]
    public void 取消失败进入RecoveryFailed而不是伪装成Cancelled()
    {
        var adapter = new CertifiedSkillBehaviorAdapter(id => true, type => "screen_side_arm_raise", id => new SkillExecutionHandle { ExecutionId = "cancel-fail", SkillId = id, Status = BehaviorExecutionStatus.Executing }, (handle, reason) => false);
        var c = new BehaviorCoordinator(new LifeStateStore(), adapter);
        var at = DateTime.UtcNow;
        var result = c.Handle(new BehaviorEvent("cancel-fail", "test", "brief_arm_raise", "test", "cancel-fail-corr", at), at);
        Assert.IsTrue(c.TakeoverByDrag(result.Intent.IntentId, at.AddSeconds(1)));
        Assert.AreEqual(BehaviorExecutionStatus.RecoveryFailed, result.Status);
        Assert.That(result.Reason, Does.Contain("cancel"));
    }

    [Test]
    public void 默认Legacy适配器拒绝并且不伪造Executing()
    {
        var c = new BehaviorCoordinator(new LifeStateStore());
        var at = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var result = c.Handle(new BehaviorEvent("legacy", "user", "pet_click", "legacy_click", "legacy-corr", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Rejected, result.Status);
        Assert.AreEqual("adapter-rejected:legacy-adapter-unsupported", result.Reason);
        Assert.AreEqual(0, c.Snapshot.Executions.Count);
    }

    [Test]
    public void 已准入身体请求登记后共享唯一执行关联()
    {
        EmbodiedActionRequest request;
        string admissionReason;
        Assert.IsTrue(EmbodiedRuntimeAdmission.TryBeginSkill(
            ScreenSideArmRaiseCertification.SkillId, out request, out admissionReason), admissionReason);
        try
        {
            var c = new BehaviorCoordinator(new LifeStateStore());
            var at = DateTime.UtcNow;
            var execution = c.RegisterAdmittedExecution(
                "request_body_skill", "request_body_skill", "user_requested_body_skill",
                request.CorrelationId, request.SkillId, request, at);

            Assert.IsNotNull(execution);
            Assert.AreEqual(BehaviorExecutionStatus.Executing, execution.Status);
            Assert.AreEqual(execution.Handle.ExecutionId, request.BehaviorExecutionId);
            Assert.AreEqual(execution.Intent.IntentId, execution.Handle.IntentId);
            Assert.AreEqual(request.RequestId, execution.Handle.RequestId);
            Assert.IsNull(c.RegisterAdmittedExecution(
                "request_body_skill", "request_body_skill", "duplicate",
                request.CorrelationId, request.SkillId, request, at.AddMilliseconds(1)));

            Assert.IsTrue(c.RecordTerminal(execution.Handle.ExecutionId,
                BehaviorExecutionStatus.Completed, "test-completed", at.AddSeconds(1)));
            Assert.AreEqual(BehaviorExecutionStatus.Completed, execution.Status);
        }
        finally
        {
            EmbodiedRuntimeAdmission.CancelSkill(request, "behavior-intent-test-cleanup");
        }
    }

    [Test]
    public void 重复事件ID在首次执行后被拒绝()
    {
        var c = new BehaviorCoordinator(new LifeStateStore(), TestAdapter("event-id-exec"));
        var at = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var first = c.Handle(new BehaviorEvent("same-event", "test", "pet_click", "first", "event-id-corr", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Executing, first.Status);
        var duplicate = c.Handle(new BehaviorEvent("same-event", "test", "pet_click", "second", "event-id-corr-2", at.AddSeconds(1)), at.AddSeconds(1));
        Assert.AreEqual(BehaviorExecutionStatus.Rejected, duplicate.Status);
        Assert.AreEqual("duplicate-event", duplicate.Reason);
        Assert.AreEqual(1, c.Snapshot.Executions.Count);
    }

    [Test]
    public void 非法事件和过期意图不会进入执行()
    {
        var c = new BehaviorCoordinator(new LifeStateStore());
        var at = DateTime.UtcNow;
        var invalid = c.Handle(new BehaviorEvent("bad", "", "pet_click", "click", "x", at), at);
        Assert.AreEqual(BehaviorExecutionStatus.Rejected, invalid.Status);
        Assert.AreEqual("event-source-required", invalid.Reason);
        var expired = c.Handle(new BehaviorEvent("old", "user", "pet_click", "click", "old", at.AddSeconds(-20)), at);
        Assert.AreEqual(BehaviorExecutionStatus.Rejected, expired.Status);
        Assert.AreEqual("event-expired", expired.Reason);
    }

    [Test]
    public void 输入租约持续执行时续期LifeState且终态后停止续期()
    {
        var life = new LifeStateStore();
        var coordinator = new BehaviorCoordinator(life);
        var at = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        var lease = new Live2DInputLease(40, Live2DInputKind.Walking,
            "walk-pose", "walking-state", EmbodiedResource.Body,
            BodyWriterControlLevel.InputLeaseOnly);
        var execution = coordinator.RegisterInputExecution("walking", "walk-pose", "started",
            "walking-40", "walking", lease, at);

        Assert.IsNotNull(execution);
        Assert.IsTrue(coordinator.RenewInputExecution(execution.Handle.ExecutionId,
            at.AddSeconds(29), TimeSpan.FromSeconds(30)));
        life.Expire(at.AddSeconds(31));
        Assert.AreEqual(LifeActionStatus.Active, life.Snapshot.ActionStatus);
        Assert.AreEqual(BehaviorExecutionStatus.Executing, execution.Status);

        Assert.IsTrue(coordinator.RecordTerminal(execution.Handle.ExecutionId,
            BehaviorExecutionStatus.Completed, "walking-stopped", at.AddSeconds(32)));
        Assert.IsFalse(coordinator.RenewInputExecution(execution.Handle.ExecutionId,
            at.AddSeconds(33), TimeSpan.FromSeconds(30)));
        life.Expire(at.AddSeconds(64));
        Assert.AreEqual(LifeActionStatus.Completed, life.Snapshot.ActionStatus);
    }

    [Test]
    public void 输入租约旧关联不能续期交接后的当前动作()
    {
        var life = new LifeStateStore();
        var coordinator = new BehaviorCoordinator(life);
        var at = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        var walkingLease = new Live2DInputLease(41, Live2DInputKind.Walking,
            "walk-pose", "walking-state", EmbodiedResource.Body,
            BodyWriterControlLevel.InputLeaseOnly);
        var dragLease = new Live2DInputLease(42, Live2DInputKind.DragResponse,
            "drag-response", "drag-response", EmbodiedResource.Body,
            BodyWriterControlLevel.InputLeaseOnly);
        var walking = coordinator.RegisterInputExecution("walking", "walk-pose", "started",
            "walking-41", "walking", walkingLease, at);
        var drag = coordinator.RegisterInputExecution("drag", "drag-response", "started",
            "drag-42", "dragging", dragLease, at.AddSeconds(1));

        Assert.IsFalse(coordinator.RenewInputExecution(walking.Handle.ExecutionId,
            at.AddSeconds(29), TimeSpan.FromSeconds(30)));
        Assert.IsTrue(coordinator.RenewInputExecution(drag.Handle.ExecutionId,
            at.AddSeconds(29), TimeSpan.FromSeconds(30)));
        life.Expire(at.AddSeconds(31));
        Assert.AreEqual(LifeActionStatus.Active, life.Snapshot.ActionStatus);
        Assert.AreEqual("dragging", life.Snapshot.CurrentAction);
    }

    [Test]
    public void 连续输入记录有界且保留绝对索引()
    {
        var coordinator = new BehaviorCoordinator(new LifeStateStore());
        var at = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 520; i++)
        {
            var lease = new Live2DInputLease(i, Live2DInputKind.DesktopPhysics,
                "desktop-physics", "physics", EmbodiedResource.Body,
                BodyWriterControlLevel.InputLeaseOnly);
            var execution = coordinator.RegisterInputExecution("desktop_physics", "desktop-physics",
                "physics-started", "physics-" + i, "physics", lease, at.AddMilliseconds(i), false);
            Assert.IsNotNull(execution);
            Assert.IsTrue(coordinator.RecordTerminal(execution.Handle.ExecutionId,
                BehaviorExecutionStatus.Completed, "physics-idle", at.AddMilliseconds(i + 1)));
        }
        Assert.AreEqual(520, coordinator.ExecutionCount);
        Assert.AreEqual(8, coordinator.OldestExecutionIndex);
        Assert.IsNull(coordinator.GetExecutionAt(7));
        Assert.IsNotNull(coordinator.GetExecutionAt(8));
        Assert.AreEqual(512, coordinator.Snapshot.Executions.Count);
    }

    [Test]
    public void 原子交接后旧租约终态不覆盖新动作生命状态()
    {
        var life = new LifeStateStore();
        var coordinator = new BehaviorCoordinator(life);
        var at = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        var walkingLease = new Live2DInputLease(1, Live2DInputKind.Walking,
            "walk-pose", "walking-state", EmbodiedResource.Body,
            BodyWriterControlLevel.InputLeaseOnly);
        var dragLease = new Live2DInputLease(2, Live2DInputKind.DragResponse,
            "drag-response", "drag-response", EmbodiedResource.Body,
            BodyWriterControlLevel.InputLeaseOnly);
        var walking = coordinator.RegisterInputExecution("walking", "walk-pose", "started",
            "walk-1", "walking", walkingLease, at);
        var drag = coordinator.RegisterInputExecution("drag", "drag-response", "started",
            "drag-2", "dragging", dragLease, at.AddMilliseconds(1));
        Assert.IsNotNull(walking);
        Assert.IsNotNull(drag);
        Assert.IsTrue(coordinator.RecordTerminal(walking.Handle.ExecutionId,
            BehaviorExecutionStatus.Cancelled, "drag-handoff", at.AddMilliseconds(2)));
        Assert.AreEqual(BehaviorExecutionStatus.Cancelled, walking.Status);
        Assert.AreEqual(LifeActionStatus.Active, life.Snapshot.ActionStatus);
        Assert.AreEqual("dragging", life.Snapshot.CurrentAction);
        Assert.IsTrue(coordinator.RecordTerminal(drag.Handle.ExecutionId,
            BehaviorExecutionStatus.Completed, "drag-release", at.AddMilliseconds(3)));
        Assert.AreEqual(LifeActionStatus.Completed, life.Snapshot.ActionStatus);
    }
}
