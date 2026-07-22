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
    /// 编译 LaTeX 源码为 PDF（通过桥接服务器调 pdflatex/xelatex）
    /// </summary>
    /// <param name="source">LaTeX 文档源码</param>
    /// <param name="outputPath">输出 .tex 路径（可选，默认 Documents 目录）</param>
    /// <param name="compiler">编译器：pdflatex / xelatex / lualatex（默认 xelatex）</param>
    /// <param name="title">文档标题（用于命名文件夹，可选）</param>
    /// <param name="pinToDesktop">是否在桌面创建快捷方式</param>
    /// <returns>包含 pdf_path 和 tex_path 的 JSON 文本</returns>
    public static async Task<string> CompileLatexAsync(string source, string outputPath = null, string compiler = "xelatex", string title = null, bool pinToDesktop = false)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "❌ 未提供 LaTeX 源码";

        string url = $"{BASE_URL}/compile_latex";
        var payload = new Newtonsoft.Json.Linq.JObject
        {
            ["source"] = source,
            ["output_path"] = outputPath ?? "",
            ["compiler"] = compiler,
            ["title"] = title ?? "",
            ["pin_to_desktop"] = pinToDesktop
        };
        string jsonBody = payload.ToString(Newtonsoft.Json.Formatting.None);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 180;

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
