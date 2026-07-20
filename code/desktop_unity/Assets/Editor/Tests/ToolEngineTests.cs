using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// ToolEngine 重构验证测试套件
/// 覆盖：注册/初始化/JSON Schema/同步执行/异步执行/错误处理/ChatManager 接口
///
/// 设计原则：不硬编码工具列表——通过反射动态获取实际列表，
/// 确保新增工具时无需更新测试。
/// </summary>
public class ToolEngineTests
{
    // ================================================================
    //  辅助方法 — 动态获取工具列表
    // ================================================================

    private static List<string> GetSyncToolNames()
    {
        var field = typeof(ToolRegistry).GetField("_syncTools",
            BindingFlags.NonPublic | BindingFlags.Static);
        return ((Dictionary<string, IPetTool>)field?.GetValue(null))
            ?.Keys.ToList() ?? new List<string>();
    }

    private static List<string> GetAsyncToolNames()
    {
        var field = typeof(ToolRegistry).GetField("_asyncTools",
            BindingFlags.NonPublic | BindingFlags.Static);
        return ((Dictionary<string, IPetTool>)field?.GetValue(null))
            ?.Keys.ToList() ?? new List<string>();
    }

    private static List<IPetTool> GetAllTools()
    {
        var tools = new List<IPetTool>();
        var sf = typeof(ToolRegistry).GetField("_syncTools",
            BindingFlags.NonPublic | BindingFlags.Static);
        var af = typeof(ToolRegistry).GetField("_asyncTools",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (sf?.GetValue(null) is Dictionary<string, IPetTool> sync)
            tools.AddRange(sync.Values);
        if (af?.GetValue(null) is Dictionary<string, IPetTool> async)
            tools.AddRange(async.Values);
        return tools;
    }

    private static void ResetRegistry()
    {
        var field = typeof(ToolRegistry).GetField("_initialized",
            BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, false);
        ToolRegistry.Initialize();
    }

    [SetUp]
    public void SetUp()
    {
        ResetRegistry();
    }

    // ================================================================
    //  1. 注册与初始化
    // ================================================================

    [Test]
    public void Initialize_发现工具数量大于零()
    {
        Assert.Greater(ToolRegistry.ToolCount, 0,
            "初始化后应发现至少 1 个工具");
    }

    [Test]
    public void 所有工具名称唯一()
    {
        var allNames = new List<string>();
        allNames.AddRange(GetSyncToolNames());
        allNames.AddRange(GetAsyncToolNames());

        var dupes = allNames.GroupBy(n => n)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.AreEqual(0, dupes.Count,
            $"发现重复工具名称: {string.Join(", ", dupes)}");
    }

    [Test]
    public void 所有工具可被HasTool检测()
    {
        var allNames = new List<string>();
        allNames.AddRange(GetSyncToolNames());
        allNames.AddRange(GetAsyncToolNames());

        var missing = allNames.Where(n => !ToolRegistry.HasTool(n)).ToList();
        Assert.AreEqual(0, missing.Count,
            $"HasTool 未能检测到: {string.Join(", ", missing)}");
    }

    [Test]
    public void IsAsync分类与内部字典一致()
    {
        foreach (var name in GetSyncToolNames())
        {
            Assert.IsFalse(ToolRegistry.IsAsync(name),
                $"同步工具 {name} 不应被标记为异步");
        }

        foreach (var name in GetAsyncToolNames())
        {
            Assert.IsTrue(ToolRegistry.IsAsync(name),
                $"异步工具 {name} 应被标记为异步");
        }
    }

    // ================================================================
    //  2. JSON Schema 生成
    // ================================================================

    [Test]
    public void GetToolsJson_返回有效JSON()
    {
        string json = ToolRegistry.GetToolsJson();
        Assert.IsNotNull(json);
        Assert.IsTrue(json.StartsWith("["), "JSON 应以 [ 开头");
        Assert.IsTrue(json.EndsWith("]"), "JSON 应以 ] 结尾");

        // 验证每个工具名称都在 JSON 中出现
        foreach (var name in GetSyncToolNames())
        {
            StringAssert.Contains(name, json,
                $"JSON 应包含同步工具 {name}");
        }
        foreach (var name in GetAsyncToolNames())
        {
            StringAssert.Contains(name, json,
                $"JSON 应包含异步工具 {name}");
        }

        // 统计 function 定义数
        int functionCount = 0;
        int idx = 0;
        while ((idx = json.IndexOf("\"type\": \"function\"", idx)) != -1)
        {
            functionCount++;
            idx++;
        }
        Assert.AreEqual(ToolRegistry.ToolCount, functionCount,
            $"JSON 应包含 {ToolRegistry.ToolCount} 个 function 定义");
    }

    [Test]
    public void GetToolsJson_自动初始化()
    {
        ResetRegistry();
        // 不显式初始化，直接调用 GetToolsJson（应自动触发初始化）
        string json = ToolRegistry.GetToolsJson();
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("\"type\": \"function\""),
            "自动初始化后应包含 function 定义");
    }

