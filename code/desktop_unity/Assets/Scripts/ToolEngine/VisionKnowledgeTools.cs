using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
