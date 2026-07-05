using Live2D.Cubism.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 运行时模型分析器 — 不依赖 Editor，运行时可分析任意 Live2D 模型
///
/// 功能：
/// 1. 遍历模型全部参数，收集范围/默认值
/// 2. 加载 cdi3.json 获取中文名
/// 3. 根据 KNOWN_PATTERNS 自动匹配标准语义名
/// 4. 输出完整的 ModelBodySchema
///
/// 与 Live2DModelAnalyzer 的关系：
/// - Live2DModelAnalyzer 是静态工具类，侧重分析报告和模板生成
/// - RuntimeModelAnalyzer 是实例组件，产出结构化数据供其他组件消费
/// </summary>
public class RuntimeModelAnalyzer
{
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

    /// <summary>标准语义名列表（用于模板生成）</summary>
    public static readonly string[] STANDARD_SEMANTICS = new string[]
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

    // ────────────────────────────────────────────────────────────────
    //  核心分析
    // ────────────────────────────────────────────────────────────────

    /// <summary>分析 CubismModel，产出完整的 ModelBodySchema</summary>
    public static ModelBodySchema AnalyzeModel(CubismModel model, string cdiJson = null)
    {
        var schema = new ModelBodySchema();
        schema.modelName = model.name;

        // 解析 cdi3.json
        Dictionary<string, string> cdiNames = null;
        if (!string.IsNullOrEmpty(cdiJson))
            cdiNames = ParseCdi3Json(cdiJson);

        var usedSemantics = new HashSet<string>();

        foreach (var p in model.Parameters)
        {
            var def = new ParameterDef
            {
                paramId = p.Id,
                min = p.MinimumValue,
                max = p.MaximumValue,
                defaultValue = p.DefaultValue,
                cdiName = cdiNames != null && cdiNames.ContainsKey(p.Id) ? cdiNames[p.Id] : "",
            };

            // 尝试匹配已知模式
            string cleanId = p.Id.Replace("Param", "").Replace("param", "");
            if (KNOWN_PATTERNS.TryGetValue(cleanId, out string semantic) && !usedSemantics.Contains(semantic))
            {
                def.semantic = semantic;
                usedSemantics.Add(semantic);
            }

            // 推断 bodyPart
            def.bodyPart = InferBodyPart(def);
            // 推断值域类型
            def.valueDomain = InferValueDomain(def);
            // 推断轴
            def.axis = InferAxis(def);

            // 如果没匹配到语义名，自动生成一个占位语义名
            if (string.IsNullOrEmpty(def.semantic))
            {
                def.semantic = $"unmapped_{def.paramId.ToLower()}";
            }

            schema.parameters.Add(def);
        }

        // 构建分组
        schema.groups = BuildDefaultGroups(schema);

        return schema;
    }

    // ────────────────────────────────────────────────────────────────
    //  启发式推断
    // ────────────────────────────────────────────────────────────────

