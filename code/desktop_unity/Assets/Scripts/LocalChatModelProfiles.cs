using System;

/// <summary>
/// 本地聊天模型适配策略。
///
/// 这里刻意只描述“聊天”参数；动作、分类、摘要仍由 qwen2.5:3b 的实时链路独立控制。
/// 同一份策略同时供真实聊天、模型设置页样例和 UI 说明使用，避免三处参数漂移。
/// </summary>
public sealed class LocalChatModelProfile
{
    public readonly string Model;
    public readonly string Title;
    public readonly string Summary;
    public readonly float Temperature;
    public readonly int MaxTokens;
    public readonly int TimeoutSeconds;
    public readonly int HistoryChars;
    public readonly string PromptAdjustment;

    public LocalChatModelProfile(
        string model,
        string title,
        string summary,
        float temperature,
        int maxTokens,
        int timeoutSeconds,
        int historyChars,
        string promptAdjustment)
    {
        Model = model;
        Title = title;
        Summary = summary;
        Temperature = temperature;
        MaxTokens = maxTokens;
        TimeoutSeconds = timeoutSeconds;
        HistoryChars = historyChars;
        PromptAdjustment = promptAdjustment;
    }
}

public static class LocalChatModelProfiles
{
    private static readonly LocalChatModelProfile Qwen3_8B = new LocalChatModelProfile(
        "qwen3:8b", "质量优先", "640 tokens · 75s · 上下文 1600 字",
        0.64f, 640, 75, 1600,
        "当前模型为 8B 质量优先档：可以展开到 3 至 6 句，但每句仍保持短而具体；先回答，再补充理由或建议。不要为了凑长度重复。\n"
        + "若问题复杂，允许使用较长的完整解释；普通寒暄不必强行写长。");

    private static readonly LocalChatModelProfile Qwen25_3B = new LocalChatModelProfile(
        "qwen2.5:3b", "均衡体验", "576 tokens · 55s · 上下文 1400 字",
        0.62f, 576, 55, 1400,
        "当前模型为 3B 均衡档：优先保证回答完整和连贯，普通问题控制在 3 至 5 句、70 至 150 字；每句只表达一个意思。\n"
        + "不要要求模型铺陈过多背景，先给结论和一个可执行建议。");

    private static readonly LocalChatModelProfile Qwen25_1_5B = new LocalChatModelProfile(
        "qwen2.5:1.5b", "低端设备", "384 tokens · 45s · 上下文 900 字",
        0.58f, 384, 45, 900,
        "当前模型为 1.5B 低端档：回答保持 2 至 4 句、45 至 100 字；只保留最相关的事实、判断和一个建议。\n"
        + "不要展开长背景，不要重复用户问题，不要为了人设堆叠比喻。");

    private static readonly LocalChatModelProfile Qwen25_0_5B = new LocalChatModelProfile(
        "qwen2.5:0.5b", "极低占用", "256 tokens · 35s · 上下文 600 字",
        0.55f, 256, 35, 600,
        "当前模型为 0.5B 极低占用档：只回答核心问题，控制在 1 至 3 句、25 至 70 字；使用简单、直接的中文。\n"
        + "不写长铺垫，不追加无关追问，不尝试复杂推理或多步骤方案。");

    private static readonly LocalChatModelProfile Fallback = new LocalChatModelProfile(
        "local", "通用本地", "512 tokens · 60s · 上下文 1200 字",
        0.62f, 512, 60, 1200,
        "当前模型为通用本地档：优先给出完整但简洁的回答，控制在 2 至 5 句，避免重复和空泛铺垫。");

    public static LocalChatModelProfile Get(string model)
    {
        if (string.Equals(model, Qwen3_8B.Model, StringComparison.OrdinalIgnoreCase)) return Qwen3_8B;
        if (string.Equals(model, Qwen25_3B.Model, StringComparison.OrdinalIgnoreCase)) return Qwen25_3B;
        if (string.Equals(model, Qwen25_1_5B.Model, StringComparison.OrdinalIgnoreCase)) return Qwen25_1_5B;
        if (string.Equals(model, Qwen25_0_5B.Model, StringComparison.OrdinalIgnoreCase)) return Qwen25_0_5B;
        return Fallback;
    }
}
