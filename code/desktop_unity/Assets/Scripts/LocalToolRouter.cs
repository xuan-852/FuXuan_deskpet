using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        "delete_reminder", "generate_ppt", "generate_docx", "generate_xlsx", "compile_latex"
    };

    private static readonly string[] FallbackTools =
    {
        "open_app", "open_url", "open_folder", "search", "search_web", "get_system_info",
        "get_clipboard", "notify", "file_read", "search_files", "search_file", "get_weather",
        "set_expression", "play_action", "stop_action", "generate_motion", "take_screenshot",
        "query_reminders", "query_exams", "query_scores", "query_schedule", "query_user_status",
        "set_reminder", "generate_ppt", "generate_docx", "generate_xlsx",
        "run_command", "openclaw_task"
    };

    private static readonly string[] ActionKeywords =
    {
        "打开", "启动", "运行", "执行", "搜索", "查找", "查询", "读取", "查看",
        "创建", "生成", "设置提醒", "提醒我", "播放", "停止", "截图", "天气", "文件",
        "网页", "网址", "剪贴板", "复制", "整理", "锁屏", "关机", "重启", "命令",
        "脚本", "联网", "PPT", "Word", "Excel", "LaTeX", "课表", "课程", "上课", "学业"
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
            // qwen2.5 偶尔会在 arguments 内生成坏 JSON。对无参数工具，若仍能
            // 提取出明确的 call/tool，则可以安全地补成 {}，避免整条请求退回闲聊。
            string looseAction = Regex.Match(content, "\"action\"\\s*:\\s*\"(call|none)\"", RegexOptions.IgnoreCase).Groups[1].Value;
            string looseTool = Regex.Match(content, "\"tool\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.Equals(looseAction, "call", StringComparison.OrdinalIgnoreCase)
                && IsNoArgumentTool(looseTool))
            {
                result.Success = true;
                result.ShouldExecute = true;
                result.ToolName = looseTool.Trim();
                result.ArgumentsJson = "{}";
                result.Error = "";
                return result;
            }
            result.Error = "本地工具规划 JSON 解析失败: " + ex.Message;
            return result;
        }
    }

    /// <summary>
    /// 规划模型失败时，对明确的日常短指令做受限兜底。这里不执行工具，
    /// 只生成同样的 LocalToolPlan，后续仍由 ChatManager 做白名单、参数和危险确认。
    /// </summary>
    public static bool TryBuildKeywordPlan(string intent, string userMessage, out LocalToolPlan plan)
    {
        plan = new LocalToolPlan
        {
            Success = false,
            ShouldExecute = false,
            ToolName = "",
            ArgumentsJson = "{}",
            Reason = "",
            Error = "未匹配到高置信度本地术式"
        };

        if (string.IsNullOrWhiteSpace(userMessage)) return false;
        string message = userMessage.Trim();

        if (ContainsAny(message, "系统信息", "CPU", "内存状态", "电脑状态", "硬件信息"))
            return AssignPlan(out plan, "get_system_info", "{}", "用户明确查询系统状态");

        if (ContainsAny(message, "天气", "温度", "下雨", "气温"))
            return AssignPlan(out plan, "get_weather", "{}", "用户明确查询天气");

        if (ContainsAny(message, "课表", "课程表", "课程安排", "上课", "课程")
            && ContainsAny(message, "查", "看", "问", "打开", "今天", "明天", "本周", "下周", "第"))
        {
            int week = ExtractWeek(message);
            string args = week > 0 ? JsonConvert.SerializeObject(new { week }) : "{}";
            return AssignPlan(out plan, "query_schedule", args, "用户明确查询或打开课表");
        }

        bool mentionsClipboard = message.IndexOf("剪贴板", StringComparison.OrdinalIgnoreCase) >= 0;
        if (mentionsClipboard && ContainsAny(message, "查看", "读取", "内容", "复制了什么"))
            return AssignPlan(out plan, "get_clipboard", "{}", "用户明确读取剪贴板");

        if (mentionsClipboard && ContainsAny(message, "复制", "写入", "放到", "保存"))
        {
            string text = ExtractQuotedText(message);
            if (!string.IsNullOrWhiteSpace(text))
                return AssignPlan(out plan, "set_clipboard", JsonConvert.SerializeObject(new { text }), "用户明确写入剪贴板");
        }

        if (ContainsAny(message, "搜索", "查找", "找一下", "找找")
            && ContainsAny(message, "文件", "README", ".md", ".cs", ".json", "项目"))
        {
            string query = ExtractFileQuery(message);
            if (!string.IsNullOrWhiteSpace(query))
            {
                return AssignPlan(out plan, "search_files",
                    JsonConvert.SerializeObject(new { query, root = "" }), "用户明确搜索文件");
            }
        }

        if (ContainsAny(message, "打开", "开启")
            && ContainsAny(message, "文件夹", "目录", "桌面", "下载", "文档"))
        {
            string folder = "";
            if (message.Contains("下载")) folder = "Downloads";
            else if (message.Contains("文档")) folder = "Documents";
            else if (message.Contains("桌面")) folder = "Desktop";
            return AssignPlan(out plan, "open_folder",
                JsonConvert.SerializeObject(new { path = folder }), "用户明确打开文件夹");
        }

        if (ContainsAny(message, "生成", "创建", "做一个") && ContainsAny(message, "Excel", "xlsx", "表格"))
        {
            return AssignPlan(out plan, "generate_xlsx",
                JsonConvert.SerializeObject(new { description = message }), "用户明确生成 Excel");
        }

        if (ContainsAny(message, "播放", "做一个") && ContainsAny(message, "动作", "挥手", "点头", "微笑"))
        {
            return AssignPlan(out plan, "play_action",
                JsonConvert.SerializeObject(new { action = message }), "用户明确播放动作");
        }

        return false;
    }

    private static LocalToolPlan MakePlan(string toolName, string argumentsJson, string reason)
    {
        return new LocalToolPlan
        {
            Success = true,
            ShouldExecute = true,
            ToolName = toolName,
            ArgumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson,
            Reason = reason,
            Error = ""
        };
    }

    private static bool AssignPlan(out LocalToolPlan plan, string toolName, string argumentsJson, string reason)
    {
        plan = MakePlan(toolName, argumentsJson, reason);
        return true;
    }

    private static bool ContainsAny(string message, params string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (!string.IsNullOrEmpty(keyword)
                && message.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static string ExtractQuotedText(string message)
    {
        Match match = Regex.Match(message, "「([^」]+)」|“([^”]+)”|\"([^\"]+)\"|'([^']+)'|《([^》]+)》");
        if (!match.Success) return "";
        for (int i = 1; i < match.Groups.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(match.Groups[i].Value))
                return match.Groups[i].Value.Trim();
        }
        return "";
    }

    private static string ExtractFileQuery(string message)
    {
        Match fileName = Regex.Match(message, @"[A-Za-z0-9_\-]+\.(md|txt|cs|json|yaml|yml|js|ps1|xlsx|docx|pptx)", RegexOptions.IgnoreCase);
        if (fileName.Success) return fileName.Value;

        string quoted = ExtractQuotedText(message);
        if (!string.IsNullOrWhiteSpace(quoted)) return quoted;

        Match tail = Regex.Match(message, @"(?:搜索|查找|找一下|找找)(.+)$");
        if (!tail.Success) return "";
        string query = tail.Groups[1].Value.Trim();
        query = Regex.Replace(query, @"^(一下|一个|项目里的|项目中的|电脑里的|文件夹里的)", "").Trim();
        query = query.Trim('。', '？', '?', '！', '!');
        return query;
    }

    private static int ExtractWeek(string message)
    {
        Match match = Regex.Match(message ?? "", @"第\s*(\d+)\s*周");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int week))
            return Math.Max(0, week);
        return 0;
    }

    private static bool IsNoArgumentTool(string toolName)
    {
        switch ((toolName ?? "").Trim())
        {
            case "get_system_info":
            case "get_clipboard":
            case "get_mouse_pos":
            case "get_weather":
            case "query_reminders":
            case "query_exams":
            case "query_scores":
            case "query_schedule":
            case "query_user_status":
            case "stop_action":
            case "take_screenshot":
                return true;
            default:
                return false;
        }
    }
}
