using UnityEngine;

/// <summary>
/// 悬浮球 + 辐射菜单（类似原神/星穹铁道 Tab 轮盘）
///
/// 功能：
/// - 桌面常驻悬浮球，可拖拽 reposition
/// - 点击悬浮球展开辐射菜单，子项围绕圆形排列
/// - 子项：设置 / 报告 / 便签 → 点击直接打开对应的 BallPanel
/// - 点击辐射菜单外区域或右键关闭菜单
///
/// 点击穿透集成：
/// DragHandler.UpdateClickThrough() 中已将悬浮球区域计入可点击范围
/// </summary>
public class FloatingBall : MonoBehaviour
{
    // ==================== 配置参数 ====================
    [Header("悬浮球外观")]
    public float ballRadius = 22f;              // 悬浮球半径
    public float ballMarginX = 20f;             // 距屏幕右边缘
    public float ballMarginY = 80f;             // 距屏幕底边缘

    [Header("辐射菜单")]
    public float menuRadius = 90f;              // 菜单项距球心距离
    public float itemRadius = 28f;              // 菜单项半径
    public float menuOpenAngle = -90f;          // 起始角度（度），-90=正上方

    // 菜单项定义
    private readonly string[] _itemLabels = { "⚙ 设置", "📊 报告", "📋 便签" };
    private readonly BallPanel.PanelType[] _itemPanelTypes = {
        BallPanel.PanelType.Settings,
        BallPanel.PanelType.Report,
        BallPanel.PanelType.Reminders
    };

    // ==================== 运行时状态 ====================
    private Vector2 _ballPos;           // 球中心屏幕坐标（左上角原点）
    private bool _isDragging = false;
    private bool _isDragPending = false; // 可能是点击，等移动超过阈值才确认是拖拽
    private bool _isMenuOpen = false;
    private Vector2 _dragStartMouse;
    private Vector2 _dragOffset;

    private DesktopPet _pet;
    private BallPanel _ballPanel;

    // 拖拽/点击区分阈值
    private const float CLICK_THRESHOLD = 8f;

    // ==================== AI 状态指示 ====================
    private string _aiStatusText = "";        // 当前显示的 AI 状态文字
    private Color _aiStatusColor = Color.white;
    private float _aiStatusTimer = 0f;        // 状态计时器（自动清除旧状态）
    private const float AI_STATUS_DURATION = 8f; // 状态持续秒数后淡出

    // 样式
    private GUIStyle _ballStyle;
    private GUIStyle _itemStyle;
    private GUIStyle _itemHoverStyle;
    private GUIStyle _statusStyle;
    private Texture2D _ballTex;
    private Texture2D _menuBgTex;
    private Texture2D _itemTex;
    private Texture2D _itemHoverTex;
    private Texture2D _statusBgTex;
    private Texture2D _glowTexClosed;  // 缓存发光纹理（菜单关闭态）
    private Texture2D _glowTexOpen;    // 缓存发光纹理（菜单展开态）
    private bool _stylesInit = false;

    // ==================== 公开属性 ====================
    public bool IsMenuOpen => _isMenuOpen;
    public Vector2 BallCenter => _ballPos;
    public float BallRadius => ballRadius;

    /// <summary>悬浮球包围盒（用于点击穿透检测）</summary>
    public Rect BallRect
    {
        get
        {
            float r = ballRadius + 4f;
            return new Rect(_ballPos.x - r, _ballPos.y - r, r * 2f, r * 2f);
        }
    }

    /// <summary>辐射菜单整体包围盒（含所有菜单项）</summary>
    public Rect MenuBoundsRect
    {
        get
        {
            if (!_isMenuOpen) return BallRect;
            float r = menuRadius + itemRadius + 10f;
            return new Rect(_ballPos.x - r, _ballPos.y - r, r * 2f, r * 2f);
        }
    }

