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
public partial class RightPanel : MonoBehaviour
{
    // ==================== 配置参数 ====================
    [Header("窗口尺寸（常规窗口）")]
    public float panelWidth = 560f;        // 窗口宽度
    public float panelHeight = 720f;       // 窗口高度（长方形偏正方）
    public float inputBarHeight = 64f;     // 底部输入框高度，保留底部呼吸空间

    [Header("热键")]
    public KeyCode toggleKey = KeyCode.BackQuote;  // ~ 键切换（窗口内）

    // ==================== 全局热键 (Shift+~) ====================
    // 说明：不用 RegisterHotKey（WM_HOTKEY 会被 Unity 消息泵吞掉，收不到），
    // 改用 GetAsyncKeyState 直接轮询物理键盘状态，任意窗口焦点下均有效。
    private const int VK_OEM_3 = 0xC0;           // ~ 键虚拟码
    private const int VK_F2 = 0x71;               // F2 虚拟码
    private const int VK_LSHIFT = 0xA0;          // 左 Shift
    private const int VK_RSHIFT = 0xA1;          // 右 Shift
    private const int KEY_DOWN = 0x8000;         // 高位为 1 表示按下
    private bool _globalTildeWasDown = false;    // 按下沿检测（防止按住连发）
    private bool _globalF2WasDown = false;       // F2 按下沿检测（外置窗口焦点下仍可用）

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // ==================== 工具按钮定义（不用 emoji，用中文单字） ====================
    private readonly (string icon, string label, BallPanel.PanelType? panelType)[] _tools = new (string, string, BallPanel.PanelType?)[]
    {
        ("聊", "聊天", null),                          // 聚焦输入框
        ("设", "设置", BallPanel.PanelType.Settings),
        ("签", "便签", BallPanel.PanelType.Reminders),
        ("告", "报告", BallPanel.PanelType.Report),
        ("耗", "消耗", BallPanel.PanelType.Usage),
        ("忆", "忆境", BallPanel.PanelType.Memory),
        ("收", "收纳", null),                          // 启动 Pogget
    };

    // ==================== 运行时状态 ====================
    private Rect _panelRect;             // 窗口矩形（位置可拖动）
    private bool _isOpen = false;        // 是否打开
    private bool _closing = false;       // 淡出动画中（动画结束才真正隐藏）
    private float _animAlpha = 0f;       // 面板整体透明度（0~1，进/出动画）
    private Color _panelTint = Color.white; // 每帧面板整体 tint（含 _animAlpha），内部恢复点统一使用
    private const float FADE_SPEED = 5f; // 淡入淡出速度（/秒）
    private bool _isDragging = false;    // 标题栏拖动中
    private Vector2 _dragOffset;         // 拖动偏移

    private ChatManager _chat;
    private BallPanel _ballPanel;
    private DesktopPet _pet;             // 权重设置引用
    private PerformanceMonitor _performanceMonitor;
    private WindowOverlay _windowOverlay;
    private ReminderManager _reminders;  // 便签引用
    private string _inputText = "";
    private const int MAX_INPUT_LENGTH = 300;   // 输入最大长度，防超长文字溢出输入框
    private bool _inputFocused = false; // 是否聚焦到输入框

    // ==================== 子面板字段/方法已拆分至 RightPanel.SubPanels.cs（2026-08-14）====================

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
    private Texture2D _starfieldTex;     // 星空星点纹理（背景叠加，已弃用→分层星点替代）
    private Texture2D _bgGlowTex;        // 左上紫光晕（径向渐变，模拟立绘打光）
    private Texture2D _bgNebulaTex;      // 星云斑块（低频噪声，打破色带）
    private GUIStyle _panelBorderStyle;  // 圆角细边框（SDF 九宫格，替代 2px 硬边+四角方块）
    private StarField _starField = new StarField();  // 星空背景系统（2026-08-14 拆分自本文件，必须实例化否则 Init/DrawStars NRE）
    private Texture2D _taijiTex;         // 太极图（发送按钮）
    private Texture2D _hexagramTex;      // 卦象三爻装饰（标题栏）
    private Texture2D _extWindowIconTex; // 独立窗口图标（两窗方块，程序绘制，不依赖字形）
    private bool _stylesReady = false;

    // ==================== 外部面板窗口（独立普通窗口，QQ 式可被遮挡；2026-08-15 大工程） ====================
    private bool _externalMode;      // 独立窗口模式激活
    private bool _externalRender;    // 正在向独立窗口渲染（抑制屏幕事件处理）
    private bool _runInBackgroundBeforeExternal;
    private Vector2 _externalMousePos = new Vector2(-1f, -1f);
    private bool _testExternalMouseOverride;
    private RenderTexture _chatRT;   // 面板渲染目标（独立窗口显示用，尺寸跟随当前视图）
    private float _lastExtCapture;   // 渲染/推送节流计时
    private float _lastExtReadStart; // 异步读回开始时间（超时兜底防冻结）
    // 输入变化时立即触发一次外置 RT 推送，避免固定 30 FPS 节流带来的字符滞后。
    // 非输入变化仍按普通动画频率推送，避免为降低输入延迟而长期增加 GPU/CPU 负载。
    private bool _externalInputDirty;
    private string _lastExternalComposition = string.Empty;
    private int _lastExternalInputVersion = -1;
    // ★ 异步读回（AsyncGPUReadback）：渲染保持 60fps 动画流畅，读回不阻塞主线程
    private Unity.Collections.NativeArray<byte> _extReadBack;
    private bool _extReadPending;    // 上一帧读回未完成（防止堆积）
    private int _extReadGen;         // ★ 读回代际（RT/NativeArray 重建时递增，作废旧回调，防 destroyTJDevice 崩溃）
    // ★ 外部交互命中表（Phase A3）：渲染外置面板时登记可点区域（矩形+动作），
    //   独立窗口点击坐标回来查表执行（IMGUI Event.current 无法注入，故手动命中）
    // ★ 2026-08-17 坐标基准：外置窗口不再增加独立标题栏，整个客户区直接绘制面板。
    //   _extHitZones 与 Win32 客户区坐标保持 1:1，避免外置标题栏和面板标题栏叠加。
    private readonly List<ExtHitZone> _extHitZones = new List<ExtHitZone>();
    private readonly List<ExtHitZone> _extTitleZones = new List<ExtHitZone>();
    private PanelView _extHitView = (PanelView)(-1);
    private bool _pendingExtInput;
    private float _pendingExtInputX;
    private float _pendingExtInputY;
    private bool _pendingExtInputDoubleClick;
    private PanelView _pendingExtInputView = (PanelView)(-1);
    private struct ExtHitZone
    {
        public Rect rect;
        public System.Action action;
        public bool doubleClickOnly;
    }

    /// <summary>外部窗口输入入口（独立窗口线程 → 主线程，经 MainThreadDispatcher 调用）
    /// ★ 坐标 = 客户区坐标（含标题栏），与命中表 rect 同基准：
    ///   标题栏区（y &lt; EXT_TITLE_BAR_H）查 _extTitleZones；内容区直接查 _extHitZones（不再减 44）</summary>
    public void HandleExternalInput(float x, float y, bool isDoubleClick)
    {
        // 兼容旧版本标题栏命中区；当前外置窗口已取消独立标题栏，按钮全部属于面板命中表。
        if (y < EXT_TITLE_BAR_H)
        {
            var tp = new Vector2(x, y);
            foreach (var zone in _extTitleZones)
            {
                if (zone.rect.Contains(tp))
                {
                    try { zone.action(); }
                    catch (Exception e) { Debug.LogWarning($"[RightPanel] 外部标题栏点击动作异常: {e.Message}"); }
                    return;
                }
            }
            Debug.Log($"[RightPanel] 外部标题栏点击未命中: ({x:F0},{y:F0})，可点区域 {_extTitleZones.Count} 个");
            return;
        }

        // 会话列表的视觉状态和命中表都可能跨越一次 Repaint 才更新。
        // 这里按当前视图直接解释坐标，避免旧聊天视图命中表吞掉会话项点击。
        if (_currentView == PanelView.SessionList && TryHandleSessionListInput(x, y, isDoubleClick))
            return;

        // 模型设置页的选项/样例是外置窗口中最常用的首屏交互。
        // 在命中表尚未完成一帧重建时，使用与绘制区域相同的几何公式兜底，避免切页首击丢失。
        if (_currentView == PanelView.ModelSettings && TryHandleModelSettingsInput(x, y))
            return;

        // 视图已切换但尚未完成下一帧外置渲染时，禁止使用旧视图的动作闭包。
        // 等待下一次 Repaint 重建命中表，避免“点 A 执行了旧页面的 B 动作”。
        if (_extHitView != _currentView)
        {
            // 页面切换后 RenderTexture/命中表可能还在下一帧重建。
            // 暂存一次点击，避免用户刚切页就点击时丢失操作。
            _pendingExtInput = true;
            _pendingExtInputX = x;
            _pendingExtInputY = y;
            _pendingExtInputDoubleClick = isDoubleClick;
            _pendingExtInputView = _currentView;
            Debug.Log($"[RightPanel] 外部点击等待当前视图命中表: {_currentView}（旧表 {_extHitView}），已排队");
            return;
        }

        // 内容区：客户区坐标直接查命中表（rect 已是客户区基准）
        var p = new Vector2(x, y);
        foreach (var zone in _extHitZones)
        {
            if (zone.rect.Contains(p))
            {
                // 会话项遵循 QQ 式双击进入：第一次按下只保留在列表，不提前切页。
                if (zone.doubleClickOnly && !isDoubleClick)
                    return;
                try { zone.action(); }
                catch (Exception e) { Debug.LogWarning($"[RightPanel] 外部点击动作异常: {e.Message}"); }
                return; // 只命中第一个（渲染顺序=绘制顺序，最上层优先）
            }
        }
        Debug.Log($"[RightPanel] 外部点击未命中: ({x:F0},{y:F0})，可点区域 {_extHitZones.Count} 个");
    }

    /// <summary>登记一个外部可点区域（仅外部渲染时收集，面板局部坐标）</summary>
    private void RegisterExtHit(Rect rect, System.Action action, bool doubleClickOnly = false)
    {
        if (_externalRender)
        {
            _extHitZones.Add(new ExtHitZone
            {
                rect = rect,
                action = action,
                doubleClickOnly = doubleClickOnly
            });
        }
    }

    /// <summary>登记一个外部标题栏按钮可点区域（客户区坐标，仅外部渲染时收集）</summary>
    private void RegisterExtTitleHit(Rect rect, System.Action action)
    {
        if (_externalRender) _extTitleZones.Add(new ExtHitZone { rect = rect, action = action });
    }

