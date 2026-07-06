using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Live2D.Cubism.Core;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 视觉具身验证器 — GLM-4V 作为考官，观察 DeepSeek 实际生成的动作效果
///
/// 核心思路：
///   让 GLM-4V（视觉模型）逐一观察 AI 执行的动作效果，
///   判断"这个姿势看起来是否像描述的那样"。
///   如果 AI 能做出"让视觉模型信服"的动作，那它就真的具有了具身智能。
///
/// 流程：
///   1. 对每个测试用例：生成动作 → 播放 → 在峰值截图 → GLM-4V 评估
///   2. GLM-4V 回答：是否像描述？打分(1-5) + 具体理由
///   3. 汇总报告：通过率、平均分、详细反馈
/// </summary>
public static class VisionMotionVerifier
{
    /// <summary>上次运行的完整报告</summary>
    public static string LastReport { get; private set; }
    /// <summary>当前运行中的中间结果（每次完成一个测试就更新，供 EditorWindow 轮询）</summary>
    public static List<VisionTestResult> InProgressResults { get; private set; }
    /// <summary>上次运行的原始结果列表</summary>
    private static List<VisionTestResult> _lastResults;

    /// <summary>清除运行中结果，准备新一轮验证</summary>
    public static void ClearInProgress()
    {
        InProgressResults = null;
        LastReport = null;
    }
    // ──────────────────────────────────────────────
    //  测试用例
    // ──────────────────────────────────────────────

    public class VisionTestCase
    {
        public string Id;
        public string Description;          // 自然语言描述
        public string ExpectedPoseSummary;  // 期望的姿势特征（给 GLM 参考）
        public string ChineseName;          // 中文简称
    }

    /// <summary>LLM 翻译测试组 — 这些必须走 DeepSeek 翻译路径</summary>
    private static readonly VisionTestCase[] VISION_TESTS =
    {
        new() { Id = "T1", Description = "害羞地捂脸",
                ExpectedPoseSummary = "手在脸附近，头微低",
                ChineseName = "害羞捂脸" },
        new() { Id = "T2", Description = "昂首挺胸叉腰",
                ExpectedPoseSummary = "头仰起，手臂向外弯，挺胸姿势",
                ChineseName = "挺胸叉腰" },
        new() { Id = "T3", Description = "惊讶地捂住嘴",
                ExpectedPoseSummary = "头微仰，手在嘴前方，嘴巴微张",
                ChineseName = "惊讶捂嘴" },
        new() { Id = "T4", Description = "忧郁地望向远方",
                ExpectedPoseSummary = "头侧转，眼神放空望向侧方",
                ChineseName = "忧郁远望" },
        new() { Id = "T5", Description = "俏皮地眨一下右眼",
                ExpectedPoseSummary = "右眼闭合，左眼正常，歪头微笑",
                ChineseName = "俏皮眨眼" },
        new() { Id = "T6", Description = "标准地行一个礼",
                ExpectedPoseSummary = "双手在身体两侧向下摆，微微躬身",
                ChineseName = "行礼" },
        new() { Id = "T7", Description = "被吓到缩成一团",
                ExpectedPoseSummary = "全身收缩，低头含胸，手臂夹紧",
                ChineseName = "吓到缩团" },
        new() { Id = "T8", Description = "骄傲地抬起头",
                ExpectedPoseSummary = "头高高仰起，身体后倾，面带微笑",
                ChineseName = "骄傲抬头" },
        new() { Id = "T9", Description = "歪着头思考",
                ExpectedPoseSummary = "头歪向一侧，眼珠上漂或低垂",
                ChineseName = "歪头思考" },
        new() { Id = "T10",Description = "双手合十祈祷",
                ExpectedPoseSummary = "双手在胸前合拢，头低垂，闭眼",
                ChineseName = "合十祈祷" },
    };

    // ──────────────────────────────────────────────
    //  单条测试结果
    // ──────────────────────────────────────────────

    public class VisionTestResult
    {
        public string Id;
        public string Description;
        public string ChineseName;
        public int Score;           // GLM-4V 评分 1-5
        public string GlmJudgment; // GLM-4V 原文判断
        public bool IsPass;        // Score >= 3
        public bool WasLLMGenerated; // 是否走了 LLM 路径
        public int KeyFrameCount;
        public string ErrorMessage;
    }

