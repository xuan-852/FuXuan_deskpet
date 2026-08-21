using NUnit.Framework;

public class LocalToolRouterTests
{
    [Test]
    public void ParsePlanAcceptsMarkdownWrappedCallJson()
    {
        LocalToolPlan plan = LocalToolRouter.ParsePlan(
            "```json\n{\"action\":\"call\",\"tool\":\"open_app\",\"arguments\":{\"app\":\"notepad\"},\"reason\":\"打开记事本\"}\n```");

        Assert.IsTrue(plan.Success);
        Assert.IsTrue(plan.ShouldExecute);
        Assert.AreEqual("open_app", plan.ToolName);
        StringAssert.Contains("notepad", plan.ArgumentsJson);
    }

    [Test]
    public void ParsePlanAcceptsNoToolDecision()
    {
        LocalToolPlan plan = LocalToolRouter.ParsePlan(
            "{\"action\":\"none\",\"tool\":\"\",\"arguments\":{},\"reason\":\"普通聊天\"}");

        Assert.IsTrue(plan.Success);
        Assert.IsFalse(plan.ShouldExecute);
        Assert.AreEqual("", plan.ToolName);
    }

    [Test]
    public void IntentAllowlistRejectsToolsOutsideCurrentRoute()
    {
        Assert.IsTrue(LocalToolRouter.IsAllowed("open_app", "command"));
        Assert.IsTrue(LocalToolRouter.IsAllowed("generate_ppt", "knowledge"));
        Assert.IsFalse(LocalToolRouter.IsAllowed("file_delete", "command"));
        Assert.IsFalse(LocalToolRouter.IsAllowed("file_delete", "chat"));
    }

    [Test]
    public void ActionKeywordCanRecoverFromClassifierMiss()
    {
        Assert.IsTrue(LocalToolRouter.ShouldAttempt("chat", "请帮我打开记事本"));
        Assert.IsFalse(LocalToolRouter.ShouldAttempt("chat", "今天感觉有点累"));
    }

    [Test]
    public void CompactCatalogKeepsNamesAndArgumentsOnly()
    {
        string source = "[{\"type\":\"function\",\"function\":{\"name\":\"open_app\",\"description\":\"打开应用\",\"parameters\":{\"type\":\"object\",\"properties\":{\"app\":{\"type\":\"string\"}}}}}]";
        string compact = LocalToolRouter.BuildCompactCatalog(source);

        StringAssert.Contains("open_app", compact);
        StringAssert.Contains("properties", compact);
        StringAssert.DoesNotContain("function", compact);
    }

    [Test]
    public void MalformedNoArgumentPlanCanRecoverSafeTool()
    {
        LocalToolPlan plan = LocalToolRouter.ParsePlan(
            "{\"action\":\"call\",\"tool\":\"get_system_info\",\"arguments\":{\"reason\" \"系统状态\"}}" );

        Assert.IsTrue(plan.Success);
        Assert.IsTrue(plan.ShouldExecute);
        Assert.AreEqual("get_system_info", plan.ToolName);
        Assert.AreEqual("{}", plan.ArgumentsJson);
    }

    [Test]
    public void KeywordFallbackBuildsCommonLocalTasks()
    {
        LocalToolPlan plan;

        Assert.IsTrue(LocalToolRouter.TryBuildKeywordPlan("", "请查看当前系统信息", out plan));
        Assert.AreEqual("get_system_info", plan.ToolName);

        Assert.IsTrue(LocalToolRouter.TryBuildKeywordPlan("", "请搜索项目里的 README.md 文件", out plan));
        Assert.AreEqual("search_files", plan.ToolName);
        StringAssert.Contains("README.md", plan.ArgumentsJson);

        Assert.IsTrue(LocalToolRouter.TryBuildKeywordPlan("", "请把“本地工具测试”复制到剪贴板", out plan));
        Assert.AreEqual("set_clipboard", plan.ToolName);
        StringAssert.Contains("本地工具测试", plan.ArgumentsJson);

        Assert.IsTrue(LocalToolRouter.IsAllowed("generate_xlsx", "operation"));
    }
}
