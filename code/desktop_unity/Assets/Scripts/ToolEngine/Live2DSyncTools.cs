using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ================================================================
//  Live2D 法身控制 — 同步工具（无需网络）
// ================================================================

public class SetExpressionTool : IPetTool
{
    public string ToolName => "set_expression";
    public string ToolDescription => "【法身·变脸术】切换桌面宠物的面部表情。用户说「笑一个」「换个表情」「伤心脸」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""expression"":{""type"":""string"",""description"":""表情名称，如 happy/sad/angry/surprised/cry/embarrassed/love/default 等""}},""required"":[""expression""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string exp = ToolHelpers.JsonRead(argsJson, "expression");
        if (string.IsNullOrEmpty(exp)) return "❌ 未指定表情";
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null) return "❌ 本座法身未现";
        renderer.PlayExpression(exp);
        return $"✅ 已切换表情为「{exp}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class PlayActionTool : IPetTool
{
    public string ToolName => "play_action";
    public string ToolDescription => "【法身·演武】播放一段复合动作（挥手/点头/摇头/叉腰/跳舞等）。用户说「做个动作」「挥手」「跳个舞」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""action"":{""type"":""string"",""description"":""动作名称，如 wave/nod/shake_hands/bow/stretch/dance 等""}},""required"":[""action""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string action = ToolHelpers.JsonRead(argsJson, "action");
        if (string.IsNullOrEmpty(action)) return "❌ 未指定动作";
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null) return "❌ 本座法身未现";
        renderer.PlayAction(action);
        return $"✅ 正在演武「{action}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class StopActionTool : IPetTool
{
    public string ToolName => "stop_action";
    public string ToolDescription => "【法身·归元】停止当前所有动作和表情，恢复常态。用户说「停下来」「归元」「别动了」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null) return "❌ 本座法身未现";
        renderer.ActionController?.StopAllWithFade();
        return "✅ 已归元，恢复常态";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class InspectMotionMemoryTool : IPetTool
{
    public string ToolName => "inspect_motion_memory";
    public string ToolDescription => "【法身·演武心经】查看闭环修为统计——所有演武动作的记录、得分和最佳成绩。用户问「动作记录」「演武统计」「看看学了多少动作」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var mm = MotionMemoryManager.Instance;
        if (mm == null) return "❌ 演武心经未载入";
        return mm.GetStatistics();
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class InspectPersonalityTool : IPetTool
{
    public string ToolName => "inspect_personality";
    public string ToolDescription => "【法身·本心】查看当前人格特质与关系状态。用户问「你现在是什么性格」「我们的关系如何」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var pm = PersonalityManager.Instance;
        if (pm == null) return "❌ 本心未载入";
        return pm.FormatForPrompt();
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class ExploreBodySyncTool : IPetTool
{
    public string ToolName => "explore_body";
    public string ToolDescription => "【法身·内观自省】查看当前身体各部位参数状态（同步版本，无需GLM视觉）。输出头部/眼睛/眉毛/嘴/身体/手臂等所有可动部件的实时参数值。用户问「你现在是什么姿势」「参数状态」时调用。完整视觉分析用异步版本。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";
    public bool IsAsync => true; // 既有同步快照又有异步GLM分析，以异步为主

    public string Execute(string argsJson) => "⏳ 本座正在内观……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || renderer.CubismModel == null)
        {
            onResult?.Invoke("❌ 本座法身未现");
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;
        var lines = new List<string>
        {
            $"🧘 本座当前状态 ({DateTime.Now:HH:mm:ss})："
        };

        var partOrder = new[] { "head", "eye", "brow", "mouth", "body", "arm", "hand", "leg", "shoulder", "hair", "skirt", "special", "camera", "breath" };
        var partLabels = new Dictionary<string, string>
        {
            ["head"] = "头部", ["eye"] = "眼睛", ["brow"] = "眉毛", ["mouth"] = "嘴",
            ["body"] = "身体", ["arm"] = "手臂", ["hand"] = "手", ["leg"] = "腿",
            ["shoulder"] = "肩膀", ["hair"] = "头发", ["skirt"] = "裙子",
            ["special"] = "特殊", ["camera"] = "镜头", ["breath"] = "呼吸"
        };

        // 从映射数据获取 bodyPart 标注
        var entryPartMap = new Dictionary<string, string>();
        var mapAsset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
        if (mapAsset != null)
        {
            try
            {
                var mapObj = UnityEngine.JsonUtility.FromJson<FuxuanMapData>(mapAsset.text);
                if (mapObj?.entries != null)
                    foreach (var e in mapObj.entries)
                        if (!string.IsNullOrEmpty(e.part))
                            entryPartMap[e.s] = e.part;
            }
            catch { }
        }

        var byPart = new Dictionary<string, List<string>>();
        foreach (var semantic in mapper.SemanticToId.Keys)
        {
            if (!mapper.TryGetRange(semantic, out var range)) continue;
            float current = mapper.Get(semantic);
            float normalized = Mathf.Abs(current - range.Default) / Mathf.Max(range.Max - range.Min, 0.001f);
            string activeMark = normalized > 0.05f ? " ⚡" : "";
            string part = entryPartMap.TryGetValue(semantic, out var p) ? p : "other";
            if (!byPart.ContainsKey(part)) byPart[part] = new List<string>();
            byPart[part].Add($"  {semantic}={current:F2}[{range.Min:F0}~{range.Max:F0}]{activeMark}");
        }

        foreach (var part in partOrder)
        {
            if (!byPart.TryGetValue(part, out var plist)) continue;
            string label = partLabels.TryGetValue(part, out var lb) ? lb : part;
            lines.Add($"\n■ {label} ({plist.Count})");
            lines.AddRange(plist);
        }

        if (byPart.TryGetValue("other", out var others))
        {
            lines.Add($"\n■ 其他 ({others.Count})");
            lines.AddRange(others);
        }

        string result = string.Join("\n", lines);
        if (result.Length > 1500) result = result[..1500] + "\n...（截断，完整版请使用异步内观）";
        onResult?.Invoke(result);
    }
}

public class ControlBodyTool : IPetTool
{
    public string ToolName => "control_body";
    public string ToolDescription => "【法身·御形】精确控制身体参数或套用表情模板。用户说「把头转过来」「抬起右手」「歪头笑」时调用。可指定具体参数值（如 ParamAngleX=15）或表情名称。自动锁定防空闲动画覆盖，持续到下次解锁。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""expression"":{""type"":""string"",""description"":""表情/姿势模板名称，如 happy/sad/angry/tilt_head 等（可选）""},""params"":{""type"":""object"",""description"":""具体参数映射，如 {""ParamAngleX"":15,""ParamAngleY"":5}（可选）""}},""required"":[]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null) return "❌ 本座法身未现";

        var mapper = renderer.Mapper;

        // 设置 AI 控制锁
        renderer.SetAiControlLock();

        // 解析 expression
        string expression = ToolHelpers.JsonRead(argsJson, "expression");
        if (!string.IsNullOrEmpty(expression))
        {
            var templates = MotionPlanner.PlanFromDescription(expression, 1f, mapper);
            if (templates.KeyFrames.Count > 0)
            {
                foreach (var kv in templates.KeyFrames[0].Values)
                    mapper.Set(kv.Key, kv.Value);
            }
        }

        var controlKeys = new HashSet<string> { "expression", "duration" };
        var paramValues = new Dictionary<string, float>();
        var warnings = new List<string>();

        // 解析顶层参数
        foreach (string rawPair in argsJson.Split(','))
        {
            string pair = rawPair.Trim().TrimStart('{').TrimEnd('}');
            int colonIdx = pair.IndexOf(':');
            if (colonIdx < 0) continue;

            string key = pair.Substring(0, colonIdx).Trim().Trim('"');
            string valStr = pair.Substring(colonIdx + 1).Trim().Trim('"');
            if (controlKeys.Contains(key)) continue;
            if (valStr.StartsWith("{") || valStr.StartsWith("[")) continue;

            if (float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                paramValues[key] = val;
            }
        }

        // 从 params 字段读取
        var fromParams = ToolHelpers.JsonReadDict(argsJson, "params");
        foreach (var kv in fromParams)
            paramValues[kv.Key] = kv.Value;

        if (paramValues.Count == 0 && string.IsNullOrEmpty(expression))
            return "❌ 未指定任何参数或表情";

        // 安全校验
        var results = SafetyValidator.ValidateBulk(paramValues, mapper);
        foreach (var r in results)
            warnings.AddRange(r.Warnings);

        // 应用参数
        int applied = 0;
        foreach (var kv in paramValues)
        {
            mapper.Set(kv.Key, kv.Value);
            applied++;
        }

        string result = $"✅ 已御形：{applied} 个参数已调整";
        if (warnings.Count > 0)
            result += "\n⚠ 注意：\n" + string.Join("\n", warnings);
        return result;
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}
