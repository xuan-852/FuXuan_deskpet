using UnityEngine;

/// <summary>
/// 右侧多功能面板 — 符玄·太卜司主题
///
/// 设计语言：
/// - 紫灰渐变背景（太卜司星象风格）
/// - 顶部 "太卜司" 标识 + 实时时间
/// - 紫光按钮 + 装饰细线分割
/// - 底部法阵纹样点缀
///
/// 功能：
/// - 屏幕右边缘窄条触发，按 ~ 键或鼠标划过展开
/// - 滑出面板含工具按钮 + 底部输入框
/// - 跨 BallPanel 标签页（设置/便签/报告）保持可见
///
/// 状态切换：
///   ~ 键 → 切换展开/收起（手动模式，保持状态）
///   鼠标移到右边缘 → 自动展开（临时模式，移出自动收回）
///   展开时面板宽度 ~220px，收起时 ~8px 窄条
/// </summary>
public class RightPanel : MonoBehaviour
{
    // ==================== 配置参数 ====================
    [Header("面板尺寸")]
    public float panelWidthCollapsed = 8f;     // 收起时宽度 (px)
    public float panelWidthExpanded = 220f;    // 展开时宽度 (px)
    public float inputBarHeight = 48f;         // 底部输入框高度
    public float slideSpeed = 10f;             // 滑入滑出动画速度

    [Header("热键")]
    public KeyCode toggleKey = KeyCode.BackQuote;  // ~ 键切换

    // ==================== 工具按钮定义（不用 emoji，用中文单字） ====================
    private readonly (string icon, string label, BallPanel.PanelType? panelType)[] _tools = new (string, string, BallPanel.PanelType?)[]
    {
        ("聊", "聊天", null),                          // 聚焦输入框
        ("设", "设置", BallPanel.PanelType.Settings),
        ("签", "便签", BallPanel.PanelType.Reminders),
        ("告", "报告", BallPanel.PanelType.Report),
    };

    // ==================== 运行时状态 ====================
    private float _animWidth;          // 当前动画宽度
    private bool _isExpanded;          // 当前是否展开
    private bool _wantsExpand;         // 目标展开状态
    private float _mouseLeaveTimer;    // 鼠标离开计时器
    private const float AUTO_HIDE_DELAY = 1.0f; // 自动隐藏延迟

    private ChatManager _chat;
    private BallPanel _ballPanel;
    private string _inputText = "";
    private bool _inputFocused = false; // 是否聚焦到输入框

    // ==================== 鼠标跟踪 ====================
    private int _hotkeyFrame = 0;       // 防止同一帧内 toggle 多次

    // ==================== 样式 ====================
    private GUIStyle _panelStyle;
    private GUIStyle _inputStyle;
    private GUIStyle _toolBtnStyle;
    private GUIStyle _toolBtnHoverStyle;
    private GUIStyle _separatorStyle;
    private GUIStyle _placeholderStyle;
    private GUIStyle _hintStyle;         // 按钮悬停提示（极淡）
    private GUIStyle _topBarStyle;       // 顶栏文字
    private GUIStyle _timeStyle;         // 时间文字
    private GUIStyle _brandStyle;        // 太卜司标识
    private Texture2D _bgTex;            // 面板背景（渐变替代）
    private Texture2D _inputBgTex;       // 输入框背景
    private Texture2D _inputHoverBgTex;  // 输入框悬停背景（提亮紫）
    private Texture2D _separatorTex;     // 分隔线
    private Texture2D _whiteTex;         // 白图
    private Texture2D _toolTex;          // 按钮正常背景
    private Texture2D _toolHoverTex;     // 按钮悬停背景
    private Texture2D _glowTex;          // 按钮发光光晕
    private Texture2D _accentLineTex;    // 装饰细线（紫）
    private Texture2D _ornamentTL;       // 左上云纹角饰
    private Texture2D _ornamentTR;       // 右上云纹角饰
    private Texture2D _ornamentBR;       // 右下云纹角饰
    private Texture2D _ornamentBL;       // 左下云纹角饰
    private bool _stylesReady = false;

