using Live2D.Cubism.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Live2D 模型分析器 — 扫描模型参数，自动推测映射关系
///
/// 功能：
/// 1. 列出模型所有参数及其范围
/// 2. 根据命名规则 + 启发式规则，自动匹配语义名
/// 3. 导出未映射的参数列表
/// 4. 生成映射 JSON 模板
///
/// 使用场景：
/// - 拿到一个新 Live2D 模型后，用此工具分析
/// - 自动匹配已知语义参数（如 ParamAngleX → head_angle_x）
/// - 手动确认后生成映射文件
/// </summary>
public static class Live2DModelAnalyzer
{
    /// <summary>已知的命名模式 → 语义名映射</summary>
    private static readonly Dictionary<string, string> KNOWN_PATTERNS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // 身体
        { "bodyanglex",     "body_angle_x" },
        { "bodyangley",     "body_angle_y" },
        { "bodyanglez",     "body_angle_z" },
        { "body_x",         "body_angle_x" },
        { "body_y",         "body_angle_y" },
        { "body_z",         "body_angle_z" },

        // 头
        { "anglex",         "head_angle_x" },
        { "angley",         "head_angle_y" },
        { "anglez",         "head_angle_z" },
        { "headx",          "head_angle_x" },
        { "heady",          "head_angle_y" },
        { "headz",          "head_angle_z" },

        // 呼吸
        { "breath",         "breath" },

        // 眼睛
        { "eye_l_open",     "eye_l_open" },
        { "eye_r_open",     "eye_r_open" },
        { "eyelopen",       "eye_l_open" },
        { "eyeropen",       "eye_r_open" },
        { "eyeballx",       "eye_ball_x" },
        { "eyebally",       "eye_ball_y" },
        { "eye_ball_x",     "eye_ball_x" },
        { "eye_ball_y",     "eye_ball_y" },
        { "eyelsmile",      "eye_l_smile" },
        { "eyersmile",      "eye_r_smile" },
        { "eyelform",       "eye_l_smile" },
        { "eyerform",       "eye_r_smile" },

        // 眉毛
        { "browry",         "brow_r_y" },
        { "browly",         "brow_l_y" },
        { "browryform",     "brow_r_y" },
        { "browlyform",     "brow_l_y" },
        { "browl",          "brow_l_angle" },
        { "browr",          "brow_r_angle" },
        { "browlx",         "brow_l_angle" },
        { "browrx",         "brow_r_angle" },

        // 嘴
        { "mouthform",      "mouth_form" },
        { "mouthopeny",     "mouth_open_y" },
        { "mouthopen",      "mouth_open_y" },