    /// <summary>
    /// 当前外置会话列表的无渲染帧命中兜底。
    /// 外置窗口的像素渲染与 Unity 主线程事件不是同一时序，不能把“上一帧命中表”
    /// 当成当前页面真相；列表项和底部入口使用与 DrawSessionListView 相同的几何公式。
    /// </summary>
    private bool TryHandleSessionListInput(float x, float y, bool isDoubleClick)
    {
        float pw = _panelRect.width;
        float ph = _panelRect.height;
        float py = 0f;
        float titleH = 76f;
        float closeSize = 36f;
        Vector2 point = new Vector2(x, y);

        Rect closeRect = new Rect(pw - closeSize - 14f, py + (titleH - closeSize) / 2f, closeSize, closeSize);
        if (closeRect.Contains(point))
        {
            if (_externalMode) ExternalChatWindow.RequestClose();
            else Close();
            return true;
        }

        float searchY = py + titleH + 12f;
        Rect newRect = new Rect(pw - 60f, searchY, 48f, 48f);
        if (newRect.Contains(point))
        {
            Debug.Log("[RightPanel] 外部命中：新建会话（多角色扩展预留）");
            return true;
        }

        RefreshSessionList();
        float listY = searchY + 48f + 12f;
        float listH = ph - (listY - py) - 80f;
        if (listH < 40f) listH = 40f;
        Rect listView = new Rect(6f, listY, pw - 12f, listH);
        const float itemH = 96f;
        for (int i = 0; i < _sessions.Count; i++)
        {
            Rect itemRect = new Rect(2f + listView.x - _sessionScroll.x,
                8f + i * itemH + listView.y - _sessionScroll.y,
                listView.width - 8f,
                itemH - 8f);
            if (!itemRect.Contains(point)) continue;

            if (isDoubleClick)
            {
                EnterChat(i);
                Debug.Log($"[RightPanel] 外部会话项双击进入聊天: {i}");
            }
            else
            {
                Debug.Log($"[RightPanel] 外部会话项单击保留列表: {i}");
            }
            return true;
        }

        float toolY = py + ph - 76f;
        float toolW = (pw - 48f) / 5f;
        var toolDefs = new (string label, BallPanel.PanelType type)[]
        {
            ("设置", BallPanel.PanelType.Settings),
            ("便签", BallPanel.PanelType.Reminders),
            ("报告", BallPanel.PanelType.Report),
            ("消耗", BallPanel.PanelType.Usage),
            ("忆境", BallPanel.PanelType.Memory)
        };
        for (int i = 0; i < toolDefs.Length; i++)
        {
            Rect btnRect = new Rect(12f + i * toolW, toolY + 12f, toolW - 8f, 50f);
            if (!btnRect.Contains(point)) continue;
            var type = toolDefs[i].type;
            OpenSubPanel(type);
            return true;
        }

        return false;
    }

    /// <summary>视图尺寸变化后作废旧外置命中表，防止旧闭包操作新页面。</summary>
    private void InvalidateExternalHitZones()
    {
        if (!_externalMode) return;
        _extHitZones.Clear();
        _extTitleZones.Clear();
        _extHitView = (PanelView)(-1);
        _pendingExtInput = false;
        _pendingExtInputView = (PanelView)(-1);
    }

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

    // ==================== OpenClaw 任务进度（类智能体入口·实时步骤可见） ====================
    private readonly List<LogLine> _liveLogLines = new List<LogLine>(); // 动态行（任务步骤/审批），RebuildLog 后保留
    private int _lastSeenStepCount = -1;   // 上次已见步骤数（检测新步骤写日志）
    private bool _taskActiveSeen = false;  // 任务在途检测沿（开始/结束）
    private string _lastSeenApprovalId = ""; // 上次已见审批 id（检测新审批弹窗）
    private float _approvalShownAt = -1f;  // 审批弹窗打开时间（60s 自动拒绝）
    private bool _approvalDialogOpen = false; // 审批弹窗是否打开

    // ==================== QQ 式两级界面（会话列表 ⇄ 聊天）+ 子面板（设置/便签/报告） ====================
    /// <summary>窗口视图：SessionList=第一级窄条会话列表；Chat=第二级展开；ModelSettings=独立模型设置页</summary>
    private enum PanelView { SessionList, Chat, Settings, ModelSettings, Reminders, Report, Usage, Memory }
    private PanelView _currentView = PanelView.SessionList;

    // 尺寸参照 QQ 实测（Win32：324×846 窄条模式）；展开后左会话栏 280 + 右聊天区 580
    // ★ QQ 实测基准 324x846，按 1.5 倍放大（边长），字体同步参照 QQ
    private const float SESSION_LIST_W = 486f;   // 第一级窄条宽度（324×1.5）
    private const float SESSION_LIST_H = 1269f;  // 第一级高度（846×1.5）
    private const float CHAT_PANEL_W = 1290f;    // 第二级展开宽度（420 + 870）
    private const float CHAT_PANEL_H = 1269f;    // 第二级高度
    private const float SIDEBAR_W = 420f;        // 第二级左侧会话栏宽度（280×1.5）
    // 子面板（设置/便签/报告）专用尺寸：比聊天窄，内容更聚焦（星空风格页内切换）
    private const float SUB_PANEL_W = 860f;
    private const float SUB_PANEL_H = 900f;


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
    private readonly Dictionary<string, Texture2D> _mascotEmoteTex = new Dictionary<string, Texture2D>(); // 表情形象帧缓存（表情包）
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
    private const float MIN_PANEL_W = 300f;
    private const float MIN_VISIBLE = 56f;  // 常规窗口式拖出屏幕：最小可见条（≈标题栏高度），保证还能抓住拖回
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
    private GUIStyle _toolTipStyle;        // 工具按钮 hover 提示
    private GUIStyle _termLogStyle;        // 日志-符玄（紫）
    private GUIStyle _termLogUserStyle;    // 日志-用户（浅蓝白）
    private GUIStyle _termLogDimStyle;     // 日志-系统/工具（灰）
    private GUIStyle _emptyStateTitleStyle;
    private GUIStyle _emptyStateHintStyle;
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
    private Texture2D _transparentTex;     // 输入控件样式透明底，避免 GUI 默认黑底

    // ==================== 装饰状态 ====================
    private string _timeDisplay = "";
    private float _timeRefreshTimer = 0f;

    /// <summary>面板完整区域（供 DragHandler 判断鼠标是否在面板交互区域内）</summary>
    public Rect PanelRect => _panelRect;

    /// <summary>审批弹窗是否打开（模态遮罩，DragHandler 需强制关穿透才能点按钮）</summary>
    public bool IsApprovalDialogOpen => _approvalDialogOpen;

    /// <summary>供 DragHandler 判断鼠标是否在面板交互区域内（用于点击穿透控制）</summary>
    public bool IsPointInInteractiveArea(Vector2 guiMousePos)
    {
        return _isOpen && _panelRect.Contains(guiMousePos);
    }

    /// <summary>启动 Pogget 桌面收纳工具</summary>
    private void LaunchPogget()
    {
        // ★ Pogget 路径配置化（阶段0）：环境变量 POGGET_EXE 覆盖，默认 d:\pogget\Pogget.exe（向后兼容）
        string exePath = System.Environment.GetEnvironmentVariable("POGGET_EXE") ?? @"d:\pogget\Pogget.exe";
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
        RuntimeReadinessService.EnsureExists();
        RefreshRefs();
        DisableLegacyBallPanels();
        // 恢复字体档位（默认 1=A2 1.2×）
        try
        {
            _fontScaleLevel = Mathf.Clamp(PlayerPrefs.GetInt("RightPanelFontScale", 1), 0, FONT_SCALES.Length - 1);
        }
        catch (Exception ex)
        {
            _fontScaleLevel = 1;
            Debug.LogWarning($"[RightPanel] 字体档位读取失败，使用默认值（无害）: {ex.Message}");
        }
        // QQ 式两级界面：初始为第一级「会话列表」窄条（324×846，贴 QQ 实测），热键打开后双击进聊天
        _currentView = PanelView.SessionList;
        float w = Mathf.Min(SESSION_LIST_W, Screen.width - 20f);
        float h = Mathf.Min(SESSION_LIST_H, Screen.height - 40f);
        float x = (Screen.width - w) / 2f;
        float y = (Screen.height - h) / 2f;
        _panelRect = new Rect(x, y, w, h);
        Debug.Log($"[RightPanel] 已就绪，屏幕={Screen.width}x{Screen.height}，视图=会话列表 {w}x{h} 居中=({x},{y})");

        // 全局热键 Shift+~（轮询物理键盘状态，不依赖窗口焦点，防误触）
        Debug.Log("[RightPanel] 全局热键已启用: Shift+~ (GetAsyncKeyState 轮询)");

        // ★ 2026-08-16：加载长效消耗日志历史（跨重启累计，「消耗」面板显示累计含历史）
        UsageLogger.LoadHistoryIntoUsageStats();
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
        // 子面板（设置权重/便签）依赖的引用
        if (_pet == null)
        {
            _pet = GetComponent<DesktopPet>();
            if (_pet == null) _pet = FindObjectOfType<DesktopPet>();
        }
        if (_windowOverlay == null)
        {
            _windowOverlay = GetComponent<WindowOverlay>();
            if (_windowOverlay == null) _windowOverlay = FindObjectOfType<WindowOverlay>();
        }
        if (_reminders == null)
        {
            _reminders = ReminderManager.Instance;
            if (_reminders == null) _reminders = GetComponent<ReminderManager>();
        }
    }

    /// <summary>旧 BallPanel 已被页内视图取代；无论它挂在哪个场景对象上都禁止绘制。</summary>
    private void DisableLegacyBallPanels()
    {
        var legacyPanels = FindObjectsOfType<BallPanel>();
        for (int i = 0; i < legacyPanels.Length; i++)
        {
            BallPanel panel = legacyPanels[i];
            panel.Close();
            panel.enabled = false;
        }
        if (legacyPanels.Length > 0)
            Debug.Log($"[RightPanel] 已禁用遗留 BallPanel 实例: {legacyPanels.Length}");
    }

