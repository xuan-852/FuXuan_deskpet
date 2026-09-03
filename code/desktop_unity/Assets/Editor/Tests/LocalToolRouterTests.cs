using NUnit.Framework;
using Newtonsoft.Json.Linq;

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

    [Test]
    public void KeywordFallbackRecognizesScheduleQueries()
    {
        LocalToolPlan plan;

        Assert.IsTrue(LocalToolRouter.ShouldAttempt("chat", "查看今天的课表"));
        Assert.IsTrue(LocalToolRouter.TryBuildKeywordPlan("", "查看今天的课表", out plan));
        Assert.AreEqual("query_schedule", plan.ToolName);
        Assert.AreEqual("{}", plan.ArgumentsJson);

        Assert.IsTrue(LocalToolRouter.TryBuildKeywordPlan("", "查看第 12 周课程安排", out plan));
        Assert.AreEqual("query_schedule", plan.ToolName);
        StringAssert.Contains("\"week\":12", plan.ArgumentsJson);
        Assert.IsTrue(LocalToolRouter.IsAllowed("query_schedule", "knowledge"));
    }

    [Test]
    public void ScheduleOpenRequestUsesDashboardUrl()
    {
        LocalToolPlan plan;

        Assert.IsTrue(LocalToolRouter.ShouldAttempt("chat", "请打开课表网页"));
        Assert.IsTrue(LocalToolRouter.TryBuildScheduleOpenPlan("请打开课表网页", out plan));
        Assert.AreEqual("open_url", plan.ToolName);
        StringAssert.Contains(LocalToolRouter.ScheduleDashboardUrl, plan.ArgumentsJson);
        Assert.IsTrue(LocalToolRouter.IsAllowed("open_url", "knowledge"));

        Assert.IsFalse(LocalToolRouter.TryBuildScheduleOpenPlan("我今天有什么课", out plan));
    }

    [Test]
    public void EveryRegisteredToolHasANaturalLanguageRoute()
    {
        ToolRegistry.Initialize();
        JObject[] tools = JArray.Parse(ToolRegistry.GetToolsJson())
            .ToObject<JObject[]>();
        var unrouted = new System.Collections.Generic.List<string>();

        foreach (JObject item in tools)
        {
            string name = item["function"]?["name"]?.ToString();
            // 危险工具走审批而非本地自动路由，不要求出现在自然语言白名单中。
            if (ToolRegistry.IsDangerous(name)) continue;
            bool routed = LocalToolRouter.IsAllowed(name, "command")
                || LocalToolRouter.IsAllowed(name, "knowledge")
                || LocalToolRouter.IsAllowed(name, "operation");
            if (!routed) unrouted.Add(name);
        }

        Assert.IsEmpty(unrouted,
            "以下已注册工具没有进入任何自然语言意图目录: "
            + string.Join(", ", unrouted));
    }

    [Test]
    public void NaturalLanguageKeywordsCoverPreferencesTemplatesAndMotionReview()
    {
        Assert.IsTrue(LocalToolRouter.ShouldAttempt("chat", "请记住我喜欢无糖咖啡"));
        Assert.IsTrue(LocalToolRouter.ShouldAttempt("chat", "请记住这个任务模板"));
        Assert.IsTrue(LocalToolRouter.ShouldAttempt("chat", "请复盘并验证这个动作"));
        Assert.IsTrue(LocalToolRouter.IsAllowed("set_preference", "operation"));
        Assert.IsTrue(LocalToolRouter.IsAllowed("save_task_template", "operation"));
        Assert.IsTrue(LocalToolRouter.IsAllowed("run_verification", "operation"));
    }

    [Test]
    public void DocumentPlanUsesOriginalUserRequestAndSafeCompiler()
    {
        const string original = "帮我写一份中文 PDF：8 页，包含摘要、目录、三章正文和参考文献，排版正式。";
        string hardened;
        string error;
        bool ok = LocalToolRouter.TryHardenPlanArguments(
            "compile_latex", original,
            "{\"description\":\"模型缩写后的需求\",\"compiler\":\"xelatex\",\"title\":\"报告\"}",
            out hardened, out error);

        Assert.IsTrue(ok, error);
        JObject args = JObject.Parse(hardened);
        Assert.AreEqual(original, args["description"]?.ToString());
        Assert.AreEqual("xelatex", args["compiler"]?.ToString());
    }

    [Test]
    public void DocumentPlanRejectsUnsafeCompilerAndOversizedRequest()
    {
        string hardened;
        string error;
        Assert.IsFalse(LocalToolRouter.TryHardenPlanArguments(
            "compile_latex", "生成 PDF", "{\"compiler\":\"cmd.exe\"}",
            out hardened, out error));
        Assert.IsTrue(error.Contains("编译器"));

        Assert.IsFalse(LocalToolRouter.TryHardenPlanArguments(
            "compile_latex", new string('a', LocalToolRouter.MaxForwardedTaskChars + 1), "{}",
            out hardened, out error));
        Assert.IsTrue(error.Contains("过长"));
    }

    [Test]
    public void PdfNaturalLanguageFallbackKeepsFullRequest()
    {
        const string message = "请帮我写一份 PDF 报告，主题是本地模型安全，包含摘要、目录和参考文献。";
        LocalToolPlan plan;
        Assert.IsTrue(LocalToolRouter.TryBuildKeywordPlan("knowledge", message, out plan));
        Assert.AreEqual("compile_latex", plan.ToolName);
        StringAssert.Contains(message, plan.ArgumentsJson);
    }

    [Test]
    public void ComplexTasksUseQualityPlannerAndSimpleQueriesStayLightweight()
    {
        Assert.IsTrue(LocalToolRouter.ShouldUseQualityPlanner(
            "请写一份包含摘要、目录、参考文献和正式排版的 PDF 报告"));
        Assert.IsTrue(LocalToolRouter.ShouldUseQualityPlanner(
            "请让 OpenClaw 登录网站，完成多步骤调研并汇总结果"));
        Assert.IsFalse(LocalToolRouter.ShouldUseQualityPlanner("请查看当前系统信息"));
        Assert.IsFalse(LocalToolRouter.ShouldUseQualityPlanner("今天天气怎么样"));
    }
}
