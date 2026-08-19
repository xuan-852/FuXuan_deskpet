using System;
using System.Text.RegularExpressions;

/// <summary>
/// 本地回复的低成本人设护栏。
/// 只做确定性、低风险的词级修正，不重写语义，不调用第二次模型。
/// </summary>
public static class LocalReplyPostProcessor
{
    public static string Process(string reply)
    {
        return Process(reply, null);
    }

    /// <summary>
    /// 在词级人设护栏后，处理用户明确提出的“三句话”等硬格式要求。
    /// 只做单次、确定性裁剪，不为格式问题重新调用模型。
    /// </summary>
    public static string Process(string reply, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(reply)) return reply;

        string result = reply.Trim()
            .Replace("将军", "主人")
            .Replace("我们", "你我");

        // 避免模型在普通自述中使用“我”；“我们”已在上面单独处理。
        // 保留“自我”等固定词，不把它们误改成“自本座”；“我的”仍会变为“本座的”。
        result = Regex.Replace(result, "(?<!自)我(?!们)", "本座");

        // 长回复中称呼过密会显得机械：保留前两次，后续改为“你”。
        int keptOwnerAddress = 0;
        result = Regex.Replace(result, "主人", match =>
        {
            keptOwnerAddress++;
            return keptOwnerAddress <= 2 ? match.Value : "你";
        });

        int requestedSentences = GetRequestedSentenceCount(userMessage);
        if (requestedSentences > 0)
            result = LimitSentences(result, requestedSentences);

        return result;
    }

    private static int GetRequestedSentenceCount(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return 0;
        if (userMessage.Contains("三句话") || userMessage.Contains("三句") || userMessage.Contains("3句话") || userMessage.Contains("3句")) return 3;
        if (userMessage.Contains("一句话") || userMessage.Contains("一句") || userMessage.Contains("1句话") || userMessage.Contains("1句")) return 1;
        return 0;
    }

    private static string LimitSentences(string text, int maxSentences)
    {
        var matches = Regex.Matches(text, @"[^。！？!?]+[。！？!?]?");
        if (matches.Count <= maxSentences) return text;

        var kept = new System.Text.StringBuilder();
        int count = 0;
        foreach (Match match in matches)
        {
            string sentence = match.Value.Trim();
            if (sentence.Length == 0) continue;
            if (count++ >= maxSentences) break;
            if (kept.Length > 0) kept.Append('\n');
            kept.Append(sentence);
        }
        return kept.Length > 0 ? kept.ToString() : text;
    }
}
