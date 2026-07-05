using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 参数关联检测器 — 自动发现参数之间的关联关系
///
/// 检测策略：
/// 1. 范围对称检测：两个参数的 min/max 范围完全相同 → 可能是一对左右对称参数
/// 2. 命名对称检测：两个参数的 ID 或 cdi 名呈左右对称模式
/// 3. 范围近似检测：多个参数范围大小近似 → 可能是同组部位参数
/// 4. 端点关联检测：参数出现在相同的极限值 → 可能互斥或耦合
///
/// 输出：List<ParameterRelation> 供 ModelBodySchema.relations 使用
/// </summary>
public static class ParameterRelationDetector
{
    /// <summary>检测所有参数关联，返回发现的关联列表</summary>
    public static List<ParameterRelation> DetectRelations(List<ParameterDef> parameters)
    {
        var relations = new List<ParameterRelation>();
        if (parameters == null || parameters.Count < 2) return relations;

        // 1. 对称关联检测（左右对称参数对）
        DetectSymmetricalPairs(parameters, relations);

        // 2. 命名对称检测（ID 或 cdi 名中的 L/R 对）
        DetectNamingSymmetry(parameters, relations);

        // 3. 同部位范围近似组
        DetectSameRangeGroups(parameters, relations);

        // 4. 开关-从属关联
        DetectToggleDependencies(parameters, relations);

        // 去重
        relations = Deduplicate(relations);

        return relations;
    }

    /// <summary>获取关联摘要文本（供 AI 使用）</summary>
    public static string GetRelationsSummary(List<ParameterRelation> relations)
    {
        if (relations == null || relations.Count == 0)
            return "未发现参数关联关系。";

        var lines = new List<string>();
        lines.Add($"发现 {relations.Count} 个参数关联关系：\n");

        foreach (var rel in relations)
        {
            string icon = rel.type switch
            {
                RelationType.Symmetrical => "🔗",
                RelationType.Coupled => "🔄",
                RelationType.Prerequisite => "🔐",
                RelationType.MutuallyExclusive => "⛔",
                RelationType.Dependent => "🔀",
                RelationType.SameRange => "📏",
                _ => "•"
            };

            string semStr = string.Join(", ", rel.involvedSemantics);
            if (string.IsNullOrEmpty(semStr))
                semStr = string.Join(", ", rel.involvedParamIds);

            lines.Add($"  {icon} [{rel.type}] {semStr}");
            if (!string.IsNullOrEmpty(rel.description))
                lines.Add($"     {rel.description}");
        }

        return string.Join("\n", lines);
    }

    // ────────────────────────────────────────────────────────────────
    //  检测方法
    // ────────────────────────────────────────────────────────────────

    /// <summary>检测范围完全相同的参数对（对称关联）</summary>
    static void DetectSymmetricalPairs(List<ParameterDef> parameters, List<ParameterRelation> relations)
    {
        // 寻找范围完全相同的参数对：如 (eye_l_open, eye_r_open), (arm_right_upper, arm_left_upper)
        for (int i = 0; i < parameters.Count; i++)
        {
            for (int j = i + 1; j < parameters.Count; j++)
            {
                var a = parameters[i];
                var b = parameters[j];

                // 范围必须完全相同
                if (Math.Abs(a.min - b.min) > 0.01f || Math.Abs(a.max - b.max) > 0.01f)
                    continue;

                // 检查是否是一对左右对称参数
                if (IsSymmetricalPair(a.semantic, b.semantic, a.paramId, b.paramId, a.cdiName, b.cdiName))
                {
                    var rel = new ParameterRelation
                    {
                        type = RelationType.Symmetrical,
                        involvedSemantics = new List<string> { a.semantic, b.semantic },
                        involvedParamIds = new List<string> { a.paramId, b.paramId },
                        description = $"{GetDisplayName(a)} 与 {GetDisplayName(b)} 是对称关联（范围完全相同）",
                        confidence = 0.8f,
                        strength = 0.9f,
                    };

                    // 检查是否高度对称（语义名都有且完善）
                    if (!a.semantic.StartsWith("unmapped_") && !b.semantic.StartsWith("unmapped_"))
                        rel.confidence = 0.95f;

                    relations.Add(rel);
                }
            }
        }
    }

