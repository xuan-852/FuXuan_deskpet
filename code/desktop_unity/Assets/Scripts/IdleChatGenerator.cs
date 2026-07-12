using UnityEngine;
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

    // 问候缓存对应的时间段标签（如 "上午"、"中午"、"下午"）
    // 跨时段时自动清空旧缓存，防止上午的问候出现在中午
    private string _greetingTimePeriod = "";
    // 闲话缓存对应的时间段标签
    private string _idleTimePeriod = "";

    /// <summary>
    /// 清空所有缓存（睡眠/挂起后调用），防止唤醒后使用过时的问候语/闲话。
    /// 同时重置生成时间戳，确保唤醒后立即重新生成。 
    /// </summary>
    public void ClearCache()
    {
        _idleCache.Clear();
        _greetingCache.Clear();
        _lastIdleGenTime = -999f;
        _lastGreetingGenTime = -999f;
        _isIdleGenerating = false;
        _isGreetingGenerating = false;
        _greetingTimePeriod = "";
        _idleTimePeriod = "";
        Debug.Log("[IdleChatGenerator] 🧹 已清空所有缓存（系统挂起）");
    }

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
        "累了就歇会儿吧~",
        "本座替你看着呢，安心~",
    };

    private static readonly string[] FALLBACK_GREETINGS = new string[]
    {
        "今日运势不错哦~",
        "你好呀~",
        "嗯？你来了~",
        "等你许久了呢~",
        "精神可好？本座替你算了一卦…",
    };

    private string ApiKey => ChatConfig.ApiKey;

    // ==================================================================
    //  闲话系统（用于待机气泡）
    // ==================================================================

    /// <summary>获取一条闲话（优先从缓存取）</summary>
    /// <param name="timeContext">时间/天气等上下文描述</param>
    public string GetIdleLine(string timeContext)
    {
        // ⚠️ 检测时间段是否变化 — 如果变了，清空旧闲话缓存
        string currentPeriod = ExtractTimePeriod(timeContext);
        if (_idleCache.Count > 0
            && currentPeriod != _idleTimePeriod
            && !string.IsNullOrEmpty(_idleTimePeriod))
        {
            Debug.Log($"[IdleChatGenerator] ⏰ 闲话时间段变化：'{_idleTimePeriod}' → '{currentPeriod}'，清空闲话缓存");
            _idleCache.Clear();
            _lastIdleGenTime = -999f;
        }

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

        // 记录本次生成对应的时间段
        _idleTimePeriod = ExtractTimePeriod(context);

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
            "2. 语气温柔中带着自信，七分关心三分傲，不可傲慢或说教\n" +
            "3. 符合符玄的身份（太卜司、法眼、穷观阵、星象卜算等）\n" +
            "4. 不要重复已有的常用句式\n" +
            "5. 其中1-2句可以自然提及忆境中的往事（如果有的话），不要全部围绕记忆\n" +
            "6. 「傲」与「娇」的比例大约 3:7 —— 多展示关心和温柔，少一些傲气\n" +
            "当前场景：{0}\n{1}", context, memories);

        string userPrompt = string.Format(
            "请生成{0}句简短的闲话（自言自语），每句不超过25字。" +
            "用 ||| 分隔，不要序号。例如：嗯…今日星象不错~ ||| 你气色尚佳，想来是有好事~",
            batchSize);

        string jsonBody = BuildSimpleRequestBody(sysPrompt, userPrompt);
        yield return StartCoroutine(
            ApiClient.PostRequest(apiUrl, ApiKey, jsonBody, 30,
                json => HandleIdleBatchResponse(json),
                err => { Debug.LogWarning($"[IdleChatGenerator] ⚠️ {err}"); _isIdleGenerating = false; }));
    }

    private void HandleIdleBatchResponse(string responseJson)
    {
        if (responseJson != null)
        {
            string content = ApiClient.ExtractContent(responseJson);
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
        // ⚠️ 检测时间段是否变化 — 如果变了，清空旧缓存
        // 防止上午生成的 "早安~" 在中午被取出
        string currentPeriod = ExtractTimePeriod(timeContext);
        if (_greetingCache.Count > 0
            && currentPeriod != _greetingTimePeriod
            && !string.IsNullOrEmpty(_greetingTimePeriod))
        {
            Debug.Log($"[IdleChatGenerator] ⏰ 时间段变化：'{_greetingTimePeriod}' → '{currentPeriod}'，清空问候缓存");
            _greetingCache.Clear();
            _lastGreetingGenTime = -999f; // 允许立即重新生成
        }

        // 缓存不足 → 后台触发生成
        TryGenerateGreeting(timeContext);

        if (_greetingCache.Count > 0)
            return _greetingCache.Dequeue();

        return FALLBACK_GREETINGS[Random.Range(0, FALLBACK_GREETINGS.Length)];
    }

    /// <summary>从时间上下文中提取时间段标签（"上午（10点钟）" → "上午"）</summary>
    private string ExtractTimePeriod(string timeContext)
    {
        if (string.IsNullOrEmpty(timeContext)) return "";
        int idx = timeContext.IndexOf('（');
        if (idx > 0) return timeContext.Substring(0, idx);
        return timeContext; // "周末，休息日" 等无括号的情况
    }

    /// <summary>触发问候生成（如果满足冷却条件）</summary>
    public void TryGenerateGreeting(string context)
    {
        if (_isGreetingGenerating) return;
        if (Time.time - _lastGreetingGenTime < generationCooldown) return;

        // 记录本次生成对应的时间段（用于后续跨时段检测）
        _greetingTimePeriod = ExtractTimePeriod(context);

        StartCoroutine(GenerateGreetingCoroutine(context));
    }

    private IEnumerator GenerateGreetingCoroutine(string context)
    {
        _isGreetingGenerating = true;
        _lastGreetingGenTime = Time.time;

        string memories = BuildMemoryContext();
        string sysPrompt = string.Format(
            "你是符玄，仙舟「罗浮」太卜司之首。你正在对在电脑前的人说话，主动问候一句。\n" +
            "符玄的性格：自信耿直，略带傲气，以「本座」自称。说话文雅带古风，常用卜算比喻。但对待眼前这人，态度温柔关切，不摆架子。\n" +
            "要求：\n" +
            "1. 每句话不超过30字\n" +
            "2. 语气温柔关切，七分温暖三分俏皮，不可傲慢或说教\n" +
            "3. 日常问候如「早啊」「今天如何？」即可，不必过于正式\n" +
            "4. 结合当前的{0}自然提及\n" +
            "5. 如果有忆境记录，可以其中1句自然地提及最近的记忆（不要全围绕记忆）\n" +
            "6. 不要重复，要有新意\n" +
            "7. 直接输出问候语本身，不要加任何前缀后缀\n" +
            "8. 「傲」与「娇」的比例大约 3:7 —— 多一分温柔，少一分傲气\n{1}",
            context, memories);

        string userPrompt = string.Format(
            "请生成3句不同的日常问候，每句不超过30字，结合{0}的特点。" +
            "用 ||| 分隔，不要序号。例如：看你精神不错，今日定有好事~ ||| 嗯？你来了，正好陪本座说说话。",
            context);

        string jsonBody = BuildSimpleRequestBody(sysPrompt, userPrompt);
        yield return StartCoroutine(
            ApiClient.PostRequest(apiUrl, ApiKey, jsonBody, 30,
                json => HandleGreetingResponse(json),
                err => { Debug.LogWarning($"[IdleChatGenerator] ⚠️ {err}"); _isGreetingGenerating = false; }));
    }

    private void HandleGreetingResponse(string responseJson)
    {
        if (responseJson != null)
        {
            string content = ApiClient.ExtractContent(responseJson);
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

    /// <summary>构造简单的 user/system 双消息请求体</summary>
    private string BuildSimpleRequestBody(string systemPrompt, string userMessage)
    {
        return "{\"model\":\"" + ApiClient.EscapeJson(model)
            + "\",\"messages\":["
            + "{\"role\":\"system\",\"content\":\"" + ApiClient.EscapeJson(systemPrompt) + "\"}"
            + ",{\"role\":\"user\",\"content\":\"" + ApiClient.EscapeJson(userMessage) + "\"}"
            + "],\"max_tokens\":512}";
    }
}
