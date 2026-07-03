using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

/// <summary>
/// 共享 API 客户端 — 统一的 HTTP 请求与 JSON 解析工具
///
/// ChatManager 和 IdleChatGenerator 中原先各自维护了一套近乎相同的
/// PostRequest / EscapeJson / ExtractContent / ExtractErrorMessage，
/// 现提取至此处集中管理，减少重复代码。
///
/// 用法:
///   yield return ApiClient.PostRequest(url, key, json, timeout,
///       json => { /* 成功 */ },
///       err => { /* 失败 */ });
/// </summary>
public static class ApiClient
{
    /// <summary>
    /// 发送 POST 请求到 OpenAI 兼容的 /v1/chat/completions 端点
    /// </summary>
    /// <param name="baseUrl">API 基础地址，例如 "https://api.deepseek.com"</param>
    /// <param name="apiKey">Bearer token，为空则不传 Authorization 头</param>
    /// <param name="jsonBody">请求体 JSON</param>
    /// <param name="timeout">超时秒数</param>
    /// <param name="onSuccess">HTTP 200 时回调，参数为完整响应 JSON</param>
    /// <param name="onError">失败时回调，参数为人类可读错误描述</param>
    public static IEnumerator PostRequest(
        string baseUrl, string apiKey, string jsonBody, int timeout,
        Action<string> onSuccess, Action<string> onError)
    {
        string fullUrl = baseUrl.TrimEnd('/') + "/v1/chat/completions";

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = timeout;

            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(req.downloadHandler.text);
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = !string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\"")
                    ? ExtractErrorMessage(errBody)
                    : req.error;
                onError?.Invoke(errMsg);
            }
        }
    }

    // ================================================================
    //  JSON 转义
    // ================================================================

    /// <summary>转义字符串中的特殊字符，使其可嵌入 JSON 字符串值中</summary>
    public static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
    }

    // ================================================================
    //  JSON 解析
    // ================================================================

    /// <summary>从 DeepSeek/OpenAI 兼容响应中提取 content 字段文本</summary>
    public static string ExtractContent(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";

        // 从 messages 数组中找 content
        int msgIdx = json.IndexOf("\"message\"");
        if (msgIdx < 0) return "";

        string key = "\"content\":\"";
        int idx = json.IndexOf(key, msgIdx);
        if (idx < 0)
        {
            // content 可能为 null（纯 tool_call 回复）
            if (json.IndexOf("\"content\":null", msgIdx) >= 0)
                return "";
            return "";
        }

        idx += key.Length;
        var content = new StringBuilder();
        for (int i = idx; i < json.Length; i++)
        {
            if (json[i] == '\\' && i + 1 < json.Length)
            {
                char next = json[i + 1];
                switch (next)
                {
                    case 'n': content.Append('\n'); i++; break;
                    case 't': content.Append('\t'); i++; break;
                    case '"': content.Append('"');  i++; break;
                    case '\\':content.Append('\\'); i++; break;
                    case 'r': i++; break;
                    default:  content.Append(json[i]); break;
                }
            }
            else if (json[i] == '"')
            {
                break;
            }
            else
            {
                content.Append(json[i]);
            }
        }
        return content.ToString();
    }

    /// <summary>从错误响应 JSON 中提取 message 字段（人类可读错误描述）</summary>
    public static string ExtractErrorMessage(string json)
    {
        if (string.IsNullOrEmpty(json)) return "未知错误";

        string key = "\"message\":\"";
        int idx = json.IndexOf(key);
        if (idx < 0) return json; // 回退：返回原始 JSON

        idx += key.Length;
        var msg = new StringBuilder();
        for (int i = idx; i < json.Length; i++)
        {
            if (json[i] == '"') break;
            msg.Append(json[i]);
        }
        return msg.ToString();
    }

    /// <summary>从 JSON 中提取 "key":"value" 中的 value 纯字符串</summary>
    public static string ExtractSimpleString(string json, int start)
    {
        if (start >= json.Length) return "";
        var sb = new StringBuilder();
        for (int i = start; i < json.Length; i++)
        {
            if (json[i] == '\\' && i + 1 < json.Length)
            {
                char n = json[i + 1];
                if (n == '"')  { sb.Append('"'); i++; }
                else if (n == '\\') { sb.Append('\\'); i++; }
                else if (n == 'n')  { sb.Append('\n'); i++; }
                else if (n == 't')  { sb.Append('\t'); i++; }
                else if (n == 'r')  { i++; }
                else sb.Append(json[i]);
            }
            else if (json[i] == '"')
            {
                break;
            }
            else
            {
                sb.Append(json[i]);
            }
        }
        return sb.ToString();
    }
}
