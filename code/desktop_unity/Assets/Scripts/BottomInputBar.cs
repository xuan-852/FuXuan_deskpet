using UnityEngine;

/// <summary>
/// 底部输入栏 — Windows 搜索风格
///
/// 固定位置、简洁浅色、干净输入体验
/// </summary>
public class BottomInputBar : MonoBehaviour
{
    // ===== 固定坐标（测试最优值） =====
    private const float BAR_LEFT = 265f;
    private const float BAR_RIGHT = 635f;
    private const float BAR_TOP = 1528f;
    private const float BAR_BOTTOM = 1600f;

    // 便捷访问
    public float BarLeft => BAR_LEFT;
    public float BarRight => BAR_RIGHT;
    public float BarTop => BAR_TOP;
    public float BarBottom => BAR_BOTTOM;
    private float BarW => BarRight - BarLeft;
    private float BarH => BarBottom - BarTop;

    // ===== 内部状态 =====
    private ChatManager _chat;
    private string _inputText = "";

    // ===== 样式 =====
    private GUIStyle _barBgStyle;
    private GUIStyle _inputStyle;
    private GUIStyle _shadowStyle;
    private Texture2D _bgTex;
    private Texture2D _inputBgTex;
    private Texture2D _shadowTex;
    private Texture2D _whiteTex;     // 单色白图，配合 GUI.color 实现任意颜色线条
    private bool _stylesReady = false;

    // ===== 淡入动画 =====
    private float _alpha = 0f;
    private const float FADE_IN_DURATION = 0.5f;
    private float _fadeTimer = 0f;
    private bool _started = false;

    // ===== 公共属性 =====

    public bool IsMouseOverBar { get; private set; } = false;

    void Start()
    {
        RefreshChatRef();
        _started = true;
        Debug.Log($"[BottomInputBar] 搜索栏已就绪 | left={BAR_LEFT:F0} right={BAR_RIGHT:F0} top={BAR_TOP:F0} bottom={BAR_BOTTOM:F0}");
    }

    /// <summary>查找 ChatManager 引用</summary>
    private void RefreshChatRef()
    {
        if (_chat != null) return;
        _chat = GetComponent<ChatManager>();
        if (_chat == null) _chat = FindObjectOfType<ChatManager>();
    }

    /// <summary>跳过逐句动画，代理到 ChatManager</summary>
    public void SkipSentenceAnimation()
    {
        if (_chat != null) _chat.SkipSentenceAnimation();
    }

    void OnGUI()
    {
        InitStyles();
        RefreshChatRef();

        if (!_started)
        {
            _fadeTimer += Time.deltaTime;
            _alpha = Mathf.Clamp01(_fadeTimer / FADE_IN_DURATION);
        }
        else _alpha = 1f;

        // 鼠标悬停检测
        Vector2 mp = Event.current.mousePosition;
        IsMouseOverBar = mp.x >= BarLeft && mp.x <= BarRight && mp.y >= BarTop && mp.y <= BarBottom;

        DrawBar();
    }

    private void InitStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        // 共享白图 — 配合 GUI.color 实现任意颜色线条，避免每帧 new Texture2D
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();

        // Windows 搜索风格 — 白色干净背景
        _bgTex = new Texture2D(1, 1);
        _bgTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.92f));
        _bgTex.Apply();

        _inputBgTex = new Texture2D(1, 1);
        _inputBgTex.SetPixel(0, 0, new Color(0.85f, 0.85f, 0.88f, 0.6f));
        _inputBgTex.Apply();

        // 半透明遮罩
        _shadowTex = new Texture2D(1, 1);
        _shadowTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.15f));
        _shadowTex.Apply();

        _shadowStyle = new GUIStyle { normal = { background = _shadowTex } };

        _barBgStyle = new GUIStyle
        {
            normal = { background = _bgTex },
            padding = new RectOffset(12, 12, 10, 10)
        };

        _inputStyle = new GUIStyle(GUI.skin.textField)
        {
            normal = { textColor = new Color(0.15f, 0.15f, 0.18f), background = _inputBgTex },
            fontSize = 14,
            padding = new RectOffset(10, 10, 6, 6),
            margin = new RectOffset(0, 4, 0, 0),
            alignment = TextAnchor.MiddleLeft
        };
    }

    private void DrawBar()
    {
        float y = BarTop;
        float w = BarW;
        float h = BarH;
        float screenH = Screen.height;

        // ——— 白色输入框区域 ———
        Rect barRect = new Rect(BarLeft, y, w, h);
        GUI.Box(barRect, GUIContent.none, _barBgStyle);

        // ——— 顶部细阴影线（浅灰） ———
        DrawLine((int)BarLeft, (int)y, (int)(BarLeft + w), (int)y, new Color(0.80f, 0.80f, 0.82f, _alpha * 0.8f));

        // ——— 半透明遮罩延伸到屏幕底部 ———
        // 防止拖动窗口钻入输入框与任务栏之间，又能让普通窗口隐约可见
        if (BarBottom < screenH)
        {
            Rect shadowRect = new Rect(BarLeft, BarBottom, w, screenH - BarBottom);
            GUI.Box(shadowRect, GUIContent.none, _shadowStyle);
        }

        float padL = 14f;
        float padR = 14f;
        float padT = 8f;
        float padB = 8f;

        // ——— 输入框（全宽，发送按钮已删除，按 Enter 发送） ———
        float inputH = h - padT - padB;
        float inputW = w - padL - padR;
        float inputY = y + padT;

        // Enter 检测
        bool enterPressed = Event.current.isKey
            && Event.current.type == EventType.KeyDown
            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
            && _inputText.Length > 0
            && GUI.GetNameOfFocusedControl() == "bottomChatInput";

        GUI.SetNextControlName("bottomChatInput");
        _inputText = GUI.TextField(new Rect(BarLeft + padL, inputY, inputW, inputH), _inputText, _inputStyle);

        if (enterPressed)
        {
            Event.current.Use();
            string msg = _inputText.Trim();
            _inputText = "";
            if (_chat != null) _chat.SendMessage(msg, null);
            GUI.FocusControl(null);
        }
    }

    /// <summary>画一条水平线（使用缓存白图 + GUI.color，不每帧 new Texture）</summary>
    private void DrawLine(int x1, int y1, int x2, int y2, Color color)
    {
        Color oldColor = GUI.color;
        GUI.color = color;
        GUI.Box(new Rect(x1, y1, x2 - x1, 1), GUIContent.none,
            new GUIStyle { normal = { background = _whiteTex } });
        GUI.color = oldColor;
    }

    void OnDestroy()
    {
        // 清理纹理，防止泄漏
        if (_whiteTex != null) Destroy(_whiteTex);
        if (_bgTex != null) Destroy(_bgTex);
        if (_inputBgTex != null) Destroy(_inputBgTex);
        if (_shadowTex != null) Destroy(_shadowTex);
    }
}
