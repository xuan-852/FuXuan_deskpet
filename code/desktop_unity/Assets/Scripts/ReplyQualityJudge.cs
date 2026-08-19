using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 回答内容质量裁判。
///
/// 默认使用零 Token 的规则裁判；质量测试可设置 FU_XUAN_REPLY_JUDGE=local，
/// 让 Ollama 对同一回答按五维 rubric 评分。cloud 只允许显式配置，避免日常运行烧钱。
/// 原始输入和回答只存在于裁判调用内，不落盘。
/// </summary>
public static class ReplyQualityJudge
{
    public const string RuleProvider = "rule";
    public const string LocalProvider = "local";
    public const string CloudProvider = "cloud";

    public sealed class Result
    {
        public string Provider = RuleProvider;
        public int Persona = -1;
        public int Memory = -1;
        public int Time = -1;
        public int Relevance = -1;
        public int Constraint = -1;
        public string Reason = "";

        public int AverageScore
        {
            get
            {
                int sum = 0;
                int count = 0;
                Add(Persona, ref sum, ref count);
                Add(Memory, ref sum, ref count);
                Add(Time, ref sum, ref count);
                Add(Relevance, ref sum, ref count);
                Add(Constraint, ref sum, ref count);
                return count == 0 ? -1 : (int)Math.Round((double)sum / count);
            }
        }

        private static void Add(int value, ref int sum, ref int count)
        {
            if (value >= 0) { sum += value; count++; }
        }
    }

    public static string Provider
    {
        get
        {
            string value = Environment.GetEnvironmentVariable("FU_XUAN_REPLY_JUDGE");
            if (string.Equals(value, LocalProvider, StringComparison.OrdinalIgnoreCase)) return LocalProvider;
            if (string.Equals(value, CloudProvider, StringComparison.OrdinalIgnoreCase)) return CloudProvider;
            return RuleProvider;
        }
    }

    public static IEnumerator EvaluateAsync(string input, string reply, Action<Result> onResult)
    {
        Result ruleResult = EvaluateByRules(input, reply);
        string provider = Provider;

        if (provider == LocalProvider)
        {
            if (LocalLLMClient.IsReady || !ChatConfig.UseCloudBaseline)
            {
                bool completed = false;
                yield return LocalLLMClient.PromptAsync(
                    BuildSystemPrompt(),
                    BuildJudgeInput(input, reply),
                    (ok, content) =>
                    {
                        Result parsed;
                        if (ok && TryParse(content, LocalProvider, out parsed))
                            onResult?.Invoke(parsed);
                        else
                            onResult?.Invoke(ruleResult);
                        completed = true;
                    },
                    temperature: 0.0f,
                    maxTokens: 160);
                if (!completed) onResult?.Invoke(ruleResult);
                yield break;
            }
        }

        if (provider == CloudProvider && !ChatConfig.UseOllamaMode && !ChatManager.IsTestMode)
        {
            bool completed = false;
            string body = BuildCloudBody(BuildJudgeInput(input, reply));
            yield return ApiClient.PostRequest(
                "https://api.deepseek.com", ChatConfig.ApiKey, body, 60,
                response =>
                {
                    Result parsed;
                    if (TryParse(ApiClient.ExtractContent(response), CloudProvider, out parsed))
                        onResult?.Invoke(parsed);
                    else
                        onResult?.Invoke(ruleResult);
                    completed = true;
                },
                _ => { onResult?.Invoke(ruleResult); completed = true; },
                "judge");
            if (!completed) onResult?.Invoke(ruleResult);
            yield break;
        }

        onResult?.Invoke(ruleResult);
    }

