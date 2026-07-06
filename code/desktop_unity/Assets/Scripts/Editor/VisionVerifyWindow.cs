using Live2D.Cubism.Core;
using Live2DFramework.ActionAgent;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 视觉具身验证窗口 — 有完整 UI 的 EditorWindow
///
/// 类似「自律训练」窗口的交互体验：
///   → 进入 Play Mode 后点击菜单
///   → 窗口自动检测 Mapper 就绪状态
///   → 点击「开始验证」运行 10 个动作测试
///   → 每完成一个测试实时更新表格
///   → 完成后显示最终报告和弹窗
/// </summary>
public class VisionVerifyWindow : EditorWindow
{
    // ================================================================
    //  Menu entry
    // ================================================================

    [MenuItem("Tools/Live2D/🧿 视觉具身验证 (vis_verify)")]
    private static void OpenWindow()
    {
        var window = GetWindow<VisionVerifyWindow>("🧿 视觉验证");
        window.minSize = new Vector2(700, 450);
        window.Show();
    }

    /// <summary>供 SelfTrainingManager 调用的入口（防止菜单项冲突）</summary>
    public static void OpenFromSelfTraining()
    {
        OpenWindow();
    }

    // ================================================================
    //  State
    // ================================================================

    private enum WindowState
    {
        Checking,       // 检查 Play Mode / Renderer
        Ready,          // 就绪，等用户点击
        Running,        // 验证进行中
        Done,           // 验证完成
    }

    private WindowState _state = WindowState.Checking;
    private string _statusText = "检查运行环境...";

    // 场景引用
    private Live2DRenderer _renderer;
    private CubismModel _model;

    // 进度跟踪
    private int _totalTests = 10;
    private int _completedTests = 0;

    // UI 状态
    private Vector2 _tableScrollPos;
    private Vector2 _logScrollPos;
    private StringBuilder _logBuilder = new StringBuilder();
    private bool _updateAttached = false;

    // 上次轮询 <summary> 避免重复日志
    private int _lastPolledResultCount = 0;

    // ================================================================
    //  Lifecycle
    // ================================================================

    private void OnEnable()
    {
        if (!_updateAttached)
        {
            EditorApplication.update += OnEditorPoll;
            _updateAttached = true;
        }

        // 进入窗口时检查环境
        RefreshEnvironment();
    }

    private void OnDisable()
    {
        if (_updateAttached)
        {
            EditorApplication.update -= OnEditorPoll;
            _updateAttached = false;
        }
    }

    /// <summary>EditorApplication.update 轮询 — 跟踪协程进度</summary>
    private void OnEditorPoll()
    {
        if (_state == WindowState.Running)
        {
            // 轮询 VisionMotionVerifier 的中间结果
            var results = VisionMotionVerifier.InProgressResults;
            if (results != null)
            {
                _completedTests = results.Count;
                if (_completedTests != _lastPolledResultCount)
                {
                    _lastPolledResultCount = _completedTests;
                    AppendLog($"► 完成 ({_completedTests}/{_totalTests})");
                }
            }

            // 检测是否完成（LastReport 非空）
            if (!string.IsNullOrEmpty(VisionMotionVerifier.LastReport))
            {
                _state = WindowState.Done;
                _statusText = $"✅ 验证完成！{_completedTests}/{_totalTests} 个动作测试完毕";
                AppendLog("══════ 验证完成 ══════");
                Repaint();
            }
        }

        // 周期性重绘以更新表格
        if (_state == WindowState.Running || _state == WindowState.Checking)
        {
            Repaint();
        }
    }

    // ================================================================
    //  Environment
    // ================================================================

    private void RefreshEnvironment()
    {
        _state = WindowState.Checking;
        _statusText = "检查运行环境...";
        _renderer = null;
        _model = null;

        if (!EditorApplication.isPlaying)
        {
            _state = WindowState.Ready;
            _statusText = "❌ 未处于 Play 模式 — 请先点击播放按钮进入运行模式";
            return;
        }

        var r = UnityEngine.Object.FindObjectOfType<Live2DRenderer>();
        if (r == null)
        {
            _state = WindowState.Ready;
            _statusText = "❌ 场景中未找到 Live2DRenderer — 请先进入 Play 模式";
            return;
        }

        _renderer = r;
        _model = r.CubismModel;

        if (r.Mapper == null || !r.Mapper.IsLoaded)
        {
            _state = WindowState.Checking;
            _statusText = "⏳ 等待 Mapper 加载...";
            return;
        }

        _state = WindowState.Ready;
        _statusText = $"✅ 就绪 — 模型「{_model?.name ?? "N/A"}」已加载，Mapper 就绪";
    }

    // ================================================================
    //  Actions
    // ================================================================