    void Update()
    {
        RefreshRefs();

        // 外置输入框是不可见的原生键盘通道，文字由此同步到 Unity RT；
        // 不再让原生 EDIT 覆盖 IMGUI 输入框，避免黑框与真实输入框交替闪烁。
        if (_externalMode)
        {
            int inputVersion = ExternalChatWindow.GetInputTextVersion();
            string nativeComposition = ExternalChatWindow.GetInputComposition();
            if (inputVersion != _lastExternalInputVersion || nativeComposition != _lastExternalComposition)
            {
                if (inputVersion != _lastExternalInputVersion)
                {
                    _lastExternalInputVersion = inputVersion;
                    _inputText = ExternalChatWindow.GetInputText();
                }
                _lastExternalComposition = nativeComposition;
                _externalInputDirty = true;
                GUI.changed = true;
            }
        }

        // 外置窗口是独立线程，拖动/缩放不会触发 Unity 的 IMGUI 事件；
        // 每帧同步透明层缺口，保证普通外置窗口移动后仍可点击。
        if (_externalMode && _windowOverlay != null)
            _windowOverlay.RefreshExternalWindowHole();

        // 0. 测试收件箱注入：测试模式或纯云端质量基线模式下，外部脚本向 InboxFile 写入一行
        //    → 本帧检测到即作为用户消息发送（绕过 UI 点击，窗口位置无关，适合自动化测试）
        CheckTestInbox();

        // 1. 热键切换 — 兼容中文键盘（`·~` 键、F2、\ 均可）
        // ★ 输入框聚焦时不响应热键（否则聊天打字输入 ~、\ 会误触面板收起）
        bool inputFocused = GUI.GetNameOfFocusedControl() == "rightPanelInput";
        bool togglePressed = !inputFocused && (Input.GetKeyDown(toggleKey)
            || Input.GetKeyDown(KeyCode.F2)
            || Input.GetKeyDown(KeyCode.Backslash));
        if (togglePressed && Time.frameCount != _hotkeyFrame)
        {
            _hotkeyFrame = Time.frameCount;
            ToggleHotkeyPanel();
        }

        // 1b. 全局热键 Shift+~（任意窗口焦点下均可触发）
        CheckGlobalHotkey();

        // 2. 终端日志重建（历史条数变化时刷新 + 滚到底）
        if (_chat != null && _chat.HistoryCount != _lastLogCount)
        {
            _lastLogCount = _chat.HistoryCount;
            RebuildLog();
            _sessionDirty = true;   // 会话列表最后消息同步刷新
            _pendingAutoScroll = true;
        }

        // 3. 标题栏拖动（鼠标按住时在 Update 里更新位置）
        if (_isDragging)
        {
            // ★ 防吸鼠标：左键已松开但某视图漏了 MouseUp 复位时，强制结束拖动
            if (!Input.GetMouseButton(0))
            {
                _isDragging = false;
                _isResizing = false;
            }
            else
            {
                Vector2 mp = Input.mousePosition;
                mp.y = Screen.height - mp.y; // 转 GUI 坐标
                Vector2 newPos = mp - _dragOffset;
                // ★ 常规窗口式拖动：允许部分拖出屏幕（保留 MIN_VISIBLE 可见条便于抓回），不再强制全屏内
                newPos.x = Mathf.Clamp(newPos.x, MIN_VISIBLE - panelWidth, Mathf.Max(Screen.width - MIN_VISIBLE, MIN_VISIBLE));
                newPos.y = Mathf.Clamp(newPos.y, MIN_VISIBLE - panelHeight, Mathf.Max(Screen.height - MIN_VISIBLE, MIN_VISIBLE));
                _panelRect.x = newPos.x;
                _panelRect.y = newPos.y;
            }
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

        // 4c. OpenClaw 任务进度轮询（后台线程写静态原子属性，本帧对比变化写日志/开弹窗）
        CheckOpenClawTaskProgress();

        // 4d. 淡入淡出动画推进：_closing 时 alpha 归零后才真正隐藏
        if (_isOpen)
        {
            float target = _closing ? 0f : 1f;
            _animAlpha = Mathf.MoveTowards(_animAlpha, target, FADE_SPEED * Time.deltaTime);
            if (_closing && _animAlpha <= 0.001f)
            {
                _isOpen = false;
                _closing = false;
                _isDragging = false;
                Debug.Log("[RightPanel] 淡出完成，已隐藏");
            }
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

    /// <summary>切换窗口开关（带淡入淡出：打开立即 alpha 0→1；关闭先播放淡出，动画结束才隐藏）</summary>
    public void Toggle()
    {
        if (_isOpen)
        {
            // 正在淡出时再次按 → 取消淡出（立刻恢复）
            if (_closing) { _closing = false; return; }
            // 已完全打开 → 开始淡出
            _closing = true;
            _inputFocused = false;
            return;
        }
        // 打开：alpha 从 0 开始淡入
        _isOpen = true;
        _closing = false;
        _animAlpha = 0f;
        // 热键打开默认落第一级「会话列表」（QQ 式：窄条列表 → 双击进聊天）
        _currentView = PanelView.SessionList;
        ApplyViewSize();
        _inputFocused = true; // 打开后自动聚焦输入框
    }

    /// <summary>按当前视图应用窗口尺寸（窄条 ⇄ 展开，左上角保持，允许停在屏外但保留最小可见条）</summary>
    private void ApplyViewSize()
    {
        InvalidateExternalHitZones();
        float w = _currentView == PanelView.SessionList ? SESSION_LIST_W
            : IsSubPanelView(_currentView) ? SUB_PANEL_W
            : CHAT_PANEL_W;
        float h = _currentView == PanelView.SessionList ? SESSION_LIST_H
            : IsSubPanelView(_currentView) ? SUB_PANEL_H
            : CHAT_PANEL_H;
        w = Mathf.Min(w, Screen.width - 20f);
        h = Mathf.Min(h, Screen.height - 40f);
        // ★ 与拖动一致：允许停在屏外，仅夹到最小可见条可见（常规窗口行为），不再强制全屏内收拢
        float nx = Mathf.Clamp(_panelRect.x, MIN_VISIBLE - w, Mathf.Max(Screen.width - MIN_VISIBLE, MIN_VISIBLE));
        float ny = Mathf.Clamp(_panelRect.y, MIN_VISIBLE - h, Mathf.Max(Screen.height - MIN_VISIBLE, MIN_VISIBLE));
        _panelRect = new Rect(nx, ny, w, h);
        panelWidth = w;
        panelHeight = h;
        // 外置窗口可获得前台焦点，Unity 主窗口随后进入后台。必须先允许后台运行，
        // 否则 Unity 停止 Repaint，外置窗口会卡在上一视图的纹理和命中表。
        if (_externalMode && ExternalChatWindow.IsCreated)
        {
            ExternalChatWindow.SetSize(Mathf.RoundToInt(w), Mathf.RoundToInt(h));
            UnityEngine.GUI.changed = true;
        }
        Debug.Log($"[RightPanel] 视图切换 → {_currentView}，窗口={w}x{h} @ ({_panelRect.x:F0},{_panelRect.y:F0})");
    }

    /// <summary>双击会话 → 进入聊天视图（窗口展开，左会话栏+右聊天区）</summary>
    private void EnterChat(int sessionIdx)
    {
        if (sessionIdx < 0 || sessionIdx >= _sessions.Count) return;
        _activeSession = sessionIdx;
        _currentView = PanelView.Chat;
        ApplyViewSize();
        _inputFocused = true;          // 进入聊天后聚焦输入框
        _pendingAutoScroll = true;     // 聊天日志滚到底
        Debug.Log($"[RightPanel] 进入聊天: {_sessions[sessionIdx].name}");
    }

    /// <summary>聊天视图「◀ 返回」→ 回到会话列表（窗口收窄）</summary>
    public void BackToSessionList()
    {
        _currentView = PanelView.SessionList;
        ApplyViewSize();
        Debug.Log("[RightPanel] 返回会话列表");
    }

    /// <summary>判断是否为子面板视图（设置/便签/报告）</summary>
    private bool IsSubPanelView(PanelView v)
    {
        return v == PanelView.Settings || v == PanelView.ModelSettings || v == PanelView.Reminders || v == PanelView.Report || v == PanelView.Usage || v == PanelView.Memory;
    }

    /// <summary>打开独立模型设置页；模型切换不会改变动作模型。</summary>
    private void OpenModelSettings()
    {
        _prevView = _currentView;
        _currentView = PanelView.ModelSettings;
        ApplyViewSize();
        Debug.Log($"[RightPanel] 打开模型设置页（当前聊天模型 {LocalLLMClient.ChatModelName}）");
    }

    /// <summary>打开子面板（设置/便签/报告）：记录来源视图，切换为页内视图并应用子面板尺寸</summary>
    private void OpenSubPanel(BallPanel.PanelType type)
    {
        _prevView = _currentView; // ◀ 返回时回到来源视图
        switch (type)
        {
            case BallPanel.PanelType.Settings: _currentView = PanelView.Settings; break;
            case BallPanel.PanelType.Reminders: _currentView = PanelView.Reminders; break;
            case BallPanel.PanelType.Report: _currentView = PanelView.Report; break;
            case BallPanel.PanelType.Usage: _currentView = PanelView.Usage; break;
            case BallPanel.PanelType.Memory: _currentView = PanelView.Memory; break;
            default: return;
        }
        // 进入子面板时从宠物实时权重加载（设置页专用）
        if (_currentView == PanelView.Settings && _pet != null && !_settingsLoaded)
        {
            _wLeftEdge = _pet.taskWeightMoveLeftEdge;
            _wRightEdge = _pet.taskWeightMoveRightEdge;
            _wLeftTime = _pet.taskWeightMoveLeftTime;
            _wRightTime = _pet.taskWeightMoveRightTime;
            _wStop = _pet.taskWeightStopTime;
            _settingsLoaded = true;
        }
        ApplyViewSize();
        _settingsScrollPos = Vector2.zero;
        _reportScrollPos = Vector2.zero;
        _reminderScrollPos = Vector2.zero;
        _remindersRefreshed = false;
        Debug.Log($"[RightPanel] 打开子面板 → {_currentView}（来源 {_prevView}）");
    }

    /// <summary>子面板「◀ 返回」→ 回到来源视图</summary>
    private void BackFromSubPanel()
    {
        if (!IsSubPanelView(_prevView)) _prevView = PanelView.Chat;
        _currentView = _prevView;
        // 子面板允许一层嵌套（设置 → 模型设置）。返回后清掉旧来源，
        // 避免再次点击返回时仍停留在刚刚离开的设置页。
        _prevView = PanelView.Chat;
        ApplyViewSize();
        Debug.Log($"[RightPanel] 子面板返回 → {_currentView}");
    }

    /// <summary>统一关闭当前视图：外置模式关闭原生窗口，内嵌模式才播放 Unity 淡出。</summary>
    private void RequestClosePanel()
    {
        if (_externalMode) ExternalChatWindow.RequestClose();
        else Close();
    }

    /// <summary>刷新标题栏时间（1s 节流）</summary>
    private void RefreshTime()
    {
        _timeRefreshTimer += Time.deltaTime;
        if (_timeRefreshTimer > 1f || string.IsNullOrEmpty(_timeDisplay))
        {
            _timeRefreshTimer = 0f;
            _timeDisplay = System.DateTime.Now.ToString("HH:mm");
        }
    }

    // ==================== 测试注入通道 ====================

    private float _nextInboxCheck = 0f;

    /// <summary>
    /// 测试收件箱：测试模式或纯云端质量基线模式启用。
    /// 外部测试脚本向 DataPathConfig.InboxFile 写入一行文字，
    /// 这里以 0.25s 间隔轮询，读到非空内容即处理，然后清空文件（保留文件避免反复触发）。
    /// 支持两种格式：
    ///   - 普通文本 → 作为用户消息调用 ChatManager.SendMessage 发送（走 LLM）
    ///   - @@emote:xxx → 测试表情注入，不走 LLM，直接左侧气泡 + 右上角表情徽章
    /// 发送后 HistoryCount +1 → Update 第 2 步自动 RebuildLog → 气泡直接可见。
    /// </summary>
    private void CheckTestInbox()
    {
        if (!ChatManager.IsTestMode && !ChatConfig.UseCloudBaseline && !ChatConfig.UseOllamaMode) return;
        if (Time.time < _nextInboxCheck) return;
        _nextInboxCheck = Time.time + 0.25f;

        string inboxPath = DataPathConfig.InboxFile;
        if (!System.IO.File.Exists(inboxPath)) return;

        string content;
        try { content = System.IO.File.ReadAllText(inboxPath).Trim(); }
        catch { return; }
        if (string.IsNullOrEmpty(content)) return;

        try { System.IO.File.WriteAllText(inboxPath, ""); }
        catch { return; }

        // ★ 配对质量测试：@@case:chat_001 设置后续遥测的案例编号；@@case: 清除。
        if (content.StartsWith("@@case:"))
        {
            string caseId = content.Substring("@@case:".Length).Trim();
            QualityTelemetry.SetCaseId(caseId);
            Debug.Log($"[QualityTest] 当前案例: {(string.IsNullOrEmpty(QualityTelemetry.CurrentCaseId) ? "(none)" : QualityTelemetry.CurrentCaseId)}");
            return;
        }

        // ★ 质量动作案例：@@motion:动作描述 → 绕过自主决策随机性，直接执行指定动作。
        //    仅在本地/纯云端质量模式或 .test_mode 下启用；动作描述不写入质量日志。
        if (content.StartsWith("@@motion:"))
        {
            string description = content.Substring("@@motion:".Length).Trim();
            if (MotionAgent.Instance != null && !string.IsNullOrEmpty(description))
            {
                MotionAgent.Instance.RunQualityMotionCase(description);
                Debug.Log($"[QualityTest] 已注入动作案例: {QualityTelemetry.CurrentCaseId}");
            }
            else
            {
                Debug.LogWarning("[QualityTest] 动作案例失败：MotionAgent 未就绪或描述为空");
            }
            return;
        }

        // Test-only action injection: @@idle:1..9 bypasses the LLM so the
        // existing idle/hardcoded action implementations can be inspected.
        if (content.StartsWith("@@idle:"))
        {
            if (!ChatManager.IsTestMode) return;
            string rawId = content.Substring("@@idle:".Length).Trim();
            if (int.TryParse(rawId, out int actionId) && actionId >= 1 && actionId <= 9)
            {
                var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
                if (renderer != null)
                {
                    renderer.ForceIdleAction(actionId);
                    Debug.Log($"[TestInbox] idle action triggered: #{actionId}");
                }
                else
                {
                    Debug.LogWarning("[TestInbox] @@idle could not find Live2DRenderer");
                }
            }
            else
            {
                Debug.LogWarning($"[TestInbox] invalid @@idle argument: {rawId} (expected 1-9)");
            }
            return;
        }

        // Test-only model capture: save the actual Live2D render, excluding
        // the external window/background, for frame-by-frame action review.
        if (content.StartsWith("@@shot:"))
        {
            if (!ChatManager.IsTestMode) return;
            string rawName = content.Substring("@@shot:".Length).Trim();
            string safeName = rawName.Replace("..", "_").Replace("\\", "_").Replace("/", "_");
            if (string.IsNullOrEmpty(safeName)) safeName = "capture";
            var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
            byte[] png = renderer?.CaptureModelSnapshot();
            if (png == null || png.Length == 0)
            {
                Debug.LogWarning("[TestInbox] @@shot failed: empty model snapshot");
            }
            else
            {
                string dir = System.IO.Path.Combine(DataPathConfig.DataRoot, "action_captures");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, safeName + ".png");
                System.IO.File.WriteAllBytes(path, png);
                Debug.Log($"[TestInbox] model snapshot saved: {path} ({png.Length} bytes)");
            }
            return;
        }

        // ★ 测试视图切换：@@view:settings|reminders|report|chat|list|back|close|open
        //   终端测试链路——无需模拟鼠标点击，写一行文件即可可靠切页（仅测试模式）。
        //   设置/便签/报告 = 页内子面板；chat = 聊天视图；list = 会话列表；back = 子面板返回。
        if (content.StartsWith("@@view:"))
        {
            string viewCmd = content.Substring("@@view:".Length).Trim();
            HandleTestViewCommand(viewCmd);
            return;
        }

        // ★ 测试审批注入：@@approval:命令文本 → 注入 OpenClaw 审批弹窗（仅测试模式）
        if (content.StartsWith("@@approval:"))
        {
            string cmd = content.Substring("@@approval:".Length).Trim();
            OpenClawBridge.InjectTestApproval(string.IsNullOrEmpty(cmd) ? "shutdown -s -t 0" : cmd);
            // 直接打开审批弹窗（测试链路不走轮询检测沿，确保弹窗立即可见）
            _approvalDialogOpen = true;
            _approvalShownAt = Time.time;
            _lastSeenApprovalId = OpenClawBridge.PendingApproval?.approvalId ?? "";
            Debug.Log($"[TestInbox] 已注入测试审批: {cmd} → 审批弹窗打开");
            return;
        }

        // ★ 测试退出命令：@@test:quit → 走与托盘「退出」完全相同的回调（2026-08-17 验收 P10）
        //   （不直接 taskkill：必须验证 ExternalChatWindow.Shutdown + OnDestroy 清理链）
        if (content.StartsWith("@@test:quit"))
        {
            Debug.Log("[TestInbox] @@test:quit → 执行完整退出（等同托盘退出）");
            var pet = GameObject.FindObjectOfType<DesktopPet>();
            if (pet != null)
            {
                // 先关外置窗口线程，再走托盘退出回调（OnDestroy 会释放互斥体 + Application.Quit）
                if (ExternalChatWindow.IsCreated) ExternalChatWindow.Shutdown();
                pet.QuitFromTestCommand();
            }
            else Debug.LogWarning("[TestInbox] @@test:quit 未找到 DesktopPet");
            return;
        }

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

    /// <summary>
    /// 终端测试链路：处理 @@view:xxx 命令（仅测试模式，由 inbox.txt 触发）。
    /// 命令一览：
    ///   settings/reminders/report → 打开对应页内子面板（OpenSubPanel）
    ///   chat → 切到聊天视图（EnterChat 语义，无会话时建默认会话）
    ///   list → 切回会话列表
    ///   back → 子面板 ◀ 返回来源视图
    ///   open/close → 打开/淡出关闭面板（等价热键 Toggle/Close）
    /// 设计目的：让外部测试脚本/CI 不依赖模拟鼠标点击坐标，即可可靠驱动 UI 状态。
    /// </summary>
    private void HandleTestViewCommand(string cmd)
    {
        Debug.Log($"[TestInbox] @@view 命令: {cmd}");
        switch (cmd)
        {
            case "settings": OpenSubPanel(BallPanel.PanelType.Settings); break;
            case "model":
            case "model-settings": OpenModelSettings(); break;
            case "reminders": OpenSubPanel(BallPanel.PanelType.Reminders); break;
            case "report": OpenSubPanel(BallPanel.PanelType.Report); break;
            case "usage": OpenSubPanel(BallPanel.PanelType.Usage); break;
            case "memory": OpenSubPanel(BallPanel.PanelType.Memory); break;
            case "chat":
                if (_currentView != PanelView.Chat)
                {
                    if (_sessions == null || _sessions.Count == 0) { RefreshSessionList(); }
                    if (_sessions != null && _sessions.Count > 0) EnterChat(0);
                    else { _currentView = PanelView.Chat; ApplyViewSize(); }
                }
                break;
            case "list": BackToSessionList(); break;
            case "back": BackFromSubPanel(); break;
            case "open":
                if (!_isOpen) Toggle();
                break;
            case "close":
                if (_isOpen) Close();
                break;
            case "external":
                // 独立面板窗口（⧉ 等价命令）：先确保面板打开，保持当前视图（整面板外置）
                if (!_isOpen) Toggle();
                if (_currentView != PanelView.Chat && _currentView != PanelView.SessionList
                    && !IsSubPanelView(_currentView))
                {
                    if (_sessions == null || _sessions.Count == 0) RefreshSessionList();
                    if (_sessions != null && _sessions.Count > 0) EnterChat(0);
                    else { _currentView = PanelView.Chat; ApplyViewSize(); }
                }
                if (!_externalMode) EnableExternalMode();
                break;
            case "embed":
                // 退回内嵌聊天窗口
                if (_externalMode) DisableExternalMode();
                break;
            default:
                // ★ 带参数命令（@@view:extclick:x,y[,dbl] / @@view:exthover:x,y）：前缀匹配
                if (cmd.StartsWith("extclick:"))
                {
                    // 外部点击注入（铁律4 终端链路）：模拟独立窗口点击命中表
                    string rest = cmd.Substring("extclick:".Length);
                    var parts = rest.Split(',');
                    float cx, cy; bool dbl = false;
                    if (parts.Length >= 2 && float.TryParse(parts[0].Trim(), out cx) && float.TryParse(parts[1].Trim(), out cy))
                    {
                        if (parts.Length >= 3) bool.TryParse(parts[2].Trim(), out dbl);
                        Debug.Log($"[TestInbox] 外部点击注入: 客户区({cx:F0},{cy:F0}) dbl={dbl}");
                        // ★ 2026-08-17 坐标基准修正：extclick 坐标 = 客户区坐标（与 HandleExternalInput/命中表同基准），
                        //   直接传，不做 ±44 转换（此前 +44 与 HandleExternalInput 内 -44 抵消，属巧合正确）
                        HandleExternalInput(cx, cy, dbl);
                    }
                    else Debug.LogWarning($"[TestInbox] extclick 参数格式错误: {rest}（应为 x,y[,dbl]）");
                    break;
                }
                if (cmd.StartsWith("exthover:"))
                {
                    // 外置 RT 悬停注入：只改变 hover 坐标，不触发点击，供视觉验收和自动化回归使用。
                    string rest = cmd.Substring("exthover:".Length);
                    var parts = rest.Split(',');
                    float hx, hy;
                    if (parts.Length >= 2 && float.TryParse(parts[0].Trim(), out hx) && float.TryParse(parts[1].Trim(), out hy))
                    {
                        _externalMousePos = new Vector2(hx, hy);
                        _testExternalMouseOverride = true;
                        GUI.changed = true;
                        Debug.Log($"[TestInbox] 外置悬停注入: 客户区({hx:F0},{hy:F0})");
                    }
                    else Debug.LogWarning($"[TestInbox] exthover 参数格式错误: {rest}（应为 x,y）");
                    break;
                }
                Debug.LogWarning($"[TestInbox] 未知 @@view 命令: {cmd}（支持 settings/reminders/report/usage/chat/list/back/open/close/external/embed/extclick/exthover）");
                break;
        }
    }

    /// <summary>轮询全局热键 Shift+~ / F2（不依赖 Unity 窗口焦点）</summary>
    private void CheckGlobalHotkey()
    {
        bool shiftDown = (GetAsyncKeyState(VK_LSHIFT) & KEY_DOWN) != 0
                      || (GetAsyncKeyState(VK_RSHIFT) & KEY_DOWN) != 0;
        bool tildeDown = (GetAsyncKeyState(VK_OEM_3) & KEY_DOWN) != 0;
        bool f2Down = (GetAsyncKeyState(VK_F2) & KEY_DOWN) != 0;

        // 使用带修饰键的组合或 F2，均不受旧 IMGUI 输入焦点影响；
        // Shift+~ 不会与普通聊天文字冲突，F2 兼容用户已有的桌宠习惯。
        bool tildePressed = shiftDown && tildeDown && !_globalTildeWasDown;
        bool f2Pressed = f2Down && !_globalF2WasDown;
        if ((tildePressed || f2Pressed) && Time.frameCount != _hotkeyFrame)
        {
            _hotkeyFrame = Time.frameCount;
            // 全局热键的关闭语义必须是幂等 Close；Toggle 在淡出期间会取消关闭，
            // 导致用户按一次 ~ 后面板仍然停留或重新出现。
            ToggleHotkeyPanel();
        }
        _globalTildeWasDown = tildeDown;
        _globalF2WasDown = f2Down;
    }

    /// <summary>
    /// 热键统一唤出独立聊天窗口。旧的热键只切换内嵌 RightPanel，容易与遗留
    /// BallPanel/左下角系统面板同时出现；现在首次唤出直接进入独立窗口，
    /// 再按一次则完整关闭独立窗口和面板。
    /// </summary>
    private void ToggleHotkeyPanel()
    {
        DisableLegacyBallPanels();
        if (_externalMode)
        {
            ExternalChatWindow.RequestClose();
            return;
        }

        if (_isOpen)
        {
            Close();
            return;
        }

        EnableExternalMode();
    }

    /// <summary>关闭窗口（带淡出动画：先播放淡出，动画结束才真正隐藏）</summary>
    public void Close()
    {
        if (!_isOpen) return;
        if (_closing) return;
        _closing = true;
        _inputFocused = false;
    }

    /// <summary>
    /// 外置窗口关闭时立即清掉 Unity 内嵌面板状态。
    /// 外置窗口已经拥有自己的生命周期，不能再等待 Unity 面板淡出，否则会闪回一帧
    /// 或把最后一帧紫色 RT 留在桌面上。
    /// </summary>
    private void HideEmbeddedPanelImmediately()
    {
        _isOpen = false;
        _closing = false;
        _animAlpha = 0f;
        _inputFocused = false;
        _isDragging = false;
        _isResizing = false;
    }

    // ==================================================================
    //  OpenClaw 任务进度轮询（类智能体入口：步骤可见 + 审批弹窗）
    //  后台任务线程只写 OpenClawBridge 静态原子属性，本方法在主线程逐帧对比变化。
    // ==================================================================

    /// <summary>
    /// 主线程轮询：任务开始/结束、新步骤写日志、新审批开弹窗、审批 60s 超时自动拒绝。
    /// </summary>
    private void CheckOpenClawTaskProgress()
    {
        bool active = OpenClawBridge.HasActiveTask;

        // ——— 任务开始 / 结束沿 ———
        if (active && !_taskActiveSeen)
        {
            _taskActiveSeen = true;
            _lastSeenStepCount = 0;
            AddLiveLog("⚙ OpenClaw 任务开始执行", 2);
        }
        else if (!active && _taskActiveSeen)
        {
            _taskActiveSeen = false;
            AddLiveLog("✔ OpenClaw 任务已结束", 2);
        }

        // ——— 新步骤 → 日志区（步骤级含工具名+摘要） ———
        int stepCount = OpenClawBridge.ActiveStepCount;
        if (active && stepCount > _lastSeenStepCount)
        {
            _lastSeenStepCount = stepCount;
            if (!string.IsNullOrEmpty(OpenClawBridge.ActiveStepLabel))
                AddLiveLog("[openclaw] " + OpenClawBridge.ActiveStepLabel, 2);
        }

        // ——— 新审批 → 打开审批弹窗 ———
        var pa = OpenClawBridge.PendingApproval;
        string paId = pa?.approvalId ?? "";
        if (!string.IsNullOrEmpty(paId) && paId != _lastSeenApprovalId)
        {
            _lastSeenApprovalId = paId;
            _approvalDialogOpen = true;
            _approvalShownAt = Time.time;
            string cmd = string.IsNullOrEmpty(pa.command) ? (pa.title ?? "(无命令描述)") : pa.command;
            AddLiveLog("⚠ OpenClaw 请求审批: " + cmd, 2);
        }
        else if (string.IsNullOrEmpty(paId) && _approvalDialogOpen)
        {
            // 审批已被后台解析（回执送达）→ 关弹窗
            _approvalDialogOpen = false;
        }

        // ——— 审批弹窗 60s 超时自动拒绝（防挂起） ———
        if (_approvalDialogOpen && Time.time - _approvalShownAt > 60f)
        {
            _approvalDialogOpen = false;
            AddLiveLog("⏰ OpenClaw 审批超时（60s），已自动拒绝", 2);
            _ = AutoDenyApproval();
        }
    }

    /// <summary>追加一条动态日志（同时入 _logLines 即时显示 + _liveLogLines 供 RebuildLog 保留）</summary>
    private void AddLiveLog(string text, int kind)
    {
        var ln = new LogLine { text = text, kind = kind };
        _logLines.Add(ln);
        _liveLogLines.Add(ln);
        _pendingAutoScroll = true;
        // 防无限增长（动态行上限 300，日志区上限 800）
        if (_liveLogLines.Count > 300) _liveLogLines.RemoveRange(0, _liveLogLines.Count - 300);
        if (_logLines.Count > 800) _logLines.RemoveRange(0, _logLines.Count - 800);
    }

    /// <summary>审批回执：允许一次 / 总是允许 / 拒绝（POST 桥接层，后台任务继续）</summary>
    private async void ResolveApproval(string decision)
    {
        _approvalDialogOpen = false;
        string taskId = OpenClawBridge.ActiveTaskId;
        if (string.IsNullOrEmpty(taskId)) taskId = OpenClawBridge.LastTaskId;
        bool ok = await OpenClawBridge.ApproveTaskAsync(taskId, decision);
        string verb = decision == "deny" ? "拒绝" : (decision == "allow-always" ? "总是允许" : "允许");
        AddLiveLog(ok ? $"✔ 已{verb} OpenClaw 审批（{decision}）"
                      : $"❌ 审批回执失败: {OpenClawBridge.LastError}", 2);
    }

    /// <summary>审批超时自动拒绝（后台发送，不阻塞主线程）</summary>
    private async System.Threading.Tasks.Task AutoDenyApproval()
    {
        string taskId = OpenClawBridge.ActiveTaskId;
        if (string.IsNullOrEmpty(taskId)) taskId = OpenClawBridge.LastTaskId;
        await OpenClawBridge.ApproveTaskAsync(taskId, "deny");
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

        // ——— 淡入淡出：全局透明度包裹整个面板（进/出动画） ———
        // ★ 内部所有 GUI.color = Color.white 恢复点必须改用 _panelTint，否则会破坏整体 alpha
        _panelTint = new Color(1f, 1f, 1f, _animAlpha);
        GUI.color = _panelTint;

        // ★ 外部窗口模式（2026-08-15 大工程 Phase A1）：整个面板（含背景/视图/审批）渲染到独立普通窗口，
        //   屏幕不再画面板。桌宠本体（Unity 置顶窗口）不受影响。
        if (_externalMode)
        {
            DrawExternalPanelToTexture();
            GUI.color = Color.white;
            return;
        }

        DrawPanelContent(px, py, pw, ph, mp);

        GUI.color = Color.white; // 恢复全局色，防止淡入淡出半透明残留影响其它 OnGUI
    }

    /// <summary>
    /// 面板内容统一绘制（屏幕模式与外置 RT 模式共用）：
    /// 背景质感分层 → 四角角饰 → 视图分发（会话列表/子面板/聊天）→ 审批模态弹窗。
    /// 外部模式渲染时 px/py 传 0（RT 原点），mp 传独立窗口回传坐标（无回传时 Vector2.zero）。
    /// </summary>
    private void DrawPanelContent(float px, float py, float pw, float ph, Vector2 mp)
    {
        // 审批弹窗语义：覆盖整个面板（含会话侧栏）→ 保存调用方传入的原始面板矩形
        float panelX = px, panelY = py, panelW = pw, panelH = ph;

        // ——— 面板背景 ——— 质感分层：渐变 → 左上紫光晕 → 星云 → 分层星点 → 圆角细边框
        Rect bgRect = new Rect(px, py, pw, ph);
        GUI.Box(bgRect, GUIContent.none, _panelStyle);
        if (_bgGlowTex != null)
            GUI.DrawTexture(bgRect, _bgGlowTex, ScaleMode.StretchToFill);
        if (_bgNebulaTex != null)
            GUI.DrawTexture(bgRect, _bgNebulaTex, ScaleMode.StretchToFill);
        // 分层星点：慢速漂移 + 方块拖尾（星尘），绝对像素尺寸不随面板缩放；大星呼吸微闪
        _starField.UpdateStarMotion();
        _starField.DrawStars(px, py, pw, ph, _animAlpha);
        // 圆角细边框（SDF 九宫格，替代原 2px 硬边 + 四角方块）
        if (_panelBorderStyle != null)
            GUI.Box(bgRect, GUIContent.none, _panelBorderStyle);

        // 四角云纹角饰（太卜司星纹，半透明叠加）
        Color ornamentA = new Color(1f, 1f, 1f, 0.35f);
        GUI.color = ornamentA;
        GUI.DrawTexture(new Rect(px + 8f, py + 8f, 30f, 30f), _ornamentTL);
        GUI.DrawTexture(new Rect(px + pw - 38f, py + 8f, 30f, 30f), _ornamentTR);
        GUI.DrawTexture(new Rect(px + 8f, py + ph - 38f, 30f, 30f), _ornamentBL);
        GUI.DrawTexture(new Rect(px + pw - 38f, py + ph - 38f, 30f, 30f), _ornamentBR);
        GUI.color = _panelTint;

        // ═══════════════════════════════════════
        //  视图分发 — QQ 式两级界面 + 子面板（设置/便签/报告）
        //  第一级：会话列表窄条（热键打开默认）；第二级：左会话栏 + 右聊天区（双击进入）
        //  子面板：设置/便签/报告在对话框内部切换（星空风格，不再弹独立灰窗）
        // ═══════════════════════════════════════
        if (_currentView == PanelView.SessionList)
        {
            DrawSessionListView(px, py, pw, ph, mp);
        }
        // ——— 子面板视图（设置/便签/报告/消耗） ———
        else if (_currentView == PanelView.Settings || _currentView == PanelView.ModelSettings || _currentView == PanelView.Reminders
            || _currentView == PanelView.Report || _currentView == PanelView.Usage || _currentView == PanelView.Memory)
        {
            DrawSubPanelView(px, py, pw, ph, mp);
        }
        else
        {
            // 第二级：左侧会话栏占 SIDEBAR_W，聊天区整体右移
            DrawSessionSidebar(px, py, SIDEBAR_W, ph, mp);
            px += SIDEBAR_W;
            pw -= SIDEBAR_W;

            DrawChatArea(px, py, pw, ph, mp);
        }

        // ——— OpenClaw 审批模态弹窗（所有视图最上层绘制，敏感命令必须人工确认） ———
        // 不能放在 Chat 分支内部：SessionList/Settings/Reminders/Report/Usage
        // 也可能在外置窗口中收到审批请求，必须覆盖整块面板并阻断下层命中区。
        if (_approvalDialogOpen)
        {
            GUI.color = _panelTint;
            DrawApprovalDialog(panelX, panelY, panelW, panelH);
        }
    }

    // ==================================================================
    //  OpenClaw 审批弹窗（todo 6：关键敏感操作可审批）
    //  显示待执行命令，用户三选一：允许一次 / 总是允许 / 拒绝。
    // ==================================================================
    private void DrawApprovalDialog(float px, float py, float pw, float ph)
    {
        var pa = OpenClawBridge.PendingApproval;
        if (pa == null) { _approvalDialogOpen = false; return; }

        // ★ 模态语义：审批弹窗打开时，下层视图命中区域全部失效（外置模式遮罩挡点击）
        if (_externalRender) _extHitZones.Clear();
        // ——— 全面板半透明遮罩（模态，阻断下层交互） ———
        GUI.color = new Color(0f, 0f, 0f, 0.62f);
        GUI.DrawTexture(new Rect(px, py, pw, ph), _whiteTex);
        GUI.color = _panelTint;

        // ——— 弹窗主体（居中，红边警示） ———
        float w = Mathf.Min(480f, pw - 40f);
        float h = 210f;
        float wx = px + (pw - w) / 2f;
        float wy = py + (ph - h) / 2f;
        Rect box = new Rect(wx, wy, w, h);
        GUI.Box(box, GUIContent.none, _panelStyle);
        UiTextureFactory.DrawPixelRect(box, new Color(0.85f, 0.35f, 0.35f, 0.9f));   // 红边
        UiTextureFactory.DrawPixelRect(new Rect(box.x - 3f, box.y - 3f, box.width + 6f, 3f), new Color(0.85f, 0.35f, 0.35f, 0.5f));
        UiTextureFactory.DrawPixelRect(new Rect(box.x - 3f, box.yMax, box.width + 6f, 3f), new Color(0.85f, 0.35f, 0.35f, 0.5f));

        // 标题
        GUI.Label(new Rect(wx + 18f, wy + 14f, w - 36f, 24f), "⚠ OpenClaw 请求执行系统命令", _termTitleStyle);
        // 说明（★ 2026-08-17 验收 P2：提亮避免红边上对比度不足——改用浅灰高亮样式）
        GUI.Label(new Rect(wx + 18f, wy + 46f, w - 36f, 18f), "以下命令为敏感操作，需本座主人亲自批准：", _termLogStyle);
        // 命令内容（等宽高亮，换行显示；★ 加暗色底衬提升可读性）
        string cmd = string.IsNullOrEmpty(pa.command) ? (pa.title ?? "(无命令描述)") : pa.command;
        float cmdH = _termPromptStyle.CalcHeight(new GUIContent(cmd), w - 52f);
        if (cmdH > 52f) cmdH = 52f; // 命令最多显示 3 行
        Rect cmdRect = new Rect(wx + 18f, wy + 70f, w - 52f, cmdH);
        UiTextureFactory.DrawPixelRect(cmdRect, new Color(0.10f, 0.06f, 0.16f, 0.85f)); // 暗紫底衬
        GUI.Label(cmdRect, cmd, _termPromptStyle);
        // 超时倒计时
        float remain = Mathf.Max(0f, 60f - (Time.time - _approvalShownAt));
        GUI.Label(new Rect(wx + 18f, wy + 70f + cmdH + 10f, w - 36f, 16f),
            $"{(int)remain}s 后自动拒绝", _termLogDimStyle);

        // ——— 三选一按钮：允许一次 / 总是允许 / 拒绝 ———
        float bw = (w - 60f) / 3f;
        float by = wy + h - 46f;
        if (GUI.Button(new Rect(wx + 18f, by, bw, 30f), "✓ 允许一次", _termToolBtnStyle))
            ResolveApproval("allow-once");
        if (GUI.Button(new Rect(wx + 24f + bw, by, bw, 30f), "↻ 总是允许", _termToolBtnStyle))
            ResolveApproval("allow-always");
        if (GUI.Button(new Rect(wx + 30f + bw * 2f, by, bw, 30f), "✕ 拒绝", _termToolBtnStyle))
            ResolveApproval("deny");
        // 外部命中：审批三按钮（外置模式弹窗模态只响应这三个区域）
        RegisterExtHit(new Rect(wx + 18f, by, bw, 30f), () => ResolveApproval("allow-once"));
        RegisterExtHit(new Rect(wx + 24f + bw, by, bw, 30f), () => ResolveApproval("allow-always"));
        RegisterExtHit(new Rect(wx + 30f + bw * 2f, by, bw, 30f), () => ResolveApproval("deny"));

        // 防穿透：弹窗打开时吞掉所有鼠标事件
        if (Event.current.type == EventType.MouseDown)
            Event.current.Use();
    }

    // ==================================================================
    //  QQ 式两级界面：第一级「会话列表」视图（热键打开默认）
    //  布局参照 QQ 窄条：标题栏 50 → 搜索/新建 32 → 会话列表（滚动）→ 底部工具
    // ==================================================================

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

        // ——— 面板背景 ——— 太卜司星空（藏蓝夜空：上深蓝 → 下墨蓝，α≈0.95；蓝底紫饰双色调避免单紫色治）
        _bgTex = UiTextureFactory.MakeGradientTex(64, 64,
            new Color(0.10f, 0.15f, 0.32f, 0.95f),
            new Color(0.04f, 0.07f, 0.16f, 0.95f), true);
        _panelStyle = new GUIStyle { normal = { background = _bgTex } };
        // 左上冷蓝光晕（模拟场景打光，克制减alpha）+ 星云斑块（蓝紫双色调，破色带）
        _bgGlowTex = UiTextureFactory.MakeGlowTex(256, new Color(0.35f, 0.42f, 0.80f, 0.32f), 1.0f, new Vector2(0.08f, 0.10f));
        _bgNebulaTex = UiTextureFactory.MakeNebulaTex(256, 256, 42);
        // 分层星点：大星（40px 光晕+白芯过曝）+ 中星（24px）+ 小星（十字点），绝对像素尺寸不随面板拉伸
        _starField.Init(42);  // 分层星点：纹理 + 位置生成（StarField）
        // 圆角细边框（SDF 九宫格：四角 16px 圆弧，线宽 2.5px，亮蓝紫提高可见性）
        _panelBorderStyle = new GUIStyle
        {
            normal = { background = UiTextureFactory.GenRoundedBorderTex(64, 16f, 2.5f, new Color(0.55f, 0.65f, 1.00f, 0.90f)) },
            border = new RectOffset(16, 16, 16, 16)
        };
        // 太极发送按钮 + 卦象装饰
        _taijiTex = UiTextureFactory.MakeTaijiTex(30);
        _hexagramTex = UiTextureFactory.GenHexagramTex(12, 12, new Color(0.92f, 0.82f, 0.56f, 0.92f));
        // 独立窗口图标（两窗方块；不用 ⧉ 字形——等宽字体无字形会渲染空白）
        _extWindowIconTex = UiTextureFactory.GenExtWindowTex(22, 20, new Color(0.78f, 0.66f, 0.98f, 0.95f));

        // ——— 顶部装饰线（紫） ———
        _accentLineTex = UiTextureFactory.MakeTex(1, 1, cAccent);

        // ——— 分隔线 ———
        _separatorTex = UiTextureFactory.MakeTex(1, 1, new Color(0.45f, 0.35f, 0.65f, 0.25f));
        _separatorStyle = new GUIStyle { normal = { background = _separatorTex } };

        // ——— 输入框背景 ——— 圆角胶囊（复刻 ChatBubble 圆角风格，替代直角 1×1）
        _inputBgTex = UiTextureFactory.GenRoundedRect(64, 48, 10, new Color(0.16f, 0.13f, 0.27f, 0.92f));
        _inputHoverBgTex = UiTextureFactory.GenRoundedRect(64, 48, 10, new Color(0.23f, 0.18f, 0.37f, 0.96f));
        _inputGlowTex = UiTextureFactory.GenGlowRoundedRect(64, 48, 10, new Color(0.48f, 0.36f, 0.72f, 0.42f));
        _inputStyle = new GUIStyle
        {
            normal = { textColor = cTextMain, background = _inputBgTex },
            hover = { textColor = cTextMain, background = _inputHoverBgTex },
            focused = { textColor = Color.white, background = _inputHoverBgTex },
            fontSize = 18,
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
            fontSize = 18,
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
            normal = { background = UiTextureFactory.MakeTex(1, 1, new Color(0.11f, 0.09f, 0.15f, 0.92f)) }
        };

        _termTitleStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 16, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.90f, 0.80f, 0.58f, 1f) },  // 太卜司金
            alignment = TextAnchor.MiddleLeft
        };
        _termStatusStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14,
            normal = { textColor = new Color(0.58f, 0.55f, 0.65f, 0.9f) },
            alignment = TextAnchor.MiddleLeft
        };
        _termTimeStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14,
            normal = { textColor = new Color(0.58f, 0.55f, 0.65f, 0.9f) },
            alignment = TextAnchor.MiddleRight
        };
        _termToolBtnStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 15,
            normal = { textColor = new Color(0.66f, 0.62f, 0.76f, 0.9f) },
            hover = { textColor = new Color(0.75f, 0.62f, 0.98f, 1f) },
            alignment = TextAnchor.MiddleCenter
        };
        _termToolBtnHoverStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 15, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.80f, 0.68f, 1.00f, 1f) },
            hover = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter
        };
        _toolTipStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 12,
            normal = { textColor = new Color(0.88f, 0.83f, 0.98f, 1f) },
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 2, 2),
            clipping = TextClipping.Clip
        };
        _termLogStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 15,
            normal = { textColor = new Color(0.80f, 0.72f, 0.95f, 1f) },
            alignment = TextAnchor.UpperLeft
        };
        _termLogUserStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 15,
            normal = { textColor = new Color(0.80f, 0.90f, 0.98f, 1f) },
            alignment = TextAnchor.UpperLeft
        };
        _termLogDimStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 15, wordWrap = true,
            normal = { textColor = new Color(0.55f, 0.54f, 0.60f, 0.9f) },
            alignment = TextAnchor.UpperLeft
        };
        _emptyStateTitleStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 16, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.76f, 0.68f, 0.92f, 0.92f) },
            alignment = TextAnchor.MiddleCenter
        };
        _emptyStateHintStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 13,
            normal = { textColor = new Color(0.55f, 0.54f, 0.64f, 0.82f) },
            alignment = TextAnchor.MiddleCenter
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
            normal = { textColor = Color.white, background = _transparentTex },
            focused = { textColor = Color.white, background = _transparentTex },
            hover = { textColor = Color.white, background = _transparentTex },
            active = { textColor = Color.white, background = _transparentTex },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 6, 4, 4),
            clipping = TextClipping.Clip
        };
        _termPlaceholderStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 15,
            normal = { textColor = new Color(0.55f, 0.52f, 0.62f, 0.85f) },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 6, 4, 4)
        };

        // ——— QQ 式对话气泡样式 ——— 符玄左紫(渐变+金边)、用户右蓝(渐变+浅蓝边)
        _bubbleFxTex = UiTextureFactory.GenBubbleTex(64, 48, 10,
            new Color(0.45f, 0.33f, 0.62f, 0.96f), new Color(0.30f, 0.20f, 0.46f, 0.96f),
            new Color(0.88f, 0.78f, 0.55f, 0.95f));
        _bubbleUserTex = UiTextureFactory.GenBubbleTex(64, 48, 10,
            new Color(0.24f, 0.42f, 0.60f, 0.96f), new Color(0.14f, 0.28f, 0.44f, 0.96f),
            new Color(0.55f, 0.72f, 0.95f, 0.9f));
        _bubbleFxStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, wordWrap = true, richText = false,
            normal = { background = _bubbleFxTex, textColor = new Color(0.90f, 0.85f, 0.99f, 1f) },
            padding = new RectOffset(14, 14, 11, 11),
            border = new RectOffset(10, 10, 10, 10),
            alignment = TextAnchor.UpperLeft,
            clipping = TextClipping.Clip
        };
        _bubbleUserStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, wordWrap = true, richText = false,
            normal = { background = _bubbleUserTex, textColor = new Color(0.85f, 0.92f, 0.99f, 1f) },
            padding = new RectOffset(14, 14, 11, 11),
            border = new RectOffset(10, 10, 10, 10),
            alignment = TextAnchor.UpperLeft,
            clipping = TextClipping.Clip
        };
        _userAvatarTex = UiTextureFactory.GenRoundedRect(24, 24, 8, new Color(0.30f, 0.24f, 0.45f, 0.95f));
        _userAvatarStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 14, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0),
            normal = { textColor = new Color(0.85f, 0.80f, 0.98f, 1f) }
        };
        _inputBarBgStyle = new GUIStyle
        {
            normal = { background = UiTextureFactory.MakeTex(1, 1, new Color(0.09f, 0.08f, 0.13f, 0.78f)) }
        };
        _statusDotTex = UiTextureFactory.MakeCircleTex(8, Color.white);
        _pixelFxTex = LoadPixelFx(); // ★多模态：优先加载 Resources/PixelFuXuan.png，回退代码生成
        // 像素符玄动态形象（17x24 网格图，睁眼/闭眼两帧，×4 放大）
        _mascotOpenTex = LoadMascot(true);
        _mascotBlinkTex = LoadMascot(false);
        // ★ 日志风暴修复（2026-08）：此前用 `new GUIStyle()` 空样式传参 BeginScrollView，
        //    Unity 绘制滚动条时回退查找 skin 的 upbutton/downbutton，每帧刷 2 条
        //    "Unable to find style 'upbutton' in skin 'GameSkin'" 错误 → 67MB/98万行日志拖垮桌宠。
        //    GUIStyle.none 让 Unity 内部直接跳过滚动条绘制，不再触发样式查找。
        _invisibleScrollbar = GUIStyle.none;
        _closeBtnStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.75f, 0.70f, 0.82f, 0.85f) },
            hover = { textColor = new Color(1f, 0.45f, 0.45f, 1f) },
            active = { textColor = Color.white },
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };

        // ——— 发送按钮 ——— 圆形紫色（hover 提亮）
        _sendBtnTex = UiTextureFactory.MakeCircleTex(30, new Color(0.55f, 0.40f, 0.85f, 0.30f));
        _sendBtnHoverTex = UiTextureFactory.MakeCircleTex(30, new Color(0.66f, 0.50f, 0.95f, 0.65f));
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
        _toolTex = UiTextureFactory.MakeCircleTex(34, new Color(0.50f, 0.35f, 0.80f, 0.18f));
        _toolHoverTex = UiTextureFactory.MakeCircleTex(34, new Color(0.60f, 0.45f, 0.90f, 0.40f));
        _glowTex = UiTextureFactory.MakeCircleTex(48, cAccentGlow);

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
        _ornamentTL = UiTextureFactory.GenCornerOrnament(40, cAccentDim, true);
        _ornamentTR = UiTextureFactory.GenCornerOrnament(40, cAccentDim, false);
        _ornamentBR = UiTextureFactory.GenCornerOrnament(40, cAccentDim, false);
        _ornamentBL = UiTextureFactory.GenCornerOrnament(40, cAccentDim, true);

        // 白图（保留）
        _whiteTex = UiTextureFactory.MakeTex(1, 1, Color.white);

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
        _borderTex = UiTextureFactory.MakeTex(2, 2, new Color(0.58f, 0.42f, 0.88f, 0.9f));

        // 日志交替行背景（极淡紫，提升可读性）
        _logRowAltTex = UiTextureFactory.MakeTex(1, 1, new Color(0.14f, 0.10f, 0.22f, 0.35f));

        // 标题栏像素渐变（上深下浅，3 级台阶模拟像素色带）
        // 标题栏像素渐变（顶部玻璃高光 → 中紫 → 底部深紫）
        _titleBarPixelTex = new Texture2D(1, 4, TextureFormat.ARGB32, false);
        _titleBarPixelTex.wrapMode = TextureWrapMode.Clamp;
        _titleBarPixelTex.SetPixel(0, 0, new Color(0.32f, 0.24f, 0.44f, 0.55f)); // 顶部玻璃高光
        _titleBarPixelTex.SetPixel(0, 1, new Color(0.15f, 0.11f, 0.22f, 0.95f));
        _titleBarPixelTex.SetPixel(0, 2, new Color(0.11f, 0.08f, 0.17f, 0.95f));
        _titleBarPixelTex.SetPixel(0, 3, new Color(0.08f, 0.06f, 0.13f, 0.95f));
        _titleBarPixelTex.Apply();

        // 输入栏像素背景（略深，与日志区区分）
        _inputBarPixelTex = UiTextureFactory.MakeTex(1, 1, new Color(0.10f, 0.07f, 0.16f, 0.90f));
        _transparentTex = UiTextureFactory.MakeTex(1, 1, new Color(0f, 0f, 0f, 0f));
        // 输入样式在前面初始化，此处补绑定透明背景，避免 GUIStyle 使用默认黑底。
        _termInputStyle.normal.background = _transparentTex;
        _termInputStyle.focused.background = _transparentTex;
        _termInputStyle.hover.background = _transparentTex;
        _termInputStyle.active.background = _transparentTex;

        // 子面板样式（设置/便签/报告 — 星空紫金主题）→ 已拆分至 RightPanel.SubPanels.cs
        InitSubPanelStyles();

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
        // ===== 纹理生成（17 个静态方法）已拆分至 UiTextureFactory.cs；星空系统已拆分至 StarField.cs（2026-08-14）=====

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

    /// <summary>当前应显示的形象纹理：表情激活 → 表情帧；否则睁眼/闭眼帧</summary>
    private Texture2D GetActiveMascotTex()
    {
        if (!string.IsNullOrEmpty(_mascotEmotion))
        {
            Texture2D t = GetMascotEmoteTex(_mascotEmotion);
            if (t != null) return t;
        }
        return _mascotBlinking ? _mascotBlinkTex : _mascotOpenTex;
    }

    /// <summary>表情名 → 表情形象帧（惰性生成缓存）</summary>
    private Texture2D GetMascotEmoteTex(string expName)
    {
        if (string.IsNullOrEmpty(expName)) return null;
        Texture2D cached;
        if (_mascotEmoteTex.TryGetValue(expName, out cached)) return cached;
        cached = LoadMascotEmote(expName);
        if (cached != null) _mascotEmoteTex[expName] = cached;
        return cached;
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
        // ★ SetPixel(0,0) 是纹理底部，而 rows[0] 是符号顶部 → 必须行反转，否则 GUI 显示上下颠倒
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                tex.SetPixel(x, rows.Length - 1 - y, rows[y][x] == '#' ? c : Color.clear);
        tex.Apply();
        _emblemTex[key] = tex;
        return tex;
    }

    /// <summary>
    /// 生成表情形象帧：在 17x24 原图上重绘眼睛/眉毛/嘴/腮红（表情包），×4 放大。
    /// 脸部坐标（x=列, y=行）：眼睛 y=12 x=5/x=9（紫眼）；眉毛 y=10 x=4-5/x=9-10；腮红 y=13-14；嘴 y=15。
    /// </summary>
    private static Texture2D LoadMascotEmote(string expName)
    {
        var src = Resources.Load<Texture2D>("PixelFuXuan_17x24");
        if (src == null) return null;
        try
        {
            var px = src.GetPixels32();
            int w = src.width, h = src.height;
            Color32 skin   = new Color32(255, 243, 235, 255); // 肤色
            Color32 eye    = new Color32(159, 117, 148, 255); // 原紫眼
            Color32 line   = new Color32(202, 202, 212, 255); // 闭眼缝线
            Color32 dark   = new Color32(72, 70, 78, 255);    // 深描边（眉/嘴）
            Color32 blushC = new Color32(255, 150, 175, 255); // 腮红粉
            Color32 tearC  = new Color32(140, 200, 255, 255); // 泪滴蓝

            void Set(int x, int y, Color32 c) { if (x >= 0 && x < w && y >= 0 && y < h) px[y * w + x] = c; }
            // 微笑嘴：y15 行中间 x6-10 改肤色，保留两端黑 x5/x11 作嘴角
            void SmileMouth() { for (int mx = 6; mx <= 10; mx++) Set(mx, 15, skin); }

            switch (expName)
            {
                case "happy":   // ^ ^ 眯眼笑 + 微笑嘴
                    Set(5, 12, line); Set(9, 12, line); SmileMouth();
                    break;
                case "angry":   // 怒眉压低 + 抿嘴
                    Set(5, 10, dark); Set(9, 10, dark);
                    Set(7, 15, dark); Set(8, 15, dark); Set(9, 15, dark);
                    break;
                case "sad":     // 八字垂眉 + 泪滴 + 委屈嘴
                    Set(4, 10, dark); Set(10, 10, dark);
                    Set(5, 13, tearC); Set(9, 13, tearC);
                    Set(7, 15, dark); Set(8, 15, dark); Set(9, 15, dark);
                    break;
                case "surprise": // 2x2 大眼 ○○ + O 形嘴
                    for (int ey = 11; ey <= 12; ey++)
                    { Set(4, ey, eye); Set(5, ey, eye); Set(8, ey, eye); Set(9, ey, eye); }
                    Set(7, 14, dark); Set(6, 15, dark); Set(8, 15, dark); Set(7, 16, dark); Set(7, 15, skin);
                    break;
                case "confused": // 挑眉 + 右眼眯 + 歪嘴
                    Set(5, 10, dark);
                    Set(9, 12, line);
                    Set(8, 15, dark);
                    break;
                case "sleepy":  // 闭眼 + 哈欠 O 嘴
                    Set(5, 12, line); Set(9, 12, line);
                    Set(7, 14, dark); Set(7, 15, dark); Set(8, 15, dark);
                    break;
                case "blush":   // 闭眼 + 大红脸 + 抿嘴
                    Set(5, 12, line); Set(9, 12, line);
                    for (int bx = 4; bx <= 5; bx++) { Set(bx, 13, blushC); Set(bx, 14, blushC); }
                    for (int bx = 9; bx <= 10; bx++) { Set(bx, 13, blushC); Set(bx, 14, blushC); }
                    Set(7, 15, line); Set(8, 15, line);
                    break;
                case "love":    // ♥ 微笑嘴 + 脸颊粉（爱心只由右上角徽章表达，爱心眼太小糊脸，删掉）
                    Set(4, 13, blushC); Set(10, 13, blushC);
                    SmileMouth();
                    break;
                case "tear":    // 泪汪汪（双竖泪滴 + 撇嘴）
                    Set(5, 13, tearC); Set(5, 14, tearC);
                    Set(9, 13, tearC); Set(9, 14, tearC);
                    Set(7, 15, dark); Set(8, 15, dark);
                    break;
                default:
                    return null;
            }

            // ×4 放大（与 LoadMascot 一致，Point 锐利）
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
            Debug.LogWarning("[RightPanel] 表情形象帧生成失败(" + e.GetType().Name + ")，跳过");
            return null;
        }
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
            h += 4f; // 仅留少量中文换行余量，避免旧版按整字号补偿造成气泡上下间距过大
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
        // ★ OpenClaw 任务动态行（步骤/审批）在历史重建后保留，不丢失
        for (int i = 0; i < _liveLogLines.Count; i++)
            _logLines.Add(_liveLogLines[i]);
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
        foreach (var kv in _mascotEmoteTex)
            if (kv.Value != null) Destroy(kv.Value);
        _mascotEmoteTex.Clear();
        if (_mascotOpenTex != null) Destroy(_mascotOpenTex);
        if (_mascotBlinkTex != null) Destroy(_mascotBlinkTex);
        if (_pixelFxTex != null) Destroy(_pixelFxTex);
        if (_statusDotTex != null) Destroy(_statusDotTex);
        if (_scanlineTex != null) Destroy(_scanlineTex);
        if (_borderTex != null) Destroy(_borderTex);
        if (_logRowAltTex != null) Destroy(_logRowAltTex);
        if (_titleBarPixelTex != null) Destroy(_titleBarPixelTex);
        if (_inputBarPixelTex != null) Destroy(_inputBarPixelTex);
        if (_transparentTex != null) Destroy(_transparentTex);
        if (_monoFont != null) Destroy(_monoFont);
        // ★ 退出顺序：先停外置窗口线程（避免窗口线程访问已释放的 RT/NativeArray 或与
        //   引擎 D3D 设备销毁竞态 → destroyTJDevice 崩溃），再释放渲染资源
        DisableExternalMode();
        ExternalChatWindow.Shutdown();
        if (_chatRT != null) { _chatRT.Release(); Destroy(_chatRT); }
        if (_extReadBack.IsCreated) _extReadBack.Dispose(); // ★ NativeArray 必须释放（泄漏=内存增长）
    }

    // ═══════════════════════════════════════════════════════════
    //  外部聊天窗口（独立窗口）— 开关 / 渲染捕获 / 输入桥
    // ═══════════════════════════════════════════════════════════
    public bool IsExternalMode => _externalMode;

    /// <summary>切换独立窗口模式（聊天标题栏 ⧉ 按钮 / 测试命令触发）</summary>
    public void ToggleExternalMode()
    {
        if (_externalMode) DisableExternalMode();
        else EnableExternalMode();
    }

    private void EnableExternalMode()
    {
        if (_externalMode) return;
        // ★ 必须确保面板打开：OnGUI 开头 !_isOpen 会提前 return，外置窗口会空白
        if (!_isOpen)
        {
            _isOpen = true;
            _closing = false;
            _animAlpha = 1f;
            ApplyViewSize(); // 确保 _panelRect 有合法尺寸
        }
        _runInBackgroundBeforeExternal = Application.runInBackground;
        Application.runInBackground = true;
        _externalMode = true;
        // 外置窗口必须保持普通（非置顶）窗口；进入外置模式时主动刷新 Unity
        // 全屏透明层的穿透样式，避免沿用上一帧“宠物/内嵌面板可交互”状态而挡住外置窗口。
        if (_windowOverlay != null)
        {
            _windowOverlay.SetClickThrough(true);
            _windowOverlay.RefreshExternalWindowHole();
        }
        ExternalChatWindow.OnSendText += OnExternalSend;
        ExternalChatWindow.OnClosed += OnExternalClosed;
        ExternalChatWindow.OnPanelClick += OnExternalPanelClick;
        ExternalChatWindow.OnPanelMouseMove += OnExternalPanelMouseMove;
        _externalInputDirty = true;
        _lastExternalComposition = string.Empty;
        _lastExternalInputVersion = ExternalChatWindow.GetInputTextVersion();
        SetExternalUiPerformanceMode(true);
        // 整面板外置：窗口尺寸 = 面板视图 + 自绘标题栏（客户区与 RT 1:1）
        int w = Mathf.Max(320, Mathf.RoundToInt(_panelRect.width));
        int h = Mathf.Max(200, Mathf.RoundToInt(_panelRect.height));
        ExternalChatWindow.Show(w, h);
        ExternalChatWindow.ShowInputBar(false); // 原生 EDIT 仅作屏外输入桥，Unity 绘制可见文字/光标/背景
        Debug.Log("[RightPanel] ⧉ 已切换到独立面板窗口（可被其他窗口遮挡）");
    }

    private void DisableExternalMode()
    {
        if (!_externalMode) return;
        _externalMode = false;
        _externalInputDirty = false;
        _lastExternalComposition = string.Empty;
        _lastExternalInputVersion = -1;
        SetExternalUiPerformanceMode(false);
        ExternalChatWindow.OnSendText -= OnExternalSend;
        ExternalChatWindow.OnClosed -= OnExternalClosed;
        ExternalChatWindow.OnPanelClick -= OnExternalPanelClick;
        ExternalChatWindow.OnPanelMouseMove -= OnExternalPanelMouseMove;
        ExternalChatWindow.Hide();
        if (_windowOverlay != null)
            _windowOverlay.RefreshExternalWindowHole(false);
        Application.runInBackground = _runInBackgroundBeforeExternal;
        Debug.Log("[RightPanel] 已退出独立面板窗口");
    }

    /// <summary>独立窗口面板区点击 → 命中表处理（主线程）</summary>
    private void OnExternalPanelClick(float x, float y, bool isDoubleClick)
    {
        // 双击先走单击命中（会话列表双击进聊天由 EnterChat 处理；此处简化：双击查表执行）
        HandleExternalInput(x, y, isDoubleClick);
    }

    private void OnExternalPanelMouseMove(float x, float y)
    {
        if (_testExternalMouseOverride) return;
        _externalMousePos = new Vector2(x, y);
        // 鼠标移动只改变外置 RT 中的 hover 提示，不改变命中表或业务状态。
        GUI.changed = true;
    }

    private void OnExternalSend(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string message = text.Trim();
        Debug.Log($"[RightPanel] 外部窗口发送: {message}");

        // DoSend() 已经在窗口线程清空了 Win32 EDIT。这里不能再把已发送文本写回
        // Unity 渲染字层，否则当 MainThreadDispatcher 与 RightPanel.Update 的执行
        // 顺序交错时，旧句子会永久残留在输入栏，后续输入看起来像被冻结。
        _inputText = string.Empty;
        _lastExternalInputVersion = ExternalChatWindow.GetInputTextVersion();
        _lastExternalComposition = string.Empty;
        _externalInputDirty = true;
        GUI.changed = true;

        if (_chat != null) _chat.SendMessage(message, null);
        else Debug.LogWarning("[RightPanel] 外部窗口发送时 ChatManager 未就绪");
    }

    private void OnExternalClosed()
    {
        // 独立窗口 X → 先立即清掉 Unity 内嵌面板，再解除外置状态；两者生命周期互不串联。
        HideEmbeddedPanelImmediately();
        DisableExternalMode();
    }

    private void SetExternalUiPerformanceMode(bool enabled)
    {
        if (_performanceMonitor == null)
        {
            _performanceMonitor = _pet != null ? _pet.GetPerformanceMonitor() : null;
            if (_performanceMonitor == null)
                _performanceMonitor = FindObjectOfType<PerformanceMonitor>();
        }
        if (_performanceMonitor != null)
            _performanceMonitor.SetExternalUiMode(enabled);
        else
            Debug.LogWarning("[RightPanel] 未找到 PerformanceMonitor，外置 UI 无法临时提升帧率");
    }

    /// <summary>外置窗口不再额外绘制标题栏，直接使用面板自身标题行。</summary>
    private const int EXT_TITLE_BAR_H = 0;

    /// <summary>把整个面板渲染到独立窗口（IMGUI → RenderTexture → 异步读回 BGRA → 推送）
    /// ★ 无边框窗口：RT 顶部 44px 自绘星空标题栏
    /// ★ 性能：渲染每帧执行（GPU 侧，60fps 星空动画流畅）；读回用 AsyncGPUReadback 异步
    ///   （不阻塞主线程），推送按 _lastExtCapture 节流</summary>
    private void DrawExternalPanelToTexture()
    {
        // IMGUI 只在 Repaint 事件提交绘制，非 Repaint 时直接返回（否则 RT 空）
        if (Event.current.type != EventType.Repaint)
            return;

        // 渲染尺寸 = 当前面板视图尺寸 + 顶部自绘标题栏
        int rtW = Mathf.Max(64, Mathf.RoundToInt(_panelRect.width));
        int rtH = Mathf.Max(64, Mathf.RoundToInt(_panelRect.height));
        if (_chatRT == null || _chatRT.width != rtW || _chatRT.height != rtH)
        {
            // ★ 2026-08-17 崩溃修复：RT/NativeArray 重建前必须使在途 AsyncGPUReadback 回调失效。
            //   旧回调稍后触发时会访问已 Dispose 的 _extReadBack → destroyTJDevice 崩溃
            //   （崩溃栈：SetBuffer ← DrawExternalPanelToTexture b__0 AsyncGPUReadbackRequest）。
            //   用代际计数：重建即递增，回调闭包捕获当时 gen，不匹配则丢弃（资源已换新）。
            _extReadGen++;
            if (_chatRT != null) _chatRT.Release();
            // ★ BGRA32：AsyncGPUReadback 读出 BGRA 字节序，与 SetDIBitsToDevice 匹配（ARGB32 会 R/B 互换变橙色）
            _chatRT = new RenderTexture(rtW, rtH, 0, RenderTextureFormat.BGRA32);
            if (_extReadBack.IsCreated) _extReadBack.Dispose();
            _extReadBack = new Unity.Collections.NativeArray<byte>(rtW * rtH * 4, Unity.Collections.Allocator.Persistent,
                Unity.Collections.NativeArrayOptions.UninitializedMemory);
            _extReadPending = false;
            ExternalChatWindow.SetSize(rtW, rtH); // 窗口客户区跟随（面板+标题栏）
        }
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = _chatRT;
        GL.Clear(true, true, new Color(0.06f, 0.05f, 0.10f, 1f));
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        _externalRender = true;
        // 原生窗口消息在拖动、DPI 缩放或子控件焦点切换时可能暂时不派发；
        // 每帧从窗口坐标轮询，确保外置 RT 的 hover 与真实鼠标保持同步。
        // 鼠标位置由 ExternalChatWindow 的合并后 WM_MOUSEMOVE 事件驱动。
        // 不要在每帧调用 TryGetMousePosition：它会从 Unity 主线程同步访问另一个线程的窗口。
        _extHitZones.Clear();    // 渲染帧重建命中表（面板局部坐标）
        _extTitleZones.Clear();  // 渲染帧重建标题栏命中表（客户区坐标）
        try
        {
            DrawExternalTitleBar(rtW); // 保留调用点兼容旧布局；当前实现不再绘制独立标题栏
            DrawPanelContent(0, 0, rtW, rtH, _externalMousePos);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RightPanel] 外部面板渲染异常: {e}");
        }
        _externalRender = false;
        _extHitView = _currentView;
        if (_pendingExtInput && _pendingExtInputView == _currentView)
        {
            float pendingX = _pendingExtInputX;
            float pendingY = _pendingExtInputY;
            bool pendingDoubleClick = _pendingExtInputDoubleClick;
            _pendingExtInput = false;
            // 当前帧已经完成命中表重建，再回放刚才的点击；避免切页后的首击丢失。
            HandleExternalInput(pendingX, pendingY, pendingDoubleClick);
        }
        GUI.matrix = prevMatrix;
        RenderTexture.active = prev;

        // 异步读回 + 推送（High=60 / Normal=45 / Low=30fps；读回不阻塞主线程）
        // ★ 防卡死：_extReadPending 超时兜底（读回 >0.5s 未完成视为异常，重置继续）——
        //   RT 重建/NativeArray 更换时旧回调可能永不触发，导致 pending 永久 true 画面冻结
        if (_extReadPending && Time.time - _lastExtReadStart > 0.5f)
        {
            Debug.LogWarning("[RightPanel] 异步读回超时，重置 pending（防冻结）");
            _extReadPending = false;
        }
        // 外置聊天 UI 按当前性能档位推送：High=60 / Normal=45 / Low=30 FPS。
        // 输入变化仍走 dirty 即时通道，确保中文组词和光标反馈不等待普通节流。
        float uiFps = _performanceMonitor != null ? Mathf.Clamp(_performanceMonitor.targetFPS, 15f, 60f) : 60f;
        float captureInterval = 1f / uiFps;
        if ((_externalInputDirty || Time.time - _lastExtCapture >= captureInterval)
            && !_extReadPending && _extReadBack.IsCreated)
        {
            _lastExtCapture = Time.time;
            _externalInputDirty = false;
            _lastExtReadStart = Time.time;
            _extReadPending = true;
            int gen = _extReadGen; // ★ 捕获当前代际：RT/NativeArray 重建后此回调作废
            // ★ 2026-08-17 崩溃修复（v2）：改用 AsyncGPUReadback.Request——每次请求分配独立
            //   NativeArray，由 Unity 管理其生命周期；回调里 CopyTo 预分配接收数组。
            //   之前用 RequestIntoNativeArray(ref _extReadBack, ...) 要求传入的 buffer 在回调
            //   期间持续有效，而视图切换会 Dispose+重建它 → 违反约束 → MallocTracked 崩溃
            //   （崩溃栈：NativeArray ctor ← DrawExternalPanelToTexture ← OnGUI）。
            UnityEngine.Rendering.AsyncGPUReadback.Request(_chatRT, 0, (req) =>
            {
                if (req.hasError) { _extReadPending = false; return; }
                // ★ 代际校验：若读回期间 RT/NativeArray 已被重建（视图切换），丢弃旧数据
                if (gen != _extReadGen) { _extReadPending = false; return; }
                try
                {
                    var data = req.GetData<byte>();
                    if (data.IsCreated && data.Length >= _extReadBack.Length)
                        data.CopyTo(_extReadBack); // 预分配接收数组，零新分配
                    ExternalChatWindow.SetBuffer(_extReadBack, rtW, rtH);
                }
                catch (Exception e) { Debug.LogWarning($"[RightPanel] 外部窗口像素推送失败: {e.Message}"); }
                _extReadPending = false;
            });
        }
    }

    /// <summary>外置窗口自绘星空标题栏（紫色渐变 + 星点 + 标题 + 最小化/关闭按钮，像素风）</summary>
    private void DrawExternalTitleBar(int rtW)
    {
        // 独立窗口不再有第二层“独立面板”标题栏；最小化和关闭按钮绘制在
        // DrawChatArea / DrawSessionListView 自身的标题行，与 Unity 面板共用同一坐标。
    }
}