    // ==================== 装饰状态 ====================
    private string _timeDisplay = "";
    private float _timeRefreshTimer = 0f;

    // ==================== 触发器窄条 ====================
    /// <summary>右侧触发器区域（鼠标划过展开）</summary>
    private Rect TriggerRect => new Rect(Screen.width - panelWidthCollapsed - 4f, 0, panelWidthCollapsed + 8f, Screen.height);

    /// <summary>面板完整区域</summary>
    private Rect PanelRect => new Rect(Screen.width - _animWidth, 0, _animWidth, Screen.height);

    /// <summary>供 DragHandler 判断鼠标是否在面板交互区域内（用于点击穿透控制）</summary>
    public bool IsPointInInteractiveArea(Vector2 guiMousePos)
    {
        if (!_isExpanded && _animWidth <= panelWidthCollapsed + 1f) return false;
        return PanelRect.Contains(guiMousePos);
    }

    // ==================== 生命周期 ====================

    void Start()
    {
        RefreshRefs();
        _animWidth = panelWidthCollapsed;
        Debug.Log($"[RightPanel] 已就绪，屏幕={Screen.width}x{Screen.height}");
    }

    private void RefreshRefs()
    {
        if (_chat == null)
        {
            _chat = GetComponent<ChatManager>();
            if (_chat == null) _chat = FindObjectOfType<ChatManager>();
        }
        if (_ballPanel == null)
        {
            _ballPanel = GetComponent<BallPanel>();
            if (_ballPanel == null) _ballPanel = FindObjectOfType<BallPanel>();
        }
    }

    void Update()
    {
        RefreshRefs();

        // 1. 热键切换 — 兼容中文键盘（`·~` 键和 F2 均可）
        bool togglePressed = Input.GetKeyDown(toggleKey)
            || Input.GetKeyDown(KeyCode.F2)
            || Input.GetKeyDown(KeyCode.Backslash);
        if (togglePressed && Time.frameCount != _hotkeyFrame)
        {
            _hotkeyFrame = Time.frameCount;
            _wantsExpand = !_wantsExpand;
            if (_wantsExpand) _inputFocused = true; // 展开后自动聚焦输入框
        }

        // 2. 鼠标在右边缘触发（仅收起时）
        if (!_wantsExpand)
        {
            Vector2 mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y; // 转 GUI 坐标
            if (TriggerRect.Contains(mousePos))
            {
                _wantsExpand = true;
                _mouseLeaveTimer = 0f;
            }
        }

        // 3. 鼠标离开面板 → 自动收回
        if (_wantsExpand && _isExpanded)
        {
            Vector2 mp = Input.mousePosition;
            mp.y = Screen.height - mp.y;
            float pw = _animWidth;
            float px = Screen.width - pw;
            bool overPanel = new Rect(px, 0, pw, Screen.height).Contains(mp);

            if (overPanel)
            {
                _mouseLeaveTimer = 0f;
            }
            else
            {
                _mouseLeaveTimer += Time.deltaTime;
                if (_mouseLeaveTimer > AUTO_HIDE_DELAY)
                {
                    if (Time.frameCount - _hotkeyFrame > 60)
                    {
                        _wantsExpand = false;
                    }
                }
            }
        }

        // 4. 动画插值
        float target = _wantsExpand ? panelWidthExpanded : panelWidthCollapsed;
        _animWidth = Mathf.Lerp(_animWidth, target, Time.deltaTime * slideSpeed);
        if (Mathf.Abs(_animWidth - target) < 0.5f)
            _animWidth = target;

        _isExpanded = _animWidth > panelWidthCollapsed + 2f;
    }

