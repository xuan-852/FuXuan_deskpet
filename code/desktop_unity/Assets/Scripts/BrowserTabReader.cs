using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 浏览器标签页深度感知 — Windows UI Automation (UIA) 实现
///
/// 通过反射调用 System.Windows.Automation (UIAutomationClient.dll / UIAutomationTypes.dll)，
/// 这两个程序集是 .NET Framework 的一部分，Windows 系统自带，无需任何安装。
///
/// 支持: Edge (Chromium), Chrome, Firefox, Opera, Brave, Vivaldi
///
/// 用法:
///   if (BrowserTabReader.IsBrowser(procName))
///       var tabs = BrowserTabReader.ReadTabs(hwnd);
/// </summary>
public static class BrowserTabReader
{
    private static bool _ready;
    private static bool _initAttempted; // true after first init attempt

    // Cached types
    private static Type _autoElementType;
    private static Type _conditionType;
    private static Type _propConditionType;

    // Cached AutomationProperty objects
    private static object _controlTypeProperty;
    private static object _nameProperty;
    private static object _isOffscreenProperty;

    // Cached ControlType values
    private static object _tabItemControlType;

    // Cached TrueCondition (Condition.TrueCondition)
    private static object _trueCondition;

    // TreeScope enum values
    private static object _treeScopeSubtree;

    // Browser process names (小写)
    private static readonly HashSet<string> BrowserProcesses = new HashSet<string>
    {
        "msedge", "chrome", "firefox", "opera", "brave", "vivaldi"
    };

    static BrowserTabReader()
    {
        Initialize();
    }

    /// <summary>UIA 是否可用（若不可用，GetBrowserTabsSummary 返回降级提示）</summary>
    public static bool IsAvailable => _ready;

    private static void Initialize()
    {
        if (_initAttempted) return;
        _initAttempted = true;
        try
        {
            var clientAsm = Assembly.Load(
                "UIAutomationClient, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            var typesAsm = Assembly.Load(
                "UIAutomationTypes, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");

            _autoElementType = clientAsm.GetType("System.Windows.Automation.AutomationElement");
            _conditionType = clientAsm.GetType("System.Windows.Automation.Condition");
            _propConditionType = clientAsm.GetType("System.Windows.Automation.PropertyCondition");

            var controlTypeType = typesAsm.GetType("System.Windows.Automation.ControlType");
            var treeScopeType = typesAsm.GetType("System.Windows.Automation.TreeScope");

            // Static properties / fields
            var flags = BindingFlags.Public | BindingFlags.Static;
            _trueCondition = _conditionType.GetField("TrueCondition", flags)?.GetValue(null);
            _controlTypeProperty = _autoElementType.GetField("ControlTypeProperty", flags)?.GetValue(null);
            _nameProperty = _autoElementType.GetField("NameProperty", flags)?.GetValue(null);
            _isOffscreenProperty = _autoElementType.GetField("IsOffscreenProperty", flags)?.GetValue(null);
            _tabItemControlType = controlTypeType.GetField("TabItem", flags)?.GetValue(null);

            _treeScopeSubtree = Enum.Parse(treeScopeType, "Subtree");

            _ready = _autoElementType != null
                     && _propConditionType != null
                     && _tabItemControlType != null
                     && _treeScopeSubtree != null;

            if (_ready)
                Debug.Log("[BrowserTabReader] ✅ UI Automation 初始化成功");
            else
                Debug.LogWarning("[BrowserTabReader] ❌ 初始化失败，部分类型未找到");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BrowserTabReader] UIA 初始化失败（系统可能不支持）: {ex.Message}");
        }
    }

    /// <summary>判断进程名是否为已知浏览器</summary>
    public static bool IsBrowser(string processName)
    {
        return !string.IsNullOrEmpty(processName)
               && BrowserProcesses.Contains(processName.ToLowerInvariant());
    }

    /// <summary>
    /// 读取指定浏览器窗口的所有标签页标题
    /// </summary>
    /// <param name="browserHwnd">浏览器窗口句柄（来自 GetForegroundWindow）</param>
    /// <returns>标签列表，每个元素格式 "📄 标签名" 或 "⏳ 后台标签名"</returns>
    public static List<string> ReadTabs(IntPtr browserHwnd)
    {
        var tabs = new List<string>();
        if (!_ready || browserHwnd == IntPtr.Zero) return tabs;

        try
        {
            // AutomationElement.ElementFromHandle(hwnd)
            object element = _autoElementType.InvokeMember(
                "ElementFromHandle",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.InvokeMethod,
                null, null, new object[] { browserHwnd });

            if (element == null) return tabs;

            // new PropertyCondition(ControlTypeProperty, TabItem)
            object tabCondition = Activator.CreateInstance(
                _propConditionType,
                new object[] { _controlTypeProperty, _tabItemControlType });

            // element.FindAll(TreeScope.Subtree, condition)
            object collection = _autoElementType.InvokeMember(
                "FindAll",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod,
                null, element,
                new object[] { _treeScopeSubtree, tabCondition });

            if (collection != null)
            {
                // AutomationElementCollection.Count
                int count = (int)_autoElementType.InvokeMember(
                    "get_Count",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty,
                    null, collection, null);

                for (int i = 0; i < count; i++)
                {
                    // collection[i]
                    object tabEl = _autoElementType.InvokeMember(
                        "get_Item",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty,
                        null, collection, new object[] { i });

                    if (tabEl == null) continue;

                    // tabEl.GetCurrentPropertyValue(NameProperty)
                    string name = _autoElementType.InvokeMember(
                        "GetCurrentPropertyValue",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod,
                        null, tabEl,
                        new object[] { _nameProperty }) as string ?? "";

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // tabEl.GetCurrentPropertyValue(IsOffscreenProperty)
                    bool isOffscreen = false;
                    try
                    {
                        isOffscreen = (bool)_autoElementType.InvokeMember(
                            "GetCurrentPropertyValue",
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod,
                            null, tabEl,
                            new object[] { _isOffscreenProperty });
                    }
                    catch { /* 某些浏览器可能不支持 IsOffscreen */ }

                    tabs.Add(isOffscreen ? $"⏳ {name.Trim()}" : $"📄 {name.Trim()}");
                }
            }

            if (tabs.Count > 0)
                Debug.Log($"[BrowserTabReader] 读取到 {tabs.Count} 个标签");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BrowserTabReader] 读取标签失败: {ex.Message}");
        }

        return tabs;
    }
}
