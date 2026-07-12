using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 本地轻量 LLM 客户端 — 连接 Ollama (OpenAI-compatible API)
///
/// 用于 MotionAgent 的实时动作决策，调用本地小模型（如 Qwen2.5-0.5B）
/// 替代每次都走云端 DeepSeek，实现低延迟、无网络的常驻动作控制。
///
/// 兼容 OpenAI /chat/completions 格式：
///   POST http://localhost:11434/v1/chat/completions
///   {
///     "model": "qwen2.5:0.5b",
///     "messages": [...],
///     "temperature": 0.7,
///     "max_tokens": 128
///   }
/// </summary>
public static class LocalLLMClient
{
    private const string DEFAULT_BASE_URL = "http://127.0.0.1:11434/v1";
    private const int DEFAULT_TIMEOUT = 60;
    private const int MAX_RETRY = 2;

    /// <summary>
    /// 当前使用的 base URL（可通过 SetBaseUrl 修改）
    /// </summary>
    public static string BaseUrl { get; private set; } = DEFAULT_BASE_URL;

    /// <summary>
    /// 当前使用的模型名（可通过 SetModel 修改）
    /// </summary>
    public static string ModelName { get; private set; } = "qwen2.5:0.5b";

    /// <summary>
    /// 是否就绪（上次连接成功则 true）
    /// </summary>
    public static bool IsReady { get; private set; } = false;

    /// <summary>
    /// 最后一次检查的结果描述
    /// </summary>
    public static string LastHealthMessage { get; private set; } = "";

    /// <summary>
    /// 设置 API 地址
    /// </summary>
    public static void SetBaseUrl(string url)
    {
        BaseUrl = url.TrimEnd('/');
        if (!BaseUrl.EndsWith("/v1"))
            BaseUrl += "/v1";
    }

    /// <summary>
    /// 设置模型名
    /// </summary>
    public static void SetModel(string model)
    {
        ModelName = model;
    }

    /// <summary>
    /// 重置就绪状态（连接失败时调用）
    /// </summary>
    public static void MarkUnready()
    {
        IsReady = false;
    }

    // ──────────────────────────────────────────────
    //  健康检查
    // ──────────────────────────────────────────────