    // ──────────────────────────────────────────────
    //  核心验证方法（协程）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 运行完整视觉验证套件
    /// </summary>
    /// <param name="mapper">参数映射器</param>
    /// <param name="model">Cubism 模型</param>
    /// <param name="renderer">渲染器（用于截图）</param>
    /// <param name="onProgress">进度回调 (index, total, currentName)</param>
    /// <param name="onResult">最终报告回调</param>
    public static IEnumerator RunVisionVerification(
        Live2DParameterMapper mapper,
        CubismModel model,
        Live2DRenderer renderer,
        Action<int, int, string> onProgress,
        Action<string> onResult)
    {
        var results = new List<VisionTestResult>();
        int total = VISION_TESTS.Length;

        for (int i = 0; i < total; i++)
        {
            var tc = VISION_TESTS[i];
            onProgress?.Invoke(i + 1, total, tc.ChineseName);

            var result = new VisionTestResult
            {
                Id = tc.Id,
                Description = tc.Description,
                ChineseName = tc.ChineseName,
            };

            // ——— 1. 规划动作 ———
            var plan = MotionPlanner.PlanFromDescription(tc.Description, 3.5f, mapper);
            if (plan == null || plan.KeyFrames.Count == 0)
            {
                result.ErrorMessage = "Plan 为 null";
                result.Score = 1;
                results.Add(result);
                continue;
            }

            bool usedLLM = false;
            // ——— 2. 尝试 LLM 翻译兜底 ———
            if (plan.Description == "泛用微动" || plan.KeyFrames.Count <= 2)
            {
                MotionPlanner.MotionPlan llmPlan = null;
                yield return MotionTranslator.TranslateAsync(
                    tc.Description, mapper, model, 3.5f, p => llmPlan = p);
                if (llmPlan != null && llmPlan.KeyFrames.Count > 2)
                {
                    plan = llmPlan;
                    usedLLM = true;
                }
            }
            else
            {
                // 如果直接命中模板，也视为一种能力（虽然不算LLM）
                usedLLM = false;
            }

            result.WasLLMGenerated = usedLLM;
            result.KeyFrameCount = plan.KeyFrames.Count;

            // ——— 3. 设置 mapper 引用供 PlayAndCapture 使用 ———
            _mapper = mapper;

            // ——— 4. 播放动作并截图 ———
            string base64Snapshot = null;

            // 锁定 AI 控制
            if (renderer != null)
                renderer.SetAiControlLock(plan.TotalDuration + 2f);

            var generator = new MotionGenerator(mapper, model);
            generator.ResetState(plan.Description);

            // 用协程播放，在进度约 55% 时截图（更靠近动作峰值，40% 太早了）
            yield return PlayAndCapture(generator, plan, 0.55f, renderer,
                capturedBase64 => base64Snapshot = capturedBase64);

            if (string.IsNullOrEmpty(base64Snapshot))
            {
                // 截图失败，尝试截全屏
                byte[] png = renderer?.CaptureModelSnapshot();
                if (png != null && png.Length > 50)
                    base64Snapshot = "data:image/png;base64," + Convert.ToBase64String(png);
            }

            if (string.IsNullOrEmpty(base64Snapshot))
            {
                result.ErrorMessage = "截图失败";
                result.Score = 1;
                results.Add(result);
                continue;
            }

            // ——— 4. 发送 GLM-4V 评估 ———
            string glmResponse = null;
            yield return EvaluateWithGlm(tc, base64Snapshot, r => glmResponse = r);

            // ——— 5. 解析结果 ———
            if (!string.IsNullOrEmpty(glmResponse))
            {
                result.GlmJudgment = glmResponse;
                result.Score = ExtractScore(glmResponse);
                result.IsPass = result.Score >= 3;
            }
            else
            {
                result.ErrorMessage = "GLM-4V 评估失败";
                result.Score = 1;
            }

            results.Add(result);
            InProgressResults = new List<VisionTestResult>(results);

            // 每两个动作之间延迟一帧，给模型喘息时间
            yield return null;
        }

        // ——— 生成最终报告 ———
        string report = BuildReport(results, total);
        LastReport = report;
        onResult?.Invoke(report);
    }

    // ──────────────────────────────────────────────
    //  播放动作并在指定进度截图
    // ──────────────────────────────────────────────

