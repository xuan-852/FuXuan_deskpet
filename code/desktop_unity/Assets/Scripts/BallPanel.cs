using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 悬浮球子面板 — 每个悬浮球菜单项对应一个独立面板
///
/// 面板类型：
/// - 设置 (Settings)：任务权重调节 + 保存/清空
/// - 报告 (Report)：MotionMemoryManager 演武心经报告
/// - 便签 (Reminders)：卜算记事簿待办/已完成管理
///
/// 行为：
/// - 每个面板独立浮动窗口，可拖拽标题栏
/// - 面板外点击或右键关闭
/// - 点击穿透由 DragHandler 管理
/// </summary>
public class BallPanel : MonoBehaviour
{
    public enum PanelType { None, Settings, Report, Reminders }

    // ==================== 运行时状态 ====================
    private PanelType _currentPanel = PanelType.None;
    private Rect _panelRect;
    private float _panelWidth = 420f;
    private float _panelHeight = 580f;

    // 拖拽
    private bool _isDragging = false;
    private Vector2 _dragMouseOffset = Vector2.zero;
    private bool _titleHovered = false;

    // ==================== 引用 ====================
    private DesktopPet _pet;
    private ReminderManager _reminders;

    // ==================== 设置面板状态 ====================
    private int _wLeftEdge, _wRightEdge, _wLeftTime, _wRightTime, _wStop;

    // ==================== 报告面板状态 ====================
    private Vector2 _reportScrollPos = Vector2.zero;

    // ==================== 便签面板状态 ====================
    private Vector2 _reminderScrollPos = Vector2.zero;
    private string _newReminderText = "";
    private string _newReminderTime = "";
    private string _reminderStatusMsg = "";
    private Color _reminderStatusColor = Color.gray;
    private bool _showAddReminder = false;
    private bool _showDoneReminders = false;

    // ==================== 样式 ====================
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _smallButtonStyle;
    private GUIStyle _closeButtonStyle;
    private GUIStyle _textFieldStyle;
    private Texture2D _bgTexture;
    private Texture2D _sectionBg;
    private Texture2D _btnBg;
    private Texture2D _btnSmallBg;
    private Texture2D _inputBg;
    private bool _stylesInit = false;

    // ==================== 公开属性 ====================
    public bool IsOpen => _currentPanel != PanelType.None;
    public PanelType CurrentPanel => _currentPanel;
    public Rect PanelRect => _panelRect;
    public bool IsMouseOverPanel(Vector2 mousePos) => IsOpen && _panelRect.Contains(mousePos);
    public float PanelWidth => _panelWidth;
    public float PanelHeight => _panelHeight;

    // ==================== 生命周期 ====================

    void Start()
    {
        _pet = GetComponent<DesktopPet>();
        _reminders = ReminderManager.Instance;
        if (_reminders == null)
        {
            _reminders = GetComponent<ReminderManager>();
            if (_reminders == null) _reminders = gameObject.AddComponent<ReminderManager>();
        }
    }

