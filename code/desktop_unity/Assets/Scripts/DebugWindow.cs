using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 调试子窗口 — 独立浮动面板
///
/// 功能：
///   1. 📊 实时参数日志：显示所有跟踪参数的当前值 + 区间
///   2. 🎮 参数调节：拖拽滑块实时调参（使用模型实际区间）
///   3. 独立拖动、可折叠展开
///
/// 打开方式：ContextMenu 设置标签 → "🔧 调试面板" 按钮
/// </summary>
public class DebugWindow : MonoBehaviour
{
    private Live2DRenderer _renderer;

    // 窗口状态
    private bool _isOpen = false;
    private Rect _windowRect = new Rect(30, 30, 340, 520);
    private bool _isDragging = false;
    private Vector2 _dragOffset;

    // 滚动
    private Vector2 _scrollPos = Vector2.zero;
    private Vector2 _logScrollPos = Vector2.zero;

    // 折叠
    private bool _showLog = true;
    private bool _showSliders = true;

    // 偏移值缓存（参数名 → 当前偏移量）
    private Dictionary<string, float> _offsetValues = new Dictionary<string, float>();

    // 需要显示的参数列表（覆盖手部全部参数）
    private string[] _trackedParams = new string[]
    {
        // — 左手（Group8 "手臂R1/R2" 实际是左手） —
        "Param33", "Param31", "Param32",
        "Param38", "Param39",
        // — 右手基础（Group17 "右手 基础"） —
        "Param94", "Param97",
        "Param93", "Param118", "Param99",
        "Param102", "Param103", "Param105", "Param106", "Param107",
        // — 右手透视/图层（Group17） —
        "Param95", "Param117", "Param98", "Param100", "Param116", "Param120",
        "Param108", "Param119",
        // — 右手L组（Group8 "手臂L1/L2/L3"） —
        "Param34", "Param36", "Param37",
        // — 手指模式（Group16 "右手 指"） —
        "Param92", "Param110",
        "Param111", "Param112", "Param113", "Param114", "Param115",
        // — 法阵特效 —
        "Param132", "Param154", "Param133", "Param136", "Param134", "Param135",
        "Param157", "Param156",
    };

    // ===== 样式 =====
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _valueLabelStyle;
    private GUIStyle _closeButtonStyle;
    private GUIStyle _logStyle;
    private Texture2D _bgTexture;
    private bool _stylesInitialized = false;

    // ===== 公开接口 =====

    public bool IsOpen => _isOpen;

    public void Toggle()
    {
        _isOpen = !_isOpen;
        if (_isOpen)
        {
            // 打开时立即读取参数区间
            RefreshSliderRanges();
        }
        // 关闭时不自动清除偏移，用户可手动点"重置全部"
    }

    public void Open()
    {
        if (!_isOpen) Toggle();
    }

    public void Close()
    {
        if (_isOpen) Toggle();
    }

    void Start()
    {
        _renderer = GetComponent<Live2DRenderer>();
        if (_renderer == null)
            _renderer = FindObjectOfType<Live2DRenderer>();
    }