    void OnGUI()
    {
        InitStyles();
        RefreshRefs();

        if (!_isExpanded && _animWidth <= panelWidthCollapsed + 1f) return;

        float pw = _animWidth;              // 面板当前宽度
        float ph = Screen.height;           // 面板高度 = 全屏
        float px = Screen.width - pw;       // 面板 X
        float py = 0;
        Vector2 mp = Event.current.mousePosition; // GUI 坐标

        // ——— 面板背景 ———
        Rect bgRect = new Rect(px, py, pw, ph);
        GUI.Box(bgRect, GUIContent.none, _panelStyle);

        // ═══════════════════════════════════════
        //  顶栏 — 太卜司标识 + 时间
        // ═══════════════════════════════════════
        float topDecoY = 8f;
        float topDecoH = 34f;

        // 左上云纹角饰
        if (_ornamentTL != null)
        {
            GUI.color = new Color(0.55f, 0.40f, 0.85f, 0.50f);
            GUI.DrawTexture(new Rect(px + 2, py + 2, 28f, 28f), _ornamentTL);
        }
        // 右上云纹角饰
        if (_ornamentTR != null)
        {
            GUI.color = new Color(0.55f, 0.40f, 0.85f, 0.50f);
            GUI.DrawTexture(new Rect(px + pw - 30, py + 2, 28f, 28f), _ornamentTR);
        }

        // "太卜司" 标识
        Rect brandRect = new Rect(px, topDecoY - 2f, pw, 20f);
        GUI.color = Color.white;
        GUI.Label(brandRect, "太 卜 司", _brandStyle);

        // ——— 更新时钟 ———
        _timeRefreshTimer += Time.deltaTime;
        if (_timeRefreshTimer > 1f || string.IsNullOrEmpty(_timeDisplay))
        {
            _timeRefreshTimer = 0f;
            _timeDisplay = System.DateTime.Now.ToString("HH:mm");
        }

        // 时间显示（金色）
        Rect timeRect = new Rect(px, topDecoY + 16f, pw, 18f);
        GUI.Label(timeRect, _timeDisplay, _timeStyle);

        // ——— 紫光分隔线 ———
        float sep1Y = topDecoY + topDecoH;
        GUI.Box(new Rect(px + 12f, sep1Y, pw - 24f, 1f), GUIContent.none, _separatorStyle);
        // 细紫线
        GUI.Box(new Rect(px + 30f, sep1Y + 2f, pw - 60f, 1f), GUIContent.none, new GUIStyle { normal = { background = MakeTex(1, 1, new Color(0.55f, 0.40f, 0.85f, 0.3f)) } });

        // ═══════════════════════════════════════
        //  工具按钮
        // ═══════════════════════════════════════
        float btnStartY = sep1Y + 10f;
        float btnSize = 34f;
        float btnGap = 6f;
        float btnX = px + (pw - btnSize) / 2f;

        int localHoveredToolIndex = -1;
        for (int i = 0; i < _tools.Length; i++)
        {
            float by = btnStartY + i * (btnSize + btnGap);
            Rect btnRect = new Rect(btnX, by, btnSize, btnSize);
            bool hover = bgRect.Contains(mp) && btnRect.Contains(mp);
            if (hover) localHoveredToolIndex = i;

            // 悬停光晕
            if (hover)
            {
                Rect glowRect = new Rect(btnX - 7f, by - 7f, btnSize + 14f, btnSize + 14f);
                GUI.DrawTexture(glowRect, _glowTex);
            }

            GUIStyle style = hover ? _toolBtnHoverStyle : _toolBtnStyle;
            if (GUI.Button(btnRect, new GUIContent(_tools[i].icon, _tools[i].label), style))
            {
                var tool = _tools[i];
                if (tool.panelType.HasValue && _ballPanel != null)
                {
                    Vector2 panelPos = new Vector2(px - 440f, 40f);
                    _ballPanel.ShowPanel(tool.panelType.Value, panelPos);
                }
                else if (tool.label == "聊天")
                {
                    _inputFocused = true;
                    GUI.FocusControl("rightPanelInput");
                }
            }
        }

        // ——— 按钮下方悬停提示 ———
        float hintAreaY = btnStartY + _tools.Length * (btnSize + btnGap) + 4f;
        if (localHoveredToolIndex >= 0)
        {
            Rect hintRect = new Rect(px + 8f, hintAreaY, pw - 16f, 18f);
            GUI.Label(hintRect, _tools[localHoveredToolIndex].label, _hintStyle);
        }

        // ═══════════════════════════════════════
        //  底部区域 — 角落云纹 + 输入框
        // ═══════════════════════════════════════

        // ——— 底部云纹角饰 ———
        float ornSize = 32f;
        float ornMargin = 4f;
        GUI.color = new Color(0.55f, 0.40f, 0.85f, 0.40f);
        if (_ornamentBL != null)
            GUI.DrawTexture(new Rect(px + ornMargin, ph - inputBarHeight - ornSize - 4f, ornSize, ornSize), _ornamentBL);
        if (_ornamentBR != null)
            GUI.DrawTexture(new Rect(px + pw - ornSize - ornMargin, ph - inputBarHeight - ornSize - 4f, ornSize, ornSize), _ornamentBR);

        // ——— 输入框 ———
        float inputAreaH = inputBarHeight;
        float inputAreaY = ph - inputAreaH - 8f;
        float inputAreaX = px + 6f;
        float inputAreaW = pw - 12f;

        Rect inputBgRect = new Rect(inputAreaX, inputAreaY, inputAreaW, inputAreaH);

        // 输入框上方的分隔线
        GUI.Box(new Rect(inputAreaX + 4f, inputAreaY - 2f, inputAreaW - 8f, 1f), GUIContent.none, _separatorStyle);

        // 输入框
        GUI.SetNextControlName("rightPanelInput");

        // ★ Enter 发送（必须在 TextField 之前检测，因为 TextField 会消费 Enter 事件）
        if (Event.current.isKey
            && Event.current.type == EventType.KeyDown
            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
            && _inputText.Length > 0
            && GUI.GetNameOfFocusedControl() == "rightPanelInput")
        {
            Event.current.Use();
            string msg = _inputText.Trim();
            _inputText = "";
            if (_chat != null)
                _chat.SendMessage(msg, null);
        }

        // ——— 自定义悬停描边（不带默认灰色边框） ———
        Vector2 mousePos2 = Event.current.mousePosition;
        bool hoveringInput = inputBgRect.Contains(mousePos2);
        if (hoveringInput || GUI.GetNameOfFocusedControl() == "rightPanelInput")
        {
            Color borderColor = hoveringInput
                ? new Color(0.65f, 0.50f, 0.90f, 0.40f)
                : new Color(0.70f, 0.55f, 0.92f, 0.60f);
            GUI.color = borderColor;
            float bw = inputBgRect.width;
            float bh = inputBgRect.height;
            float bx = inputBgRect.x;
            float by = inputBgRect.y;
            GUI.DrawTexture(new Rect(bx - 1f, by - 1f, bw + 2f, 1f), _whiteTex);  // 顶
            GUI.DrawTexture(new Rect(bx - 1f, by + bh, bw + 2f, 1f), _whiteTex);  // 底
            GUI.DrawTexture(new Rect(bx - 1f, by - 1f, 1f, bh + 2f), _whiteTex);  // 左
            GUI.DrawTexture(new Rect(bx + bw, by - 1f, 1f, bh + 2f), _whiteTex);  // 右
            GUI.color = Color.white;
        }

        _inputText = GUI.TextField(inputBgRect, _inputText, _inputStyle);

        // 聚焦请求
        if (_inputFocused)
        {
            _inputFocused = false;
            GUI.FocusControl("rightPanelInput");
        }

        // 空输入框提示文字
        if (string.IsNullOrEmpty(_inputText) && GUI.GetNameOfFocusedControl() != "rightPanelInput")
        {
            GUI.Label(inputBgRect, "向符玄发送消息…", _placeholderStyle);
        }

        // ——— 面板内点击 → 防穿透 ———
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if (bgRect.Contains(mp))
            {
                bool onButton = localHoveredToolIndex >= 0;
                bool onInput = inputBgRect.Contains(mp);
                if (!onButton && !onInput)
                {
                    Event.current.Use();
                    _inputFocused = true;
                }
            }
            else if (_isExpanded)
            {
                _wantsExpand = false;
            }
        }
    }

    // ==================== 样式初始化 ====================

    private void InitStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        // ═══════════════════════════════════════
        //  符玄主题配色表
        // ═══════════════════════════════════════
        Color cDarkBg     = new Color(0.06f, 0.05f, 0.10f, 0.78f); // 深紫黑底
        Color cMidBg      = new Color(0.10f, 0.08f, 0.15f, 0.70f); // 中紫底
        Color cAccent     = new Color(0.55f, 0.40f, 0.85f, 1.0f);  // 符玄紫
        Color cAccentDim  = new Color(0.40f, 0.28f, 0.65f, 0.6f);  // 暗紫
        Color cAccentGlow = new Color(0.65f, 0.50f, 0.95f, 0.30f); // 紫光晕
        Color cTextMain   = new Color(0.92f, 0.90f, 0.96f, 1.0f);  // 主文字白紫
        Color cTextDim    = new Color(0.60f, 0.55f, 0.70f, 0.6f);  // 淡文字
        Color cGold       = new Color(0.85f, 0.75f, 0.50f, 1.0f);  // 金色点缀

        // ——— 面板背景 ——— 竖直渐变模拟（左右两半混合）
        _bgTex = MakeGradientTex(64, 64, cDarkBg, cMidBg, true);
        _panelStyle = new GUIStyle { normal = { background = _bgTex } };

        // ——— 顶部装饰线（紫） ———
        _accentLineTex = MakeTex(1, 1, cAccent);

        // ——— 分隔线 ———
        _separatorTex = MakeTex(1, 1, new Color(0.45f, 0.35f, 0.65f, 0.25f));
        _separatorStyle = new GUIStyle { normal = { background = _separatorTex } };

        // ——— 输入框背景 ——— 紫调（不从默认皮肤继承，避免 hover 灰色边框）
        _inputBgTex = MakeTex(1, 1, new Color(0.22f, 0.16f, 0.35f, 0.75f));
        _inputHoverBgTex = MakeTex(1, 1, new Color(0.32f, 0.24f, 0.48f, 0.85f));
        _inputStyle = new GUIStyle
        {
            normal = { textColor = cTextMain, background = _inputBgTex },
            hover = { textColor = cTextMain, background = _inputHoverBgTex },
            focused = { textColor = Color.white, background = _inputHoverBgTex },
            fontSize = 15,
            padding = new RectOffset(12, 12, 8, 8),
            alignment = TextAnchor.MiddleLeft,
            border = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            stretchHeight = true
        };

        // ——— 输入框占位提示 ——— 淡紫白色
        _placeholderStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.72f, 0.68f, 0.82f, 0.9f) },
            fontSize = 15,
            padding = new RectOffset(12, 10, 8, 8),
            alignment = TextAnchor.MiddleLeft
        };

        // ——— 工具按钮 ——— 紫光主题
        _toolTex = MakeCircleTex(34, new Color(0.50f, 0.35f, 0.80f, 0.18f));
        _toolHoverTex = MakeCircleTex(34, new Color(0.60f, 0.45f, 0.90f, 0.40f));
        _glowTex = MakeCircleTex(48, cAccentGlow);

        _toolBtnStyle = new GUIStyle
        {
            normal = { background = _toolTex, textColor = new Color(0.75f, 0.70f, 0.85f) },
            hover = { background = _toolHoverTex, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };

        _toolBtnHoverStyle = new GUIStyle
        {
            normal = { background = _toolHoverTex, textColor = Color.white },
            hover = { background = _toolHoverTex, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };

        // ——— 按钮悬停提示 ———
        _hintStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.55f, 0.50f, 0.70f, 0.5f) },
            fontSize = 12,
            alignment = TextAnchor.UpperLeft
        };

        // ——— 顶栏 ——— 太卜司标识 + 时间
        _brandStyle = new GUIStyle
        {
            normal = { textColor = cGold },
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter
        };
        _topBarStyle = new GUIStyle
        {
            normal = { textColor = cAccent },
            fontSize = 11,
            alignment = TextAnchor.UpperCenter
        };
        _timeStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.85f, 0.80f, 0.92f, 0.85f) },
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter
        };

        // ——— 角落云纹角饰（复刻 ChatBubble 风格） ———
        _ornamentTL = GenCornerOrnament(40, cAccentDim, true);
        _ornamentTR = GenCornerOrnament(40, cAccentDim, false);
        _ornamentBR = GenCornerOrnament(40, cAccentDim, false);
        _ornamentBL = GenCornerOrnament(40, cAccentDim, true);

        // 白图（保留）
        _whiteTex = MakeTex(1, 1, Color.white);
    }

    // ==================== 工具方法 ====================

    private static Texture2D MakeTex(int w, int h, Color color)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, color);
        tex.Apply();
        return tex;
    }

    /// <summary>创建圆形纹理（用于按钮背景）</summary>
    private static Texture2D MakeCircleTex(int size, Color color)
    {
        size = Mathf.Max(size, 4);
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float center = (size - 1) / 2f;
        float rad = center - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = dist <= rad ? color.a : Mathf.Lerp(color.a, 0f, (dist - rad) / 2f);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>创建竖直渐变纹理</summary>
    private static Texture2D MakeGradientTex(int w, int h, Color top, Color bottom, bool horizontal = false)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            Color c = Color.Lerp(top, bottom, t);
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    /// <summary>创建角落云纹图案（复刻 ChatBubble 风格）</summary>
    private static Texture2D GenCornerOrnament(int size, Color c, bool topLeft)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        Color t = new Color(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = topLeft ? x : (size - 1f - x);
                float py = topLeft ? y : (size - 1f - y);
                float d = Mathf.Sqrt((px * px + py * py) / (2f * (size - 1f) * (size - 1f)));
                float angle = Mathf.Atan2(py + 0.01f, px + 0.01f);
                float spiral = Mathf.Sin(angle * 3f + d * 10f) * 0.5f + 0.5f;
                float alphaMask = Mathf.Clamp01((1f - d) * 1.8f - 0.5f);
                float val = Mathf.Pow(spiral * alphaMask, 0.6f);
                bool draw = val > 0.20f && d < 0.85f;
                float a = draw ? Mathf.Clamp01(val * 1.5f) * c.a : 0f;
                tex.SetPixel(x, y, draw ? new Color(c.r, c.g, c.b, a) : t);
            }
        }
        tex.Apply();
        return tex;
    }

    // ==================== 清理 ====================

    void OnDestroy()
    {
        if (_bgTex != null) Destroy(_bgTex);
        if (_inputBgTex != null) Destroy(_inputBgTex);
        if (_inputHoverBgTex != null) Destroy(_inputHoverBgTex);
        if (_separatorTex != null) Destroy(_separatorTex);
        if (_whiteTex != null) Destroy(_whiteTex);
        if (_toolTex != null) Destroy(_toolTex);
        if (_toolHoverTex != null) Destroy(_toolHoverTex);
        if (_glowTex != null) Destroy(_glowTex);
        if (_ornamentTL != null) Destroy(_ornamentTL);
        if (_ornamentTR != null) Destroy(_ornamentTR);
        if (_ornamentBR != null) Destroy(_ornamentBR);
        if (_ornamentBL != null) Destroy(_ornamentBL);
    }
}
