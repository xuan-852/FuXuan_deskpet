using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 本地轻量 LLM 客户端 — 连接 Ollama (OpenAI-compatible API)
///
/// 用于 MotionAgent 的实时动作决策和聊天回退：动作/摘要使用轻量模型，
/// 聊天请求可通过独立 ChatModelName 使用质量更高的本地模型。
///
/// 兼容 OpenAI /chat/completions 格式：
///   POST http://localhost:11434/v1/chat/completions
///   {
///     "model": "qwen2.5:3b",
///     "messages": [...],
///     "temperature": 0.7,
///     "max_tokens": 128
///   }
/// </summary>
public static class LocalLLMClient
{
    private const string DEFAULT_BASE_URL = "http://127.0.0.1:11434/v1";
    private const int DEFAULT_TIMEOUT = 75;
    private const int MAX_RETRY = 2;

    /// <summary>
    /// 当前使用的 base URL（可通过 SetBaseUrl 修改）
    /// </summary>
    public static string BaseUrl { get; private set; } = DEFAULT_BASE_URL;

    /// <summary>
    /// 当前使用的模型名（可通过 SetModel 修改）
    /// MotionAgent 启动时会覆盖为 3b，这里设为 3b 作为默认值便于本地服务使用
    /// </summary>
    public static string ModelName { get; private set; } = ResolveConfiguredModel("qwen2.5:3b");

    /// <summary>聊天专用模型。未显式覆盖时使用已安装的 qwen3:8b，和动作/摘要模型分离。</summary>
    public static string ChatModelName { get; private set; } = ResolveConfiguredChatModel("qwen3:8b");

    /// <summary>
    /// 是否就绪（上次连接成功则 true）
    /// </summary>
    public static bool IsReady { get; private set; } = false;
    private static readonly HashSet<string> ReadyModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 是否被暂停（游戏检测暂停时设为 true，此时不发起新请求）
    /// </summary>
    public static bool Paused { get; set; } = false;

    /// <summary>
    /// 最后一次检查的结果描述
    /// </summary>
    public static string LastHealthMessage { get; private set; } = "";

