using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 符玄「镜鉴」— GLM-4V 多帧动作评分验证器
///
/// 使用 GLM-4V (智谱) 对动作多帧拼图进行视觉评分：
///   - 动作播放期间在 20%/40%/60%/80% 进度抓取截图
///   - 合成 2×2 拼图后发给 GLM-4V 评分
///   - ≥ passThreshold(3) → 可信，记入 MotionMemoryManager UpdateScore
///   - 否则 → 触发负反馈
///
/// 注：Qwen-VL-Plus 实测无视觉区分度（始终打 1~2 分），已移除。
///
/// 验证日志: Application.persistentDataPath/validation_log.json
/// </summary>
public class DualModelValidator : MonoBehaviour
{
    [Header("评分阈值")]
    [Tooltip("最低通过分（含），低于此分不写入记忆")]
    public int passThreshold = 3;

    // ==================================================================
    //  运行时
    // ==================================================================

    private Live2DRenderer _renderer;
    private MotionMemoryManager _memoryManager;

    /// <summary>验证日志条目</summary>
    [Serializable]
    public class ValidationEntry
    {
        public string timestamp;
        public string actionDescription;
        public int scoreGlm;
        public int scoreAvg;
        public bool isConsensus;
        public string reviewGlm;
        public float duration;
        public int keyframeCount;
    }

    [Serializable]
    private class ValidationStore
    {
        public List<ValidationEntry> entries = new List<ValidationEntry>();
    }

    private ValidationStore _log = new ValidationStore();
    private string LogPath => Path.Combine(Application.persistentDataPath, "validation_log.json");

