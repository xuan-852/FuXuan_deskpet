using System;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 右键上下文菜单 — 分类标签布局
/// 标签：设置 | 动作 | 聊天
///
/// 用 OnGUI 绘制，无需 Canvas/UIPrefab
/// </summary>
public class ContextMenu : MonoBehaviour
{
    private DesktopPet _pet;
    private Live2DRenderer _renderer;
    private WindowOverlay _window;
    private ChatManager _chat;
    private ReminderManager _reminders;

    // ===== 标签系统 =====
    private enum Tab { 设置, 动作, 聊天, 便签 }
    private Tab _currentTab = Tab.设置;
    private string[] _tabNames = { "⚙ 设置", "▶ 动作", "💬 聊天", "📋 便签" };

    // ===== 菜单状态 =====
    private bool _isOpen = false;
    private Rect _menuRect;
    private float _menuWidth = 300f;
    private float _menuHeight = 420f;
    private Vector2 _scrollPos = Vector2.zero;
    // 拖动
    private bool _isDragging = false;
    private Vector2 _dragMouseOffset = Vector2.zero;

    // ===== 权重编辑器副本 =====
    private int _wLeftEdge, _wRightEdge, _wLeftTime, _wRightTime, _wStop;

    // ===== 聊天 =====
    private string _chatInputText = "";
    private string _chatStatusMsg = "";
    private Color _chatStatusColor = Color.gray;
    private Vector2 _chatScrollPos = Vector2.zero;
    private bool _chatShowConfig = false;

    // ===== 句子队列本地计时器（逐句重播） =====
    private int _localSentenceIdx = 0;
    private float _localSentenceTimer = 0f;
    private int _lastSentenceVersion = -1;
    private bool _isLocalAnimating = false;
    public float localSentenceInterval = 1.8f;



    // ===== 样式 =====
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _smallButtonStyle;
    private GUIStyle _closeButtonStyle;
    private GUIStyle _tabButtonStyle;
    private GUIStyle _tabButtonActiveStyle;
    private GUIStyle _textFieldStyle;
    private Texture2D _bgTexture;
    private Texture2D _sectionBg;
    private Texture2D _btnBg;
    private Texture2D _btnSmallBg;
    private Texture2D _tabBg;
    private Texture2D _tabActiveBg;
    private Texture2D _inputBg;
    private bool _stylesInitialized = false;

    void Start()
    {
        _pet = GetComponent<DesktopPet>();
        _renderer = GetComponent<Live2DRenderer>();
        _window = GetComponent<WindowOverlay>();
        if (_window == null) _window = FindObjectOfType<WindowOverlay>();

        // 聊天管理器
        _chat = GetComponent<ChatManager>();
        if (_chat == null) _chat = gameObject.AddComponent<ChatManager>();

        // 提醒管理器（使用单例，不重复创建）
        _reminders = ReminderManager.Instance;
        if (_reminders == null)
        {
            _reminders = GetComponent<ReminderManager>();
            if (_reminders == null) _reminders = gameObject.AddComponent<ReminderManager>();
        }

        // 工具调用（符玄法阵）
        var toolInvoker = GetComponent<ToolCallInvoker>();
        if (toolInvoker == null) toolInvoker = gameObject.AddComponent<ToolCallInvoker>();
        _chat.toolInvoker = toolInvoker;

        // 自动聊天（定时问候 + 互动事件 + 气泡）
        var autoChat = GetComponent<AutoChat>();
        if (autoChat == null) gameObject.AddComponent<AutoChat>();
    }

    void InitStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        _bgTexture = MakeTex(1, 1, new Color(0.15f, 0.15f, 0.17f, 0.95f));
        _sectionBg = MakeTex(1, 1, new Color(0.12f, 0.12f, 0.14f, 0.9f));
        _btnBg = MakeTex(1, 1, new Color(0.25f, 0.25f, 0.28f, 1f));
        _btnSmallBg = MakeTex(1, 1, new Color(0.3f, 0.3f, 0.33f, 1f));
        _tabBg = MakeTex(1, 1, new Color(0.2f, 0.2f, 0.22f, 1f));
        _tabActiveBg = MakeTex(1, 1, new Color(0.35f, 0.25f, 0.4f, 1f));
        _inputBg = MakeTex(1, 1, new Color(0.08f, 0.08f, 0.1f, 0.9f));

