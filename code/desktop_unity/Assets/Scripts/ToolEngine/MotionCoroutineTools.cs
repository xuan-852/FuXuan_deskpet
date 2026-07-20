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
//  演武：根据描述生成并播放动作
// ================================================================

public class GenerateMotionTool : IPetTool
{
    public string ToolName => "generate_motion";
    public string ToolDescription => "【法身·演武】根据自然语言描述生成并播放动作。不但会动，还能自动截图并送 GLM-4V 考官评价质量（闭环自评）。用户说「做个开心的动作」「演武」「跳个舞」「挥手」时调用。需要文字描述，越详细越好。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""description"":{""type"":""string"",""description"":""动作描述，如「开心地挥手」「害羞地遮脸」「叉腰生气」""},""duration"":{""type"":""number"",""description"":""持续时间（秒），默认3秒""}},""required"":[""description""]}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 演武中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || renderer.CubismModel == null)
        {
            onResult?.Invoke("❌ 本座法身未现，无法演武");
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;

        string description = ToolHelpers.JsonRead(argsJson, "description");
        string durationStr = ToolHelpers.JsonRead(argsJson, "duration");
        float duration = 3f;
        if (!string.IsNullOrEmpty(durationStr))
            float.TryParse(durationStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out duration);

        if (string.IsNullOrEmpty(description))
        {
            onResult?.Invoke("❌ 请描述想要的动作");
            yield break;
        }

        // 规划动作
        var plan = MotionPlanner.PlanFromDescription(description, duration, mapper);
        if (plan == null || plan.KeyFrames.Count == 0)
        {
            onResult?.Invoke($"❌ 未能理解「{description}」的演武方式");
            yield break;
        }

        // 如果回退到泛用微动，尝试 LLM 翻译
        if (plan.Description == "泛用微动" || plan.KeyFrames.Count <= 2)
        {
            MotionPlanner.MotionPlan llmPlan = null;
            yield return MotionTranslator.TranslateAsync(description, mapper, model, duration, p => llmPlan = p);
            if (llmPlan != null && llmPlan.KeyFrames.Count > 2)
            {
                plan = llmPlan;
                Debug.Log($"[GenerateMotionTool] LLM 翻译成功：「{description}」→ {plan.KeyFrames.Count} 帧");
            }
        }

        if (duration > 0.5f) plan.TotalDuration = duration;

        // 设置 AI 控制锁
        renderer.SetAiControlLock(plan.TotalDuration + 1f);

        // 多帧截图（20%/40%/60%/80% 进度）
        var framePngs = new List<byte[]>();
        var capturePoints = new float[] { 0.20f, 0.40f, 0.60f, 0.80f };
        var generator = new MotionGenerator(mapper, model);
        yield return generator.PlayAsync(plan, progress =>
        {
            for (int i = 0; i < capturePoints.Length; i++)
            {
                if (i >= framePngs.Count && progress >= capturePoints[i])
                {
                    if (progress >= 0.60f) model.ForceUpdateNow();
                    framePngs.Add(renderer.CaptureModelSnapshot());
                }
            }
        });

        string baseResult = $"✅ 演武完成：「{plan.Description}」，持续 {plan.TotalDuration:F1} 秒，共 {plan.KeyFrames.Count} 个关键帧";

        // GLM 闭环自评
        var validator = GameObject.FindObjectOfType<DualModelValidator>();
        string collageDataUrl = DualModelValidator.ComposeCollage(framePngs);
        if (collageDataUrl != null && validator != null)
        {
            bool consensus = false;
            int avgScore = 0, sGlm = 0;
            string rGlm = "";
            yield return validator.ValidateAsync(description, collageDataUrl, plan,
                (c, avg, g, _u1, _u2, rg, _rq) => { consensus = c; avgScore = avg; sGlm = g; rGlm = rg; });

            string result = baseResult + $"\n\n👁️ 自评反馈：{rGlm}";

            // 闭环学习
            var mm = MotionMemoryManager.Instance;
            if (mm != null && plan != null && consensus)
            {
                string snapshot = ExtractPlanSnapshot(plan);
                mm.RecordMotion(description, snapshot, plan.KeyFrames.Count, plan.TotalDuration);
                mm.UpdateScore(description, avgScore, $"多帧镜鉴{sGlm}/5\n{rGlm}", snapshot);
            }

            onResult?.Invoke(result);
        }
        else
        {
            onResult?.Invoke(baseResult + "\n\nℹ️ 自评暂不可用，下次演武时自动重试。");
        }
    }

