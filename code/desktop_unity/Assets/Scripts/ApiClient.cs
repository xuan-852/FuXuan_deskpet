using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 共享 API 客户端 — 统一的 HTTP 请求与 JSON 解析工具
///
/// 整合了 ChatManager 和 IdleChatGenerator 中曾各自维护的重复代码，
/// 提供 PostRequest / StreamRequest 及 JSON 解析辅助方法。
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
                string errMsg = !string.IsNullOrEmpty(errBody)
                    ? ExtractErrorMessage(errBody)
                    : req.error;
                onError?.Invoke(errMsg);
            }
        }
    }

    // ================================================================
    //  JSON 解析
    // ================================================================

    /// <summary>从 DeepSeek/OpenAI 兼容响应中提取 content 字段文本</summary>
    public static string ExtractContent(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        try
        {
            var root = JObject.Parse(json);
            var choices = root["choices"] as JArray;
            if (choices == null || choices.Count == 0) return "";
            var delta = choices[0]["message"]?["content"];
            return delta?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>从错误响应 JSON 中提取 message 字段（人类可读错误描述）</summary>
    public static string ExtractErrorMessage(string json)
    {
        if (string.IsNullOrEmpty(json)) return "未知错误";
        try
        {
            var root = JObject.Parse(json);
            var msg = root["error"]?["message"] ?? root["message"];
            return msg?.ToString() ?? json;
        }
        catch
        {
            return json;
        }
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
                if (!string.IsNullOrEmpty(errBody))
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
                    // DeepSeek 可能分别发 id delta 和 name delta
                    // 只在同时有 id+非空 name 时才创建新累加器
                    if (!string.IsNullOrEmpty(delta.toolName))
                    {
                        toolCallAcc.Add(new ToolCallAccumulator
                        {
                            id = delta.toolCallId,
                            name = delta.toolName
                        });
                    }
                    // 如果只有 id 没 name（空字符串也算无 name），说明 name 在后面独立 delta 中
                    // 创建占位累加器，后面由 name 分支更新
                    else if (toolCallAcc.Count == 0 || toolCallAcc[toolCallAcc.Count - 1].id != delta.toolCallId)
                    {
                        toolCallAcc.Add(new ToolCallAccumulator
                        {
                            id = delta.toolCallId,
                            name = ""
                        });
                    }
                    // arguments 片段
                    if (delta.toolArgsPart != null && toolCallAcc.Count > 0)
                        toolCallAcc[toolCallAcc.Count - 1].args.Append(delta.toolArgsPart);
                }
                else if (delta.toolArgsPart != null && toolCallAcc.Count > 0)
                {
                    toolCallAcc[toolCallAcc.Count - 1].args.Append(delta.toolArgsPart);
                }
                else if (delta.toolName != null && toolCallAcc.Count > 0)
                {
                    var last = toolCallAcc[toolCallAcc.Count - 1];
                    if (string.IsNullOrEmpty(last.name))
                        last.name = delta.toolName;
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

        try
        {
            var root = JObject.Parse(dataJson);
            var choice = root["choices"]?[0];
            if (choice == null) return result;

            var delta = choice["delta"];
            if (delta == null) return result;

            // content
            var content = delta["content"];
            if (content != null)
                result.content = content.Type == JTokenType.Null ? null : content.ToString();
            else
                result.content = null;

            // tool_calls
            var toolCalls = delta["tool_calls"] as JArray;
            if (toolCalls != null && toolCalls.Count > 0)
            {
                var tc = toolCalls[0];
                result.toolCallId = tc["id"]?.ToString();
                var fn = tc["function"];
                if (fn != null)
                {
                    result.toolName = fn["name"]?.ToString();
                    result.toolArgsPart = fn["arguments"]?.ToString();
                }
            }
        }
        catch
        {
            // 单个 data 行解析失败不影响整体流
        }

        return result;
    }

    /// <summary>将流式积累的 tool_calls 重建为 JSON 数组</summary>
    private static string BuildToolCallsJson(List<ToolCallAccumulator> acc)
    {
        if (acc.Count == 0) return null;

        var arr = new JArray();
        for (int i = 0; i < acc.Count; i++)
        {
            var t = acc[i];
            if (string.IsNullOrEmpty(t.name)) continue;

            arr.Add(new JObject
            {
                ["id"] = t.id,
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = t.name,
                    ["arguments"] = t.args.ToString()
                }
            });
        }

        return arr.Count == 0 ? null : arr.ToString(Newtonsoft.Json.Formatting.None);
    }
}
