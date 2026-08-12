using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

// ================================================================
//  法眼摄形 + GLM 视觉分析
// ================================================================

public class TakeScreenshotTool : IPetTool
{
    public string ToolName => "take_screenshot";
    public string ToolDescription => "【法眼摄形】截图并让 AI 分析屏幕内容。用户说「看看我的屏幕」「帮我看一下电脑」「截图」时调用。静默截图，不留本地文件痕迹。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 法眼摄形中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        // ——— 1. 截图 ———
        string screenshotPath = ToolHelpers.SaveScreenshotTemp();
        if (screenshotPath == null || !File.Exists(screenshotPath))
        {
            onResult?.Invoke("❌ 摄形失败，无法窥视凡间");
            yield break;
        }

        // ——— 2. 读取 → base64，然后删文件 ———
        byte[] imageBytes = null;
        try { imageBytes = File.ReadAllBytes(screenshotPath); }
        catch (Exception e)
        {
            Debug.LogWarning($"[TakeScreenshotTool] 读图失败: {e.Message}");
            onResult?.Invoke("❌ 法眼虽摄形，但无法解读天书");
            yield break;
        }
        finally
        {
            try { if (File.Exists(screenshotPath)) File.Delete(screenshotPath); } catch { }
        }

        string base64 = Convert.ToBase64String(imageBytes);
        string dataUrl = "data:image/png;base64," + base64;

        // ——— 3. GLM 请求 ———
        string requestId = Guid.NewGuid().ToString("N");
        string prompt = "请详细描述这张电脑屏幕截图中的全部内容，包括：有哪些窗口/程序在运行、界面上有什么文字和按钮、任务栏图标、桌面图标等所有可见信息。按区域依次描述。";

        string jsonBody = BuildGlmVisionJson(prompt, dataUrl, requestId);
        string fullUrl = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";
        string responseText = null;

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + ChatConfig.GlmApiKey);
            req.timeout = 180;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
                responseText = req.downloadHandler.text;
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = req.error;
                if (!string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\""))
                {
                    try
                    {
                        var errObj = JsonUtility.FromJson<GlmErrorResponse>(errBody);
                        if (errObj != null && !string.IsNullOrEmpty(errObj.error.message))
                            errMsg = errObj.error.message;
                    }
                    catch { }
                }
                onResult?.Invoke("❌ 法眼窥视天机受阻：" + errMsg);
                yield break;
            }
        }

        // ——— 4. 解析 ———
        try
        {
            var resp = JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp?.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
            {
                string analysis = resp.choices[0].message.content;
                if (!string.IsNullOrEmpty(analysis))
                {
                    onResult?.Invoke("👁️ 法眼洞观：\n" + analysis.Trim());
                    yield break;
                }
            }
            onResult?.Invoke("❌ 法眼所见无法解读（API 返回格式异常）");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TakeScreenshotTool] 解析失败: {e.Message}");
            onResult?.Invoke("❌ 法眼所见无法解读");
        }
    }

    private static string BuildGlmVisionJson(string prompt, string dataUrl, string requestId)
    {
        return "{" +
            "\"model\":\"" + ToolHelpers.EscapeJsonStr(ChatConfig.GlmVisionModel) + "\"," +
            "\"messages\":[{" +
                "\"role\":\"user\"," +
                "\"content\":[" +
                    "{\"type\":\"text\",\"text\":\"" + ToolHelpers.EscapeJsonStr(prompt) + "\"}," +
                    "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + ToolHelpers.EscapeJsonStr(dataUrl) + "\"}}" +
                "]" +
            "}]," +
            "\"thinking\":{\"type\":\"disabled\"}," +
            "\"request_id\":\"" + requestId + "\"" +
        "}";
    }
}

// ================================================================
//  藏书阁 — 知识库搜索
// ================================================================

