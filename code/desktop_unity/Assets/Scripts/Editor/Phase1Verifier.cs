using Live2D.Cubism.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 阶段一 集成验证器 — 一键验证所有阶段一模块
///
/// 使用方式：Tools → Live2D → 🔍 阶段一验证器
/// 前置条件：场景中需要有一个挂载了 CubismModel 的 GameObject 被选中
/// </summary>
public class Phase1Verifier : EditorWindow
{
    private CubismModel _model;
    private GameObject _modelObject;
    private Vector2 _scrollPos;
    private StringBuilder _log = new StringBuilder();
    private bool _hasRun = false;
    private int _errorCount = 0;

    [MenuItem("Tools/Live2D/🔍 阶段一验证器")]
    private static void OpenWindow()
    {
        var window = GetWindow<Phase1Verifier>("阶段一验证");
        window.minSize = new Vector2(700, 500);
        window.Show();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        _modelObject = (GameObject)EditorGUILayout.ObjectField("模型 GameObject", _modelObject, typeof(GameObject), true);
        if (GUILayout.Button("从选中获取", GUILayout.Width(80)))
        {
            if (Selection.activeGameObject != null)
            {
                _modelObject = Selection.activeGameObject;
                _model = _modelObject.GetComponent<CubismModel>();
            }
        }
        EditorGUILayout.EndHorizontal();

        GUI.enabled = _modelObject != null;

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("▶ 运行全部验证", GUILayout.Height(30)))
        {
            RunAllVerifications();
        }
        if (GUILayout.Button("清空日志", GUILayout.Height(30), GUILayout.Width(80)))
        {
            _log.Clear();
            _hasRun = false;
        }
        EditorGUILayout.EndHorizontal();

