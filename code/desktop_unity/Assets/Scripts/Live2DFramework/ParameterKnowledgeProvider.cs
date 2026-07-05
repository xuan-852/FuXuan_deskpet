using Live2D.Cubism.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 参数知识提供者 — 第三阶段：参数知识库
///
/// 职责：将 fuxuan_map.json 中的结构化参数知识（部位归属、域类型、轴方向、
/// 取值范围、关联关系、前提条件等）转化为 AI 可理解的自然语言描述，
/// 供 ChatManager.BuildSystemPrompt() 注入到 system prompt 中。
///
/// 使用方式：
///   string knowledge = ParameterKnowledgeProvider.GenerateKnowledgePrompt(mapper);
///   systemPrompt += knowledge;
///
/// 数据来源：
/// - Live2DParameterMapper: 语义名 ↔ 参数 ID，当前值，范围
/// - fuxuan_map.json: 部位(part)、域(domain)、轴(axis)、关联(related)、前提(prerequisite)
/// - group 定义: fuxuan_map.json 中的 groups 数组
/// - relation 定义: fuxuan_map.json 中的 relations 数组
/// - specialBehavior 定义: fuxuan_map.json 中的 specialBehaviors 数组
/// </summary>
public static class ParameterKnowledgeProvider
{
    // ──────────────────────────────────────────────
    //  公开入口
    // ──────────────────────────────────────────────

    /// <summary>生成完整的参数知识 system prompt 文本</summary>
    /// <param name="mapper">已加载映射的 Live2DParameterMapper 实例</param>
    /// <param name="model">CubismModel 实例（用于读取当前参数值）</param>
    /// <returns>格式化的知识提示文本，可直接追加到 system prompt</returns>
    public static string GenerateKnowledgePrompt(Live2DParameterMapper mapper, CubismModel model)
    {
        if (mapper == null || !mapper.IsLoaded)
            return "";

        var knowledge = new StringBuilder();
        knowledge.AppendLine("\n\n【你的身体参数 — 符玄】");
        knowledge.AppendLine("你拥有以下可控制的身体部位和参数。每个参数有：语义名、取值范围、当前值、部位归属、值域类型。");
        knowledge.AppendLine("你可以通过 `control_body` 工具直接设置参数值，或用 `generate_motion` 工具描述你想做的动作。");
        knowledge.AppendLine("注意：取值范围外的值会被自动截断。对称参数通常应一起控制。");
        knowledge.AppendLine();

        // 1. 解析完整映射数据
        var fullMap = LoadFullMappingData();

        // 2. 按 groups 顺序输出（保持逻辑性）
        if (fullMap.groups != null && fullMap.groups.Count > 0)
        {
            // 用 HashSet 记录已输出的参数，避免重复
            var printed = new HashSet<string>();

            foreach (var group in fullMap.groups)
            {
                string section = GenerateGroupSection(group, fullMap, mapper, model, printed);
                if (!string.IsNullOrEmpty(section))
                    knowledge.Append(section);
            }

            // 补充：groups 中未覆盖但有映射的参数
            var remaining = new List<string>();
            foreach (var semantic in mapper.SemanticToId.Keys)
            {
                if (!printed.Contains(semantic))
                    remaining.Add(semantic);
            }
            if (remaining.Count > 0)
            {
                knowledge.AppendLine("\n■ 其他参数");
                foreach (var sem in remaining)
                {
                    var entry = fullMap?.entries?.Find(e => e.s == sem);
                    knowledge.AppendLine(FormatParamLine(sem, entry, mapper, model));
                }
                knowledge.AppendLine();
            }
        }
        else
        {
            // 3. 回退：按 bodyPart 自动分组
            knowledge.Append(GenerateByBodyPart(fullMap, mapper, model));
        }

        // 4. 特殊行为说明
        if (fullMap.specialBehaviors != null && fullMap.specialBehaviors.Count > 0)
        {
            knowledge.AppendLine("【特殊行为说明】");
            foreach (var sb in fullMap.specialBehaviors)
            {
                knowledge.AppendLine($"• {sb.title} ({sb.name}): {sb.desc}");
            }
            knowledge.AppendLine();
        }

        // 5. 已知问题
        if (fullMap.knownIssues != null && fullMap.knownIssues.Count > 0)
        {
            knowledge.AppendLine("【已知参数问题】");
            foreach (var issue in fullMap.knownIssues)
            {
                knowledge.AppendLine($"  ⚠ {issue}");
            }
            knowledge.AppendLine();
        }

        return knowledge.ToString();
    }

