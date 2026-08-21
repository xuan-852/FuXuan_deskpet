using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// 记忆治理的纯逻辑层：负责规范化、近似去重、相关性和时间衰减计算。
/// 不访问 Unity 生命周期或文件，便于 EditMode 测试，也避免把治理逻辑继续堆进 PetMemory。
/// </summary>
public static class MemoryGovernance
{
    public const int MaxSummaryLength = 180;
    public const float DefaultConfidence = 0.65f;
    public const float DurableConfidenceThreshold = 0.72f;

    /// <summary>
    /// 记忆的保留层级：durable 是稳定事实/偏好，episodic 是短期事件，
    /// tool 是工具产生的短期轨迹，reflection 是经反思确认的短期洞察。
    /// </summary>
    public const string DurableTier = "durable";
    public const string EpisodicTier = "episodic";
    public const string ToolTier = "tool";
    public const string ReflectionTier = "reflection";

    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var sb = new StringBuilder(value.Length);
        foreach (char c in value.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static bool IsValidSummary(string summary)
    {
        string normalized = Normalize(summary);
        return normalized.Length >= 3;
    }

    /// <summary>判断一条用户消息是否包含“值得提取”的明确表达。</summary>
    public static bool HasExplicitMemorySignal(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsLowValueConversation(message)) return false;

        string[] markers =
        {
            "我叫", "我的名字", "叫我", "称呼我", "我喜欢", "我不喜欢", "我讨厌",
            "我偏好", "我习惯", "我通常", "以后", "记住", "别叫", "不要称呼",
            "我是", "我在", "我正在准备", "我的生日", "过敏", "我不能", "我需要",
            "我希望", "请记住", "务必记得"
        };

        return markers.Any(message.Contains);
    }