    /// <summary>检测命名对称性（参数 ID 或 cdi 名中的 L/R 模式）</summary>
    static void DetectNamingSymmetry(List<ParameterDef> parameters, List<ParameterRelation> relations)
    {
        // 按 cdi 名分组检测：名字中带"左"和带"右"的对应参数
        var leftParams = parameters.FindAll(p =>
            (!string.IsNullOrEmpty(p.cdiName) && p.cdiName.Contains("左")) ||
            (!string.IsNullOrEmpty(p.semantic) && p.semantic.Contains("_l_")));
        var rightParams = parameters.FindAll(p =>
            (!string.IsNullOrEmpty(p.cdiName) && p.cdiName.Contains("右")) ||
            (!string.IsNullOrEmpty(p.semantic) && p.semantic.Contains("_r_")));

        // 尝试按名称模式配对
        foreach (var left in leftParams)
        {
            string leftBase = ExtractBaseName(left.semantic);
            if (string.IsNullOrEmpty(leftBase)) leftBase = ExtractBaseName(left.cdiName);

            foreach (var right in rightParams)
            {
                string rightBase = ExtractBaseName(right.semantic);
                if (string.IsNullOrEmpty(rightBase)) rightBase = ExtractBaseName(right.cdiName);

                if (leftBase == rightBase && left.paramId != right.paramId)
                {
                    // 检查是否已存在关联
                    bool exists = relations.Any(r =>
                        (r.involvedParamIds.Contains(left.paramId) && r.involvedParamIds.Contains(right.paramId)));
                    if (!exists)
                    {
                        relations.Add(new ParameterRelation
                        {
                            type = RelationType.Symmetrical,
                            involvedSemantics = new List<string> { left.semantic, right.semantic },
                            involvedParamIds = new List<string> { left.paramId, right.paramId },
                            description = $"基于命名对称：{GetDisplayName(left)} ↔ {GetDisplayName(right)}",
                            confidence = 0.7f,
                            strength = 0.8f,
                        });
                    }
                }
            }
        }
    }

    /// <summary>检测同部位范围近似参数组</summary>
    static void DetectSameRangeGroups(List<ParameterDef> parameters, List<ParameterRelation> relations)
    {
        // 按身体部位分组
        var grouped = parameters.GroupBy(p => p.bodyPart);

        foreach (var group in grouped)
        {
            var list = group.ToList();
            if (list.Count < 3) continue; // 至少三个参数才有"组"的概念

            // 对组内参数的范围幅度进行聚类
            var rangeClusters = new Dictionary<float, List<ParameterDef>>();
            foreach (var p in list)
            {
                float range = Mathf.Abs(p.max - p.min);
                float bucket = RoundToBucket(range);
                if (!rangeClusters.ContainsKey(bucket))
                    rangeClusters[bucket] = new List<ParameterDef>();
                rangeClusters[bucket].Add(p);
            }

            foreach (var kv in rangeClusters)
            {
                if (kv.Value.Count >= 3) // 至少三个范围近似的同部位参数
                {
                    bool exists = relations.Any(r =>
                        r.type == RelationType.SameRange &&
                        r.involvedParamIds.Any(id => kv.Value.Any(p => p.paramId == id)));

                    if (!exists)
                    {
                        relations.Add(new ParameterRelation
                        {
                            type = RelationType.SameRange,
                            involvedParamIds = kv.Value.Select(p => p.paramId).ToList(),
                            involvedSemantics = kv.Value.Select(p => p.semantic).ToList(),
                            description = $"{kv.Value.Count} 个 {kv.Value[0].bodyPart} 参数范围相近 (~{kv.Key:F1})，可能属于同一组",
                            confidence = 0.6f,
                            strength = 0.5f,
                        });
                    }
                }
            }
        }
    }