    /// <summary>生成精简版知识（用于 token 敏感的场景）</summary>
    public static string GenerateCompactKnowledge(Live2DParameterMapper mapper, CubismModel model)
    {
        if (mapper == null || !mapper.IsLoaded)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n【身体参数概要】可通过 control_body(语义名, 值) 控制:");

        var fullMap = LoadFullMappingData();
        if (fullMap.groups != null)
        {
            foreach (var g in fullMap.groups)
            {
                sb.Append($" {g.title}:[");
                bool first = true;
                foreach (var s in g.paramSemantics)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    if (mapper.TryGetRange(s, out var range))
                        sb.Append($"{s}({range.Min:F0}~{range.Max:F0})");
                    else
                        sb.Append(s);
                }
                sb.AppendLine("]");
            }
        }
        return sb.ToString();
    }

    // ──────────────────────────────────────────────
    //  内部数据结构 — 完整映射条目
    // ──────────────────────────────────────────────

    [Serializable]
    private class FullMapData
    {
        public string formatVersion;
        public string modelName;
        public string description;
        public List<FullEntry> entries;
        public List<GroupDef> groups;
        public List<RelationDef> relations;
        public List<SpecialBehaviorDef> specialBehaviors;
        public List<string> knownIssues;
    }

    [Serializable]
    private class FullEntry
    {
        /// <summary>语义名</summary>
        public string s;
        /// <summary>参数 ID</summary>
        public string p;
        /// <summary>中文描述</summary>
        public string d;
        /// <summary>身体部位</summary>
        public string part;
        /// <summary>值域类型</summary>
        public string domain;
        /// <summary>轴方向</summary>
        public string axis;
        /// <summary>最小值</summary>
        public float min;
        /// <summary>最大值</summary>
        public float max;
        /// <summary>关联参数</summary>
        public List<string> related;
        /// <summary>关联类型</summary>
        public string relType;
        /// <summary>前提条件（如 sword_finger_switch=1）</summary>
        public string prerequisite;
        /// <summary>左右侧</summary>
        public string side;
    }

    [Serializable]
    private class GroupDef
    {
        /// <summary>分组标识</summary>
        public string g;
        /// <summary>显示标题</summary>
        public string title;
        /// <summary>参数语义名列表</summary>
        public List<string> paramSemantics;
    }

    [Serializable]
    private class RelationDef
    {
        public string type;
        public string a;
        public string b;
        public string desc;
    }

    [Serializable]
    private class SpecialBehaviorDef
    {
        public string name;
        public string title;
        public List<string> @params;
        public string pattern;
        public string desc;
    }

    // ──────────────────────────────────────────────
    //  内部数据加载
    // ──────────────────────────────────────────────

    private static FullMapData _cache;
    private static bool _cacheAttempted;

    /// <summary>从 Resources 加载完整映射数据</summary>
    private static FullMapData LoadFullMappingData()
    {
        if (_cacheAttempted)
            return _cache;

        _cacheAttempted = true;

        try
        {
            // 优先从 Resources 加载
            TextAsset asset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
            if (asset != null)
            {
                _cache = ParseFullMapping(asset.text);
                if (_cache != null)
                {
                    Debug.Log($"[ParameterKnowledgeProvider] 已从 Resources 加载: {_cache.entries?.Count ?? 0} 条目");
                    return _cache;
                }
            }

            // 回退：从文件系统加载
            string filePath = Path.Combine(Application.dataPath,
                "Scripts/Live2DFramework/ParamMaps/fuxuan_map.json");
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                _cache = ParseFullMapping(json);
                Debug.Log($"[ParameterKnowledgeProvider] 已从文件系统加载: {_cache?.entries?.Count ?? 0} 条目");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ParameterKnowledgeProvider] 加载映射数据失败: {ex.Message}");
        }

        return _cache;
    }

    /// <summary>解析 JSON 为 FullMapData（兼容 v2 entries 格式和旧格式）</summary>
    private static FullMapData ParseFullMapping(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        // 尝试标准解析：先看 entries 格式
        var wrapper = JsonUtility.FromJson<EntryWrapper>(json);
        if (wrapper?.entries != null && wrapper.entries.Length > 0)
        {
            var data = new FullMapData
            {
                formatVersion = wrapper.formatVersion,
                modelName = wrapper.modelName,
                description = wrapper.description,
                entries = new List<FullEntry>(),
                groups = new List<GroupDef>(),
                relations = new List<RelationDef>(),
                specialBehaviors = new List<SpecialBehaviorDef>(),
                knownIssues = new List<string>()
            };

            // 转换 entries
            foreach (var e in wrapper.entries)
            {
                data.entries.Add(new FullEntry
                {
                    s = e.s,
                    p = e.p,
                    d = e.d ?? "",
                    part = e.part ?? "unknown",
                    domain = e.domain ?? "normalized",
                    axis = e.axis ?? "",
                    min = e.min,
                    max = e.max,
                    related = e.related != null ? new List<string>(e.related) : null,
                    relType = e.relType ?? "",
                    prerequisite = e.prerequisite ?? "",
                    side = e.side ?? ""
                });
            }

            // 如果 JSON 还包含 groups/relations/specialBehaviors，需要二次解析
            // 因为 JsonUtility 不支持嵌套数组对象的反序列化
            // 使用 JSONObject 方式补解析
            try
            {
                var jo = JsonUtility.FromJson<FullMapData>(json);
                if (jo != null)
                {
                    if (jo.groups != null) data.groups = jo.groups;
                    if (jo.relations != null) data.relations = jo.relations;
                    if (jo.specialBehaviors != null) data.specialBehaviors = jo.specialBehaviors;
                    if (jo.knownIssues != null) data.knownIssues = jo.knownIssues;
                }
            }
            catch
            {
                // 如果直接解析失败，说明 groups 等字段需要特殊处理
                // 但我们已在 json 中有这些数据，手动解析
                try { ParseSupplementaryFields(json, data); }
                catch { /* 忽略辅助字段解析错误 */ }
            }

            return data;
        }

        return null;
    }

    /// <summary>辅助解析 groups/relations/specialBehaviors/knownIssues</summary>
    private static void ParseSupplementaryFields(string json, FullMapData data)
    {
        // 使用简单的字符串解析提取 JSON 数组
        // groups
        var groupsMatch = System.Text.RegularExpressions.Regex.Match(json,
            "\"groups\"\\s*:\\s*\\[\\s*(.*?)\\]\\s*(?:,\\s*\"|\n\\s*])",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (groupsMatch.Success)
            TryParseGroups(groupsMatch.Groups[1].Value, data);

        // relations
        var relMatch = System.Text.RegularExpressions.Regex.Match(json,
            "\"relations\"\\s*:\\s*\\[\\s*(.*?)\\]\\s*(?:,\\s*\"|\n\\s*])",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (relMatch.Success)
            TryParseRelations(relMatch.Groups[1].Value, data);

        // specialBehaviors
        var sbMatch = System.Text.RegularExpressions.Regex.Match(json,
            "\"specialBehaviors\"\\s*:\\s*\\[\\s*(.*?)\\]\\s*(?:,\\s*\"|\n\\s*])",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (sbMatch.Success)
            TryParseSpecialBehaviors(sbMatch.Groups[1].Value, data);

        // knownIssues
        var kiMatch = System.Text.RegularExpressions.Regex.Match(json,
            "\"knownIssues\"\\s*:\\s*\\[\\s*(.*?)\\]\\s*(?:,\\s*\"|\\n\\s*])",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (kiMatch.Success)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(kiMatch.Groups[1].Value,
                "\"([^\"]+)\"");
            foreach (System.Text.RegularExpressions.Match m in matches)
                data.knownIssues.Add(m.Groups[1].Value);
        }
    }

    private static void TryParseGroups(string groupJson, FullMapData data)
    {
        // 分组解析：每个对象有 "g", "title", "params" 数组
        var matches = System.Text.RegularExpressions.Regex.Matches(groupJson,
            "\\{\\s*\"g\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"title\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"params\"\\s*:\\s*\\[\\s*([^\\]]+)\\]");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var g = new GroupDef
            {
                g = m.Groups[1].Value,
                title = m.Groups[2].Value,
                paramSemantics = new List<string>()
            };
            var paramMatches = System.Text.RegularExpressions.Regex.Matches(m.Groups[3].Value, "\"([^\"]+)\"");
            foreach (System.Text.RegularExpressions.Match pm in paramMatches)
                g.paramSemantics.Add(pm.Groups[1].Value);
            data.groups.Add(g);
        }
    }

    private static void TryParseRelations(string relJson, FullMapData data)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(relJson,
            "\\{\\s*\"type\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"a\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"b\"\\s*:\\s*\"([^\"]+)\"\\s*(?:,\\s*\"desc\"\\s*:\\s*\"([^\"]*)\")?");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            data.relations.Add(new RelationDef
            {
                type = m.Groups[1].Value,
                a = m.Groups[2].Value,
                b = m.Groups[3].Value,
                desc = m.Groups[4].Success ? m.Groups[4].Value : ""
            });
        }
    }

    private static void TryParseSpecialBehaviors(string sbJson, FullMapData data)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(sbJson,
            "\\{\\s*\"name\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"title\"\\s*:\\s*\"([^\"]+)\"");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string name = m.Groups[1].Value;
            string title = m.Groups[2].Value;
            // 提取 desc
            var descMatch = System.Text.RegularExpressions.Regex.Match(sbJson,
                "\"desc\"\\s*:\\s*\"([^\"]+)\"");
            data.specialBehaviors.Add(new SpecialBehaviorDef
            {
                name = name,
                title = title,
                desc = descMatch.Success ? descMatch.Groups[1].Value : ""
            });
        }
    }

    [Serializable]
    private class EntryWrapper
    {
        public string formatVersion;
        public string modelName;
        public string description;
        public MapEntry[] entries;
    }

    [Serializable]
    private class MapEntry
    {
        public string s;
        public string p;
        public string d;
        public string part;
        public string domain;
        public string axis;
        public float min;
        public float max;
        public string[] related;
        public string relType;
        public string prerequisite;
        public string side;
    }

    // ──────────────────────────────────────────────
    //  文本生成
    // ──────────────────────────────────────────────

    /// <summary>按 groups 定义生成部位段落</summary>
    private static string GenerateGroupSection(
        GroupDef group,
        FullMapData fullMap,
        Live2DParameterMapper mapper,
        CubismModel model,
        HashSet<string> printed)
    {
        if (group.paramSemantics == null || group.paramSemantics.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine($"■ {group.title} ({group.g})");

        foreach (var semantic in group.paramSemantics)
        {
            if (printed.Contains(semantic)) continue;
            printed.Add(semantic);

            // 查找完整条目数据
            var entry = fullMap?.entries?.Find(e => e.s == semantic);
            sb.AppendLine(FormatParamLine(semantic, entry, mapper, model));

            // 附加关联关系
            var relations = GetRelationsFor(semantic, fullMap);
            if (relations.Count > 0)
            {
                foreach (var rel in relations)
                {
                    sb.AppendLine($"    ↳ 关联: {rel}");
                }
            }

            // 附加前提条件
            if (entry != null && !string.IsNullOrEmpty(entry.prerequisite))
            {
                sb.AppendLine($"    ⚠ 前提: {TranslatePrerequisite(entry.prerequisite, fullMap)}");
            }
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>按 bodyPart 自动分组（回退方案）</summary>
    private static string GenerateByBodyPart(
        FullMapData fullMap,
        Live2DParameterMapper mapper,
        CubismModel model)
    {
        if (fullMap?.entries == null || fullMap.entries.Count == 0)
            return "";

        var sb = new StringBuilder();
        var byPart = new Dictionary<string, List<FullEntry>>();

        foreach (var entry in fullMap.entries)
        {
            string part = string.IsNullOrEmpty(entry.part) ? "unknown" : entry.part;
            if (!byPart.ContainsKey(part))
                byPart[part] = new List<FullEntry>();
            byPart[part].Add(entry);
        }

        // 定义部位显示顺序
        var partOrder = new[] {
            "head", "eye", "brow", "mouth", "body",
            "arm", "hand", "finger", "leg", "shoulder",
            "hair", "skirt", "special", "camera", "unknown"
        };

        var printed = new HashSet<string>();

        foreach (var part in partOrder)
        {
            if (!byPart.TryGetValue(part, out var entries)) continue;

            string title = GetPartDisplayName(part);
            sb.AppendLine($"■ {title}");

            foreach (var entry in entries)
            {
                if (printed.Contains(entry.s)) continue;
                printed.Add(entry.s);

                sb.AppendLine(FormatParamLine(entry.s, entry, mapper, model));

                if (!string.IsNullOrEmpty(entry.prerequisite))
                    sb.AppendLine($"    ⚠ 前提: {TranslatePrerequisite(entry.prerequisite, fullMap)}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>格式化单行参数描述</summary>
    private static string FormatParamLine(
        string semantic,
        FullEntry entry,
        Live2DParameterMapper mapper,
        CubismModel model)
    {
        string desc = entry?.d ?? "";
        string domain = entry?.domain ?? "";
        string axis = entry?.axis ?? "";
        string side = entry?.side ?? "";

        string rangeStr = "";
        float currentVal = 0f;
        bool hasCurrent = false;

        if (mapper.TryGetRange(semantic, out var range))
        {
            rangeStr = $"[{range.Min:F1}~{range.Max:F1}]";
            currentVal = mapper.Get(semantic);
            hasCurrent = true;
        }
        else if (entry != null)
        {
            // fallback: use entry's min/max
            rangeStr = $"[{entry.min:F1}~{entry.max:F1}]";
        }

        string sideStr = "";
        if (!string.IsNullOrEmpty(side))
            sideStr = side == "left" ? "（左）" : side == "right" ? "（右）" : "";

        string axisStr = "";
        if (!string.IsNullOrEmpty(axis))
            axisStr = $"{axis}轴";

        string domainStr = GetDomainSymbol(domain);

        string currentStr = hasCurrent ? $"，当前={currentVal:F2}" : "";

        return $"  {domainStr} {semantic,-22} {rangeStr,-12} {desc}{sideStr} {axisStr}{currentStr}";
    }

    /// <summary>获取某参数的所有关联关系描述</summary>
    private static List<string> GetRelationsFor(string semantic, FullMapData fullMap)
    {
        var results = new List<string>();
        if (fullMap?.relations == null) return results;

        foreach (var rel in fullMap.relations)
        {
            if (rel.a == semantic)
            {
                // a 关联到 b
                string typeStr = rel.type switch
                {
                    "symmetrical" => "左右对称",
                    "coupled" => "组合关联",
                    "prerequisite" => "前提条件",
                    _ => rel.type
                };
                results.Add($"{typeStr}: {rel.a} ↔ {rel.b}（{rel.desc}）");
            }
            else if (rel.b == semantic)
            {
                string typeStr = rel.type switch
                {
                    "symmetrical" => "左右对称",
                    "coupled" => "组合关联",
                    "prerequisite" => "前提条件",
                    _ => rel.type
                };
                // 避免对同一关系重复输出相同的描述
                string desc = $"{typeStr}: {rel.a} ↔ {rel.b}（{rel.desc}）";
                if (!results.Contains(desc))
                    results.Add(desc);
            }

            // 处理 b 可能是逗号分隔的列表（prerequisite 场景）
            if (rel.type == "prerequisite" && !string.IsNullOrEmpty(rel.b))
            {
                var parts = rel.b.Split(',');
                foreach (var p in parts)
                {
                    if (p.Trim() == semantic && rel.a != semantic)
                    {
                        string desc = $"前提条件: {rel.a} → {rel.b}（{rel.desc}）";
                        if (!results.Contains(desc))
                            results.Add(desc);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>将前提条件转为可读文本</summary>
    private static string TranslatePrerequisite(string prerequisite, FullMapData fullMap)
    {
        // 格式如 "sword_finger_switch=1"
        var parts = prerequisite.Split('=');
        if (parts.Length == 2)
        {
            string semantic = parts[0].Trim();
            string val = parts[1].Trim();

            // 查找中文名
            string displayName = semantic;
            if (fullMap?.entries != null)
            {
                var entry = fullMap.entries.Find(e => e.s == semantic);
                if (entry != null && !string.IsNullOrEmpty(entry.d))
                    displayName = $"{entry.d}({semantic})";
            }

            return $"需要先设置 {displayName}={val} 此参数才生效";
        }
        return prerequisite;
    }

    /// <summary>获取部位显示名</summary>
    private static string GetPartDisplayName(string part)
    {
        return part switch
        {
            "head" => "头部 (head)",
            "eye" or "eyes" => "眼睛 (eyes)",
            "brow" => "眉毛 (brow)",
            "mouth" => "嘴巴 (mouth)",
            "body" => "身体 (body)",
            "arm" => "手臂 (arm)",
            "hand" => "手掌 (hand)",
            "finger" => "手指 (finger)",
            "leg" => "腿 (leg)",
            "shoulder" => "肩膀 (shoulder)",
            "hair" => "头发 (hair)",
            "skirt" => "裙子/衣饰 (skirt)",
            "special" => "特殊效果 (special)",
            "camera" => "镜头/覆盖层 (camera)",
            _ => part
        };
    }

    /// <summary>值域类型符号</summary>
    private static string GetDomainSymbol(string domain)
    {
        return domain switch
        {
            "normalized" => "◈",   // 归一化连续
            "angle" => "◎",        // 角度
            "position" => "◆",     // 位移
            "toggle" => "◉",       // 开关
            "scale" => "⬥",        // 缩放
            "alpha" => "◐",        // 透明度
            _ => "◇"               // 其他
        };
    }

    // ──────────────────────────────────────────────
    //  运行时查询辅助
    // ──────────────────────────────────────────────

    /// <summary>获取某个部位的所有参数语义名列表</summary>
    public static List<string> GetParametersByBodyPart(string bodyPart)
    {
        var data = LoadFullMappingData();
        if (data?.entries == null) return new List<string>();

        return data.entries
            .FindAll(e => e.part == bodyPart)
            .ConvertAll(e => e.s);
    }

    /// <summary>获取某个参数的完整元数据</summary>
    public static string GetParameterMeta(string semantic)
    {
        var data = LoadFullMappingData();
        if (data?.entries == null) return "";

        var entry = data.entries.Find(e => e.s == semantic);
        if (entry == null) return "";

        var sb = new StringBuilder();
        sb.Append($"语义: {entry.s}, ID: {entry.p}");
        if (!string.IsNullOrEmpty(entry.d)) sb.Append($", 描述: {entry.d}");
        sb.Append($", 部位: {entry.part}, 域: {entry.domain}");
        if (!string.IsNullOrEmpty(entry.axis)) sb.Append($", 轴: {entry.axis}");
        sb.Append($", 范围: [{entry.min}~{entry.max}]");
        if (!string.IsNullOrEmpty(entry.side)) sb.Append($", 侧: {entry.side}");
        if (!string.IsNullOrEmpty(entry.prerequisite))
            sb.Append($", 前提: {entry.prerequisite}");
        if (entry.related != null && entry.related.Count > 0)
            sb.Append($", 关联: {string.Join(",", entry.related)}({entry.relType})");
        return sb.ToString();
    }
}
