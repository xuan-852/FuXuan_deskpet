using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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

    /// <summary>
    /// 桥接鉴权 Token — 与 openclaw_bridge.js 的 BRIDGE_TOKEN 一致。
    /// 只读环境变量 BRIDGE_TOKEN（与 JS 端同源配置）。
    /// 缺省时不使用任何内置默认值（历史版本的内置 Token 已泄漏并轮换），直接返回空串禁用桥接。
    /// </summary>
    private static string BridgeToken
    {
        get
        {
            var env = System.Environment.GetEnvironmentVariable("BRIDGE_TOKEN");
            if (string.IsNullOrEmpty(env))
            {
                Debug.LogWarning("[OpenClawBridge] ⚠️ 环境变量 BRIDGE_TOKEN 未配置，桥接鉴权将被拒绝。请设置 BRIDGE_TOKEN（与 openclaw_bridge.js / PM2 的 BRIDGE_TOKEN 一致）。");
                return "";
            }
            return env;
        }
    }

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
            req.SetRequestHeader("x-bridge-token", BridgeToken);

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
                var obj = JObject.Parse(raw);
                bool success = obj["success"]?.Value<bool>() ?? false;
                if (success)
                {
                    IsAvailable = true;
                    LastError = "";
                    string response = obj["response"]?.ToString();
                    if (!string.IsNullOrEmpty(response))
                        return response;

                    return raw;
                }
                else
                {
                    string err = obj["error"]?.ToString() ?? "未知错误";
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
            req.SetRequestHeader("x-bridge-token", BridgeToken);
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
            try
            {
                var obj = JObject.Parse(raw);
                string status = obj["status"]?.ToString();
                IsAvailable = (status == "ok");
            }
            catch
            {
                IsAvailable = false;
            }

            if (!IsAvailable)
                LastError = "通神阵法未就绪";
            else
                LastError = "";
            return IsAvailable;
        }
    }

    /// <summary>
    /// 编译 LaTeX 源码为 PDF（通过桥接服务器生成源码并编译）
    /// </summary>
    /// <param name="source">LaTeX 文档源码（直接提供时）</param>
    /// <param name="outputPath">输出 .tex 路径（可选，默认 Documents 目录）</param>
    /// <param name="compiler">编译器：pdflatex / xelatex / lualatex（默认 xelatex）</param>
    /// <param name="title">文档标题（用于命名文件夹，可选）</param>
    /// <param name="pinToDesktop">是否在桌面创建快捷方式</param>
    /// <param name="description">文档需求描述（AI 将根据描述生成源码，优先级低于 source）</param>
    /// <returns>包含 pdf_path 和 tex_path 的 JSON 文本</returns>
    public static async Task<string> CompileLatexAsync(string source, string outputPath = null, string compiler = "xelatex", string title = null, bool pinToDesktop = false, string description = null)
    {
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(description))
            return "❌ 未提供 LaTeX 源码或需求描述";

        string url = $"{BASE_URL}/compile_latex";
        var payload = new Newtonsoft.Json.Linq.JObject
        {
            ["source"] = source ?? "",
            ["output_path"] = outputPath ?? "",
            ["compiler"] = compiler,
            ["title"] = title ?? "",
            ["pin_to_desktop"] = pinToDesktop,
            ["description"] = description ?? ""
        };
        string jsonBody = payload.ToString(Newtonsoft.Json.Formatting.None);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-bridge-token", BridgeToken);
            // 长文档（多章节）走分块生成 + 编译，全程可能 10-20 分钟，180s 会超时。
            req.timeout = 1800;

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = req.error;
                return $"{{\"success\":false,\"error\":\"{req.error}\"}}";
            }

            string raw = req.downloadHandler?.text ?? "{}";
            try
            {
                var obj = JObject.Parse(raw);
                bool success = obj["success"]?.Value<bool>() ?? false;
                if (success)
                    return raw; // 完整 JSON 给工具层解析

                string err = obj["error"]?.ToString() ?? "未知编译错误";
                LastError = err;
                return $"{{\"success\":false,\"error\":\"{err}\"}}";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return $"{{\"success\":false,\"error\":\"{ex.Message}\"}}";
            }
        }
    }
}