    public static DualModelValidator Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        LoadLog();
    }

    void Start()
    {
        _renderer = FindObjectOfType<Live2DRenderer>();
        _memoryManager = MotionMemoryManager.Instance;
        if (_memoryManager == null)
        {
            var go = new GameObject("MotionMemoryManager");
            _memoryManager = go.AddComponent<MotionMemoryManager>();
            go.transform.SetParent(transform);
        }
    }

    // ==================================================================
    //  公开接口
    // ==================================================================

    /// <summary>
    /// 对动作截图执行双模型交叉验证
    /// </summary>
    /// <param name="description">动作描述</param>
    /// <param name="imageDataUrl">截图 data:image/png;base64,...</param>
    /// <param name="plan">动作计划（用于提取参数信息）</param>
    /// <param name="onResult">回调: (isConsensus, avgScore, glmScore, qwenScore, glmReview, qwenReview)</param>
    /// <param name="imageDataUrl">多帧拼图截图 data:image/png;base64,...</param>
    /// <param name="singleScore">GLM 评分（1-5），回调后由调用方处理</param>
    /// <param name="singleReview">GLM 评语</param>
    public IEnumerator ValidateAsync(
        string description,
        string imageDataUrl,
        MotionPlanner.MotionPlan plan,
        Action<bool, int, int, int, int, string, string> onResult)
    {
        if (string.IsNullOrEmpty(imageDataUrl))
        {
            Debug.LogWarning("[DualModelValidator] 截图为空，跳过验证");
            onResult(false, 0, 0, 0, 0, "", "");
            yield break;
        }

        // ── 单模型：GLM-4V ──
        string review = "";
        int score = 0;
        bool done = false;

        Coroutine coro = StartCoroutine(CallGlmVision(description, imageDataUrl,
            (s, r) => { score = s; review = r; done = true; }));

        float timeout = 90f;
        float elapsed = 0f;
        while (!done)
        {
            if (elapsed >= timeout)
            {
                Debug.LogWarning("[DualModelValidator] GLM-4V 超时");
                StopCoroutine(coro);
                onResult(false, 0, 0, 0, 0, "", "");
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // ── 裁决 ──
        bool isConsensus = score >= passThreshold;
        int avgScore = isConsensus ? score : 0;

        // ── 写日志 ──
        int kfCount = plan?.KeyFrames?.Count ?? 0;
        float dur = plan?.TotalDuration ?? 0f;
        LogValidation(description, score, avgScore, isConsensus, review, dur, kfCount);

        // ── Consensus → 由调用方写入 MotionMemoryManager ──
        // ── 低分 → 记录负反馈 ──
        if (_memoryManager != null && plan != null && !isConsensus && score > 0 && score <= _memoryManager.negativeThreshold)
        {
            string paramSnapshot = ExtractParamSnapshot(plan);
            _memoryManager.RecordNegativeExample(description, paramSnapshot, score, $"GLM={score}/5: {Truncate(review, 120)}");
        }

        onResult?.Invoke(isConsensus, avgScore, score, 0, 0, review, "");
    }

    // ==================================================================
    //  模型 A: GLM-4V
    // ==================================================================

    private IEnumerator CallGlmVision(string description, string imageDataUrl,
        Action<int, string> onResult)
    {
        string apiKey = ChatConfig.GlmApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("[DualModelValidator] GLM API Key 未配置");
            onResult(0, "");
            yield break;
        }

        string prompt = BuildScorePrompt(description);

        string jsonBody = "{";
        jsonBody += "\"model\":\"" + EscapeJson(ChatConfig.GlmVisionModel) + "\",";
        jsonBody += "\"messages\":[{";
        jsonBody += "\"role\":\"user\",";
        jsonBody += "\"content\":[";
        jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJson(prompt) + "\"},";
        jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJson(imageDataUrl) + "\"}}";
        jsonBody += "]}],";
        jsonBody += "\"temperature\":0.1,";
        jsonBody += "\"max_tokens\":2048";
        jsonBody += "}";

        string url = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.timeout = 90;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string response = req.downloadHandler.text;
                Debug.Log($"[DualModelValidator] GLM 原始响应: {Truncate(response, 300)}");
                ParseScoreResponse(response, out int score, out string review);
                onResult(score, review);
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = TryExtractGlmError(errBody) ?? req.error;
                Debug.LogWarning($"[DualModelValidator] GLM 请求失败: {errMsg}");
                onResult(0, "");
            }
        }
    }



    // ==================================================================
    //  评分 Prompt 工厂
    // ==================================================================

    private static string BuildScorePrompt(string description)
    {
        return "你是一个严格的动画质量评审专家。下面是一张桌面宠物（符玄）的动作截图。\n\n"
            + "AI 被要求做出这个动作：**「" + description + "」**\n\n"
            + "请仔细观察截图中的角色姿态，然后回答：\n\n"
            + "=== 评分标准 ===\n"
            + "5分 = 完美还原，一看就是「" + description + "」\n"
            + "4分 = 基本到位，主要特征明显但有轻微偏差\n"
            + "3分 = 能看到设计意图，但整体不够准确\n"
            + "2分 = 只有一点点关联，需要仔细看才勉强联想到\n"
            + "1分 = 完全不像，看不出在做什么\n\n"
            + "=== 重要：回复必须以打分开头，然后才是理由 ===\n"
            + "回复格式（严格按此格式）：\n"
            + "打分：【X/5】\n"
            + "判断：【是/基本是/不太像/完全不像】\n"
            + "理由：...";
    }

    // ==================================================================
    //  响应解析
    // ==================================================================

    private void ParseScoreResponse(string responseJson, out int score, out string review)
    {
        score = 0;
        review = "";

        try
        {
            // 提取 content
            string content = ExtractContent(responseJson);
            if (string.IsNullOrEmpty(content))
            {
                Debug.LogWarning("[DualModelValidator] 响应中未找到 content");
                return;
            }

            review = content.Trim();

            // 尝试从 content 中提取 【X/5】
            var match = Regex.Match(content, @"【(\d+)/5】");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int s))
            {
                score = Mathf.Clamp(s, 1, 5);
                return;
            }

            // 截断兜底: content 末尾的 【X（被截断未闭合）
            match = Regex.Match(content, @"【(\d+)(?:/5】?)?\z");
            if (match.Success && int.TryParse(match.Groups[1].Value, out s))
            {
                score = Mathf.Clamp(s, 1, 5);
                return;
            }

            // 备选: X/5
            match = Regex.Match(content, @"(\d+)/5");
            if (match.Success && int.TryParse(match.Groups[1].Value, out s))
            {
                score = Mathf.Clamp(s, 1, 5);
                return;
            }

            Debug.LogWarning($"[DualModelValidator] 无法从响应中提取评分: {Truncate(content, 100)}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DualModelValidator] 解析响应异常: {e.Message}");
        }
    }

    /// <summary>从标准 OpenAI 响应 JSON 中提取 choices[0].message.content</summary>
    private static string ExtractContent(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        // 先定位 "choices" 数组，再找 message 里的 content（避免误中 reasoning_content）
        int choicesIdx = json.IndexOf("\"choices\"");
        if (choicesIdx < 0) return null;

        int messageIdx = json.IndexOf("\"message\"", choicesIdx + 9);
        // GLM-4V 可能没有 "message" 包装层（content 直接在 choice 下）
        int searchFrom = messageIdx >= 0 ? messageIdx + 9 : choicesIdx + 9;

        // 精确匹配 "content": 而非 "reasoning_content":
        // 用前驱字符检查：如果匹配到的是 reasoning_content，则 _content 的 _ 会在前面
        int contentIdx = json.IndexOf("\"content\":", searchFrom);
        while (contentIdx >= 0)
        {
            // 检查前一个字符：如果是 _ 则是 reasoning_content，跳过
            if (contentIdx > 0 && json[contentIdx - 1] == '_')
            {
                contentIdx = json.IndexOf("\"content\":", contentIdx + 9);
                continue;
            }
            break;
        }
        if (contentIdx < 0) return null;

        int colon = contentIdx + 9; // content": 的末尾
        if (colon < 0) return null;

        int start = colon + 1;
        while (start < json.Length && (json[start] == ' ' || json[start] == '"'))
        {
            if (json[start] == '"') { start++; break; }
            start++;
        }

        if (start >= json.Length) return null;

        // 找到闭合引号
        int end = start;
        bool esc = false;
        while (end < json.Length)
        {
            if (esc) { esc = false; end++; continue; }
            if (json[end] == '\\') { esc = true; end++; continue; }
            if (json[end] == '"') break;
            end++;
        }

        return json.Substring(start, end - start)
            .Replace("\\n", "\n")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Replace("\\t", "\t");
    }

    // ==================================================================
    //  数据提取
    // ==================================================================

    /// <summary>从 MotionPlan 中提取中间帧参数快照</summary>
    private static string ExtractParamSnapshot(MotionPlanner.MotionPlan plan)
    {
        if (plan == null || plan.KeyFrames == null || plan.KeyFrames.Count == 0)
            return "";

        // 取中间帧
        int midIdx = plan.KeyFrames.Count / 2;
        var midFrame = plan.KeyFrames[midIdx];
        if (midFrame.Values == null || midFrame.Values.Count == 0)
            return "";

        // 提取绝对值最大的前 5 个参数
        var topParams = midFrame.Values
            .OrderByDescending(kv => Mathf.Abs(kv.Value))
            .Take(5)
            .Select(kv => $"{kv.Key}={kv.Value:F2}")
            .ToList();

        return string.Join(", ", topParams);
    }

    // ==================================================================
    //  日志
    // ==================================================================

    private void LogValidation(string desc, int sGlm, int sAvg,
        bool consensus, string rGlm, float dur, int kfCount)
    {
        var entry = new ValidationEntry
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            actionDescription = desc,
            scoreGlm = sGlm,
            scoreAvg = sAvg,
            isConsensus = consensus,
            reviewGlm = Truncate(rGlm, 120),
            duration = dur,
            keyframeCount = kfCount
        };
        _log.entries.Add(entry);

        if (consensus)
        {
            Debug.Log($"[DualModelValidator] ✅ GLM 鉴通过: 「{desc}」 {sGlm}/5");
        }
        else
        {
            Debug.Log($"[DualModelValidator] ⚠️ GLM 鉴低分: 「{desc}」 {sGlm}/5");
        }

        SaveLog();
    }

    /// <summary>获取验证统计摘要</summary>
    public string GetStatistics()
    {
        var sb = new StringBuilder();
        int total = _log.entries.Count;
        int consensus = _log.entries.Count(e => e.isConsensus);
        int disagreements = total - consensus;
        float agreeRate = total > 0 ? (float)consensus / total * 100f : 0;

        sb.AppendLine("📊 GLM 镜鉴 — 验证统计");
        sb.AppendLine($"▸ 总验证次数: {total}");
        sb.AppendLine($"▸ 通过(Consensus): {consensus} ({agreeRate:F1}%)");
        sb.AppendLine($"▸ 低分: {disagreements}");

        if (total > 0)
        {
            float avgGlm = (float)_log.entries.Where(e => e.scoreGlm > 0).DefaultIfEmpty().Average(e => e.scoreGlm);
            sb.AppendLine($"▸ GLM-4V 平均分: {avgGlm:F2}");

            // 最近 5 条低分
            var recentFails = _log.entries
                .Where(e => !e.isConsensus && e.scoreGlm > 0)
                .TakeLast(5)
                .ToList();
            if (recentFails.Count > 0)
            {
                sb.AppendLine("\n【最近低分记录】");
                foreach (var e in recentFails)
                {
                    sb.AppendLine($"  ⚠️ 「{e.actionDescription}」 GLM={e.scoreGlm}/5");
                }
            }
        }

        return sb.ToString();
    }

    // ==================================================================
    //  持久化
    // ==================================================================

    private void SaveLog()
    {
        try
        {
            string json = JsonUtility.ToJson(_log, prettyPrint: true);
            File.WriteAllText(LogPath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DualModelValidator] 保存验证日志失败: {e.Message}");
        }
    }

    private void LoadLog()
    {
        try
        {
            if (File.Exists(LogPath))
            {
                string json = File.ReadAllText(LogPath);
                _log = JsonUtility.FromJson<ValidationStore>(json) ?? new ValidationStore();
                Debug.Log($"[DualModelValidator] 💾 已加载 {_log.entries.Count} 条验证记录");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DualModelValidator] 加载验证日志失败: {e.Message}");
            _log = new ValidationStore();
        }
    }

    // ==================================================================
    //  辅助
    // ==================================================================

    /// <summary>
    /// 将多帧 PNG 合成一张拼图，返回 base64 data URL 供 GLM-4V 视觉评分
    /// </summary>
    /// <param name="framePngs">各帧 PNG bytes（最多 4 帧）</param>
    /// <param name="cols">拼图列数（默认 2，2列×2行）</param>
    public static string ComposeCollage(List<byte[]> framePngs, int cols = 2)
    {
        if (framePngs == null || framePngs.Count == 0) return null;

        int count = Mathf.Min(framePngs.Count, 4);
        cols = Mathf.Min(cols, count);
        int rows = (count + cols - 1) / cols;

        // 加载第一帧获取帧尺寸
        var sampleTex = new Texture2D(2, 2);
        sampleTex.LoadImage(framePngs[0]);
        int fw = sampleTex.width;
        int fh = sampleTex.height;
        UnityEngine.Object.DestroyImmediate(sampleTex);

        // 创建拼图画布
        int cw = fw * cols;
        int ch = fh * rows;
        var canvas = new Texture2D(cw, ch, TextureFormat.RGB24, false);

        for (int i = 0; i < count; i++)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(framePngs[i]);
            int x = (i % cols) * fw;
            int y = ch - ((i / cols) + 1) * fh; // 从顶部往下排
            Color[] pixels = tex.GetPixels(0, 0, fw, fh);
            canvas.SetPixels(x, y, fw, fh, pixels);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        canvas.Apply();
        byte[] resultPng = canvas.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(canvas);

        return "data:image/png;base64," + Convert.ToBase64String(resultPng);
    }

    private static string TryExtractGlmError(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            int msgIdx = body.IndexOf("\"message\"");
            if (msgIdx < 0) return null;
            int colon = body.IndexOf(':', msgIdx + 9);
            if (colon < 0) return null;
            int start = body.IndexOf('"', colon + 1);
            if (start < 0) return null;
            start++;
            int end = body.IndexOf('"', start);
            if (end < 0) return null;
            return body.Substring(start, end - start);
        }
        catch { return null; }
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
    }
}