    private static DateTime _lastOllamaLaunchAttemptUtc = DateTime.MinValue;
    private static readonly TimeSpan OllamaLaunchCooldown = TimeSpan.FromSeconds(60);

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
        string previousModel = ModelName;
        ModelName = model;
        IsReady = false;
        ReadyModels.Remove(previousModel ?? "");
        ReadyModels.Remove(model ?? "");
    }

    /// <summary>
    /// 设置聊天专用模型。聊天和动作模型分开切换，避免为了提高对话质量而抬高动作循环的资源占用。
    /// </summary>
    public static void SetChatModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        string previousModel = ChatModelName;
        ChatModelName = model.Trim();
        ReadyModels.Remove(previousModel ?? "");
        ReadyModels.Remove(ChatModelName);
    }

    /// <summary>
    /// 返回当前进程配置的本地模型。
    /// FU_XUAN_LOCAL_MODEL 只用于实验/部署覆盖，未设置时保持生产默认模型。
    /// </summary>
    public static string ResolveConfiguredModel(string fallback)
    {
        string configured = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_MODEL");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        return fallback;
    }

    /// <summary>
    /// 解析聊天模型：专用环境变量优先，其次沿用全局本地模型覆盖，最后使用聊天质量默认值。
    /// </summary>
    public static string ResolveConfiguredChatModel(string fallback)
    {
        string configured = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_CHAT_MODEL");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        configured = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_MODEL");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        return fallback;
    }

    public static bool IsModelReady(string model)
    {
        return !string.IsNullOrWhiteSpace(model) && ReadyModels.Contains(model);
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
    public static IEnumerator CheckHealthAsync(Action<bool, string> onResult, string modelOverride = null)
    {
        string modelToCheck = string.IsNullOrWhiteSpace(modelOverride) ? ModelName : modelOverride;
        bool firstOk = false;
        bool firstServiceReachable = false;
        string firstMessage = "";

        yield return CheckHealthOnce(modelToCheck, (ok, message, serviceReachable) =>
        {
            firstOk = ok;
            firstServiceReachable = serviceReachable;
            firstMessage = message ?? "";
        });

        if (firstOk || firstServiceReachable)
        {
            onResult?.Invoke(firstOk, firstMessage);
            yield break;
        }

        string launchMessage;
        if (!TryStartOllama(out launchMessage))
        {
            onResult?.Invoke(false, firstMessage);
            yield break;
        }

        Debug.LogWarning($"[LocalLLMClient] {launchMessage}，等待本地 API 就绪后重试");
        yield return new WaitForSecondsRealtime(2f);

        bool retryOk = false;
        string retryMessage = "";
        yield return CheckHealthOnce(modelToCheck, (ok, message, _) =>
        {
            retryOk = ok;
            retryMessage = message ?? "";
        });

        if (!retryOk && !string.IsNullOrEmpty(launchMessage))
            retryMessage = $"{retryMessage}；{launchMessage}";
        onResult?.Invoke(retryOk, retryMessage);
    }

    private static IEnumerator CheckHealthOnce(
        string modelToCheck,
        Action<bool, string, bool> onResult)
    {
        string url = BaseUrl.Replace("/v1", "") + "/api/tags";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                // Ollama service availability is not model availability.
                // The exact configured model must be present before requests are allowed.
                string body = req.downloadHandler.text;
                // 检查模型是否存在
                bool modelFound = body.Contains(modelToCheck) || body.Contains(modelToCheck.Replace(":latest", ""));
                ReadyModels.RemoveWhere(m => string.Equals(m, modelToCheck, StringComparison.OrdinalIgnoreCase));
                if (modelFound) ReadyModels.Add(modelToCheck);
                if (string.Equals(modelToCheck, ModelName, StringComparison.OrdinalIgnoreCase))
                    IsReady = modelFound;
                string msg = modelFound
                    ? $"✅ 本地 LLM 就绪（{modelToCheck}）"
                    : $"⚠️ Ollama 在线，但模型「{modelToCheck}」未找到，需运行: ollama pull {modelToCheck}";
                LastHealthMessage = msg;
                onResult?.Invoke(modelFound, msg, true);
            }
            else
            {
                if (string.Equals(modelToCheck, ModelName, StringComparison.OrdinalIgnoreCase))
                    IsReady = false;
                string err = $"❌ 本地 LLM 不可达: {req.error}（请确保 Ollama 已启动）";
                LastHealthMessage = err;
                onResult?.Invoke(false, err, false);
            }
        }
    }

    /// <summary>
    /// 在桌宠先于 Ollama 启动时拉起本机 Ollama。
    /// 测试模式和 Unity Editor 禁止启动真实外部进程。
    /// </summary>
    private static bool TryStartOllama(out string message)
    {
        message = "";
        if (Application.isEditor || ChatManager.IsTestMode)
            return false;

        DateTime now = DateTime.UtcNow;
        if (now - _lastOllamaLaunchAttemptUtc < OllamaLaunchCooldown)
            return false;
        _lastOllamaLaunchAttemptUtc = now;

        string executablePath = FindOllamaExecutable();
        if (string.IsNullOrEmpty(executablePath))
        {
            message = "未找到 Ollama 可执行文件";
            return false;
        }

        bool isApp = executablePath.EndsWith("ollama app.exe", StringComparison.OrdinalIgnoreCase);
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = System.IO.Path.GetDirectoryName(executablePath),
                UseShellExecute = isApp,
                CreateNoWindow = !isApp,
                WindowStyle = isApp
                    ? System.Diagnostics.ProcessWindowStyle.Minimized
                    : System.Diagnostics.ProcessWindowStyle.Hidden
            };
            if (!isApp)
                startInfo.Arguments = "serve";

            System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
            process?.Dispose();
            message = isApp ? "已启动 Ollama 应用" : "已启动 Ollama serve";
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LocalLLMClient] 启动 Ollama 失败: {ex.Message}");
            message = "启动 Ollama 失败";
            return false;
        }
    }

    private static string FindOllamaExecutable()
    {
        string configured = Environment.GetEnvironmentVariable("OLLAMA_EXE");
        string[] candidates =
        {
            configured,
            CombineIfSet(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "Programs", "Ollama", "ollama app.exe"),
            CombineIfSet(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "Programs", "Ollama", "ollama.exe"),
            CombineIfSet(Environment.GetEnvironmentVariable("ProgramFiles"), "Ollama", "ollama app.exe"),
            CombineIfSet(Environment.GetEnvironmentVariable("ProgramFiles"), "Ollama", "ollama.exe")
        };

        foreach (string candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && System.IO.File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string CombineIfSet(string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        string path = root;
        foreach (string part in parts)
            path = System.IO.Path.Combine(path, part);
        return path;
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
        int timeout = DEFAULT_TIMEOUT,
        string modelOverride = null)
    {
        // 被游戏模式暂停时直接跳过
        if (Paused)
        {
            onResult?.Invoke(false, "本地 LLM 已暂停（游戏模式）");
            yield break;
        }

        string requestModel = string.IsNullOrWhiteSpace(modelOverride) ? ModelName : modelOverride;
        if (!IsModelReady(requestModel))
        {
            // 首次使用前先做健康检查
            bool healthOk = false;
            yield return CheckHealthAsync((ok, _) => healthOk = ok, requestModel);
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

            string jsonBody = BuildChatRequestBody(messages, temperature, maxTokens, requestModel);
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
                        // ★ 2026-08-16：本地调用也记录（免费，供消耗对比分析）
                        UsageLogger.Record("local", requestModel, 0, 0, 0, 0);
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
                        ReadyModels.Remove(requestModel);
                        if (string.Equals(requestModel, ModelName, StringComparison.OrdinalIgnoreCase))
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
        int maxTokens = 256,
        int timeout = DEFAULT_TIMEOUT,
        string modelOverride = null)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage { role = "system", content = systemPrompt },
            new ChatMessage { role = "user", content = userPrompt }
        };
        return ChatAsync(messages, onResult, temperature, maxTokens, timeout, modelOverride);
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

    private static string BuildChatRequestBody(List<ChatMessage> messages, float temperature, int maxTokens, string modelName)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"model\":\"").Append(EscapeJson(modelName)).Append("\",");
        sb.Append("\"temperature\":").Append(temperature.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"max_tokens\":").Append(maxTokens).Append(',');
        // Qwen3 等推理模型在 Ollama /v1 兼容接口默认会开启 thinking。
        // 日常桌宠回复需要把预算留给最终 content；复杂任务可通过环境变量重新开启。
        string reasoningEffort = ResolveReasoningEffort(modelName);
        if (!string.IsNullOrEmpty(reasoningEffort))
            sb.Append("\"reasoning_effort\":\"").Append(EscapeJson(reasoningEffort)).Append("\",");
        // ★ 2026-08-15：显式扩展上下文窗口到 8K——动作翻译/闲话 prompt 较大（schema 可达 5K 字符），
        //   默认 4K 上下文会截断导致本地模型 JSON 解析失败回退云端。Ollama 支持按请求覆盖 num_ctx。
        sb.Append("\"options\":{\"num_ctx\":8192},");
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

    /// <summary>
    /// 解析本地模型的推理开关。
    /// FU_XUAN_LOCAL_REASONING_EFFORT 支持 none/low/medium/high/max；
    /// 未设置时仅对 qwen3 系列默认关闭，保持 qwen2.5 等非推理模型原行为。
    /// </summary>
    private static string ResolveReasoningEffort(string modelName)
    {
        string configured = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_REASONING_EFFORT");
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!modelName.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase))
                return null;
            return "none";
        }

        string value = configured.Trim().ToLowerInvariant();
        return value == "none" || value == "low" || value == "medium"
            || value == "high" || value == "max" ? value : null;
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
