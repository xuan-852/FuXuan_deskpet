using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 法阵法器 — 工具注册中心
/// 收集所有 IPetTool 实现，提供注册、查询、调度、JSON Schema 生成功能。
/// 例用法：ToolRegistry.Initialize(); var json = ToolRegistry.GetToolsJson();
/// </summary>
public static class ToolRegistry
{
    private static Dictionary<string, IPetTool> _syncTools;
    private static Dictionary<string, IPetTool> _asyncTools;
    private static bool _initialized = false;

    /// <summary>已注册的全部工具数量</summary>
    public static int ToolCount => (_syncTools?.Count ?? 0) + (_asyncTools?.Count ?? 0);

    // ⚠️ 危险工具清单 — 执行前必须经用户确认（ToolConfirmManager）
    // 这些工具会删除数据 / 控制系统 / 关机锁屏，AI 幻觉或 prompt 注入时不能静默执行
    private static readonly HashSet<string> DangerousTools = new HashSet<string>
    {
        "file_delete",     // 删除文件（permanent=true 永久删除）
        "power",           // 关机 / 重启 / 睡眠
        "lock_screen",     // 锁定屏幕
        "run_command",     // 执行系统命令
        "set_volume",      // 修改系统音量
        "mute",            // 静音切换
        "openclaw_task",   // 外包给 OpenClaw 的任务可能含浏览器操作/命令执行等
    };

    /// <summary>危险工具是否需要用户确认</summary>
    public static bool IsDangerous(string name) => DangerousTools.Contains(name);

    /// <summary>危险操作的中文说明（用于确认气泡展示）</summary>
    public static string GetDangerDescription(string name)
    {
        switch (name)
        {
            case "file_delete": return "删除文件/文件夹（permanent=true 时不可恢复）";
            case "power":       return "关机 / 重启 / 睡眠";
            case "lock_screen": return "锁定电脑屏幕";
            case "run_command": return "在系统上执行命令";
            case "set_volume":  return "修改系统音量";
            case "mute":        return "切换静音状态";
            case "openclaw_task": return "外包给 OpenClaw 智能体执行任务（可能涉及浏览器操作、命令执行）";
            default:            return name;
        }
    }

    /// <summary>自动发现并注册所有 IPetTool</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _syncTools = new Dictionary<string, IPetTool>();
        _asyncTools = new Dictionary<string, IPetTool>();

        // 通过反射发现所有 IPetTool 的非抽象实现类
        var toolTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => typeof(IPetTool).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && t != typeof(IPetTool));

        foreach (var type in toolTypes)
        {
            try
            {
                var instance = (IPetTool)Activator.CreateInstance(type);
                Register(instance);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolRegistry] 无法实例化 {type.Name}: {ex.Message}");
            }
        }

        _initialized = true;
        Debug.Log($"[ToolRegistry] 法阵已载入 {ToolCount} 道术式（同步 {_syncTools.Count} + 异步 {_asyncTools.Count}）");
    }

    /// <summary>手动注册一个工具</summary>
    public static void Register(IPetTool tool)
    {
        if (tool == null || string.IsNullOrEmpty(tool.ToolName))
        {
            Debug.LogError("[ToolRegistry] 试图注册空工具");
            return;
        }

        var dict = tool.IsAsync ? _asyncTools : _syncTools;
        if (dict.ContainsKey(tool.ToolName))
        {
            Debug.LogWarning($"[ToolRegistry] 术式「{tool.ToolName}」重复注册，跳过");
            return;
        }
        dict[tool.ToolName] = tool;
    }

    /// <summary>判断工具是否需要协程执行</summary>
    public static bool IsAsync(string name) => _asyncTools?.ContainsKey(name) ?? false;

    /// <summary>判断工具是否已注册</summary>
    public static bool HasTool(string name) =>
        (_syncTools?.ContainsKey(name) ?? false) || (_asyncTools?.ContainsKey(name) ?? false);

    /// <summary>同步执行工具</summary>
    public static string Execute(string name, string argsJson)
    {
        if (_syncTools == null || !_syncTools.TryGetValue(name, out var tool))
        {
            return $"❌ 不识此术：「{name}」";
        }
        try { return tool.Execute(argsJson ?? "{}"); }
        catch (Exception e) { return $"❌ 施法失败：{e.Message}"; }
    }

    /// <summary>异步执行工具（协程），结果通过 onResult 回调返回</summary>
    public static IEnumerator ExecuteAsync(string name, string argsJson, Action<string> onResult)
    {
        if (_asyncTools == null || !_asyncTools.TryGetValue(name, out var tool))
        {
            onResult?.Invoke($"❌ 不识此术：「{name}」");
            yield break;
        }
        yield return tool.ExecuteAsync(argsJson ?? "{}", result => onResult?.Invoke(result));
    }

    /// <summary>
    /// 生成所有工具的 OpenAI Function Calling JSON Schema
    /// </summary>
    public static string GetToolsJson()
    {
        if (!_initialized) Initialize();

        var allTools = CollectAllTools();
        return BuildToolsJson(allTools);
    }

    /// <summary>
    /// 仅生成指定工具列表的 JSON Schema（按意图过滤）
    /// </summary>
    public static string GetToolsJson(string[] allowedToolNames)
    {
        if (!_initialized) Initialize();

        var allowedSet = new HashSet<string>(allowedToolNames);
        var allTools = CollectAllTools();
        var filtered = allTools.Where(t => allowedSet.Contains(t.ToolName)).ToList();
        return BuildToolsJson(filtered);
    }

    /// <summary>收集全部已注册工具</summary>
    private static List<IPetTool> CollectAllTools()
    {
        var allTools = new List<IPetTool>();
        if (_syncTools != null) allTools.AddRange(_syncTools.Values);
        if (_asyncTools != null) allTools.AddRange(_asyncTools.Values);
        return allTools;
    }

    /// <summary>将工具列表构建为 JSON Schema 字符串</summary>
    private static string BuildToolsJson(List<IPetTool> tools)
    {
        var sb = new StringBuilder();
        sb.Append("[\n");

        // 按名称排序，保证每次生成顺序一致
        tools.Sort((a, b) => string.Compare(a.ToolName, b.ToolName, StringComparison.Ordinal));

        for (int i = 0; i < tools.Count; i++)
        {
            var t = tools[i];
            sb.Append("  {\n");
            sb.Append("    \"type\": \"function\",\n");
            sb.Append("    \"function\": {\n");
            sb.Append($"      \"name\": \"{EscapeJson(t.ToolName)}\",\n");
            sb.Append($"      \"description\": \"{EscapeJson(t.ToolDescription)}\",\n");
            sb.Append($"      \"parameters\": {t.ToolParametersJson}\n");
            sb.Append("    }\n");
            sb.Append("  }");
            if (i < tools.Count - 1) sb.Append(",");
            sb.Append("\n");
        }

        sb.Append("]");
        return sb.ToString();
    }

    /// <summary>JSON 字符串转义</summary>
    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