        GUI.enabled = true;
        EditorGUILayout.Space(10);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        string text = _log.ToString();
        if (!string.IsNullOrEmpty(text))
        {
            GUILayout.TextArea(text, GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndScrollView();
    }

    private void Log(string msg) { _log.AppendLine(msg); Debug.Log("[Phase1Verifier] " + msg); }
    private void LogError(string msg) { _errorCount++; _log.AppendLine("❌ " + msg); Debug.LogError("[Phase1Verifier] " + msg); }
    private void LogSuccess(string msg) { _log.AppendLine("✅ " + msg); }

    private void RunAllVerifications()
    {
        _log.Clear();
        _hasRun = true;
        _errorCount = 0;
        _model = _modelObject.GetComponent<CubismModel>();
        if (_model == null) { LogError("选中的 GameObject 没有 CubismModel 组件"); return; }

        Log("══════════ 阶段一 集成验证 ══════════");
        Log($"模型: {_modelObject.name}  时间: {DateTime.Now:HH:mm:ss}");
        Log("");

        // =========================================================
        // ① 编译检查
        // =========================================================
        LogSuccess("① 编译检查 — 通过 (窗口能打开说明无编译错误)");
        Log("");

        // =========================================================
        // ② ModelBodySchema — 数据结构
        // =========================================================
        Log("--- ② ModelBodySchema 数据结构 ---");
        try
        {
            var pd = new ParameterDef
            {
                semantic = "test_param",
                paramId = "ParamTest",
                bodyPart = "body",
                valueDomain = ValueDomain.Angle,
                axis = "X",
                min = -30,
                max = 30
            };
            if (pd.semantic == "test_param" && pd.bodyPart == "body" && pd.valueDomain == ValueDomain.Angle)
                LogSuccess("ParameterDef 创建 & 字段赋值正常");
            else
                LogError("ParameterDef 字段异常");

            var pg = new ParameterGroup { groupName = "test", displayName = "测试组" };
            pg.paramsList.Add("test_param");
            if (pg.paramsList.Count == 1)
                LogSuccess("ParameterGroup 创建正常");
            else
                LogError("ParameterGroup 异常");

            var sb = new SpecialBehavior
            {
                behaviorName = "safeguard",
                description = "眼睛保护",
                involvedParams = new List<string> { "Param63", "Param64" }
            };
            if (sb.behaviorName == "safeguard" && sb.involvedParams.Count == 2)
                LogSuccess("SpecialBehavior 创建正常");
            else
                LogError("SpecialBehavior 异常");

            var rel = new ParameterRelation
            {
                type = RelationType.Symmetrical,
                involvedSemantics = new List<string> { "eye_l_open", "eye_r_open" },
                confidence = 1f
            };
            if (rel.type == RelationType.Symmetrical && rel.involvedSemantics.Count == 2)
                LogSuccess("ParameterRelation 创建正常");
            else
                LogError("ParameterRelation 异常");
        }
        catch (Exception ex) { LogError($"ModelBodySchema 异常: {ex.Message}"); }
        Log("");

        // =========================================================
        // ③ Live2DModelAnalyzer — 运行时分析
        // =========================================================
        Log("--- ③ Live2DModelAnalyzer 运行时分析 ---");
        try
        {
            int total = _model.Parameters.Length;
            Log($"模型参数总数: {total}");

            // 调用 Analyze — 验证静态方法可用
            var result = Live2DModelAnalyzer.Analyze(_model);
            if (result != null)
            {
                Log($"分析结果: totalParams={result.totalParams}, autoMatched={result.autoMatched}, unmatched={result.unmatched}");
                Log($"allParams={result.allParams?.Count ?? 0}, unmappedParamIds={result.unmappedParamIds?.Count ?? 0}");
                LogSuccess("Live2DModelAnalyzer.Analyze() 正常");
            }
            else
            {
                LogError("Live2DModelAnalyzer.Analyze() 返回 null");
            }

            // 检查 GenerateMappingTemplate
            string template = Live2DModelAnalyzer.GenerateMappingTemplate(_model);
            if (!string.IsNullOrEmpty(template))
            {
                Log($"GenerateMappingTemplate: {template.Length} 字符");
                LogSuccess("GenerateMappingTemplate() 正常");
            }
            else
            {
                LogError("GenerateMappingTemplate() 返回空");
            }

            // 检查 cdi3.json
            string cdiPath = Path.Combine(Application.streamingAssetsPath, "fuxuan", "fuxuan.model3.json");
            if (File.Exists(cdiPath))
                LogSuccess($"CDI3 JSON 存在于: StreamingAssets/fuxuan/");
            else
                Log("CDI3 JSON 不在 StreamingAssets 路径 (可能内嵌在 Resources 或 Prefab 中)");
        }
        catch (Exception ex) { LogError($"Live2DModelAnalyzer 异常: {ex.Message}"); }
        Log("");

        // =========================================================
        // ④ fuxuan_map.json 映射文件
        // =========================================================
        Log("--- ④ fuxuan_map.json 映射文件 ---");
        try
        {
            // Resources 副本
            TextAsset mapAsset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
            if (mapAsset != null)
            {
                string json = mapAsset.text;
                int c = CountJsonEntries(json);
                Log($"Resources 副本: {json.Length} 字符, {c} 条目");
                LogSuccess("映射文件可从 Resources 加载");
            }
            else
                LogError("Resources 中未找到 fuxuan_map");

            // Scripts 路径副本
            string scriptPath = Path.Combine(Application.dataPath, "Scripts", "Live2DFramework", "ParamMaps", "fuxuan_map.json");
            if (File.Exists(scriptPath))
            {
                string text = File.ReadAllText(scriptPath);
                int c = CountJsonEntries(text);
                Log($"Scripts 路径副本: {text.Length} 字符, {c} 条目");
            }
            else
                LogError("Scripts 路径映射文件不存在");
        }
        catch (Exception ex) { LogError($"映射文件异常: {ex.Message}"); }
        Log("");

        // =========================================================
        // ⑤ ParameterRelationDetector — 关联检测
        // =========================================================
        Log("--- ⑤ ParameterRelationDetector 关联检测 ---");
        try
        {
            // 准备参数列表
            var paramDefs = new List<ParameterDef>();
            foreach (var p in _model.Parameters)
            {
                paramDefs.Add(new ParameterDef
                {
                    paramId = p.Id,
                    semantic = p.Id,
                    cdiName = "",
                    bodyPart = "",
                    min = p.MinimumValue,
                    max = p.MaximumValue
                });
            }

            // 调用静态检测
            var relations = ParameterRelationDetector.DetectRelations(paramDefs);
            Log($"DetectRelations: 发现 {relations.Count} 个关联");

            string summary = ParameterRelationDetector.GetRelationsSummary(relations);
            if (!string.IsNullOrEmpty(summary))
            {
                string[] lines = summary.Split('\n');
                Log($"GetRelationsSummary: {lines.Length} 行");
                LogSuccess("ParameterRelationDetector 正常");
            }
        }
        catch (Exception ex) { LogError($"ParameterRelationDetector 异常: {ex.Message}"); }
        Log("");

        // =========================================================
        // ⑥ Live2DParameterMapper — 语义映射器
        // =========================================================
        Log("--- ⑥ Live2DParameterMapper 语义映射器 ---");
        try
        {
            var mapper = new Live2DParameterMapper(_model);
            TextAsset mapAsset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
            if (mapAsset != null)
            {
                mapper.LoadMappingFromJson(mapAsset.text);
                Log($"模型名: {mapper.ModelName}");
                Log($"语义→ID: {mapper.SemanticToId.Count} 条目");
                Log($"ID→语义: {mapper.IdToSemantic.Count} 条目");

                // 验证关键映射
                string[] tests = { "body_angle_x", "eye_l_open", "mouth_form", "breath", "arm_right_upper" };
                int found = 0;
                foreach (var s in tests)
                {
                    if (mapper.SemanticToId.ContainsKey(s))
                    {
                        string pid = mapper.SemanticToId[s];
                        bool reverseOk = mapper.IdToSemantic.ContainsKey(pid) && mapper.IdToSemantic[pid] == s;
                        Log($"  {s} → {pid} {(reverseOk ? "(双向✓)" : "(反向缺失)")}");
                        found++;
                    }
                }
                LogSuccess($"关键映射: {found}/{tests.Length} 命中");
            }

            // Set/Get 测试
            mapper.Set("breath", 0.5f);
            float v = mapper.Get("breath");
            if (Mathf.Abs(v - 0.5f) < 0.01f) LogSuccess($"mapper.Set/Get: breath=0.5 ✓");
            else LogError($"mapper Set/Get 异常: 期望0.5, 得到{v}");
        }
        catch (Exception ex) { LogError($"Live2DParameterMapper 异常: {ex.Message}"); }
        Log("");

        // =========================================================
        // 总结
        // =========================================================
        if (_errorCount == 0)
        {
            LogSuccess("");
            LogSuccess("═══════════════════════════════════");
            LogSuccess("  🎉 阶段一全部验证通过！");
            LogSuccess("═══════════════════════════════════");
        }
        else
        {
            LogError($"发现 {_errorCount} 个问题需要修复");
        }
        Repaint();
    }

    private int CountJsonEntries(string json)
    {
        int n = 0, i = 0;
        while ((i = json.IndexOf("\"p\":", i)) != -1) { n++; i++; }
        return n;
    }
}
