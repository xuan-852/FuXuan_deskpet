using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 闲话/问候生成器 — 调用 DeepSeek API 动态生成符玄的回复
/// 
/// 职责：
/// 1. 维护闲话缓存（批量生成，减少 API 调用）
/// 2. 维护问候缓存（每次问候前预生成）
/// 3. 提供 fallback 机制（API 失败或冷却中时返回硬编码备用）
/// 4. 自动管理生成冷却，避免频繁调用
/// 
/// 与 ChatManager 的区别：
/// - ChatManager = 正式对话（带历史、工具调用）
/// - IdleChatGenerator = 轻量单次生成（无历史、无工具、短文本）
/// </summary>
public class IdleChatGenerator : MonoBehaviour
{
    [Header("API 设置")]
    public string apiUrl = "https://api.deepseek.com";
    public string model = "deepseek-chat";

    [Header("缓存设置")]
    [Tooltip("最小缓存量 — 低于此数触发后台批量生成")]
    public int minCacheSize = 4;

    [Tooltip("每次批量生成的条数")]
    public int batchSize = 10;

    [Tooltip("批量生成冷却（秒）")]
    public float generationCooldown = 180f; // 3分钟

    // ===== 内部状态 =====
    private Queue<string> _idleCache = new Queue<string>();
    private Queue<string> _greetingCache = new Queue<string>();
    private float _lastIdleGenTime = -999f;
    private float _lastGreetingGenTime = -999f;
    private bool _isIdleGenerating = false;
    private bool _isGreetingGenerating = false;

    // ===== Fallback 备用库 =====
    // API 不可用/冷却中时使用，确保不会出现空气泡
    private static readonly string[] FALLBACK_IDLE = new string[]
    {
        "嗯…在想什么呢~",
        "好安静呀…",
        "今天天气不错~",
        "你在干嘛呢？",
        "要不要一起说说话？",
        "唔…该做点什么好呢~",
        "闲来无事，看星星去~",
    };

    private static readonly string[] FALLBACK_GREETINGS = new string[]
    {
        "今日运势不错哦~",
        "你好呀~",
        "找本座何事呀~",
        "嗯？你来了~",
    };

    private string ApiKey => ChatConfig.ApiKey;

    // ==================================================================
    //  闲话系统（用于待机气泡）
    // ==================================================================

    /// <summary>获取一条闲话（优先从缓存取）</summary>
    /// <param name="timeContext">时间/天气等上下文描述</param>
    public string GetIdleLine(string timeContext)
    {
        // 缓存不足 → 后台触发批量生成
        TryGenerateIdleBatch(timeContext);

        // 有缓存 → 取出
        if (_idleCache.Count > 0)
            return _idleCache.Dequeue();

        // 无缓存 → fallback
        return FALLBACK_IDLE[Random.Range(0, FALLBACK_IDLE.Length)];
    }

    /// <summary>闲话缓存是否充足（供外部判断是否触发预生成）</summary>
    public bool IsIdleCacheHealthy => _idleCache.Count >= minCacheSize;

    /// <summary>触发闲话批量生成（如果满足冷却条件）</summary>
    public void TryGenerateIdleBatch(string context)
    {
        if (_isIdleGenerating) return;
        if (Time.time - _lastIdleGenTime < generationCooldown) return;
        StartCoroutine(GenerateIdleBatchCoroutine(context));
    }

