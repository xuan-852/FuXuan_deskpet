using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Live2D.Cubism.Core;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 动作翻译器 — 将自然语言动作描述通过 LLM 翻译为结构化关键帧序列
///
/// 解决 MotionPlanner.PlanFromDescription 只能做 5 种硬编码模板的瓶颈：
///   1. 调用 DeepSeek API，传入完整 body schema（参数名/范围/部位）
///   2. DeepSeek 返回结构化 JSON（关键帧序列）
///   3. 解析为 MotionPlan 交给 MotionGenerator 播放
///
/// 使用方式（从协程中调用）：
///   MotionPlanner.MotionPlan result = null;
///   yield return MotionTranslator.TranslateAsync(
///       "害羞地捂脸", mapper, model, 3f, p => result = p);
///   if (result != null) 播放 result;
/// </summary>
public static class MotionTranslator
{
    private const string API_URL = "https://api.deepseek.com/chat/completions";
    private const string MODEL = "deepseek-chat";
    private const float TEMPERATURE = 0.3f;
    private const int TIMEOUT = 30;

    // ──────────────────────────────────────────────
    //  公开入口
    // ──────────────────────────────────────────────

    /// <summary>
    /// 翻译动作描述为核心帧序列（协程）
    /// </summary>
    /// <param name="description">自然语言动作描述</param>
    /// <param name="mapper">已加载的 Live2DParameterMapper</param>
    /// <param name="model">CubismModel 实例</param>
    /// <param name="duration">建议持续时间（秒）</param>
    /// <param name="onResult">完成回调，参数为解析后的 MotionPlan（失败时为 null）</param>
    public static IEnumerator TranslateAsync(
        string description,
        Live2DParameterMapper mapper,
        CubismModel model,
        float duration,
        Action<MotionPlanner.MotionPlan> onResult)
    {
        if (onResult == null) yield break;

        // ——— API Key 检查 ———
        string apiKey = ChatConfig.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("[MotionTranslator] DeepSeek API Key 未配置，跳过翻译");
            onResult(null);
            yield break;
        }

        if (mapper == null || !mapper.IsLoaded)
        {
            Debug.LogWarning("[MotionTranslator] Mapper 未就绪，跳过翻译");
            onResult(null);
            yield break;
        }

        // ——— 1. 构建 body schema 字符串 ———
        string bodySchema = BuildBodySchema(mapper);

        // ——— 1b. 注入运动记忆（自主闭环学习）———
        string motionMemories = GetMotionMemories();

        // ——— 2. 构建 System Prompt ———
        string systemPrompt =
            "You are a 3D character animation designer for a Live2D desktop pet (a cute anime girl). " +
            "Your task is to translate natural language action descriptions into structured keyframe parameter sequences.\n\n" +

            "RULES:\n" +
            "1. Output ONLY valid JSON — no markdown, no explanation, no code fences.\n" +
            "2. Each keyframe defines parameter values at a specific time point.\n" +
            "3. First keyframe at time 0 should have neutral/anticipation values (empty object means 'keep current').\n" +
            "4. Use 3~8 keyframes for a natural animation (ramp-up → hold → ramp-down).\n" +
            "5. Parameters not in a keyframe keep their previous values.\n" +
            "6. Eyes: left/right should usually be set together (same value).\n" +
            "7. head_angle_x: negative=left, positive=right. head_angle_y: negative=down, positive=up. head_angle_z: negative=tilt left, positive=tilt right.\n" +
            "8. Keep all values within their valid [min~max] ranges.\n" +
            "9. Total duration should be approximately " + duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " seconds.\n" +
            "10. CRITICAL: Make movements LARGE and EXAGGERATED — push parameters to 60-90% of their range.\n" +
            "    This character's movements are judged by a visual AI (GLM-4V) that needs CLEAR VISUAL DIFFERENCES.\n" +
            "    Small/subtle movements (10-30% range) will be INVISIBLE to the visual judge.\n" +
            "    Use the FULL RANGE of head_angle (15-25°), body rotation, arm raises, etc.\n\n" +

            "IMPORTANT PARAMETER RANGE NOTES:\n" +
            "  - Arm parameters (arm_*) are ANGLE values in degrees with range [-30~30]. " +
            "    0 = neutral/resting, 15~25 = clearly visible raised arm, 30 = max.\n" +
            "    DO NOT use values like 0.7 for arm_* — that's only 0.7 degrees and invisible!\n" +
            "  - Eye/head/body angle parameters are also in degrees.\n" +
            "  - Normalized parameters (eye_*_open, mouth_open_y, hand_layer_*, etc.) use [0~1] range.\n" +
            "  - FINGER parameters: This model has TWO finger modes controlled by sword_finger_switch (0 or 1):\n" +
            "    Mode 0 (default hand, sword_finger_switch=0): finger_normal_1~5 [0~1], finger_z_rotate [-20~20]\n" +
            "    Mode 1 (sword-finger/pointing, sword_finger_switch=1): finger_thumb/index/middle/ring/pinky [0~1]\n" +
            "    To use pointing gestures: set sword_finger_switch=1, then finger_index=1, others=0\n" +
            "    To use counting/normal gestures: keep sword_finger_switch=0, use finger_normal_1~5\n" +
            "  - HAND_LAYER parameters (hand_layer_95/98/100/108/116/117/119/120): control where hands appear visually [0~1].\n" +
            "    Set hand_layer_100=1 to make hands visible in front of the body.\n\n" +

