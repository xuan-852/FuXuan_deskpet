using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// 符玄·太卜司 AI 终端窗口（OpenClaw 式）— 常规窗口大小浮动面板
///
/// 设计语言：
/// - 常规 Windows 窗口尺寸（560×720，长方形偏正方），可拖动、可关闭
/// - 终端式布局：标题栏（状态点+时间+✕）→ 工具行 → 日志滚动区 → 命令行输入
/// - 标题栏与输入框内嵌「符玄头像」★多模态资源
///
/// 功能：
/// - ~ / F2 / \ 键切换开关
/// - 标题栏拖动移动窗口，右上角 ✕ 关闭
/// - 终端日志滚动显示 ChatManager 历史，底部命令行与符玄对话
/// - 跨 BallPanel 标签页（设置/便签/报告）保持可见
///
/// ★多模态标注（需切换多模态模型生成的资源）：
///   1. Assets/Resources/PixelFuXuan.png — 符玄头像原图（透明背景，高清立绘，粉色短发+紫瞳）
///      生成后放入 Resources 目录，代码优先加载它；未找到时回退到代码生成的占位像素画。
/// </summary>
public class RightPanel : MonoBehaviour
{
    // ==================== 配置参数 ====================
    [Header("窗口尺寸（常规窗口）")]
    public float panelWidth = 560f;        // 窗口宽度
    public float panelHeight = 720f;       // 窗口高度（长方形偏正方）
    public float inputBarHeight = 48f;     // 底部输入框高度

    [Header("热键")]
    public KeyCode toggleKey = KeyCode.BackQuote;  // ~ 键切换（窗口内）

    // ==================== 全局热键 (Shift+~) ====================
    // 说明：不用 RegisterHotKey（WM_HOTKEY 会被 Unity 消息泵吞掉，收不到），
    // 改用 GetAsyncKeyState 直接轮询物理键盘状态，任意窗口焦点下均有效。
    private const int VK_OEM_3 = 0xC0;           // ~ 键虚拟码
    private const int VK_LSHIFT = 0xA0;          // 左 Shift
    private const int VK_RSHIFT = 0xA1;          // 右 Shift
    private const int KEY_DOWN = 0x8000;         // 高位为 1 表示按下
    private bool _globalTildeWasDown = false;    // 按下沿检测（防止按住连发）

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // ==================== 工具按钮定义（不用 emoji，用中文单字） ====================
    private readonly (string icon, string label, BallPanel.PanelType? panelType)[] _tools = new (string, string, BallPanel.PanelType?)[]
    {
        ("聊", "聊天", null),                          // 聚焦输入框
        ("设", "设置", BallPanel.PanelType.Settings),
        ("签", "便签", BallPanel.PanelType.Reminders),
        ("告", "报告", BallPanel.PanelType.Report),
        ("收", "收纳", null),                          // 启动 Pogget
    };

    // ==================== 运行时状态 ====================
    private Rect _panelRect;             // 窗口矩形（位置可拖动）
    private bool _isOpen = false;        // 是否打开
    private bool _isDragging = false;    // 标题栏拖动中
    private Vector2 _dragOffset;         // 拖动偏移

    private ChatManager _chat;
    private BallPanel _ballPanel;
    private string _inputText = "";
    private const int MAX_INPUT_LENGTH = 300;   // 输入最大长度，防超长文字溢出输入框
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
    private GUIStyle _sendBtnStyle;      // 发送按钮
    private GUIStyle _sendBtnHoverStyle; // 发送按钮悬停
    private GUIStyle _topBarStyle;       // 顶栏文字
    private GUIStyle _timeStyle;         // 时间文字
    private GUIStyle _brandStyle;        // 太卜司标识
    private Texture2D _bgTex;            // 面板背景（渐变替代）
    private Texture2D _inputBgTex;       // 输入框背景（圆角胶囊）
    private Texture2D _inputHoverBgTex;  // 输入框悬停背景（提亮紫）
    private Texture2D _inputGlowTex;     // 输入框圆角发光描边
    private Texture2D _separatorTex;     // 分隔线
    private Texture2D _whiteTex;         // 白图
    private Texture2D _toolTex;          // 按钮正常背景
    private Texture2D _toolHoverTex;     // 按钮悬停背景
    private Texture2D _glowTex;          // 按钮发光光晕
    private Texture2D _sendBtnTex;       // 发送按钮背景
    private Texture2D _sendBtnHoverTex;  // 发送按钮悬停背景
    private Texture2D _accentLineTex;    // 装饰细线（紫）
    private Texture2D _ornamentTL;       // 左上云纹角饰
    private Texture2D _ornamentTR;       // 右上云纹角饰
    private Texture2D _ornamentBR;       // 右下云纹角饰
    private Texture2D _ornamentBL;       // 左下云纹角饰
    private Texture2D _starfieldTex;     // 星空星点纹理（背景叠加）
    private Texture2D _taijiTex;         // 太极图（发送按钮）
    private Texture2D _hexagramTex;      // 卦象三爻装饰（标题栏）
    private bool _stylesReady = false;

    // ==================== 字体档位缩放 ====================
    private int _fontScaleLevel = 1;                       // 0=最小 1/2/3=更大（默认 1=A2 1.2×）
    private static readonly float[] FONT_SCALES = { 1f, 1.2f, 1.4f, 1.6f };
    private readonly Dictionary<GUIStyle, int> _baseFontSizes = new Dictionary<GUIStyle, int>();

    // ==================== 终端风格（OpenClaw 式） ====================
    private struct LogLine { public string text; public int kind; } // 0=符玄 1=用户 2=系统/工具
    private Vector2 _logScroll;            // 终端日志滚动位置
    private bool _pendingAutoScroll;       // 新增日志后滚到底
    private int _lastLogCount = -1;        // 上次日志条数（检测增量）
    private readonly List<LogLine> _logLines = new List<LogLine>();

    // ==================== QQ 式对话气泡 ====================
    private GUIStyle _bubbleFxStyle;       // 符玄气泡（左，紫）
    private GUIStyle _bubbleUserStyle;     // 用户气泡（右，蓝）
    private Texture2D _bubbleFxTex;
    private Texture2D _bubbleUserTex;
    private Texture2D _userAvatarTex;      // 用户头像（深色圆角 + 我）
    private GUIStyle _userAvatarStyle;

    // ==================== 像素符玄动态形象（QQ 动态形象风格，17x24 网格图） ====================
    private Texture2D _mascotOpenTex;       // 睁眼帧（17x24 → ×4 = 68x96，Point 锐利）
    private Texture2D _mascotBlinkTex;      // 闭眼帧（程序生成：眼睛替换为肤色+闭眼缝线）
    private bool _mascotBlinking;           // 眨眼中
    private float _mascotBlinkT;            // 眨眼进度
    private float _mascotBlinkTimer = 3.2f; // 距下次眨眼秒数
    private float _mascotJumpStart = -10f;  // 跳跃触发时间戳（负=未触发）
    private bool _mascotSubscribed;         // 是否已订阅 OnNewReply
    private const int MASCOT_UPSCALE = 4;   // 17x24 → 68x96

    // ==================== 表情差分徽章（AI 回复【表情:xxx】时右上角显示符号） ====================
    private string _mascotEmotion = "";     // 当前表情（happy/angry/confused/...，空=无徽章）
    private float _mascotEmotionTimer;        // 徽章剩余显示秒数
    private readonly Dictionary<string, Texture2D> _emblemTex = new Dictionary<string, Texture2D>(); // 符号徽章纹理缓存
    private const float EMOTION_SHOW_TIME = 4f; // 表情徽章显示时长
    private const float EMBLEM_SIZE = 18f;   // 徽章显示尺寸（8x8 点阵 × ~2）

    // ==================== 窗口拉伸 ====================
    private bool _isResizing = false;
    private const float MIN_PANEL_W = 400f;
    private const float MIN_PANEL_H = 480f;
    private const float MAX_PANEL_W = 1920f;
    private const float MAX_PANEL_H = 1600f;
    private Font _monoFont;                // 终端等宽字体（中文用雅黑）
    private GUIStyle _titleBarStyle;       // 标题栏背景
    private GUIStyle _termTitleStyle;      // 标题文字
    private GUIStyle _termStatusStyle;     // 状态文字
    private GUIStyle _termTimeStyle;       // 时间
    private GUIStyle _termToolBtnStyle;    // 工具文本按钮
    private GUIStyle _termToolBtnHoverStyle;
    private GUIStyle _termLogStyle;        // 日志-符玄（紫）
    private GUIStyle _termLogUserStyle;    // 日志-用户（浅蓝白）
    private GUIStyle _termLogDimStyle;     // 日志-系统/工具（灰）
    private GUIStyle _termPromptStyle;     // > 提示符
    private GUIStyle _termInputStyle;      // 终端输入框（透明）
    private GUIStyle _termPlaceholderStyle;
    private GUIStyle _inputBarBgStyle;     // 输入行背景条
    private GUIStyle _invisibleScrollbar;  // 隐藏滚动条
    private GUIStyle _closeBtnStyle;       // ✕ 关闭按钮
    private Texture2D _statusDotTex;       // 状态圆点
    private Texture2D _pixelFxTex;         // 符玄头像（★多模态可替换，高清原图）
    private Texture2D _scanlineTex;        // CRT 扫描线纹理（叠加在日志区）
    private Texture2D _borderTex;          // 像素边框（2px 紫色硬边）
    private Texture2D _logRowAltTex;       // 日志交替行背景（极淡紫）
    private Texture2D _titleBarPixelTex;   // 标题栏像素渐变背景
    private Texture2D _inputBarPixelTex;   // 输入栏像素背景