    /// <summary>检测本地 LLM 是否可用（协程）</summary>
    public static IEnumerator CheckHealthAsync(Action<bool, string> onResult)
    {
        string url = BaseUrl.Replace("/v1", "") + "/api/tags";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                IsReady = true;
                string body = req.downloadHandler.text;
                // 检查模型是否存在
                bool modelFound = body.Contains(ModelName) || body.Contains(ModelName.Replace(":latest", ""));
                string msg = modelFound
                    ? $"✅ 本地 LLM 就绪（{ModelName}）"
                    : $"⚠️ Ollama 在线，但模型「{ModelName}」未找到，需运行: ollama pull {ModelName}";
                LastHealthMessage = msg;
                onResult?.Invoke(modelFound, msg);
            }
            else
            {
                IsReady = false;
                string err = $"❌ 本地 LLM 不可达: {req.error}（请确保 Ollama 已启动）";
                LastHealthMessage = err;
                onResult?.Invoke(false, err);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  核心请求
    // ──────────────────────────────────────────────

    /// <summary>
    /// 发送聊天请求到本地 LLM（协程，支持重试）
    /// </summary>
    /// <param name="messages">OpenAI 格式消息列表</param>
    /// <param name="onResult">完成回调（success, content）</param>
    /// <param name="temperature">采样温度</param>
    /// <param name="maxTokens">最大 token 数</param>
    /// <param name="timeout">超时秒数</param>
    public static IEnumerator ChatAsync(
        List<ChatMessage> messages,
        Action<bool, string> onResult,
        float temperature = 0.7f,
        int maxTokens = 256,
        int timeout = DEFAULT_TIMEOUT)
    {
        if (!IsReady)
        {
            // 首次使用前先做健康检查
            bool healthOk = false;
            yield return CheckHealthAsync((ok, _) => healthOk = ok);
            if (!healthOk)
            {
                onResult?.Invoke(false, "本地 LLM 未就绪");
                yield break;
            }
        }

        string content = "";
        bool success = false;

        for (int retry = 0; retry <= MAX_RETRY; retry++)
        {
            if (retry > 0)
                yield return new WaitForSeconds(0.5f);

            string jsonBody = BuildChatRequestBody(messages, temperature, maxTokens);
            string url = BaseUrl + "/chat/completions";

            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = timeout;

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string responseText = req.downloadHandler.text;
                    string extracted = ExtractContent(responseText);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        content = extracted;
                        success = true;
                        break;
                    }
                    Debug.LogWarning($"[LocalLLM] 解析响应为空 (retry={retry}): {StringTruncateExtension.Truncate(responseText, 100)}");
                }
                else
                {
                    string errBody = req.downloadHandler?.text ?? "";
                    string errMsg = ExtractErrorMessage(errBody) ?? req.error;
                    Debug.LogWarning($"[LocalLLM] 请求失败 (retry={retry}): {errMsg}");
                    if (retry >= MAX_RETRY)
                    {
                        // 连续失败 → 标记不可用
                        IsReady = false;
                        content = errMsg;
                    }
                }
            }
        }

        onResult?.Invoke(success, content);
    }

    // ──────────────────────────────────────────────
    //  简化接口：单一 system + user prompt
    // ──────────────────────────────────────────────

    /// <summary>发送一轮 system+user 对话（最常用）</summary>
    public static IEnumerator PromptAsync(
        string systemPrompt,
        string userPrompt,
        Action<bool, string> onResult,
        float temperature = 0.7f,
        int maxTokens = 256)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage { role = "system", content = systemPrompt },
            new ChatMessage { role = "user", content = userPrompt }
        };
        return ChatAsync(messages, onResult, temperature, maxTokens);
    }

    /// <summary>仅发送 user prompt（无 system）</summary>
    public static IEnumerator SimplePromptAsync(
        string prompt,
        Action<bool, string> onResult,
        float temperature = 0.7f,
        int maxTokens = 256)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage { role = "user", content = prompt }
        };
        return ChatAsync(messages, onResult, temperature, maxTokens);
    }

    // ──────────────────────────────────────────────
    //  消息模型
    // ──────────────────────────────────────────────

    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;
    }

    // ──────────────────────────────────────────────
    //  请求构建
    // ──────────────────────────────────────────────

    private static string BuildChatRequestBody(List<ChatMessage> messages, float temperature, int maxTokens)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"model\":\"").Append(EscapeJson(ModelName)).Append("\",");
        sb.Append("\"temperature\":").Append(temperature.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"max_tokens\":").Append(maxTokens).Append(',');
        sb.Append("\"messages\":[");
        for (int i = 0; i < messages.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var m = messages[i];
            sb.Append("{\"role\":\"");
            sb.Append(EscapeJson(m.role));
            sb.Append("\",\"content\":\"");
            sb.Append(EscapeJson(m.content));
            sb.Append("\"}");
        }
        sb.Append("]");
        sb.Append('}');
        return sb.ToString();
    }

    // ──────────────────────────────────────────────
    //  响应解析
    // ──────────────────────────────────────────────

    /// <summary>从 OpenAI 响应中提取 content 字段</summary>
    private static string ExtractContent(string responseJson)
    {
        try
        {
            // 定位到 choices[0].message.content
            int choicesIdx = responseJson.IndexOf("\"choices\"");
            if (choicesIdx < 0) return null;

            int msgIdx = responseJson.IndexOf("\"message\"", choicesIdx);
            if (msgIdx < 0) return null;

            int contentIdx = responseJson.IndexOf("\"content\"", msgIdx);
            if (contentIdx < 0) return null;

            int colon = responseJson.IndexOf(':', contentIdx + 9);
            if (colon < 0) return null;

            int valStart = colon + 1;
            while (valStart < responseJson.Length && responseJson[valStart] == ' ') valStart++;
            if (valStart >= responseJson.Length || responseJson[valStart] != '"') return null;

            valStart++;
            int valEnd = valStart;
            bool escaped = false;
            while (valEnd < responseJson.Length)
            {
                if (escaped) { escaped = false; valEnd++; continue; }
                if (responseJson[valEnd] == '\\') { escaped = true; valEnd++; continue; }
                if (responseJson[valEnd] == '"') break;
                valEnd++;
            }
            if (valEnd >= responseJson.Length) return null;

            string raw = responseJson.Substring(valStart, valEnd - valStart);
            return raw.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\\", "\\");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LocalLLM] 解析响应异常: {e.Message}");
            return null;
        }
    }

    /// <summary>从错误响应中提取 error.message</summary>
    private static string ExtractErrorMessage(string errorJson)
    {
        try
        {
            int errIdx = errorJson.IndexOf("\"error\"");
            if (errIdx < 0) return null;
            int msgIdx = errorJson.IndexOf("\"message\"", errIdx);
            if (msgIdx < 0) return null;
            int colon = errorJson.IndexOf(':', msgIdx + 9);
            if (colon < 0) return null;
            int start = colon + 1;
            while (start < errorJson.Length && errorJson[start] == ' ') start++;
            if (start >= errorJson.Length || errorJson[start] != '"') return null;
            start++;
            int end = start;
            bool esc = false;
            while (end < errorJson.Length)
            {
                if (esc) { esc = false; end++; continue; }
                if (errorJson[end] == '\\') { esc = true; end++; continue; }
                if (errorJson[end] == '"') break;
                end++;
            }
            if (end >= errorJson.Length) return null;
            return errorJson.Substring(start, end - start).Replace("\\\"", "\"");
        }
        catch { return null; }
    }

    /// <summary>JSON 转义（简化版）</summary>
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