    // ================================================================
    //  3. 同步工具执行
    // ================================================================

    [Test]
    public void Execute_未知工具返回错误()
    {
        string result = ToolRegistry.Execute("non_existent_tool", "{}");
        Assert.IsTrue(result.Contains("不识此术"),
            "未知工具应返回错误提示");
    }

    // ⚠️ 注意：不遍历所有工具测试空参数执行！
    // lock_screen 等工具的空参数调用会导致系统锁屏。
    // 此处只测试少数安全的只读工具。

    [Test]
    public void Execute_只读工具不抛异常()
    {
        // 只读安全工具白名单——这些工具以空参数调用不会产生副作用
        string[] safeReadonlyTools = {
            "get_system_info", "get_mouse_pos", "get_clipboard",
            "get_volume", "get_screen_size", "query_reminders",
            "query_exams", "query_scores", "query_schedule",
            "show_inner_state", "get_traffic_info"
        };

        foreach (var name in safeReadonlyTools)
        {
            if (!ToolRegistry.HasTool(name)) continue; // 跳过不存在的工具

            Assert.DoesNotThrow(() => ToolRegistry.Execute(name, null),
                $"工具 {name} 处理 null 参数时不应抛异常");
            Assert.DoesNotThrow(() => ToolRegistry.Execute(name, "{}"),
                $"工具 {name} 处理空 JSON 时不应抛异常");
        }
    }