    /// <summary>根据参数信息推断所属身体部位</summary>
    static string InferBodyPart(ParameterDef def)
    {
        string id = def.paramId.ToLower();
        string cdi = def.cdiName?.ToLower() ?? "";

        // 优先级 1: cdi3 中文名关键字
        if (cdi.Contains("头发") || cdi.Contains("刘海") || cdi.Contains("后发") ||
            cdi.Contains("鬓发") || cdi.Contains("发")) return "hair";
        if (cdi.Contains("手") || cdi.Contains("手臂") || cdi.Contains("前腕") ||
            cdi.Contains("腕") || cdi.Contains("手指") || cdi.Contains("指")) return "arm";
        if (cdi.Contains("眼")) return "eye";
        if (cdi.Contains("眉")) return "brow";
        if (cdi.Contains("嘴") || cdi.Contains("口")) return "mouth";
        if (cdi.Contains("身体") || cdi.Contains("体")) return "body";
        if (cdi.Contains("头") || cdi.Contains("角度")) return "head";
        if (cdi.Contains("腿") || cdi.Contains("脚")) return "leg";
        if (cdi.Contains("肩")) return "shoulder";
        if (cdi.Contains("裙子") || cdi.Contains("裙") || cdi.Contains("圆盘")) return "skirt";
        if (cdi.Contains("饰品") || cdi.Contains("饰")) return "ornament";
        if (cdi.Contains("钱") || cdi.Contains("泪") || cdi.Contains("黑脸") ||
            cdi.Contains("生气") || cdi.Contains("蒙版")) return "special";
        if (cdi.Contains("星") || cdi.Contains("星星")) return "special";
        if (cdi.Contains("镜")) return "camera";
        if (cdi.Contains("呼吸")) return "breath";

        // 优先级 2: 语义名（已匹配的）
        if (!string.IsNullOrEmpty(def.semantic))
        {
            if (def.semantic.StartsWith("body_")) return "body";
            if (def.semantic.StartsWith("head_")) return "head";
            if (def.semantic.StartsWith("eye_")) return "eye";
            if (def.semantic.StartsWith("brow_")) return "brow";
            if (def.semantic.StartsWith("mouth_")) return "mouth";
            if (def.semantic.StartsWith("arm_") || def.semantic.StartsWith("hand_") ||
                def.semantic.StartsWith("finger_") || def.semantic.StartsWith("sword_")) return "arm";
            if (def.semantic.StartsWith("leg_")) return "leg";
            if (def.semantic.StartsWith("hair_")) return "hair";
            if (def.semantic.StartsWith("skirt_")) return "skirt";
            if (def.semantic.StartsWith("special_") || def.semantic.StartsWith("star_")) return "special";
            if (def.semantic == "breath") return "breath";
            if (def.semantic.StartsWith("camera_")) return "camera";
            if (def.semantic.StartsWith("character_")) return "camera";
        }

        // 优先级 3: 参数 ID 模式
        if (id.Contains("angle") || id.Contains("body")) return "body";
        if (id.Contains("eye")) return "eye";
        if (id.Contains("brow")) return "brow";
        if (id.Contains("mouth")) return "mouth";
        if (id.Contains("arm") || (id.Length <= 8 && id.StartsWith("param"))) return "arm";
        if (id.Contains("hair")) return "hair";

        // 优先级 4: 范围推断
        float range = Mathf.Abs(def.max - def.min);
        if (range > 60f)  return "body";      // 大范围 → 身体/头旋转
        if (range > 20f)  return "arm";       // 中范围 → 手臂
        if (range > 5f)   return "finger";    // 小范围 → 手指
        if (range <= 1f)  return "special";   // 开关

        return "unknown";
    }

    /// <summary>推断值域类型</summary>
    static ValueDomain InferValueDomain(ParameterDef def)
    {
        float range = Mathf.Abs(def.max - def.min);
        string cdi = def.cdiName?.ToLower() ?? "";
        string id = def.paramId.ToLower();

        // 显式标记
        if (cdi.Contains("开关") || cdi.Contains("切换") || cdi.Contains("显隐") ||
            cdi.Contains("透明")) return ValueDomain.Toggle;
        if (cdi.Contains("角度") || cdi.Contains("旋转") || id.Contains("angle")) return ValueDomain.Angle;
        if (cdi.Contains("放大") || cdi.Contains("缩小") || id.Contains("scale")) return ValueDomain.Scale;
        if (id.Contains("camera") || id.Contains("镜")) return ValueDomain.Position;

        // 开关检测（范围 <= 1 且默认值在端点）
        if (range <= 1f && (def.defaultValue == 0f || def.defaultValue == 1f))
            return ValueDomain.Toggle;

        // 角度检测
        if (range > 30f && range <= 180f &&
            (def.semantic?.Contains("angle") == true || id.Contains("angle")))
            return ValueDomain.Angle;

        // 归一化检测
        if (range <= 1f) return ValueDomain.Normalized;

        return ValueDomain.Other;
    }

    /// <summary>推断参数控制的轴方向</summary>
    static string InferAxis(ParameterDef def)
    {
        string id = def.paramId.ToLower();
        string cdi = def.cdiName?.ToLower() ?? "";

        if (id.EndsWith("x") || cdi.Contains("左右") || cdi.EndsWith("x")) return "X";
        if (id.EndsWith("y") || cdi.Contains("上下") || cdi.EndsWith("y")) return "Y";
        if (id.EndsWith("z") || cdi.EndsWith("z")) return "Z";

        return "";
    }

