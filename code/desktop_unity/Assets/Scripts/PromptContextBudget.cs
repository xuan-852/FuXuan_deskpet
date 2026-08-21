using System;

/// <summary>
/// SystemPrompt 分层内容预算工具。
///
/// 只裁剪指定上下文段，不改动段落顺序，也不移动动态尾部；这样可以控制活动、窗口、
/// 知识库等内容的最大体积，同时保持固定 Prompt 前缀的缓存稳定性。
/// </summary>
public static class PromptContextBudget
{
    // 忆境已经在 PetMemory 内部按相关性收束，这里只留第二道总预算保险。
    public const int MemoryChars = 1400;
    // 本地模型容量更紧，只注入更小的相关记忆片段，避免挤压角色卡和最近对话。
    public const int LocalMemoryChars = 700;
    public const int PersonalityChars = 1400;
    public const int PreferenceChars = 1600;
    public const int KnowledgeChars = 5000;
    public const int ActivityChars = 1200;
    public const int VisibleWindowsChars = 1800;
    public const int BrowserTabsChars = 1800;
    public const int ParameterKnowledgeChars = 6000;
    public const int MotionMemoryChars = 3000;
    public const int ClipboardChars = 1000;
    public const int TrajectoryChars = 2500;
    public const int TemplateChars = 2500;

    /// <summary>超过上限时保留头部和尾部，中间内容以明确标记替代。</summary>
    public static string TrimSection(string value, int maxChars, string sectionName)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0 || value.Length <= maxChars)
            return value;

        string marker = $"\n…【{sectionName}已按上下文预算裁剪】…\n";
        if (marker.Length >= maxChars)
            return value.Substring(0, maxChars);

        int remaining = maxChars - marker.Length;
        int headChars = (int)Math.Ceiling(remaining * 0.7);
        int tailChars = remaining - headChars;
        return value.Substring(0, headChars) + marker
            + value.Substring(value.Length - tailChars, tailChars);
    }

    /// <summary>估算中文/英文混合文本的保守 Token 数，用于诊断，不参与云端调用。</summary>
    public static int EstimateTokens(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        return (int)Math.Ceiling(value.Length / 2.0);
    }
}
