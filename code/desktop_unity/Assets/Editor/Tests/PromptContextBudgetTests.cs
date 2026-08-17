using NUnit.Framework;

/// <summary>PromptContextBudget 的纯文本预算测试，不创建 Unity 对象、不联网。</summary>
public class PromptContextBudgetTests
{
    [Test]
    public void 未超预算时保持原文不变()
    {
        string text = "固定前缀\n动态内容";
        Assert.AreSame(text, PromptContextBudget.TrimSection(text, 100, "活动"));
    }

    [Test]
    public void 超预算时结果不超过上限且包含裁剪标记()
    {
        string text = new string('甲', 100) + "尾部关键字段";
        string trimmed = PromptContextBudget.TrimSection(text, 40, "浏览器标签");

        Assert.LessOrEqual(trimmed.Length, 40);
        StringAssert.Contains("浏览器标签已按上下文预算裁剪", trimmed);
        StringAssert.Contains("关键字段", trimmed);
    }

    [Test]
    public void 裁剪保留头部和尾部()
    {
        string text = "头部信息" + new string('中', 80) + "尾部信息";
        string trimmed = PromptContextBudget.TrimSection(text, 32, "知识库");

        StringAssert.StartsWith("头部", trimmed);
        StringAssert.Contains("尾部信息", trimmed);
    }

    [Test]
    public void 极小预算不会越界()
    {
        string text = "一段较长的内容";
        string trimmed = PromptContextBudget.TrimSection(text, 5, "段落");
        Assert.AreEqual(5, trimmed.Length);
    }

    [Test]
    public void Token估算对空文本为零并向上取整()
    {
        Assert.AreEqual(0, PromptContextBudget.EstimateTokens(""));
        Assert.AreEqual(2, PromptContextBudget.EstimateTokens("abc"));
        Assert.AreEqual(3, PromptContextBudget.EstimateTokens("中文内容啊"));
    }
}
