using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 符玄「演武录」— 自主动作采集与评分引擎
///
/// 自动增强 MotionMemoryManager 的数据飞轮：
/// 1. 监听 MotionAgent 的每次动作执行
/// 2. 在动作峰值时刻（~50%进度）截取模型截图
/// 3. 调用 GLM-4V 视觉模型评分 (1-5)
/// 4. 存入 MotionMemoryManager
///
/// 这样 MotionAgent 的每次自主动作都会自动变成训练数据，
/// 无需 ChatManager 介入即可持续进化 MotionMemory。
/// </summary>
public class AutoMotionCollector : MonoBehaviour
{
    [Header("采集配置")]
    [Tooltip("是否启用自动采集")]
    public bool enabled = true;

    [Tooltip("动作峰值截图时机（进度比率，0~1）")]
    public float peakCaptureProgress = 0.5f;

    [Tooltip("最小动作时长才采集（秒）")]
    public float minActionDuration = 2f;

    [Tooltip("连续采集冷却（秒，防刷屏）")]
    public float captureCooldown = 5f;

    [Header("评分配置")]
    [Tooltip("最低通过分，低于此分不保存最佳模板")]
    public int minPassScore = 2;

    // ==================================================================
    //  运行时状态
    // ==================================================================

    private Live2DRenderer _renderer;
    private MotionMemoryManager _memoryManager;
    private float _lastCaptureTime = 0f;

    /// <summary>待采集队列</summary>
    private readonly Queue<string> _pendingActions = new Queue<string>();

    /// <summary>单例</summary>
    public static AutoMotionCollector Instance { get; private set; }

    // ==================================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
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
    /// 通知采集器：动作即将执行
    /// MotionAgent 在每次执行动作前调用此方法
    /// </summary>
    public void NotifyMotionStart(string actionDescription, float duration)
    {
        if (!enabled) return;
        if (duration < minActionDuration) return;
        if (Time.time - _lastCaptureTime < captureCooldown) return;

        _pendingActions.Enqueue(actionDescription);
        Debug.Log($"[AutoMotionCollector] 已入列待采集: 「{actionDescription}」");
    }

    /// <summary>
    /// 转为协程采集流程：在动作播放到 peakProgress 时截图→评分→存储
    /// 由 MotionAgent 在播放协程中调用（替代 NotifyMotionStart 的简化方式）
    /// </summary>
    /// <param name="actionDescription">动作描述</param>
    /// <param name="totalDuration">动作总时长</param>
    /// <param name="getProgress">获取当前进度的函数</param>
    public IEnumerator CollectCoroutine(
        string actionDescription,
        float totalDuration,
        Func<float> getProgress)
    {
        if (!enabled) yield break;
        if (totalDuration < minActionDuration) yield break;
        if (Time.time - _lastCaptureTime < captureCooldown) yield break;

        if (_renderer == null) yield break;
        if (_memoryManager == null) yield break;

        // 等待峰值时刻
        float targetProgress = peakCaptureProgress;
        while (true)
        {
            float progress = getProgress?.Invoke() ?? 0f;
            if (progress >= targetProgress) break;
            yield return null;
        }

        // 小延迟让画面稳定
        yield return new WaitForSeconds(0.1f);

        // 截图
        byte[] screenshot = _renderer.CaptureModelSnapshot();
        if (screenshot == null)
        {
            Debug.LogWarning("[AutoMotionCollector] 截图失败");
            yield break;
        }

        _lastCaptureTime = Time.time;

        // 记录到 MotionMemoryManager（先记录，评分后更新）
        _memoryManager.RecordMotion(
            actionDescription,
            $"auto_collect_{DateTime.Now:HHmmss}",
            0,
            totalDuration);

        // 通过 GLM-4V 评分
        string dataUrl = "data:image/png;base64," + Convert.ToBase64String(screenshot);
        string review = "";
        int score = 0;

        yield return EvaluateWithGlm(actionDescription, dataUrl, (s, r) => { score = s; review = r; });

        if (score > 0)
        {
            string paramSnapshot = $"auto_score={score}:{DateTime.Now:HHmmss}";
            bool isNewBest = _memoryManager.UpdateScore(actionDescription, score, review, paramSnapshot);
            Debug.Log($"[AutoMotionCollector] 📊 「{actionDescription}」→ GLM评分 {score}/5 {(isNewBest ? "🏆新纪录!" : "")}");
        }
    }

    // ==================================================================
    //  GLM-4V 评分
    // ==================================================================

    private const string GLM_API_URL = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
    private const string GLM_MODEL = "glm-4v-flash"; // 使用 flash 版本节约额度
    private const int GLM_TIMEOUT = 60;

    private IEnumerator EvaluateWithGlm(
        string description,
        string imageDataUrl,
        Action<int, string> onResult)
    {
        string apiKey = ChatConfig.GlmApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("[AutoMotionCollector] GLM API Key 未配置，跳过评分");
            onResult(0, "");
            yield break;
        }

        string systemPrompt =
            "你是一个严格的动画质量评审官。给定一个动作描述和对应的截图，请从以下三个维度评分(1-5分):\n" +
            "1. 姿态准确度(Pose Accuracy): 角色姿态是否符合动作描述\n" +
            "2. 幅度适当性(Amplitude): 动作幅度是否够大、清晰可见\n" +
            "3. 整体协调性(Coordination): 姿态是否自然协调\n\n" +
            "评分标准:\n" +
            "5=完美, 4=良好, 3=一般, 2=较差(轻微可见但不明显), 1=几乎看不出\n\n" +
            "输出格式(JSON ONLY):\n" +
            "{\"score\": 4, \"review\": \"姿态准确，但嘴巴可以再张开一点\"}";

