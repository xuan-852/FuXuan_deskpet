using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 本地工具路由策略。
///
/// 本地 Ollama 不使用模型原生 Function Calling，而是让轻量模型输出严格 JSON
/// 工具计划，再由 ChatManager 做白名单、参数和危险操作确认，最后复用 ToolEngine。
/// </summary>
public struct LocalToolPlan
{
    public bool Success;
    public bool ShouldExecute;
    public string ToolName;
    public string ArgumentsJson;
    public string Reason;
    public string Error;
}

public static class LocalToolRouter
{
    private static readonly string[] CommandTools =
    {
        "open_app", "open_url", "open_folder", "search", "search_web", "openclaw_search",
        "get_system_info", "get_mouse_pos", "get_clipboard", "set_clipboard", "notify",
        "file_read", "search_files", "search_file", "run_command", "openclaw_task"
    };

    private static readonly string[] KnowledgeTools =
    {
        "search", "search_web", "openclaw_search", "knowledge_search", "get_weather",
        "query_reminders", "query_exams", "query_scores", "query_schedule", "query_user_status",
        "generate_ppt", "generate_docx", "generate_xlsx", "compile_latex", "openclaw_task",
        "file_read", "search_files", "search_file"
    };

    private static readonly string[] OperationTools =
    {
        "set_expression", "play_action", "stop_action", "generate_motion", "take_screenshot",
        "get_system_info", "get_mouse_pos", "query_reminders", "set_reminder", "mark_reminder_done",
        "delete_reminder"
    };

    private static readonly string[] FallbackTools =
    {
        "open_app", "open_url", "open_folder", "search", "search_web", "get_system_info",
        "get_clipboard", "notify", "file_read", "search_files", "search_file", "get_weather",
        "set_expression", "play_action", "stop_action", "generate_motion", "take_screenshot",
        "query_reminders", "set_reminder", "generate_ppt", "generate_docx", "generate_xlsx",
        "run_command", "openclaw_task"
    };

    private static readonly string[] ActionKeywords =
    {
        "打开", "启动", "运行", "执行", "搜索", "查找", "查询", "读取", "查看",
        "创建", "生成", "设置提醒", "提醒我", "播放", "停止", "截图", "天气", "文件",
        "网页", "网址", "剪贴板", "复制", "整理", "锁屏", "关机", "重启", "命令",
        "脚本", "联网", "PPT", "Word", "Excel", "LaTeX"
    };

    public static string[] GetAllowedTools(string intent)
    {
        switch ((intent ?? "").Trim().ToLowerInvariant())
        {
            case "command": return CommandTools;
            case "knowledge": return KnowledgeTools;
            case "operation": return OperationTools;
            default: return FallbackTools;
        }
    }

    /// <summary>判断这条本地消息是否值得额外调用一次工具规划模型。</summary>
    public static bool ShouldAttempt(string intent, string userMessage)
    {
        string normalizedIntent = (intent ?? "").Trim().ToLowerInvariant();
        if (normalizedIntent == "command" || normalizedIntent == "knowledge" || normalizedIntent == "operation")
            return true;

        if (normalizedIntent == "chat" || normalizedIntent == "emotion")
            return ContainsActionKeyword(userMessage);

        return ContainsActionKeyword(userMessage);
    }

    public static bool ContainsActionKeyword(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return false;
        foreach (string keyword in ActionKeywords)
        {
            if (userMessage.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>本地模型只能调用当前意图白名单中的工具。</summary>
    public static bool IsAllowed(string toolName, string intent)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        foreach (string allowed in GetAllowedTools(intent))
        {
            if (string.Equals(allowed, toolName.Trim(), StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// 将 ToolEngine 的 OpenAI schema 压缩成给本地规划器看的目录。
    /// 保留工具名、描述和参数，不把无关的 Function Calling 外壳重复发送。
    /// </summary>
    public static string BuildCompactCatalog(string toolsJson)
    {
        if (string.IsNullOrWhiteSpace(toolsJson)) return "[]";

        try
        {
            var source = JArray.Parse(toolsJson);
            var compact = new JArray();
            foreach (JToken item in source)
            {
                JObject function = item["function"] as JObject;
                if (function == null) continue;

                string description = function["description"]?.ToString() ?? "";
                if (description.Length > 180)
                    description = description.Substring(0, 180) + "…";

                var entry = new JObject
                {
                    ["tool"] = function["name"]?.ToString() ?? "",
                    ["description"] = description,
                    ["arguments"] = function["parameters"] ?? new JObject()
                };
                compact.Add(entry);
            }
            return compact.ToString(Formatting.None);
        }
        catch
        {
            return "[]";
        }
    }

    /// <summary>从本地模型的纯文本响应中提取并验证工具计划 JSON。</summary>
    public static LocalToolPlan ParsePlan(string content)
    {
        var result = new LocalToolPlan
        {
            Success = false,
            ShouldExecute = false,
            ToolName = "",
            ArgumentsJson = "{}",
            Reason = "",
            Error = "本地工具规划结果为空"
        };

        if (string.IsNullOrWhiteSpace(content)) return result;

        try
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                result.Error = "本地工具规划不是 JSON";
                return result;
            }

            JObject obj = JObject.Parse(content.Substring(start, end - start + 1));
            string action = obj["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "none";
            result.ToolName = obj["tool"]?.ToString()?.Trim() ?? "";
            result.Reason = obj["reason"]?.ToString()?.Trim() ?? "";

            if (action == "none" || string.IsNullOrEmpty(result.ToolName))
            {
                result.Success = true;
                result.ShouldExecute = false;
                result.Error = "";
                return result;
            }

            if (action != "call")
            {
                result.Error = "action 必须是 call 或 none";
                return result;
            }

            JToken arguments = obj["arguments"];
            if (arguments != null && arguments.Type == JTokenType.Object)
                result.ArgumentsJson = arguments.ToString(Formatting.None);

            result.Success = true;
            result.ShouldExecute = true;
            result.Error = "";
            return result;
        }
        catch (Exception ex)
        {
            result.Error = "本地工具规划 JSON 解析失败: " + ex.Message;
            return result;
        }
    }
}
