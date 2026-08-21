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
    public const int MaxSummaryLength = 240;
    public const float DefaultConfidence = 0.65f;

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

        // 相关性优先，重要度和可信度兜底；避免所有高分旧记忆挤掉当前问题相关内容。
        return lexical * 0.45f + importance * 0.25f + confidence * 0.20f + recency * 0.10f;
    }

    public static float TermOverlap(string query, string candidate)
    {
        HashSet<string> queryTerms = ExtractTerms(query);
        HashSet<string> candidateTerms = ExtractTerms(candidate);
        if (queryTerms.Count == 0 || candidateTerms.Count == 0) return 0f;
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
