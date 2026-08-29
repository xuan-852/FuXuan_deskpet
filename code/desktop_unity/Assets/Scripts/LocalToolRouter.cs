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
    /// <summary>课程表 Web 看板地址。看板由 D:\C\小程序\server 提供。</summary>
    public const string ScheduleDashboardUrl = "http://localhost:3000";
    /// <summary>转发给文档/外包工具的用户需求上限，避免本地模型或桥接请求无限膨胀。</summary>
    public const int MaxForwardedTaskChars = 60000;

    private static readonly HashSet<string> SafeLatexCompilers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "xelatex", "pdflatex", "lualatex"
    };

    private static readonly string[] CommandTools =
    {
        "launch_pogget", "pogget_agent", "open_app", "open_url", "open_folder",
        "search", "search_web", "openclaw_search", "openclaw_task",
        "lock_screen", "set_volume", "mute", "power",
        "get_system_info", "get_mouse_pos", "list_files", "notify",
        "get_clipboard", "set_clipboard", "take_screenshot",
        "file_open", "file_move", "file_copy", "file_delete", "file_rename",
        "file_info", "file_create", "dir_create", "file_read", "search_files",
        "search_file", "run_command"
    };

    private static readonly string[] KnowledgeTools =
    {
        "search", "search_web", "openclaw_search", "openclaw_task", "open_url",
        "knowledge_search", "knowledge_index", "get_weather",
        "query_reminders", "query_exams", "query_scores", "query_schedule", "query_user_status",
        "query_preferences", "query_task_templates",
        "inspect_motion_memory", "inspect_personality", "explore_body", "explore_body_vision",
        "generate_ppt", "generate_docx", "generate_xlsx", "compile_latex",
        "get_system_info", "get_mouse_pos", "get_clipboard", "list_files", "file_info",
        "file_read", "search_files", "search_file"
    };

    private static readonly string[] OperationTools =
    {
        "set_expression", "play_action", "stop_action", "generate_motion", "take_screenshot",
        "control_body", "inspect_motion_memory", "inspect_personality",
        "explore_body", "explore_body_vision", "run_verification", "vis_verify", "self_review",
        "knowledge_index", "get_system_info", "get_mouse_pos", "query_reminders",
        "set_reminder", "mark_reminder_done", "delete_reminder",
        "set_preference", "query_preferences", "remove_preference",
        "query_task_templates", "save_task_template", "remove_task_template",
        "generate_ppt", "generate_docx", "generate_xlsx", "compile_latex"
    };

    private static readonly string[] FallbackTools =
    {
        "compile_latex", "control_body", "delete_reminder", "dir_create", "explore_body",
        "explore_body_vision", "file_copy", "file_create", "file_delete", "file_info",
        "file_move", "file_open", "file_read", "file_rename", "generate_docx",
        "generate_motion", "generate_ppt", "generate_xlsx", "get_clipboard", "get_mouse_pos",
        "get_system_info", "get_weather", "inspect_motion_memory", "inspect_personality",
        "knowledge_index", "knowledge_search", "launch_pogget", "list_files", "mark_reminder_done",
        "notify", "open_app", "open_folder", "open_url", "openclaw_search", "openclaw_task",
        "play_action", "pogget_agent", "query_exams", "query_preferences", "query_reminders",
        "query_schedule", "query_scores", "query_task_templates", "query_user_status",
        "remove_preference", "remove_task_template", "run_command", "run_verification",
        "save_task_template", "search", "search_file", "search_files", "search_web",
        "self_review", "set_clipboard", "set_expression", "set_preference", "set_reminder",
        "stop_action", "take_screenshot", "vis_verify", "generate_ppt", "generate_docx",
        "generate_xlsx", "file_info", "file_read", "file_copy", "file_move", "file_rename",
        "file_create", "dir_create", "set_volume", "mute", "lock_screen", "power"
    };

    private static readonly string[] ActionKeywords =
    {
        "打开", "启动", "运行", "执行", "搜索", "查找", "查询", "读取", "查看",
        "创建", "生成", "设置提醒", "提醒我", "播放", "停止", "截图", "天气", "文件",
        "网页", "网址", "剪贴板", "复制", "整理", "锁屏", "关机", "重启", "命令",
        "脚本", "联网", "PPT", "Word", "Excel", "PDF", "LaTeX", "文档", "论文", "报告", "简历",
        "课表", "课程", "上课", "学业",
        "偏好", "习惯", "喜欢", "模板", "人格", "动作记忆", "身体", "姿势", "手势",
        "验证动作", "视觉验证", "复盘", "自检", "索引", "知识库", "容器", "Pogget"
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

    /// <summary>
    /// 判断是否需要质量优先的 8B 工具规划器。
    /// 简单查询继续使用 3B；文档、办公和 OpenClaw 多步骤任务更看重参数完整性。
    /// </summary>
    public static bool ShouldUseQualityPlanner(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return false;
        return ContainsAny(userMessage,
            "PDF", "LaTeX", "论文", "报告", "简历", "文档", "章节", "参考文献", "排版",
            "PPT", "Word", "Excel", "OpenClaw", "多步骤", "多步", "浏览器", "登录",
            "填表", "持续监测", "调研", "研究", "外包", "网页操作", "任务流程");
    }

    /// <summary>
    /// 识别“打开课表”这类明确的网页导航请求。
    /// 这是高置信度路由：不让轻量模型在 open_url/query_schedule 之间猜测，
    /// 但仍然只返回计划，最终执行继续经过 ChatManager 的白名单和工具校验。
    /// </summary>
    public static bool TryBuildScheduleOpenPlan(string userMessage, out LocalToolPlan plan)
    {
        plan = default(LocalToolPlan);
        if (string.IsNullOrWhiteSpace(userMessage)) return false;

        string message = userMessage.Trim();
        bool mentionsSchedule = ContainsAny(message, "课表", "课程表", "课程安排", "上课", "课程");
        bool requestsOpen = ContainsAny(message, "打开", "进入", "访问", "跳转", "浏览", "网页");
        if (!mentionsSchedule || !requestsOpen) return false;

        return AssignPlan(
            out plan,
            "open_url",
            JsonConvert.SerializeObject(new { url = ScheduleDashboardUrl }),
            "用户明确要求打开课表网页");
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
    /// 对本地模型提出的任务参数做确定性加固。
    /// 工具名仍由模型规划，但文档/外包任务正文优先使用用户原话，避免 3B 模型
    /// 在摘要时漏掉章节、页数、格式等要求；最终执行仍由 ChatManager 负责白名单和审批。
    /// </summary>
    public static bool TryHardenPlanArguments(string toolName, string userMessage, string argumentsJson,
        out string hardenedArgumentsJson, out string error)
    {
        hardenedArgumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        error = "";

        string normalizedTool = (toolName ?? "").Trim();
        bool needsOriginalRequest = normalizedTool == "compile_latex"
            || normalizedTool == "generate_ppt"
            || normalizedTool == "generate_docx"
            || normalizedTool == "generate_xlsx"
            || normalizedTool == "openclaw_task";
        if (!needsOriginalRequest) return true;

        string original = (userMessage ?? "").Trim();
        if (string.IsNullOrEmpty(original))
        {
            error = "原始用户需求为空，已阻止向外部生成器发送空任务";
            return false;
        }
        if (original.Length > MaxForwardedTaskChars)
        {
            error = $"原始用户需求过长（{original.Length} 字符），请分章节或分段提交";
            return false;
        }

        JObject args;
        try
        {
            args = JObject.Parse(hardenedArgumentsJson);
        }
        catch (System.Exception ex)
        {
            error = "工具参数不是合法 JSON 对象: " + ex.Message;
            return false;
        }

        if (normalizedTool == "compile_latex"
            || normalizedTool == "generate_ppt"
            || normalizedTool == "generate_docx"
            || normalizedTool == "generate_xlsx")
        {
            args["description"] = original;
        }
        else if (normalizedTool == "openclaw_task" && string.IsNullOrWhiteSpace(args["template"]?.ToString()))
        {
            args["task"] = original;
        }

        if (normalizedTool == "compile_latex")
        {
            string compiler = args["compiler"]?.ToString()?.Trim().ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(compiler) && !SafeLatexCompilers.Contains(compiler))
            {
                error = "PDF 编译器只允许 xelatex、pdflatex 或 lualatex";
                return false;
            }
            if (string.IsNullOrEmpty(compiler)) args["compiler"] = "xelatex";
        }

        hardenedArgumentsJson = args.ToString(Formatting.None);
        return true;
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
            && ContainsAny(message, "查", "看", "问", "今天", "明天", "本周", "下周", "第"))
        {
            int week = ExtractWeek(message);
            string args = week > 0 ? JsonConvert.SerializeObject(new { week }) : "{}";
            return AssignPlan(out plan, "query_schedule", args, "用户明确查询课表");
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

        if (ContainsAny(message, "生成", "创建", "写", "制作", "导出", "做一个")
            && ContainsAny(message, "PDF", "LaTeX", "论文", "报告", "简历", "文档"))
        {
            return AssignPlan(out plan, "compile_latex",
                JsonConvert.SerializeObject(new { description = message, compiler = "xelatex" }),
                "用户明确生成 PDF/LaTeX 文档");
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