    private static string ExtractPlanSnapshot(MotionPlanner.MotionPlan plan)
    {
        if (plan == null || plan.KeyFrames.Count == 0) return "";
        int midIdx = Mathf.Clamp(plan.KeyFrames.Count / 2, 0, plan.KeyFrames.Count - 1);
        var midKf = plan.KeyFrames[midIdx];
        if (midKf.Values.Count == 0) return "";
        var topParams = midKf.Values
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .Take(5)
            .Select(kv => $"{kv.Key}={kv.Value:F2}");
        return string.Join(", ", topParams);
    }
}

// ================================================================
//  内观自省：截图 → GLM-4V 分析身体部位
// ================================================================

public class ExploreBodyVisionTool : IPetTool
{
    public string ToolName => "explore_body_vision";
    public string ToolDescription => "【法身·内观自省 GLM】截取法身当前渲染图 → GLM-4V 视觉分析各部位状态 → 输出详细分析。比同步版 explore_body 更直观，能看到具体画面。用户说「AI看看我现在」「帮我看看我的姿势」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""focus"":{""type"":""string"",""description"":""关注重点，如 face/hand/body/overall，可选""}},""required"":[]}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 本座正在内观……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || renderer.CubismModel == null)
        {
            onResult?.Invoke("❌ 本座法身未现");
            yield break;
        }

        // 截图
        byte[] pngBytes = renderer.CaptureModelSnapshot();
        if (pngBytes == null || pngBytes.Length == 0)
        {
            onResult?.Invoke("❌ 无法摄形");
            yield break;
        }
        string dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);

        // 构建参数快照（供 GLM 参考）
        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;
        string paramSnapshot = BuildParamSnapshot(mapper);

        string focus = ToolHelpers.JsonRead(argsJson, "focus");
        if (string.IsNullOrEmpty(focus)) focus = "overall";

        string prompt =
            "你是一名 Live2D 模型调试专家。请严格分析这张桌面宠物（符玄/玄机）的当前渲染截图，逐区域回答：\n\n"
            + "1. **面部朝向与视线**：脸向左/右/前？视线方向？眉毛、眼睛形状？\n"
            + "2. **嘴巴与表情**：嘴张开程度？整体情绪（平静/微笑/惊讶/生气）？\n"
            + "3. **头部姿态**：头向左/右转？上扬/低头？\n"
            + "4. **身体朝向**：躯干朝左/右/前？\n"
            + "5. **头发与飘带**：头发/飘带的动态状态？\n"
            + "6. **手部**：手的位置高度，手指形态？\n"
            + "7. **其他明显特征**：是否有特殊效果、表情切换、动作播放？\n\n"
            + $"当前激活参数以供参考（参数名=当前值）：\n{paramSnapshot}\n\n"
            + "请指出哪些参数需要调整以达到期望的表情/姿态，并给出具体的参数方向和幅度建议。\n"
            + "回复格式：先输出分析结论，再以 ###参数调整建议### 开头列出建议。";

        // GLM 请求
        string requestId = Guid.NewGuid().ToString("N");
        string jsonBody = "{" +
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
                onResult?.Invoke("❌ 内观受阻：" + errMsg);
                yield break;
            }
        }

        try
        {
            var resp = JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp?.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
            {
                string analysis = resp.choices[0].message.content;
                if (!string.IsNullOrEmpty(analysis))
                {
                    onResult?.Invoke("🧘 内观自省之果：\n" + analysis.Trim());
                    yield break;
                }
            }
            onResult?.Invoke("❌ 内观所见无法解读（API 返回格式异常）");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ExploreBodyVisionTool] 解析失败: {e.Message}");
            onResult?.Invoke("❌ 内观所见无法解读");
        }
    }

    private static string BuildParamSnapshot(Live2DParameterMapper mapper)
    {
        if (mapper == null) return "（无参数数据）";
        var activeLines = new List<string>();
        var entryPartMap = new Dictionary<string, string>();
        var mapAsset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
        if (mapAsset != null)
        {
            try
            {
                var mapObj = JsonUtility.FromJson<FuxuanMapData>(mapAsset.text);
                if (mapObj?.entries != null)
                    foreach (var e in mapObj.entries)
                        if (!string.IsNullOrEmpty(e.part))
                            entryPartMap[e.s] = e.part;
            }
            catch { }
        }

        int count = 0;
        foreach (var semantic in mapper.SemanticToId.Keys)
        {
            if (!mapper.TryGetRange(semantic, out var range)) continue;
            float current = mapper.Get(semantic);
            if (Mathf.Abs(current - range.Default) > 0.02f && count < 30)
            {
                string part = entryPartMap.TryGetValue(semantic, out var p) ? p : "unknown";
                activeLines.Add($"• {semantic} = {current:F2} (区域:{part}, 默认:{range.Default:F2})");
                count++;
            }
        }

        string result = string.Join("\n", activeLines);
        return string.IsNullOrEmpty(result) ? "（所有参数均在默认值附近）" : result;
    }
}