            "SPECIAL PATTERNS for common pose types (use DEGREES for angle params, normalized for [0~1] params):\n" +
            "  - Hands_on_hips/叉腰: arm_right_upper=15~25, arm_left_upper=15~25, " +
            "arm_right_lower=-15~-25, arm_left_lower=-15~-25 (arms out + elbows bent)\n" +
            "  - Bowing/行礼: body_angle_y=-15~-25 (lean forward), " +
            "arm_right_lower=-15~-25 + arm_left_lower=-15~-25 (arms swing down)\n" +
            "  - Head_tilt_thinking/歪头思考: head_angle_z=15~25 (large tilt!), eye_ball_y=-0.5~-0.7 (eyes looking up)\n" +
            "  - Covering_face/捂脸: arm_right_upper=20~30, arm_left_upper=20~30, " +
            "arm_right_rotation=20~30 (bring hands toward face), " +
            "arm_right_reach=0.5~0.8, head_angle_y=-8~-15 (look down)\n" +
            "  - Surprise/惊讶捂嘴: head_angle_y=8~15, eye_l_open=0.9~1.0, eye_r_open=0.9~1.0, " +
            "mouth_open_y=0.5~0.8, arm_right_upper=20~30, arm_left_upper=20~30\n" +
            "  - Cowering/缩团: head_angle_y=-15~-25 (chin down), " +
            "arm_right_upper=-15~-25, arm_left_upper=-15~-25 (pull arms in/down), " +
            "arm_right_lower=-15~-25, arm_left_lower=-15~-25, " +
            "body_angle_y=15~25 (lean back)\n" +
            "  - Pointing/指: sword_finger_switch=1, arm_right_upper=15~25, arm_right_lower=10~20, " +
            "finger_index=1.0, finger_thumb=1.0, finger_middle=0, finger_ring=0, finger_pinky=0\n" +
            "  - Beckoning/招手(come here): arm_right_upper=20~28, arm_right_lower=10~18, " +
            "finger_normal_1~2=0.6~1.0 (curl). Alternate arm position every 0.4s for waving motion.\n" +
            "  - Hands_covering_face/捂脸: arm_right_upper=20~30, arm_left_upper=20~30, " +
            "arm_right_rotation=20~30, arm_right_reach=0.5~0.8, " +
            "hand_layer_100=1, hand_layer_120=0.8, " +
            "head_angle_y=-8~-15 (look down)\n" +
            "  - Hands_praying/合十祈祷: arm_right_upper=10~20, arm_left_upper=10~20, " +
            "arm_right_lower=15~25, arm_left_lower=15~25, " +
            "hand_layer_100=1, head_angle_y=-3~-8\n\n" +

            (string.IsNullOrEmpty(motionMemories) ? "" : motionMemories + "\n\n") +

            "Output format (JSON ONLY):\n" +
            "{\n" +
            "  \"totalDuration\": 3.0,\n" +
            "  \"description\": \"brief action name\",\n" +
            "  \"keyframes\": [\n" +
            "    {\"time\": 0.0, \"values\": {}},\n" +
            "    {\"time\": 0.5, \"values\": {\"param1\": val1, \"param2\": val2}},\n" +
            "    {\"time\": 1.5, \"values\": {\"param1\": val3}},\n" +
            "    {\"time\": 3.0, \"values\": {\"param1\": 0, \"param2\": 0}}\n" +
            "  ]\n" +
            "}\n\n" +

            "Example for \"excited wave\" — note the LARGE values:\n" +
            "{\"totalDuration\":3.0,\"description\":\"excited wave\",\"keyframes\":[" +
            "{\"time\":0,\"values\":{}}," +
            "{\"time\":0.3,\"values\":{\"arm_right_upper\":0.8,\"arm_right_lower\":0.4,\"head_angle_y\":8,\"eye_l_smile\":0.8,\"eye_r_smile\":0.8}}," +
            "{\"time\":0.8,\"values\":{\"arm_right_upper\":1.0,\"arm_right_lower\":0.6,\"mouth_form\":0.6}}," +
            "{\"time\":1.3,\"values\":{\"arm_right_upper\":0.8,\"arm_right_lower\":0.4}}," +
            "{\"time\":1.8,\"values\":{\"arm_right_upper\":1.0,\"arm_right_lower\":0.6}}," +
            "{\"time\":2.5,\"values\":{\"arm_right_upper\":0.5,\"arm_right_lower\":0.2}}," +
            "{\"time\":3.0,\"values\":{\"arm_right_upper\":0,\"arm_right_lower\":0,\"head_angle_y\":0,\"eye_l_smile\":0,\"eye_r_smile\":0,\"mouth_form\":0}}" +
            "]}";