    /// <summary>判断点是否在悬浮球或辐射菜单的可交互区域内</summary>
    public bool IsPointInInteractiveArea(Vector2 pt)
    {
        if (Vector2.Distance(pt, _ballPos) <= ballRadius + 4f)
            return true;
        if (_isMenuOpen)
        {
            float r = menuRadius + itemRadius + 10f;
            if (Mathf.Abs(pt.x - _ballPos.x) > r || Mathf.Abs(pt.y - _ballPos.y) > r)
                return false;
            // 精确检测每个菜单项
            for (int i = 0; i < 3; i++)
            {
                Vector2 itemCenter = GetItemCenter(i);
                if (Vector2.Distance(pt, itemCenter) <= itemRadius + 4f)
                    return true;
            }
        }
        return false;
    }

    void Start()
    {
        _pet = GetComponent<DesktopPet>();
        _ballPanel = GetComponent<BallPanel>();
        if (_ballPanel == null)
        {
            _ballPanel = gameObject.AddComponent<BallPanel>();
            Debug.Log("[FloatingBall] 自动添加了 BallPanel 组件");
        }

        // 订阅 AI 状态事件
        var chat = GetComponent<ChatManager>();
        if (chat != null)
        {
            chat.OnRequestStarted += () => SetAiStatus("🤔 思考中…", new Color(0.6f, 0.8f, 1f));
            chat.OnToolCalled += (toolName) => SetAiStatus($"⚡ 施展「{GetToolDisplayName(toolName)}」…");
            chat.OnToolResult += (toolName, result) => SetAiStatus($"✓ 「{GetToolDisplayName(toolName)}」完成");
            chat.OnRequestError += (err) => SetAiStatus($"❌ {err}", new Color(1f, 0.4f, 0.4f));
            chat.OnNewReply += (text) => SetAiStatus("💬 回复中…");
        }

        // 默认位置：右下角
        _ballPos = new Vector2(Screen.width - ballMarginX, Screen.height - ballMarginY);
        UnityEngine.Debug.Log($"[FloatingBall] 已启动! 位置=({_ballPos.x:F0},{_ballPos.y:F0}), 屏幕={Screen.width}x{Screen.height}");
    }

    /// <summary>设置 AI 状态文字（自动计时清除）</summary>
    private void SetAiStatus(string text, Color? color = null)
    {
        _aiStatusText = text;
        _aiStatusColor = color ?? Color.white;
        _aiStatusTimer = AI_STATUS_DURATION;
    }

    /// <summary>获取工具调用的中文显示名</summary>
    private string GetToolDisplayName(string toolName)
    {
        return toolName switch
        {
            "generate_motion" => "演武术式",
            "evaluate_motion" => "武式评审",
            "open_url" => "观星术",
            "take_screenshot" => "摄形术",
            "set_volume" => "调音术",
            "lock_screen" => "封印术",
            "shutdown" => "封机术",
            "restart" => "重启术",
            "send_notification" => "传音术",
            "clipboard_write" => "录写术",
            "clipboard_read" => "观简术",
            "get_system_info" => "洞观术",
            "search_files" => "搜灵术",
            "launch_app" => "开阵术",
            "query_exams" => "卜算考试",
            "query_scores" => "卜算成绩",
            "query_schedule" => "卜算课表",
            "add_reminder" => "添记事",
            "list_reminders" => "阅记事",
            "delete_reminder" => "销记事",
            "play_expression" => "演面术",
            "play_action" => "演武术式",
            _ => toolName
        };
    }