        _titleStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.9f, 0.6f, 0.8f), background = _sectionBg },
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 6, 6)
        };

        _sectionStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.7f, 0.7f, 0.8f), background = _sectionBg },
            fontStyle = FontStyle.Bold,
            fontSize = 12,
            padding = new RectOffset(6, 0, 4, 4)
        };

        _labelStyle = new GUIStyle
        {
            normal = { textColor = Color.white },
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(6, 0, 2, 2)
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            normal = { textColor = Color.white, background = _btnBg },
            hover = { background = MakeTex(1, 1, new Color(0.35f, 0.35f, 0.4f)) },
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(6, 6, 4, 4)
        };

        _smallButtonStyle = new GUIStyle(_buttonStyle)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(6, 6, 0, 0),
            fixedWidth = 24,
            fixedHeight = 22
        };

        _closeButtonStyle = new GUIStyle(_buttonStyle)
        {
            normal = { textColor = new Color(1f, 0.4f, 0.4f), background = _btnSmallBg },
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter
        };

        _tabButtonStyle = new GUIStyle(_buttonStyle)
        {
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f), background = _tabBg },
            fontSize = 11,
            padding = new RectOffset(4, 4, 4, 4),
            margin = new RectOffset(0, 0, 0, 0)
        };

        _tabButtonActiveStyle = new GUIStyle(_tabButtonStyle)
        {
            normal = { textColor = Color.white, background = _tabActiveBg },
            fontStyle = FontStyle.Bold
        };

        _textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            normal = { textColor = Color.white, background = _inputBg },
            fontSize = 11,
            padding = new RectOffset(4, 4, 3, 3)
        };

    }

    #region 公开接口

    public void Open(Vector2 screenPos)
    {
        _isOpen = true;
        _currentTab = Tab.设置;   // 默认打开设置
        _scrollPos = Vector2.zero;

        // ★ 重置聊天状态同步
        _lastSentenceVersion = -1;

        // ★ 暂停宠物运动
        if (_pet != null)
        {
            _pet.ForceStop();
            _pet.isPaused = true;
        }

        // 复制当前权重
        _wLeftEdge = _pet.taskWeightMoveLeftEdge;
        _wRightEdge = _pet.taskWeightMoveRightEdge;
        _wLeftTime = _pet.taskWeightMoveLeftTime;
        _wRightTime = _pet.taskWeightMoveRightTime;
        _wStop = _pet.taskWeightStopTime;

        // 定位菜单 — 相对于模型头顶位置
        float headCenterX = _pet.petX + _pet.petWidth / 2f;
        float headY = _pet.petY;
        float x = Mathf.Clamp(headCenterX - _menuWidth / 2f, 10, Screen.width - _menuWidth - 10);
        float y = Mathf.Clamp(headY - _menuHeight - 10, 10, Screen.height - _menuHeight - 10);
        _menuRect = new Rect(x, y, _menuWidth, _menuHeight);
    }

    public void Close()
    {
        _isOpen = false;

        if (_renderer != null)
            _renderer.OnForcedActionFinished -= OnForcedActionComplete;

        if (_pet != null)
        {
            _pet.isPaused = false;
            _pet.Resume();
        }
    }

    public bool IsOpen => _isOpen;
    public bool IsMouseOverMenu(Vector2 mousePos) => _isOpen && _menuRect.Contains(mousePos);

    #endregion

    #region 本地句子队列计时器

    void Update()
    {
        if (!_isLocalAnimating || _chat == null || !_chat.HasMultiSentenceReply || !_isOpen) return;

        _localSentenceTimer += Time.deltaTime;
        if (_localSentenceTimer >= localSentenceInterval)
        {
            _localSentenceTimer = 0f;
            _localSentenceIdx++;

            if (_localSentenceIdx >= _chat.SentenceList.Count)
            {
                _isLocalAnimating = false;
                _localSentenceIdx = _chat.SentenceList.Count - 1;
            }
        }
    }

    /// <summary>每次绘制聊天标签时调用：检测新回复，重置本地索引</summary>
    private void CheckLocalSentenceState()
    {
        if (_chat == null) return;

        // 有新回复 → 重置本地计时器
        if (_chat.SentenceVersionId != _lastSentenceVersion)
        {
            _lastSentenceVersion = _chat.SentenceVersionId;
            _localSentenceIdx = 0;
            _localSentenceTimer = 0f;
            _isLocalAnimating = _chat.HasMultiSentenceReply;
        }
    }

    #endregion

    #region 主绘制循环

    /// <summary>OnGUI 中用 Event.current 处理拖拽（不 e.Use()，不吞其他控件事件）</summary>
    private void HandleDragEvent(Event e)
    {
        if (!_isOpen) return;

        Rect titleBar = new Rect(_menuRect.x, _menuRect.y, _menuRect.width, 30);

        if (e.type == EventType.MouseDown && titleBar.Contains(e.mousePosition))
        {
            _isDragging = true;
            _dragMouseOffset = e.mousePosition - new Vector2(_menuRect.x, _menuRect.y);
        }
        else if (e.type == EventType.MouseUp)
        {
            _isDragging = false;
        }
        else if (e.type == EventType.MouseDrag && _isDragging)
        {
            Vector2 newPos = e.mousePosition - _dragMouseOffset;
            newPos.x = Mathf.Clamp(newPos.x, 0, Screen.width - _menuWidth);
            newPos.y = Mathf.Clamp(newPos.y, 0, Screen.height - _menuHeight);
            _menuRect.x = newPos.x;
            _menuRect.y = newPos.y;
        }
    }

    void OnGUI()
    {
        if (!_isOpen) return;
        InitStyles();

        // ★ 用 Event 处理拖拽（不 e.Use()，不干扰其他控件事件）
        HandleDragEvent(Event.current);

        // 背景（不消耗事件，纯绘制）
        GUI.Box(_menuRect, GUIContent.none, new GUIStyle { normal = { background = _bgTexture } });

        GUILayout.BeginArea(_menuRect);

        // ===== 标题 + 拖动手柄指示 =====
        GUILayout.BeginHorizontal();
        GUILayout.Label("✦ 符玄 · 控制面板", _titleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("⣿ 拖动", new GUIStyle { normal = { textColor = new Color(0.6f, 0.6f, 0.8f) }, fontSize = 11 });
        GUILayout.Space(2);
        GUILayout.EndHorizontal();

        GUILayout.Space(2);

        // ===== 标签栏 =====
        DrawTabBar();
        GUILayout.Space(4);

        // ===== 标签内容 =====
        _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, true,
            GUILayout.Width(_menuWidth), GUILayout.Height(_menuHeight - 90));

        switch (_currentTab)
        {
            case Tab.设置: DrawSettingsTab(); break;
            case Tab.动作: DrawActionsTab(); break;
            case Tab.聊天: DrawChatTab(); break;
            case Tab.便签: DrawRemindersTab(); break;
        }

        GUILayout.EndScrollView();

        // ===== 底部按钮行 =====
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕ 关闭", _closeButtonStyle, GUILayout.Width(80), GUILayout.Height(24)))
            Close();
        if (GUILayout.Button("⏻ 退出", _closeButtonStyle, GUILayout.Width(80), GUILayout.Height(24)))
        {
            Close();
#if !UNITY_EDITOR
            Application.Quit();
#endif
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    /// <summary>绘制标签栏</summary>
    private void DrawTabBar()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(4);

        for (int i = 0; i < _tabNames.Length; i++)
        {
            bool isActive = ((int)_currentTab == i);
            GUIStyle style = isActive ? _tabButtonActiveStyle : _tabButtonStyle;

            if (GUILayout.Button(_tabNames[i], style, GUILayout.Height(26)))
            {
                if ((Tab)i != _currentTab)
                {
                    _currentTab = (Tab)i;
                    _scrollPos = Vector2.zero;
                }
            }
        }

        GUILayout.Space(4);
        GUILayout.EndHorizontal();
    }

    #endregion

    #region 标签页: 设置

    private void DrawSettingsTab()
    {
        GUILayout.Label("⚙ 任务权重", _sectionStyle);
        GUILayout.Space(2);

        DrawWeightRow("向左走到边缘", ref _wLeftEdge, 0, 10);
        DrawWeightRow("向右走到边缘", ref _wRightEdge, 0, 10);
        DrawWeightRow("向左走定时", ref _wLeftTime, 0, 10);
        DrawWeightRow("向右走定时", ref _wRightTime, 0, 10);
        DrawWeightRow("停止", ref _wStop, 0, 10);

        GUILayout.Space(4);

        if (GUILayout.Button("✓ 应用权重", _buttonStyle, GUILayout.Height(26)))
            ApplyWeights();

        GUILayout.Space(6);

        // 快捷预设
        GUILayout.Label("📦 预设", _sectionStyle);
        GUILayout.Space(2);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("好动", _buttonStyle, GUILayout.Height(24)))
        {
            _wLeftEdge = 3; _wRightEdge = 3;
            _wLeftTime = 3; _wRightTime = 3; _wStop = 1;
            ApplyWeights();
        }
        if (GUILayout.Button("均衡", _buttonStyle, GUILayout.Height(24)))
        {
            _wLeftEdge = 2; _wRightEdge = 2;
            _wLeftTime = 2; _wRightTime = 2; _wStop = 2;
            ApplyWeights();
        }
        if (GUILayout.Button("安静", _buttonStyle, GUILayout.Height(24)))
        {
            _wLeftEdge = 1; _wRightEdge = 1;
            _wLeftTime = 1; _wRightTime = 1; _wStop = 6;
            ApplyWeights();
        }
        GUILayout.EndHorizontal();
    }

    #endregion

    #region 标签页: 动作

    private void DrawActionsTab()
    {
        GUILayout.Label("▶ 强制播放动作", _sectionStyle);
        GUILayout.Space(2);

        DrawActionButtonRow("tilt", "歪头", "smile", "微笑", "brow", "挑眉");
        DrawActionButtonRow("idle:4", "星辉", "stretch", "伸懒腰", "heart_eyes", "爱心");
        DrawActionButtonRow("money", "数钱", "cry", "委屈", "magic_circle", "法阵");
        DrawActionButtonRow("blush", "害羞", null, null, null, null);

        GUILayout.Space(8);
        GUILayout.Label("🛠 工具", _sectionStyle);
        GUILayout.Space(2);

        if (GUILayout.Button("🔧 调试面板", _buttonStyle, GUILayout.Height(26)))
        {
            var dw = GetComponent<DebugWindow>();
            if (dw == null) dw = gameObject.AddComponent<DebugWindow>();
            dw.Open();
            Close();
        }
    }

    #endregion

    #region 标签页: 聊天

    // 聊天标签专用的聊天消息样式
    private GUIStyle _chatMsgStyle;
    private GUIStyle _chatUserStyle;

    private void InitChatStyles()
    {
        if (_chatMsgStyle != null) return;
        _chatMsgStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.7f, 1f, 0.8f) },
            fontSize = 11,
            wordWrap = true,
            padding = new RectOffset(4, 4, 2, 2)
        };
        _chatUserStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.6f, 0.8f, 1f) },
            fontSize = 11,
            wordWrap = true,
            padding = new RectOffset(4, 4, 2, 2)
        };
    }

    private void DrawChatTab()
    {
        InitChatStyles();

        if (_chat == null)
        {
            GUILayout.Label("⚠ ChatManager 未初始化", _labelStyle);
            return;
        }

        CheckLocalSentenceState();

        // ——— 输入区域（用 MinHeight 撑满可见区，推到菜单底部，Enter 发送，按钮已删除） ———
        GUILayout.BeginVertical(GUILayout.MinHeight(_menuHeight - 90));
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();

        bool enterPressed = Event.current.isKey
            && Event.current.type == EventType.KeyDown
            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
            && _chatInputText.Length > 0 && GUI.GetNameOfFocusedControl() == "chatInput";

        GUI.SetNextControlName("chatInput");
        _chatInputText = GUILayout.TextField(_chatInputText, _textFieldStyle,
            GUILayout.Height(24), GUILayout.MinWidth(200));

        if (enterPressed)
        {
            Event.current.Use();
            string msg = _chatInputText;
            _chatInputText = "";
            if (_chat != null)
            {
                if (_isLocalAnimating || _chat.IsSentenceAnimating) _chat.SkipSentenceAnimation();
                _isLocalAnimating = false;
                _localSentenceIdx = 0;
                _chat.SendMessage(msg, null);
            }
            GUI.FocusControl(null);
        }

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    #endregion

    #region 聊天复制

    /// <summary>将聊天记录复制到剪贴板</summary>
    private void CopyChatToClipboard()
    {
        if (_chat == null) return;

        var visible = _chat.GetVisibleHistory();
        if (visible.Count == 0)
        {
            _chatStatusMsg = "没有聊天记录可复制";
            _chatStatusColor = Color.gray;
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var entry in visible)
        {
            string who = entry.role == "user" ? "我" : "符玄";
            sb.AppendLine($"[{who}] {entry.content}");
            sb.AppendLine();
        }

        GUIUtility.systemCopyBuffer = sb.ToString().TrimEnd();
        _chatStatusMsg = $"✅ 已复制 {_chat.HistoryCount} 条消息到剪贴板";
        _chatStatusColor = Color.green;
    }

    #endregion

    #region 标签页: 便签

    // ——— 便签相关状态 ———
    private Vector2 _reminderScrollPos = Vector2.zero;
    private string _newReminderText = "";
    private string _newReminderTime = "";
    private string _reminderStatusMsg = "";
    private Color _reminderStatusColor = Color.gray;
    private bool _showAddReminder = false;
    private bool _showDoneReminders = false;  // true=已完成视图 false=待办视图

    private void DrawRemindersTab()
    {
        if (_reminders == null)
        {
            GUILayout.Label("⚠ ReminderManager 未初始化", _labelStyle);
            return;
        }

        // ——— 状态栏 ———
        int pendingCount = _reminders.PendingCount;
        int doneCount = _reminders.GetDoneReminders().Count;
        GUILayout.Label($"📋 卜算记事簿 — {pendingCount} 待办 / {doneCount} 已完成", _sectionStyle);
        GUILayout.Space(2);

        // ——— 操作按钮行 ———
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("✚ 新建提醒", _buttonStyle, GUILayout.Height(24)))
        {
            _showAddReminder = !_showAddReminder;
            if (!_showAddReminder) { _newReminderText = ""; _newReminderTime = ""; }
        }
        if (GUILayout.Button("🔄 刷新", _buttonStyle, GUILayout.Height(24)))
        {
            _reminderStatusMsg = $"已刷新，{pendingCount} 项待办";
            _reminderStatusColor = Color.green;
        }
        // ——— 待办/已完成 切换按钮 ———
        string toggleLabel = _showDoneReminders ? "⏳ 看待办" : "✅ 已完成";
        if (GUILayout.Button(toggleLabel, _buttonStyle, GUILayout.Height(24)))
        {
            _showDoneReminders = !_showDoneReminders;
            _reminderScrollPos = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        // ——— 新建提醒输入区（仅待办视图显示） ———
        if (_showAddReminder && !_showDoneReminders)
        {
            GUILayout.Box("", new GUIStyle { normal = { background = _sectionBg } });
            GUILayout.Space(2);

            GUILayout.Label("提醒内容：", _labelStyle);
            _newReminderText = GUILayout.TextField(_newReminderText, _textFieldStyle, GUILayout.Height(22));

            GUILayout.Space(2);
            GUILayout.Label("时间 (可选, 如 2025-01-15 14:30)：", _labelStyle);
            _newReminderTime = GUILayout.TextField(_newReminderTime, _textFieldStyle, GUILayout.Height(22));

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✓ 添加", _buttonStyle, GUILayout.Height(24)))
            {
                if (!string.IsNullOrEmpty(_newReminderText))
                {
                    DateTime remindAt;
                    if (string.IsNullOrEmpty(_newReminderTime))
                        remindAt = DateTime.Now.AddHours(1);
                    else if (!DateTime.TryParse(_newReminderTime, out remindAt))
                    {
                        _reminderStatusMsg = "❌ 时间格式不对，使用 yyyy-MM-dd HH:mm";
                        _reminderStatusColor = Color.red;
                        remindAt = DateTime.Now.AddHours(1);
                    }

                    if (remindAt <= DateTime.Now)
                        remindAt = DateTime.Now.AddMinutes(5);

                    _reminders.AddReminder(_newReminderText, remindAt, null, "normal", "user");
                    _reminderStatusMsg = $"✅ 已添加提醒：{_newReminderText}";
                    _reminderStatusColor = Color.green;
                    _newReminderText = "";
                    _newReminderTime = "";
                    _showAddReminder = false;
                }
                else
                {
                    _reminderStatusMsg = "❌ 请输入提醒内容";
                    _reminderStatusColor = Color.red;
                }
            }
            if (GUILayout.Button("✕ 取消", _closeButtonStyle, GUILayout.Width(60), GUILayout.Height(24)))
            {
                _showAddReminder = false;
                _newReminderText = "";
                _newReminderTime = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        // ——— 状态消息 ———
        if (!string.IsNullOrEmpty(_reminderStatusMsg))
        {
            var style = new GUIStyle(_labelStyle) { normal = { textColor = _reminderStatusColor } };
            GUILayout.Label(_reminderStatusMsg, style);
            GUILayout.Space(2);
        }

        // ——— 提醒列表 ———
        List<ReminderManager.Reminder> list;
        string emptyMsg;
        if (_showDoneReminders)
        {
            list = _reminders.GetDoneReminders();
            emptyMsg = "📭 暂无已完成事项";
        }
        else
        {
            list = _reminders.GetPendingReminders();
            emptyMsg = "📭 暂无待办事项\n可以让我记下待办，或在上面新建~";
        }

        if (list.Count == 0)
        {
            GUILayout.Space(10);
            GUILayout.Label(emptyMsg, new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(4);
            if (_showDoneReminders)
            {
                GUILayout.Label("勾选待办前的 ☐ 即可标记完成", new GUIStyle(_labelStyle) { fontSize = 10, normal = { textColor = Color.gray } });
            }
        }
        else
        {
            _reminderScrollPos = GUILayout.BeginScrollView(_reminderScrollPos, false, true,
                GUILayout.Height(_menuHeight - 280));

            foreach (var r in list)
            {
                string timeStr = "未知时间";
                if (DateTime.TryParse(r.remindAt, out var dt))
                    timeStr = dt.ToString("MM-dd HH:mm");

                string status = r.done ? "✅" : "⬜";
                string recurringTag = r.recurring == "daily" ? " 🔄每日" :
                                       r.recurring == "weekday" ? " 🔄工作日" :
                                       r.recurring == "weekly" ? " 🔄每周" : "";

                // 优先级标记
                string priorityTag = "";
                Color itemBg = new Color(0.12f, 0.12f, 0.14f, 0.7f);
                if (r.priority == "high" && !r.done)
                {
                    priorityTag = " ⚠️";
                    itemBg = new Color(0.25f, 0.12f, 0.12f, 0.7f);
                }
                else if (r.priority == "low" && !r.done)
                {
                    priorityTag = " ☕";
                }

                // 条目背景
                GUILayout.BeginHorizontal(new GUIStyle { normal = { background = MakeTex(1, 1, itemBg) } });

                // 勾选框（待办才可勾选，已完成只显示✅）
                if (!_showDoneReminders)
                {
                    bool newDone = GUILayout.Toggle(r.done,
                        new GUIContent("", "标记完成"),
                        new GUIStyle { fixedWidth = 20, fixedHeight = 20 });
                    if (newDone != r.done)
                    {
                        if (newDone) _reminders.MarkDone(r.id);
                        _reminderStatusMsg = newDone ? $"✅ 已勾销「{r.text}」" : "";
                        _reminderStatusColor = Color.green;
                    }
                }

                // 内容
                string displayText = _showDoneReminders
                    ? $"✅ [{timeStr}] {r.text}"
                    : $"{status} [{timeStr}]{priorityTag}{recurringTag} {r.text}";
                var itemStyle = new GUIStyle(_labelStyle)
                {
                    normal = { textColor = r.done ? new Color(0.5f, 0.5f, 0.5f) : Color.white },
                    fontSize = 11,
                    wordWrap = true,
                    stretchWidth = true
                };
                GUILayout.Label(displayText, itemStyle);

                // 删除按钮
                if (GUILayout.Button("✕", _closeButtonStyle, GUILayout.Width(22), GUILayout.Height(20)))
                {
                    _reminders.DeleteReminder(r.id);
                    _reminderStatusMsg = $"🗑️ 已删除「{r.text}」";
                    _reminderStatusColor = Color.gray;
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            GUILayout.EndScrollView();
        }
    }

    #endregion

    #region 绘制辅助

    private void DrawWeightRow(string label, ref int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _labelStyle, GUILayout.Width(130));

        if (GUILayout.Button("-", _smallButtonStyle))
            value = Mathf.Max(min, value - 1);

        string valStr = value.ToString();
        GUIStyle valStyle = new GUIStyle(_labelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fixedWidth = 30
        };
        GUILayout.Label(valStr, valStyle);

        if (GUILayout.Button("+", _smallButtonStyle))
            value = Mathf.Min(max, value + 1);

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawActionButtonRow(string id1, string name1, string id2, string name2, string id3, string name3)
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (!string.IsNullOrEmpty(id1)) DrawActionButton(id1, name1);
        if (!string.IsNullOrEmpty(id2)) DrawActionButton(id2, name2);
        if (!string.IsNullOrEmpty(id3)) DrawActionButton(id3, name3);

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(3);
    }

    private void DrawActionButton(string id, string name)
    {
        if (GUILayout.Button($"{name}", _buttonStyle, GUILayout.Width(75), GUILayout.Height(28)))
        {
            if (_renderer != null)
            {
                if (_pet != null)
                    _pet.ForceStop();
                _renderer.ForceAction(id);

                // 关闭菜单
                _isOpen = false;

                _renderer.OnForcedActionFinished -= OnForcedActionComplete;
                _renderer.OnForcedActionFinished += OnForcedActionComplete;
            }
        }
    }

    private void OnForcedActionComplete()
    {
        if (_renderer != null)
            _renderer.OnForcedActionFinished -= OnForcedActionComplete;

        if (_pet != null)
        {
            _pet.isPaused = false;
            _pet.Resume();
        }
    }

    #endregion

    #region 操作

    private void ApplyWeights()
    {
        _pet.taskWeightMoveLeftEdge = _wLeftEdge;
        _pet.taskWeightMoveRightEdge = _wRightEdge;
        _pet.taskWeightMoveLeftTime = _wLeftTime;
        _pet.taskWeightMoveRightTime = _wRightTime;
        _pet.taskWeightStopTime = _wStop;

        Debug.Log($"[ContextMenu] 权重已应用 左边={_wLeftEdge} 右边={_wRightEdge} 左走={_wLeftTime} 右走={_wRightTime} 停止={_wStop}");
    }

    #endregion

    #region 工具

    private Texture2D MakeTex(int w, int h, Color c)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = c;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }

    #endregion
}
