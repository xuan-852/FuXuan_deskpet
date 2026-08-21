using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// RightPanel 子面板分部（2026-08-14 拆分自 RightPanel.cs）
/// 页内子面板：设置（任务权重）/ 模型设置 / 便签（卜算记事簿）/ 报告（演武心经）/ 忆境（核心记忆治理）。
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
    private Vector2 _memoryScrollPos;        // 忆境页滚动
    private bool _memoryClearConfirm = false;
    private string _memoryStatusMsg = "";
    private Color _memoryStatusColor = Color.gray;
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
    private GUIStyle _modelButtonStyle;
    private GUIStyle _modelBodyStyle;
    private GUIStyle _modelSmallStyle;
    private GUIStyle _modelSectionStyle;
    private Texture2D _subBtnBg;           // 子面板按钮背景
    private Texture2D _subBtnHoverBg;
    private Texture2D _subInputBg;         // 子面板输入框背景
    private Texture2D _subSectionBg;       // 子面板小节背景
    private bool _subStylesReady = false;
    private int _modelDemoIndex = 0;
    private int _selectedChatModelIndex = -1;
    private string _modelStatusMsg = "";
    private Color _modelStatusColor = Color.gray;

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
            PanelView.ModelSettings => "🤖 设置 · 对话模型",
            PanelView.Reminders => "📋 便签 · 卜算记事簿",
            PanelView.Report => "📝 报告 · 演武心经",
            PanelView.Usage => "💰 消耗 · Token 统计",
            PanelView.Memory => "🧠 忆境 · 核心记忆",
            _ => ""
        };
        GUIStyle headerTitleStyle = _currentView == PanelView.ModelSettings
            ? new GUIStyle(_termTitleStyle) { fontSize = 14 }
            : _termTitleStyle;
        GUIStyle headerHintStyle = _currentView == PanelView.ModelSettings
            ? new GUIStyle(_termLogDimStyle) { fontSize = 11 }
            : _termLogDimStyle;
        GUI.Label(new Rect(px + 56f, py + 8f, pw - 200f, 24f), subTitle, headerTitleStyle);
        GUI.Label(new Rect(px + 56f, py + 32f, pw - 200f, 18f),
            _currentView == PanelView.Settings ? "调教本座走位习惯，微调权重以符占卜之数" :
            _currentView == PanelView.ModelSettings ? "选择本地对话模型，并查看新架构的真实生成样例" :
            _currentView == PanelView.Reminders ? "卜算记事簿 — 记要事，勿相忘" :
            _currentView == PanelView.Usage ? "Token 消耗统计 — 看看本座每小时烧多少" :
            _currentView == PanelView.Memory ? "核心事实与长期记忆 — 可查看、清理，不会误触对话注入" :
            "演武心经 — 每次演武后 AI 自评记录的修为报告", headerHintStyle);

        // —— 时间 + ✕ 关闭 ——
        GUI.Label(new Rect(px + pw - 130f, py + 16f, 60f, 22f), _timeDisplay, _termTimeStyle);
        float closeSize = 36f;
        Rect closeRect = new Rect(px + pw - closeSize - 14f, py + (titleH - closeSize) / 2f, closeSize, closeSize);
        if (closeRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(closeRect, new Color(0.80f, 0.25f, 0.25f, 0.35f));
        if (GUI.Button(closeRect, "✕", _closeBtnStyle)) { RequestClosePanel(); }
        RegisterExtHit(closeRect, RequestClosePanel); // 外部命中：✕ 关闭面板/窗口

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
            case PanelView.ModelSettings: DrawModelSettingsSubPanel(contentX, contentY, contentW, contentH, mp); break;
            case PanelView.Reminders: DrawRemindersSubPanel(contentX, contentY, contentW, contentH, mp); break;
            case PanelView.Report: DrawReportSubPanel(contentX, contentY, contentW, contentH, mp); break;
            case PanelView.Usage: DrawUsageSubPanel(contentX, contentY, contentW, contentH, mp); break;
            case PanelView.Memory: DrawMemorySubPanel(contentX, contentY, contentW, contentH, mp); break;
        }

        // 右键关闭（快捷收面板）
        if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
        {
            Close();
            Event.current.Use();
        }
    }

    // ==================================================================
    //  子面板内容：忆境（记忆治理）— 只读浏览 + 过期清理 + 二次确认清空
    // ==================================================================
    private void DrawMemorySubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        var memory = PetMemory.Instance;
        if (memory == null)
        {
            GUI.Label(new Rect(x, y + 24f, w, 40f), "⚠ 忆境模块尚未初始化", new GUIStyle(_subLabelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.55f, 0.55f, 1f) }
            });
            return;
        }

        float actionY = y;
        Rect refreshRect = new Rect(x, actionY, 110f, 38f);
        if (GUI.Button(refreshRect, "🔄 刷新", _subBtnStyle))
        {
            _memoryClearConfirm = false;
            _memoryStatusMsg = "已刷新当前忆境视图";
            _memoryStatusColor = Color.gray;
        }
        RegisterExtHit(refreshRect, () =>
        {
            _memoryClearConfirm = false;
            _memoryStatusMsg = "已刷新当前忆境视图";
            _memoryStatusColor = Color.gray;
        });

        Rect cleanupRect = new Rect(x + 120f, actionY, 140f, 38f);
        if (GUI.Button(cleanupRect, "🧹 清理过期", _subBtnStyle))
            CleanExpiredMemories(memory);
        RegisterExtHit(cleanupRect, () => CleanExpiredMemories(memory));

        Rect clearRect = new Rect(x + 270f, actionY, 140f, 38f);
        string clearLabel = _memoryClearConfirm ? "⚠ 再点一次清空" : "🗑 清空忆境";
        if (GUI.Button(clearRect, clearLabel, _memoryClearConfirm ? _closeBtnStyle : _subBtnStyle))
            RequestClearMemories(memory);
        RegisterExtHit(clearRect, () => RequestClearMemories(memory));

        float cursorY = actionY + 48f;
        if (!string.IsNullOrEmpty(_memoryStatusMsg))
        {
            GUI.Label(new Rect(x, cursorY, w, 24f), _memoryStatusMsg,
                new GUIStyle(_subLabelStyle) { fontSize = 14, normal = { textColor = _memoryStatusColor } });
            cursorY += 28f;
        }

        var facts = memory.GetCoreFacts();
        var entries = memory.GetAllMemories();
        float summaryH = 62f;
        Rect summaryRect = new Rect(x, cursorY, w, summaryH);
        UiTextureFactory.DrawPixelRect(summaryRect, new Color(0.22f, 0.16f, 0.35f, 0.42f));
        GUI.Label(new Rect(x + 14f, cursorY + 8f, w - 28f, 22f),
            $"忆境总览   记忆 {entries.Count}/{memory.maxMemories} 条   ·   核心事实 {facts.Count} 条",
            new GUIStyle(_termTitleStyle) { fontSize = 17 });
        GUI.Label(new Rect(x + 14f, cursorY + 34f, w - 28f, 20f),
            "当前列表为持久化记忆的只读快照；访问次数会在对话检索时更新。",
            new GUIStyle(_termLogDimStyle) { fontSize = 13 });
        cursorY += summaryH + 12f;

        float listH = h - (cursorY - y) - 4f;
        if (listH < 80f) listH = 80f;
        Rect viewRect = new Rect(x, cursorY, w, listH);
        UiTextureFactory.DrawPixelRect(viewRect, new Color(0.05f, 0.04f, 0.09f, 0.35f));

        float itemH = 76f;
        float factH = facts.Count > 0 ? 34f + facts.Count * 42f : 0f;
        float contentH = Mathf.Max(listH, 18f + factH + 38f + entries.Count * itemH);
        Rect contentRect = new Rect(0f, 0f, w - 14f, contentH);
        _memoryScrollPos = GUI.BeginScrollView(viewRect, _memoryScrollPos, contentRect, false, false,
            _invisibleScrollbar, _invisibleScrollbar);

        float localY = 10f;
        var sectionStyle = new GUIStyle(_subSectionStyle) { fontSize = 16 };
        GUI.Label(new Rect(8f, localY, contentRect.width - 16f, 28f), "◆ 核心事实（始终注入）", sectionStyle);
        localY += 34f;
        if (facts.Count == 0)
        {
            GUI.Label(new Rect(16f, localY, contentRect.width - 32f, 30f), "暂无核心事实", _subLabelStyle);
            localY += 34f;
        }
        else
        {
            for (int i = 0; i < facts.Count; i++)
            {
                Rect factRect = new Rect(8f, localY, contentRect.width - 16f, 34f);
                UiTextureFactory.DrawPixelRect(factRect, new Color(0.18f, 0.14f, 0.28f, 0.52f));
                GUI.Label(new Rect(factRect.x + 10f, factRect.y + 2f, factRect.width - 20f, 30f),
                    "• " + facts[i], new GUIStyle(_subLabelStyle) { fontSize = 15, wordWrap = false });
                localY += 42f;
            }
        }

        GUI.Label(new Rect(8f, localY + 2f, contentRect.width - 16f, 28f), "◆ 长期记忆（按时间倒序）", sectionStyle);
        localY += 36f;
        if (entries.Count == 0)
        {
            GUI.Label(new Rect(16f, localY, contentRect.width - 32f, 34f), "暂无长期记忆。完成一次对话或工具任务后再来查看吧。", _subLabelStyle);
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                Rect itemRect = new Rect(8f, localY, contentRect.width - 16f, itemH - 8f);
                bool expired = MemoryGovernance.IsExpired(entry, System.DateTime.Now);
                Color itemBg = expired ? new Color(0.30f, 0.16f, 0.16f, 0.52f) : new Color(0.12f, 0.10f, 0.18f, 0.60f);
                UiTextureFactory.DrawPixelRect(itemRect, itemBg);
                string state = expired ? " · 已过期" : "";
                string meta = $"[{entry.category}/{entry.source}] 重要度 {entry.importance}/10 · 可信度 {entry.confidence:F2} · 访问 {entry.accessCount} 次{state}";
                GUI.Label(new Rect(itemRect.x + 12f, itemRect.y + 6f, itemRect.width - 24f, 30f),
                    entry.summary ?? "（空记忆）", new GUIStyle(_subLabelStyle)
                    {
                        fontSize = 15, wordWrap = true,
                        normal = { textColor = expired ? new Color(1f, 0.65f, 0.65f, 1f) : Color.white }
                    });
                GUI.Label(new Rect(itemRect.x + 12f, itemRect.y + 38f, itemRect.width - 24f, 20f),
                    $"{meta} · 记录 {entry.timestamp}", new GUIStyle(_termLogDimStyle) { fontSize = 12 });
                localY += itemH;
            }
        }
        GUI.EndScrollView();
    }

    private void CleanExpiredMemories(PetMemory memory)
    {
        int removed = memory.RemoveExpiredMemories();
        _memoryClearConfirm = false;
        _memoryStatusMsg = removed > 0 ? $"✅ 已清理过期记忆 {removed} 条" : "当前没有已过期记忆";
        _memoryStatusColor = removed > 0 ? new Color(0.55f, 0.85f, 0.55f, 1f) : Color.gray;
    }

    private void RequestClearMemories(PetMemory memory)
    {
        if (!_memoryClearConfirm)
        {
            _memoryClearConfirm = true;
            _memoryStatusMsg = "再次点击将清空长期记忆与核心事实；此操作不可撤销";
            _memoryStatusColor = new Color(1f, 0.65f, 0.35f, 1f);
            return;
        }
        memory.ClearMemories();
        _memoryClearConfirm = false;
        _memoryStatusMsg = "🧹 忆境已清空";
        _memoryStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
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

        // —— 独立模型设置入口 ——
        Rect modelRect = new Rect(x, y, 230f, 40f);
        if (modelRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(modelRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        if (GUI.Button(modelRect, "🤖 AI 模型设置", _subBtnStyle))
            OpenModelSettings();
        RegisterExtHit(modelRect, OpenModelSettings);

        // —— 小节标题：任务权重 ——
        float sectionY = y + 56f;
        Rect sec1 = new Rect(x, sectionY, w, 36f);
        GUI.Label(sec1, "⚙ 任务权重", _subSectionStyle);
        UiTextureFactory.DrawPixelRect(new Rect(x, sec1.yMax - 1f, w, 1f), new Color(0.45f, 0.35f, 0.65f, 0.3f));

        float rowsY = sec1.yMax + 10f;
        DrawWeightRowGui(x, rowsY, w, rowH, labelW, btnW, btnGap, "向左走到边缘", ref _wLeftEdge, 0, 10, "任务：走到屏幕左边缘的次数权重", v => _wLeftEdge = v);
        DrawWeightRowGui(x, rowsY + rowH * 1, w, rowH, labelW, btnW, btnGap, "向右走到边缘", ref _wRightEdge, 0, 10, "任务：走到屏幕右边缘的次数权重", v => _wRightEdge = v);
        DrawWeightRowGui(x, rowsY + rowH * 2, w, rowH, labelW, btnW, btnGap, "向左走定时", ref _wLeftTime, 0, 10, "任务：向左走动的时间权重", v => _wLeftTime = v);
        DrawWeightRowGui(x, rowsY + rowH * 3, w, rowH, labelW, btnW, btnGap, "向右走定时", ref _wRightTime, 0, 10, "任务：向右走动的时间权重", v => _wRightTime = v);
        DrawWeightRowGui(x, rowsY + rowH * 4, w, rowH, labelW, btnW, btnGap, "停止", ref _wStop, 0, 10, "任务：静止不动的权重", v => _wStop = v);

        // —— 应用权重 ——
        float applyY = rowsY + rowH * 5 + 6f;
        Rect applyRect = new Rect(x, applyY, 180f, 40f);
        if (applyRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(applyRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        if (GUI.Button(applyRect, "✓ 应用权重", _subBtnStyle))
            ApplyWeightsToPet();
        RegisterExtHit(applyRect, ApplyWeightsToPet); // 外部命中：应用权重

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
            var ps = presets[i];
            RegisterExtHit(pr, () => // 外部命中：预设
            {
                _wLeftEdge = ps.le; _wRightEdge = ps.re;
                _wLeftTime = ps.lt; _wRightTime = ps.rt; _wStop = ps.stop;
                ApplyWeightsToPet();
            });
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
        RegisterExtHit(saveRect, () => // 外部命中：保存配置
        {
            if (PetConfig.Instance != null)
            {
                PetConfig.Instance.CollectAll();
                PetConfig.Instance.Save();
            }
        });
        Rect clearRect = new Rect(x + 230f, sec3.yMax + 10f, 180f, 40f);
        if (clearRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(clearRect, new Color(0.70f, 0.25f, 0.25f, 0.20f));
        if (GUI.Button(clearRect, "🗑 清空忆境", _subBtnStyle))
        {
            if (PetMemory.Instance != null)
                PetMemory.Instance.ClearMemories();
        }
        RegisterExtHit(clearRect, () => // 外部命中：清空忆境
        {
            if (PetMemory.Instance != null)
                PetMemory.Instance.ClearMemories();
        });

        // 底部提示
        GUI.Label(new Rect(x, y + h - 34f, w, 24f),
            "💡 权重越高越倾向执行该任务；「保存配置」写入天机簿，重启后生效",
            new GUIStyle(_termLogDimStyle) { fontSize = 14, alignment = TextAnchor.UpperLeft });
    }

    // ==================================================================
    //  子面板内容：对话模型 — 本地模型选择 + 真实生成样例 + 云端占位
    // ==================================================================
    private void DrawModelSettingsSubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        EnsureModelSelection();

        float columnGap = 18f;
        float leftW = Mathf.Min(300f, w * 0.38f);
        float rightX = x + leftW + columnGap;
        float rightW = w - leftW - columnGap;

        DrawModelOptions(x, y, leftW, h, mp);
        DrawModelExamples(rightX, y, rightW, h, mp);

        if (!string.IsNullOrEmpty(_modelStatusMsg))
        {
            GUI.Label(new Rect(x, y + h - 28f, w, 22f), _modelStatusMsg,
                new GUIStyle(_modelSmallStyle) { normal = { textColor = _modelStatusColor } });
        }
    }

    private void EnsureModelSelection()
    {
        if (_selectedChatModelIndex >= 0 && _selectedChatModelIndex < ModelSettingsProfiles.Length) return;
        _selectedChatModelIndex = 0;
        for (int i = 0; i < ModelSettingsProfiles.Length; i++)
        {
            if (string.Equals(ModelSettingsProfiles[i].Model, LocalLLMClient.ChatModelName, StringComparison.OrdinalIgnoreCase))
            {
                _selectedChatModelIndex = i;
                break;
            }
        }
    }

    /// <summary>
    /// 外置模型页命中兜底。坐标必须与 DrawModelOptions/DrawModelExamples 的布局公式保持一致，
    /// 用来覆盖页面切换后命中表还未完成一帧重建的极短窗口。
    /// </summary>
    private bool TryHandleModelSettingsInput(float x, float y)
    {
        const float contentX = 16f;
        const float contentY = 68f; // 标题栏 54 + 内容上边距 14
        const float leftW = 300f;
        const float gap = 18f;
        const float optionY = contentY + 84f;

        for (int i = 0; i < ModelSettingsProfiles.Length; i++)
        {
            if (new Rect(contentX, optionY + i * 72f, leftW, 62f).Contains(new Vector2(x, y)))
            {
                SelectModelProfile(i);
                return true;
            }
        }

        float applyY = optionY + ModelSettingsProfiles.Length * 72f + 14f;
        if (new Rect(contentX, applyY, leftW, 42f).Contains(new Vector2(x, y)))
        {
            if (_selectedChatModelIndex >= 0 && !ModelSettingsProfiles[_selectedChatModelIndex].Cloud)
                ApplySelectedChatModel();
            return true;
        }

        float panelW = Mathf.Max(860f, _panelRect.width);
        float contentW = panelW - 32f;
        float rightX = contentX + leftW + gap;
        float rightW = contentW - leftW - gap;
        float tabY = contentY + 80f;
        float tabW = (rightW - 12f) / 3f;
        for (int i = 0; i < LocalModelDemoData.Qwen3Samples.Length; i++)
        {
            if (new Rect(rightX + i * (tabW + 6f), tabY, tabW, 34f).Contains(new Vector2(x, y)))
            {
                _modelDemoIndex = i;
                Debug.Log("[RightPanel] 模型样例切换: " + LocalModelDemoData.Qwen3Samples[i].CaseId);
                return true;
            }
        }
        return false;
    }

    private void DrawModelOptions(float x, float y, float w, float h, Vector2 mp)
    {
        GUI.Label(new Rect(x, y, w, 34f), "选择聊天模型", _modelSectionStyle);
        GUI.Label(new Rect(x, y + 34f, w, 40f),
            "聊天模型独立于动作模型，质量优先时可使用更大的本地模型。",
            _modelSmallStyle);

        float optionY = y + 84f;
        for (int i = 0; i < ModelSettingsProfiles.Length; i++)
        {
            ModelProfile profile = ModelSettingsProfiles[i];
            Rect optionRect = new Rect(x, optionY + i * 72f, w, 62f);
            bool selected = i == _selectedChatModelIndex;
            if (selected)
                UiTextureFactory.DrawPixelRect(optionRect, new Color(0.45f, 0.30f, 0.78f, 0.30f));
            else if (optionRect.Contains(mp))
                UiTextureFactory.DrawPixelRect(optionRect, new Color(0.45f, 0.30f, 0.78f, 0.14f));

            string state = profile.Cloud ? "待配置" : (LocalLLMClient.IsModelReady(profile.Model) ? "已检测" : "可选择");
            string label = (selected ? "● " : "○ ") + profile.Title + "\n" + profile.Model + " · " + state;
            int profileIndex = i;
            if (GUI.Button(optionRect, label, _modelButtonStyle))
                SelectModelProfile(profileIndex);
            RegisterExtHit(optionRect, () => SelectModelProfile(profileIndex));
        }

        float applyY = optionY + ModelSettingsProfiles.Length * 72f + 14f;
        Rect applyRect = new Rect(x, applyY, w, 42f);
        bool cloud = ModelSettingsProfiles[_selectedChatModelIndex].Cloud;
        if (!cloud && GUI.Button(applyRect, "✓ 应用本地模型", _modelButtonStyle))
            ApplySelectedChatModel();
        RegisterExtHit(applyRect, () => { if (!cloud) ApplySelectedChatModel(); });

        GUI.Label(new Rect(x, applyY + 52f, w, 64f),
            "当前聊天：" + LocalLLMClient.ChatModelName + "\n动作/摘要：" + LocalLLMClient.ModelName + "\n两者分离，避免动作循环跟着升高占用。",
            _modelSmallStyle);
    }

    private void SelectModelProfile(int index)
    {
        if (index < 0 || index >= ModelSettingsProfiles.Length) return;
        ModelProfile profile = ModelSettingsProfiles[index];
        _selectedChatModelIndex = index;
        _modelStatusMsg = profile.Cloud
            ? "云端模型暂未启用：需要先完成安全的 API Key 存储与脱敏。"
            : "已选择 " + profile.Model + "，点击「应用本地模型」后生效。";
        _modelStatusColor = profile.Cloud ? new Color(1f, 0.70f, 0.35f, 1f) : Color.gray;
        Debug.Log("[RightPanel] 模型设置页选择: " + profile.Model);
    }

    private void ApplySelectedChatModel()
    {
        ModelProfile profile = ModelSettingsProfiles[_selectedChatModelIndex];
        if (profile.Cloud) return;
        LocalLLMClient.SetChatModel(profile.Model);
        if (PetConfig.Instance != null)
        {
            PetConfig.Instance.CollectAll();
            PetConfig.Instance.Save();
        }
        _modelStatusMsg = "已应用 " + profile.Model + "；下一次对话将使用该模型。";
        _modelStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
        Debug.Log("[RightPanel] 对话模型已切换: " + profile.Model);
    }

    private void DrawModelExamples(float x, float y, float w, float h, Vector2 mp)
    {
        GUI.Label(new Rect(x, y, w, 34f), "生成质量对比", _modelSectionStyle);
        GUI.Label(new Rect(x, y + 34f, w, 38f),
            "以下是隔离测试得到的真实回复，不会写入生产忆境。",
            _modelSmallStyle);

        float tabsY = y + 80f;
        float tabW = (w - 12f) / 3f;
        for (int i = 0; i < LocalModelDemoData.Qwen3Samples.Length; i++)
        {
            Rect tab = new Rect(x + i * (tabW + 6f), tabsY, tabW, 34f);
            if (i == _modelDemoIndex)
                UiTextureFactory.DrawPixelRect(tab, new Color(0.45f, 0.30f, 0.78f, 0.30f));
            int demoIndex = i;
            if (GUI.Button(tab, LocalModelDemoData.Qwen3Samples[i].CaseId, _modelButtonStyle))
                _modelDemoIndex = demoIndex;
            RegisterExtHit(tab, () => _modelDemoIndex = demoIndex);
        }

        LocalModelDemoData.Sample sample = LocalModelDemoData.Qwen3Samples[Mathf.Clamp(_modelDemoIndex, 0, LocalModelDemoData.Qwen3Samples.Length - 1)];
        float cardY = tabsY + 48f;
        float cardH = Mathf.Min(470f, h - 180f);
        UiTextureFactory.DrawPixelRect(new Rect(x, cardY, w, cardH), new Color(0.04f, 0.05f, 0.13f, 0.72f));
        GUI.Label(new Rect(x + 14f, cardY + 12f, w - 28f, 24f),
            "本地真实样例 · " + LocalModelDemoData.TestModel + " · " + LocalModelDemoData.TestDate,
            _modelSmallStyle);
        GUI.Label(new Rect(x + 14f, cardY + 48f, w - 28f, 54f),
            "用户：" + sample.Input,
            _modelBodyStyle);
        GUI.Label(new Rect(x + 14f, cardY + 108f, w - 28f, cardH - 172f),
            "符玄：" + sample.Reply,
            _modelBodyStyle);
        GUI.Label(new Rect(x + 14f, cardY + cardH - 52f, w - 28f, 40f),
            string.Format("耗时 {0:0.0}s · {1} 字 · 规则评分 {2}/5", sample.LatencyMs / 1000f, sample.ReplyChars, sample.RuleScore),
            _modelSmallStyle);

        float cloudY = cardY + cardH + 18f;
        UiTextureFactory.DrawPixelRect(new Rect(x, cloudY, w, 104f), new Color(0.12f, 0.08f, 0.20f, 0.78f));
        GUI.Label(new Rect(x + 14f, cloudY + 12f, w - 28f, 24f), "云端模型 · DeepSeek", _modelBodyStyle);
        GUI.Label(new Rect(x + 14f, cloudY + 40f, w - 28f, 54f),
            "暂未放入伪造对照文本。接入云端前需由用户配置专属 API Key，并完成安全存储、脱敏日志与消耗记录。",
            _modelSmallStyle);

        GUI.Label(new Rect(x, h + y - 28f, w, 22f),
            string.Format("本批次：3/3 成功 · 平均 {0:0.0}s · 小样本仅用于选型参考", LocalModelDemoData.AverageLatencyMs / 1000f),
            _modelSmallStyle);
    }

    private struct ModelProfile
    {
        public readonly string Title;
        public readonly string Model;
        public readonly bool Cloud;

        public ModelProfile(string title, string model, bool cloud)
        {
            Title = title;
            Model = model;
            Cloud = cloud;
        }
    }

    private static readonly ModelProfile[] ModelSettingsProfiles =
    {
        new ModelProfile("质量优先", "qwen3:8b", false),
        new ModelProfile("均衡体验", "qwen2.5:3b", false),
        new ModelProfile("低端设备", "qwen2.5:1.5b", false),
        new ModelProfile("极低占用", "qwen2.5:0.5b", false),
        new ModelProfile("云端对话", "DeepSeek API", true)
    };

    /// <summary>权重调节行：标签 + [-] 数值 [+]；onExtSet 为外部命中时的字段写入回调（null=不登记）</summary>
    private void DrawWeightRowGui(float x, float y, float w, float rowH, float labelW, float btnW, float btnGap,
        string label, ref int value, int min, int max, string tip, System.Action<int> onExtSet = null)
    {
        GUI.Label(new Rect(x, y, labelW, rowH), label, _subLabelStyle);
        float valX = x + labelW + 20f;
        float valW = 64f;
        // [-] 按钮
        Rect minusRect = new Rect(valX + valW + 8f, y + 4f, btnW, rowH - 8f);
        if (GUI.Button(minusRect, "−", _subBtnStyle)) value = Mathf.Max(min, value - 1);
        if (onExtSet != null)
        {
            int mn = min;
            RegisterExtHit(minusRect, () => onExtSet(Mathf.Max(mn, GetWeightRef(label) - 1)));
        }
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
        if (onExtSet != null)
        {
            int mx = max;
            RegisterExtHit(plusRect, () => onExtSet(Mathf.Min(mx, GetWeightRef(label) + 1)));
        }
        // 提示（hover 数值区时）
        if (valBg.Contains(Event.current.mousePosition))
            GUI.Label(new Rect(x, y + rowH, w, 20f), tip, new GUIStyle(_termLogDimStyle) { fontSize = 13 });
    }

    /// <summary>读取当前权重字段（外部命中用；label 匹配）</summary>
    private int GetWeightRef(string label)
    {
        switch (label)
        {
            case "向左走到边缘": return _wLeftEdge;
            case "向右走到边缘": return _wRightEdge;
            case "向左走定时": return _wLeftTime;
            case "向右走定时": return _wRightTime;
            case "停止": return _wStop;
            default: return 0;
        }
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
        RegisterExtHit(refreshRect, () => { /* 刷新：下次绘制自动取最新 */ });
        Rect copyRect = new Rect(x + 110f, y, 100f, 38f);
        if (GUI.Button(copyRect, "📋 复制", _subBtnStyle))
        {
            if (!string.IsNullOrEmpty(_lastReportText))
            {
                GUIUtility.systemCopyBuffer = _lastReportText;
                Debug.Log("[RightPanel] 报告已复制到剪贴板");
            }
        }
        RegisterExtHit(copyRect, () => // 外部命中：复制报告
        {
            if (!string.IsNullOrEmpty(_lastReportText))
            {
                GUIUtility.systemCopyBuffer = _lastReportText;
                Debug.Log("[RightPanel] 报告已复制到剪贴板（外部）");
            }
        });

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

        // —— 累计（含跨重启历史） ——
        float totalY = y + cardH + 20f;
        Rect totalRect = new Rect(x, totalY, w, cardH);
        UiTextureFactory.DrawPixelRect(totalRect, new Color(0.22f, 0.16f, 0.35f, 0.30f));
        GUI.Label(new Rect(x + 16f, totalY + 12f, w - 32f, 26f), "📈 累计（跨重启）", new GUIStyle(_termTitleStyle) { fontSize = 18 });
        GUI.Label(new Rect(x + 16f, totalY + 46f, w - 32f, 24f),
            $"调用 <color=#d8ccff>{totalCalls}</color> 次 · 输入 <color=#d8ccff>{totalPrompt:N0}</color> tokens · 输出 <color=#d8ccff>{totalCompletion:N0}</color>", big);
        GUI.Label(new Rect(x + 16f, totalY + 76f, w - 32f, 24f),
            $"缓存命中率 <color=#ffd98a>{UsageStats.HitRate(totalPrompt, totalHit) * 100f:F1}%</color> · 估算 <color=#8aff8a>≈ ¥{totalCost:F2}</color>", big);

        // —— 来源明细（长效日志，钱花在哪一目了然） ——
        float srcY = totalY + cardH + 12f;
        var srcSummary = UsageLogger.SummarizeBySource();
        if (srcSummary.Count > 0)
        {
            float srcCardH = 40f + srcSummary.Count * 24f;
            Rect srcRect = new Rect(x, srcY, w, Mathf.Min(srcCardH, h - (srcY - y) - 110f));
            UiTextureFactory.DrawPixelRect(srcRect, new Color(0.16f, 0.20f, 0.32f, 0.35f));
            GUI.Label(new Rect(x + 16f, srcY + 10f, w - 32f, 24f), "🧾 来源明细（长效日志）", new GUIStyle(_termTitleStyle) { fontSize = 16 });
            float ly = srcY + 40f;
            foreach (var kv in srcSummary)
            {
                string name = kv.Key switch
                {
                    "chat" => "聊天/工具",
                    "motion" => "动作翻译",
                    "idle" => "闲话问候",
                    "weather" => "天气语录",
                    "reflect" => "记忆提炼",
                    "glm" => "GLM视觉",
                    "local" => "本地Ollama",
                    _ => kv.Key
                };
                string costStr = kv.Key == "local" ? "免费" : $"¥{kv.Value.Item5:F2}";
                GUI.Label(new Rect(x + 16f, ly, w - 32f, 20f),
                    $"· {name}：{kv.Value.Item1} 次 · 输入{kv.Value.Item2:N0} · 输出{kv.Value.Item4:N0} · <color=#8aff8a>{costStr}</color>", dim);
                ly += 24f;
            }
            srcY = ly + 12f;
        }

        // —— 说明 ——
        float noteY = srcY;
        GUI.Label(new Rect(x + 16f, noteY, w - 32f, h - (noteY - y) - 10f),
            "💡 说明：\n· 长效日志落盘于 usage_log.jsonl（上限 2MB，重启保留）\n" +
            "· 仅统计带 usage 的云端调用（DeepSeek/GLM），本地 Ollama 记免费对比\n" +
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
        RegisterExtHit(addRect, () => // 外部命中：新建/收起
        {
            _showAddReminder = !_showAddReminder;
            if (!_showAddReminder) { _newReminderText = ""; _newReminderTime = ""; }
        });
        Rect refreshRect = new Rect(x + w - 224f, btnY, 96f, 34f);
        if (GUI.Button(refreshRect, "🔄 刷新", _subBtnStyle))
        {
            _reminderStatusMsg = $"已刷新，{pendingCount} 项待办";
            _reminderStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
        }
        RegisterExtHit(refreshRect, () => // 外部命中：刷新
        {
            _reminderStatusMsg = $"已刷新，{_reminders.PendingCount} 项待办";
            _reminderStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
        });
        Rect toggleRect = new Rect(x + w - 118f, btnY, 100f, 34f);
        if (GUI.Button(toggleRect, _showDoneReminders ? "⏳ 看待办" : "✅ 已完成", _subBtnStyle))
        {
            _showDoneReminders = !_showDoneReminders;
            _reminderScrollPos = Vector2.zero;
        }
        RegisterExtHit(toggleRect, () => // 外部命中：待办/已完成切换
        {
            _showDoneReminders = !_showDoneReminders;
            _reminderScrollPos = Vector2.zero;
        });

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
                TryAddReminder();
            }
            RegisterExtHit(okBtn, TryAddReminder); // 外部命中：添加便签
            Rect cancelBtn = new Rect(x + 144f, cursorY + 120f, 80f, 36f);
            if (GUI.Button(cancelBtn, "✕ 取消", _subBtnStyle))
            {
                _showAddReminder = false;
                _newReminderText = "";
                _newReminderTime = "";
            }
            RegisterExtHit(cancelBtn, () => // 外部命中：取消新建
            {
                _showAddReminder = false;
                _newReminderText = "";
                _newReminderTime = "";
            });
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
                // 外部命中：勾销（滚动偏移补偿）
                Rect extDoneBox = new Rect(doneBox.x + viewRect.x - _reminderScrollPos.x,
                    doneBox.y + viewRect.y - _reminderScrollPos.y, doneBox.width, doneBox.height);
                RegisterExtHit(extDoneBox, () =>
                {
                    if (!r.done)
                    {
                        _reminders.MarkDone(r.id);
                        _reminderStatusMsg = $"✅ 已勾销「{r.text}」";
                        _reminderStatusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
                    }
                });
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
            // 外部命中：删除（滚动偏移补偿）
            Rect extDelRect = new Rect(delRect.x + viewRect.x - _reminderScrollPos.x,
                delRect.y + viewRect.y - _reminderScrollPos.y, delRect.width, delRect.height);
            RegisterExtHit(extDelRect, () =>
            {
                _reminders.DeleteReminder(r.id);
                _reminderStatusMsg = $"🗑️ 已删除「{r.text}」";
                _reminderStatusColor = Color.gray;
            });
        }
        GUI.EndScrollView();
    }

    /// <summary>添加便签（内嵌按钮与外部命中共用）</summary>
    private void TryAddReminder()
    {
        if (_reminders == null) return;
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
        _modelButtonStyle = new GUIStyle(_subBtnStyle)
        {
            fontSize = 14,
            padding = new RectOffset(8, 8, 4, 4),
            wordWrap = true
        };
        _modelBodyStyle = new GUIStyle(_subLabelStyle)
        {
            fontSize = 14,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        _modelSmallStyle = new GUIStyle(_termLogDimStyle)
        {
            fontSize = 11,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        _modelSectionStyle = new GUIStyle(_subSectionStyle)
        {
            fontSize = 15,
            padding = new RectOffset(8, 8, 4, 4)
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