    // ==================== 装饰状态 ====================
    private string _timeDisplay = "";
    private float _timeRefreshTimer = 0f;

    /// <summary>面板完整区域（供 DragHandler 判断鼠标是否在面板交互区域内）</summary>
    public Rect PanelRect => _panelRect;

    /// <summary>供 DragHandler 判断鼠标是否在面板交互区域内（用于点击穿透控制）</summary>
    public bool IsPointInInteractiveArea(Vector2 guiMousePos)
    {
        return _isOpen && _panelRect.Contains(guiMousePos);
    }

    /// <summary>启动 Pogget 桌面收纳工具</summary>
    private void LaunchPogget()
    {
        string exePath = @"d:\pogget\Pogget.exe";
        if (!System.IO.File.Exists(exePath))
        {
            Debug.LogWarning($"[RightPanel] Pogget 未找到: {exePath}");
            return;
        }
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)
            };
            System.Diagnostics.Process.Start(psi);
            Debug.Log("[RightPanel] 已启动 Pogget");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RightPanel] 启动 Pogget 失败: {e.Message}");
        }
    }

    // ==================== 生命周期 ====================

    void Start()
    {
        RefreshRefs();
        // 恢复字体档位（默认 1=A2 1.2×）
        _fontScaleLevel = Mathf.Clamp(PlayerPrefs.GetInt("RightPanelFontScale", 1), 0, FONT_SCALES.Length - 1);
        // 常规窗口：居中显示（长方形偏正方）
        float x = (Screen.width - panelWidth) / 2f;
        float y = (Screen.height - panelHeight) / 2f;
        _panelRect = new Rect(x, y, panelWidth, panelHeight);
        Debug.Log($"[RightPanel] 已就绪，屏幕={Screen.width}x{Screen.height}，窗口={panelWidth}x{panelHeight} 居中=({x},{y})");

        // 全局热键 Shift+~（轮询物理键盘状态，不依赖窗口焦点，防误触）
        Debug.Log("[RightPanel] 全局热键已启用: Shift+~ (GetAsyncKeyState 轮询)");
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

        // 0. 测试收件箱注入（仅测试模式）：外部脚本向 D:\DesktopPetData\inbox.txt 写入一行
        //    → 本帧检测到即作为用户消息发送（绕过 UI 点击，窗口位置无关，适合自动化测试）
        CheckTestInbox();

        // 1. 热键切换 — 兼容中文键盘（`·~` 键、F2、\ 均可）
        bool togglePressed = Input.GetKeyDown(toggleKey)
            || Input.GetKeyDown(KeyCode.F2)
            || Input.GetKeyDown(KeyCode.Backslash);
        if (togglePressed && Time.frameCount != _hotkeyFrame)
        {
            _hotkeyFrame = Time.frameCount;
            Toggle();
        }

        // 1b. 全局热键 Shift+~（任意窗口焦点下均可触发）
        CheckGlobalHotkey();

        // 2. 终端日志重建（历史条数变化时刷新 + 滚到底）
        if (_chat != null && _chat.HistoryCount != _lastLogCount)
        {
            _lastLogCount = _chat.HistoryCount;
            RebuildLog();
            _pendingAutoScroll = true;
        }

        // 3. 标题栏拖动（鼠标按住时在 Update 里更新位置）
        if (_isDragging)
        {
            Vector2 mp = Input.mousePosition;
            mp.y = Screen.height - mp.y; // 转 GUI 坐标
            Vector2 newPos = mp - _dragOffset;
            newPos.x = Mathf.Clamp(newPos.x, 0, Screen.width - panelWidth);
            newPos.y = Mathf.Clamp(newPos.y, 0, Screen.height - panelHeight);
            _panelRect.x = newPos.x;
            _panelRect.y = newPos.y;
        }

        // 4. 订阅 AI 回复（用于形象跳跃反馈）+ 表情标记（用于徽章）
        if (!_mascotSubscribed && _chat != null)
        {
            _mascotSubscribed = true;
            _chat.OnNewReply += OnMascotReply;
            _chat.OnExpressionTag += OnMascotExpression;
        }

        // 4b. 表情徽章计时（到时清除）
        if (_mascotEmotionTimer > 0f)
        {
            _mascotEmotionTimer -= Time.deltaTime;
            if (_mascotEmotionTimer <= 0f) _mascotEmotion = "";
        }

        // 5. 像素形象眨眼（随机 3~5s 一次，闭合 0.12s）
        if (_mascotOpenTex != null)
        {
            if (_mascotBlinking)
            {
                _mascotBlinkT += Time.deltaTime;
                if (_mascotBlinkT >= 0.12f)
                {
                    _mascotBlinking = false;
                    _mascotBlinkTimer = UnityEngine.Random.Range(3f, 5f);
                }
            }
            else
            {
                _mascotBlinkTimer -= Time.deltaTime;
                if (_mascotBlinkTimer <= 0f)
                {
                    _mascotBlinking = true;
                    _mascotBlinkT = 0f;
                }
            }
        }
    }

    /// <summary>切换窗口开关</summary>
    public void Toggle()
    {
        _isOpen = !_isOpen;
        if (_isOpen)
        {
            _inputFocused = true; // 打开后自动聚焦输入框
        }
    }

    // ==================== 测试注入通道 ====================

    private float _nextInboxCheck = 0f;

    /// <summary>
    /// 测试收件箱：仅测试模式（D:\DesktopPetData\.test_mode 存在）启用。
    /// 外部测试脚本向 D:\DesktopPetData\inbox.txt 写入一行文字，
    /// 这里以 0.25s 间隔轮询，读到非空内容即处理，然后清空文件（保留文件避免反复触发）。
    /// 支持两种格式：
    ///   - 普通文本 → 作为用户消息调用 ChatManager.SendMessage 发送（走 LLM）
    ///   - @@emote:xxx → 测试表情注入，不走 LLM，直接左侧气泡 + 右上角表情徽章
    /// 发送后 HistoryCount +1 → Update 第 2 步自动 RebuildLog → 气泡直接可见。
    /// </summary>
    private void CheckTestInbox()
    {
        if (!ChatManager.IsTestMode) return;
        if (Time.time < _nextInboxCheck) return;
        _nextInboxCheck = Time.time + 0.25f;

        const string inboxPath = @"D:\DesktopPetData\inbox.txt";
        if (!System.IO.File.Exists(inboxPath)) return;

        string content;
        try { content = System.IO.File.ReadAllText(inboxPath).Trim(); }
        catch { return; }
        if (string.IsNullOrEmpty(content)) return;

        try { System.IO.File.WriteAllText(inboxPath, ""); }
        catch { return; }

        if (_chat == null) return;

        // ★ 测试表情注入：@@emote:happy → 不走 LLM，直接左侧气泡 + 徽章
        if (content.StartsWith("@@emote:"))
        {
            string emote = content.Substring("@@emote:".Length).Trim();
            if (!string.IsNullOrEmpty(emote))
            {
                _chat.InjectEmoteTest(emote);
                Debug.Log($"[TestInbox] 已注入表情: {emote}");
            }
            return;
        }

        _chat.SendMessage(content, null);
        Debug.Log($"[TestInbox] 已注入消息: {content}");
    }

    /// <summary>轮询全局热键 Shift+~（GetAsyncKeyState 物理按键，不依赖消息队列）</summary>
    private void CheckGlobalHotkey()
    {
        bool shiftDown = (GetAsyncKeyState(VK_LSHIFT) & KEY_DOWN) != 0
                      || (GetAsyncKeyState(VK_RSHIFT) & KEY_DOWN) != 0;
        bool tildeDown = (GetAsyncKeyState(VK_OEM_3) & KEY_DOWN) != 0;

        // 按下沿触发：Shift 按住时 ~ 从「未按下」→「按下」瞬间触发一次
        if (shiftDown && tildeDown && !_globalTildeWasDown && Time.frameCount != _hotkeyFrame)
        {
            _hotkeyFrame = Time.frameCount;
            Toggle();
        }
        _globalTildeWasDown = tildeDown;
    }

    /// <summary>关闭窗口</summary>
    public void Close()
    {
        _isOpen = false;
        _isDragging = false;
    }

    void OnGUI()
    {
        InitStyles();
        RefreshRefs();

        if (!_isOpen) return;

        float pw = _panelRect.width;        // 窗口宽度
        float ph = _panelRect.height;       // 窗口高度
        float px = _panelRect.x;            // 窗口 X
        float py = _panelRect.y;            // 窗口 Y
        Vector2 mp = Event.current.mousePosition; // GUI 坐标

        // ——— 面板背景 ———
        Rect bgRect = new Rect(px, py, pw, ph);
        GUI.Box(bgRect, GUIContent.none, _panelStyle);
        // 星空星点叠加（太卜司观星氛围）
        if (_starfieldTex != null)
            GUI.DrawTexture(bgRect, _starfieldTex, ScaleMode.StretchToFill);

        // ——— 像素边框（2px 紫色硬边，带四角强调） ———
        Color borderC = new Color(0.58f, 0.42f, 0.88f, 0.85f);
        Color borderDim = new Color(0.40f, 0.28f, 0.65f, 0.50f);
        // 上下边
        DrawPixelRect(new Rect(px, py, pw, 2f), borderC);
        DrawPixelRect(new Rect(px, py + ph - 2f, pw, 2f), borderC);
        // 左右边
        DrawPixelRect(new Rect(px, py, 2f, ph), borderC);
        DrawPixelRect(new Rect(px + pw - 2f, py, 2f, ph), borderC);
        // 四角 6×6 加粗强调
        DrawPixelRect(new Rect(px, py, 6f, 6f), borderC);
        DrawPixelRect(new Rect(px + pw - 6f, py, 6f, 6f), borderC);
        DrawPixelRect(new Rect(px, py + ph - 6f, 6f, 6f), borderC);
        DrawPixelRect(new Rect(px + pw - 6f, py + ph - 6f, 6f, 6f), borderC);

        // 四角云纹角饰（太卜司星纹，半透明叠加）
        Color ornamentA = new Color(1f, 1f, 1f, 0.35f);
        GUI.color = ornamentA;
        GUI.DrawTexture(new Rect(px + 8f, py + 8f, 30f, 30f), _ornamentTL);
        GUI.DrawTexture(new Rect(px + pw - 38f, py + 8f, 30f, 30f), _ornamentTR);
        GUI.DrawTexture(new Rect(px + 8f, py + ph - 38f, 30f, 30f), _ornamentBL);
        GUI.DrawTexture(new Rect(px + pw - 38f, py + ph - 38f, 30f, 30f), _ornamentBR);
        GUI.color = Color.white;

        // ═══════════════════════════════════════
        //  终端标题栏 — [像素符玄] 符玄@太卜司:~ + 状态 + 时间 + ✕
        // ═══════════════════════════════════════
        float titleH = 36f;
        Rect titleBarRect = new Rect(px + 2f, py + 2f, pw - 4f, titleH);
        // 像素渐变标题栏背景
        GUI.DrawTexture(titleBarRect, _titleBarPixelTex);
        // 标题栏底部分隔线
        DrawPixelRect(new Rect(px + 2f, py + 2f + titleH, pw - 4f, 1f), new Color(0.58f, 0.42f, 0.88f, 0.6f));

        // 符玄头像（标题栏左侧，30×30，带深色描边以增强对比）
        float fxHeadSize = 30f;
        Rect fxHeadRect = new Rect(px + 8f, py + 4f, fxHeadSize, fxHeadSize);
        // 描边（在头像下方画一层深色方形，让小头像在亮背景上更易识别）
        DrawPixelRect(new Rect(fxHeadRect.x - 2f, fxHeadRect.y - 2f, fxHeadRect.width + 4f, fxHeadRect.height + 4f), new Color(0f, 0f, 0f, 0.65f));
        GUI.DrawTexture(fxHeadRect, _pixelFxTex);

        bool waiting = _chat != null && _chat.IsWaiting;
        float statusPulse = waiting ? 0.55f + 0.45f * Mathf.Sin(Time.time * 3f) : 1f; // 思考中 → 紫色呼吸
        Color statusC = waiting
            ? new Color(0.72f, 0.55f, 0.95f, statusPulse)   // 思考中 → 紫
            : new Color(0.45f, 0.85f, 0.55f, 1f);  // 就绪 → 绿
        GUI.color = statusC;
        GUI.DrawTexture(new Rect(px + fxHeadSize + 12f, py + titleH / 2f - 3f, 7f, 7f), _statusDotTex);
        GUI.color = Color.white;

        // 卦象三爻装饰（金色，太卜司占卜符号）
        if (_hexagramTex != null)
            GUI.DrawTexture(new Rect(px + fxHeadSize + 24f, py + titleH / 2f - 7f, 14f, 14f), _hexagramTex);

        GUI.Label(new Rect(px + fxHeadSize + 46f, py + 3f, pw - 220f, 16f), "符玄@太卜司: ~", _termTitleStyle);
        GUI.Label(new Rect(px + fxHeadSize + 46f, py + 19f, pw - 220f, 14f),
            waiting ? "● 思考中…" : "● 就绪", _termStatusStyle);

        // ——— 字体档位按钮（时间左侧，点击循环 A → A2 → A3 → A4） ———
        Rect fontBtnRect = new Rect(px + pw - 138f, py + 4f, 32f, 28f);
        if (fontBtnRect.Contains(mp))
            DrawPixelRect(fontBtnRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        string fontLbl = _fontScaleLevel == 0 ? "A" : "A" + (_fontScaleLevel + 1);
        string fontTip = "字体大小: " + FONT_SCALES[_fontScaleLevel] + "x (档 " + (_fontScaleLevel + 1) + "/4)";
        if (GUI.Button(fontBtnRect, new GUIContent(fontLbl, fontTip), _termToolBtnStyle))
            CycleFontScale();

        // 时间（标题栏右，✕ 左侧）
        _timeRefreshTimer += Time.deltaTime;
        if (_timeRefreshTimer > 1f || string.IsNullOrEmpty(_timeDisplay))
        {
            _timeRefreshTimer = 0f;
            _timeDisplay = System.DateTime.Now.ToString("HH:mm");
        }
        GUI.Label(new Rect(px + pw - 90f, py + 8f, 44f, 16f), _timeDisplay, _termTimeStyle);

        // ——— ✕ 关闭按钮（右上角，像素方块风格） ———
        float closeSize = 24f + _fontScaleLevel * 2f;
        Rect closeRect = new Rect(px + pw - closeSize - 8f, py + 6f, closeSize, closeSize);
        // hover 时画红色方块背景
        if (closeRect.Contains(mp))
            DrawPixelRect(closeRect, new Color(0.80f, 0.25f, 0.25f, 0.35f));
        if (GUI.Button(closeRect, "✕", _closeBtnStyle))
        {
            Close();
        }

        // ——— 标题栏拖动（按住标题栏移动窗口，排除 ✕ 按钮区域防误触） ———
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0
            && titleBarRect.Contains(mp) && !closeRect.Contains(mp))
        {
            _isDragging = true;
            _dragOffset = mp - new Vector2(px, py);
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseUp)
        {
            _isDragging = false;
            _isResizing = false;
        }

        // ═══════════════════════════════════════
        //  工具行 — 终端式文本按钮 [聊] [设] [签] [告] [收]
        // ═══════════════════════════════════════
        float toolRowY = py + titleH + 8f;
        float toolBtnW = 48f;
        float toolBtnH = 26f + _fontScaleLevel * 3f;
        float toolBtnGap = 8f;
        float toolTotalW = _tools.Length * toolBtnW + (_tools.Length - 1) * toolBtnGap;
        float toolStartX = px + (pw - toolTotalW) / 2f;

        int hoveredTool = -1;
        for (int i = 0; i < _tools.Length; i++)
        {
            Rect tbRect = new Rect(toolStartX + i * (toolBtnW + toolBtnGap), toolRowY, toolBtnW, toolBtnH);
            bool tbHover = bgRect.Contains(mp) && tbRect.Contains(mp);
            if (tbHover) hoveredTool = i;
            // hover 时画淡紫方块背景
            if (tbHover)
                DrawPixelRect(tbRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
            // 底部 1px 强调线
            DrawPixelRect(new Rect(tbRect.x, tbRect.yMax - 1f, tbRect.width, 1f),
                tbHover ? new Color(0.66f, 0.50f, 0.95f, 0.8f) : new Color(0.40f, 0.28f, 0.65f, 0.3f));
            if (GUI.Button(tbRect, "[" + _tools[i].icon + "]", tbHover ? _termToolBtnHoverStyle : _termToolBtnStyle))
            {
                var tool = _tools[i];
                if (tool.panelType.HasValue && _ballPanel != null)
                {
                    Vector2 panelPos = new Vector2(px - 440f, py + 40f);
                    _ballPanel.ShowPanel(tool.panelType.Value, panelPos);
                }
                else if (tool.label == "聊天")
                {
                    _inputFocused = true;
                    GUI.FocusControl("rightPanelInput");
                }
                else if (tool.label == "收纳")
                {
                    LaunchPogget();
                }
            }
        }

        // ═══════════════════════════════════════
        //  终端日志滚动区
        // ═══════════════════════════════════════
        float logY = toolRowY + toolBtnH + 8f;
        float logH = ph - (logY - py) - inputBarHeight - 14f;
        if (logH < 40f) logH = 40f;

        float logViewW = pw - 16f;
        float maxBubbleW = logViewW * 0.72f;   // 气泡最大宽度
        float avatarSize = 24f + _fontScaleLevel * 2f;  // 头像尺寸（随档位略增）
        Rect logView = new Rect(px + 8f, logY, logViewW, logH);

        // 日志区背景（略深于面板）
        DrawPixelRect(logView, new Color(0.05f, 0.04f, 0.09f, 0.5f));

        // 第一遍：测量内容总高度（气泡自动换行）
        float totalH = 8f;
        for (int i = 0; i < _logLines.Count; i++)
        {
            var ln = _logLines[i];
            if (ln.kind == 2)
            {
                totalH += _termLogDimStyle.CalcHeight(new GUIContent(ln.text), logViewW - 40f) + 8f;
            }
            else
            {
                GUIStyle bubble = ln.kind == 1 ? _bubbleUserStyle : _bubbleFxStyle;
                float naturalW = bubble.CalcSize(new GUIContent(ln.text)).x;
                float bubbleW = Mathf.Min(naturalW, maxBubbleW);
                totalH += Mathf.Max(CalcBubbleHeight(bubble, ln.text, bubbleW, naturalW), avatarSize) + 8f;
            }
        }
        if (waiting) totalH += 24f;
        // 右下角动态形象占位：内容底部预留形象高度+间距，消息滚到底时停在形象上方不被遮挡
        float mascotReserve = 0f;
        if (_mascotOpenTex != null && logH > 130f)
            mascotReserve = 24f * MASCOT_UPSCALE + 24f;
        Rect content = new Rect(0f, 0f, logViewW, Mathf.Max(totalH + mascotReserve, logH));

        _logScroll = GUI.BeginScrollView(logView, _logScroll, content, false, false, _invisibleScrollbar, _invisibleScrollbar);

        // 第二遍：QQ 式左右对话气泡 —— 符玄=左紫气泡，用户=右蓝气泡，系统=居中灰字
        float yCursor = 8f;
        for (int i = 0; i < _logLines.Count; i++)
        {
            var ln = _logLines[i];
            if (ln.kind == 2)
            {
                // 系统/工具消息：居中灰字（无气泡）
                float sysW = logViewW - 40f;
                float sysH = _termLogDimStyle.CalcHeight(new GUIContent(ln.text), sysW);
                GUI.Label(new Rect((logViewW - sysW) / 2f, yCursor, sysW, sysH), ln.text, _termLogDimStyle);
                yCursor += sysH + 8f;
                continue;
            }
            bool isUser = ln.kind == 1;
            GUIStyle bubble = isUser ? _bubbleUserStyle : _bubbleFxStyle;
            // 气泡宽度：短消息贴合文字，长消息自动换行封顶
            float naturalW = bubble.CalcSize(new GUIContent(ln.text)).x;
            float bubbleW = Mathf.Min(naturalW, maxBubbleW);
            float bubbleH = CalcBubbleHeight(bubble, ln.text, bubbleW, naturalW);
            Rect bubbleRect, avatarRect;
            if (isUser)
            {
                // 用户消息：靠右，头像在气泡右侧
                avatarRect = new Rect(logViewW - 8f - avatarSize, yCursor + 2f, avatarSize, avatarSize);
                bubbleRect = new Rect(avatarRect.x - 8f - bubbleW, yCursor, bubbleW, bubbleH);
                GUI.DrawTexture(avatarRect, _userAvatarTex);
                // ★ 中文基线修正：雅黑等字体的 MiddleCenter 对中文视觉中心偏左下，
                //   按头像尺寸比例向右上补偿（实测 26px 头像约偏 -3px x / +5px y）
                float avatarTextOffX = avatarSize * 0.12f;
                float avatarTextOffY = -avatarSize * 0.20f;
                GUI.Label(
                    new Rect(avatarRect.x + avatarTextOffX, avatarRect.y + avatarTextOffY,
                             avatarRect.width, avatarRect.height),
                    "我", _userAvatarStyle);
            }
            else
            {
                // 符玄消息：靠左，头像在气泡左侧
                avatarRect = new Rect(8f, yCursor + 2f, avatarSize, avatarSize);
                bubbleRect = new Rect(avatarRect.xMax + 8f, yCursor, bubbleW, bubbleH);
                GUI.DrawTexture(avatarRect, _pixelFxTex);
            }
            GUI.Label(bubbleRect, ln.text, bubble);
            yCursor += Mathf.Max(bubbleH, avatarSize) + 8f;
        }
        if (waiting)
        {
            GUI.Label(new Rect(20f, yCursor, logViewW - 40f, 20f), "● 思考中…", _termLogDimStyle);
        }

        GUI.EndScrollView();

        // ——— 像素符玄动态形象（右下角浮层，QQ 动态形象风格） ———
        // 呼吸浮动 + 点击/AI回复时跳跃 + 眨眼
        if (_mascotOpenTex != null && _mascotBlinkTex != null && logH > 130f)
        {
            float mw = 17f * MASCOT_UPSCALE;
            float mh = 24f * MASCOT_UPSCALE;
            float breath = Mathf.Sin(Time.time * 2.2f) * 2f;          // 呼吸 ±2px
            float jump = 0f;
            float jumpAge = Time.time - _mascotJumpStart;
            if (jumpAge >= 0f && jumpAge < 0.45f)
                jump = 26f * Mathf.Sin(Mathf.PI * (jumpAge / 0.45f)); // 跳跃 26px
            Rect mascotRect = new Rect(logView.xMax - mw - 14f, logView.yMax - mh - 10f + breath + jump, mw, mh);
            // 点击互动：戳戳额头 → 触发聊天 + 跳一下
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && mascotRect.Contains(mp))
            {
                _mascotJumpStart = Time.time;
                if (_chat != null) _chat.SendMessage("*你伸出手指，轻轻戳了戳符玄的额头*", null);
                Event.current.Use();
            }
            // 地面小阴影（跳跃时缩小）
            float shadowScale = (jumpAge >= 0f && jumpAge < 0.45f) ? 0.5f : 1f;
            GUI.color = new Color(0f, 0f, 0f, 0.35f * shadowScale);
            GUI.DrawTexture(new Rect(mascotRect.x + 8f, logView.yMax - 6f, mw - 16f, 4f), _whiteTex);
            GUI.color = Color.white;
            // 形象本体（眨眼时切换闭眼帧）
            GUI.DrawTexture(mascotRect, _mascotBlinking ? _mascotBlinkTex : _mascotOpenTex);
            // 表情差分徽章（右上角：AI 回复【表情:xxx】时弹出对应符号，4 秒后消失）
            if (!string.IsNullOrEmpty(_mascotEmotion))
            {
                Texture2D emblem = GetEmblemTex(_mascotEmotion);
                if (emblem != null)
                {
                    float bs = EMBLEM_SIZE;
                    Rect badgeBg = new Rect(mascotRect.xMax - bs - 5f, mascotRect.y - 5f, bs, bs);
                    DrawPixelRect(badgeBg, new Color(0f, 0f, 0f, 0.78f)); // 黑色圆角底
                    GUI.DrawTexture(badgeBg, emblem);
                }
            }
        }

        // CRT 扫描线叠加（在日志区上方，半透明）
        Color prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.5f);
        Rect scanRect = new Rect(logView.x, logView.y, logView.width, logView.height);
        Rect scanUV = new Rect(0f, 0f, 1f, logView.height / 4f);
        GUI.DrawTextureWithTexCoords(scanRect, _scanlineTex, scanUV);
        GUI.color = prevColor;

        // 新增日志 → 自动滚到底
        if (_pendingAutoScroll)
        {
            _pendingAutoScroll = false;
            _logScroll.y = float.MaxValue;
        }

        // ═══════════════════════════════════════
        //  底部终端输入行 — [像素符玄] > [输入框] (→)
        // ═══════════════════════════════════════
        float inputY = py + ph - inputBarHeight - 6f;
        float inputX = px + 8f;
        float inputW = pw - 16f;

        // 输入栏背景 + 顶部分隔线
        Rect inputBarBgRect = new Rect(px + 2f, inputY - 4f, pw - 4f, inputBarHeight + 10f);
        GUI.DrawTexture(inputBarBgRect, _inputBarPixelTex);
        DrawPixelRect(new Rect(px + 2f, inputY - 4f, pw - 4f, 1f), new Color(0.58f, 0.42f, 0.88f, 0.5f));

        // 符玄头像（输入框内最左，高清原图）★多模态资源：Resources/PixelFuXuan.png
        float fxSize = 56f; // 高清原图平滑显示
        Rect fxRect = new Rect(inputX + 4f, inputY + (inputBarHeight - fxSize) / 2f, fxSize, fxSize);
        // 背景描边
        DrawPixelRect(new Rect(fxRect.x - 3f, fxRect.y - 3f, fxRect.width + 6f, fxRect.height + 6f), new Color(0f, 0f, 0f, 0.7f));
        GUI.DrawTexture(fxRect, _pixelFxTex);

        // > 提示符
        float promptW = 16f;
        float tfH = 28f + _fontScaleLevel * 3f;
        float tfY = inputY + (inputBarHeight - tfH) / 2f;
        GUI.Label(new Rect(inputX + fxSize + 8f, tfY, promptW, tfH), ">", _termPromptStyle);

        // 输入框（透明背景，文字直接绘在输入条上）
        float sendBtnSize = 30f;
        float tfX = inputX + fxSize + promptW + 12f;
        float tfW = inputW - fxSize - promptW - 18f - sendBtnSize - 6f;
        Rect inputBgRect = new Rect(tfX, tfY, tfW, tfH);

        // 输入框背景（圆角胶囊 + 发光描边）
        GUI.DrawTexture(inputBgRect, _inputBgTex);
        GUI.DrawTexture(inputBgRect, _inputGlowTex);

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

        _inputText = GUI.TextField(inputBgRect, _inputText, MAX_INPUT_LENGTH, _termInputStyle);

        // ——— 发送按钮（太极图，符玄道法风，hover 紫色光晕） ———
        Rect sendBtnRect = new Rect(tfX + tfW + 6f, inputY + (inputBarHeight - sendBtnSize) / 2f, sendBtnSize, sendBtnSize);
        bool sendHover = sendBtnRect.Contains(Event.current.mousePosition);
        if (_taijiTex != null)
        {
            GUI.DrawTexture(sendBtnRect, _taijiTex);
            if (sendHover) GUI.DrawTexture(sendBtnRect, _glowTex); // 紫色光晕提亮
        }
        else
        {
            // 回退：像素方块背景
            DrawPixelRect(sendBtnRect, sendHover
                ? new Color(0.66f, 0.50f, 0.95f, 0.5f)
                : new Color(0.50f, 0.35f, 0.80f, 0.25f));
        }
        // 边框
        DrawPixelRect(new Rect(sendBtnRect.x, sendBtnRect.y, sendBtnRect.width, 1f), new Color(0.58f, 0.42f, 0.88f, 0.6f));
        DrawPixelRect(new Rect(sendBtnRect.x, sendBtnRect.yMax - 1f, sendBtnRect.width, 1f), new Color(0.58f, 0.42f, 0.88f, 0.6f));
        DrawPixelRect(new Rect(sendBtnRect.x, sendBtnRect.y, 1f, sendBtnRect.height), new Color(0.58f, 0.42f, 0.88f, 0.6f));
        DrawPixelRect(new Rect(sendBtnRect.xMax - 1f, sendBtnRect.y, 1f, sendBtnRect.height), new Color(0.58f, 0.42f, 0.88f, 0.6f));
        if (GUI.Button(sendBtnRect, new GUIContent(" ", "发送 (Enter)"), _sendBtnStyle))
        {
            string sendMsg = _inputText.Trim();
            if (sendMsg.Length > 0)
            {
                _inputText = "";
                if (_chat != null)
                    _chat.SendMessage(sendMsg, null);
                _inputFocused = true; // 发送后保持聚焦
            }
        }

        // 空输入框提示
        if (string.IsNullOrEmpty(_inputText) && GUI.GetNameOfFocusedControl() != "rightPanelInput")
        {
            GUI.Label(inputBgRect, "向符玄下达指令…", _termPlaceholderStyle);
        }

        // 聚焦请求
        if (_inputFocused)
        {
            _inputFocused = false;
            GUI.FocusControl("rightPanelInput");
        }

        // ——— 右下角拉伸手柄（可调窗口大小，QQ 式） ———
        float handleSize = 20f;
        Rect resizeRect = new Rect(px + pw - handleSize, py + ph - handleSize, handleSize, handleSize);
        bool resizeHover = resizeRect.Contains(mp);
        // 手柄视觉：三条斜线
        float hc = resizeHover ? 1f : 0.6f;
        DrawPixelRect(new Rect(px + pw - 16f, py + ph - 6f, 9f, 1f), new Color(0.66f, 0.50f, 0.95f, hc));
        DrawPixelRect(new Rect(px + pw - 21f, py + ph - 11f, 9f, 1f), new Color(0.66f, 0.50f, 0.95f, hc * 0.8f));
        DrawPixelRect(new Rect(px + pw - 26f, py + ph - 16f, 9f, 1f), new Color(0.66f, 0.50f, 0.95f, hc * 0.6f));

        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && resizeRect.Contains(mp))
        {
            _isResizing = true;
            Event.current.Use();
        }
        if (_isResizing && Event.current.type == EventType.MouseDrag)
        {
            float newW = Mathf.Clamp(Event.current.mousePosition.x - px, MIN_PANEL_W, MAX_PANEL_W);
            float newH = Mathf.Clamp(Event.current.mousePosition.y - py, MIN_PANEL_H, MAX_PANEL_H);
            _panelRect.width = newW;
            _panelRect.height = newH;
            panelWidth = newW;
            panelHeight = newH;
            Event.current.Use();
        }

        // ——— 窗口内点击 → 防穿透 ———
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if (bgRect.Contains(mp))
            {
                bool onButton = hoveredTool >= 0;
                bool onInput = inputBgRect.Contains(mp) || fxRect.Contains(mp) || sendBtnRect.Contains(mp);
                if (!onButton && !onInput && !_isDragging)
                {
                    Event.current.Use();
                    _inputFocused = true;
                }
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

        // ——— 面板背景 ——— 太卜司星空（深紫黑→藏蓝渐变），叠加星点
        _bgTex = MakeGradientTex(64, 64,
            new Color(0.11f, 0.08f, 0.18f, 0.88f),
            new Color(0.05f, 0.05f, 0.12f, 0.88f), true);
        _panelStyle = new GUIStyle { normal = { background = _bgTex } };
        // 星空星点 + 太极发送按钮 + 卦象装饰
        _starfieldTex = MakeStarfieldTex(96, 96, 70);
        _taijiTex = MakeTaijiTex(30);
        _hexagramTex = GenHexagramTex(12, 12, new Color(0.92f, 0.82f, 0.56f, 0.92f));

        // ——— 顶部装饰线（紫） ———
        _accentLineTex = MakeTex(1, 1, cAccent);

        // ——— 分隔线 ———
        _separatorTex = MakeTex(1, 1, new Color(0.45f, 0.35f, 0.65f, 0.25f));
        _separatorStyle = new GUIStyle { normal = { background = _separatorTex } };

        // ——— 输入框背景 ——— 圆角胶囊（复刻 ChatBubble 圆角风格，替代直角 1×1）
        _inputBgTex = GenRoundedRect(64, 48, 14, new Color(0.22f, 0.16f, 0.35f, 0.80f));
        _inputHoverBgTex = GenRoundedRect(64, 48, 14, new Color(0.32f, 0.24f, 0.48f, 0.90f));
        _inputGlowTex = GenGlowRoundedRect(64, 48, 14, new Color(0.72f, 0.55f, 0.95f, 0.9f));
        _inputStyle = new GUIStyle
        {
            normal = { textColor = cTextMain, background = _inputBgTex },
            hover = { textColor = cTextMain, background = _inputHoverBgTex },
            focused = { textColor = Color.white, background = _inputHoverBgTex },
            fontSize = 15,
            padding = new RectOffset(16, 14, 8, 8),
            alignment = TextAnchor.MiddleLeft,
            border = new RectOffset(14, 14, 14, 14),
            margin = new RectOffset(0, 0, 0, 0),
            stretchHeight = true
        };

        // ——— 输入框占位提示 ——— 淡紫白色（保留旧样式引用）
        _placeholderStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.72f, 0.68f, 0.82f, 0.9f) },
            fontSize = 15,
            padding = new RectOffset(14, 10, 8, 8),
            alignment = TextAnchor.MiddleLeft
        };

        // ═══════════════════════════════════════
        //  终端风格（OpenClaw 式）— 深色底 + 日志滚动区 + 命令行输入
        // ═══════════════════════════════════════
        if (_monoFont == null)
            _monoFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "SimSun" }, 15);

        _titleBarStyle = new GUIStyle
        {
            normal = { background = MakeTex(1, 1, new Color(0.11f, 0.09f, 0.15f, 0.92f)) }
        };

        _termTitleStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.90f, 0.80f, 0.58f, 1f) },  // 太卜司金
            alignment = TextAnchor.MiddleLeft
        };
        _termStatusStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 12,
            normal = { textColor = new Color(0.58f, 0.55f, 0.65f, 0.9f) },
            alignment = TextAnchor.MiddleLeft
        };
        _termTimeStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 13,
            normal = { textColor = new Color(0.58f, 0.55f, 0.65f, 0.9f) },
            alignment = TextAnchor.MiddleRight
        };
        _termToolBtnStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14,
            normal = { textColor = new Color(0.66f, 0.62f, 0.76f, 0.9f) },
            hover = { textColor = new Color(0.75f, 0.62f, 0.98f, 1f) },
            alignment = TextAnchor.MiddleCenter
        };
        _termToolBtnHoverStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.80f, 0.68f, 1.00f, 1f) },
            hover = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter
        };
        _termLogStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14,
            normal = { textColor = new Color(0.80f, 0.72f, 0.95f, 1f) },
            alignment = TextAnchor.UpperLeft
        };
        _termLogUserStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14,
            normal = { textColor = new Color(0.80f, 0.90f, 0.98f, 1f) },
            alignment = TextAnchor.UpperLeft
        };
        _termLogDimStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, wordWrap = true,
            normal = { textColor = new Color(0.55f, 0.54f, 0.60f, 0.9f) },
            alignment = TextAnchor.UpperLeft
        };
        _termPromptStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 17, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.62f, 0.48f, 0.95f, 1f) },
            alignment = TextAnchor.MiddleLeft
        };
        _termInputStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 15,
            normal = { textColor = Color.white },
            focused = { textColor = Color.white },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 6, 4, 4),
            clipping = TextClipping.Clip
        };
        _termPlaceholderStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14,
            normal = { textColor = new Color(0.55f, 0.52f, 0.62f, 0.85f) },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 6, 4, 4)
        };

        // ——— QQ 式对话气泡样式 ——— 符玄左紫(渐变+金边)、用户右蓝(渐变+浅蓝边)
        _bubbleFxTex = GenBubbleTex(64, 48, 10,
            new Color(0.45f, 0.33f, 0.62f, 0.96f), new Color(0.30f, 0.20f, 0.46f, 0.96f),
            new Color(0.88f, 0.78f, 0.55f, 0.95f));
        _bubbleUserTex = GenBubbleTex(64, 48, 10,
            new Color(0.24f, 0.42f, 0.60f, 0.96f), new Color(0.14f, 0.28f, 0.44f, 0.96f),
            new Color(0.55f, 0.72f, 0.95f, 0.9f));
        _bubbleFxStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, wordWrap = true,
            normal = { background = _bubbleFxTex, textColor = new Color(0.90f, 0.85f, 0.99f, 1f) },
            padding = new RectOffset(10, 10, 8, 8),
            border = new RectOffset(10, 10, 10, 10)
        };
        _bubbleUserStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, wordWrap = true,
            normal = { background = _bubbleUserTex, textColor = new Color(0.85f, 0.92f, 0.99f, 1f) },
            padding = new RectOffset(10, 10, 8, 8),
            border = new RectOffset(10, 10, 10, 10)
        };
        _userAvatarTex = GenRoundedRect(24, 24, 8, new Color(0.30f, 0.24f, 0.45f, 0.95f));
        _userAvatarStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 11, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.85f, 0.80f, 0.98f, 1f) }
        };
        _inputBarBgStyle = new GUIStyle
        {
            normal = { background = MakeTex(1, 1, new Color(0.09f, 0.08f, 0.13f, 0.78f)) }
        };
        _statusDotTex = MakeCircleTex(8, Color.white);
        _pixelFxTex = LoadPixelFx(); // ★多模态：优先加载 Resources/PixelFuXuan.png，回退代码生成
        // 像素符玄动态形象（17x24 网格图，睁眼/闭眼两帧，×4 放大）
        _mascotOpenTex = LoadMascot(true);
        _mascotBlinkTex = LoadMascot(false);
        _invisibleScrollbar = new GUIStyle();
        _closeBtnStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.75f, 0.70f, 0.82f, 0.85f) },
            hover = { textColor = new Color(1f, 0.45f, 0.45f, 1f) },
            active = { textColor = Color.white },
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };

        // ——— 发送按钮 ——— 圆形紫色（hover 提亮）
        _sendBtnTex = MakeCircleTex(30, new Color(0.55f, 0.40f, 0.85f, 0.30f));
        _sendBtnHoverTex = MakeCircleTex(30, new Color(0.66f, 0.50f, 0.95f, 0.65f));
        _sendBtnStyle = new GUIStyle
        {
            normal = { background = _sendBtnTex, textColor = new Color(0.85f, 0.80f, 0.95f, 0.8f) },
            hover = { background = _sendBtnHoverTex, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };
        _sendBtnHoverStyle = new GUIStyle
        {
            normal = { background = _sendBtnHoverTex, textColor = Color.white },
            hover = { background = _sendBtnHoverTex, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
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

        // ═══════════════════════════════════════
        //  像素终端增强纹理
        // ═══════════════════════════════════════

        // CRT 扫描线（每隔 2 像素一条半透明暗线，模拟老显示器）
        _scanlineTex = new Texture2D(1, 4, TextureFormat.ARGB32, false);
        _scanlineTex.wrapMode = TextureWrapMode.Repeat;
        _scanlineTex.SetPixel(0, 0, new Color(0, 0, 0, 0.12f));
        _scanlineTex.SetPixel(0, 1, new Color(0, 0, 0, 0f));
        _scanlineTex.SetPixel(0, 2, new Color(0, 0, 0, 0.12f));
        _scanlineTex.SetPixel(0, 3, new Color(0, 0, 0, 0f));
        _scanlineTex.Apply();

        // 像素边框（纯色 2px 硬边，带轻微发光）
        _borderTex = MakeTex(2, 2, new Color(0.58f, 0.42f, 0.88f, 0.9f));

        // 日志交替行背景（极淡紫，提升可读性）
        _logRowAltTex = MakeTex(1, 1, new Color(0.14f, 0.10f, 0.22f, 0.35f));

        // 标题栏像素渐变（上深下浅，3 级台阶模拟像素色带）
        _titleBarPixelTex = new Texture2D(1, 3, TextureFormat.ARGB32, false);
        _titleBarPixelTex.wrapMode = TextureWrapMode.Clamp;
        _titleBarPixelTex.SetPixel(0, 0, new Color(0.14f, 0.10f, 0.20f, 0.95f));
        _titleBarPixelTex.SetPixel(0, 1, new Color(0.11f, 0.08f, 0.16f, 0.95f));
        _titleBarPixelTex.SetPixel(0, 2, new Color(0.08f, 0.06f, 0.13f, 0.95f));
        _titleBarPixelTex.Apply();

        // 输入栏像素背景（略深，与日志区区分）
        _inputBarPixelTex = MakeTex(1, 1, new Color(0.10f, 0.07f, 0.16f, 0.90f));

        // ——— 采集所有样式的基准字号（仅首次），再按档位统一缩放 ———
        if (_baseFontSizes.Count == 0)
        {
            foreach (var f in GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                if (typeof(GUIStyle).IsAssignableFrom(f.FieldType))
                {
                    var st = f.GetValue(this) as GUIStyle;
                    if (st != null) _baseFontSizes[st] = st.fontSize;
                }
            }
        }
        ApplyFontScale();
    }

    // ==================== 字体档位 ====================

    /// <summary>按当前档位统一缩放所有已采集样式的字号</summary>
    private void ApplyFontScale()
    {
        float s = FONT_SCALES[_fontScaleLevel];
        foreach (var kv in _baseFontSizes)
            kv.Key.fontSize = Mathf.RoundToInt(kv.Value * s);
    }

    /// <summary>循环切换字体档位（A → A2 → A3 → A4 → A）</summary>
    private void CycleFontScale()
    {
        _fontScaleLevel = (_fontScaleLevel + 1) % FONT_SCALES.Length;
        ApplyFontScale();
        PlayerPrefs.SetInt("RightPanelFontScale", _fontScaleLevel);
        Debug.Log($"[RightPanel] 字体档位 → {_fontScaleLevel + 1}/{FONT_SCALES.Length} ({FONT_SCALES[_fontScaleLevel]}x)");
    }

    // ==================== 工具方法 ====================

    /// <summary>绘制实心像素矩形（用于边框、分隔线、按钮背景）</summary>
    private static void DrawPixelRect(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
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

    /// <summary>生成圆角矩形纹理（复刻 ChatBubble 风格，用于输入框胶囊背景）</summary>
    private static Texture2D GenRoundedRect(int w, int h, float r, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color t = new Color(0, 0, 0, 0);
        float r2 = r * r;
        float rw = w - r - 1;
        float rh = h - r - 1;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool draw;
                if (x < r && y < r)
                    draw = (x - r + 0.5f) * (x - r + 0.5f) + (y - r + 0.5f) * (y - r + 0.5f) <= r2;
                else if (x > rw && y < r)
                    draw = (x - rw - 0.5f) * (x - rw - 0.5f) + (y - r + 0.5f) * (y - r + 0.5f) <= r2;
                else if (x < r && y > rh)
                    draw = (x - r + 0.5f) * (x - r + 0.5f) + (y - rh - 0.5f) * (y - rh - 0.5f) <= r2;
                else if (x > rw && y > rh)
                    draw = (x - rw - 0.5f) * (x - rw - 0.5f) + (y - rh - 0.5f) * (y - rh - 0.5f) <= r2;
                else
                    draw = true;

                tex.SetPixel(x, y, draw ? c : t);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成圆角发光描边（SDF 边缘紫光，内部透明，叠在输入框背景上）</summary>
    private static Texture2D GenGlowRoundedRect(int w, int h, float r, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color t = new Color(0, 0, 0, 0);
        float hw = (w - 1f) / 2f;
        float hh = (h - 1f) / 2f;
        float rr = Mathf.Max(r - 1f, 0.5f); // 内缩一点，让发光带居中在边缘

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 圆角矩形 SDF（有符号距离，负=内部）
                float qx = Mathf.Abs(x - hw) - (hw - rr);
                float qy = Mathf.Abs(y - hh) - (hh - rr);
                float ax = Mathf.Max(qx, 0f);
                float ay = Mathf.Max(qy, 0f);
                float dist = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rr;

                if (dist <= 0f)
                {
                    tex.SetPixel(x, y, t); // 内部透明，透出圆角背景
                }
                else
                {
                    // 边缘 3px 紫色发光带：平滑渐弱
                    float a = Mathf.Clamp01(1f - (dist - 1f) / 3f);
                    a = a * a * c.a;
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
                }
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

    /// <summary>生成星空纹理：透明底上随机散布星点（白色/金色/紫色）</summary>
    private static Texture2D MakeStarfieldTex(int w, int h, int starCount)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);
        // 确定性随机（固定种子，避免每次生成不同星空闪烁）
        var rng = new System.Random(42);
        for (int i = 0; i < starCount; i++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float a = 0.25f + (float)rng.NextDouble() * 0.65f;
            int t = rng.Next(3);
            Color c = t == 0 ? new Color(1f, 1f, 1f, a)
                    : t == 1 ? new Color(0.95f, 0.85f, 0.60f, a)   // 金色星
                             : new Color(0.75f, 0.60f, 1f, a);     // 紫色星
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成太极图（黑白双鱼，发送按钮）</summary>
    private static Texture2D MakeTaijiTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float c = (size - 1) / 2f;
        float R = c - 0.5f;          // 外圆半径
        float r = R / 2f;            // 鱼身小圆半径
        float eye = r / 2.6f;        // 鱼眼半径
        Color black = new Color(0.13f, 0.10f, 0.17f, 0.96f);
        Color white = new Color(0.93f, 0.89f, 0.98f, 0.96f);
        Color clear = new Color(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                if (dx * dx + dy * dy > R * R) { tex.SetPixel(x, y, clear); continue; }
                Color col = (x < c) ? black : white;   // 左黑右白
                // 右上白鱼（圆心 (c, c-r)），内含黑眼
                float d1x = x - c, d1y = y - (c - r);
                if (d1x * d1x + d1y * d1y <= r * r) col = white;
                if (d1x * d1x + d1y * d1y <= eye * eye) col = black;
                // 左下黑鱼（圆心 (c, c+r)），内含白眼
                float d2x = x - c, d2y = y - (c + r);
                if (d2x * d2x + d2y * d2y <= r * r) col = black;
                if (d2x * d2x + d2y * d2y <= eye * eye) col = white;
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成卦象三爻（☰ 形，三横线，标题栏金色装饰）</summary>
    private static Texture2D GenHexagramTex(int w, int h, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        int barH = Mathf.Max(h / 10, 1);      // 每爻粗细
        int gap = Mathf.Max((h - 3 * barH) / 2, 1); // 爻间缝隙
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);
        for (int i = 0; i < 3; i++)
        {
            int y0 = i * (barH + gap);
            for (int y = y0; y < y0 + barH; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成带细边的圆角气泡纹理（SDF 圆角矩形，内部渐变 + 边框色）</summary>
    private static Texture2D GenBubbleTex(int w, int h, float r, Color fillTop, Color fillBottom, Color border)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        float hw = (w - 1f) / 2f, hh = (h - 1f) / 2f;
        float rr = Mathf.Max(r - 1f, 0.5f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float qx = Mathf.Abs(x - hw) - (hw - rr);
                float qy = Mathf.Abs(y - hh) - (hh - rr);
                float ax = Mathf.Max(qx, 0f), ay = Mathf.Max(qy, 0f);
                float dist = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rr;
                if (dist > 0f) { tex.SetPixel(x, y, clear); continue; }
                float ty = y / (float)(h - 1);
                Color col = Color.Lerp(fillTop, fillBottom, ty);
                // 边框带：距边缘 2px 内渐变过渡到边框色
                if (dist > -2f)
                    col = Color.Lerp(border, col, Mathf.Clamp01((dist + 2f) / 1.2f));
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 加载符玄头像（高清原图，平滑显示）
    /// ★多模态：优先加载 Resources/PixelFuXuan.png（透明背景高清立绘）
    /// 未找到时回退到代码生成的占位像素画。
    /// </summary>
    private static Texture2D LoadPixelFx()
    {
        var loaded = Resources.Load<Texture2D>("PixelFuXuan");
        if (loaded != null)
        {
            loaded.filterMode = FilterMode.Bilinear; // 高清原图平滑缩放
            Debug.Log("[RightPanel] 已加载多模态生成的符玄头像: Resources/PixelFuXuan");
            return loaded;
        }
        Debug.LogWarning("[RightPanel] 未找到 Resources/PixelFuXuan.png，使用代码生成占位像素画。可用多模态模型生成后替换。");
        return GenPixelFx(2);
    }

    /// <summary>AI 回复到达 → 形象跳一下</summary>
    private void OnMascotReply(string reply)
    {
        _mascotJumpStart = Time.time;
    }

    /// <summary>AI 回复解析出表情标记 → 右上角显示对应符号徽章（4 秒）</summary>
    private void OnMascotExpression(string expName)
    {
        _mascotEmotion = expName;
        _mascotEmotionTimer = EMOTION_SHOW_TIME;
    }

    /// <summary>表情名 → 符号徽章纹理（8x8 点阵，惰性生成缓存）</summary>
    private Texture2D GetEmblemTex(string expName)
    {
        string key;
        switch (expName)
        {
            case "angry": key = "angry"; break;                 // 生气 → 红色感叹号
            case "confused": key = "confused"; break;          // 困惑 → 黄色问号
            case "happy": case "love": case "blush": key = "happy"; break; // 开心/爱/害羞 → 粉色爱心
            case "sleepy": key = "sleepy"; break;              // 困倦 → 蓝色 Z
            case "sad": case "tear": key = "sad"; break;     // 伤心/哭腔 → 灰色三点
            case "surprise": key = "surprise"; break;         // 惊讶 → 双感叹号
            default: return null;                                // neutral/calm/未知 → 无徽章
        }
        Texture2D cached;
        if (_emblemTex.TryGetValue(key, out cached)) return cached;
        Color c;
        string[] rows;
        switch (key)
        {
            case "angry":
                c = new Color(1f, 0.30f, 0.30f, 1f);
                rows = new[] {
                    "..##..",
                    "..##..",
                    "..##..",
                    "..##..",
                    "..##..",
                    "......",
                    "..##..",
                    "..##.." };
                break;
            case "confused":
                c = new Color(1f, 0.83f, 0.30f, 1f);
                rows = new[] {
                    ".####.",
                    "##..##",
                    "....##",
                    "...##.",
                    "..##..",
                    "......",
                    "..##..",
                    "......" };
                break;
            case "happy":
                c = new Color(1f, 0.55f, 0.75f, 1f);
                rows = new[] {
                    "##..##",
                    "######",
                    "######",
                    ".####.",
                    "..##..",
                    "...#..",
                    "......",
                    "......" };
                break;
            case "sleepy":
                c = new Color(0.45f, 0.65f, 1f, 1f);
                rows = new[] {
                    "######",
                    "....##",
                    "...##.",
                    "..##..",
                    ".##...",
                    "##....",
                    "######",
                    "......" };
                break;
            case "sad":
                c = new Color(0.65f, 0.65f, 0.72f, 1f);
                rows = new[] {
                    "......",
                    "......",
                    "......",
                    "......",
                    "......",
                    "......",
                    "#.#.#.",
                    "......" };
                break;
            default: // surprise 双感叹号（细主干+底部点，避免被认成竖线 ||）
                c = new Color(1f, 0.55f, 0.20f, 1f);
                rows = new[] {
                    ".#..#.",
                    ".#..#.",
                    ".#..#.",
                    ".#..#.",
                    ".#..#.",
                    "......",
                    ".#..#.",
                    "......" };
                break;
        }
        var tex = new Texture2D(rows[0].Length, rows.Length, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                tex.SetPixel(x, y, rows[y][x] == '#' ? c : Color.clear);
        tex.Apply();
        _emblemTex[key] = tex;
        return tex;
    }

    /// <summary>加载 17x24 像素符玄并放大为动态形象（×4，Point 锐利）；闭眼帧程序生成（眼睛行替换为肤色+闭眼缝线）</summary>
    private static Texture2D LoadMascot(bool openEyes)
    {
        var src = Resources.Load<Texture2D>("PixelFuXuan_17x24");
        if (src == null)
        {
            Debug.LogWarning("[RightPanel] 未找到 Resources/PixelFuXuan_17x24.png，像素动态形象不可用");
            return null;
        }
        try
        {
            var px = src.GetPixels32();
            int w = src.width, h = src.height;
            if (!openEyes)
            {
                // 闭眼帧：真正的眼睛 = 第13行(y=12, 0-indexed) 的 M11 两个格子 (x5 和 x9)
                // 闭眼 = 把两个 M11 眼睛像素替换为 H22 闭眼缝线（×4 放大后为 4px 横线）
                Color32 line = new Color32(202, 202, 212, 255);  // H22 #CACAD4 缝线
                px[12 * w + 5] = line;  // 左眼闭眼缝线
                px[12 * w + 9] = line;  // 右眼闭眼缝线
            }
            int uw = w * MASCOT_UPSCALE, uh = h * MASCOT_UPSCALE;
            var tex = new Texture2D(uw, uh, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < uh; y++)
            {
                int sy = y / MASCOT_UPSCALE;
                for (int x = 0; x < uw; x++)
                    tex.SetPixel(x, y, px[sy * w + (x / MASCOT_UPSCALE)]);
            }
            tex.Apply();
            return tex;
        }
        catch (System.Exception e)
        {
            // 纹理不可读（Read/Write 未启用）时的降级：直接用原纹理，靠 GPU Point 整数倍放大，保证面板不崩
            Debug.LogWarning("[RightPanel] 17x24 纹理不可读(" + e.GetType().Name + ")，降级为原尺寸放大渲染");
            src.filterMode = FilterMode.Point;
            src.wrapMode = TextureWrapMode.Clamp;
            return src;
        }
    }

    /// <summary>生成像素符玄小人（16x16 像素画，按 scale 放大，Point 过滤保持锐利）</summary>
    private static Texture2D GenPixelFx(int scale)
    {
        string[] rows =
        {
            ".PP.PPPPPPPP.P..",
            "PPPPPPPPPPPPPPP.",
            "PPPPPPPPPPPPPPP.",
            "PPPPPPPPPPPPPPP.",
            "PPPPPPPPPPPPPPP.",
            ".PPFFFFFFFFPPPP.",
            "...FFFFFFFF.....",
            "...FEFFFFFE.....",
            "...FFFFFFFF.....",
            "...FRFFMFRF.....",
            "....FFFFFFF.....",
            "....WWWWWWW.....",
            "...WWWWWWWWW....",
            "...WAWWWWWWAW...",
            "....WWWWWWW.....",
            ".....WWWWW......",
        };
        var pal = new Dictionary<char, Color>
        {
            { 'P', new Color(0.95f, 0.68f, 0.82f, 1f) }, // 粉发亮
            { 'F', new Color(0.80f, 0.52f, 0.72f, 1f) }, // 刘海粉紫
            { 'E', new Color(0.55f, 0.35f, 0.90f, 1f) }, // 紫眼睛
            { 'R', new Color(0.98f, 0.66f, 0.72f, 1f) }, // 腮红
            { 'M', new Color(0.80f, 0.45f, 0.58f, 1f) }, // 嘴
            { 'W', new Color(0.93f, 0.92f, 0.97f, 1f) }, // 白衣
            { 'A', new Color(0.68f, 0.48f, 0.88f, 1f) }, // 紫饰
        };
        int h = rows.Length;
        int w = rows[0].Length;
        var tex = new Texture2D(w * scale, h * scale, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color t = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c;
                if (!pal.TryGetValue(rows[y][x], out c)) c = t;
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                        tex.SetPixel(x * scale + sx, y * scale + sy, c);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>根据 ChatManager 历史重建终端日志行</summary>
    /// <summary>计算气泡高度：多行文本补足一行余量，防止 GUI.Label 高度不足时尾部出现省略号“...”截断。</summary>
    private static float CalcBubbleHeight(GUIStyle bubble, string text, float bubbleW, float naturalW)
    {
        float h = bubble.CalcHeight(new GUIContent(text), bubbleW);
        if (naturalW > bubbleW + 1f)
            h += bubble.fontSize; // 多行：补偿行高/换行点偏差，避免尾部省略号
        return h;
    }

    private void RebuildLog()
    {
        _logLines.Clear();
        if (_chat == null) return;
        var hist = _chat.History;
        for (int i = 0; i < hist.Count; i++)
        {
            var e = hist[i];
            if (string.IsNullOrEmpty(e.content)) continue;
            // ★ 显示前统一清洗：剥离 markdown 语法与内嵌表情/动作标记
            string text = ChatManager.CleanDisplayText(e.content).Replace('\n', ' ').Replace('\r', ' ');
            // 注意：不截断长文本——气泡自动换行、日志区可滚动，完整显示符玄的长回复
            string role = e.role;
            if (role == "user")
                _logLines.Add(new LogLine { text = text, kind = 1 });
            else if (role == "assistant")
                _logLines.Add(new LogLine { text = text, kind = 0 });
            else if (role == "tool")
                _logLines.Add(new LogLine { text = "[tool] " + (string.IsNullOrEmpty(e.name) ? "?" : e.name) + ": " + text, kind = 2 });
            else
                _logLines.Add(new LogLine { text = text, kind = 2 });
        }
    }

    // ==================== 清理 ====================

    void OnDestroy()
    {
        // 全局热键为轮询模式，无需注销（GetAsyncKeyState 无资源占用）
        if (_bgTex != null) Destroy(_bgTex);
        if (_inputBgTex != null) Destroy(_inputBgTex);
        if (_inputHoverBgTex != null) Destroy(_inputHoverBgTex);
        if (_inputGlowTex != null) Destroy(_inputGlowTex);
        if (_sendBtnTex != null) Destroy(_sendBtnTex);
        if (_sendBtnHoverTex != null) Destroy(_sendBtnHoverTex);
        if (_separatorTex != null) Destroy(_separatorTex);
        if (_whiteTex != null) Destroy(_whiteTex);
        if (_toolTex != null) Destroy(_toolTex);
        if (_toolHoverTex != null) Destroy(_toolHoverTex);
        if (_glowTex != null) Destroy(_glowTex);
        if (_ornamentTL != null) Destroy(_ornamentTL);
        if (_ornamentTR != null) Destroy(_ornamentTR);
        if (_ornamentBR != null) Destroy(_ornamentBR);
        if (_ornamentBL != null) Destroy(_ornamentBL);
        if (_starfieldTex != null) Destroy(_starfieldTex);
        if (_taijiTex != null) Destroy(_taijiTex);
        if (_hexagramTex != null) Destroy(_hexagramTex);
        if (_mascotSubscribed && _chat != null)
        {
            _chat.OnNewReply -= OnMascotReply;
            _chat.OnExpressionTag -= OnMascotExpression;
        }
        foreach (var kv in _emblemTex)
            if (kv.Value != null) Destroy(kv.Value);
        _emblemTex.Clear();
        if (_mascotOpenTex != null) Destroy(_mascotOpenTex);
        if (_mascotBlinkTex != null) Destroy(_mascotBlinkTex);
        if (_pixelFxTex != null) Destroy(_pixelFxTex);
        if (_statusDotTex != null) Destroy(_statusDotTex);
        if (_scanlineTex != null) Destroy(_scanlineTex);
        if (_borderTex != null) Destroy(_borderTex);
        if (_logRowAltTex != null) Destroy(_logRowAltTex);
        if (_titleBarPixelTex != null) Destroy(_titleBarPixelTex);
        if (_inputBarPixelTex != null) Destroy(_inputBarPixelTex);
        if (_monoFont != null) Destroy(_monoFont);
    }
}