        // ——— 3. 构建 User Prompt ———
        string userPrompt =
            "Available body parameters (semantic_name [min~max] default=value):\n" +
            bodySchema + "\n" +
            "Translate this action into a keyframe sequence:\n\"" + description + "\"\n" +
            "Duration: ~" + duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s\n" +
            "REMEMBER: Use LARGE EXAGGERATED values (60-90% of range) so a visual AI can clearly see the pose!\n\n" +
            "JSON output only:";

        // ——— 4. 调用 DeepSeek API ———
        string jsonBody = BuildRequestBody(systemPrompt, userPrompt);

        using (UnityWebRequest req = new UnityWebRequest(API_URL, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.timeout = TIMEOUT;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string responseText = req.downloadHandler.text;
                MotionPlanner.MotionPlan plan = ParseResponse(responseText, description);
                if (plan != null && plan.KeyFrames.Count > 0)
                {
                    // ——— 6. 参数分布检查：如果全是头/面参数，注入肢体参数 ———
                    EnrichWithLimbParams(plan);
                    Debug.Log($"[MotionTranslator] ✅ 翻译成功：「{description}」→ {plan.KeyFrames.Count} 帧, {plan.TotalDuration:F1}s");
                    onResult(plan);
                }
                else
                {
                    Debug.LogWarning($"[MotionTranslator] 解析结果无效: {StringTruncateExtension.Truncate(responseText, 200)}");
                    onResult(null);
                }
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = ExtractErrorMessage(errBody) ?? req.error;
                Debug.LogWarning($"[MotionTranslator] API 请求失败: {errMsg}");
                onResult(null);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Body Schema 构建
    // ──────────────────────────────────────────────

    /// <summary>根据 mapper 中的参数按身体部位分组构建 schema 文本</summary>
    /// <summary>过滤掉对视觉验证没有意义的参数组（微动、物理衣发），让 DeepSeek 聚焦可见关键部位</summary>
    private static readonly HashSet<string> _schemaExcludedParts = new HashSet<string>
    {
        "BODY_MICRO",   // breath, shoulder — 几乎不可见的微动
        "CLOTHES",      // hair_*, skirt_* — 物理驱动，截图看不出区别
    };

    // ── 视觉标定缓存 ──
    private static Dictionary<string, string> _visualDescriptions = null;
    // ── 中文名缓存（从 fuxuan_map.json 加载） ──
    private static Dictionary<string, string> _chineseNames = null;

    private static void LoadVisualDescriptions()
    {
        if (_visualDescriptions != null) return;
        _visualDescriptions = new Dictionary<string, string>();

        try
        {
            TextAsset asset = Resources.Load<TextAsset>("Live2D/ParamMaps/vision_calibration");
            if (asset == null) return;

            // 手动解析 JSON（无 Newtonsoft.Json 依赖）
            string json = asset.text;

            // 提取 calibrations 数组
            int arrStart = json.IndexOf("\"calibrations\"");
            if (arrStart < 0) return;
            int colon = json.IndexOf(':', arrStart + 14);
            if (colon < 0) return;
            int bracketStart = json.IndexOf('[', colon);
            if (bracketStart < 0) return;

            // 逐个解析对象
            int pos = bracketStart + 1;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;
                int depth = 0;
                int objEnd = -1;
                for (int i = objStart; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') { depth--; if (depth == 0) { objEnd = i; break; } }
                }
                if (objEnd < 0) break;

                string obj = json.Substring(objStart, objEnd - objStart + 1);

                // 提取 semantic 和 visualDescription
                string sem = ExtractJsonString(obj, "semantic");
                string vdesc = ExtractJsonString(obj, "visualDescription");
                if (!string.IsNullOrEmpty(sem) && !string.IsNullOrEmpty(vdesc))
                {
                    _visualDescriptions[sem] = vdesc;
                }

                pos = objEnd + 1;
            }

            Debug.Log($"[MotionTranslator] 已加载 {_visualDescriptions.Count} 条视觉标定描述");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MotionTranslator] 加载视觉标定失败: {ex.Message}");
        }
    }