    private void StartVerification()
    {
        if (_renderer == null)
        {
            RefreshEnvironment();
            if (_renderer == null) return;
        }

        // 重置状态
        VisionMotionVerifier.ClearInProgress();
        _logBuilder.Clear();
        _completedTests = 0;
        _lastPolledResultCount = 0;
        _totalTests = 10;

        AppendLog("══════ 视觉具身验证开始 ══════");
        AppendLog("使用 GLM-4V 作为考官评审 10 个动作");
        AppendLog($"> 模型: {ChatConfig.GlmVisionModel}");
        AppendLog($"> 耗时: 每个动作约 5-15 秒");

        if (_renderer.Mapper == null || !_renderer.Mapper.IsLoaded)
        {
            _statusText = "⏳ Mapper 尚未就绪，等待 15 秒...";
            _renderer.StartCoroutine(WaitMapperAndRun(_renderer));
        }
        else
        {
            _statusText = $"▶ 正在验证 (0/{_totalTests})...";
            _state = WindowState.Running;
            _renderer.StartCoroutine(VisionMotionVerifier.RunVisionVerification(
                _renderer.Mapper, _renderer.CubismModel, _renderer,
                onProgress: (idx, total, name) =>
                {
                    AppendLog($"► ({idx}/{total}) {name}");
                    _statusText = $"▶ 正在验证 ({idx}/{total}) — {name}";
                },
                onResult: report =>
                {
                    AppendLog("══════ 报告已生成 ══════");
                    _statusText = "✅ 验证完成！点击「查看报告」保存";
                    _state = WindowState.Done;
                }
            ));
        }

        Repaint();
    }

    private System.Collections.IEnumerator WaitMapperAndRun(Live2DRenderer r)
    {
        float timeout = 15f;
        while ((r.Mapper == null || !r.Mapper.IsLoaded) && timeout > 0f)
        {
            _statusText = $"⏳ 等待 Mapper 就绪... ({(int)((15f - timeout) * 100 / 15f)}%)";
            timeout -= Time.deltaTime;
            Repaint();
            yield return null;
        }

        if (r.Mapper == null || !r.Mapper.IsLoaded)
        {
            _statusText = "❌ Mapper 等待超时（15 秒）";
            _state = WindowState.Ready;
            AppendLog("❌ 等待超时，Mapper 未能就绪");
            Repaint();
            yield break;
        }

        AppendLog("✅ Mapper 已就绪");
        _statusText = $"▶ 正在验证 (0/{_totalTests})...";
        _state = WindowState.Running;

        r.StartCoroutine(VisionMotionVerifier.RunVisionVerification(
            r.Mapper, r.CubismModel, r,
            onProgress: (idx, total, name) =>
            {
                AppendLog($"► ({idx}/{total}) {name}");
                _statusText = $"▶ 正在验证 ({idx}/{total}) — {name}";
            },
            onResult: report =>
            {
                AppendLog("══════ 报告已生成 ══════");
                _statusText = "✅ 验证完成！";
                _state = WindowState.Done;
            }
        ));
        Repaint();
    }

    private void SaveReport()
    {
        string report = VisionMotionVerifier.LastReport;
        if (string.IsNullOrEmpty(report))
        {
            EditorUtility.DisplayDialog("保存报告", "暂无报告可保存。请先运行视觉验证。", "确定");
            return;
        }

        string path = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(),
            "vis_verify_report.md");
        System.IO.File.WriteAllText(path, report);
        AppendLog($"📄 报告已保存: {path}");

        // 弹窗显示摘要
        var summary = VisionMotionVerifier.GetCompactSummary();
        EditorUtility.DisplayDialog("🧿 视觉具身验证报告", summary, "确定");

