using NUnit.Framework;

/// <summary>工具结果回填预算测试，不执行任何真实工具。</summary>
public class ToolResultBudgetTests
{
    [Test]
    public void 大文件结果预算高于普通结果()
    {
        Assert.Greater(ToolResultBudget.GetMaxChars("file_read"), ToolResultBudget.GetMaxChars("notify"));
    }

    [Test]
    public void OpenClaw结果有独立预算()
    {
        Assert.AreEqual(4500, ToolResultBudget.GetMaxChars("openclaw_task"));
    }

    [Test]
    public void 短结果保持不变()
    {
        string result = "✅ 已完成";
        Assert.AreSame(result, ToolResultBudget.Compact("notify", result));
    }

    [Test]
    public void 长结果按工具预算压缩()
    {
        string result = new string('甲', 5000) + "末尾关键结果";
        string compact = ToolResultBudget.Compact("notify", result);

        Assert.LessOrEqual(compact.Length, ToolResultBudget.GetMaxChars("notify"));
        StringAssert.Contains("工具结果·notify已按上下文预算裁剪", compact);
        StringAssert.Contains("关键结果", compact);
    }

    [Test]
    public void 空工具名仍能安全压缩()
    {
        string result = new string('甲', 4000);
        Assert.DoesNotThrow(() => ToolResultBudget.Compact(null, result));
        Assert.LessOrEqual(ToolResultBudget.Compact(null, result).Length, ToolResultBudget.GetMaxChars(null));
    }
}
