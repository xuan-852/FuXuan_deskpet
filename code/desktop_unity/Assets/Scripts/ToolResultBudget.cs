using System;

/// <summary>
/// 工具结果回填预算。
///
/// 工具执行结果本身仍交给 UI 和记忆逻辑使用；只有写入 ChatManager 历史、再次发送给
/// 云端模型的副本会按工具类型压缩，避免大文件、网页和任务轨迹在每轮重复占用输入 Token。
/// </summary>
public static class ToolResultBudget
{
    private const int DefaultChars = 2800;

    /// <summary>按工具类型返回回填模型的最大字符数。</summary>
    public static int GetMaxChars(string toolName)
    {
        switch ((toolName ?? "").Trim().ToLowerInvariant())
        {
            case "file_read":
            case "file_info":
            case "get_clipboard":
                return 5000;
            case "search":
            case "search_web":
            case "openclaw_search":
                return 4200;
            case "openclaw_task":
                return 4500;
            case "take_screenshot":
            case "explore_body_vision":
            case "self_review":
                return 3500;
            case "list_files":
            case "search_files":
            case "search_file":
                return 3000;
            default:
                return DefaultChars;
        }
    }

    /// <summary>压缩工具结果；短结果保持原引用，长结果保留头尾。</summary>
    public static string Compact(string toolName, string result)
    {
        return PromptContextBudget.TrimSection(
            result, GetMaxChars(toolName), "工具结果·" + (string.IsNullOrEmpty(toolName) ? "unknown" : toolName));
    }
}