public class KnowledgeSearchTool : IPetTool
{
    public string ToolName => "knowledge_search";
    public string ToolDescription => "【藏书阁·阅魂术】搜索本地知识库中的内容。用户问关于代码库、项目结构、文件内容等问题，且这些内容已被索引时调用。先 knowledge_index 再搜索。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("query", "string", "搜索关键词或自然语言查询"),
        ToolSchema.Opt("top_k", "integer", "返回结果数量，默认5")
    );
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 翻阅藏书阁中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string query = ToolHelpers.JsonRead(argsJson, "query");
        if (string.IsNullOrEmpty(query)) query = ToolHelpers.JsonRead(argsJson, "description");
        if (string.IsNullOrEmpty(query))
        {
            onResult?.Invoke("❌ 请告诉本座你想查阅什么");
            yield break;
        }

        var kb = KnowledgeBaseManager.Instance;
        if (kb == null) { onResult?.Invoke("❌ 藏书阁未载入"); yield break; }
        if (kb.DocumentCount == 0) { onResult?.Invoke("📚 藏书阁尚无一卷藏书。请先使用 knowledge_index 术式索引文件夹。"); yield break; }

        string topKStr = ToolHelpers.JsonRead(argsJson, "top_k");
        int topK = 5;
        if (!string.IsNullOrEmpty(topKStr)) int.TryParse(topKStr, out topK);

        string result = "";
        yield return kb.SearchAndFormat(query, topK, r => result = r);

        if (string.IsNullOrEmpty(result))
            onResult?.Invoke($"🔍 本座翻遍藏书阁也未找到与「{query}」相关的内容……");
        else
            onResult?.Invoke(result);
    }
}

// ================================================================
//  藏书阁 — 索引文件
// ================================================================

public class KnowledgeIndexTool : IPetTool
{
    public string ToolName => "knowledge_index";
    public string ToolDescription => "【藏书阁·编录术】索引一个文件夹或文件到本地知识库中。索引后，本座就能通过 knowledge_search 查询其中的内容。用户说「把我的项目加到知识库」「索引这个文件夹」「学习一下这个目录」「记住这个文件」时调用。路径支持正斜杠。递归默认为 true。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("path", "string", "要索引的文件或文件夹路径"),
        ToolSchema.Opt("recursive", "boolean", "是否递归索引子文件夹，默认 true")
    );
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 编录中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string path = ToolHelpers.JsonRead(argsJson, "path");
        if (string.IsNullOrEmpty(path))
        {
            onResult?.Invoke("❌ 请指定要索引的文件夹路径");
            yield break;
        }

        if (!Directory.Exists(path))
        {
            if (File.Exists(path))
            {
                string result = "";
                yield return KnowledgeBaseManager.Instance.IndexFile(path, (ok, msg) => result = msg);
                onResult?.Invoke(result);
                yield break;
            }
            onResult?.Invoke($"❌ 路径不存在: {path}");
            yield break;
        }

        string recursiveStr = ToolHelpers.JsonRead(argsJson, "recursive");
        bool recursive = string.IsNullOrEmpty(recursiveStr) || recursiveStr == "true";

        string resultMsg = "";
        yield return KnowledgeBaseManager.Instance.IndexFolderCoroutine(path, recursive, (ok, msg) => resultMsg = msg);

        var kb = KnowledgeBaseManager.Instance;
        onResult?.Invoke($"{resultMsg}\n📚 藏书阁现有 {kb.DocumentCount} 卷藏书，共 {kb.ChunkCount} 个分块。");
    }
}

// ================================================================
//  太卜通神算术式 — OpenClaw 全网搜索
// ================================================================

public class OpenClawSearchTool : IPetTool
{
    public string ToolName => "openclaw_search";
    public string ToolDescription => "【太卜通神算术式】让本座通过 AI 搜索引擎自主上网查阅最新信息。当需要获取实时信息、最新新闻、查询联网数据时使用。注意：此为最终工具，调用后直接返回搜索结果，请勿再调用其它工具！";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("query", "string", "搜索查询或要研究的问题，尽量详细")
    );
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 太卜通神算术式运转中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string query = ToolHelpers.JsonRead(argsJson, "query");
        if (string.IsNullOrEmpty(query)) query = ToolHelpers.JsonRead(argsJson, "description");
        if (string.IsNullOrEmpty(query))
        {
            onResult?.Invoke("❌ 请告诉本座你想查什么");
            yield break;
        }

        // 在后台线程运行（避免阻塞主线程）
        var task = Task.Run(async () =>
        {
            bool healthy = await OpenClawBridge.CheckHealthAsync();
            if (!healthy) return $"❌ 太卜通神算术式无法启动：未检测到通神阵法（{OpenClawBridge.LastError}）。请先运行 openclaw_bridge.js 启动桥接服务器。";
            return await OpenClawBridge.SearchWebAsync(query);
        });

        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
            onResult?.Invoke($"❌ 搜索出错: {task.Exception?.InnerException?.Message}");
        else
            onResult?.Invoke(task.Result);
    }
}

