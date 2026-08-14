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

    /// <summary>
    /// 生成办公文档（PPT / Word / Excel）。
    /// 桥接服务器让 AI 组织内容 → 本地 Python (python-pptx/docx/openpyxl) 渲染成文件。
    /// </summary>
    /// <param name="type">文档类型：ppt / docx / xlsx</param>
    /// <param name="description">文档需求描述（自然语言，如「做一个关于 AI 的汇报 PPT」）</param>
    /// <param name="title">文档标题（可选，用于命名文件）</param>
    /// <param name="theme">PPT 主题色（可选：blue/green/purple/dark/orange）</param>
    /// <returns>JSON 文本：{"success":true,"path":"...","title":"...","folder_path":"..."} 或失败 JSON</returns>
    public static async Task<string> GenerateOfficeAsync(string type, string description, string title = null, string theme = null)
    {
        if (string.IsNullOrWhiteSpace(type) || !(type == "ppt" || type == "docx" || type == "xlsx"))
            return "❌ 文档类型必须是 ppt / docx / xlsx 之一";
        if (string.IsNullOrWhiteSpace(description))
            return "❌ 未提供文档需求描述，请告诉本座想生成什么内容";

        string url = $"{BASE_URL}/generate_office";
        var payload = new Newtonsoft.Json.Linq.JObject
        {
            ["type"] = type,
            ["description"] = description,
            ["title"] = title ?? "",
            ["theme"] = theme ?? ""
        };
        string jsonBody = payload.ToString(Newtonsoft.Json.Formatting.None);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-bridge-token", BridgeToken);
            // AI 组织内容 + 本地渲染，通常 10-60s；给足余量
            req.timeout = 300;

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = req.error;
                IsAvailable = false;
                return $"{{\"success\":false,\"error\":\"{req.error}\"}}";
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
                    return raw; // 完整 JSON 给工具层解析
                }

                string err = obj["error"]?.ToString() ?? "未知错误";
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

    /// <summary>
    /// 提取 PDF 文本（供「藏书阁」knowledge_index 索引 PDF 用）。
    /// 桥接服务器调用本地 Python（PyMuPDF，中文支持好）提取文本层。
    /// </summary>
    /// <param name="pdfPath">PDF 文件绝对路径</param>
    /// <param name="maxChars">最多提取字符数（默认 50 万，防超大 PDF 拖垮索引）</param>
    /// <returns>JSON 文本：{"success":true,"text":"...","pages":N,"chars":N}
    ///          或 {"success":false,"error":"...","is_scanned":true}（扫描版 PDF 无文本层）</returns>
    public static async Task<string> ExtractPdfTextAsync(string pdfPath, int maxChars = 500000)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
            return "{\"success\":false,\"error\":\"未提供 PDF 路径\"}";

        string url = $"{BASE_URL}/extract_pdf";
        var payload = new Newtonsoft.Json.Linq.JObject
        {
            ["path"] = pdfPath,
            ["max_chars"] = maxChars
        };
        string jsonBody = payload.ToString(Newtonsoft.Json.Formatting.None);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-bridge-token", BridgeToken);
            // 大 PDF 提取可能耗时较长，给足余量
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
                    return raw; // 完整 JSON（含 text）给工具层解析

                string err = obj["error"]?.ToString() ?? "PDF 提取失败";
                LastError = err;
                // 透传 is_scanned 标记，让工具层给出针对性提示
                bool isScanned = obj["is_scanned"]?.Value<bool>() ?? false;
                if (isScanned)
                    return $"{{\"success\":false,\"error\":\"{err}\",\"is_scanned\":true}}";
                return $"{{\"success\":false,\"error\":\"{err}\"}}";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return $"{{\"success\":false,\"error\":\"{ex.Message}\"}}";
            }
        }
    }

    /// <summary>当前是否有在途任务（提交后未完成）</summary>
    public static bool IsBusy { get; private set; } = false;

    /// <summary>最近提交的任务 ID</summary>
    public static string LastTaskId { get; private set; } = "";

    // ==================================================================
    //  ★ 实时进度状态（后台任务线程写原子字段，主线程 OnGUI 只读，无锁安全）
    // ==================================================================

    /// <summary>是否有 OpenClaw 任务正在后台执行（ExecuteTaskAndWaitAsync 在途）</summary>
    public static bool HasActiveTask { get; private set; } = false;

    /// <summary>当前在途任务 ID（审批回执用；任务结束后保留供查询）</summary>
    public static string ActiveTaskId { get; private set; } = "";

    /// <summary>已记录步骤数（进度可见：第几步）</summary>
    public static int ActiveStepCount { get; private set; } = 0;

    /// <summary>当前步骤标签（如「第3步: exec 下载文件」；无步骤时为空串）</summary>
    public static string ActiveStepLabel { get; private set; } = "";

    /// <summary>最近一次任务的总步骤数（供轨迹库记录 stepCount）</summary>
    public static int LastTaskStepCount { get; private set; } = 0;

    /// <summary>挂起的 exec 审批（null=无）；含 id/command/host/title/slug</summary>
    public static PendingApprovalInfo PendingApproval { get; private set; } = null;

    /// <summary>最近一次审批回执结果（成功 true / 失败 false），UI 提示用</summary>
    public static bool LastApprovalOk { get; private set; } = false;

    /// <summary>OpenClaw exec 审批信息（与桥接层 pendingApproval 结构对应）</summary>
    public class PendingApprovalInfo
    {
        /// <summary>审批 ID（回执时必须原样带回）</summary>
        public string approvalId = "";
        /// <summary>审批类型 slug（如 exec）</summary>
        public string approvalSlug = "";
        /// <summary>待执行的命令（exec 审批的核心内容，展示给用户）</summary>
        public string command = "";
        /// <summary>主机信息（可选）</summary>
        public string host = "";
        /// <summary>审批标题（可选）</summary>
        public string title = "";
    }

    /// <summary>
    /// 上次任务是否「不可重试错误」（网络连不上/超时/断连等）。
    /// LLM 看到此类失败后不应换说法反复重调任务（烧 token 元凶）。
    /// </summary>
    public static bool LastTaskWasFatal { get; private set; } = false;

    /// <summary>不可重试错误的结果前缀——用于识别 LLM 不应重试的任务失败</summary>
    public const string FATAL_PREFIX = "❌ [不可重试]";

    /// <summary>
    /// 提交任务给 OpenClaw Agent 执行（异步，后台运行）。
    /// 返回 task_id，之后用 PollTaskAsync 轮询状态、CancelTaskAsync 取消。
    /// </summary>
    /// <param name="task">任务描述（自然语言，如「查一下 B 站本周热门视频 TOP5」）</param>
    /// <param name="mode">执行模式：agent（默认，OpenClaw 自行选择工具）/ browser（引导使用浏览器）</param>
    /// <param name="timeoutMs">网关等待超时（毫秒，默认 180000）</param>
    /// <param name="maxSteps">步骤预算上限（成本熔断，默认 0=桥接侧默认 20 步）</param>
    /// <returns>task_id；失败返回空串并设置 LastError</returns>
    public static async Task<string> SubmitTaskAsync(string task, string mode = "agent", int timeoutMs = 180000, int maxSteps = 0)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            LastError = "未提供任务描述";
            return "";
        }

        string url = $"{BASE_URL}/task";
        var payload = new JObject
        {
            ["task"] = task,
            ["mode"] = mode,
            ["timeoutMs"] = timeoutMs,
            ["maxSteps"] = maxSteps
        };
        string jsonBody = payload.ToString(Newtonsoft.Json.Formatting.None);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-bridge-token", BridgeToken);
            req.timeout = 15; // 提交只做入队，快速返回

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = req.error;
                IsAvailable = false;
                return "";
            }

            string raw = req.downloadHandler?.text ?? "{}";
            try
            {
                var obj = JObject.Parse(raw);
                bool success = obj["success"]?.Value<bool>() ?? false;
                if (!success)
                {
                    LastError = obj["error"]?.ToString() ?? "任务提交失败";
                    return "";
                }

                string taskId = obj["task_id"]?.ToString();
                LastTaskId = taskId;
                LastError = "";
                return taskId ?? "";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return "";
            }
        }
    }

    /// <summary>
    /// 轮询任务状态
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <returns>JSON 字符串：{"success":true,"status":"running|done|error|cancelled","result":"...","error":"..."}</returns>
    public static async Task<string> PollTaskAsync(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
            return "{\"success\":false,\"error\":\"task_id 为空\"}";

        string url = $"{BASE_URL}/task/{UnityWebRequest.EscapeURL(taskId)}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 10;
            req.SetRequestHeader("x-bridge-token", BridgeToken);
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = req.error;
                return $"{{\"success\":false,\"error\":\"{req.error}\"}}";
            }
            return req.downloadHandler?.text ?? "{\"success\":false,\"error\":\"empty response\"}";
        }
    }

    /// <summary>
    /// 取消任务
    /// </summary>
    /// <returns>true=取消成功/已在终态，false=失败（LastError 含原因）</returns>
    public static async Task<bool> CancelTaskAsync(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            LastError = "task_id 为空";
            return false;
        }

        string url = $"{BASE_URL}/task/{UnityWebRequest.EscapeURL(taskId)}/cancel";
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("x-bridge-token", BridgeToken);
            req.timeout = 10;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = req.error;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 审批 OpenClaw 任务挂起的敏感操作（exec 等）。
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="decision">allow-once（允许一次）/ allow-always（总是允许）/ deny（拒绝）</param>
    /// <returns>true=回执成功送达，false=失败（LastError 含原因）</returns>
    public static async Task<bool> ApproveTaskAsync(string taskId, string decision)
    {
        if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(decision))
        {
            LastError = "task_id / decision 为空";
            return false;
        }
        if (decision != "allow-once" && decision != "allow-always" && decision != "deny")
        {
            LastError = $"decision 非法: {decision}（应为 allow-once/allow-always/deny）";
            return false;
        }

        string url = $"{BASE_URL}/task/{UnityWebRequest.EscapeURL(taskId)}/approve";
        var payload = new JObject { ["decision"] = decision };
        string jsonBody = payload.ToString(Newtonsoft.Json.Formatting.None);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-bridge-token", BridgeToken);
            req.timeout = 10;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = req.error;
                LastApprovalOk = false;
                return false;
            }

            try
            {
                var obj = JObject.Parse(req.downloadHandler?.text ?? "{}");
                LastApprovalOk = obj["success"]?.Value<bool>() ?? false;
                if (!LastApprovalOk)
                    LastError = obj["error"]?.ToString() ?? "审批回执失败";
                return LastApprovalOk;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LastApprovalOk = false;
                return false;
            }
        }
    }

    /// <summary>
    /// 一站式执行任务：提交 + 心跳轮询直到完成（或卡死/超时/失败）。
    /// 适合「等结果」类工具调用；长任务请用 SubmitTaskAsync + PollTaskAsync 组合。
    ///
    /// 心跳机制（下载大文件不随意熔断）：
    /// 每 heartbeatSeconds 探测一次 agent 活动（lastActivityAt 由桥接层在中间事件时刷新）。
    /// 有进度更新 → 心跳自动重置；连续 maxIdleHeartbeats 次无更新 → 判定卡死，取消并提示可重试。
    /// </summary>
    /// <param name="task">任务描述</param>
    /// <param name="mode">agent / browser</param>
    /// <param name="totalTimeoutSeconds">总硬上限（默认 1800 秒=30 分钟，仅防失控；卡死由心跳判定）</param>
    /// <param name="maxSteps">步骤预算上限（成本熔断，默认 0=桥接侧默认 20 步）</param>
    /// <param name="heartbeatSeconds">心跳探测间隔秒数（默认 60）</param>
    /// <param name="maxIdleHeartbeats">连续无进展心跳数阈值（默认 5 → 300s 无进展判定卡死）</param>
    /// <returns>任务结果文本（成功时含 AI 答复，失败时以 ❌ 开头；不可重试错误以 ❌ [不可重试] 开头）</returns>
    public static async Task<string> ExecuteTaskAndWaitAsync(
        string task, string mode = "agent", int totalTimeoutSeconds = 1800,
        int maxSteps = 0, int heartbeatSeconds = 60, int maxIdleHeartbeats = 5)
    {
        IsBusy = true;
        LastTaskWasFatal = false;
        // ★ 进度状态复位（后台线程写，主线程 OnGUI 只读）
        HasActiveTask = true;
        ActiveStepCount = 0;
        ActiveStepLabel = "";
        PendingApproval = null;
        LastTaskStepCount = 0;
        try
        {
            string taskId = await SubmitTaskAsync(task, mode, timeoutMs: 180000, maxSteps: maxSteps);
            if (string.IsNullOrEmpty(taskId))
            {
                // 提交失败（桥接不可用/连接失败）→ 不可重试
                LastTaskWasFatal = IsNetworkishError(LastError);
                return $"{FATAL_PREFIX} 任务提交失败: {LastError}";
            }
            ActiveTaskId = taskId;

            // ★ 心跳参数：探测间隔与「连续无进展」阈值
            int pollDelayMs = Math.Min(Math.Max(5, heartbeatSeconds * 1000), 5000); // 每 5s 轮询（done 更快感知）
            int maxIdleSeconds = Math.Max(30, heartbeatSeconds * maxIdleHeartbeats); // 默认 300s 无进展=卡死

            double deadline = Time.realtimeSinceStartup + Math.Max(60, totalTimeoutSeconds);
            while (Time.realtimeSinceStartup < deadline)
            {
                string raw = await PollTaskAsync(taskId);
                try
                {
                    var obj = JObject.Parse(raw);
                    if (!(obj["success"]?.Value<bool>() ?? false))
                        return $"❌ 任务轮询失败: {obj["error"]?.ToString() ?? "未知"}";

                    string status = obj["status"]?.ToString() ?? "running";

                    // ★ 实时步骤/审批解析（无论状态，每轮都刷新；后台线程写原子字段）
                    RefreshTaskProgress(obj);

                    switch (status)
                    {
                        case "done":
                            LastTaskStepCount = ActiveStepCount;
                            PendingApproval = null; // 任务已终态，清审批
                            return obj["result"]?.ToString() ?? "(无结果)";
                        case "error":
                            LastTaskStepCount = ActiveStepCount;
                            PendingApproval = null;
                            string err = obj["error"]?.ToString() ?? "未知错误";
                            bool fatal = obj["fatal"]?.Value<bool>() ?? err.Contains("不可重试");
                            if (fatal)
                            {
                                LastTaskWasFatal = true;
                                return $"{FATAL_PREFIX} 任务失败（网络/连接问题，重试无益）: {err}";
                            }
                            return $"❌ 任务执行出错: {err}";
                        case "cancelled":
                            LastTaskStepCount = ActiveStepCount;
                            PendingApproval = null;
                            return "❌ 任务已被取消";
                        default: // running / queued
                        {
                            // ★ 心跳判定：agent 最后活动时间（桥接层在中间事件时刷新）。
                            //   lastActivityAt > 0 且停滞超过 maxIdleSeconds → 卡死（可重试，非不可重试）
                            long lastActivityMs = obj["lastActivityAt"]?.Value<long>() ?? 0;
                            if (lastActivityMs > 0)
                            {
                                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                double idleSeconds = (nowMs - lastActivityMs) / 1000.0;
                                if (idleSeconds > maxIdleSeconds)
                                {
                                    LastTaskWasFatal = false; // 可重试：用户可再发起或换思路
                                    await CancelTaskAsync(taskId);
                                    LastTaskStepCount = ActiveStepCount;
                                    PendingApproval = null;
                                    return $"❌ 任务疑似卡死：连续超过 {maxIdleSeconds}s 无任何进度（下载/执行停滞），已取消。可再次尝试，或检查网络/下载源后重试。";
                                }
                            }
                            // queued 且从未有活动（等待前序任务）→ 不算卡死，继续等
                            await Task.Delay(pollDelayMs);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    return $"❌ 解析任务状态出错: {ex.Message}";
                }
            }

            // 总硬上限兜底（仅防失控；正常下载靠心跳延长）
            LastTaskWasFatal = true;
            await CancelTaskAsync(taskId);
            LastTaskStepCount = ActiveStepCount;
            PendingApproval = null;
            return $"{FATAL_PREFIX} 任务超过总时长上限（{totalTimeoutSeconds}s），已取消。若为下载任务请确认下载源可用后再试。";
        }
        finally
        {
            IsBusy = false;
            HasActiveTask = false;   // ★ 任务结束（任何路径）→ 进度状态复位
            ActiveStepLabel = "";
            PendingApproval = null;
        }
    }

    /// <summary>
    /// 从轮询响应刷新实时进度状态（步骤数/步骤标签/挂起审批）。
    /// 后台任务线程调用——只写引用/值类型原子字段，主线程 OnGUI 只读，无锁安全。
    /// </summary>
    private static void RefreshTaskProgress(JObject obj)
    {
        try
        {
            // ——— 步骤 ———
            var stepsArr = obj["steps"] as JArray;
            if (stepsArr != null)
            {
                int n = stepsArr.Count;
                ActiveStepCount = n;
                if (n > 0)
                {
                    var last = stepsArr[n - 1] as JObject;
                    if (last != null)
                    {
                        string tool = last["tool"]?.ToString() ?? "tool";
                        string summary = last["summary"]?.ToString() ?? "";
                        // 摘要精简：去换行/压缩空白，截断（标题栏一行显示）
                        summary = summary.Replace("\r", " ").Replace("\n", " ").Trim();
                        if (summary.Length > 48) summary = summary.Substring(0, 48) + "…";
                        ActiveStepLabel = $"第{n}步: {tool}" + (string.IsNullOrEmpty(summary) ? "" : $" {summary}");
                    }
                }
            }

            // ——— 挂起审批 ———
            var pa = obj["pendingApproval"] as JObject;
            if (pa != null && pa["id"] != null)
            {
                var info = new PendingApprovalInfo
                {
                    approvalId = pa["id"]?.ToString() ?? "",
                    approvalSlug = pa["slug"]?.ToString() ?? "",
                    command = pa["command"]?.ToString() ?? "",
                    host = pa["host"]?.ToString() ?? "",
                    title = pa["title"]?.ToString() ?? ""
                };
                // 仅当 id 变化或从无到有时刷新（避免每轮重设导致 UI 检测不到"新审批"）
                var cur = PendingApproval;
                if (cur == null || cur.approvalId != info.approvalId)
                    PendingApproval = info;
            }
            else if (pa != null && pa["id"] == null)
            {
                // pendingApproval 对象存在但已无 id → 已解析
                PendingApproval = null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OpenClawBridge] 刷新任务进度失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 判断错误信息是否属于「网络/连接类」——此类错误重试无益，应直接告知用户。
    /// </summary>
    private static bool IsNetworkishError(string err)
    {
        if (string.IsNullOrEmpty(err)) return false;
        string e = err.ToLowerInvariant();
        return e.Contains("timeout") || e.Contains("timed out") || e.Contains("timedout")
            || e.Contains("refused") || e.Contains("reset") || e.Contains("unreachable")
            || e.Contains("disconnected") || e.Contains("connection") || e.Contains("socket")
            || e.Contains("curl error") || e.Contains("not connected") || e.Contains("network");
    }
}
