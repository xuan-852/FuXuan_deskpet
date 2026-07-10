using UnityEngine;

/// <summary>
/// 右侧多功能面板 — Windows 11 Widgets 风格
///
/// 功能：
/// - 屏幕右边缘窄条触发，按 ~ 键或鼠标划过展开
/// - 滑出面板含工具图标 + 底部输入框
/// - 跨 BallPanel 标签页（设置/便签/报告）保持可见
/// - 磨砂玻璃风格，不抢占桌面空间
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
    private Texture2D _bgTex;            // 面板背景
    private Texture2D _inputBgTex;       // 输入框背景
    private Texture2D _separatorTex;     // 分隔线
    private Texture2D _whiteTex;         // 白图
    private Texture2D _toolTex;          // 按钮正常背景
    private Texture2D _toolHoverTex;     // 按钮悬停背景
    private bool _stylesReady = false;

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

        // ——— 工具按钮 ——— 全部计算在本地，不依赖成员变量
        float btnY = 12f;
        float btnSize = 32f;
        float btnGap = 4f;
        float btnX = px + (pw - btnSize) / 2f;

        int localHoveredToolIndex = -1;
        for (int i = 0; i < _tools.Length; i++)
        {
            float by = btnY + i * (btnSize + btnGap);
            Rect btnRect = new Rect(btnX, by, btnSize, btnSize);
            bool hover = bgRect.Contains(mp) && btnRect.Contains(mp);
            if (hover) localHoveredToolIndex = i;

            GUIStyle style = hover ? _toolBtnHoverStyle : _toolBtnStyle;
            if (GUI.Button(btnRect, new GUIContent(_tools[i].icon, _tools[i].label), style))
            {
                var tool = _tools[i];
                if (tool.panelType.HasValue && _ballPanel != null)
                {
                    Vector2 panelPos = new Vector2(px - 290f, 60f);
                    _ballPanel.ShowPanel(tool.panelType.Value, panelPos);
                }
                else if (tool.label == "聊天")
                {
                    _inputFocused = true;
                    GUI.FocusControl("rightPanelInput");
                }
            }
        }

        // ——— 按钮下方提示文字（极淡，仅悬停时微光） ———
        float hintAreaY = btnY + _tools.Length * (btnSize + btnGap) + 4f;
        if (localHoveredToolIndex >= 0)
        {
            Rect hintRect = new Rect(px + 8f, hintAreaY, pw - 16f, 18f);
            GUI.Label(hintRect, _tools[localHoveredToolIndex].label, _hintStyle);
        }

        // ——— 底部输入框 ———
        float inputAreaH = inputBarHeight;
        float inputAreaY = ph - inputAreaH - 8f;
        float inputAreaX = px + 6f;
        float inputAreaW = pw - 12f;

        Rect inputBgRect = new Rect(inputAreaX, inputAreaY, inputAreaW, inputAreaH);

        // 输入框上方的细线分隔
        GUI.Box(new Rect(inputAreaX, inputAreaY, inputAreaW, 1f), GUIContent.none, _separatorStyle);

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
            GUI.Label(inputBgRect, "输入消息…", _placeholderStyle);
        }

        // ——— 面板内点击 → 防穿透 ———
        // 注意：不能简单 Event.Use() 全部，否则 GUI.Button/TextField 无法工作。
        // 只在点击发生在面板背景上且不是按钮/输入框时消费事件。
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if (bgRect.Contains(mp))
            {
                // 检查是否点在了按钮或输入框上 — 这些控件自己会处理事件
                bool onButton = localHoveredToolIndex >= 0;
                bool onInput = inputBgRect.Contains(mp);
                if (!onButton && !onInput)
                {
                    Event.current.Use(); // 面板内空白区域点击 → 防桌面穿透 + 聚焦输入框
                    _inputFocused = true;
                }
            }
            else if (_isExpanded)
            {
                // 面板外任意点击 → 收回（但让事件继续传播给其他 UI）
                _wantsExpand = false;
            }
        }
    }

    // ==================== 样式初始化 ====================

    private void InitStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        // 面板背景 — 深色半透明磨砂感
        _bgTex = MakeTex(1, 1, new Color(0.08f, 0.08f, 0.12f, 0.75f));
        _panelStyle = new GUIStyle { normal = { background = _bgTex } };

        // 分隔线
        _separatorTex = MakeTex(1, 1, new Color(1f, 1f, 1f, 0.15f));
        _separatorStyle = new GUIStyle { normal = { background = _separatorTex } };

        // 输入框背景 — 比面板亮一个层次
        _inputBgTex = MakeTex(1, 1, new Color(0.35f, 0.35f, 0.40f, 0.6f));
        _inputStyle = new GUIStyle(GUI.skin.textField)
        {
            normal = { textColor = new Color(0.95f, 0.95f, 1.0f), background = _inputBgTex },
            focused = { textColor = Color.white, background = _inputBgTex },
            fontSize = 16,
            padding = new RectOffset(10, 10, 8, 8),
            alignment = TextAnchor.MiddleLeft,
            border = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            stretchHeight = true
        };

        // 输入框占位提示文字
        _placeholderStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.6f, 0.6f, 0.7f, 0.8f) },
            fontSize = 16,
            padding = new RectOffset(10, 10, 8, 8),
            alignment = TextAnchor.MiddleLeft
        };

        // 工具按钮 — 可见圆角按钮背景
        _toolTex = MakeCircleTex(32, new Color(1f, 1f, 1f, 0.12f));
        _toolHoverTex = MakeCircleTex(32, new Color(0.6f, 0.7f, 1f, 0.25f));

        _toolBtnStyle = new GUIStyle
        {
            normal = { background = _toolTex, textColor = new Color(0.85f, 0.85f, 0.9f) },
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

        // 按钮悬停提示（极淡文字）
        _hintStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.6f, 0.6f, 0.7f, 0.35f) },
            fontSize = 12,
            alignment = TextAnchor.UpperLeft
        };

        // 白图（保留供今后扩展使用）
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

    // ==================== 清理 ====================

    void OnDestroy()
    {
        if (_bgTex != null) Destroy(_bgTex);
        if (_inputBgTex != null) Destroy(_inputBgTex);
        if (_separatorTex != null) Destroy(_separatorTex);
        if (_whiteTex != null) Destroy(_whiteTex);
        if (_toolTex != null) Destroy(_toolTex);
        if (_toolHoverTex != null) Destroy(_toolHoverTex);
    }
}
