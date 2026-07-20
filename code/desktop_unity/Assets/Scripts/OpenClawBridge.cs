using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 太卜通神算术式 — OpenClaw AI 搜索桥接器
///
/// 连接本地 Node.js 桥接服务器 (openclaw_bridge.js)，
/// 通过 OpenClaw Gateway 实现实时网络搜索与研究。
/// 桥接服务器地址：http://127.0.0.1:19876
/// </summary>
public static class OpenClawBridge
{
    private const string BASE_URL = "http://127.0.0.1:19876";

    /// <summary>桥接服务器是否可用（最近一次健康检查结果）</summary>
    public static bool IsAvailable { get; private set; } = false;

    /// <summary>上次错误信息</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 执行网络搜索，返回 AI 研究的文本结果
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="timeoutSeconds">超时秒数（默认 180 秒）</param>
    /// <returns>搜索结果的文本内容</returns>
    public static async Task<string> SearchWebAsync(string query, int timeoutSeconds = 180)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "❌ 未提供查询内容，本座如何通神推演？";

        string url = $"{BASE_URL}/search?q={UnityWebRequest.EscapeURL(query)}";

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");

            var op = req.SendWebRequest();

            // 等待完成（非阻塞）
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = req.error;
                IsAvailable = false;
                return $"❌ 太卜通神术式失联: {req.error}";
            }

            string raw = req.downloadHandler?.text ?? "{}";
            try
            {
                // 手动简易 JSON 解析
                string success = ExtractJsonValue(raw, "success");
                if (success == "true")
                {
                    IsAvailable = true;
                    LastError = "";
                    string response = ExtractJsonValue(raw, "response");
                    if (!string.IsNullOrEmpty(response))
                        return response;

                    // fallback: 返回完整 body
                    return raw;
                }
                else
                {
                    string err = ExtractJsonValue(raw, "error");
                    LastError = err;
                    return $"❌ 通神术式未应验: {err}";
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return $"❌ 解析卦象出错: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// 检查桥接服务器健康状态
    /// </summary>
    public static async Task<bool> CheckHealthAsync()
    {
        string url = $"{BASE_URL}/health";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 3;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                IsAvailable = false;
                LastError = req.error;
                return false;
            }

            string raw = req.downloadHandler?.text ?? "";
            string status = ExtractJsonValue(raw, "status");
            IsAvailable = (status == "ok");
            if (!IsAvailable)
                LastError = "通神阵法未就绪";
            else
                LastError = "";
            return IsAvailable;
        }
    }

    /// <summary>简易 JSON 字段提取（不依赖外部库）</summary>
    private static string ExtractJsonValue(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int idx = json.IndexOf(search);
        if (idx >= 0)
        {
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == '"') { sb.Append('"'); i++; }
                    else if (n == '\\') { sb.Append('\\'); i++; }
                    else if (n == 'n') { sb.Append('\n'); i++; }
                    else sb.Append(json[i]);
                }
                else if (json[i] == '"') break;
                else sb.Append(json[i]);
            }
            return sb.ToString().Trim();
        }

        // 尝试提取不带引号的布尔/数字值
        search = $"\"{key}\":";
        idx = json.IndexOf(search);
        if (idx >= 0)
        {
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ',' || c == '}' || c == ']') break;
                sb.Append(c);
            }
            return sb.ToString().Trim().Trim('"');
        }

        return "";
    }
}