    void OnGUI()
    {
        if (!_isOpen) return;
        InitStyles();

        // 拖动
        HandleDragEvent(Event.current);

        // 背景
        GUI.Box(_windowRect, GUIContent.none, new GUIStyle { normal = { background = _bgTexture } });

        GUILayout.BeginArea(_windowRect);

        // ===== 标题栏 =====
        GUILayout.BeginHorizontal();
        GUILayout.Label("🔧 调试面板", _titleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕", _closeButtonStyle, GUILayout.Width(24), GUILayout.Height(20)))
            Toggle();
        GUILayout.EndHorizontal();

        GUILayout.Space(2);

        // ===== 偏移总开关 =====
        GUILayout.BeginHorizontal();
        bool wasEnabled = _renderer != null && _renderer.debugOffsetEnabled;
        bool nowEnabled = GUILayout.Toggle(wasEnabled, " 启用偏移（在动画上叠加）", GUILayout.Width(200));
        if (_renderer != null && nowEnabled != wasEnabled)
        {
            _renderer.debugOffsetEnabled = nowEnabled;
            if (!nowEnabled)
            {
                _renderer.debugOffsets.Clear();
                _offsetValues.Clear();
            }
        }
        if (_renderer != null && GUILayout.Button("全部归零", GUILayout.Width(70), GUILayout.Height(20)))
        {
            ClearAllOffsets();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // ===== ① 实时参数日志 =====
        _showLog = GUILayout.Toggle(_showLog, "📊 实时参数日志", _sectionStyle);
        if (_showLog)
        {
            string logText = BuildParamLog();
            float logHeight = Mathf.Min(30 + _trackedParams.Length * 14f, 180f);
            GUILayout.BeginVertical(GUILayout.Height(logHeight));
            _logScrollPos = GUILayout.BeginScrollView(_logScrollPos);
            GUILayout.TextArea(logText, _logStyle, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        GUILayout.Space(4);

        // ===== ② 参数调节滑块 =====
        _showSliders = GUILayout.Toggle(_showSliders, "🎮 参数调节（拖拽滑块）", _sectionStyle);
        if (_showSliders)
        {
            float sliderAreaHeight = Mathf.Min(30 + _trackedParams.Length * 22f, 280f);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(sliderAreaHeight));

            foreach (var paramName in _trackedParams)
            {
                if (_renderer == null) break;

                float min = _renderer.GetParameterMin(paramName);
                float max = _renderer.GetParameterMax(paramName);
                float range = max - min;
                float offsetLimit = range * 0.5f;  // 偏移范围对称：±区间一半
                if (offsetLimit < 0.01f) offsetLimit = 0.5f;

                float cur = _renderer.GetParameterValue(paramName);

                // 偏移值初始化
                if (!_offsetValues.ContainsKey(paramName))
                    _offsetValues[paramName] = 0f;

                GUILayout.BeginHorizontal();

                // 参数名
                GUILayout.Label(paramName, _labelStyle, GUILayout.Width(70));

                // 拖拽滑块（偏移量，范围对称）
                float offset = _offsetValues[paramName];
                float newOffset = GUILayout.HorizontalSlider(offset, -offsetLimit, offsetLimit, GUILayout.ExpandWidth(true));

                // 偏移变化 → 写偏移表
                if (newOffset != offset)
                {
                    _offsetValues[paramName] = newOffset;
                    if (_renderer != null)
                    {
                        _renderer.debugOffsetEnabled = true;
                        _renderer.debugOffsets[paramName] = newOffset;
                    }
                }

                // 显示：动画值 + 偏移 = 最终值
                float finalVal = Mathf.Clamp(cur + offset, min, max);
                string displayStr = $"{cur:F2}{(offset >= 0 ? "+" : "")}{offset:F2}={finalVal:F2}";
                GUILayout.Label(displayStr, _valueLabelStyle, GUILayout.Width(110));

                GUILayout.EndHorizontal();
                GUILayout.Space(1);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(2);

            // 清除偏移按钮
            if (GUILayout.Button("🗑 清除所有偏移（恢复动画控制）", GUILayout.Height(22)))
            {
                ClearAllOffsets();
            }
        }

        GUILayout.EndArea();
    }

    // ===== 内部方法 =====

    private void RefreshSliderRanges()
    {
        // 初始化所有参数的偏移量为 0
        foreach (var name in _trackedParams)
        {
            _offsetValues[name] = 0f;
        }
    }

    private void ClearAllOffsets()
    {
        if (_renderer != null)
        {
            _renderer.debugOffsets.Clear();
            _renderer.debugOffsetEnabled = false;
        }
        _offsetValues.Clear();
    }

    /// <summary>构建参数日志文本</summary>
    private string BuildParamLog()
    {
        if (_renderer == null) return "Live2DRenderer 未初始化";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"⏱ {System.DateTime.Now:HH:mm:ss}  |  偏移: {(_renderer.debugOffsetEnabled ? "启用" : "禁用")}  |  {_renderer.debugOffsets.Count} 个偏移中");
        sb.AppendLine("─".PadRight(44, '─'));

        foreach (var name in _trackedParams)
        {
            float val = _renderer.GetParameterValue(name);  // 最终值（动画+偏移后的）
            float min = _renderer.GetParameterMin(name);
            float max = _renderer.GetParameterMax(name);
            bool hasOffset = _renderer.debugOffsets.ContainsKey(name);
            float offsetVal = hasOffset ? _renderer.debugOffsets[name] : 0f;
            float animVal = hasOffset ? val - offsetVal : val;  // 反推纯动画值
            // 但由于 clamp，反推可能不准，读取 _offsetValues 作为缓存
            if (_offsetValues.TryGetValue(name, out float cachedOffset))
                offsetVal = cachedOffset;
            string mark = hasOffset ? " ★" : "";
            if (hasOffset)
                sb.AppendLine($"  {name,-14}  动画{animVal,+7:F3} 偏移{(offsetVal >= 0 ? "+" : "")}{offsetVal,+7:F3} = {val,+7:F3}{mark}");
            else
                sb.AppendLine($"  {name,-14}  = {val,+7:F3}  [{min,+6:F1}, {max,+6:F1}]{mark}");
        }

        // 额外显示不在跟踪列表但有偏移的参数
        foreach (var kv in _renderer.debugOffsets)
        {
            if (System.Array.IndexOf(_trackedParams, kv.Key) < 0)
            {
                float val = _renderer.GetParameterValue(kv.Key);
                sb.AppendLine($"  {kv.Key,-14} = {val,+7:F3}  [偏移额外]");
            }
        }

        return sb.ToString();
    }

    /// <summary>处理窗口拖动</summary>
    private void HandleDragEvent(Event e)
    {
        Rect titleBarRect = new Rect(_windowRect.x, _windowRect.y, _windowRect.width, 24);

        if (e.type == EventType.MouseDown && titleBarRect.Contains(e.mousePosition))
        {
            _isDragging = true;
            _dragOffset = e.mousePosition - _windowRect.position;
            e.Use();
        }

        if (e.type == EventType.MouseUp)
            _isDragging = false;

        if (_isDragging && e.type == EventType.MouseDrag)
        {
            _windowRect.position = e.mousePosition - _dragOffset;
        }
    }

    // ===== 样式初始化 =====

    private void InitStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        _bgTexture = MakeTex(1, 1, new Color(0.10f, 0.10f, 0.13f, 0.95f));

        _titleStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.8f, 0.9f, 1f) },
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(6, 0, 3, 2)
        };

        _sectionStyle = new GUIStyle(GUI.skin.toggle)
        {
            normal = { textColor = new Color(0.7f, 0.8f, 0.9f) },
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(4, 0, 2, 2)
        };

        _labelStyle = new GUIStyle
        {
            normal = { textColor = Color.white },
            fontSize = 10,
            padding = new RectOffset(2, 2, 2, 2),
            alignment = TextAnchor.MiddleLeft
        };

        _valueLabelStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.6f, 0.9f, 0.6f) },
            fontSize = 9,
            padding = new RectOffset(2, 2, 2, 2),
            alignment = TextAnchor.MiddleRight
        };

        _closeButtonStyle = new GUIStyle(GUI.skin.button)
        {
            normal = { textColor = new Color(1f, 0.4f, 0.4f) },
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _logStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.5f, 0.85f, 0.5f) },
            fontSize = 10,
            fontStyle = FontStyle.Normal,
            padding = new RectOffset(4, 0, 2, 2),
            wordWrap = false
        };
    }

    private Texture2D MakeTex(int w, int h, Color c)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = c;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
}