    /// <summary>从 fuxuan_map.json 加载中文名映射</summary>
    private static void LoadChineseNames()
    {
        if (_chineseNames != null) return;
        _chineseNames = new Dictionary<string, string>();

        try
        {
            TextAsset asset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
            if (asset == null) return;

            string json = asset.text;

            // 找 entries 数组
            int entriesStart = json.IndexOf("\"entries\"");
            if (entriesStart < 0) return;
            int colon = json.IndexOf(':', entriesStart + 9);
            if (colon < 0) return;
            int bracketStart = json.IndexOf('[', colon);
            if (bracketStart < 0) return;

            int pos = bracketStart + 1;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;
                int depth = 0;
                int objEnd = -1;
                for (int i = objStart; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') { depth--; if (depth == 0) { objEnd = i; break; } }
                }
                if (objEnd < 0) break;

                string obj = json.Substring(objStart, objEnd - objStart + 1);

                // 提取 s(semantic) 和 d(description)
                string sem = ExtractJsonString(obj, "s");
                string desc = ExtractJsonString(obj, "d");
                if (!string.IsNullOrEmpty(sem) && !string.IsNullOrEmpty(desc))
                {
                    _chineseNames[sem] = desc;
                }

                pos = objEnd + 1;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MotionTranslator] 加载中文名失败: {ex.Message}");
        }
    }

    private static string BuildBodySchema(Live2DParameterMapper mapper)
    {
        // 确保视觉标定和中文名已加载
        LoadVisualDescriptions();
        LoadChineseNames();

        var parts = new Dictionary<string, List<(string name, float min, float max, float def)>>();

        foreach (string semantic in mapper.SemanticToId.Keys)
        {
            if (!mapper.TryGetRange(semantic, out var range)) continue;
            string part = GetBodyPartName(semantic);
            if (_schemaExcludedParts.Contains(part)) continue;
            if (!parts.ContainsKey(part))
                parts[part] = new List<(string, float, float, float)>();
            parts[part].Add((semantic, range.Min, range.Max, range.Default));
        }

        var sb = new StringBuilder();
        foreach (var kv in parts)
        {
            sb.AppendLine($"#{kv.Key}#");
            foreach (var (name, min, max, def) in kv.Value)
            {
                sb.Append($"  {name}[{min:F1}~{max:F1}] def={def:F2}");

                // 中文名（从 fuxuan_map.json 加载）
                if (_chineseNames != null && _chineseNames.TryGetValue(name, out string cn))
                    sb.Append($" ({cn})");

                // 方向提示
                string hint = GetValueHint(name);
                if (hint != null) sb.Append($" [{hint}]");

                // 视觉描述（从 vision_calibration.json 加载）
                if (_visualDescriptions != null && _visualDescriptions.TryGetValue(name, out string visDesc))
                {
                    string brief = visDesc.Length > 80 ? visDesc.Substring(0, 80) + "…" : visDesc;
                    sb.Append($"  ← {brief}");
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string GetBodyPartName(string semantic)
    {
        if (semantic.StartsWith("head_")) return "HEAD";
        if (semantic.StartsWith("eye_") || semantic == "eye_l_open" || semantic == "eye_r_open") return "EYES";
        if (semantic.StartsWith("brow_")) return "BROWS";
        if (semantic.StartsWith("mouth_")) return "MOUTH";
        if (semantic.StartsWith("arm_")) return "ARMS";
        if (semantic.StartsWith("hand_")) return "HANDS";
        if (semantic.StartsWith("finger_") || semantic.StartsWith("sword_")) return "FINGERS";
        if (semantic.StartsWith("leg_")) return "LEGS";
        if (semantic.StartsWith("body_")) return "BODY";
        if (semantic.StartsWith("hair_") || semantic.StartsWith("skirt_") || semantic.StartsWith("ribbon_")) return "CLOTHES";
        if (semantic == "breath" || semantic == "shoulder") return "BODY_MICRO";
        return "OTHER";
    }

    private static string GetValueHint(string semantic)
    {
        return semantic switch
        {
            "head_angle_x" => "-=left +=right",
            "head_angle_y" => "-=down +=up",
            "head_angle_z" => "-=tilt_left +=tilt_right",
            "eye_ball_x" => "-=look_left +=look_right",
            "eye_ball_y" => "-=look_up +=look_down",
            "eye_l_open" or "eye_r_open" => "0=closed 1=fully_open",
            "eye_l_smile" or "eye_r_smile" => "0=none 1=max_smile",
            "mouth_form" => "-=pout +=smile",
            "mouth_open_y" => "0=closed 1=wide_open",
            "brow_l_y" or "brow_r_y" => "-=lower +=raise",
            "body_angle_x" => "-=lean_left +=lean_right",
            "body_angle_y" => "-=lean_forward +=lean_back",
            "body_angle_z" => "-=twist_left +=twist_right",
            _ => null
        };
    }

    // ──────────────────────────────────────────────
    //  API 请求构建
    // ──────────────────────────────────────────────

    private static string BuildRequestBody(string systemPrompt, string userPrompt)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"model\":\"").Append(MODEL).Append("\",");
        sb.Append("\"temperature\":").Append(TEMPERATURE.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"messages\":[");
        sb.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJson(systemPrompt)).Append("\"},");
        sb.Append("{\"role\":\"user\",\"content\":\"").Append(EscapeJson(userPrompt)).Append("\"}");
        sb.Append("]");
        sb.Append('}');
        return sb.ToString();
    }

    // ──────────────────────────────────────────────
    //  响应解析（纯文本处理，无 JSON 库依赖）
    // ──────────────────────────────────────────────

    private static MotionPlanner.MotionPlan ParseResponse(string responseText, string originalDescription)
    {
        try
        {
            // ——— 提取 content 字段（从 DeepSeek 响应中取出 assistant 的回复） ———
            string content = ExtractContentField(responseText);
            if (content == null) content = responseText; // fallback

            // ——— 清理 Markdown 代码块标记 ———
            content = content.Trim();
            if (content.StartsWith("```json")) content = content.Substring(7).Trim();
            else if (content.StartsWith("```")) content = content.Substring(3).Trim();
            if (content.EndsWith("```")) content = content.Substring(0, content.Length - 3).Trim();

            // ——— 提取顶层 JSON 对象 ———
            int braceStart = content.IndexOf('{');
            int braceEnd = content.LastIndexOf('}');
            if (braceStart < 0 || braceEnd <= braceStart) return null;
            string json = content.Substring(braceStart, braceEnd - braceStart + 1);

            // ——— 解析 totalDuration ———
            float totalDuration = 3f;
            string td = ExtractJsonValue(json, "totalDuration");
            if (td != null)
                float.TryParse(td, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out totalDuration);

            // ——— 解析 description ———
            string desc = ExtractJsonString(json, "description") ?? originalDescription;

            // ——— 解析 keyframes 数组 ———
            var keyframes = new List<MotionPlanner.KeyFrame>();

            // 先提取 keyframes 数组的原始字符串
            int arrStart = FindJsonArrayStart(json, "keyframes");
            if (arrStart < 0) return null;

            int depth = 0;
            int arrEnd = -1;
            for (int i = arrStart; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
            }
            if (arrEnd < 0) return null;

            string kfArrayStr = json.Substring(arrStart, arrEnd - arrStart + 1);

            // 逐个解析数组中的对象
            int pos = 1; // 跳过 [
            while (pos < kfArrayStr.Length - 1)
            {
                int objStart = kfArrayStr.IndexOf('{', pos);
                if (objStart < 0) break;

                // 找到匹配的 }
                depth = 0;
                int objEnd = -1;
                for (int i = objStart; i < kfArrayStr.Length; i++)
                {
                    if (kfArrayStr[i] == '{') depth++;
                    else if (kfArrayStr[i] == '}') { depth--; if (depth == 0) { objEnd = i; break; } }
                }
                if (objEnd < 0) break;

                string kfJson = kfArrayStr.Substring(objStart, objEnd - objStart + 1);

                // 解析 time
                string timeStr = ExtractJsonValue(kfJson, "time");
                if (timeStr == null) { pos = objEnd + 1; continue; }

                float time = 0f;
                if (!float.TryParse(timeStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out time))
                { pos = objEnd + 1; continue; }

                // 解析 values 对象
                var values = new Dictionary<string, float>();
                int vObjStart = FindJsonObjectStart(kfJson, "values");
                if (vObjStart >= 0)
                {
                    depth = 0;
                    int vObjEnd = -1;
                    for (int i = vObjStart; i < kfJson.Length; i++)
                    {
                        if (kfJson[i] == '{') depth++;
                        else if (kfJson[i] == '}') { depth--; if (depth == 0) { vObjEnd = i; break; } }
                    }

                    if (vObjEnd > vObjStart)
                    {
                        string valObj = kfJson.Substring(vObjStart, vObjEnd - vObjStart + 1);
                        ParseKeyValuePairs(valObj, values);
                    }
                }

                keyframes.Add(new MotionPlanner.KeyFrame
                {
                    Time = time,
                    Curve = MotionPlanner.InterpolationType.Smooth,
                    Values = values
                });

                pos = objEnd + 1;
            }

            // ——— 排序关键帧 ———
            if (keyframes.Count == 0) return null;
            keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));

            // ——— 确保最后一帧归零（清理参数） ———
            MotionPlanner.KeyFrame last = keyframes[keyframes.Count - 1];
            var resetValues = new Dictionary<string, float>();
            bool needsResetFrame = false;
            foreach (var kv in last.Values)
            {
                if (Mathf.Abs(kv.Value) > 0.01f)
                {
                    resetValues[kv.Key] = 0f;
                    needsResetFrame = true;
                }
            }
            if (needsResetFrame && last.Time < totalDuration)
            {
                keyframes.Add(new MotionPlanner.KeyFrame
                {
                    Time = totalDuration,
                    Curve = MotionPlanner.InterpolationType.EaseIn,
                    Values = resetValues
                });
            }

            return new MotionPlanner.MotionPlan
            {
                TotalDuration = Mathf.Max(totalDuration, keyframes[keyframes.Count - 1].Time + 0.3f),
                Description = "LLM:" + desc,
                KeyFrames = keyframes,
                Looping = false
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MotionTranslator] 解析异常: {e.Message}\n{StringTruncateExtension.Truncate(responseText, 300)}");
            return null;
        }
    }

    // ──────────────────────────────────────────────
    //  JSON 工具函数
    // ──────────────────────────────────────────────

    /// <summary>从 DeepSeek 响应中提取 assistant 的 content 字段</summary>
    private static string ExtractContentField(string responseJson)
    {
        // 定位到 choices[0].message.content
        int choicesIdx = responseJson.IndexOf("\"choices\"");
        if (choicesIdx < 0) return null;

        int msgIdx = responseJson.IndexOf("\"message\"", choicesIdx);
        if (msgIdx < 0) return null;

        int contentIdx = responseJson.IndexOf("\"content\"", msgIdx);
        if (contentIdx < 0) return null;

        int colon = responseJson.IndexOf(':', contentIdx + 9);
        if (colon < 0) return null;

        // 跳过空白
        int valStart = colon + 1;
        while (valStart < responseJson.Length && responseJson[valStart] == ' ') valStart++;

        if (valStart >= responseJson.Length) return null;
        if (responseJson[valStart] != '"') return null;

        // 找到闭合的引号
        valStart++; // 跳过开引号
        int valEnd = valStart;
        bool escaped = false;
        while (valEnd < responseJson.Length)
        {
            if (escaped) { escaped = false; valEnd++; continue; }
            if (responseJson[valEnd] == '\\') { escaped = true; valEnd++; continue; }
            if (responseJson[valEnd] == '"') break;
            valEnd++;
        }

        if (valEnd >= responseJson.Length) return null;
        string raw = responseJson.Substring(valStart, valEnd - valStart);
        // 反转义
        return raw.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\\", "\\");
    }

    /// <summary>在 JSON 中找指定 key 对应的简单值（数字/无引号值）</summary>
    private static string ExtractJsonValue(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\"");
        if (idx < 0) return null;
        int colon = json.IndexOf(':', idx + key.Length + 2);
        if (colon < 0) return null;

        // 跳过空白
        int start = colon + 1;
        while (start < json.Length && json[start] == ' ') start++;
        if (start >= json.Length) return null;

        // 如果是字符串值
        if (json[start] == '"')
        {
            start++;
            int end = start;
            bool esc = false;
            while (end < json.Length)
            {
                if (esc) { esc = false; end++; continue; }
                if (json[end] == '\\') { esc = true; end++; continue; }
                if (json[end] == '"') break;
                end++;
            }
            if (end >= json.Length) return null;
            return DecodeJsonString(json.Substring(start, end - start));
        }

        // 数值
        int valEnd = start;
        while (valEnd < json.Length && (char.IsDigit(json[valEnd]) || json[valEnd] == '.' || json[valEnd] == '-' || json[valEnd] == '+' || json[valEnd] == 'e' || json[valEnd] == 'E'))
            valEnd++;

        return json.Substring(start, valEnd - start);
    }

    /// <summary>提取 JSON 字符串字段值</summary>
    private static string ExtractJsonString(string json, string key)
    {
        string val = ExtractJsonValue(json, key);
        return val;
    }

    /// <summary>在 JSON 中找到指定 key 后的数组起始位置</summary>
    private static int FindJsonArrayStart(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\"");
        if (idx < 0) return -1;
        int colon = json.IndexOf(':', idx + key.Length + 2);
        if (colon < 0) return -1;
        int start = colon + 1;
        while (start < json.Length && json[start] == ' ') start++;
        if (start >= json.Length || json[start] != '[') return -1;
        return start;
    }

    /// <summary>在 JSON 中找到指定 key 后的对象起始位置</summary>
    private static int FindJsonObjectStart(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\"");
        if (idx < 0) return -1;
        int colon = json.IndexOf(':', idx + key.Length + 2);
        if (colon < 0) return -1;
        int start = colon + 1;
        while (start < json.Length && json[start] == ' ') start++;
        if (start >= json.Length || json[start] != '{') return -1;
        return start;
    }

    /// <summary>解析扁平 JSON 对象的键值对（{ "key1": val1, "key2": val2 }）</summary>
    private static void ParseKeyValuePairs(string json, Dictionary<string, float> output)
    {
        int pos = 1; // 跳过 {
        while (pos < json.Length - 1)
        {
            // 找 key
            int keyStart = json.IndexOf('"', pos);
            if (keyStart < 0 || keyStart >= json.Length - 2) break;
            int keyEnd = json.IndexOf('"', keyStart + 1);
            if (keyEnd < 0) break;
            string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

            // 找 colon
            int colon = json.IndexOf(':', keyEnd + 1);
            if (colon < 0) break;

            // 找 value
            int vStart = colon + 1;
            while (vStart < json.Length && json[vStart] == ' ') vStart++;
            if (vStart >= json.Length) break;

            // 如果是对象或数组嵌套，跳过
            if (json[vStart] == '{' || json[vStart] == '[')
            {
                pos = vStart + 1;
                continue;
            }

            // 字符串值，跳过
            if (json[vStart] == '"')
            {
                // 找到闭合引号
                int strEnd = vStart + 1;
                bool esc = false;
                while (strEnd < json.Length)
                {
                    if (esc) { esc = false; strEnd++; continue; }
                    if (json[strEnd] == '\\') { esc = true; strEnd++; continue; }
                    if (json[strEnd] == '"') break;
                    strEnd++;
                }                // 解析 key 中的 Unicode 转义
                key = DecodeJsonString(key);                pos = strEnd + 1;
                continue;
            }

            // 数值
            int vEnd = vStart;
            while (vEnd < json.Length && (char.IsDigit(json[vEnd]) || json[vEnd] == '.' || json[vEnd] == '-' || json[vEnd] == '+' || json[vEnd] == 'e' || json[vEnd] == 'E'))
                vEnd++;

            string valStr = json.Substring(vStart, vEnd - vStart).Trim();
            if (float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                output[key] = val;
            }

            pos = vEnd + 1;
        }
    }

    /// <summary>从 DeepSeek 错误响应中提取 message</summary>
    private static string ExtractErrorMessage(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody)) return null;
        int msgIdx = responseBody.IndexOf("\"message\"");
        if (msgIdx < 0) return null;
        int colon = responseBody.IndexOf(':', msgIdx + 9);
        if (colon < 0) return null;
        int start = responseBody.IndexOf('"', colon + 1);
        if (start < 0) return null;
        start++;
        int end = start;
        bool esc = false;
        while (end < responseBody.Length)
        {
            if (esc) { esc = false; end++; continue; }
            if (responseBody[end] == '\\') { esc = true; end++; continue; }
            if (responseBody[end] == '"') break;
            end++;
        }
        if (end >= responseBody.Length) return null;
        return DecodeJsonString(responseBody.Substring(start, end - start));
    }

    /// <summary>转义 JSON 字符串中的特殊字符</summary>
    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    /// <summary>
    /// 解码 JSON 字符串中的转义序列（\n, \\, \", \uXXXX）
    /// </summary>
    private static string DecodeJsonString(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var sb = new StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                char n = raw[i + 1];
                switch (n)
                {
                    case '"':  sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case 'n':  sb.Append('\n'); i++; break;
                    case 'r':  sb.Append('\r'); i++; break;
                    case 't':  sb.Append('\t'); i++; break;
                    case 'u':
                        if (i + 5 < raw.Length)
                        {
                            try
                            {
                                string hex = raw.Substring(i + 2, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                i += 5;
                            }
                            catch { sb.Append(raw[i]); }
                        }
                        else sb.Append(raw[i]);
                        break;
                    default: sb.Append(raw[i]); break;
                }
            }
            else sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    // ──────────────────────────────────────────────
    //  参数分布增强 — 打破头部偏好
    // ──────────────────────────────────────────────

    /// <summary>
    /// 检查生成的关键帧参数分布。如果全是头/面参数而没有任何肢体参数，
    /// 自动注入一组随机肢体参数变体，让动作更多样化。
    /// </summary>
    private static void EnrichWithLimbParams(MotionPlanner.MotionPlan plan)
    {
        if (plan == null || plan.KeyFrames.Count == 0) return;

        // 统计所有关键帧中出现的参数类型
        bool hasArmParam = false, hasHandParam = false, hasFingerParam = false;
        int headFaceCount = 0, totalCount = 0;

        foreach (var kf in plan.KeyFrames)
        {
            foreach (var key in kf.Values.Keys)
            {
                totalCount++;
                if (key.StartsWith("arm_")) hasArmParam = true;
                else if (key.StartsWith("hand_")) hasHandParam = true;
                else if (key.StartsWith("finger_") || key.StartsWith("sword_")) hasFingerParam = true;
                else if (key.StartsWith("head_") || key.StartsWith("eye_") || key.StartsWith("brow_") || key.StartsWith("mouth_"))
                    headFaceCount++;
            }
        }

        // 如果已经包含肢体参数，不需要增强
        if (hasArmParam || hasHandParam || hasFingerParam) return;

        // 如果头面参数占比 > 80%，注入肢体参数
        float headFaceRatio = totalCount > 0 ? (float)headFaceCount / totalCount : 1f;
        if (headFaceRatio < 0.8f) return;

        // 选择一组合适的肢体参数变体
        int variant = UnityEngine.Random.Range(0, 5);
        var enrichParams = new Dictionary<string, float>();

        switch (variant)
        {
            case 0: // 右手抬起
                enrichParams["arm_right_upper"] = UnityEngine.Random.Range(12f, 22f);
                enrichParams["arm_right_lower"] = UnityEngine.Random.Range(-10f, 10f);
                break;
            case 1: // 双臂微展（惊讶/欢迎）
                enrichParams["arm_right_upper"] = UnityEngine.Random.Range(10f, 18f);
                enrichParams["arm_left_upper"] = UnityEngine.Random.Range(10f, 18f);
                enrichParams["arm_right_reach"] = UnityEngine.Random.Range(0.2f, 0.5f);
                break;
            case 2: // 左手叉腰
                enrichParams["arm_left_upper"] = UnityEngine.Random.Range(15f, 22f);
                enrichParams["arm_left_lower"] = UnityEngine.Random.Range(-18f, -12f);
                break;
            case 3: // 双手缩胸（害羞/冷）
                enrichParams["arm_right_upper"] = UnityEngine.Random.Range(-15f, -8f);
                enrichParams["arm_left_upper"] = UnityEngine.Random.Range(-15f, -8f);
                enrichParams["arm_right_lower"] = UnityEngine.Random.Range(-15f, -8f);
                enrichParams["arm_right_reach"] = 0.3f;
                break;
            case 4: // 右手捂胸
                enrichParams["arm_right_upper"] = UnityEngine.Random.Range(15f, 25f);
                enrichParams["arm_right_lower"] = UnityEngine.Random.Range(8f, 15f);
                enrichParams["arm_right_rotation"] = UnityEngine.Random.Range(15f, 25f);
                enrichParams["arm_right_reach"] = UnityEngine.Random.Range(0.4f, 0.6f);
                break;
        }

        // 找到中间帧（动作峰值附近），注入肢体参数
        int midIdx = plan.KeyFrames.Count / 2;
        if (midIdx >= plan.KeyFrames.Count) midIdx = plan.KeyFrames.Count - 1;

        var midKf = plan.KeyFrames[midIdx];
        foreach (var kv in enrichParams)
        {
            midKf.Values[kv.Key] = kv.Value;
        }

        // 在最后一帧清理这些注入的参数
        var lastKf = plan.KeyFrames[plan.KeyFrames.Count - 1];
        foreach (var key in enrichParams.Keys)
        {
            if (!lastKf.Values.ContainsKey(key))
                lastKf.Values[key] = 0f;
        }

        Debug.Log($"[MotionTranslator] 鈿犱负 \"{plan.Description}\" 鑷姩娉ㄥ叆 {enrichParams.Count} 涓偄浣撳弬鏁帮紙鍘熷ご闈㈠崰姣?{headFaceRatio:P0}锛?");
    }

    // ──────────────────────────────────────────────
    //  闭环学习: 运动记忆读写
    // ──────────────────────────────────────────────

    /// <summary>从 MotionMemoryManager 读取最佳演武经验，注入 system prompt 供 AI 参考</summary>
    private static string GetMotionMemories()
    {
        var mm = MotionMemoryManager.Instance;
        if (mm == null) return "";

        // 同时注入运动记忆和肢体参数命中统计（闭环反馈）
        string memories = mm.GetFormattedMemories();
        if (!string.IsNullOrEmpty(memories))
        {
            int lines = memories.Split('\n').Length;
            int armCount = memories.Split(new[] { "arm_" }, StringSplitOptions.None).Length - 1;
            int fingerCount = memories.Split(new[] { "finger_" }, StringSplitOptions.None).Length - 1;
            Debug.Log($"[MotionTranslator] 鉂わ笍 娉ㄥ叆 {lines} 琛岃繍鍔ㄨ蹇嗗埌 prompt (鍚玜rm_={armCount}妗?鎸噁inger_={fingerCount}妗?)");
        }
        return memories;
    }

    /// <summary>成功翻译动作后，自动保存参数快照到 MotionMemoryManager（闭环学习写回）</summary>
    private static void SaveMotionMemory(string description, MotionPlanner.MotionPlan plan)
    {
        var mm = MotionMemoryManager.Instance;
        if (mm == null) return;

        // 提取中间帧的关键参数作为快照
        string snapshot = "";
        if (plan.KeyFrames.Count > 0)
        {
            int midIdx = Mathf.Clamp(plan.KeyFrames.Count / 2, 0, plan.KeyFrames.Count - 1);
            var midKf = plan.KeyFrames[midIdx];
            if (midKf.Values.Count > 0)
            {
                var topParams = midKf.Values
                    .OrderByDescending(kv => Math.Abs(kv.Value))
                    .Take(5)
                    .Select(kv => $"{kv.Key}={kv.Value:F2}");
                snapshot = string.Join(", ", topParams);
            }
        }

        mm.RecordMotion(description, snapshot, plan.KeyFrames.Count, plan.TotalDuration);
    }
}

/// <summary>字符串截断扩展（用于日志）</summary>
internal static class StringTruncateExtension
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return value.Substring(0, maxLength) + "...";
    }
}