    public static Result EvaluateByRules(string input, string reply)
    {
        input = input ?? "";
        reply = reply ?? "";
        var result = new Result { Provider = RuleProvider };

        if (string.IsNullOrWhiteSpace(reply) || ContainsAny(reply, "error", "exception", "api key", "错误", "无法回答"))
        {
            result.Persona = 0;
            result.Relevance = 0;
            result.Reason = "empty_or_error_reply";
            return result;
        }

        result.Persona = ContainsAny(reply, "本座", "太卜", "卿", "星神") ? 5 : 3;
        if (ContainsAny(reply, "as an ai", "作为ai", "我是一个语言模型")) result.Persona = 1;
        result.Relevance = reply.Length < 2 ? 1 : (reply.Length > 2000 ? 3 : 5);

        if (ContainsAny(input, "几点", "时间", "今天", "星期", "天气"))
            result.Time = ContainsAny(reply, "点", "时", "星期", "天气", "晴", "阴", "雨", "度") || ContainsDigit(reply) ? 5 : 1;

        if (ContainsAny(input, "记得", "上次", "偏好", "我叫", "我喜欢", "之前"))
            result.Memory = ContainsAny(reply, "记得", "上次", "偏好", "名字", "之前") ? 4 : 2;

        if (ContainsAny(input, "三句", "简单", "简短", "不超过", "用通俗的话"))
        {
            int sentences = CountSentences(reply);
            result.Constraint = ContainsAny(input, "三句") ? (sentences <= 3 ? 5 : 2) : (reply.Length <= 180 ? 5 : 2);
        }

        result.Reason = $"rule:persona={result.Persona},memory={result.Memory},time={result.Time},relevance={result.Relevance},constraint={result.Constraint}";
        return result;
    }

    private static string BuildSystemPrompt()
    {
        return "You are a strict evaluator for Fu Xuan desktop pet replies. " +
            "Score each applicable dimension from 0 to 5; use -1 only when not applicable. " +
            "persona=character fit, memory=correct memory use, time=correct current context, " +
            "relevance=answers the request, constraint=follows explicit constraints. " +
            "Return JSON only: {\"persona\":0,\"memory\":-1,\"time\":-1,\"relevance\":0,\"constraint\":-1,\"reason\":\"under 100 chars\"}.";
    }

    private static string BuildJudgeInput(string input, string reply)
    {
        return "USER INPUT:\n" + Truncate(input, 1200) + "\nREPLY:\n" + Truncate(reply, 2400);
    }

    private static string BuildCloudBody(string prompt)
    {
        var body = new JObject
        {
            ["model"] = "deepseek-v4-flash",
            ["temperature"] = 0.0,
            ["max_tokens"] = 160,
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = BuildSystemPrompt() },
                new JObject { ["role"] = "user", ["content"] = prompt }
            }
        };
        return body.ToString(Formatting.None);
    }

    private static bool TryParse(string content, string provider, out Result result)
    {
        result = null;
        if (string.IsNullOrEmpty(content)) return false;
        try
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            JObject obj = JObject.Parse(content.Substring(start, end - start + 1));
            result = new Result
            {
                Provider = provider,
                Persona = ClampScore(obj["persona"]),
                Memory = ClampScore(obj["memory"]),
                Time = ClampScore(obj["time"]),
                Relevance = ClampScore(obj["relevance"]),
                Constraint = ClampScore(obj["constraint"]),
                Reason = Truncate(obj["reason"]?.ToString() ?? "", 100)
            };
            return true;
        }
        catch { return false; }
    }

    private static int ClampScore(JToken token)
    {
        if (token == null) return -1;
        int value;
        return int.TryParse(token.ToString(), out value) ? Math.Max(-1, Math.Min(5, value)) : -1;
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
            if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static bool ContainsDigit(string value)
    {
        foreach (char c in value) if (char.IsDigit(c)) return true;
        return false;
    }

    private static int CountSentences(string value)
    {
        int count = 0;
        foreach (char c in value)
            if (c == '.' || c == '!' || c == '?' || c == '。' || c == '！' || c == '？') count++;
        return Math.Max(1, count);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value.Substring(0, max);
    }
}