    private IEnumerator GenerateIdleBatchCoroutine(string context)
    {
        _isIdleGenerating = true;
        _lastIdleGenTime = Time.time;

        string memories = BuildMemoryContext();
        string sysPrompt = string.Format(
            "你是符玄，仙舟「罗浮」太卜司之首。你会在独处时偶尔自言自语。\n" +
            "要求：\n" +
            "1. 每句话不超过25字\n" +
            "2. 语气自信、傲娇、带点关心\n" +
            "3. 符合符玄的身份（太卜司、法眼、穷观阵、星象卜算等）\n" +
            "4. 不要重复已有的常用句式\n" +
            "5. 其中1-2句可以自然提及忆境中的往事（如果有的话），不要全部围绕记忆\n" +
            "当前场景：{0}\n{1}", context, memories);

        string userPrompt = string.Format(
            "请生成{0}句简短的闲话（自言自语），每句不超过25字。" +
            "用 ||| 分隔，不要序号。例如：嗯…今日星象不错~ ||| 你气色尚佳，想来是有好事~",
            batchSize);

        yield return StartCoroutine(
            PostRequest(BuildRequestBody(sysPrompt, userPrompt),
                json => HandleIdleBatchResponse(json)));
    }

    private void HandleIdleBatchResponse(string responseJson)
    {
        if (responseJson != null)
        {
            string content = ExtractContent(responseJson);
            if (!string.IsNullOrEmpty(content))
            {
                // 用 ||| 分割
                string[] lines = content.Split(new string[] { "|||" },
                    System.StringSplitOptions.RemoveEmptyEntries);
                int added = 0;
                foreach (string line in lines)
                {
                    string trimmed = line.Trim().TrimStart('1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.', '、', '，', ' ');
                    if (trimmed.Length > 0 && trimmed.Length <= 50)
                    {
                        _idleCache.Enqueue(trimmed);
                        added++;
                    }
                }
                Debug.Log($"[IdleChatGenerator] ✅ 闲话生成完成，新增 {added} 条");
            }
        }
        _isIdleGenerating = false;
    }

    // ==================================================================
    //  问候系统（用于定时问候）
    // ==================================================================

    /// <summary>获取一句问候（优先从缓存取）</summary>
    public string GetGreeting(string timeContext)
    {
        // 缓存不足 → 后台触发生成
        TryGenerateGreeting(timeContext);

        if (_greetingCache.Count > 0)
            return _greetingCache.Dequeue();

        return FALLBACK_GREETINGS[Random.Range(0, FALLBACK_GREETINGS.Length)];
    }

    /// <summary>触发问候生成（如果满足冷却条件）</summary>
    public void TryGenerateGreeting(string context)
    {
        if (_isGreetingGenerating) return;
        if (Time.time - _lastGreetingGenTime < generationCooldown) return;
        StartCoroutine(GenerateGreetingCoroutine(context));
    }

    private IEnumerator GenerateGreetingCoroutine(string context)
    {
        _isGreetingGenerating = true;
        _lastGreetingGenTime = Time.time;

        string memories = BuildMemoryContext();
        string sysPrompt = string.Format(
            "你是符玄，仙舟「罗浮」太卜司之首。现在你要主动问候你的主人（电脑前的用户）。\n" +
            "要求：\n" +
            "1. 每句话不超过30字\n" +
            "2. 语气自信、傲娇、带点关心\n" +
            "3. 结合当前的{0}来问候\n" +
            "4. 如果有忆境记录，可以其中1句自然地提及最近的记忆（不要全围绕记忆）\n" +
            "5. 不要重复，要有新意\n" +
            "6. 直接输出问候语本身，不要加任何前缀后缀\n{1}",
            context, memories);

        string userPrompt = string.Format(
            "请生成3句不同的问候语，每句不超过30字，结合{0}的特点。" +
            "用 ||| 分隔。例如：晨光正好，今日宜出门走走~ ||| 看你精神不错，很好。",
            context);

        yield return StartCoroutine(
            PostRequest(BuildRequestBody(sysPrompt, userPrompt),
                json => HandleGreetingResponse(json)));
    }