    // ────────────────────────────────────────────────────────────────
    //  分组构建
    // ────────────────────────────────────────────────────────────────

    /// <summary>构建默认分组</summary>
    static List<ParameterGroup> BuildDefaultGroups(ModelBodySchema schema)
    {
        var groups = new List<ParameterGroup>();

        AddGroup(groups, "body", "身体", "身体旋转角度", schema);
        AddGroup(groups, "head", "头部", "头部旋转角度", schema);
        AddGroup(groups, "eyes", "眼睛", "睁眼/闭眼/眼珠/笑意/高光", schema);
        AddGroup(groups, "brow", "眉毛", "眉毛角度和位置", schema);
        AddGroup(groups, "mouth", "嘴", "嘴型和张开度", schema);
        AddGroup(groups, "breath", "呼吸", "呼吸参数", schema);
        AddGroup(groups, "arm", "手臂", "左右手臂的各段控制", schema);
        AddGroup(groups, "hand", "手/手指", "手掌、手指和手势切换", schema);
        AddGroup(groups, "leg", "腿", "腿部控制", schema);
        AddGroup(groups, "shoulder", "肩膀", "耸肩", schema);
        AddGroup(groups, "hair", "头发", "刘海/鬓发/后发/饰品", schema);
        AddGroup(groups, "skirt", "裙子", "裙子摆动和七星盘", schema);
        AddGroup(groups, "special", "特殊效果", "钱/泪/脸红/生气/星辉等特效", schema);
        AddGroup(groups, "camera", "镜头", "镜头控制", schema);

        return groups;
    }

    static void AddGroup(List<ParameterGroup> groups, string name, string display, string desc, ModelBodySchema schema)
    {
        var group = new ParameterGroup
        {
            groupName = name,
            displayName = display,
            description = desc,
        };

        foreach (var p in schema.parameters)
        {
            if (p.bodyPart == name || (name == "arm" && p.bodyPart == "arm") ||
                (name == "hand" && (p.bodyPart == "finger" || p.bodyPart == "hand")) ||
                (name == "eyes" && p.bodyPart == "eye"))
            {
                if (!string.IsNullOrEmpty(p.semantic))
                    group.paramsList.Add(p.semantic);
            }
        }

        // 特殊行为注册
        if (name == "eyes")
        {
            group.specialBehaviors.Add(new SpecialBehavior
            {
                behaviorName = "blink",
                involvedParams = new List<string> { "eye_l_open", "eye_r_open" },
                pattern = "both_sync",
                description = "眨眼：左右眼同步快速闭合再睁开"
            });
            group.specialBehaviors.Add(new SpecialBehavior
            {
                behaviorName = "gaze",
                involvedParams = new List<string> { "eye_ball_x", "eye_ball_y" },
                pattern = "coupled",
                description = "注视：眼珠X/Y组合成视线方向"
            });
        }

        groups.Add(group);
    }

    // ────────────────────────────────────────────────────────────────
    //  CDI3 JSON 解析
    // ────────────────────────────────────────────────────────────────