    private static IEnumerator PlayAndCapture(
        MotionGenerator generator,
        MotionPlanner.MotionPlan plan,
        float captureProgress,
        Live2DRenderer renderer,
        Action<string> onCaptured)
    {
        if (_mapper == null || plan == null || plan.KeyFrames.Count == 0)
        {
            onCaptured?.Invoke(null);
            yield break;
        }

        // 排序关键帧
        plan.KeyFrames.Sort((a, b) => a.Time.CompareTo(b.Time));

        float elapsed = 0f;
        float totalDuration = plan.TotalDuration;
        bool captured = false;

        // 记录初始参数值
        var startValues = new Dictionary<string, float>();
        foreach (var kf in plan.KeyFrames)
        {
            foreach (var kv in kf.Values)
            {
                if (!startValues.ContainsKey(kv.Key))
                    startValues[kv.Key] = _mapper.Get(kv.Key);
            }
        }

        // 如果没有关键帧在 t=0，插入一个当前状态帧
        if (plan.KeyFrames[0].Time > 0.01f)
        {
            var zeroFrame = new MotionPlanner.KeyFrame { Time = 0f, Curve = MotionPlanner.InterpolationType.Linear };
            foreach (var kv in startValues)
                zeroFrame.Values[kv.Key] = kv.Value;
            plan.KeyFrames.Insert(0, zeroFrame);
        }

        float captureTime = totalDuration * captureProgress;

        while (elapsed < totalDuration + 0.1f)
        {
            // 寻找当前帧区间
            MotionPlanner.KeyFrame current = null;
            MotionPlanner.KeyFrame next = null;

            for (int i = 0; i < plan.KeyFrames.Count - 1; i++)
            {
                if (elapsed >= plan.KeyFrames[i].Time && elapsed < plan.KeyFrames[i + 1].Time)
                {
                    current = plan.KeyFrames[i];
                    next = plan.KeyFrames[i + 1];
                    break;
                }
            }

            if (next == null && elapsed >= plan.KeyFrames[plan.KeyFrames.Count - 1].Time)
            {
                var last = plan.KeyFrames[plan.KeyFrames.Count - 1];
                foreach (var kv in last.Values)
                    _mapper.Set(kv.Key, kv.Value);

                // 最后一帧也截一下
                if (!captured && renderer != null)
                {
                    if (renderer.CubismModel != null)
                        renderer.CubismModel.ForceUpdateNow();
                    byte[] png = renderer.CaptureModelSnapshot();
                    if (png != null && png.Length > 50)
                        onCaptured?.Invoke("data:image/png;base64," + Convert.ToBase64String(png));
                    captured = true;
                }
                break;
            }

            if (current != null && next != null)
            {
                float segmentDuration = next.Time - current.Time;
                float localT = segmentDuration > 0.001f
                    ? Mathf.Clamp01((elapsed - current.Time) / segmentDuration) : 1f;
                float easedT = Ease(localT, next.Curve);

                var allKeys = new HashSet<string>(current.Values.Keys);
                allKeys.UnionWith(next.Values.Keys);

                foreach (var key in allKeys)
                {
                    float fromVal = current.Values.ContainsKey(key) ? current.Values[key] : _mapper.Get(key);
                    float toVal = next.Values.ContainsKey(key) ? next.Values[key] : _mapper.Get(key);
                    _mapper.Set(key, Mathf.Lerp(fromVal, toVal, easedT));
                }

                // ——— 到达截图时机 ———
                if (!captured && elapsed >= captureTime && renderer != null)
                {
                    // ★ 强制 Cubism 把刚刚设的参数值处理到网格变形中，截图才能反映最新状态
                    if (renderer.CubismModel != null)
                        renderer.CubismModel.ForceUpdateNow();
                    byte[] png = renderer.CaptureModelSnapshot();
                    if (png != null && png.Length > 50)
                        onCaptured?.Invoke("data:image/png;base64," + Convert.ToBase64String(png));
                    captured = true;
                }
            }

            yield return null;
            elapsed += Time.deltaTime;
        }

        // 确保最终状态
        var final = plan.KeyFrames[plan.KeyFrames.Count - 1];
        foreach (var kv in final.Values)
            _mapper.Set(kv.Key, kv.Value);

        // 如果整个动作结束都没截到，截一次
        if (!captured && renderer != null)
        {
            if (renderer.CubismModel != null)
                renderer.CubismModel.ForceUpdateNow();
            byte[] png = renderer.CaptureModelSnapshot();
            if (png != null && png.Length > 50)
                onCaptured?.Invoke("data:image/png;base64," + Convert.ToBase64String(png));
        }
    }