        string userPrompt =
            "动作描述: " + description + "\n" +
            "请根据截图评分（1-5分）:\n" +
            "JSON输出:";

        // 构建多模态请求 JSON
        string jsonBody = BuildMultimodalRequest(systemPrompt, userPrompt, imageDataUrl);

        using (UnityWebRequest req = new UnityWebRequest(GLM_API_URL, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.timeout = GLM_TIMEOUT;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string responseText = req.downloadHandler.text;
                ParseScoreResponse(responseText, out int score, out string review);
                onResult(score, review);
            }
            else
            {
                Debug.LogWarning($"[AutoMotionCollector] GLM 评分请求失败: {req.error}");
                onResult(0, "");
            }
        }
    }

    private string BuildMultimodalRequest(string systemPrompt, string userPrompt, string imageDataUrl)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"model\":\"").Append(GLM_MODEL).Append("\",");
        sb.Append("\"messages\":[");
        // system
        sb.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJson(systemPrompt)).Append("\"},");
        // user (multimodal)
        sb.Append("{\"role\":\"user\",\"content\":[");
        sb.Append("{\"type\":\"text\",\"text\":\"").Append(EscapeJson(userPrompt)).Append("\"},");
        sb.Append("{\"type\":\"image_url\",\"image_url\":{\"url\":\"").Append(imageDataUrl).Append("\"}}");
        sb.Append("]}");
        sb.Append("]");
        sb.Append('}');
        return sb.ToString();
    }

    private void ParseScoreResponse(string responseJson, out int score, out string review)
    {
        score = 0;
        review = "";

        try
        {
            // 提取 content
            string content = null;
            int choicesIdx = responseJson.IndexOf("\"choices\"");
            if (choicesIdx >= 0)
            {
                int msgIdx = responseJson.IndexOf("\"message\"", choicesIdx);
                if (msgIdx >= 0)
                {
                    int contentIdx = responseJson.IndexOf("\"content\"", msgIdx);
                    if (contentIdx >= 0)
                    {
                        int colon = responseJson.IndexOf(':', contentIdx + 9);
                        if (colon >= 0)
                        {
                            int start = colon + 1;
                            while (start < responseJson.Length && responseJson[start] == ' ') start++;
                            if (start < responseJson.Length && responseJson[start] == '"')
                            {
                                start++;
                                int end = start;
                                bool esc = false;
                                while (end < responseJson.Length)
                                {
                                    if (esc) { esc = false; end++; continue; }
                                    if (responseJson[end] == '\\') { esc = true; end++; continue; }
                                    if (responseJson[end] == '"') break;
                                    end++;
                                }
                                if (end < responseJson.Length)
                                    content = responseJson.Substring(start, end - start)
                                        .Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r");
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(content)) return;

            // 解析 JSON 格式评分
            string clean = content.Trim();
            if (clean.StartsWith("```json")) clean = clean.Substring(7).Trim();
            else if (clean.StartsWith("```")) clean = clean.Substring(3).Trim();
            if (clean.EndsWith("```")) clean = clean.Substring(0, clean.Length - 3).Trim();

            int braceStart = clean.IndexOf('{');
            int braceEnd = clean.LastIndexOf('}');
            if (braceStart < 0 || braceEnd <= braceStart)
            {
                // 纯数字评分（回退）
                TryExtractScore(clean, out score, out review);
                return;
            }

            string json = clean.Substring(braceStart, braceEnd - braceStart + 1);

            // 提取 score
            int scoreIdx = json.IndexOf("\"score\"");
            if (scoreIdx >= 0)
            {
                int colon = json.IndexOf(':', scoreIdx + 7);
                if (colon >= 0)
                {
                    int s = colon + 1;
                    while (s < json.Length && json[s] == ' ') s++;
                    int e = s;
                    while (e < json.Length && char.IsDigit(json[e])) e++;
                    if (e > s)
                        int.TryParse(json.Substring(s, e - s), out score);
                }
            }

            // 提取 review
            int revIdx = json.IndexOf("\"review\"");
            if (revIdx >= 0)
            {
                int colon = json.IndexOf(':', revIdx + 8);
                if (colon >= 0)
                {
                    int start = colon + 1;
                    while (start < json.Length && json[start] == ' ') start++;
                    if (start < json.Length && json[start] == '"')
                    {
                        start++;
                        int end = start;
                        bool esc = false;
                        while (end < json.Length)
                        {
                            if (esc) { esc = false; end++; continue; }
                            if (json[end] == '\\') { esc = true; end++; continue; }
                            if (json[end] == '"') break;
                            end++;
                        }
                        if (end < json.Length)
                            review = json.Substring(start, end - start).Replace("\\\"", "\"");
                    }
                }
            }

            score = Mathf.Clamp(score, 1, 5);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AutoMotionCollector] 解析评分异常: {e.Message}");
        }
    }

    private void TryExtractScore(string text, out int score, out string review)
    {
        score = 0;
        review = text;
        // 尝试匹配 X/5 格式
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s*/\s*5");
        if (match.Success)
            int.TryParse(match.Groups[1].Value, out score);
        else
        {
            // 尝试匹配 "score: X" 格式
            match = System.Text.RegularExpressions.Regex.Match(text, @"score[:\s]+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                int.TryParse(match.Groups[1].Value, out score);
        }
        score = Mathf.Clamp(score, 0, 5);
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }
}