    /// <summary>
    /// 本地提取不可用时的更保守兜底信号。模糊的“我在/我需要/以后”不直接落盘，
    /// 防止离线时把一次性上下文误当成稳定事实。
    /// </summary>
    public static bool HasStrongMemorySignal(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsLowValueConversation(message)) return false;
        string[] markers =
        {
            "我叫", "我的名字", "叫我", "称呼我", "我喜欢", "我不喜欢", "我讨厌",
            "我偏好", "我习惯", "我是", "我的生日", "过敏", "我不能", "我希望",
            "记住", "请记住", "别叫", "不要称呼", "务必记得"
        };
        return markers.Any(message.Contains);
    }

    /// <summary>过滤问候、确认、感谢和一次性闲聊，避免“什么都记”。</summary>
    public static bool IsLowValueConversation(string message)
    {
        string normalized = Normalize(message);
        if (normalized.Length < 3) return true;

        string[] transientPhrases =
        {
            "你好", "您好", "嗨", "早安", "晚安", "谢谢", "多谢", "好的", "好吧",
            "嗯", "哦", "哈哈", "拜拜", "再见", "你在干嘛", "最近怎么样",
            "讲个故事", "随便聊聊", "没什么", "知道了", "收到", "行"
        };
        return transientPhrases.Any(p => normalized == Normalize(p));
    }

    /// <summary>按来源和类别决定长期存储层级。</summary>
    public static string GetTier(string category, string source, int importance, int expiresAfterDays)
    {
        if (string.Equals(category, "reflection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "reflection", StringComparison.OrdinalIgnoreCase))
            return ReflectionTier;
        if (string.Equals(category, "tool", StringComparison.OrdinalIgnoreCase))
            return ToolTier;
        if (expiresAfterDays > 0 || importance <= 6)
            return EpisodicTier;
        return DurableTier;
    }

    /// <summary>保存前的第二道闸门。旧 API 仍可调用，但低价值输入不会进入长期忆境。</summary>
    public static bool ShouldPersist(
        string summary,
        string category,
        string source,
        int importance,
        float confidence,
        out string reason)
    {
        reason = "";
        if (!IsValidSummary(summary))
        {
            reason = "empty_or_short";
            return false;
        }

        string normalizedSource = (source ?? "").Trim().ToLowerInvariant();
        string normalizedCategory = (category ?? "").Trim().ToLowerInvariant();
        bool isConversation = normalizedCategory == "conversation";
        bool isTool = normalizedCategory == "tool";
        bool isReflection = normalizedCategory == "reflection" || normalizedSource == "reflection";

        // 普通系统调用不能越过长期事实闸门；明确用户消息和反思结果允许进入。
        if (isConversation && normalizedSource == "system" && importance < 7)
        {
            reason = "system_conversation_too_weak";
            return false;
        }
        if (isTool && importance < 7)
        {
            reason = "transient_tool_event";
            return false;
        }
        if (isConversation
            && normalizedSource == "local_model"
            && (importance < 7 || confidence < DurableConfidenceThreshold))
        {
            reason = "low_model_confidence";
            return false;
        }
        if (isConversation && IsLowValueConversation(summary))
        {
            reason = "low_value_conversation";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 校验本地模型的提取结果：必须有明确用户证据、足够置信度，且摘要与原话有词元交集。
    /// </summary>
    public static bool AcceptExtractedMemory(
        string userMessage,
        string summary,
        int importance,
        float confidence,
        string memoryType)
    {
        if (!HasExplicitMemorySignal(userMessage)) return false;
        if (!IsValidSummary(summary) || importance < 7 || confidence < DurableConfidenceThreshold)
            return false;
        if (string.Equals(memoryType, "episodic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(memoryType, "temporary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(memoryType, "none", StringComparison.OrdinalIgnoreCase))
            return false;

        return TermOverlap(userMessage, summary) >= 0.20f;
    }

    public static bool IsNearDuplicate(string left, string right)
    {
        string a = Normalize(left);
        string b = Normalize(right);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (a == b) return true;

        int shorter = Math.Min(a.Length, b.Length);
        int longer = Math.Max(a.Length, b.Length);
        if (shorter < 8 || shorter / (float)longer < 0.78f) return false;
        return a.Contains(b) || b.Contains(a);
    }

    public static bool IsExpired(PetMemory.MemoryEntry entry, DateTime now)
    {
        if (entry == null || string.IsNullOrEmpty(entry.expiresAt)) return false;
        if (!TryParseTimestamp(entry.expiresAt, out DateTime expires)) return false;
        return expires <= now;
    }

    public static float Relevance(PetMemory.MemoryEntry entry, string query, DateTime now)
    {
        if (entry == null || IsExpired(entry, now)) return float.MinValue;

        float importance = Clamp01(entry.importance / 10f);
        float confidence = Clamp01(entry.confidence <= 0f ? DefaultConfidence : entry.confidence);
        float recency = Recency(entry, now);
        float lexical = string.IsNullOrWhiteSpace(query)
            ? 0f
            : TermOverlap(query, (entry.topic ?? "") + " " + (entry.summary ?? ""));

        // 相关性必须占主导，避免一条无关的高分旧记忆挤掉当前问题。
        return lexical * 0.65f + importance * 0.15f + confidence * 0.10f + recency * 0.10f;
    }

    public static float TermOverlap(string query, string candidate)
    {
        HashSet<string> queryTerms = ExtractTerms(query);
        HashSet<string> candidateTerms = ExtractTerms(candidate);
        if (queryTerms.Count == 0 || candidateTerms.Count == 0) return 0f;

        // 中文单字重叠很容易被“我/你/的”等泛词误触发；优先用双字词判断主题，
        // 没有双字词时才退回普通词元比例。
        HashSet<string> queryBigrams = new HashSet<string>(
            queryTerms.Where(term => term.Length >= 2), StringComparer.OrdinalIgnoreCase);
        HashSet<string> candidateBigrams = new HashSet<string>(
            candidateTerms.Where(term => term.Length >= 2), StringComparer.OrdinalIgnoreCase);
        if (queryBigrams.Count > 0)
        {
            return queryBigrams.Intersect(candidateBigrams, StringComparer.OrdinalIgnoreCase).Count()
                / (float)queryBigrams.Count;
        }

        return queryTerms.Intersect(candidateTerms, StringComparer.OrdinalIgnoreCase).Count()
            / (float)queryTerms.Count;
    }

    public static float Recency(PetMemory.MemoryEntry entry, DateTime now)
    {
        string dateText = string.IsNullOrEmpty(entry.lastAccessAt)
            ? entry.timestamp
            : entry.lastAccessAt;
        if (!TryParseTimestamp(dateText, out DateTime date)) return 0.5f;

        double days = Math.Max(0d, (now - date).TotalDays);
        return (float)(1d / (1d + days / 30d));
    }

    /// <summary>容量淘汰分：稳定、可信、被多次确认且近期使用的记忆更难被淘汰。</summary>
    public static float RetentionScore(PetMemory.MemoryEntry entry, DateTime now)
    {
        if (entry == null) return float.MinValue;
        float importance = Math.Max(0f, Math.Min(1f, entry.importance / 10f));
        float confidence = Math.Max(0f, Math.Min(1f, entry.confidence <= 0f ? DefaultConfidence : entry.confidence));
        float evidence = Math.Max(0f, Math.Min(1f, entry.evidenceCount / 3f));
        float access = Math.Max(0f, Math.Min(1f, entry.accessCount / 5f));
        return importance * 0.45f + confidence * 0.25f + evidence * 0.20f
            + Math.Max(access, Recency(entry, now)) * 0.10f;
    }

    public static string Timestamp(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    public static bool TryParseTimestamp(string value, out DateTime result)
    {
        return DateTime.TryParseExact(
            value,
            new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "o" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
    }

    private static HashSet<string> ExtractTerms(string value)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(value)) return terms;

        var latin = new StringBuilder();
        string normalized = value.ToLowerInvariant();
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (IsCjk(c))
            {
                FlushLatin(latin, terms);
                terms.Add(c.ToString());
                if (i + 1 < normalized.Length && IsCjk(normalized[i + 1]))
                    terms.Add(normalized.Substring(i, 2));
            }
            else if (char.IsLetterOrDigit(c))
            {
                latin.Append(c);
            }
            else
            {
                FlushLatin(latin, terms);
            }
        }
        FlushLatin(latin, terms);
        return terms;
    }

    private static void FlushLatin(StringBuilder buffer, HashSet<string> terms)
    {
        if (buffer.Length == 0) return;
        if (buffer.Length >= 2) terms.Add(buffer.ToString());
        buffer.Length = 0;
    }

    private static bool IsCjk(char c)
    {
        return (c >= '\u3400' && c <= '\u4DBF') || (c >= '\u4E00' && c <= '\u9FFF');
    }
}