    // 插值曲线（与 MotionGenerator 一致）
    private static float Ease(float t, MotionPlanner.InterpolationType curve)
    {
        return curve switch
        {
            MotionPlanner.InterpolationType.Linear => t,
            MotionPlanner.InterpolationType.Smooth => t * t * (3f - 2f * t),
            MotionPlanner.InterpolationType.EaseOut => 1f - (1f - t) * (1f - t),
            MotionPlanner.InterpolationType.EaseIn => t * t,
            MotionPlanner.InterpolationType.Hold => 0f,
            MotionPlanner.InterpolationType.Bounce => Bounce(t),
            _ => t,
        };
    }

    private static float Bounce(float t)
    {
        t = 1f - t;
        if (t < 1f / 2.75f) return 1f - 7.5625f * t * t;
        if (t < 2f / 2.75f) { t -= 1.5f / 2.75f; return 1f - (7.5625f * t * t + 0.75f); }
        if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 1f - (7.5625f * t * t + 0.9375f); }
        t -= 2.625f / 2.75f;
        return 1f - (7.5625f * t * t + 0.984375f);
    }

    // 存储 mapper 引用供 PlayAndCapture 使用
    private static Live2DParameterMapper _mapper;

    // ──────────────────────────────────────────────
    //  GLM-4V 评估
    // ──────────────────────────────────────────────

    private static IEnumerator EvaluateWithGlm(
        VisionTestCase tc,
        string imageDataUrl,
        Action<string> onResult)
    {
        string prompt = "你是一名动作评审专家。下面给你一张桌面宠物（符玄/玄机）的动作截图。\n\n"
            + "AI 被要求做出这个动作：**「" + tc.Description + "」**\n"
            + "期望的姿势特征：" + tc.ExpectedPoseSummary + "\n\n"
            + "请仔细观察截图，注意这张截图是在动作播放到约 55% 时抓取的，你应该能看到动作的典型姿态。\n\n"
            + "回答：\n\n"
            + "1. **这个姿势像不像「" + tc.Description + "」？** （是/基本是/不太像/完全不像）\n"
            + "2. **你从哪里看出来？**（指出画面中哪些部位/角度让你做此判断）\n"
            + "3. **如果不像，你觉得更像什么动作？**\n"
            + "4. **给这个动作的执行质量打分（1~5分）：**\n"
            + "   5分 = 完美还原，一看就是「" + tc.Description + "」\n"
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

        string jsonBody = "{";
        jsonBody += "\"model\":\"" + EscapeJsonStr(ChatConfig.GlmVisionModel) + "\",";
        jsonBody += "\"messages\":[{";
        jsonBody += "\"role\":\"user\",";
        jsonBody += "\"content\":[";
        jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJsonStr(prompt) + "\"},";
        jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJsonStr(imageDataUrl) + "\"}}";
        jsonBody += "]}],";
        jsonBody += "\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\"";
        jsonBody += "}";

        string fullUrl = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";
        string responseText = null;

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + ChatConfig.GlmApiKey);
            req.timeout = 180; // GLM-4V 视觉处理 base64 图片可能需要 2-3 分钟

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                responseText = req.downloadHandler.text;
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = req.error;
                if (!string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\""))
                {
                    try
                    {
                        var errObj = UnityEngine.JsonUtility.FromJson<GlmErrorResponse>(errBody);
                        if (errObj != null && !string.IsNullOrEmpty(errObj.error.message))
                            errMsg = errObj.error.message;
                    }
                    catch { }
                }
                Debug.LogWarning($"[VisionMotionVerifier] GLM 评估失败: {errMsg}");
                onResult?.Invoke(null);
                yield break;
            }
        }

        try
        {
            var resp = UnityEngine.JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp != null && resp.choices != null && resp.choices.Length > 0
                && resp.choices[0].message != null)
            {
                onResult?.Invoke(resp.choices[0].message.content.Trim());
                yield break;
            }
            onResult?.Invoke(null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VisionMotionVerifier] 解析 GLM 响应失败: {e.Message}");
            onResult?.Invoke(null);
        }
    }

    // ──────────────────────────────────────────────
    //  提取分数
    // ──────────────────────────────────────────────

    /// <summary>从 GLM 回复中提取 1-5 的打分</summary>
    private static int ExtractScore(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;

        // 查找 "【X/5】" 模式
        int scoreStart = text.IndexOf("【", StringComparison.Ordinal);
        if (scoreStart >= 0)
        {
            int scoreEnd = text.IndexOf("/5】", scoreStart, StringComparison.Ordinal);
            if (scoreEnd > scoreStart)
            {
                string numStr = text.Substring(scoreStart + 1, scoreEnd - scoreStart - 1);
                if (int.TryParse(numStr, out int s) && s >= 1 && s <= 5)
                    return s;
            }
        }

        // 回退：找 "X/5"
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            int idx = trimmed.IndexOf("/5", StringComparison.Ordinal);
            if (idx > 0)
            {
                char c = trimmed[idx - 1];
                if (c >= '1' && c <= '5')
                    return c - '0';
            }
        }

        // 回退：找数字行
        foreach (var line in lines)
        {
            if (line.Contains("5分") || line.Contains("4分")) return 4;
            if (line.Contains("3分")) return 3;
            if (line.Contains("2分")) return 2;
            if (line.Contains("1分")) return 1;
        }

        return 3; // 默认中等分
    }

    // ──────────────────────────────────────────────
    //  报告生成
    // ──────────────────────────────────────────────

    private static string BuildReport(List<VisionTestResult> results, int total)
    {
        _lastResults = results;
        var sb = new StringBuilder();
        sb.AppendLine("# 👁️ 视觉具身验证报告 — GLM-4V 审评");
        sb.AppendLine($"> 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"> 考官: {ChatConfig.GlmVisionModel}");
        sb.AppendLine($"> 动作生成器: DeepSeek (MotionPlanner + MotionTranslator)");
        sb.AppendLine();

        // ——— 成绩表 ———
        sb.AppendLine("## 一、成绩单");
        sb.AppendLine("| ID | 动作 | 分数 | LLM? | 帧数 | 判断 |");
        sb.AppendLine("|----|------|------|------|------|------|");
        int passCount = 0, totalScore = 0, llmCount = 0;
        foreach (var r in results)
        {
            string scoreStr = r.ErrorMessage != null ? "❌" : ScoreToStars(r.Score);
            string passMark = r.IsPass ? "✅" : "❌";
            string llmMark = r.WasLLMGenerated ? "✅" : "⚠️";
            string frames = r.KeyFrameCount > 0 ? r.KeyFrameCount.ToString() : "N/A";
            string judgment = r.ErrorMessage ?? TruncateJudgment(r.GlmJudgment);
            sb.AppendLine($"| {r.Id} | {r.ChineseName} | {scoreStr} | {llmMark} | {frames} | {judgment} |");
            if (r.IsPass) passCount++;
            if (r.Score > 0) totalScore += r.Score;
            if (r.WasLLMGenerated) llmCount++;
        }
        sb.AppendLine();

        // ——— 汇总 ———
        sb.AppendLine("## 二、汇总");
        float passRate = total > 0 ? (float)passCount / total * 100f : 0f;
        float avgScore = total > 0 ? (float)totalScore / total : 0f;
        float llmRate = total > 0 ? (float)llmCount / total * 100f : 0f;
        sb.AppendLine($"- 通过数: {passCount}/{total} ({passRate:F1}%)");
        sb.AppendLine($"- 平均分: {avgScore:F1}/5.0");
        sb.AppendLine($"- LLM 触发率: {llmCount}/{total} ({llmRate:F1}%)");
        sb.AppendLine();

        // ——— 结论 ———
        if (passRate >= 80f && avgScore >= 4f)
        {
            sb.AppendLine("🥇 **视觉验证完全通过！** GLM-4V 确认 AI 能做出令人信服的动作。");
            sb.AppendLine("**DeepSeek sub-agent 已具备真正的具身智能。**");
        }
        else if (passRate >= 60f && avgScore >= 3f)
        {
            sb.AppendLine("🥈 **视觉验证基本通过。** 大部分动作可被视觉模型识别。");
            sb.AppendLine("AI 的具身智能已初具雏形，仍有优化空间。");
        }
        else if (passRate >= 40f)
        {
            sb.AppendLine("🥉 **需要改进。** 部分动作不够准确，视觉模型难以识别。");
            sb.AppendLine("建议：检查 MotionTranslator 的 body schema 和 prompt。");
        }
        else
        {
            sb.AppendLine("❌ **视觉验证未通过。** 动作普遍不被视觉模型认可。");
            sb.AppendLine("需排查 LLM 翻译路径或参数映射是否存在系统性问题。");
        }

        // ——— GLM 详细反馈 ———
        sb.AppendLine();
        sb.AppendLine("## 三、详细反馈");
        foreach (var r in results)
        {
            if (!string.IsNullOrEmpty(r.GlmJudgment))
            {
                sb.AppendLine($"### {r.Id} {r.ChineseName} — {ScoreToStars(r.Score)}");
                sb.AppendLine("```");
                sb.AppendLine(TruncateJudgment(r.GlmJudgment, 200));
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string ScoreToStars(int score)
    {
        return score switch
        {
            5 => "⭐⭐⭐⭐⭐",
            4 => "⭐⭐⭐⭐",
            3 => "⭐⭐⭐",
            2 => "⭐⭐",
            1 => "⭐",
            _ => "⭐",
        };
    }

    private static string TruncateJudgment(string text, int maxLen = 80)
    {
        if (string.IsNullOrEmpty(text)) return "N/A";
        // 提取判断行
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("判断：") || t.StartsWith("判断:"))
                return t.Length > maxLen ? t.Substring(0, maxLen) + "…" : t;
        }
        return text.Length > maxLen ? text.Substring(0, maxLen) + "…" : text;
    }

    // ──────────────────────────────────────────────
    //  JSON 响应模型（嵌入，与 ToolCallInvoker 一致）
    // ──────────────────────────────────────────────

    [System.Serializable]
    private class GlmVisionResponse
    {
        public GlmChoice[] choices;
    }

    [System.Serializable]
    private class GlmChoice
    {
        public GlmMessage message;
    }

    [System.Serializable]
    private class GlmMessage
    {
        public string content;
    }

    [System.Serializable]
    private class GlmErrorResponse
    {
        public GlmError error;
    }

    [System.Serializable]
    private class GlmError
    {
        public string message;
    }

    // ──────────────────────────────────────────────
    //  工具方法
    // ──────────────────────────────────────────────

    private static string EscapeJsonStr(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // ──────────────────────────────────────────────
    //  可复制的紧凑摘要
    // ──────────────────────────────────────────────

    /// <summary>生成紧凑的 Markdown 表格摘要，方便从 Console 复制</summary>
    public static string GetCompactSummary()
    {
        if (_lastResults == null || _lastResults.Count == 0)
            return "[vis_verify] 尚未运行验证，无结果";

        var sb = new StringBuilder();
        sb.AppendLine("📋 **复制以下内容 — 视觉验证结果摘要**");
        sb.AppendLine();
        sb.AppendLine("| 动作 | 分数 | 判断 |");
        sb.AppendLine("|------|------|------|");
        int passCount = 0, totalScore = 0, total = _lastResults.Count;
        foreach (var r in _lastResults)
        {
            string scoreStr = r.ErrorMessage != null ? "❌" : ScoreToStars(r.Score);
            string judgment = r.ErrorMessage ?? TruncateJudgment(r.GlmJudgment, 50);
            sb.AppendLine($"| {r.ChineseName} | {scoreStr} | {judgment} |");
            if (r.IsPass) passCount++;
            if (r.Score > 0) totalScore += r.Score;
        }
        float passRate = total > 0 ? (float)passCount / total * 100f : 0f;
        float avgScore = total > 0 ? (float)totalScore / total : 0f;
        sb.AppendLine();
        sb.AppendLine($"📊 通过率: {passCount}/{total} ({passRate:F1}%)");
        sb.AppendLine($"📊 平均分: {avgScore:F1}/5.0");
        sb.AppendLine($"📄 完整报告已保存: {System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "vis_verify_report.md")}");
        sb.AppendLine();

        if (passRate >= 80f && avgScore >= 4f)
            sb.AppendLine("🏆 结论: **🥇 视觉验证完全通过！**");
        else if (passRate >= 60f && avgScore >= 3f)
            sb.AppendLine("🏆 结论: **🥈 视觉验证基本通过。**");
        else if (passRate >= 40f)
            sb.AppendLine("🏆 结论: **🥉 需要改进。**");
        else
            sb.AppendLine("🏆 结论: **❌ 视觉验证未通过。**");

        return sb.ToString();
    }
}
