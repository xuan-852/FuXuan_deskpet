using System;
using NUnit.Framework;

public class ToolConfirmationWaitTests
{
    [SetUp]
    public void SetUp() { ToolConfirmManager.Resolve(false); }

    [TearDown]
    public void TearDown() { ToolConfirmManager.Resolve(false); }

    [TestCase(true)]
    [TestCase(false)]
    public void UserDecisionCompletesOnceAndDoesNotClearNextRequest(bool decision)
    {
        int calls = 0;
        bool? result = null;
        var wait = ToolConfirmManager.WaitForDecision("file_delete", "{}", "test only",
            ok => { calls++; result = ok; });
        Assert.IsTrue(wait.MoveNext());
        ToolConfirmManager.Resolve(decision);
        ToolConfirmManager.Request("lock_screen", "{}", "next test only", null);
        Assert.IsFalse(wait.MoveNext());
        ((IDisposable)wait).Dispose();
        Assert.AreEqual(decision, result);
        Assert.AreEqual(1, calls);
        Assert.AreEqual("lock_screen", ToolConfirmManager.PendingTool);
    }

    [Test]
    public void BusyWaitRejectsWithoutReplacingExistingApproval()
    {
        ToolConfirmManager.Request("file_delete", "original", "existing", null);
        bool? result = null;
        var wait = ToolConfirmManager.WaitForDecision("lock_screen", "new", "new", ok => result = ok);
        Assert.IsFalse(wait.MoveNext());
        Assert.AreEqual(false, result);
        Assert.AreEqual("file_delete", ToolConfirmManager.PendingTool);
        Assert.AreEqual("original", ToolConfirmManager.PendingArgs);
    }

    [Test]
    public void ImmediateTimeoutRejectsAndClearsPendingWithoutExecutingAnything()
    {
        bool? result = null;
        var wait = ToolConfirmManager.WaitForDecision("file_delete", "{}", "test only", ok => result = ok, 0f);
        Assert.IsFalse(wait.MoveNext());
        Assert.AreEqual(false, result);
        Assert.IsFalse(ToolConfirmManager.HasPending);
    }

    [Test]
    public void DisposingUnresolvedWaitRejectsOwnedApproval()
    {
        bool callback = false;
        var wait = ToolConfirmManager.WaitForDecision("file_delete", "{}", "test only", _ => callback = true);
        Assert.IsTrue(wait.MoveNext());
        ((IDisposable)wait).Dispose();
        Assert.IsFalse(ToolConfirmManager.HasPending);
        Assert.IsFalse(callback, "Disposed consumer must not resume tool execution");
    }
}
