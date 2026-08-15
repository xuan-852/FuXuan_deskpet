using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// RightPanel 子面板分部（2026-08-14 拆分自 RightPanel.cs）
/// 页内子面板：设置（任务权重）/ 便签（卜算记事簿）/ 报告（演武心经）。
/// 与主文件共用实例状态（_chat/_reminders/_pet/_animAlpha 等），partial class 保证行为逐字节不变。
/// 改子面板 UI（权重行/预设/便签列表/报告滚动）只动本文件。
/// </summary>
public partial class RightPanel
{
    // ==================== 子面板（设置/便签/报告 → RightPanel 内部视图） ====================
    private PanelView _prevView = PanelView.Chat; // 进入子面板前的视图（◀ 返回用）
    private int _wLeftEdge, _wRightEdge, _wLeftTime, _wRightTime, _wStop; // 权重编辑值
    private bool _settingsLoaded = false;   // 设置页首次进入时从 _pet 复制权重
    private Vector2 _settingsScrollPos;      // 设置页滚动
    private Vector2 _reportScrollPos;        // 报告页滚动
    private string _lastReportText = "";     // 报告缓存（复制用）
    private Vector2 _reminderScrollPos;      // 便签页滚动
    private string _newReminderText = "";
    private string _newReminderTime = "";
    private string _reminderStatusMsg = "";
    private Color _reminderStatusColor = Color.gray;
    private bool _showAddReminder = false;
    private bool _showDoneReminders = false;
    private bool _remindersRefreshed = false; // 便签首次进入拉一次
    private GUIStyle _subBtnStyle;         // 子面板按钮
    private GUIStyle _subLabelStyle;       // 子面板标签
    private GUIStyle _subSectionStyle;     // 子面板小节标题
    private GUIStyle _subInputStyle;       // 子面板输入框
    private GUIStyle _subScrollStyle;      // 子面板滚动条
    private Texture2D _subBtnBg;           // 子面板按钮背景
    private Texture2D _subBtnHoverBg;
    private Texture2D _subInputBg;         // 子面板输入框背景
    private Texture2D _subSectionBg;       // 子面板小节背景
    private bool _subStylesReady = false;

