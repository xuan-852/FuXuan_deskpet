using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

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

    // ================================================================
    //  SSE 流式请求 — 逐 token 接收，边收边显示句子
    // ================================================================

    /// <summary>
    /// 自定义 DownloadHandler — 实时缓存原始响应数据，供轮询读取
    /// </summary>
    public class SSEDownloadHandler : DownloadHandlerScript
    {
        private readonly StringBuilder _buffer = new StringBuilder();

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength == 0) return false;
            _buffer.Append(Encoding.UTF8.GetString(data, 0, dataLength));
            return true;
        }

        /// <summary>读取并清空缓冲区</summary>
        public string Drain()
        {
            string s = _buffer.ToString();
            _buffer.Clear();
            return s;
        }

        public string Peek() => _buffer.ToString();
    }

    /// <summary>一条 SSE 数据行的解析结果</summary>
    public struct StreamDelta
    {
        public string content;       // delta.content（可能为 null）
        public string toolCallId;    // tool_call id（新增 tool_call 时有值）
        public string toolName;      // function.name
        public string toolArgsPart;  // function.arguments 片段
    }

    /// <summary>SSE 流式请求 — 实时接收 token，边收边回调</summary>
    public static IEnumerator StreamRequest(
        string baseUrl, string apiKey, string jsonBody, int timeout,
        Action<string> onContentDelta,   // 每段 content delta
        Action<string, string> onFinish, // (fullContent, toolCallsJsonOrNull)
        Action<string> onError)
    {
        string fullUrl = baseUrl.TrimEnd('/') + "/v1/chat/completions";
        var handler = new SSEDownloadHandler();
        string errorMsg = null;

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = handler;
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = timeout;
            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            var asyncOp = req.SendWebRequest();

            // SSE 解析状态
            var lineBuf = new StringBuilder();
            string accumulatedContent = "";
            var toolCallAcc = new List<ToolCallAccumulator>();

            while (!asyncOp.isDone)
            {
                string raw = handler.Drain();
                if (raw.Length > 0)
                    ProcessStreamData(raw, lineBuf, ref accumulatedContent, toolCallAcc, onContentDelta);
                yield return null;
            }

            // 最后一轮
            string final = handler.Drain();
            if (final.Length > 0)
                ProcessStreamData(final, lineBuf, ref accumulatedContent, toolCallAcc, onContentDelta);

            if (req.result == UnityWebRequest.Result.Success)
            {
                string toolCallsJson = BuildToolCallsJson(toolCallAcc);
                onFinish?.Invoke(accumulatedContent, toolCallsJson);
            }
            else
            {
                errorMsg = req.error;
                string errBody = handler.Peek();
                if (!string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\""))
                    errorMsg = ExtractErrorMessage(errBody);
                onError?.Invoke(errorMsg);
            }
        }
    }

    // ---- SSE 流式辅助类型 ----

    private class ToolCallAccumulator
    {
        public string id;
        public string name;
        public StringBuilder args = new StringBuilder();
    }

    private static readonly char[] _newlineChars = { '\n', '\r' };

    /// <summary>处理一块原始 SSE 数据，解析出完整的行</summary>
    private static void ProcessStreamData(
        string raw, StringBuilder lineBuf,
        ref string accumulatedContent,
        List<ToolCallAccumulator> toolCallAcc,
        Action<string> onContentDelta)
    {
        lineBuf.Append(raw);
        string buf = lineBuf.ToString();

        while (true)
        {
            int idx = buf.IndexOfAny(_newlineChars);
            if (idx < 0) break;

            string line = buf.Substring(0, idx);
            buf = buf.Substring(idx + 1);

            if (line.Length == 0) continue;

            if (line.StartsWith("data: "))
            {
                string data = line.Substring(6).Trim();
                if (data == "[DONE]") continue;

                var delta = ParseStreamDelta(data);

                if (!string.IsNullOrEmpty(delta.content))
                {
                    accumulatedContent += delta.content;
                    onContentDelta?.Invoke(delta.content);
                }

                if (delta.toolCallId != null)
                {
                    toolCallAcc.Add(new ToolCallAccumulator
                    {
                        id = delta.toolCallId,
                        name = delta.toolName ?? ""
                    });
                    if (delta.toolArgsPart != null && toolCallAcc.Count > 0)
                        toolCallAcc[toolCallAcc.Count - 1].args.Append(delta.toolArgsPart);
                }
                else if (delta.toolArgsPart != null && toolCallAcc.Count > 0)
                {
                    toolCallAcc[toolCallAcc.Count - 1].args.Append(delta.toolArgsPart);
                }
                else if (delta.toolName != null && toolCallAcc.Count > 0)
                {
                    toolCallAcc[toolCallAcc.Count - 1].name = delta.toolName;
                }
            }
        }

        lineBuf.Clear();
        lineBuf.Append(buf);
    }

    /// <summary>解析 SSE data 行中的 delta 字段</summary>
    private static StreamDelta ParseStreamDelta(string dataJson)
    {
        var result = new StreamDelta();

        // 提取 content
        string contentKey = "\"content\":\"";
        int ci = dataJson.IndexOf(contentKey);
        if (ci >= 0)
        {
            ci += contentKey.Length;
            var sb = new StringBuilder();
            for (int i = ci; i < dataJson.Length; i++)
            {
                if (dataJson[i] == '\\' && i + 1 < dataJson.Length)
                {
                    char n = dataJson[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; }
                    else if (n == '"') { sb.Append('"'); i++; }
                    else if (n == '\\') { sb.Append('\\'); i++; }
                    else if (n == 'r') { i++; }
                    else if (n == 't') { sb.Append('\t'); i++; }
                    else sb.Append(dataJson[i]);
                }
                else if (dataJson[i] == '"')
                {
                    break;
                }
                else sb.Append(dataJson[i]);
            }
            result.content = sb.ToString();
        }
        // content 也可能为 null
        else if (dataJson.Contains("\"content\":null"))
        {
            result.content = null;
        }

        // 提取 tool_calls
        if (dataJson.Contains("\"tool_calls\""))
        {
            // 提取 id
            int idI = dataJson.IndexOf("\"id\":\"");
            if (idI >= 0)
            {
                idI += 6;
                result.toolCallId = ExtractSimpleString(dataJson, idI);
            }

            // 提取 function.name
            int fnI = dataJson.IndexOf("\"name\":\"");
            if (fnI >= 0)
            {
                fnI += 8;
                result.toolName = ExtractSimpleString(dataJson, fnI);
            }

            // 提取 function.arguments 片段
            int argI = dataJson.IndexOf("\"arguments\":");
            if (argI >= 0)
            {
                argI += 12;
                // arguments 可能是字符串或对象
                if (argI < dataJson.Length && dataJson[argI] == '"')
                {
                    result.toolArgsPart = ExtractSimpleString(dataJson, argI + 1);
                }
                else if (argI < dataJson.Length && dataJson[argI] == '{')
                {
                    // JSON 对象片段 — 按原样取
                    int depth = 1;
                    int start = argI;
                    for (int i = argI + 1; i < dataJson.Length; i++)
                    {
                        if (dataJson[i] == '{') depth++;
                        else if (dataJson[i] == '}') { depth--; if (depth == 0) { result.toolArgsPart = dataJson.Substring(start, i - start + 1); break; } }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>将流式积累的 tool_calls 重建为 JSON 数组</summary>
    private static string BuildToolCallsJson(List<ToolCallAccumulator> acc)
    {
        if (acc.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < acc.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var t = acc[i];
            sb.Append("{\"id\":\"");
            sb.Append(EscapeJson(t.id));
            sb.Append("\",\"type\":\"function\",\"function\":{\"name\":\"");
            sb.Append(EscapeJson(t.name));
            sb.Append("\",\"arguments\":\"");
            sb.Append(EscapeJson(t.args.ToString()));
            sb.Append("\"}}");
        }
        sb.Append(']');
        return sb.ToString();
    }
}