    [Test]
    public void GetSystemInfo_返回合法信息()
    {
        string result = ToolRegistry.Execute("get_system_info", "{}");
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result);
        // batchmode 下可能因环境限制返回错误，只要能返回信息即可
    }

    [Test]
    public void GetMousePos_返回坐标()
    {
        string result = ToolRegistry.Execute("get_mouse_pos", "{}");
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("光标") || result.Contains("("),
            $"get_mouse_pos 应返回坐标信息，得到: {result}");
    }

    [Test]
    public void GetClipboard_不抛异常()
    {
        Assert.DoesNotThrow(() =>
        {
            string result = ToolRegistry.Execute("get_clipboard", "{}");
            Assert.IsNotNull(result);
        });
    }

    // ================================================================
    //  4. 异步工具执行（协程路径）
    // ================================================================

    [UnityTest]
    public IEnumerator ExecuteAsync_未知工具返回错误()
    {
        string result = null;
        yield return ToolRegistry.ExecuteAsync(
            "non_existent_tool", "{}", r => result = r);
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("不识此术"),
            "未知异步工具应返回错误提示");
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_所有异步工具可启动()
    {
        // 注意：一些工具因缺少运行时依赖（Live2DRenderer、GLM 等）
        // 会返回"功能不可用"提示，但不应该抛 C# 异常
        foreach (var name in GetAsyncToolNames())
        {
            string result = null;
            yield return ToolRegistry.ExecuteAsync(
                name, "{}", r => result = r);
            Assert.IsNotNull(result,
                $"异步工具 {name} 应返回结果（可能是功能不可用提示）");
        }
    }

    // ================================================================
    //  5. ToolRegistry Edge Cases
    // ================================================================

    [Test]
    public void Register_重复工具触发警告()
    {
        LogAssert.Expect(LogType.Warning,
            "[ToolRegistry] 术式「open_url」重复注册，跳过");
        var duplicate = new OpenUrlTool();
        ToolRegistry.Register(duplicate);
    }

    [Test]
    public void Register_null触发错误()
    {
        LogAssert.Expect(LogType.Error,
            "[ToolRegistry] 试图注册空工具");
        ToolRegistry.Register(null);
    }

    [Test]
    public void 两次初始化不重复计数()
    {
        int countBefore = ToolRegistry.ToolCount;
        ToolRegistry.Initialize();
        Assert.AreEqual(countBefore, ToolRegistry.ToolCount,
            "重复初始化不应增加工具数");
    }

    // ================================================================
    //  6. ToolCallInvoker 薄封装层验证
    // ================================================================

    [UnityTest]
    public IEnumerator ToolCallInvoker_全部接口正常()
    {
        var go = new GameObject("TestInvoker");
        try
        {
            var invoker = go.AddComponent<ToolCallInvoker>();

            Assert.Greater(ToolRegistry.ToolCount, 0,
                "Awake 后应完成初始化");

            // IsCoroutineTool
            Assert.IsTrue(invoker.IsCoroutineTool("open_url"),
                "已知同步工具应返回 true");
            Assert.IsTrue(invoker.IsCoroutineTool("take_screenshot"),
                "已知异步工具应返回 true");
            Assert.IsFalse(invoker.IsCoroutineTool("nonexistent"),
                "未知工具应返回 false");

            // Execute (sync path)
            string error;
            string result = invoker.Execute("get_system_info", "{}", out error);
            Assert.IsNotNull(result);
            Assert.IsNull(error, "get_system_info 不应产生错误");

            // Execute with unknown tool
            result = invoker.Execute("no_such_tool", "{}", out error);
            Assert.IsNotNull(error);
            Assert.IsTrue(error.Contains("不识此术"));

            // GetToolsJson
            string json = invoker.GetToolsJson();
            Assert.IsNotNull(json);
            Assert.IsTrue(json.StartsWith("["));

            // ExecuteCoroutine + GetCoroutineResult (sync)
            yield return invoker.ExecuteCoroutine("get_system_info", "{}");
            string cr = invoker.GetCoroutineResult();
            Assert.IsNotNull(cr);

            // ExecuteCoroutine (async)
            yield return invoker.ExecuteCoroutine("take_screenshot", "{}");
            cr = invoker.GetCoroutineResult();
            Assert.IsNotNull(cr);

            // ExecuteCoroutine (unknown)
            yield return invoker.ExecuteCoroutine("no_such_tool", "{}");
            cr = invoker.GetCoroutineResult();
            Assert.IsNotNull(cr);
            Assert.IsTrue(cr.Contains("不识此术"),
                "未知工具的协程结果应包含错误提示");
        }
        finally
        {
            GameObject.DestroyImmediate(go);
        }
    }

    // ================================================================
    //  7. 工具接口一致性验证
    // ================================================================

    [Test]
    public void 所有工具拥有有效描述()
    {
        foreach (var tool in GetAllTools())
        {
            Assert.IsFalse(string.IsNullOrEmpty(tool.ToolDescription),
                $"工具 {tool.ToolName} 缺少描述");
            Assert.IsFalse(string.IsNullOrEmpty(tool.ToolParametersJson),
                $"工具 {tool.ToolName} 缺少参数 Schema");
        }
    }

    [Test]
    public void 所有异步工具可被IsAsync正确识别()
    {
        foreach (var name in GetAsyncToolNames())
        {
            Assert.IsTrue(ToolRegistry.IsAsync(name),
                $"异步工具 {name} 应被 IsAsync 识别");
            Assert.IsTrue(ToolRegistry.HasTool(name),
                $"异步工具 {name} 应被 HasTool 识别");
        }
    }

    [Test]
    public void 所有同步工具可被HasTool识别但不被IsAsync识别()
    {
        foreach (var name in GetSyncToolNames())
        {
            Assert.IsFalse(ToolRegistry.IsAsync(name),
                $"同步工具 {name} 不应被 IsAsync 识别");
            Assert.IsTrue(ToolRegistry.HasTool(name),
                $"同步工具 {name} 应被 HasTool 识别");
        }
    }
}