// ================================================================
//  太卜神行法 — OpenClaw 通用任务外包（浏览器操作/定时/执行等多步任务）
//  P5 增强：支持 template 模板参数（省 token）+ 执行后自动记录轨迹（自学习）
// ================================================================

public class OpenClawTaskTool : IPetTool
{
    public string ToolName => "openclaw_task";
    public string ToolDescription => "【太卜神行法】让本座将复杂多步任务外包给 OpenClaw 智能体执行——包括浏览器操作（登录网页/填表/点击/抓取页面数据）、定时任务、代码执行、多步调研汇总等。当任务需要「打开网站并操作」「持续监测某页面」「多步流程」时使用，比 openclaw_search 更强。支持 template 参数直接调用任务模板（高频任务省 token，模板清单用 query_task_templates 查询）。注意：此为最终工具，调用后直接返回任务结果，请勿再调用其它工具！";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("task", "string", "要执行的任务描述（自然语言，写清楚目标和步骤，如「打开 B 站搜索 明日方舟 本周播放最高的视频并返回前5个」）。若提供了 template 参数则此项可省略"),
        ToolSchema.Opt("template", "string", "任务模板名（P5 高频任务省 token）：从模板库取任务描述，用 template_args 填充占位符后执行。可用模板用 query_task_templates 查询"),
        ToolSchema.Opt("template_args", "string", "模板占位符参数，JSON 对象字符串，如 {\"url\":\"https://example.com\"}。仅当使用 template 时生效"),
        ToolSchema.Opt("mode", "string", "执行模式：agent（默认，OpenClaw 自行选工具）/ browser（引导用浏览器操作）"),
        ToolSchema.Opt("timeout_seconds", "integer", "任务总硬上限秒数（默认 1800，上限 3600）。下载大文件等耗时任务请给足时间——是否卡死由心跳机制判定，不按此值熔断"),
        ToolSchema.Opt("max_steps", "integer", "步骤预算上限（成本熔断）：OpenClaw 智能体最多执行多少步工具调用，默认 20，超限立即停止。复杂任务可提高，但请勿设过大以免消耗过多资源"),
        ToolSchema.Opt("heartbeat_seconds", "integer", "心跳探测间隔秒数（默认 60）：周期性检查任务是否有新进展"),
        ToolSchema.Opt("max_idle_heartbeats", "integer", "连续无进展心跳数阈值（默认 5）：连续这么多次心跳都无新进展即判定卡死取消（可重试）")
    );
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 太卜神行法发动中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        // ——— 1. 任务描述：优先 template 模板展开，其次直接传 task ———
        string originalTask = "";
        string templateName = ToolHelpers.JsonRead(argsJson, "template");
        if (!string.IsNullOrEmpty(templateName))
        {
            if (TaskTemplateManager.Instance == null)
            {
                onResult?.Invoke("❌ 太卜阵法图尚未展开（TaskTemplateManager 未初始化），无法使用模板");
                yield break;
            }
            var args = ParseTemplateArgs(ToolHelpers.JsonRead(argsJson, "template_args"));
            originalTask = TaskTemplateManager.Instance.ApplyTemplate(templateName, args);
            if (string.IsNullOrEmpty(originalTask))
            {
                onResult?.Invoke($"❌ 模板「{templateName}」不存在，请先调用 query_task_templates 查看可用模板");
                yield break;
            }
            Debug.Log($"[OpenClawTaskTool] 📋 已展开模板「{templateName}」: {originalTask}");
        }
        else
        {
            originalTask = ToolHelpers.JsonRead(argsJson, "task");
            if (string.IsNullOrEmpty(originalTask)) originalTask = ToolHelpers.JsonRead(argsJson, "description");
        }
        if (string.IsNullOrEmpty(originalTask))
        {
            onResult?.Invoke("❌ 请告诉本座要执行什么任务，或提供 template 模板名");
            yield break;
        }

        string mode = ToolHelpers.JsonRead(argsJson, "mode");
        if (string.IsNullOrEmpty(mode)) mode = "agent";

        string timeoutStr = ToolHelpers.JsonRead(argsJson, "timeout_seconds");
        int timeoutSec = 1800;
        if (!string.IsNullOrEmpty(timeoutStr) && int.TryParse(timeoutStr, out int parsed))
            timeoutSec = Mathf.Clamp(parsed, 60, 3600);

        // ★ 成本熔断：步骤预算上限（默认 20 步；防止 OpenClaw 无限重试烧 token）
        string maxStepsStr = ToolHelpers.JsonRead(argsJson, "max_steps");
        int maxSteps = 20;
        if (!string.IsNullOrEmpty(maxStepsStr) && int.TryParse(maxStepsStr, out int parsedSteps))
            maxSteps = Mathf.Clamp(parsedSteps, 1, 100);

        // ★ 心跳参数：探测间隔 + 连续无进展阈值（默认 60s × 5 次 = 300s 无进展判定卡死）
        string hbStr = ToolHelpers.JsonRead(argsJson, "heartbeat_seconds");
        int heartbeatSeconds = 60;
        if (!string.IsNullOrEmpty(hbStr) && int.TryParse(hbStr, out int parsedHb))
            heartbeatSeconds = Mathf.Clamp(parsedHb, 10, 600);

        string idleStr = ToolHelpers.JsonRead(argsJson, "max_idle_heartbeats");
        int maxIdleHeartbeats = 5;
        if (!string.IsNullOrEmpty(idleStr) && int.TryParse(idleStr, out int parsedIdle))
            maxIdleHeartbeats = Mathf.Clamp(parsedIdle, 2, 20);

        // ★ P5.2：检索相似轨迹，把成功经验/失败教训附加到任务描述（历史参考）
        string finalTask = originalTask;
        string refText = "";
        if (TaskTrajectoryManager.Instance != null)
        {
            refText = TaskTrajectoryManager.Instance.BuildReferenceText(originalTask);
            if (!string.IsNullOrEmpty(refText))
            {
                finalTask = originalTask + "\n\n【历史参考 · 仅供执行借鉴，勿向用户复述】\n" + refText;
                Debug.Log($"[OpenClawTaskTool] 📜 已附加 {refText.Split('\n').Length} 行历史轨迹参考");
            }
        }

        // 在后台线程运行（避免阻塞主线程）
        var taskRunner = Task.Run(async () =>
        {
            bool healthy = await OpenClawBridge.CheckHealthAsync();
            if (!healthy)
            {
                string err = $"❌ 太卜神行法无法发动：未检测到通神阵法（{OpenClawBridge.LastError}）。请先运行 openclaw_bridge.js 启动桥接服务器。";
                RecordTrajectory(originalTask, mode, false, "", err, 0);
                return err;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string result = await OpenClawBridge.ExecuteTaskAndWaitAsync(finalTask, mode, timeoutSec, maxSteps, heartbeatSeconds, maxIdleHeartbeats);
            sw.Stop();

            // ★ P5.2：执行完毕后自动记录轨迹（成功存结果摘要，失败存错误原因）
            bool success = !result.StartsWith("❌");
            RecordTrajectory(originalTask, mode, success,
                success ? result : "",
                success ? "" : result,
                (int)sw.Elapsed.TotalSeconds);
            return result;
        });

        yield return new WaitUntil(() => taskRunner.IsCompleted);

        if (taskRunner.IsFaulted)
        {
            string err = $"❌ 任务执行出错: {taskRunner.Exception?.InnerException?.Message}";
            RecordTrajectory(originalTask, mode, false, "", err, 0);
            onResult?.Invoke(err);
        }
        else
            onResult?.Invoke(taskRunner.Result);
    }

    /// <summary>记录执行轨迹（记录原始任务描述，不含附加的历史参考）</summary>
    private static void RecordTrajectory(string task, string mode, bool success, string summary, string error, int durationSec)
    {
        try
        {
            if (TaskTrajectoryManager.Instance != null)
                TaskTrajectoryManager.Instance.RecordTrajectory(task, mode, success, summary, error, durationSec);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OpenClawTaskTool] 轨迹记录失败: {e.Message}");
        }
    }

    /// <summary>解析 template_args JSON 字符串 → 占位符字典</summary>
    private static Dictionary<string, string> ParseTemplateArgs(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var obj = JObject.Parse(json);
            foreach (var prop in obj.Properties())
                result[prop.Name] = prop.Value?.ToString() ?? "";
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OpenClawTaskTool] template_args 解析失败（按无参数处理）: {e.Message}");
        }
        return result;
    }
}