    private void DrawSubPanelView(float px, float py, float pw, float ph, Vector2 mp)
    {
        RefreshTime();

        float titleH = 54f;
        Rect titleBarRect = new Rect(px + 2f, py + 2f, pw - 4f, titleH);
        GUI.DrawTexture(titleBarRect, _titleBarPixelTex);
        UiTextureFactory.DrawPixelRect(new Rect(px + 2f, py + 2f + titleH, pw - 4f, 1f), new Color(0.58f, 0.42f, 0.88f, 0.6f));

        // —— ◀ 返回（回到来源视图） ——
        Rect backRect = new Rect(px + 10f, py + 10f, 34f, 34f);
        if (backRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(backRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        if (GUI.Button(backRect, "◀", _termToolBtnStyle))
            BackFromSubPanel();
        RegisterExtHit(backRect, BackFromSubPanel); // 外部命中：◀ 返回来源视图

        // —— 页面标题 ——
        string subTitle = _currentView switch
        {
            PanelView.Settings => "⚙ 设置 · 任务权重",
            PanelView.Reminders => "📋 便签 · 卜算记事簿",
            PanelView.Report => "📝 报告 · 演武心经",
            PanelView.Usage => "💰 消耗 · Token 统计",
            _ => ""
        };
        GUI.Label(new Rect(px + 56f, py + 8f, pw - 200f, 26f), subTitle, _termTitleStyle);
        GUI.Label(new Rect(px + 56f, py + 32f, pw - 200f, 18f),
            _currentView == PanelView.Settings ? "调教本座走位习惯，微调权重以符占卜之数" :
            _currentView == PanelView.Reminders ? "卜算记事簿 — 记要事，勿相忘" :
            _currentView == PanelView.Usage ? "Token 消耗统计 — 看看本座每小时烧多少" :
            "演武心经 — 每次演武后 AI 自评记录的修为报告", _termLogDimStyle);

        // —— 时间 + ✕ 关闭 ——
        GUI.Label(new Rect(px + pw - 130f, py + 16f, 60f, 22f), _timeDisplay, _termTimeStyle);
        float closeSize = 36f;
        Rect closeRect = new Rect(px + pw - closeSize - 14f, py + (titleH - closeSize) / 2f, closeSize, closeSize);
        if (closeRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(closeRect, new Color(0.80f, 0.25f, 0.25f, 0.35f));
        if (GUI.Button(closeRect, "✕", _closeBtnStyle)) { Close(); }
        RegisterExtHit(closeRect, Close); // 外部命中：✕ 关闭面板

        // —— 标题栏拖拽（排除 ◀ 返回 / ✕） ——
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0
            && titleBarRect.Contains(mp) && !closeRect.Contains(mp) && !backRect.Contains(mp))
        {
            _isDragging = true;
            _dragOffset = mp - new Vector2(_panelRect.x, _panelRect.y);
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseUp)
        {
            _isDragging = false;
            _isResizing = false;
        }

        // —— 内容区（标题栏下方，含滚动） ——
        float contentX = px + 16f;
        float contentY = py + titleH + 14f;
        float contentW = pw - 32f;
        float contentH = ph - titleH - 30f;

        switch (_currentView)
        {
            case PanelView.Settings: DrawSettingsSubPanel(contentX, contentY, contentW, contentH, mp); break;
            case PanelView.Reminders: DrawRemindersSubPanel(contentX, contentY, contentW, contentH, mp); break;
            case PanelView.Report: DrawReportSubPanel(contentX, contentY, contentW, contentH, mp); break;
            case PanelView.Usage: DrawUsageSubPanel(contentX, contentY, contentW, contentH, mp); break;
        }

        // 右键关闭（快捷收面板）
        if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
        {
            Close();
            Event.current.Use();
        }
    }

    // ==================================================================
    //  子面板内容：设置（任务权重）— 权重行 + 预设 + 保存/清空忆境
    // ==================================================================
    private void DrawSettingsSubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        float rowH = 46f;
        float labelW = 260f;
        float btnW = 56f;
        float btnGap = 10f;

        // —— 小节标题：任务权重 ——
        Rect sec1 = new Rect(x, y, w, 36f);
        GUI.Label(sec1, "⚙ 任务权重", _subSectionStyle);
        UiTextureFactory.DrawPixelRect(new Rect(x, sec1.yMax - 1f, w, 1f), new Color(0.45f, 0.35f, 0.65f, 0.3f));

        float rowsY = sec1.yMax + 10f;
        DrawWeightRowGui(x, rowsY, w, rowH, labelW, btnW, btnGap, "向左走到边缘", ref _wLeftEdge, 0, 10, "任务：走到屏幕左边缘的次数权重");
        DrawWeightRowGui(x, rowsY + rowH * 1, w, rowH, labelW, btnW, btnGap, "向右走到边缘", ref _wRightEdge, 0, 10, "任务：走到屏幕右边缘的次数权重");
        DrawWeightRowGui(x, rowsY + rowH * 2, w, rowH, labelW, btnW, btnGap, "向左走定时", ref _wLeftTime, 0, 10, "任务：向左走动的时间权重");
        DrawWeightRowGui(x, rowsY + rowH * 3, w, rowH, labelW, btnW, btnGap, "向右走定时", ref _wRightTime, 0, 10, "任务：向右走动的时间权重");
        DrawWeightRowGui(x, rowsY + rowH * 4, w, rowH, labelW, btnW, btnGap, "停止", ref _wStop, 0, 10, "任务：静止不动的权重");

        // —— 应用权重 ——
        float applyY = rowsY + rowH * 5 + 6f;
        Rect applyRect = new Rect(x, applyY, 180f, 40f);
        if (applyRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(applyRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        if (GUI.Button(applyRect, "✓ 应用权重", _subBtnStyle))
            ApplyWeightsToPet();

        // —— 预设 ——
        float presetY = applyY + 56f;
        Rect sec2 = new Rect(x, presetY, w, 36f);
        GUI.Label(sec2, "📦 预设", _subSectionStyle);
        UiTextureFactory.DrawPixelRect(new Rect(x, sec2.yMax - 1f, w, 1f), new Color(0.45f, 0.35f, 0.65f, 0.3f));

        float presetBtnW = 110f;
        float presetY0 = sec2.yMax + 10f;
        var presets = new (string label, int le, int re, int lt, int rt, int stop)[]
        {
            ("好动", 3, 3, 3, 3, 1),
            ("均衡", 2, 2, 2, 2, 2),
            ("安静", 1, 1, 1, 1, 6)
        };
        for (int i = 0; i < presets.Length; i++)
        {
            Rect pr = new Rect(x + i * (presetBtnW + btnGap), presetY0, presetBtnW, 38f);
            if (GUI.Button(pr, presets[i].label, _subBtnStyle))
            {
                _wLeftEdge = presets[i].le; _wRightEdge = presets[i].re;
                _wLeftTime = presets[i].lt; _wRightTime = presets[i].rt; _wStop = presets[i].stop;
                ApplyWeightsToPet();
            }
        }

        // —— 持久化 ——
        float persistY = presetY0 + 56f;
        Rect sec3 = new Rect(x, persistY, w, 36f);
        GUI.Label(sec3, "💾 持久化", _subSectionStyle);
        UiTextureFactory.DrawPixelRect(new Rect(x, sec3.yMax - 1f, w, 1f), new Color(0.45f, 0.35f, 0.65f, 0.3f));

        Rect saveRect = new Rect(x, sec3.yMax + 10f, 220f, 40f);
        if (GUI.Button(saveRect, "💿 保存配置（天机簿）", _subBtnStyle))
        {
            if (PetConfig.Instance != null)
            {
                PetConfig.Instance.CollectAll();
                PetConfig.Instance.Save();
            }
        }
        Rect clearRect = new Rect(x + 230f, sec3.yMax + 10f, 180f, 40f);
        if (clearRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(clearRect, new Color(0.70f, 0.25f, 0.25f, 0.20f));
        if (GUI.Button(clearRect, "🗑 清空忆境", _subBtnStyle))
        {
            if (PetMemory.Instance != null)
                PetMemory.Instance.ClearMemories();
        }

        // 底部提示
        GUI.Label(new Rect(x, y + h - 34f, w, 24f),
            "💡 权重越高越倾向执行该任务；「保存配置」写入天机簿，重启后生效",
            new GUIStyle(_termLogDimStyle) { fontSize = 14, alignment = TextAnchor.UpperLeft });
    }

    /// <summary>权重调节行：标签 + [-] 数值 [+]</summary>
    private void DrawWeightRowGui(float x, float y, float w, float rowH, float labelW, float btnW, float btnGap,
        string label, ref int value, int min, int max, string tip)
    {
        GUI.Label(new Rect(x, y, labelW, rowH), label, _subLabelStyle);
        float valX = x + labelW + 20f;
        float valW = 64f;
        // [-] 按钮
        Rect minusRect = new Rect(valX + valW + 8f, y + 4f, btnW, rowH - 8f);
        if (GUI.Button(minusRect, "−", _subBtnStyle)) value = Mathf.Max(min, value - 1);
        // 数值（居中金底）
        Rect valBg = new Rect(valX, y + 6f, valW, rowH - 12f);
        UiTextureFactory.DrawPixelRect(valBg, new Color(0.20f, 0.14f, 0.32f, 0.6f));
        GUI.Label(valBg, value.ToString(), new GUIStyle(_subLabelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.92f, 0.82f, 0.56f, 1f) }
        });
        // [+] 按钮
        Rect plusRect = new Rect(valX + valW + 8f + btnW + btnGap, y + 4f, btnW, rowH - 8f);
        if (GUI.Button(plusRect, "＋", _subBtnStyle)) value = Mathf.Min(max, value + 1);
        // 提示（hover 数值区时）
        if (valBg.Contains(Event.current.mousePosition))
            GUI.Label(new Rect(x, y + rowH, w, 20f), tip, new GUIStyle(_termLogDimStyle) { fontSize = 13 });
    }

    /// <summary>将编辑中的权重应用给宠物 + 写入 PetConfig</summary>
    private void ApplyWeightsToPet()
    {
        if (_pet == null) { Debug.LogWarning("[RightPanel] DesktopPet 未找到，无法应用权重"); return; }
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
        Debug.Log($"[RightPanel] 权重已应用 左边缘={_wLeftEdge} 右边缘={_wRightEdge} 左走={_wLeftTime} 右走={_wRightTime} 停止={_wStop}");
    }

    // ==================================================================
    //  子面板内容：报告（演武心经）— 刷新/复制 + 滚动文本
    // ==================================================================
    private void DrawReportSubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        // —— 操作行：刷新 / 复制 ——
        Rect refreshRect = new Rect(x, y, 100f, 38f);
        if (GUI.Button(refreshRect, "🔄 刷新", _subBtnStyle))
        {
            // 下次绘制自动取最新（每次绘制都重新拉取，无需缓存）
        }
        Rect copyRect = new Rect(x + 110f, y, 100f, 38f);
        if (GUI.Button(copyRect, "📋 复制", _subBtnStyle))
        {
            if (!string.IsNullOrEmpty(_lastReportText))
            {
                GUIUtility.systemCopyBuffer = _lastReportText;
                Debug.Log("[RightPanel] 报告已复制到剪贴板");
            }
        }

        // —— 报告内容 ——
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
        _lastReportText = report;

        // —— 滚动区 ——
        float listY = y + 52f;
        float listH = h - 52f - 40f;
        Rect viewRect = new Rect(x, listY, w, listH);
        UiTextureFactory.DrawPixelRect(viewRect, new Color(0.05f, 0.04f, 0.09f, 0.35f));

        var repStyle = new GUIStyle(_termLogStyle)
        {
            fontSize = 16, wordWrap = true,
            normal = { textColor = new Color(0.82f, 0.80f, 0.92f, 1f) },
            richText = true,
            padding = new RectOffset(10, 10, 8, 8)
        };
        float repH = repStyle.CalcHeight(new GUIContent(report), w - 20f) + 20f;
        Rect contentRect = new Rect(0f, 0f, w - 14f, Mathf.Max(repH, listH));
        _reportScrollPos = GUI.BeginScrollView(viewRect, _reportScrollPos, contentRect, false, false,
            _invisibleScrollbar, _invisibleScrollbar);
        GUI.Label(new Rect(8f, 4f, contentRect.width - 16f, repH), report, repStyle);
        GUI.EndScrollView();

        GUI.Label(new Rect(x, y + h - 30f, w, 24f),
            "💡 每次演武后 AI 会自评并记录，分数越高下次越倾向使用",
            new GUIStyle(_termLogDimStyle) { fontSize = 14 });
    }

    // ==================================================================
    //  子面板内容：消耗（Token 统计）— 累计 + 近 1 小时 + 估算费用
    // ==================================================================
    private void DrawUsageSubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        var (hourCalls, hourPrompt, hourHit, hourCompletion) = UsageStats.GetRecent(3600f);
        float hourCost = UsageStats.EstimateCostYuan(hourPrompt, hourHit, hourCompletion);
        long totalCalls = UsageStats.TotalCalls;
        long totalPrompt = UsageStats.TotalPrompt;
        long totalHit = UsageStats.TotalCacheHit;
        long totalCompletion = UsageStats.TotalCompletion;
        float totalCost = UsageStats.EstimateCostYuan(totalPrompt, totalHit, totalCompletion);

        var big = new GUIStyle(_termLogStyle) { fontSize = 17, richText = true };
        var dim = new GUIStyle(_termLogDimStyle) { fontSize = 14, richText = true };

        // —— 近 1 小时卡片 ——
        float cardH = 150f;
        Rect cardRect = new Rect(x, y, w, cardH);
        UiTextureFactory.DrawPixelRect(cardRect, new Color(0.22f, 0.16f, 0.35f, 0.45f));
        GUI.Label(new Rect(x + 16f, y + 12f, w - 32f, 26f), "⏱ 近 1 小时消耗", new GUIStyle(_termTitleStyle) { fontSize = 18 });
        GUI.Label(new Rect(x + 16f, y + 46f, w - 32f, 24f),
            $"调用 <color=#d8ccff>{hourCalls}</color> 次 · 输入 <color=#d8ccff>{hourPrompt:N0}</color> tokens · 输出 <color=#d8ccff>{hourCompletion:N0}</color>", big);
        GUI.Label(new Rect(x + 16f, y + 76f, w - 32f, 24f),
            $"缓存命中率 <color=#ffd98a>{UsageStats.HitRate(hourPrompt, hourHit) * 100f:F1}%</color> · 估算 <color=#8aff8a>≈ ¥{hourCost:F2}</color> / 小时", big);
        GUI.Label(new Rect(x + 16f, y + 106f, w - 32f, 20f),
            $"（按非高峰价：输入未命中 ¥2/M · 命中 ¥0.5/M · 输出 ¥3/M 估算）", dim);

        // —— 累计（本次会话） ——
        float totalY = y + cardH + 20f;
        Rect totalRect = new Rect(x, totalY, w, cardH);
        UiTextureFactory.DrawPixelRect(totalRect, new Color(0.22f, 0.16f, 0.35f, 0.30f));
        GUI.Label(new Rect(x + 16f, totalY + 12f, w - 32f, 26f), "📈 累计（本次会话）", new GUIStyle(_termTitleStyle) { fontSize = 18 });
        GUI.Label(new Rect(x + 16f, totalY + 46f, w - 32f, 24f),
            $"调用 <color=#d8ccff>{totalCalls}</color> 次 · 输入 <color=#d8ccff>{totalPrompt:N0}</color> tokens · 输出 <color=#d8ccff>{totalCompletion:N0}</color>", big);
        GUI.Label(new Rect(x + 16f, totalY + 76f, w - 32f, 24f),
            $"缓存命中率 <color=#ffd98a>{UsageStats.HitRate(totalPrompt, totalHit) * 100f:F1}%</color> · 估算 <color=#8aff8a>≈ ¥{totalCost:F2}</color>", big);

        // —— 说明 ——
        float noteY = totalY + cardH + 20f;
        GUI.Label(new Rect(x + 16f, noteY, w - 32f, h - (noteY - y) - 10f),
            "💡 说明：\n· 仅统计带 usage 的云端调用（DeepSeek/GLM），本地 Ollama 不花钱不计入\n" +
            "· 数字在面板打开时实时刷新（每次绘制取最新）\n" +
            "· 费用为估算：DeepSeek 8-17 起峰谷定价（高峰 9-12/14-18 点，峰值输出最高 ¥27/M），实际以账单为准\n" +
            "· 想省钱：动作/闲话/天气已本地优先，聊天仍走云端大模型", dim);
    }