    void InitStyles()
    {
        if (_stylesInit) return;
        _stylesInit = true;

        _bgTexture = MakeTex(1, 1, new Color(0.15f, 0.15f, 0.17f, 0.95f));
        _sectionBg = MakeTex(1, 1, new Color(0.12f, 0.12f, 0.14f, 0.9f));
        _btnBg = MakeTex(1, 1, new Color(0.25f, 0.25f, 0.28f, 1f));
        _btnSmallBg = MakeTex(1, 1, new Color(0.3f, 0.3f, 0.33f, 1f));
        _inputBg = MakeTex(1, 1, new Color(0.08f, 0.08f, 0.1f, 0.9f));

        _titleStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.9f, 0.6f, 0.8f), background = _sectionBg },
            fontStyle = FontStyle.Bold,
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 8, 8)
        };

        _sectionStyle = new GUIStyle
        {
            normal = { textColor = new Color(0.7f, 0.7f, 0.8f), background = _sectionBg },
            fontStyle = FontStyle.Bold,
            fontSize = 15,
            padding = new RectOffset(10, 0, 6, 6)
        };

        _labelStyle = new GUIStyle
        {
            normal = { textColor = Color.white },
            fontSize = 15,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 0, 4, 4)
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            normal = { textColor = Color.white, background = _btnBg },
            hover = { background = MakeTex(1, 1, new Color(0.35f, 0.35f, 0.4f)) },
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(10, 10, 6, 6)
        };

        _smallButtonStyle = new GUIStyle(_buttonStyle)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(6, 6, 0, 0),
            fixedWidth = 32,
            fixedHeight = 30
        };

        _closeButtonStyle = new GUIStyle(_buttonStyle)
        {
            normal = { textColor = new Color(1f, 0.4f, 0.4f), background = _btnSmallBg },
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter
        };

        _textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            normal = { textColor = Color.white, background = _inputBg },
            fontSize = 15,
            padding = new RectOffset(4, 4, 3, 3)
        };
    }

    // ==================== 公开接口 ====================

    public void ShowPanel(PanelType type, Vector2 screenPos)
    {
        _currentPanel = type;

        // 复制当前权重
        if (_pet != null)
        {
            _wLeftEdge = _pet.taskWeightMoveLeftEdge;
            _wRightEdge = _pet.taskWeightMoveRightEdge;
            _wLeftTime = _pet.taskWeightMoveLeftTime;
            _wRightTime = _pet.taskWeightMoveRightTime;
            _wStop = _pet.taskWeightStopTime;
        }

        // 面板位置
        float x = Mathf.Clamp(screenPos.x, 10, Screen.width - _panelWidth - 10);
        float y = Mathf.Clamp(screenPos.y, 10, Screen.height - _panelHeight - 10);
        _panelRect = new Rect(x, y, _panelWidth, _panelHeight);

        _reportScrollPos = Vector2.zero;
        _reminderScrollPos = Vector2.zero;
        _isDragging = false;

        // 暂停宠物
        if (_pet != null)
        {
            _pet.ForceStop();
            _pet.isPaused = true;
        }
    }

    public void Close()
    {
        _currentPanel = PanelType.None;

        if (_pet != null)
        {
            _pet.isPaused = false;
            _pet.Resume();
        }
    }

    // ==================== OnGUI ====================

    void OnGUI()
    {
        if (!IsOpen) return;
        InitStyles();

        // ——— 拖拽处理 ———
        HandleDragEvent(Event.current);

        // ——— 背景 ———
        GUI.Box(_panelRect, GUIContent.none, new GUIStyle { normal = { background = _bgTexture } });

        GUILayout.BeginArea(_panelRect);

        // ——— 标题栏 ———
        string title = _currentPanel switch
        {
            PanelType.Settings => "⚙ 设置",
            PanelType.Report => "📊 演武心经",
            PanelType.Reminders => "📋 卜算记事簿",
            _ => ""
        };
        GUILayout.Label(title, _titleStyle);
        GUILayout.Space(2);

        // ——— 内容 ———
        switch (_currentPanel)
        {
            case PanelType.Settings: DrawSettingsPanel(); break;
            case PanelType.Report: DrawReportPanel(); break;
            case PanelType.Reminders: DrawRemindersPanel(); break;
        }

        // ——— 底部关闭按钮 ———
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("✕ 关闭", _closeButtonStyle, GUILayout.Width(120), GUILayout.Height(36)))
            Close();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void HandleDragEvent(Event e)
    {
        if (!IsOpen) return;

        Rect titleBar = new Rect(_panelRect.x, _panelRect.y, _panelRect.width, 40);
        _titleHovered = titleBar.Contains(e.mousePosition);

        if (e.type == EventType.MouseDown && _titleHovered)
        {
            _isDragging = true;
            _dragMouseOffset = e.mousePosition - new Vector2(_panelRect.x, _panelRect.y);
        }
        else if (e.type == EventType.MouseUp)
        {
            _isDragging = false;
        }
        else if (e.type == EventType.MouseDrag && _isDragging)
        {
            Vector2 newPos = e.mousePosition - _dragMouseOffset;
            newPos.x = Mathf.Clamp(newPos.x, 0, Screen.width - _panelWidth);
            newPos.y = Mathf.Clamp(newPos.y, 0, Screen.height - _panelHeight);
            _panelRect.x = newPos.x;
            _panelRect.y = newPos.y;
        }
    }

    // ==================== 设置面板 ====================

    private Vector2 _settingsScrollPos = Vector2.zero;

    private void DrawSettingsPanel()
    {
        _settingsScrollPos = GUILayout.BeginScrollView(_settingsScrollPos, false, true,
            GUILayout.Width(_panelWidth), GUILayout.Height(_panelHeight - 90));

        GUILayout.Label("⚙ 任务权重", _sectionStyle);
        GUILayout.Space(2);

        DrawWeightRow("向左走到边缘", ref _wLeftEdge, 0, 10);
        DrawWeightRow("向右走到边缘", ref _wRightEdge, 0, 10);
        DrawWeightRow("向左走定时", ref _wLeftTime, 0, 10);
        DrawWeightRow("向右走定时", ref _wRightTime, 0, 10);
        DrawWeightRow("停止", ref _wStop, 0, 10);

        GUILayout.Space(4);

        if (GUILayout.Button("✓ 应用权重", _buttonStyle, GUILayout.Height(34)))
            ApplyWeights();

        GUILayout.Space(6);

        // 快捷预设
        GUILayout.Label("📦 预设", _sectionStyle);
        GUILayout.Space(2);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("好动", _buttonStyle, GUILayout.Height(32)))
        {
            _wLeftEdge = 3; _wRightEdge = 3;
            _wLeftTime = 3; _wRightTime = 3; _wStop = 1;
            ApplyWeights();
        }
        if (GUILayout.Button("均衡", _buttonStyle, GUILayout.Height(32)))
        {
            _wLeftEdge = 2; _wRightEdge = 2;
            _wLeftTime = 2; _wRightTime = 2; _wStop = 2;
            ApplyWeights();
        }
        if (GUILayout.Button("安静", _buttonStyle, GUILayout.Height(32)))
        {
            _wLeftEdge = 1; _wRightEdge = 1;
            _wLeftTime = 1; _wRightTime = 1; _wStop = 6;
            ApplyWeights();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("💾 持久化", _sectionStyle);
        GUILayout.Space(2);

        if (GUILayout.Button("💿 保存配置（天机簿）", _buttonStyle, GUILayout.Height(34)))
        {
            if (PetConfig.Instance != null)
            {
                PetConfig.Instance.CollectAll();
                PetConfig.Instance.Save();
            }
        }

        if (GUILayout.Button("🗑 清空忆境", _buttonStyle, GUILayout.Height(34)))
        {
            if (PetMemory.Instance != null)
                PetMemory.Instance.ClearMemories();
        }

        GUILayout.EndScrollView();
    }

    // ==================== 报告面板 ====================

    private string _lastReportText = "";

    private void DrawReportPanel()
    {
        GUILayout.Label("📊 演武心经 · 修为报告", _sectionStyle);
        GUILayout.Space(2);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("🔄 刷新", _buttonStyle, GUILayout.Width(90), GUILayout.Height(32)))
        {
            // 下次绘制自动取最新
        }
        if (GUILayout.Button("📋 复制", _buttonStyle, GUILayout.Width(90), GUILayout.Height(32)))
        {
            if (!string.IsNullOrEmpty(_lastReportText))
            {
                GUIUtility.systemCopyBuffer = _lastReportText;
                Debug.Log("[BallPanel] 报告已复制到剪贴板");
            }
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        string report = "";
        try
        {
            var mm = MotionMemoryManager.Instance;
            if (mm != null)
                report = mm.GetStatistics();
            else
                report = "⚠ MotionMemoryManager 未初始化";
        }
        catch (System.Exception ex)
        {
            report = $"⚠ 读取报告异常: {ex.Message}";
        }

        if (string.IsNullOrEmpty(report))
            report = "📭 暂无演武记录\n让符玄执行动作后再来查看吧~";

        // 保存最新报告文本（供复制按钮使用）
        _lastReportText = report;

        _reportScrollPos = GUILayout.BeginScrollView(_reportScrollPos, false, true,
            GUILayout.Width(_panelWidth), GUILayout.Height(_panelHeight - 180));

        var reportStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            normal = { textColor = new Color(0.8f, 0.8f, 0.9f) },
            wordWrap = true,
            richText = true,
            padding = new RectOffset(8, 8, 4, 4)
        };

        GUILayout.Label(report, reportStyle);
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        GUILayout.Label("💡 每次演武后 AI 会自评并记录，分数越高下次越倾向使用",
            new GUIStyle { normal = { textColor = new Color(0.5f, 0.5f, 0.6f) }, fontSize = 13, wordWrap = true });
    }

    // ==================== 便签面板 ====================

    private void DrawRemindersPanel()
    {
        if (_reminders == null)
        {
            GUILayout.Label("⚠ ReminderManager 未初始化", _labelStyle);
            return;
        }

        int pendingCount = _reminders.PendingCount;
        int doneCount = _reminders.GetDoneReminders().Count;
        GUILayout.Label($"📋 {pendingCount} 待办 / {doneCount} 已完成", _sectionStyle);
        GUILayout.Space(2);

        // 操作行
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("✚ 新建", _buttonStyle, GUILayout.Height(32)))
        {
            _showAddReminder = !_showAddReminder;
            if (!_showAddReminder) { _newReminderText = ""; _newReminderTime = ""; }
        }
        if (GUILayout.Button("🔄 刷新", _buttonStyle, GUILayout.Height(32)))
        {
            _reminderStatusMsg = $"已刷新，{pendingCount} 项待办";
            _reminderStatusColor = Color.green;
        }
        string toggleLabel = _showDoneReminders ? "⏳ 看待办" : "✅ 已完成";
        if (GUILayout.Button(toggleLabel, _buttonStyle, GUILayout.Height(32)))
        {
            _showDoneReminders = !_showDoneReminders;
            _reminderScrollPos = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        // 新建输入区
        if (_showAddReminder && !_showDoneReminders)
        {
            GUILayout.Box("", new GUIStyle { normal = { background = _sectionBg } });
            GUILayout.Space(2);

            GUILayout.Label("内容：", _labelStyle);
            _newReminderText = GUILayout.TextField(_newReminderText, _textFieldStyle, GUILayout.Height(30));

            GUILayout.Space(2);
            GUILayout.Label("时间 (可选 yyyy-MM-dd HH:mm)：", _labelStyle);
            _newReminderTime = GUILayout.TextField(_newReminderTime, _textFieldStyle, GUILayout.Height(30));

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✓ 添加", _buttonStyle, GUILayout.Height(32)))
            {
                if (!string.IsNullOrEmpty(_newReminderText))
                {
                    DateTime remindAt;
                    if (string.IsNullOrEmpty(_newReminderTime))
                        remindAt = DateTime.Now.AddHours(1);
                    else if (!DateTime.TryParse(_newReminderTime, out remindAt))
                    {
                        _reminderStatusMsg = "❌ 时间格式有误";
                        _reminderStatusColor = Color.red;
                        remindAt = DateTime.Now.AddHours(1);
                    }

                    if (remindAt <= DateTime.Now)
                        remindAt = DateTime.Now.AddMinutes(5);

                    _reminders.AddReminder(_newReminderText, remindAt, null, "normal", "user");
                    _reminderStatusMsg = $"✅ 已添加：{_newReminderText}";
                    _reminderStatusColor = Color.green;
                    _newReminderText = "";
                    _newReminderTime = "";
                    _showAddReminder = false;
                }
                else
                {
                    _reminderStatusMsg = "❌ 请输入内容";
                    _reminderStatusColor = Color.red;
                }
            }
            if (GUILayout.Button("✕", _closeButtonStyle, GUILayout.Width(50), GUILayout.Height(32)))
            {
                _showAddReminder = false;
                _newReminderText = "";
                _newReminderTime = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        // 状态消息
        if (!string.IsNullOrEmpty(_reminderStatusMsg))
        {
            var style = new GUIStyle(_labelStyle) { normal = { textColor = _reminderStatusColor } };
            GUILayout.Label(_reminderStatusMsg, style);
            GUILayout.Space(2);
        }

        // 提醒列表
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
            emptyMsg = "📭 暂无待办事项";
        }

        if (list.Count == 0)
        {
            GUILayout.Space(10);
            GUILayout.Label(emptyMsg, new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter });
        }
        else
        {
            _reminderScrollPos = GUILayout.BeginScrollView(_reminderScrollPos, false, true,
                GUILayout.Width(_panelWidth), GUILayout.Height(_panelHeight - 280));

            foreach (var r in list)
            {
                string timeStr = "未知时间";
                if (DateTime.TryParse(r.remindAt, out var dt))
                    timeStr = dt.ToString("MM-dd HH:mm");

                string status = r.done ? "✅" : "⬜";
                string recurringTag = r.recurring == "daily" ? " 🔄每日" :
                                       r.recurring == "weekday" ? " 🔄工作日" :
                                       r.recurring == "weekly" ? " 🔄每周" : "";

                string priorityTag = "";
                Color itemBg = new Color(0.12f, 0.12f, 0.14f, 0.7f);
                if (r.priority == "high" && !r.done)
                {
                    priorityTag = " ⚠️";
                    itemBg = new Color(0.25f, 0.12f, 0.12f, 0.7f);
                }

                GUILayout.BeginHorizontal(new GUIStyle { normal = { background = MakeTex(1, 1, itemBg) } });

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

                string displayText = _showDoneReminders
                    ? $"✅ [{timeStr}] {r.text}"
                    : $"{status} [{timeStr}]{priorityTag}{recurringTag} {r.text}";
                var itemStyle = new GUIStyle(_labelStyle)
                {
                    normal = { textColor = r.done ? new Color(0.5f, 0.5f, 0.5f) : Color.white },
                    fontSize = 15,
                    wordWrap = true,
                    stretchWidth = true
                };
                GUILayout.Label(displayText, itemStyle);

                if (GUILayout.Button("✕", _closeButtonStyle, GUILayout.Width(30), GUILayout.Height(28)))
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

    // ==================== 辅助方法 ====================

    private void DrawWeightRow(string label, ref int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _labelStyle, GUILayout.Width(180));

        if (GUILayout.Button("-", _smallButtonStyle))
            value = Mathf.Max(min, value - 1);

        string valStr = value.ToString();
        GUIStyle valStyle = new GUIStyle(_labelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fixedWidth = 36
        };
        GUILayout.Label(valStr, valStyle);

        if (GUILayout.Button("+", _smallButtonStyle))
            value = Mathf.Min(max, value + 1);

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void ApplyWeights()
    {
        if (_pet == null) return;

        _pet.taskWeightMoveLeftEdge = _wLeftEdge;
        _pet.taskWeightMoveRightEdge = _wRightEdge;
        _pet.taskWeightMoveLeftTime = _wLeftTime;
        _pet.taskWeightMoveRightTime = _wRightTime;
        _pet.taskWeightStopTime = _wStop;

        if (PetConfig.Instance != null)
        {
            PetConfig.Instance.data.weightLeftEdge = _wLeftEdge;
            PetConfig.Instance.data.weightRightEdge = _wRightEdge;
            PetConfig.Instance.data.weightLeftTime = _wLeftTime;
            PetConfig.Instance.data.weightRightTime = _wRightTime;
            PetConfig.Instance.data.weightStop = _wStop;
        }

        Debug.Log($"[BallPanel] 权重已应用 左边={_wLeftEdge} 右边={_wRightEdge} 左走={_wLeftTime} 右走={_wRightTime} 停止={_wStop}");
    }

    private static Texture2D MakeTex(int w, int h, Color c)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = c;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
}
