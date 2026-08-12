using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// P5 省钱与自学习测试套件
/// 覆盖：TaskTrajectoryManager（记录/检索/淘汰/参考文本/FormatForPrompt）
///      TaskTemplateManager（预置模板/占位符替换/CRUD/FormatForPrompt）
///      任务模板工具（query_task_templates / save_task_template / remove_task_template）
///
/// ★ 测试数据隔离：SetUp 备份 task_trajectories.json / task_templates.json 原内容，
///   TearDown 完全恢复（不存在则删除），确保不污染真实学习数据。
/// </summary>
public class P5TrajectoryTests
{
    private const string TRAJ_FILE = "task_trajectories.json";
    private const string TPL_FILE = "task_templates.json";

    private GameObject _trajGo;
    private TaskTrajectoryManager _trajMgr;
    private GameObject _tplGo;
    private TaskTemplateManager _tplMgr;

    private string _trajBackup;
    private bool _trajExisted;
    private string _tplBackup;
    private bool _tplExisted;

    // ================================================================
    //  辅助 — 单例隔离 + 文件备份恢复
    // ================================================================

    private static void ResetTrajectoryInstance()
    {
        var field = typeof(TaskTrajectoryManager)
            .GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, null);
    }

    private static void ResetTemplateInstance()
    {
        var field = typeof(TaskTemplateManager)
            .GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, null);
    }

    private static string DataPath(string fileName)
        => Path.Combine(DataPathConfig.DataRoot, fileName);

    private static void RegisterInstance<T>(T instance)
    {
        var field = typeof(T)
            .GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, instance);
    }

    [SetUp]
    public void SetUp()
    {
        // ——— 备份原数据文件 ———
        _trajExisted = File.Exists(DataPath(TRAJ_FILE));
        _trajBackup = _trajExisted ? File.ReadAllText(DataPath(TRAJ_FILE)) : "";
        _tplExisted = File.Exists(DataPath(TPL_FILE));
        _tplBackup = _tplExisted ? File.ReadAllText(DataPath(TPL_FILE)) : "";

        // ——— TaskTrajectoryManager ———
        ResetTrajectoryInstance();
        _trajGo = new GameObject("TestTrajectories");
        _trajMgr = _trajGo.AddComponent<TaskTrajectoryManager>();
        RegisterInstance(_trajMgr);
        _trajMgr.maxEntries = 5; // 缩小上限便于测淘汰
        _trajMgr.Load();

        // ——— TaskTemplateManager ———
        ResetTemplateInstance();
        _tplGo = new GameObject("TestTemplates");
        _tplMgr = _tplGo.AddComponent<TaskTemplateManager>();
        RegisterInstance(_tplMgr);
        _tplMgr.Load();
    }

    [TearDown]
    public void TearDown()
    {
        if (_trajGo != null) GameObject.DestroyImmediate(_trajGo);
        if (_tplGo != null) GameObject.DestroyImmediate(_tplGo);
        ResetTrajectoryInstance();
        ResetTemplateInstance();

        // ——— 恢复原数据文件 ———
        try
        {
            if (_trajExisted)
                File.WriteAllText(DataPath(TRAJ_FILE), _trajBackup);
            else if (File.Exists(DataPath(TRAJ_FILE)))
                File.Delete(DataPath(TRAJ_FILE));

            if (_tplExisted)
                File.WriteAllText(DataPath(TPL_FILE), _tplBackup);
            else if (File.Exists(DataPath(TPL_FILE)))
                File.Delete(DataPath(TPL_FILE));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[P5TrajectoryTests] 恢复数据文件失败: {e.Message}");
        }
    }

    // ================================================================
    //  1. TaskTrajectoryManager — 记录与检索
    // ================================================================

    [Test]
    public void TrajectoryManager_记录成功与失败轨迹()
    {
        _trajMgr.RecordTrajectory("打开 B 站搜索明日方舟本周播放最高的视频", "agent", true, "已返回前5个视频", "", 120);
        _trajMgr.RecordTrajectory("访问教务系统查询成绩", "browser", false, "", "登录失败：验证码错误", 60);

        Assert.AreEqual(2, _trajMgr.Count, "应记录 2 条轨迹");
        var success = _trajMgr.GetSimilarTrajectories("搜索 B 站明日方舟视频", 5);
        Assert.IsTrue(success.Count > 0, "相似任务应能检索到");
        Assert.IsTrue(success[0].success, "检索到的轨迹应成功");
        Assert.IsTrue(success[0].resultSummary.Contains("前5个"), "应保留结果摘要");
    }

    [Test]
    public void TrajectoryManager_不相似任务检索不到()
    {
        _trajMgr.RecordTrajectory("编译 LaTeX 论文为 PDF", "agent", true, "编译成功", "", 30);
        var hits = _trajMgr.GetSimilarTrajectories("打开音乐播放器放一首歌", 5);
        Assert.AreEqual(0, hits.Count, "完全不相关的任务不应命中");
    }

    [Test]
    public void TrajectoryManager_超过上限淘汰最旧()
    {
        for (int i = 0; i < 8; i++)
            _trajMgr.RecordTrajectory($"测试任务 {i} 号", "agent", true, "", "", 10);

        Assert.AreEqual(5, _trajMgr.Count, "超过 maxEntries=5 应淘汰到上限");

        // 最早记录的 0 号应已被淘汰（剩余 3~7 号与 0 号相似度高仍会命中，
        // 但结果中不应再出现「0 号」自身）
        var hits = _trajMgr.GetSimilarTrajectories("测试任务 0 号", 10);
        Assert.IsTrue(hits.Count > 0, "剩余任务与 0 号相似，应能检索到");
        Assert.IsFalse(hits.Any(h => h.taskDescription.Contains("0 号")), "最早的任务 0 号应已被淘汰");

        // 最新的任务 7 号应保留在库中
        var latest = _trajMgr.GetSimilarTrajectories("测试任务 7 号", 10);
        Assert.IsTrue(latest.Any(h => h.taskDescription.Contains("7 号")), "最新的任务 7 号应保留");
    }

    [Test]
    public void TrajectoryManager_构建参考文本附加成功经验与失败教训()
    {
        _trajMgr.RecordTrajectory("访问 bilibili 看番剧更新", "browser", true, "最新一集已更新", "", 100);
        _trajMgr.RecordTrajectory("访问 bilibili 看番剧更新", "browser", false, "", "登录被风控拦截", 50);

        string refText = _trajMgr.BuildReferenceText("访问 bilibili 查看番剧是否更新");
        Assert.IsTrue(!string.IsNullOrEmpty(refText), "相似任务应产生参考文本");
        Assert.IsTrue(refText.Contains("成功经验"), "应包含成功经验");
        Assert.IsTrue(refText.Contains("失败教训"), "应包含失败教训");
        Assert.IsTrue(refText.Contains("风控"), "失败原因应被引用");
    }

    [Test]
    public void TrajectoryManager_参考计数随引用增加()
    {
        _trajMgr.RecordTrajectory("去 GitHub 看仓库发布", "agent", true, "v2.0 发布", "", 90);
        _trajMgr.BuildReferenceText("去 GitHub 看仓库发布信息");
        _trajMgr.BuildReferenceText("去 GitHub 查看最新发布");

        var hits = _trajMgr.GetSimilarTrajectories("去 GitHub 看仓库发布", 5);
        Assert.IsTrue(hits[0].referenceCount >= 2, "被引用 2 次后 referenceCount 应 ≥2");
    }

    [Test]
    public void TrajectoryManager_空库不产生参考文本()
    {
        Assert.AreEqual("", _trajMgr.BuildReferenceText("随便什么任务"), "空库应返回空串");
        Assert.AreEqual("", _trajMgr.FormatForPrompt(), "空库 FormatForPrompt 应返回空串");
    }

    [Test]
    public void TrajectoryManager_FormatForPrompt含轨迹摘要()
    {
        _trajMgr.RecordTrajectory("查天气", "agent", true, "晴 26°C", "", 15);
        string text = _trajMgr.FormatForPrompt();
        Assert.IsTrue(text.Contains("任务轨迹"), "应包含标题");
        Assert.IsTrue(text.Contains("查天气"), "应包含任务描述");
    }

    // ================================================================
    //  2. TaskTemplateManager — 模板库
    // ================================================================

    [Test]
    public void TemplateManager_预置模板已初始化()
    {
        Assert.IsTrue(_tplMgr.Count >= 5, "应预置至少 5 个高频任务模板");
        Assert.IsNotNull(_tplMgr.GetTemplate("check_website_updates"), "应含 check_website_updates");
        Assert.IsNotNull(_tplMgr.GetTemplate("download_file"), "应含 download_file");
    }

    [Test]
    public void TemplateManager_占位符替换生成任务()
    {
        string task = _tplMgr.ApplyTemplate("check_website_updates", new Dictionary<string, string>
        {
            { "url", "https://example.com" }
        });
        Assert.IsTrue(!string.IsNullOrEmpty(task), "应生成任务描述");
        Assert.IsTrue(task.Contains("https://example.com"), "占位符应被替换为实参");
        Assert.IsFalse(task.Contains("{url}"), "占位符不应残留");
    }

    [Test]
    public void TemplateManager_未提供占位符参数保持原样()
    {
        string task = _tplMgr.ApplyTemplate("download_file", new Dictionary<string, string>
        {
            { "url", "https://x.com/a.zip" }
        });
        Assert.IsTrue(task.Contains("{save_path}"), "缺失的 save_path 占位符应保持原样（LLM 自行处理）");
    }

    [Test]
    public void TemplateManager_不存在的模板返回空串()
    {
        Assert.AreEqual("", _tplMgr.ApplyTemplate("no_such_template", null), "不存在的模板应返回空串");
        Assert.IsNull(_tplMgr.GetTemplate("no_such_template"));
    }

    [Test]
    public void TemplateManager_保存与覆盖模板()
    {
        Assert.IsTrue(_tplMgr.SaveTemplate("test_custom", "测试模板", "执行 {target} 的检查", "测试"));
        var tpl = _tplMgr.GetTemplate("test_custom");
        Assert.IsNotNull(tpl);
        Assert.AreEqual("执行 {target} 的检查", tpl.template);
        Assert.AreEqual("测试", tpl.category);

        // 同名覆盖
        Assert.IsTrue(_tplMgr.SaveTemplate("test_custom", "新描述", "新模板 {a}", ""));
        Assert.AreEqual("新模板 {a}", _tplMgr.GetTemplate("test_custom").template);
        Assert.AreEqual(1, _tplMgr.GetAllTemplates().Count(t => t.name == "test_custom"), "覆盖不应新增条目");
    }

    [Test]
    public void TemplateManager_删除模板()
    {
        _tplMgr.SaveTemplate("test_delete_me", "", "任务 {x}", "");
        Assert.IsTrue(_tplMgr.RemoveTemplate("test_delete_me"));
        Assert.IsNull(_tplMgr.GetTemplate("test_delete_me"));
        Assert.IsFalse(_tplMgr.RemoveTemplate("test_delete_me"), "重复删除应返回 false");
    }

    [Test]
    public void TemplateManager_空参数保存被拒绝()
    {
        Assert.IsFalse(_tplMgr.SaveTemplate("", "", "", ""), "空模板名应拒绝");
        Assert.IsFalse(_tplMgr.SaveTemplate("test_bad", "", "", ""), "空模板内容应拒绝");
    }

    [Test]
    public void TemplateManager_FormatForPrompt含模板清单()
    {
        string text = _tplMgr.FormatForPrompt();
        Assert.IsTrue(text.Contains("任务模板"), "应包含标题");
        Assert.IsTrue(text.Contains("check_website_updates"), "应包含预置模板名");
        Assert.IsTrue(_tplMgr.ListTemplatesText().Contains("openclaw_task"), "清单应说明使用方式");
    }

    // ================================================================
    //  3. 任务模板工具（无网络，只读/自愈）
    // ================================================================

    [Test]
    public void QueryTaskTemplatesTool_返回模板清单()
    {
        var tool = new QueryTaskTemplatesTool();
        string result = tool.Execute("{}");
        Assert.IsTrue(result.StartsWith("【"), "应返回清单文本");
        Assert.IsTrue(result.Contains("check_website_updates"), "清单应含预置模板");
        Assert.IsFalse(result.StartsWith("❌"), "不应报错");
    }

    [Test]
    public void SaveTaskTemplateTool_保存并回读()
    {
        var tool = new SaveTaskTemplateTool();
        string json = "{\"name\":\"test_tool_tpl\",\"template\":\"访问 {site} 汇总内容\",\"description\":\"测试\",\"category\":\"测试\"}";
        string result = tool.Execute(json);
        Assert.IsTrue(result.StartsWith("✅"), "保存应成功");

        var tpl = _tplMgr.GetTemplate("test_tool_tpl");
        Assert.IsNotNull(tpl, "工具保存的模板应可读取");
        Assert.AreEqual("访问 {site} 汇总内容", tpl.template);
    }

    [Test]
    public void SaveTaskTemplateTool_缺参数报错()
    {
        var tool = new SaveTaskTemplateTool();
        string result = tool.Execute("{\"name\":\"test_only_name\"}");
        Assert.IsTrue(result.StartsWith("❌"), "缺 template 应报错");
    }

    [Test]
    public void RemoveTaskTemplateTool_删除模板()
    {
        _tplMgr.SaveTemplate("test_tool_del", "", "任务 {x}", "");
        var tool = new RemoveTaskTemplateTool();
        string result = tool.Execute("{\"name\":\"test_tool_del\"}");
        Assert.IsTrue(result.StartsWith("✅"), "删除应成功");
        Assert.IsNull(_tplMgr.GetTemplate("test_tool_del"));

        string result2 = tool.Execute("{\"name\":\"test_tool_del\"}");
        Assert.IsTrue(result2.StartsWith("❌"), "再次删除应报未找到");
    }

    [Test]
    public void TaskKey_中文与英文指纹可匹配()
    {
        string key1 = TaskTrajectoryManager.BuildKey("打开 bilibili 看番剧更新");
        string key2 = TaskTrajectoryManager.BuildKey("帮我打开 bilibili 看看番剧更新了没");
        float sim = TaskTrajectoryManager.Jaccard(key1, key2);
        Assert.IsTrue(sim >= 0.2f, $"相似任务指纹相似度应 ≥0.2，实际 {sim:F2}");
        Assert.IsTrue(!string.IsNullOrEmpty(key1));
    }

    [Test]
    public void TaskKey_完全不同任务相似度低()
    {
        string key1 = TaskTrajectoryManager.BuildKey("编译论文");
        string key2 = TaskTrajectoryManager.BuildKey("播放音乐");
        Assert.IsTrue(TaskTrajectoryManager.Jaccard(key1, key2) < 0.2f, "不同任务相似度应低于阈值");
    }
}