    // ==================================================================
    //  子面板内容：便签（卜算记事簿）— 待办/已完成 + 新建 + 列表
    // ==================================================================
    private void DrawRemindersSubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        if (_reminders == null)
        {
            GUI.Label(new Rect(x, y, w, 40f), "⚠ ReminderManager 未初始化", _subLabelStyle);
            return;
        }
        if (!_remindersRefreshed)
        {
            _remindersRefreshed = true;
            _reminderStatusMsg = "";
        }

        int pendingCount = _reminders.PendingCount;
        int doneCount = _reminders.GetDoneReminders().Count;

        // —— 标题行：计数 + 操作按钮 ——
        GUI.Label(new Rect(x, y, 300f, 36f), $"📋 {pendingCount} 待办 / {doneCount} 已完成", _subSectionStyle);
        float btnY = y + 4f;
        Rect addRect = new Rect(x + w - 330f, btnY, 96f, 34f);
        if (GUI.Button(addRect, _showAddReminder ? "▲ 收起" : "✚ 新建", _subBtnStyle))
        {
            _showAddReminder = !_showAddReminder;
            if (!_showAddReminder) { _newReminderText = ""; _newReminderTime = ""; }
        }
        Rect refreshRect = new Rect(x + w - 224f, btnY, 96f, 34f);
        if (GUI.Button(refreshRect, "🔄 刷新", _subBtnStyle))
        {
            _reminderStatusMsg = $"已刷新，{pendingCount} 项待办";
            _reminderStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
        }
        Rect toggleRect = new Rect(x + w - 118f, btnY, 100f, 34f);
        if (GUI.Button(toggleRect, _showDoneReminders ? "⏳ 看待办" : "✅ 已完成", _subBtnStyle))
        {
            _showDoneReminders = !_showDoneReminders;
            _reminderScrollPos = Vector2.zero;
        }