    void InitStyles()
    {
        if (_stylesInit) return;
        _stylesInit = true;

        _ballTex = MakeCircleTex((int)(ballRadius * 2 + 4), new Color(0.85f, 0.55f, 0.75f, 0.92f));
        _menuBgTex = MakeTex(1, 1, new Color(0.08f, 0.08f, 0.12f, 0.75f));
        _itemTex = MakeCircleTex((int)(itemRadius * 2 + 4), new Color(0.25f, 0.25f, 0.35f, 0.92f));
        _itemHoverTex = MakeCircleTex((int)(itemRadius * 2 + 4), new Color(0.45f, 0.35f, 0.55f, 0.95f));

        _ballStyle = new GUIStyle
        {
            normal = { background = _ballTex, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold
        };

        // 缓存发光纹理（避免每帧 new Texture2D 导致内存泄漏）
        float glowDiameter = ballRadius * 2f + 4f + 8f;
        int glowSize = (int)glowDiameter;
        _glowTexClosed = MakeCircleTex(glowSize, new Color(0.9f, 0.6f, 0.8f, 0.15f));
        _glowTexOpen = MakeCircleTex(glowSize, new Color(0.9f, 0.6f, 0.8f, 0.4f));

        _statusBgTex = MakeRoundRectTex(180, 28, new Color(0.08f, 0.08f, 0.12f, 0.85f));

        _statusStyle = new GUIStyle
        {
            normal = { background = _statusBgTex, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            fontStyle = FontStyle.Normal,
            wordWrap = false,
            contentOffset = new Vector2(0, 0),
            clipping = TextClipping.Clip,
            padding = new RectOffset(10, 10, 4, 4)
        };

        _itemStyle = new GUIStyle
        {
            normal = { background = _itemTex, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };

        _itemHoverStyle = new GUIStyle(_itemStyle)
        {
            normal = { background = _itemHoverTex, textColor = new Color(1f, 0.8f, 0.9f) }
        };
    }

    void Update()
    {
        // 如果 BallPanel 已打开，关闭辐射菜单
        if (_ballPanel != null && _ballPanel.IsOpen)
        {
            if (_isMenuOpen) _isMenuOpen = false;
        }

        // AI 状态计时器
        if (_aiStatusTimer > 0f)
        {
            _aiStatusTimer -= Time.deltaTime;
            if (_aiStatusTimer <= 0f)
            {
                _aiStatusText = "";
            }
        }
    }

    void OnGUI()
    {
        InitStyles();

        // 确保 _ballPos 已初始化（Screen 可能在 Start 时不可靠）
        if (_ballPos == Vector2.zero && Screen.width > 0)
        {
            _ballPos = new Vector2(Screen.width - ballMarginX, Screen.height - ballMarginY);
        }

        // ★ 用最低 GUI.depth 确保悬浮球绘制在最上层（depth 越低越后绘制）
        GUI.depth = -10;

        // ★ 如果 BallPanel 已打开，只绘制不处理事件（避免消耗 BallPanel 所需的事件）
        if (_ballPanel != null && _ballPanel.IsOpen)
        {
            // 确保菜单关闭
            if (_isMenuOpen) _isMenuOpen = false;
            _isDragPending = false;
            _isDragging = false;

            // 只绘制悬浮球
            DrawBall();
            return;
        }

        // ===== 1. 处理鼠标事件 =====
        Event e = Event.current;
        Vector2 mp = GetMousePos(e);

        // ---- MouseDown ----
        if (e.type == EventType.MouseDown)
        {
            if (e.button == 0) // 左键
            {
                bool onBall = Vector2.Distance(mp, _ballPos) <= ballRadius + 4f;

                if (_isMenuOpen)
                {
                    // 菜单已打开：检查菜单项点击
                    bool hitItem = false;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 itemCenter = GetItemCenter(i);
                        if (Vector2.Distance(mp, itemCenter) <= itemRadius + 4f)
                        {
                            OnItemClicked(i);
                            hitItem = true;
                            break;
                        }
                    }
                    if (hitItem) { e.Use(); return; }

                    // 点到球 → 关闭菜单
                    if (onBall) { _isMenuOpen = false; e.Use(); return; }

                    // 点菜单外部 → 关闭
                    _isMenuOpen = false;
                    e.Use();
                    return;
                }

                // 菜单未打开
                if (onBall)
                {
                    // 记录起始位置，准备区分点击/拖拽
                    _isDragPending = true;
                    _isDragging = false;
                    _dragStartMouse = mp;
                    _dragOffset = mp - _ballPos;
                    e.Use();
                    return;
                }
            }

            if (e.button == 1) // 右键 → 关闭菜单
            {
                if (_isMenuOpen)
                {
                    _isMenuOpen = false;
                    e.Use();
                    return;
                }
            }
        }

        // ---- MouseDrag ----
        if (e.type == EventType.MouseDrag && _isDragPending)
        {
            Vector2 delta = mp - _dragStartMouse;
            if (delta.magnitude >= CLICK_THRESHOLD)
            {
                // 超过阈值 → 确认是拖拽
                _isDragPending = false;
                _isDragging = true;
            }
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && _isDragging)
        {
            _ballPos = mp - _dragOffset;
            // 限制在屏幕内
            _ballPos.x = Mathf.Clamp(_ballPos.x, ballRadius + 4f, Screen.width - ballRadius - 4f);
            _ballPos.y = Mathf.Clamp(_ballPos.y, ballRadius + 4f, Screen.height - ballRadius - 4f);
            e.Use();
            return;
        }

        // ---- MouseUp ----
        if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (_isDragPending)
            {
                // 没有超过拖拽阈值 → 视为点击切换菜单
                _isDragPending = false;
                _isMenuOpen = !_isMenuOpen;
                e.Use();
                return;
            }
            if (_isDragging)
            {
                _isDragging = false;
                e.Use();
                return;
            }
        }

        // ===== 2. 绘制 =====
        // 绘制辐射菜单背景（在球下方）
        if (_isMenuOpen)
        {
            DrawRadialMenu();
        }

        // 绘制悬浮球（最上层）
        DrawBall();

        // 绘制 AI 状态指示（气泡在悬浮球下方，挤占右边缘空间）
        DrawAiStatus();
    }

    private void DrawBall()
    {
        // 球体
        float diameter = ballRadius * 2f + 4f;
        Rect ballRect = new Rect(_ballPos.x - ballRadius - 2f, _ballPos.y - ballRadius - 2f, diameter, diameter);

        // 发光效果（使用 InitStyles 中缓存的纹理，避免每帧 new Texture2D → 内存泄漏）
        Texture2D glowTex = _isMenuOpen ? _glowTexOpen : _glowTexClosed;
        if (glowTex != null)
            GUI.DrawTexture(new Rect(ballRect.x - 4, ballRect.y - 4, diameter + 8, diameter + 8), glowTex);

        // 球体本身
        if (_ballTex != null)
            GUI.DrawTexture(ballRect, _ballTex);

        // 中心图标
        string icon = _isMenuOpen ? "✕" : "✦";
        GUI.Label(ballRect, icon, _ballStyle);
    }

    /// <summary>在悬浮球下方绘制 AI 状态指示气泡</summary>
    private void DrawAiStatus()
    {
        if (string.IsNullOrEmpty(_aiStatusText) || _statusStyle == null) return;

        // 计算透明度（渐出效果）
        float alpha = 1f;
        if (_aiStatusTimer < 2f)
        {
            alpha = _aiStatusTimer / 2f; // 最后 2 秒淡出
        }
        if (alpha <= 0.01f) return;

        // 气泡位于悬浮球正下方
        float bubbleWidth = Mathf.Min(200f, _aiStatusText.Length * 9f + 20f);
        float bubbleHeight = 26f;
        float x = _ballPos.x - bubbleWidth / 2f;
        float y = _ballPos.y + ballRadius + 8f;

        // 保底不让气泡超出左右屏幕
        x = Mathf.Clamp(x, 4f, Screen.width - bubbleWidth - 4f);

        Rect bubbleRect = new Rect(x, y, bubbleWidth, bubbleHeight);

        // 旧 GUI.color 保存
        Color oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        // 背景
        if (_statusBgTex != null)
            GUI.DrawTexture(bubbleRect, _statusBgTex);

        // 文字
        Color oldTextColor = _statusStyle.normal.textColor;
        _statusStyle.normal.textColor = _aiStatusColor;
        GUI.Label(bubbleRect, _aiStatusText, _statusStyle);
        _statusStyle.normal.textColor = oldTextColor;

        GUI.color = oldColor;
    }

    private void DrawRadialMenu()
    {
        // 半透明背景圆环（连接线效果）
        for (int i = 0; i < 3; i++)
        {
            Vector2 c = GetItemCenter(i);
            // 从球心到菜单项的连线
            DrawLine(_ballPos, c, new Color(0.8f, 0.5f, 0.7f, 0.3f), 2f);
        }

        // 绘制菜单项
        Vector2 mousePos = GetMousePos(Event.current);
        for (int i = 0; i < 3; i++)
        {
            Vector2 center = GetItemCenter(i);
            float r = itemRadius * 2f + 4f;
            Rect itemRect = new Rect(center.x - itemRadius - 2f, center.y - itemRadius - 2f, r, r);

            bool hover = Vector2.Distance(mousePos, center) <= itemRadius + 4f;

            if (hover)
                GUI.DrawTexture(itemRect, _itemHoverTex);
            else
                GUI.DrawTexture(itemRect, _itemTex);

            // 标签
            var style = hover ? _itemHoverStyle : _itemStyle;
            GUI.Label(itemRect, _itemLabels[i], style);
        }
    }

    /// <summary>获取第 i 个菜单项的中心坐标</summary>
    private Vector2 GetItemCenter(int index)
    {
        float angleDeg = menuOpenAngle + index * 120f; // 3 项 = 每 120°
        float angleRad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(
            _ballPos.x + Mathf.Cos(angleRad) * menuRadius,
            _ballPos.y + Mathf.Sin(angleRad) * menuRadius
        );
    }

    private void OnItemClicked(int index)
    {
        _isMenuOpen = false;
        // ★ 清除拖拽状态，防止后续 MouseUp 误触发切换菜单
        _isDragPending = false;
        _isDragging = false;

        // 直接打开 BallPanel 对应面板
        if (_ballPanel != null && index >= 0 && index < _itemPanelTypes.Length)
        {
            BallPanel.PanelType type = _itemPanelTypes[index];
            Vector2 panelPos = new Vector2(_ballPos.x - 140f, _ballPos.y - menuRadius - itemRadius - 100f);
            panelPos.x = Mathf.Clamp(panelPos.x, 10, Screen.width - 290);
            panelPos.y = Mathf.Clamp(panelPos.y, 10, Screen.height - 390);
            _ballPanel.ShowPanel(type, panelPos);
        }
    }

    // ==================== 工具方法 ====================

    private Vector2 GetMousePos(Event e)
    {
        if (e != null)
        {
            // OnGUI 中 Event.mousePosition Y 轴是 GUI 坐标（从上往下）
            return new Vector2(e.mousePosition.x, e.mousePosition.y);
        }
        Vector2 p = Input.mousePosition;
        p.y = Screen.height - p.y;
        return p;
    }

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

    /// <summary>创建圆角矩形纹理（用于状态气泡背景）</summary>
    private static Texture2D MakeRoundRectTex(int w, int h, Color color)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float radius = Mathf.Min(w, h) / 2f - 1f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 计算四角距离
                float dx = x < radius ? radius - x : (x > w - radius ? x - (w - radius) : 0f);
                float dy = y < radius ? radius - y : (y > h - radius ? y - (h - radius) : 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = dist <= radius ? color.a : Mathf.Lerp(color.a, 0f, (dist - radius) / 2f);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeTex(int w, int h, Color color)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, color);
        tex.Apply();
        return tex;
    }

    // 简单画线
    private static Texture2D _lineTex;
    private static void DrawLine(Vector2 from, Vector2 to, Color color, float width)
    {
        if (_lineTex == null)
        {
            _lineTex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _lineTex.SetPixel(0, 0, Color.white);
            _lineTex.Apply();
        }

        Vector2 dir = (to - from).normalized;
        float len = Vector2.Distance(from, to);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        GUI.color = color;
        Matrix4x4 oldMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, from);
        GUI.DrawTexture(new Rect(from.x, from.y - width / 2f, len, width), _lineTex);
        GUI.matrix = oldMatrix;
        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        // 清理所有动态创建的纹理，防止内存泄漏
        if (_glowTexClosed != null) Destroy(_glowTexClosed);
        if (_glowTexOpen != null) Destroy(_glowTexOpen);
        // 以下由 InitStyles 创建，也一并清理
        if (_ballTex != null) Destroy(_ballTex);
        if (_menuBgTex != null) Destroy(_menuBgTex);
        if (_itemTex != null) Destroy(_itemTex);
        if (_itemHoverTex != null) Destroy(_itemHoverTex);
        if (_statusBgTex != null) Destroy(_statusBgTex);
    }
}