    /// <summary>检测开关-从属关系（toggle 参数与其他参数的关联）</summary>
    static void DetectToggleDependencies(List<ParameterDef> parameters, List<ParameterRelation> relations)
    {
        // 找到所有 toggle 类型的参数
        var toggles = parameters.FindAll(p => p.valueDomain == ValueDomain.Toggle);

        foreach (var toggle in toggles)
        {
            // 查找同部位的其他参数，toggle 可能是它们的前置条件
            string togglePart = toggle.bodyPart;
            var dependents = parameters.FindAll(p =>
                p.bodyPart == togglePart && p.valueDomain != ValueDomain.Toggle && p.paramId != toggle.paramId);

            if (dependents.Count >= 2)
            {
                bool exists = relations.Any(r =>
                    r.type == RelationType.Prerequisite &&
                    r.involvedParamIds.Contains(toggle.paramId));

                if (!exists)
                {
                    relations.Add(new ParameterRelation
                    {
                        type = RelationType.Prerequisite,
                        involvedParamIds = new List<string> { toggle.paramId },
                        involvedSemantics = new List<string> { toggle.semantic },
                        description = $"{GetDisplayName(toggle)}（开关型）可能是以下参数的前置条件：{string.Join(", ", dependents.Select(d => GetDisplayName(d)))}",
                        confidence = 0.4f, // 较低置信度，需要人工确认
                        strength = 0.5f,
                    });
                }
            }
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  辅助方法
    // ────────────────────────────────────────────────────────────────

    /// <summary>判断两个参数是否构成左右对称对</summary>
    static bool IsSymmetricalPair(string semA, string semB, string idA, string idB, string cdiA, string cdiB)
    {
        // 语义名中的左右标记
        bool hasL_r = (semA?.Contains("_l_") == true && semB?.Contains("_r_") == true) ||
                      (semA?.Contains("_r_") == true && semB?.Contains("_l_") == true);
        if (hasL_r)
        {
            // 去掉左右标记后基础名相同
            string baseA = ExtractBaseName(semA);
            string baseB = ExtractBaseName(semB);
            if (baseA == baseB) return true;
        }

        // ID 中的数字跨度：只有一位数字差且相邻（如 Param31 + Param32 可能是上下臂）
        // 但对于左右对称不太适用，留待后续扩展

        // cdi 名中的左右标记
        bool cdiL = cdiA.Contains("左") || cdiA.Contains("L");
        bool cdiR = cdiB.Contains("右") || cdiB.Contains("R");
        bool cdiL2 = cdiA.Contains("右") || cdiA.Contains("R");
        bool cdiR2 = cdiB.Contains("左") || cdiB.Contains("L");
        if ((cdiL && cdiR) || (cdiL2 && cdiR2)) return true;

        return false;
    }

    /// <summary>从语义名中提取基础名（去掉左右前后缀）</summary>
    static string ExtractBaseName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        string result = name.ToLower();
        result = result.Replace("_l_", "_{side}_");
        result = result.Replace("_r_", "_{side}_");
        result = result.Replace("right_", "{side}_");
        result = result.Replace("left_", "{side}_");
        result = result.Replace("_right", "_{side}");
        result = result.Replace("_left", "_{side}");

        return result;
    }

    /// <summary>获取参数的可读名称</summary>
    static string GetDisplayName(ParameterDef def)
    {
        if (!string.IsNullOrEmpty(def.semantic) && !def.semantic.StartsWith("unmapped_"))
            return def.semantic;
        if (!string.IsNullOrEmpty(def.cdiName))
            return def.cdiName;
        return def.paramId;
    }

    /// <summary>对范围值进行分桶（用于聚类）</summary>
    static float RoundToBucket(float range)
    {
        if (range <= 1f) return 0f;
        if (range <= 5f) return 3f;
        if (range <= 10f) return 7f;
        if (range <= 20f) return 15f;
        if (range <= 30f) return 25f;
        if (range <= 60f) return 45f;
        if (range <= 90f) return 75f;
        if (range <= 120f) return 100f;
        return 150f;
    }

    /// <summary>去重</summary>
    static List<ParameterRelation> Deduplicate(List<ParameterRelation> relations)
    {
        var result = new List<ParameterRelation>();
        var seen = new HashSet<string>();

        foreach (var rel in relations)
        {
            string key = rel.type + ":" + string.Join(",", rel.involvedParamIds.OrderBy(id => id));
            if (seen.Contains(key)) continue;
            seen.Add(key);
            result.Add(rel);
        }

        return result;
    }
}