        // —— 新建输入区 ——
        float cursorY = y + 48f;
        if (_showAddReminder && !_showDoneReminders)
        {
            Rect inputBg = new Rect(x, cursorY, w, 176f);
            UiTextureFactory.DrawPixelRect(inputBg, new Color(0.08f, 0.06f, 0.14f, 0.55f));
            UiTextureFactory.DrawPixelRect(new Rect(inputBg.x, inputBg.y, inputBg.width, 1f), new Color(0.58f, 0.42f, 0.88f, 0.5f));

            GUI.Label(new Rect(x + 14f, cursorY + 10f, 120f, 28f), "内容：", _subLabelStyle);
            Rect textField = new Rect(x + 90f, cursorY + 8f, w - 110f, 34f);
            GUI.SetNextControlName("subReminderText");
            _newReminderText = GUI.TextField(textField, _newReminderText, 80, _subInputStyle);

            GUI.Label(new Rect(x + 14f, cursorY + 54f, 130f, 28f), "时间 (可选)：", _subLabelStyle);
            Rect timeField = new Rect(x + 90f, cursorY + 52f, w - 110f, 34f);
            GUI.SetNextControlName("subReminderTime");
            _newReminderTime = GUI.TextField(timeField, _newReminderTime, 24, _subInputStyle);
            GUI.Label(new Rect(x + 14f, cursorY + 90f, w - 30f, 22f),
                "格式 yyyy-MM-dd HH:mm；留空默认 1 小时后", new GUIStyle(_termLogDimStyle) { fontSize = 13 });

            Rect okBtn = new Rect(x + 14f, cursorY + 120f, 120f, 36f);
            if (GUI.Button(okBtn, "✓ 添加", _subBtnStyle))
            {
                if (!string.IsNullOrEmpty(_newReminderText))
                {
                    System.DateTime remindAt;
                    if (string.IsNullOrEmpty(_newReminderTime))
                        remindAt = System.DateTime.Now.AddHours(1);
                    else if (!System.DateTime.TryParse(_newReminderTime, out remindAt))
                    {
                        _reminderStatusMsg = "❌ 时间格式有误";
                        _reminderStatusColor = new Color(1f, 0.45f, 0.45f, 1f);
                        remindAt = System.DateTime.Now.AddHours(1);
                    }
                    if (remindAt <= System.DateTime.Now)
                        remindAt = System.DateTime.Now.AddMinutes(5);
                    _reminders.AddReminder(_newReminderText, remindAt, null, "normal", "user");
                    _reminderStatusMsg = $"✅ 已添加：{_newReminderText}";
                    _reminderStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
                    _newReminderText = "";
                    _newReminderTime = "";
                    _showAddReminder = false;
                }
                else
                {
                    _reminderStatusMsg = "❌ 请输入内容";
                    _reminderStatusColor = new Color(1f, 0.45f, 0.45f, 1f);
                }
            }
            Rect cancelBtn = new Rect(x + 144f, cursorY + 120f, 80f, 36f);
            if (GUI.Button(cancelBtn, "✕ 取消", _subBtnStyle))
            {
                _showAddReminder = false;
                _newReminderText = "";
                _newReminderTime = "";
            }
            cursorY += 186f;
        }

