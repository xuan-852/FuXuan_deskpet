using System;
using System.IO;
using System.Text;

/// <summary>
/// 符玄桌宠的开发者指令集。
///
/// 这些指令只在桌宠本地消息入口处理，不进入 LLM、聊天历史、忆境或人格记录。
/// 新增开发指令时集中修改这里，避免把开发开关散落在 UI 和 ChatManager 中。
/// </summary>
public static class DeveloperCommandSet
{
    public enum CommandType
    {
        None,
        SetTestMode,
        SetNormalMode,
        TellMode,
        InvalidMode,
        InvalidTell
    }

    public struct ParsedCommand
    {
        public bool IsDeveloperCommand;
        public CommandType Type;
    }

    /// <summary>
    /// 解析开发者指令。返回 false 表示普通聊天文本，应继续交给模型处理。
    /// </summary>
    public static bool TryParse(string raw, out ParsedCommand parsed)
    {
        parsed = new ParsedCommand { IsDeveloperCommand = false, Type = CommandType.None };
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string[] parts = raw.Trim().Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        string root = parts[0].ToLowerInvariant();
        if (root == "/mode")
        {
            parsed.IsDeveloperCommand = true;
            if (parts.Length == 3
                && string.Equals(parts[1], "set", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[2], "test", StringComparison.OrdinalIgnoreCase))
            {
                parsed.Type = CommandType.SetTestMode;
            }
            else if (parts.Length == 3
                && string.Equals(parts[1], "set", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[2], "normality", StringComparison.OrdinalIgnoreCase))
            {
                parsed.Type = CommandType.SetNormalMode;
            }
            else
            {
                parsed.Type = CommandType.InvalidMode;
            }
            return true;
        }

        if (root == "/tell")
        {
            parsed.IsDeveloperCommand = true;
            parsed.Type = parts.Length == 2
                && string.Equals(parts[1], "mode", StringComparison.OrdinalIgnoreCase)
                ? CommandType.TellMode
                : CommandType.InvalidTell;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 执行开发者指令并返回本地回执。成功处理（包括格式错误）都返回 true，
    /// 调用方不得再把原文发送给模型。
    /// </summary>
    public static bool TryHandle(string raw, out string reply)
    {
        reply = null;
        ParsedCommand parsed;
        if (!TryParse(raw, out parsed) || !parsed.IsDeveloperCommand)
            return false;

        switch (parsed.Type)
        {
            case CommandType.SetTestMode:
                return SetMode(true, out reply);
            case CommandType.SetNormalMode:
                return SetMode(false, out reply);
            case CommandType.TellMode:
                reply = "当前模式：" + (IsTestMode() ? "测试模式" : "正常模式");
                return true;
            case CommandType.InvalidMode:
                reply = "开发指令格式：/mode set test 或 /mode set normality";
                return true;
            case CommandType.InvalidTell:
                reply = "开发指令格式：/tell mode";
                return true;
            default:
                return false;
        }
    }

    public static bool IsTestMode()
    {
        return File.Exists(DataPathConfig.TestModeFile);
    }

    private static bool SetMode(bool testMode, out string reply)
    {
        reply = null;
        string marker = DataPathConfig.TestModeFile;
        try
        {
            if (testMode)
            {
                string error;
                if (!DataPathConfig.EnsureDataRoot(out error))
                {
                    reply = "切换测试模式失败：无法准备数据目录。";
                    return true;
                }

                // 无 BOM 空文件，和既有自动化测试约定保持一致。
                File.WriteAllText(marker, string.Empty, new UTF8Encoding(false));
                reply = "已切换至测试模式：持久化写入与云端请求按测试保护策略处理。";
            }
            else
            {
                if (File.Exists(marker)) File.Delete(marker);
                reply = "已切换至正常模式：后续对话恢复正常运行策略。";
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[DeveloperCommandSet] 模式切换失败: " + ex.Message);
            reply = "模式切换失败：无法更新测试模式标记。";
        }
        return true;
    }
}