// ================================================================
//  具身验证：运行完整验证套件
// ================================================================

public class RunVerificationTool : IPetTool
{
    public string ToolName => "run_verification";
    public string ToolDescription => "【验阵校验·已废弃】快速校验 5 个硬编码模板（挥手/点头/摇头/鞠躬/伸懒腰）的参数范围。注意：此工具仅检查模板参数是否在范围内，不像 vis_verify 那样让 GLM-4V 考官实际观察 AI 执行效果。已被 vis_verify 取代！如果已经执行过 vis_verify，无需再调此工具。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""tier"":{""type"":""string"",""description"":""验证级别：quick（摘要）/ full（完整报告，可能很长），默认 quick""}},""required"":[]}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 验阵中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string tier = ToolHelpers.JsonRead(argsJson, "tier");
        if (string.IsNullOrEmpty(tier)) tier = "quick";

        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || !renderer.Mapper.IsLoaded)
        {
            onResult?.Invoke("❌ 本座法身未现，无法验证");
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;

        string fullReport = MotionVerifier.RunVerificationSuite(mapper, model);

        if (tier == "quick")
        {
            onResult?.Invoke(MotionVerifier.GetCompactSummary(mapper));
        }
        else
        {
            string report = fullReport;
            if (report.Length > 3000)
                report = report[..3000] + "\n...（报告截断，完整版见控制台）";
            Debug.Log($"[RunVerificationTool] 具身验证报告 ({tier}):\n{fullReport}");

            if (fullReport.Contains("❌") && !tier.Equals("quick"))
                onResult?.Invoke("⚠️ 部分测试未通过！\n\n" + report);
            else
                onResult?.Invoke(report);
        }
    }
}

// ================================================================
//  视觉具身验证：GLM-4V 考官检验
// ================================================================

public class VisVerifyTool : IPetTool
{
    public string ToolName => "vis_verify";
    public string ToolDescription => "【视觉具身验证·推荐】让本座依次播放所有运动模板并截取执行截图，逐帧送 GLM-4V 考官验证执行质量。用户说「验证一下动作」「检查动作质量」「做个全面体检」「动作测试」时调用。包含闭环学习，结果写入演武心经。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""mode"":{""type"":""string"",""description"":""验证模式：test_only（默认，完整验证）/ quick（返回上次缓存摘要）""}},""required"":[]}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 法阵验证中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string mode = ToolHelpers.JsonRead(argsJson, "mode");
        if (string.IsNullOrEmpty(mode)) mode = "test_only";

        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || !renderer.Mapper.IsLoaded)
        {
            onResult?.Invoke("❌ 本座法身未现，无法进行视觉验证");
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;

        // 有缓存且是 quick 模式，返回上次摘要
        if (mode == "quick" && !string.IsNullOrEmpty(VisionMotionVerifier.LastReport))
        {
            string cachedSummary = VisionMotionVerifier.LastReport;
            if (cachedSummary.Length > 2000)
                cachedSummary = cachedSummary[..2000] + "\n...（截断）";
            onResult?.Invoke(cachedSummary);
            yield break;
        }

        string report = null;
        yield return VisionMotionVerifier.RunVisionVerification(
            mapper, model, renderer,
            onProgress: (idx, total, name) => Debug.Log($"[vis_verify] ({idx}/{total}) {name}"),
            onResult: r => report = r
        );

        if (string.IsNullOrEmpty(report))
        {
            onResult?.Invoke("❌ 视觉验证未产生报告");
            yield break;
        }

        string result = report;
        if (result.Length > 3000)
            result = result[..3000] + "\n...（报告截断，完整版见控制台）";

        result += "\n\n---\n✅ 视觉验证已完成，无需再调用「验阵校验」（run_verification），本座法阵已经过 GLM 考官检验。";

        // 保存完整报告
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "vis_verify_report.md");
        File.WriteAllText(filePath, report);
        Debug.Log($"[vis_verify] ✅ 完整报告已保存: {filePath}");

        // 闭环学习
        var mm = MotionMemoryManager.Instance;
        if (mm != null)
        {
            var results = VisionMotionVerifier.LastResults;
            if (results != null)
            {
                foreach (var r in results)
                {
                    if (r.Score > 0 && !string.IsNullOrEmpty(r.ChineseName))
                    {
                        string snapshot = $"{r.KeyFrameCount}帧/vis_verify";
                        bool isNewBest = mm.UpdateScore(r.ChineseName, r.Score, r.GlmJudgment ?? "", snapshot);
                        string badge = isNewBest ? "🏆" : "📝";
                        Debug.Log($"[vis_verify] {badge} 闭环学习: 「{r.ChineseName}」→ {r.Score}/5" + (isNewBest ? " ★ 新纪录！" : ""));
                    }
                }
            }
        }

        onResult?.Invoke(result);
    }
}

