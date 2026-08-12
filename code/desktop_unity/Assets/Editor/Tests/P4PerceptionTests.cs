using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// P4 感知与个性化补强测试套件
/// 覆盖：PreferencesManager 持久化 CRUD / 容量淘汰 / SetPreference 工具 / ClipboardMonitor
///
/// ★ 测试数据隔离：所有偏好键使用 test_ 前缀，测试结束前 RemovePreference 清理，
///   避免污染真实 pet_preferences.json。
/// </summary>
public class P4PerceptionTests
{
    private GameObject _prefGo;
    private PreferencesManager _prefMgr;

    // ================================================================
    //  辅助 — 单例隔离（防止测试间残留）
    // ================================================================

    private static void ResetPreferencesInstance()
    {
        var field = typeof(PreferencesManager)
            .GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, null);
    }

    [SetUp]
    public void SetUp()
    {
        ResetPreferencesInstance();
        _prefGo = new GameObject("TestPreferences");
        _prefMgr = _prefGo.AddComponent<PreferencesManager>();
        // EditMode 下 AddComponent 不触发 Awake：手动执行单例注册与数据载入
        var field = typeof(PreferencesManager)
            .GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, _prefMgr);
        _prefMgr.Load();
    }

    [TearDown]
    public void TearDown()
    {
        if (_prefMgr != null)
        {
            // 清理本次测试写入的条目，恢复真实文件
            foreach (var e in _prefMgr.GetAllPreferences().ToList())
            {
                if (e.key.StartsWith("test_"))
                    _prefMgr.RemovePreference(e.key);
            }
        }
        if (_prefGo != null)
            GameObject.DestroyImmediate(_prefGo);
        ResetPreferencesInstance();
    }

    // ================================================================
    //  1. PreferencesManager 基础 CRUD
    // ================================================================

    [Test]
    public void PreferencesManager_设置后读取往返()
    {
        _prefMgr.SetPreference("test_call_me", "阿轩", "user", "主人明确告知的称呼");
        Assert.AreEqual("阿轩", _prefMgr.GetPreference("test_call_me"));
        Assert.AreEqual(1, _prefMgr.Count);
    }

    [Test]
    public void PreferencesManager_同key覆盖更新()
    {
        _prefMgr.SetPreference("test_call_me", "旧称呼");
        _prefMgr.SetPreference("test_call_me", "新称呼", "user", "主人纠正");
        Assert.AreEqual("新称呼", _prefMgr.GetPreference("test_call_me"));
        Assert.AreEqual(1, _prefMgr.Count, "覆盖不应新增条目");
    }

    [Test]
    public void PreferencesManager_删除偏好()
    {
        _prefMgr.SetPreference("test_temp", "临时值");
        Assert.IsTrue(_prefMgr.RemovePreference("test_temp"));
        Assert.IsNull(_prefMgr.GetPreference("test_temp"));
        Assert.IsFalse(_prefMgr.RemovePreference("test_temp"), "重复删除应返回 false");
    }

    [Test]
    public void PreferencesManager_空key或空value_忽略()
    {
        _prefMgr.SetPreference("", "值");
        _prefMgr.SetPreference("test_blank_value", "   ");
        Assert.AreEqual(0, _prefMgr.Count);
    }

    [Test]
    public void PreferencesManager_超过上限_淘汰最旧()
    {
        _prefMgr.maxEntries = 3;
        for (int i = 0; i < 5; i++)
            _prefMgr.SetPreference($"test_batch_{i}", $"值{i}");
        Assert.AreEqual(3, _prefMgr.Count, "超出上限应淘汰最旧条目");
        // 最新的（插入顺序末尾）应保留
        Assert.IsNotNull(_prefMgr.GetPreference("test_batch_4"));
        Assert.IsNull(_prefMgr.GetPreference("test_batch_0"), "最旧条目应被淘汰");
    }

    [Test]
    public void PreferencesManager_格式化摘要_含条目()
    {
        _prefMgr.SetPreference("test_habit", "23点睡");
        string summary = _prefMgr.FormatForPrompt();
        Assert.IsTrue(summary.Contains("主人偏好"), "摘要应含标题");
        Assert.IsTrue(summary.Contains("test_habit"), "摘要应含条目 key");
        Assert.IsTrue(summary.Contains("23点睡"), "摘要应含条目值");
    }

    [Test]
    public void PreferencesManager_无条目_摘要为空()
    {
        Assert.AreEqual("", _prefMgr.FormatForPrompt());
    }

    // ================================================================
    //  2. 偏好工具（走单例，需确保 Instance 可用）
    // ================================================================

    [Test]
    public void SetPreferenceTool_合法参数_返回成功()
    {
        Assert.IsNotNull(PreferencesManager.Instance, "SetUp 创建的实例应注册为单例");
        var tool = new SetPreferenceTool();
        string result = tool.Execute(
            "{\"key\":\"test_tool_key\",\"value\":\"工具值\",\"source\":\"infer\"}");
        Assert.IsTrue(result.Contains("✅"), $"应返回成功: {result}");
        Assert.AreEqual("工具值", _prefMgr.GetPreference("test_tool_key"));
    }

    [Test]
    public void SetPreferenceTool_缺参数_返回错误()
    {
        var tool = new SetPreferenceTool();
        string result = tool.Execute("{\"key\":\"test_tool_key\"}");
        Assert.IsTrue(result.Contains("❌"), "缺 value 应返回错误");

        string bad = tool.Execute("not json");
        Assert.IsTrue(bad.Contains("❌"), "非法 JSON 应返回错误");
    }

    [Test]
    public void QueryPreferencesTool_有数据_返回清单()
    {
        _prefMgr.SetPreference("test_query_key", "查询值");
        var tool = new QueryPreferencesTool();
        string result = tool.Execute("{}");
        Assert.IsTrue(result.Contains("test_query_key"));
        Assert.IsTrue(result.Contains("查询值"));
    }

    // ================================================================
    //  3. ClipboardMonitor 感知
    // ================================================================

    [Test]
    public void ClipboardMonitor_无复制记录_摘要为空()
    {
        Assert.AreEqual("", ClipboardMonitor.GetRecentClipboardSummary());
    }
}