        // 肩膀
        { "shoulder",       "shoulder" },
    };

    /// <summary>分析结果</summary>
    public class AnalysisResult
    {
        public string modelName;
        public int totalParams;
        public int autoMatched;
        public int unmatched;
        public List<ParamInfo> allParams;
        public Dictionary<string, string> autoMap;  // 语义名 → 参数 ID
        public List<string> unmappedParamIds;       // 模型中未匹配的参数 ID
    }

    public class ParamInfo
    {
        public string id;
        public float min;
        public float max;
        public float defaultValue;
        public string matchedSemantic; // 匹配到的语义名（null=未匹配）
        public string suggestedName;   // AI 建议的名称
    }

    /// <summary>分析模型并生成匹配建议</summary>
    public static AnalysisResult Analyze(CubismModel model)
    {
        var result = new AnalysisResult();
        result.modelName = model.name;
        result.totalParams = model.Parameters.Length;
        result.allParams = new List<ParamInfo>();
        result.autoMap = new Dictionary<string, string>();
        result.unmappedParamIds = new List<string>();
        var usedSemantics = new HashSet<string>(); // 防重复匹配

        foreach (var p in model.Parameters)
        {
            var info = new ParamInfo
            {
                id = p.Id,
                min = p.MinimumValue,
                max = p.MaximumValue,
                defaultValue = p.DefaultValue,
            };

            // 尝试匹配已知模式
            string cleanId = p.Id.Replace("Param", "").Replace("param", "");
            if (KNOWN_PATTERNS.TryGetValue(cleanId, out string semantic))
            {
                if (!usedSemantics.Contains(semantic))
                {
                    info.matchedSemantic = semantic;
                    result.autoMap[semantic] = p.Id;
                    usedSemantics.Add(semantic);
                    result.autoMatched++;
                }
            }

            if (info.matchedSemantic == null)
            {
                result.unmappedParamIds.Add(p.Id);
                info.suggestedName = SuggestSemanticName(p.Id, p.MinimumValue, p.MaximumValue);
            }

            result.allParams.Add(info);
        }

        result.unmatched = result.unmappedParamIds.Count;
        return result;
    }

    /// <summary>根据参数 ID 和范围推测语义名称</summary>
    static string SuggestSemanticName(string paramId, float min, float max)
    {
        float range = Mathf.Abs(max - min);

        // 按范围猜测部位
        if (range > 60f)      return $"body_part_{paramId.ToLower()}";       // 大范围 → 身体/头
        else if (range > 20f) return $"limb_part_{paramId.ToLower()}";       // 中范围 → 手臂/腿
        else if (range > 5f)  return $"finger_part_{paramId.ToLower()}";     // 小范围 → 手指
        else if (range > 1f)  return $"micro_part_{paramId.ToLower()}";      // 微调
        else                  return $"toggle_{paramId.ToLower()}";           // 开关

        // 未来可以加入更智能的推断（如范围对称性 → 左右配对）
    }

    /// <summary>生成映射模板（供人工填写）</summary>
    public static string GenerateMappingTemplate(CubismModel model)
    {
        var result = Analyze(model);
        var sb = new StringBuilder();

        sb.AppendLine("{");
        sb.AppendLine($"  \"formatVersion\": \"1.0\",");
        sb.AppendLine($"  \"modelName\": \"{result.modelName}\",");
        sb.AppendLine($"  \"description\": \"自动生成的映射模板，请填写每个语义名对应的参数 ID\",");
        sb.AppendLine("  \"entries\": [");

        // 已自动匹配的
        bool first = true;
        foreach (var kv in result.autoMap)
        {
            if (!first) sb.AppendLine(",");
            sb.Append($"    {{\"s\": \"{kv.Key}\", \"p\": \"{kv.Value}\"}}");
            first = false;
        }

        // 收集标准语义名中未匹配的（留空供填写）
        var fuxuanSemantics = GetStandardSemanticNames();
        foreach (var sem in fuxuanSemantics)
        {
            if (result.autoMap.ContainsKey(sem)) continue;
            if (!first) sb.AppendLine(",");
            sb.Append($"    {{\"s\": \"{sem}\", \"p\": \"\"}}");
            first = false;
        }

        sb.AppendLine();
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>获取标准语义名列表（从 fuxuan_map 提取）</summary>
    static string[] GetStandardSemanticNames()
    {
        return new string[]
        {
            "body_angle_x", "body_angle_y", "body_angle_z",
            "head_angle_x", "head_angle_y", "head_angle_z",
            "breath",
            "eye_l_open", "eye_r_open", "eye_ball_x", "eye_ball_y",
            "eye_l_smile", "eye_r_smile", "eye_heart",
            "brow_r_y", "brow_l_y", "brow_l_angle", "brow_r_angle",
            "mouth_form", "mouth_open_y",
            "arm_right_upper", "arm_right_mid", "arm_right_lower",
            "arm_right_rotation", "arm_right_base_rotation",
            "arm_right_switch", "arm_right_reach", "arm_right_wrist_z",
            "arm_left_upper", "arm_left_mid", "arm_left_lower",
            "arm_left_extra", "arm_left_extra2",
            "hand_layer_95", "hand_layer_117", "hand_layer_98",
            "hand_layer_100", "hand_layer_116", "hand_layer_120",
            "hand_layer_108", "hand_layer_119",
            "leg_l_lift", "leg_r_lift", "leg_l_swing", "leg_l_bend",
            "leg_r_swing", "leg_r_bend", "shoulder",
            "hair_bangs_1", "hair_bangs_2", "hair_bangs_3",
            "hair_physics_1", "hair_physics_2", "hair_physics_3",
            "hair_back_b_1", "hair_back_b_2",
            "hair_side_1", "hair_side_2", "hair_side_3",
            "hair_back_1", "hair_back_2", "hair_back_3", "hair_back_4",
            "hair_ornament_1", "hair_ornament_2", "hair_ornament_3", "hair_head_ornament",
            "skirt_drive_1", "skirt_drive_2", "skirt_drive_3",
            "skirt_drive_4", "skirt_drive_5", "skirt_drive_6", "skirt_drive_7",
            "special_money", "special_tear", "special_blush_dark",
            "special_angry", "special_outer_mask",
            "sword_finger_switch",
            "finger_normal_1", "finger_normal_2", "finger_normal_3",
            "finger_normal_4", "finger_normal_5",
            "finger_z_rotate", "finger_thumb", "finger_index",
            "finger_middle", "finger_ring", "finger_pinky",
            "camera_x", "camera_y", "character_scale"
        };
    }

    /// <summary>打印分析报告到日志</summary>
    public static void LogReport(AnalysisResult result)
    {
        Debug.Log($"=== Live2D 模型分析: {result.modelName} ===");
        Debug.Log($"总参数: {result.totalParams}");
        Debug.Log($"自动匹配: {result.autoMatched}");
        Debug.Log($"未匹配: {result.unmatched}");

        if (result.unmatched > 0)
        {
            Debug.Log("--- 未匹配参数 ---");
            foreach (var id in result.unmappedParamIds)
            {
                var info = result.allParams.Find(p => p.id == id);
                Debug.Log($"  {id} [{info.min:F2}, {info.max:F2}] 建议: {info.suggestedName}");
            }
        }

        Debug.Log("--- 匹配结果 ---");
        foreach (var info in result.allParams)
        {
            string status = info.matchedSemantic != null
                ? $"→ {info.matchedSemantic}"
                : $"(未匹配) 建议: {info.suggestedName}";
            Debug.Log($"  {info.id,-18} [{info.min,7:F2}, {info.max,7:F2}] def={info.defaultValue,7:F3}  {status}");
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor 菜单项：分析当前场景中选中的 Live2D 模型
    /// 在 Hierarchy 中选中模型根物体后，点击菜单 Tools/Live2D/分析模型参数
    /// </summary>
    [MenuItem("Tools/Live2D/分析模型参数")]
    public static void AnalyzeSelectedModel()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogError("请先在 Hierarchy 中选中 Live2D 模型根物体");
            return;
        }

        var model = selected.GetComponentInChildren<CubismModel>();
        if (model == null)
        {
            Debug.LogError("选中物体中没有 CubismModel 组件");
            return;
        }

        var result = Analyze(model);
        LogReport(result);

        // 生成映射模板到剪贴板
        string template = GenerateMappingTemplate(model);
        GUIUtility.systemCopyBuffer = template;
        Debug.Log("映射模板已复制到剪贴板，请粘贴到 JSON 文件并填写参数 ID");
    }

    /// <summary>
    /// Editor 菜单项：生成未映射参数的 AI 分析提示
    /// </summary>
    [MenuItem("Tools/Live2D/生成 AI 参数分析提示")]
    public static void GenerateAIPrompt()
    {
        var selected = Selection.activeGameObject;
        if (selected == null) return;

        var model = selected.GetComponentInChildren<CubismModel>();
        if (model == null) return;

        var result = Analyze(model);
        var sb = new StringBuilder();

        sb.AppendLine($"请分析以下 Live2D 模型参数，判断每个参数控制哪个身体部位：");
        sb.AppendLine();
        sb.AppendLine($"模型名称: {result.modelName}");
        sb.AppendLine($"参数数量: {result.totalParams}");
        sb.AppendLine();
        sb.AppendLine("## 未匹配参数");

        foreach (var info in result.allParams)
        {
            if (info.matchedSemantic != null) continue;
            sb.AppendLine($"- `{info.id}`: 范围 [{info.min:F2}, {info.max:F2}], 默认值 {info.defaultValue:F3}");
        }

        sb.AppendLine();
        sb.AppendLine("请将以上参数按以下格式输出映射：");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("[{\"s\": \"body_part_xxx\", \"p\": \"ParamID1\"}, {\"s\": \"eye_xxx\", \"p\": \"ParamID2\"}, ...]");
        sb.AppendLine("```");

        GUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log("AI 分析提示已复制到剪贴板，请粘贴给 AI 助手分析");
    }
#endif
}