    /// <summary>解析 cdi3.json 中的参数中文名</summary>
    public static Dictionary<string, string> ParseCdi3Json(string json)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(json)) return result;

        try
        {
            var cdiData = JsonUtility.FromJson<Cdi3Json>(json);
            if (cdiData?.Parameters != null)
            {
                foreach (var p in cdiData.Parameters)
                {
                    if (!string.IsNullOrEmpty(p.Id) && !string.IsNullOrEmpty(p.Name))
                        result[p.Id] = p.Name;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RuntimeModelAnalyzer] 解析 cdi3.json 失败: {ex.Message}");
        }

        return result;
    }

    /// <summary>从 StreamingAssets 路径读取 cdi3.json</summary>
    public static string LoadCdi3FromStreamingAssets(string modelDirectory)
    {
        // modelDirectory 应相对于 StreamingAssets
        string fullPath = Path.Combine(Application.streamingAssetsPath, modelDirectory);
        if (!Directory.Exists(fullPath))
        {
            Debug.LogWarning($"[RuntimeModelAnalyzer] 目录不存在: {fullPath}");
            return null;
        }

        var files = Directory.GetFiles(fullPath, "*.cdi3.json", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Debug.LogWarning($"[RuntimeModelAnalyzer] 未找到 cdi3.json 在 {fullPath}");
            return null;
        }

        try
        {
            return File.ReadAllText(files[0], Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RuntimeModelAnalyzer] 读取 cdi3.json 失败: {ex.Message}");
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  输出
    // ────────────────────────────────────────────────────────────────

    /// <summary>打印分析摘要到日志</summary>
    public static void LogSchema(ModelBodySchema schema)
    {
        int mapped = 0, unmapped = 0;
        foreach (var p in schema.parameters)
        {
            if (!string.IsNullOrEmpty(p.semantic) && !p.semantic.StartsWith("unmapped_"))
                mapped++;
            else
                unmapped++;
        }

        Debug.Log($"=== ModelBodySchema: {schema.modelName} ===");
        Debug.Log($"总参数: {schema.parameters.Count}, 已映射: {mapped}, 未映射: {unmapped}");
        Debug.Log($"分组数: {schema.groups.Count}, 关联数: {schema.relations.Count}");

        foreach (var group in schema.groups)
        {
            if (group.paramsList.Count == 0) continue;
            Debug.Log($"  [{group.displayName}] ({group.paramsList.Count} 个): " +
                      string.Join(", ", group.paramsList));
        }

        if (unmapped > 0)
        {
            Debug.Log("--- 未映射参数 ---");
            foreach (var p in schema.GetUnmappedParams())
            {
                Debug.Log($"  {p.paramId,-15} [{p.min,7:F2}, {p.max,7:F2}] def={p.defaultValue:F3}  " +
                          $"cdi={p.cdiName,-10} 建议部位={p.bodyPart}");
            }
        }
    }

    /// <summary>将 schema 转为 JSON 字符串（用于持久化）</summary>
    public static string SchemaToJson(ModelBodySchema schema)
    {
        // 简化为只保存核心信息
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"modelName\": \"{EscapeJson(schema.modelName)}\",");
        sb.AppendLine($"  \"schemaVersion\": \"{schema.schemaVersion}\",");
        sb.AppendLine("  \"parameters\": [");

        for (int i = 0; i < schema.parameters.Count; i++)
        {
            var p = schema.parameters[i];
            sb.Append("    {");
            sb.Append($"\"s\":\"{EscapeJson(p.semantic)}\"");
            sb.Append($",\"p\":\"{EscapeJson(p.paramId)}\"");
            sb.Append($",\"min\":{p.min:F2},\"max\":{p.max:F2},\"def\":{p.defaultValue:F3}");
            sb.Append($",\"part\":\"{EscapeJson(p.bodyPart)}\"");
            if (!string.IsNullOrEmpty(p.cdiName))
                sb.Append($",\"cdi\":\"{EscapeJson(p.cdiName)}\"");
            if (!string.IsNullOrEmpty(p.description))
                sb.Append($",\"desc\":\"{EscapeJson(p.description)}\"");
            sb.Append("}");
            if (i < schema.parameters.Count - 1) sb.Append(",");
            sb.AppendLine();
        }

        sb.AppendLine("  ],");
        sb.AppendLine("  \"groups\": [");
        for (int i = 0; i < schema.groups.Count; i++)
        {
            var g = schema.groups[i];
            sb.Append($"    {{\"name\":\"{EscapeJson(g.groupName)}\",\"display\":\"{EscapeJson(g.displayName)}\"");
            if (g.paramsList.Count > 0)
                sb.Append($",\"params\":[\"{string.Join("\",\"", g.paramsList)}\"]");
            sb.Append("}");
            if (i < schema.groups.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}

// ────────────────────────────────────────────────────────────────
//  CDI3 JSON 数据模型（与 Verifier 中的定义一致）
// ────────────────────────────────────────────────────────────────

[Serializable]
public class Cdi3Json
{
    public Cdi3Param[] Parameters;
    public Cdi3Group[] ParameterGroups;
}

[Serializable]
public class Cdi3Param
{
    public string Id;
    public string Name;
    public string GroupId;
}

[Serializable]
public class Cdi3Group
{
    public string Id;
    public string GroupId;
    public string Name;
}