// ================================================================
//  自省：播放指定动作并让 GLM-4V 评价执行质量
// ================================================================

public class SelfReviewTool : IPetTool
{
    public string ToolName => "self_review";
    public string ToolDescription => "【法身·自省】播放指定动作模板并截图，送 GLM-4V 审视线下执行效果。用户说「看看我刚才做得怎么样」「评审这个动作」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""action"":{""type"":""string"",""description"":""要评价的动作模板名称，如 wave/nod/bow 等""}},""required"":[""action""]}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 自省中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string actionName = ToolHelpers.JsonRead(argsJson, "action");
        if (string.IsNullOrEmpty(actionName))
        {
            onResult?.Invoke("❌ 未指定要评价的动作");
            yield break;
        }

        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || renderer.CubismModel == null)
        {
            onResult?.Invoke("❌ 本座法身未现");
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;

        // 停止当前动作，等待一帧
        renderer.ActionController?.StopAllWithFade();
        yield return null;

        // 播放目标动作
        renderer.PlayAction(actionName);

        // 等待动作达到峰值（默认 2 秒）
        yield return new WaitForSeconds(1.2f);

        // 截图
        byte[] pngBytes = renderer.CaptureModelSnapshot();
        if (pngBytes == null || pngBytes.Length == 0)
        {
            onResult?.Invoke("❌ 无法摄形");
            yield break;
        }
        string dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);

        string prompt = "你是一名动作评审专家。下面给你一张桌面宠物（符玄/玄机）的动作截图。\n\n"
            + "AI 被要求做出这个动作：**「" + actionName + "」**\n\n"
            + "请仔细观察截图，这张截图是在动作播放到峰值时刻（约60%进度）抓取的，你应该能看到该动作最明显的姿态。\n\n"
            + "回答：\n\n"
            + "1. **这个姿势像不像「" + actionName + "」？** （是/基本是/不太像/完全不像）\n"
            + "2. **你从哪里看出来？**（指出画面中哪些部位/角度让你做此判断）\n"
            + "3. **如果不像，你觉得更像什么动作？**\n"
            + "4. **给这个动作的执行质量打分（1~5分）：**\n"
            + "   5分 = 完美还原，一看就是「" + actionName + "」\n"
            + "   4分 = 基本到位，有轻微偏差\n"
            + "   3分 = 有点意思但不够准确\n"
            + "   2分 = 只有一点点关联\n"
            + "   1分 = 完全不像\n\n"
            + "=== 严格评分规则 ===\n"
            + "- 5分：动作特征非常明显，非此动作不可能误解\n"
            + "- 4分：主要动作特征到位，但有一两个细节不完美\n"
            + "- 3分：能看到一些设计意图，但整体不够清楚\n"
            + "- 2分：需要仔细看才能勉强联想到目标动作\n"
            + "- 1分：看不出在做什么，或看起来像完全不同的动作\n\n"
            + "=== 回复格式（严格按此格式） ===\n"
            + "判断：【是/基本是/不太像/完全不像】\n"
            + "理由：...\n"
            + "更像什么：...\n"
            + "打分：【X/5】← 注意 X 是 1~5 的数字，不要有空格\n"
            + "改进建议：...";

        // GLM 请求
        string requestId = Guid.NewGuid().ToString("N");
        string jsonBody = "{" +
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
                onResult?.Invoke("❌ 自省受阻：" + errMsg);
                yield break;
            }
        }

        try
        {
            var resp = JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp?.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
            {
                string analysis = resp.choices[0].message.content;
                if (!string.IsNullOrEmpty(analysis))
                {
                    onResult?.Invoke("🔍 自省对比「" + actionName + "」：\n" + analysis.Trim());
                    yield break;
                }
            }
            onResult?.Invoke("❌ 自省所见无法解读（API 返回格式异常）");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SelfReviewTool] 响应解析失败: {e.Message}");
            onResult?.Invoke("❌ 自省所见无法解读");
        }
    }
}
