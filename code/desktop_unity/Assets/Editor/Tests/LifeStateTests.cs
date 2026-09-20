using System;
using NUnit.Framework;

public class LifeStateTests
{
    private static LifeEvent Event(string id, LifeEventType type, DateTime at, string summary = null, string correlation = null, int importance = 80, int ttlSeconds = 30)
    {
        return new LifeEvent(id, type, at, at.AddSeconds(ttlSeconds), "test", correlation, importance, summary);
    }

    [Test]
    public void 事件去重并按容量保留最近事件()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore(2);
        Assert.IsTrue(store.Append(Event("a", LifeEventType.UserReturned, now), now));
        Assert.IsFalse(store.Append(Event("a", LifeEventType.UserReturned, now), now));
        Assert.IsTrue(store.Append(Event("b", LifeEventType.DirectInteraction, now.AddSeconds(1)), now.AddSeconds(1)));
        Assert.IsTrue(store.Append(Event("c", LifeEventType.UserInactive, now.AddSeconds(2)), now.AddSeconds(2)));
        Assert.AreEqual(2, store.EventCount);
        Assert.AreEqual(LifePresence.Away, store.Snapshot.Presence);
    }

    [Test]
    public void 事件拒绝越界换行和过期输入()
    {
        DateTime now = DateTime.UtcNow;
        Assert.Throws<ArgumentException>(() => new LifeEvent("a\nb", LifeEventType.UserReturned, now, now.AddSeconds(1), "test", null, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LifeEvent("a", LifeEventType.UserReturned, now, now.AddSeconds(1), "test", null, 101, null));
        var store = new LifeStateStore();
        Assert.IsFalse(store.Append(Event("expired", LifeEventType.UserReturned, now.AddSeconds(-10), null, null, 80, 1), now));
    }

    [Test]
    public void 快照复制并记录来源置信度和原因()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        store.Append(Event("interaction", LifeEventType.DirectInteraction, now, "pet-click"), now);
        var snapshot = store.Snapshot;
        Assert.AreEqual("test", snapshot.PresenceMetadata.Source);
        Assert.AreEqual(0.8f, snapshot.PresenceMetadata.Confidence, 0.001f);
        Assert.AreEqual("pet-click", snapshot.AttentionMetadata.Reason);
        var freshSnapshot = store.Snapshot;
        Assert.AreEqual("pet", freshSnapshot.AttentionTarget);
        Assert.AreNotSame(snapshot, freshSnapshot);
    }

    [Test]
    public void 动作生命周期终态重复事件不改变既有结果()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        store.Append(Event("start", LifeEventType.ActionStarted, now, "greeting-wave", "action-1"), now);
        store.Append(Event("done", LifeEventType.ActionCompleted, now.AddSeconds(1), "completed", "action-2"), now.AddSeconds(1));
        Assert.AreEqual(LifeActionStatus.Completed, store.Snapshot.ActionStatus);
        Assert.IsFalse(store.Append(Event("done", LifeEventType.ActionCompleted, now.AddSeconds(2), "completed", "action-2"), now.AddSeconds(2)));
        Assert.AreEqual(LifeActionStatus.Completed, store.Snapshot.ActionStatus);
        Assert.AreEqual("completed", store.Snapshot.LastActionResult);
    }

    [Test]
    public void 同一动作关联键允许不同阶段但拒绝重复阶段()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("start", LifeEventType.ActionStarted, now, "wave", "action-1"), now));
        Assert.IsTrue(store.Append(Event("done", LifeEventType.ActionCompleted, now.AddSeconds(1), "completed", "action-1"), now.AddSeconds(1)));
        Assert.IsFalse(store.Append(Event("done-2", LifeEventType.ActionCompleted, now.AddSeconds(2), "completed-again", "action-1"), now.AddSeconds(2)));
        Assert.AreEqual(LifeActionStatus.Completed, store.Snapshot.ActionStatus);
    }

    [Test]
    public void 工作和交互活动会映射到对应生命语义()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("working", LifeEventType.UserWorking, now, "coding"), now));
        Assert.AreEqual(LifePresence.Present, store.Snapshot.Presence);
        Assert.AreEqual(LifeActivity.Working, store.Snapshot.Activity);
        Assert.IsTrue(store.Append(Event("interacting", LifeEventType.UserInteracting,
            now.AddSeconds(1), "communication"), now.AddSeconds(1)));
        Assert.AreEqual(LifePresence.Present, store.Snapshot.Presence);
        Assert.AreEqual(LifeActivity.Interacting, store.Snapshot.Activity);
    }

    [Test]
    public void 已完成动作不会被普通中断覆盖且恢复失败优先()
    {
        DateTime now = DateTime.UtcNow;
        var completed = new LifeStateStore();
        Assert.IsTrue(completed.Append(Event("start", LifeEventType.ActionStarted, now, "wave", "completed-1"), now));
        Assert.IsTrue(completed.Append(Event("done", LifeEventType.ActionCompleted,
            now.AddSeconds(1), "completed", "completed-1"), now.AddSeconds(1)));
        Assert.IsTrue(completed.Append(Event("interrupt", LifeEventType.ActionInterrupted,
            now.AddSeconds(2), "late-interrupt", "completed-1"), now.AddSeconds(2)));
        Assert.AreEqual(LifeActionStatus.Completed, completed.Snapshot.ActionStatus);
        Assert.AreEqual("completed", completed.Snapshot.LastActionResult);

        var recoveryFailed = new LifeStateStore();
        Assert.IsTrue(recoveryFailed.Append(Event("start", LifeEventType.ActionStarted,
            now, "wave", "recovery-1"), now));
        Assert.IsTrue(recoveryFailed.Append(Event("failed", LifeEventType.ActionRecoveryFailed,
            now.AddSeconds(1), "restore-failed", "recovery-1"), now.AddSeconds(1)));
        Assert.IsTrue(recoveryFailed.Append(Event("interrupt", LifeEventType.ActionInterrupted,
            now.AddSeconds(2), "late-interrupt", "recovery-1"), now.AddSeconds(2)));
        Assert.AreEqual(LifeActionStatus.RecoveryFailed, recoveryFailed.Snapshot.ActionStatus);
        Assert.AreEqual("restore-failed", recoveryFailed.Snapshot.LastInterruption);
    }

    [Test]
    public void 四维情绪摘要会夹紧并在过期后回落()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        store.Append(Event("emotion", LifeEventType.EmotionObserved, now, "2,-1,3,-2", null, 90, 1), now);
        Assert.AreEqual(1f, store.Snapshot.Valence);
        Assert.AreEqual(0f, store.Snapshot.Arousal);
        Assert.AreEqual(1f, store.Snapshot.Energy);
        Assert.AreEqual(-1f, store.Snapshot.Warmth);
        store.Expire(now.AddSeconds(2));
        Assert.AreEqual(0f, store.Snapshot.Warmth);
    }

    [Test]
    public void 信号过期后回落到未知或中性()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        store.Append(Event("return", LifeEventType.UserReturned, now, null, null, 80, 1), now);
        store.Append(Event("emotion", LifeEventType.EmotionObserved, now, "0.8,0.9,1.0", null, 90, 1), now);
        store.Expire(now.AddSeconds(2));
        Assert.AreEqual(LifePresence.Unknown, store.Snapshot.Presence);
        Assert.AreEqual(0f, store.Snapshot.Valence);
        Assert.AreEqual(0.5f, store.Snapshot.Energy);
    }

    [Test]
    public void TTL在到期时仍有效并在下一tick回落()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        store.Append(Event("return", LifeEventType.UserReturned, now, null, null, 80, 1), now);
        long versionAtExpiry = store.Snapshot.Version;
        store.Expire(now.AddSeconds(1));
        Assert.AreEqual(LifePresence.Present, store.Snapshot.Presence);
        Assert.AreEqual(versionAtExpiry, store.Snapshot.Version);
        store.Expire(now.AddSeconds(1).AddTicks(1));
        Assert.AreEqual(LifePresence.Unknown, store.Snapshot.Presence);
        Assert.Greater(store.Snapshot.Version, versionAtExpiry);
    }

    [Test]
    public void 零TTL事件在同一时刻有效并随后过期()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        store.Append(Event("zero", LifeEventType.UserReturned, now, null, null, 80, 0), now);
        Assert.AreEqual(LifePresence.Present, store.Snapshot.Presence);
        store.Expire(now.AddTicks(1));
        Assert.AreEqual(LifePresence.Unknown, store.Snapshot.Presence);
    }

    [Test]
    public void 旧的同类信号不能覆盖较新的状态()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("new", LifeEventType.UserWorking, now.AddSeconds(2), "new", null, 80, 30), now.AddSeconds(2)));
        long version = store.Snapshot.Version;
        Assert.IsFalse(store.Append(Event("old", LifeEventType.UserInactive, now, "old", null, 80, 30), now.AddSeconds(2)));
        Assert.AreEqual(LifePresence.Present, store.Snapshot.Presence);
        Assert.AreEqual(LifeActivity.Working, store.Snapshot.Activity);
        Assert.AreEqual(version, store.Snapshot.Version);
    }

    [Test]
    public void 拒绝重复事件不会改变版本状态或事件计数()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("same", LifeEventType.UserReturned, now), now));
        var before = store.Snapshot;
        int count = store.EventCount;
        Assert.IsFalse(store.Append(Event("same", LifeEventType.UserReturned, now.AddSeconds(1)), now.AddSeconds(1)));
        var after = store.Snapshot;
        Assert.AreEqual(before.Version, after.Version);
        Assert.AreEqual(before.Presence, after.Presence);
        Assert.AreEqual(count, store.EventCount);
    }

    [Test]
    public void 活跃动作到期后中断并保留超时原因且迟到完成无效()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("start", LifeEventType.ActionStarted, now, "wave", "timeout-1", 80, 1), now));
        store.Expire(now.AddSeconds(1).AddTicks(1));
        Assert.AreEqual(LifeActionStatus.Interrupted, store.Snapshot.ActionStatus);
        Assert.AreEqual("timeout", store.Snapshot.LastInterruption);
        Assert.IsFalse(store.Append(Event("done", LifeEventType.ActionCompleted, now.AddSeconds(2), "completed", "timeout-1"), now.AddSeconds(2)));
        Assert.AreEqual(LifeActionStatus.Interrupted, store.Snapshot.ActionStatus);
    }

    [Test]
    public void 过期注意力事件不能覆盖较新的目标()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("new", LifeEventType.DirectInteraction, now.AddSeconds(2), "new-target"), now.AddSeconds(2)));
        long version = store.Snapshot.Version;
        Assert.IsFalse(store.Append(Event("old", LifeEventType.DirectInteraction, now, "old-target"), now.AddSeconds(2)));
        Assert.AreEqual("pet", store.Snapshot.AttentionTarget);
        Assert.AreEqual(version, store.Snapshot.Version);
    }

    [Test]
    public void 注意力在到期时仍有效并在下一tick回落()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("attention", LifeEventType.DirectInteraction,
            now, "pet-click", null, 80, 1), now));

        var atExpiry = store.Snapshot;
        store.Expire(now.AddSeconds(1));
        Assert.AreEqual("pet", atExpiry.AttentionTarget);
        Assert.IsNotNull(atExpiry.AttentionMetadata);
        Assert.AreEqual("pet-click", atExpiry.AttentionMetadata.Reason);
        Assert.AreEqual("pet", store.Snapshot.AttentionTarget);
        Assert.IsNotNull(store.Snapshot.AttentionMetadata);

        store.Expire(now.AddSeconds(1).AddTicks(1));
        var expired = store.Snapshot;
        Assert.IsNull(expired.AttentionTarget);
        Assert.IsNull(expired.AttentionMetadata);
    }

    [Test]
    public void 不同动作关联源的迟到终态不能覆盖当前动作()
    {
        DateTime now = DateTime.UtcNow;
        var store = new LifeStateStore();
        Assert.IsTrue(store.Append(Event("a-start", LifeEventType.ActionStarted, now, "a", "action-a"), now));
        Assert.IsTrue(store.Append(Event("a-done", LifeEventType.ActionCompleted, now.AddSeconds(1), "a-ok", "action-a"), now.AddSeconds(1)));
        long version = store.Snapshot.Version;
        Assert.IsFalse(store.Append(Event("b-interrupt", LifeEventType.ActionInterrupted, now.AddSeconds(2), "b-late", "action-b"), now.AddSeconds(2)));
        Assert.AreEqual(LifeActionStatus.Completed, store.Snapshot.ActionStatus);
        Assert.AreEqual("a-ok", store.Snapshot.LastActionResult);
        Assert.AreEqual(version, store.Snapshot.Version);
    }
}