    private void HandleGreetingResponse(string responseJson)
    {
        if (responseJson != null)
        {
            string content = ExtractContent(responseJson);
            if (!string.IsNullOrEmpty(content))
            {
                string[] lines = content.Split(new string[] { "|||" },
                    System.StringSplitOptions.RemoveEmptyEntries);
                int added = 0;
                foreach (string line in lines)
                {
                    string trimmed = line.Trim().TrimStart('1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.', '、', '，', ' ');
                    if (trimmed.Length > 0 && trimmed.Length <= 50)
                    {
                        _greetingCache.Enqueue(trimmed);
                        added++;
                    }
                }
                Debug.Log($"[IdleChatGenerator] ✅ 问候生成完成，新增 {added} 条");
            }
        }
        _isGreetingGenerating = false;
    }

    // ==================================================================
    //  记忆注入 — 从 PetMemory 取忆境数据，增强闲话/问候的真实感
    // ==================================================================

    /// <summary>
    /// 构建记忆上下文文本（最近3条），若无记忆则返回空
    /// 让符玄在闲话/问候中也能自然地谈及过去的互动
    /// </summary>
    private string BuildMemoryContext()
    {
        if (PetMemory.Instance == null) return "";
        string formatted = PetMemory.Instance.GetFormattedMemories();
        if (string.IsNullOrEmpty(formatted)) return "";

        // 只取最近3条（避免太长），且以「忆境残象」形式注入
        var lines = formatted.Split('\n');
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n【忆境残象 — 你对主人的记忆】");
        int count = 0;
        foreach (string line in lines)
        {
            if (line.Contains("entry_") && count < 3)
            {
                sb.AppendLine(line.Trim());
                count++;
            }
        }
        if (count == 0) return "";
        sb.AppendLine("（这些是你记得的事，可以在闲话或问候中自然地提及）");
        return sb.ToString();
    }

    // ==================================================================
    //  API 请求（轻量版 — 无历史、无工具调用）
    // ==================================================================

    private string BuildRequestBody(string systemPrompt, string userMessage)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"model\":\"");
        sb.Append(EscapeJson(model));
        sb.Append("\",\"messages\":[");
        sb.Append("{\"role\":\"system\",\"content\":\"");
        sb.Append(EscapeJson(systemPrompt));
        sb.Append("\"},{\"role\":\"user\",\"content\":\"");
        sb.Append(EscapeJson(userMessage));
        sb.Append("\"}],\"max_tokens\":512}");
        return sb.ToString();
    }

    private IEnumerator PostRequest(string jsonBody, System.Action<string> onResult)
    {
        string fullUrl = apiUrl.TrimEnd('/') + "/v1/chat/completions";

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 15; // 闲话生成超时更短

            if (!string.IsNullOrEmpty(ApiKey))
                req.SetRequestHeader("Authorization", "Bearer " + ApiKey);

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                onResult(req.downloadHandler.text);
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = !string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\"")
                    ? ExtractErrorMessage(errBody)
                    : req.error;
                Debug.LogWarning($"[IdleChatGenerator] ⚠️ API 请求失败: {errMsg}");
                onResult(null);
            }
        }
    }

    // ==================================================================
    //  JSON 解析
    // ==================================================================

    private string ExtractContent(string json)
    {
        try
        {
            // 简单解析 — 找 "content":"..." 
            string searchKey = "\"content\":\"";
            int idx = json.IndexOf(searchKey);
            if (idx < 0)
            {
                // 可能 content 为 null
                return "";
            }
            idx += searchKey.Length;
            StringBuilder sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == 'n') sb.Append('\n');
                    else if (next == 't') sb.Append('\t');
                    else if (next == '\\') sb.Append('\\');
                    else if (next == '"') sb.Append('"');
                    else sb.Append(next);
                    i++;
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[IdleChatGenerator] ⚠️ 解析失败: {e.Message}");
            return "";
        }
    }

    private string ExtractErrorMessage(string json)
    {
        try
        {
            string key = "\"message\":\"";
            int idx = json.IndexOf(key);
            if (idx < 0) return "未知错误";
            idx += key.Length;
            int end = json.IndexOf("\"", idx);
            return end > idx ? json.Substring(idx, end - idx) : "未知错误";
        }
        catch
        {
            return "解析错误";
        }
    }

    private string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }
}