        _statusText = $"📄 报告已保存到 {path}";
    }

    private void AppendLog(string text)
    {
        _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {text}");
        Repaint();
    }

    // ================================================================
    //  UI
    // ================================================================

    private void OnGUI()
    {
        EditorGUILayout.Space(8);

        // ─── 标题 ────────────────────────────────────────────────
        var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
        GUILayout.Label("🧿 视觉具身验证 — GLM-4V 考官", titleStyle);
        EditorGUILayout.LabelField(
            "DeepSeek 做动作 → 截图 → GLM-4V 评审 → 量化报告",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(6);

        // ─── 状态提示 ────────────────────────────────────────────
        var msgType = GetMessageType(_statusText);
        EditorGUILayout.HelpBox(_statusText, msgType);

        // ─── 控制区 ──────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();

        bool isRunning = (_state == WindowState.Running);
        bool isChecking = (_state == WindowState.Checking);

        GUI.enabled = !isRunning && !isChecking;
        if (GUILayout.Button(isChecking ? "" : "▶ 开始验证", GUILayout.Height(28)))
        {
            StartVerification();
        }
        GUI.enabled = isRunning;
        if (GUILayout.Button("⏹ 停止", GUILayout.Height(28)))
        {
            // 没法真正停止协程，但标记状态令用户知晓
            _statusText = "⏹ 停止请求已发送（当前测试完成后停止）";
        }
        GUI.enabled = !isRunning;

        if (GUILayout.Button("🔄 刷新环境", GUILayout.Height(28)))
        {
            RefreshEnvironment();
        }

        if (_state == WindowState.Done && !string.IsNullOrEmpty(VisionMotionVerifier.LastReport))
        {
            if (GUILayout.Button("📄 保存报告", GUILayout.Height(28)))
            {
                SaveReport();
            }
        }

        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // ─── 成绩表格 ────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("📊 成绩单", EditorStyles.boldLabel);

        var results = VisionMotionVerifier.InProgressResults;
        if (results != null && results.Count > 0)
        {
            _tableScrollPos = EditorGUILayout.BeginScrollView(_tableScrollPos,
                GUILayout.Height(160));

            // 表头
            EditorGUILayout.BeginHorizontal("box");
            GUILayout.Label("ID", GUILayout.Width(30));
            GUILayout.Label("动作", GUILayout.Width(70));
            GUILayout.Label("分数", GUILayout.Width(70));
            GUILayout.Label("LLM?", GUILayout.Width(45));
            GUILayout.Label("判断");
            EditorGUILayout.EndHorizontal();

            foreach (var r in results)
            {
                EditorGUILayout.BeginHorizontal();

                // ID
                GUILayout.Label(r.Id, GUILayout.Width(30));

                // 中文名
                GUILayout.Label(r.ChineseName, GUILayout.Width(70));

                // 分数（着色）
                string scoreStr;
                Color scoreColor;
                if (r.ErrorMessage != null)
                {
                    scoreStr = "❌";
                    scoreColor = Color.red;
                }
                else
                {
                    scoreStr = ScoreToStars(r.Score);
                    scoreColor = r.Score >= 4 ? Color.green : (r.Score >= 3 ? Color.yellow : Color.red);
                }
                var origColor = GUI.color;
                GUI.color = scoreColor;
                GUILayout.Label(scoreStr, GUILayout.Width(70));
                GUI.color = origColor;

                // LLM 生成标记
                GUILayout.Label(r.WasLLMGenerated ? "✅" : "⚠️", GUILayout.Width(45));

                // 判断（截取简述）
                string judgment = r.ErrorMessage ?? TruncateJudgment(r.GlmJudgment);
                GUILayout.Label(judgment, EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // 汇总行
            int passCount = 0, totalScore = 0;
            foreach (var r in results)
            {
                if (r.IsPass) passCount++;
                if (r.Score > 0) totalScore += r.Score;
            }
            float passRate = results.Count > 0 ? (float)passCount / results.Count * 100f : 0f;
            float avgScore = results.Count > 0 ? (float)totalScore / results.Count : 0f;
            EditorGUILayout.LabelField(
                $"通过: {passCount}/{results.Count} ({passRate:F1}%)  |  平均分: {avgScore:F1}/5.0",
                EditorStyles.miniBoldLabel);
        }
        else if (_state == WindowState.Running)
        {
            EditorGUILayout.HelpBox("⏳ 正在等待第一个动作结果...", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("尚未运行验证。点击「开始验证」运行 10 个动作的视觉测试。", MessageType.Info);
        }

        // ─── 日志面板 ────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("📋 执行日志", EditorStyles.boldLabel);

        _logScrollPos = EditorGUILayout.BeginScrollView(_logScrollPos,
            GUILayout.Height(100));
        string logText = _logBuilder.ToString();
        if (!string.IsNullOrEmpty(logText))
        {
            GUILayout.Label(logText, EditorStyles.wordWrappedMiniLabel);
        }
        else
        {
            GUILayout.Label("（暂无日志）", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndScrollView();

        // ─── 底部状态 ────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"完成: {_completedTests}/{_totalTests}",
            EditorStyles.miniLabel);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private MessageType GetMessageType(string text)
    {
        if (string.IsNullOrEmpty(text)) return MessageType.Info;
        if (text.Contains("❌") || text.Contains("错误") || text.Contains("失败") || text.Contains("超时"))
            return MessageType.Error;
        if (text.Contains("⚠️") || text.Contains("等待") || text.Contains("⏳"))
            return MessageType.Warning;
        if (text.Contains("✅") || text.Contains("完成") || text.Contains("就绪"))
            return MessageType.Info;
        return MessageType.Info;
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

    private static string TruncateJudgment(string text, int maxLen = 60)
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
}