        // —— 状态消息 ——
        if (!string.IsNullOrEmpty(_reminderStatusMsg))
        {
            GUI.Label(new Rect(x, cursorY, w, 26f), _reminderStatusMsg,
                new GUIStyle(_subLabelStyle) { normal = { textColor = _reminderStatusColor }, fontSize = 15 });
            cursorY += 30f;
        }

        // —— 提醒列表 ——
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
            GUI.Label(new Rect(x, cursorY + 20f, w, 40f), emptyMsg,
                new GUIStyle(_subLabelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 17 });
            return;
        }

        // 滚动列表（动态行高，手动逐行绘制）
        float listY = cursorY + 4f;
        float listH = h - (listY - y) - 8f;
        if (listH < 40f) listH = 40f;
        Rect viewRect = new Rect(x, listY, w, listH);
        UiTextureFactory.DrawPixelRect(viewRect, new Color(0.05f, 0.04f, 0.09f, 0.35f));

        // 预计算行高（换行文本按内容宽度估算）
        float itemH = 52f;
        float contentH = Mathf.Max(list.Count * itemH + 8f, listH);
        Rect contentRect = new Rect(0f, 0f, w - 14f, contentH);
        _reminderScrollPos = GUI.BeginScrollView(viewRect, _reminderScrollPos, contentRect, false, false,
            _invisibleScrollbar, _invisibleScrollbar);
        Vector2 localMp = new Vector2(mp.x - viewRect.x + _reminderScrollPos.x, mp.y - viewRect.y + _reminderScrollPos.y);

        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            Rect itemRect = new Rect(4f, 6f + i * itemH, contentRect.width, itemH - 8f);
            string timeStr = "未知时间";
            if (System.DateTime.TryParse(r.remindAt, out var dt))
                timeStr = dt.ToString("MM-dd HH:mm");
            string recurringTag = r.recurring == "daily" ? " 🔄每日" :
                                  r.recurring == "weekday" ? " 🔄工作日" :
                                  r.recurring == "weekly" ? " 🔄每周" : "";
            string priorityTag = "";
            Color itemBg = new Color(0.12f, 0.10f, 0.18f, 0.55f);
            if (r.priority == "high" && !r.done)
            {
                priorityTag = " ⚠️";
                itemBg = new Color(0.30f, 0.12f, 0.14f, 0.55f);
            }
            if (itemRect.Contains(localMp))
                itemBg = new Color(itemBg.r * 1.5f, itemBg.g * 1.3f, itemBg.b * 1.3f, itemBg.a);

            // 条目背景
            UiTextureFactory.DrawPixelRect(itemRect, itemBg);
            // 状态勾选（待办列表）或 ✅（已完成列表）
            float statusX = itemRect.x + 12f;
            if (!_showDoneReminders)
            {
                Rect doneBox = new Rect(statusX, itemRect.y + (itemRect.height - 22f) / 2f, 22f, 22f);
                UiTextureFactory.DrawPixelRect(doneBox, r.done ? new Color(0.55f, 0.85f, 0.55f, 0.9f) : new Color(0.4f, 0.3f, 0.55f, 0.6f));
                GUI.Label(doneBox, r.done ? "✓" : "", new GUIStyle(_subLabelStyle)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                });
                if (doneBox.Contains(localMp) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    if (!r.done)
                    {
                        _reminders.MarkDone(r.id);
                        _reminderStatusMsg = $"✅ 已勾销「{r.text}」";
                        _reminderStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
                    }
                    Event.current.Use();
                }
                statusX += 34f;
            }
            else
            {
                GUI.Label(new Rect(statusX, itemRect.y + (itemRect.height - 22f) / 2f, 26f, 22f), "✅",
                    new GUIStyle(_subLabelStyle) { alignment = TextAnchor.MiddleLeft });
                statusX += 34f;
            }

            // 文本（时间 + 优先级/循环标记 + 内容）
            string display = $"[{timeStr}]{priorityTag}{recurringTag} {r.text}";
            var itemStyle = new GUIStyle(_subLabelStyle)
            {
                fontSize = 16, wordWrap = true, stretchWidth = true,
                normal = { textColor = r.done ? new Color(0.5f, 0.5f, 0.55f, 1f) : new Color(0.92f, 0.90f, 0.98f, 1f) }
            };
            GUI.Label(new Rect(statusX, itemRect.y, contentRect.width - statusX - 44f, itemRect.height - 4f), display, itemStyle);

            // 删除按钮
            Rect delRect = new Rect(contentRect.width - 40f, itemRect.y + (itemRect.height - 28f) / 2f, 28f, 28f);
            if (delRect.Contains(localMp))
                UiTextureFactory.DrawPixelRect(delRect, new Color(0.70f, 0.25f, 0.25f, 0.35f));
            if (GUI.Button(delRect, "✕", _closeBtnStyle))
            {
                _reminders.DeleteReminder(r.id);
                _reminderStatusMsg = $"🗑️ 已删除「{r.text}」";
                _reminderStatusColor = Color.gray;
            }
        }
        GUI.EndScrollView();
    }

    /// <summary>子面板样式初始化（星空紫金主题；由 InitStyles 调用，幂等）</summary>
    private void InitSubPanelStyles()
    {
        if (_subStylesReady) return;
        _subStylesReady = true;
        _subBtnBg = UiTextureFactory.MakeTex(1, 1, new Color(0.42f, 0.30f, 0.62f, 0.85f));
        _subBtnHoverBg = UiTextureFactory.MakeTex(1, 1, new Color(0.55f, 0.42f, 0.80f, 0.95f));
        _subInputBg = UiTextureFactory.MakeTex(1, 1, new Color(0.08f, 0.06f, 0.14f, 0.85f));
        _subSectionBg = UiTextureFactory.MakeTex(1, 1, new Color(0.14f, 0.10f, 0.22f, 0.55f));

        _subBtnStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 16,
            normal = { background = _subBtnBg, textColor = new Color(0.90f, 0.86f, 1.00f, 1f) },
            hover = { background = _subBtnHoverBg, textColor = Color.white },
            active = { background = _subBtnHoverBg, textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(10, 10, 6, 6),
            border = new RectOffset(3, 3, 3, 3)
        };
        _subLabelStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 17,
            normal = { textColor = new Color(0.85f, 0.83f, 0.92f, 1f) },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(6, 6, 4, 4)
        };
        _subSectionStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 18, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.88f, 0.78f, 0.55f, 1f), background = _subSectionBg }, // 太卜司金
            padding = new RectOffset(10, 10, 6, 6),
            border = new RectOffset(3, 3, 3, 3)
        };
        _subInputStyle = new GUIStyle
        {
            font = _monoFont, fontSize = 16,
            normal = { background = _subInputBg, textColor = Color.white },
            focused = { background = _subInputBg, textColor = Color.white },
            active = { background = _subInputBg, textColor = Color.white },
            padding = new RectOffset(10, 10, 6, 6),
            border = new RectOffset(3, 3, 3, 3),
            alignment = TextAnchor.MiddleLeft
        };
        _subScrollStyle = GUIStyle.none;
    }
}
