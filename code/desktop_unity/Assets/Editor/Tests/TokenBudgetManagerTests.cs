using System;
using NUnit.Framework;

/// <summary>TokenBudgetManager 的纯内存预算策略测试，不发起任何网络请求。</summary>
public class TokenBudgetManagerTests
{
    private static readonly DateTime BaseUtc = new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        TokenBudgetManager.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        TokenBudgetManager.Reset();
    }

    [Test]
    public void Idle_首次请求允许并记录一次()
    {
        Assert.IsTrue(TokenBudgetManager.TryAcquire("idle", BaseUtc, out string reason), reason);
        Assert.AreEqual(1, TokenBudgetManager.GetAcquiredCount("idle", BaseUtc));
    }

    [Test]
    public void Idle_最短冷却内拒绝且不增加计数()
    {
        Assert.IsTrue(TokenBudgetManager.TryAcquire("idle", BaseUtc, out _));
        Assert.IsFalse(TokenBudgetManager.TryAcquire("idle", BaseUtc.AddMinutes(9), out string reason));
        StringAssert.Contains("成本闸门", reason);
        Assert.AreEqual(1, TokenBudgetManager.GetAcquiredCount("idle", BaseUtc.AddMinutes(9)));
    }

    [Test]
    public void Idle_达到小时上限后拒绝()
    {
        for (int i = 0; i < 4; i++)
        {
            DateTime now = BaseUtc.AddMinutes(i * 10);
            Assert.IsTrue(TokenBudgetManager.TryAcquire("idle", now, out string reason), reason);
        }

        Assert.IsFalse(TokenBudgetManager.TryAcquire("idle", BaseUtc.AddMinutes(40), out string blockedReason));
        StringAssert.Contains("4 次", blockedReason);
        Assert.AreEqual(4, TokenBudgetManager.GetAcquiredCount("idle", BaseUtc.AddMinutes(40)));
    }

    [Test]
    public void 滑动窗口过期后重新允许()
    {
        for (int i = 0; i < 4; i++)
        {
            DateTime now = BaseUtc.AddMinutes(i * 10);
            Assert.IsTrue(TokenBudgetManager.TryAcquire("idle", now, out string reason), reason);
        }

        DateTime afterWindow = BaseUtc.AddHours(1).AddSeconds(1);
        Assert.IsTrue(TokenBudgetManager.TryAcquire("idle", afterWindow, out string afterReason), afterReason);
        Assert.AreEqual(4, TokenBudgetManager.GetAcquiredCount("idle", afterWindow), "只有最早的一次调用已滑出窗口，新调用后窗口内应有 4 次");
    }

    [Test]
    public void Reflect_使用独立的更严格策略()
    {
        Assert.IsTrue(TokenBudgetManager.TryAcquire("reflect", BaseUtc, out string firstReason), firstReason);
        Assert.IsFalse(TokenBudgetManager.TryAcquire("reflect", BaseUtc.AddMinutes(10), out string blockedReason));
        StringAssert.Contains("reflect", blockedReason);

        Assert.IsTrue(TokenBudgetManager.TryAcquire("reflect", BaseUtc.AddMinutes(20), out string secondReason), secondReason);
        Assert.AreEqual(2, TokenBudgetManager.GetAcquiredCount("reflect", BaseUtc.AddMinutes(20)));
    }

    [Test]
    public void Chat和未知来源默认只观测不拒绝()
    {
        for (int i = 0; i < 20; i++)
        {
            Assert.IsTrue(TokenBudgetManager.TryAcquire("chat", BaseUtc.AddSeconds(i), out string chatReason), chatReason);
            Assert.IsTrue(TokenBudgetManager.TryAcquire("custom", BaseUtc.AddSeconds(i), out string customReason), customReason);
        }
    }

    [Test]
    public void 来源名大小写和空白会归一化()
    {
        Assert.IsTrue(TokenBudgetManager.TryAcquire(" IDLE ", BaseUtc, out string reason), reason);
        Assert.IsFalse(TokenBudgetManager.TryAcquire("idle", BaseUtc.AddMinutes(1), out string blockedReason));
        StringAssert.Contains("idle", blockedReason);
    }

    [Test]
    public void 成本闸门错误可识别为不可重试()
    {
        string error = TokenBudgetManager.BUDGET_ERROR_PREFIX + "稍后再试";
        Assert.IsTrue(TokenBudgetManager.IsBudgetRejection(error));
        Assert.IsFalse(TokenBudgetManager.IsBudgetRejection("网络超时"));
    }
}
