using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// RightPanel 聊天视图分部（2026-08-14 拆分自 RightPanel.cs）
/// QQ 式两级界面的聊天侧：会话字段 + 聊天区绘制（DrawChatArea）+ 会话列表/侧栏/刷新。
/// 改聊天 UI（气泡/输入栏/会话列表）只动本文件；与主文件共用实例状态（partial class）。
/// </summary>
public partial class RightPanel
{
    private Vector2 _sessionScroll;              // 第一级会话列表滚动位置
    private bool _sessionDirty = true;           // 会话列表数据待刷新（History 变化置 true）
    private int _activeSession = 0;              // 当前会话索引（第二级左侧高亮）
    private readonly List<ChatSession> _sessions = new List<ChatSession>(); // 会话列表（当前单角色，多角色扩展预留）
    private float _inputCaretBlinkStart = -1f;
    private string _inputCaretTextSnapshot = string.Empty;
    private bool _inputCaretFocusSnapshot;
    private const float INPUT_CARET_BLINK_PERIOD = 1.0f;
    // 外置输入栏的可见水平视口。原生 EDIT 仅作键盘/IME 宿主，Unity 字层必须自己
    // 维护横向滚动，否则长文本会一直从第 0 个字符绘制，光标只能被夹在右边。
    private float _inputHorizontalScroll;

    private float GetInputTextEndX(Rect inputRect, string text)
    {
        text = text ?? string.Empty;
        Vector2 measured = _termInputStyle.CalcSize(new GUIContent(text));
        float textWidth = Mathf.Max(0f,
            measured.x - _termInputStyle.padding.left - _termInputStyle.padding.right);
        return inputRect.x + _termInputStyle.padding.left + textWidth;
    }

    private float GetInputTextCaretX(Rect inputRect, string text, int caretIndex)
    {
        text = text ?? string.Empty;
        int index = Mathf.Clamp(caretIndex, 0, text.Length);
        return GetInputTextEndX(inputRect,
            index == text.Length ? text : text.Substring(0, index));
    }

    /// <summary>
    /// 让外置 Unity 字层的横向视口跟随插入光标，并在删除文字后向左回收空出来的区域。
    /// 这里的 x 使用 IMGUI 逻辑坐标；实际绘制时会在输入框局部 Group 内减去该偏移。
    /// </summary>
    private float UpdateInputHorizontalScroll(Rect inputRect, string visualText, int caretIndex)
    {
        visualText = visualText ?? string.Empty;
        int index = Mathf.Clamp(caretIndex, 0, visualText.Length);
        float visibleLeft = inputRect.x + _termInputStyle.padding.left;
        float visibleRight = inputRect.xMax - _termInputStyle.padding.right - 3f;
        float caretX = GetInputTextCaretX(inputRect, visualText, index);
        float contentEndX = GetInputTextEndX(inputRect, visualText);
        float scroll = Mathf.Max(0f, _inputHorizontalScroll);

        if (caretX - scroll > visibleRight)
            scroll = caretX - visibleRight;
        else if (caretX - scroll < visibleLeft)
            scroll = caretX - visibleLeft;

        // 文本变短或焦点切回开头时，不能保留已经失效的右移量。
        float maxScroll = Mathf.Max(0f, contentEndX - visibleRight);
        _inputHorizontalScroll = Mathf.Clamp(scroll, 0f, maxScroll);
        return _inputHorizontalScroll;
    }

    /// <summary>把外置 Unity 字层中的点击位置转换为隐藏 EDIT 的插入点。</summary>
    private void SetExternalInputCaretFromClick(Rect inputRect, Vector2 clickPosition)
    {
        string text = _inputText ?? string.Empty;
        float localX = clickPosition.x - inputRect.x;
        float localY = clickPosition.y - inputRect.y;
        float scroll = _inputHorizontalScroll;

        // GetCursorStringIndex 会按 GUIStyle 的真实字体、padding 和 Unicode 字宽
        // 选择最近的 UTF-16 边界，比按字符平均宽度估算可靠（中英文混排尤其明显）。
        Rect textRect = new Rect(-scroll, 0f,
            inputRect.width + scroll + 64f, inputRect.height);
        int index = _termInputStyle.GetCursorStringIndex(
            textRect, new GUIContent(text), new Vector2(localX, localY));
        index = Mathf.Clamp(index, 0, text.Length);
        ExternalChatWindow.SetInputSelection(index, index);
    }

    /// <summary>会话条目（QQ 式会话列表项）</summary>
    private class ChatSession
    {
        public string name;        // 会话/角色名
        public string lastMsg;     // 最后消息摘要（截断显示）
        public string lastTime;    // 最后时间 HH:mm
        public Texture2D avatar;   // 头像纹理
    }

    /// <summary>第二级聊天区整体绘制（终端标题栏 + 消息列表 + 输入栏；由 OnGUI 在右移 SIDEBAR_W 后调用）</summary>
    private void DrawChatArea(float px, float py, float pw, float ph, Vector2 mp)
    {
        HolidayThemeRuntime.ThemeSkin skin = HolidayThemeRuntime.Active.Skin;
        // ═══════════════════════════════════════
        //  终端标题栏 — [像素符玄] 符玄@太卜司:~ + 状态 + 时间 + ✕
        // ═══════════════════════════════════════
        float titleH = 54f;
        Rect titleBarRect = new Rect(px + 2f, py + 2f, pw - 4f, titleH);
        // 像素渐变标题栏背景
        GUI.DrawTexture(titleBarRect, _titleBarPixelTex);
        // 标题栏底部分隔线
        UiTextureFactory.DrawPixelRect(new Rect(px + 2f, py + 2f + titleH, pw - 4f, 1f), new Color(0.58f, 0.42f, 0.88f, 0.6f));

        // ◀ 返回会话列表（QQ 式展开窗口的收起按钮，聊天区左上角）
        Rect backRect = new Rect(px + 10f, py + 10f, 34f, 34f);
        if (backRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(backRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        if (GUI.Button(backRect, "◀", _termToolBtnStyle))
        {
            if (!_externalRender) BackToSessionList();
        }
        RegisterExtHit(backRect, BackToSessionList); // 外部命中：返回会话列表

        // 符玄头像（标题栏左侧，30×30，带深色描边以增强对比）
        float fxHeadSize = 38f;
        Rect fxHeadRect = new Rect(px + 54f, py + 6f, fxHeadSize, fxHeadSize);
        GUI.DrawTexture(fxHeadRect, _pixelFxTex);

        bool waiting = _chat != null && _chat.IsWaiting;
        float statusPulse = waiting ? 0.55f + 0.45f * Mathf.Sin(Time.time * 3f) : 1f; // 思考中 → 紫色呼吸

        // ★ 状态优先级：OpenClaw 任务步骤 > 思考中 > 就绪（类智能体入口·进度可见）
        string statusText;
        Color statusC;
        if (OpenClawBridge.HasActiveTask && !string.IsNullOrEmpty(OpenClawBridge.ActiveStepLabel))
        {
            // 任务执行中 → 金色 ⚙ + 步骤（工具名+摘要），呼吸提示运转中
            float taskPulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 2.2f);
            statusText = "⚙ " + OpenClawBridge.ActiveStepLabel;
            statusC = new Color(skin.StatusTask.r, skin.StatusTask.g, skin.StatusTask.b, taskPulse);
        }
        else if (waiting)
        {
            statusText = "● 思考中…";
            statusC = new Color(skin.StatusBusy.r, skin.StatusBusy.g, skin.StatusBusy.b, statusPulse);
        }
        else
        {
            statusText = "● 就绪";
            statusC = skin.StatusReady;  // 就绪
        }

        if (waiting && _chat != null && !string.IsNullOrEmpty(_chat.RequestStatusText))
            statusText = "● " + _chat.RequestStatusText;
        else if (!waiting && RuntimeReadinessService.Instance != null)
            statusText = "● " + RuntimeReadinessService.Instance.ShortStatus;

        GUI.color = statusC;
        GUI.DrawTexture(new Rect(px + fxHeadSize + 16f, py + titleH / 2f - 4f, 9f, 9f), _statusDotTex);
        GUI.color = _panelTint;

        // 卦象三爻装饰（金色，太卜司占卜符号）
        if (_hexagramTex != null)
            GUI.DrawTexture(new Rect(px + fxHeadSize + 32f, py + titleH / 2f - 10f, 18f, 18f), _hexagramTex);

        GUI.Label(new Rect(px + fxHeadSize + 60f, py + 4f, pw - 260f, 24f), "符玄@太卜司: ~", _termTitleStyle);
        GUI.Label(new Rect(px + fxHeadSize + 60f, py + 29f, pw - 260f, 20f), statusText, _termStatusStyle);

        // ——— 字体档位按钮（时间左侧，点击循环 A → A2 → A3 → A4） ———
        Rect fontBtnRect = new Rect(px + pw - 170f, py + 10f, 40f, 34f);
        if (fontBtnRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(fontBtnRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        string fontLbl = _fontScaleLevel == 0 ? "A" : "A" + (_fontScaleLevel + 1);
        string fontTip = "字体大小: " + FONT_SCALES[_fontScaleLevel] + "x (档 " + (_fontScaleLevel + 1) + "/4)";
        if (!_externalRender && GUI.Button(fontBtnRect, new GUIContent(fontLbl, fontTip), _termToolBtnStyle))
            CycleFontScale();
        RegisterExtHit(fontBtnRect, CycleFontScale); // 外部命中：字体档位

        // ——— ⧉ 独立窗口切换（QQ 式：聊天面板可被其他窗口遮挡；2026-08-15） ———
        Rect extBtnRect = new Rect(px + pw - 210f, py + 10f, 40f, 34f);
        if (!_externalRender)
        {
        // ★ 常态背景方块（细线符号在深底上不可见，加底衬突出按钮区域）
        UiTextureFactory.DrawPixelRect(extBtnRect, _externalMode
            ? new Color(0.55f, 0.40f, 0.85f, 0.45f)   // 外置激活中 → 亮紫
            : new Color(0.30f, 0.22f, 0.45f, 0.30f)); // 未激活 → 暗紫
        if (extBtnRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(extBtnRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        // ★ 图标：程序绘制两窗方块（不用 ⧉ 字形——等宽字体无字形渲染空白）
        if (_extWindowIconTex != null)
        {
            Rect iconR = new Rect(extBtnRect.center.x - 11f, extBtnRect.center.y - 10f, 22f, 20f);
            GUI.DrawTexture(iconR, _extWindowIconTex);
            if (!_externalRender && Event.current.type == EventType.MouseDown && Event.current.button == 0 && extBtnRect.Contains(mp))
            {
                Event.current.Use();
                ToggleExternalMode();
            }
        }
        else
        {
            if (!_externalRender && GUI.Button(extBtnRect, "独立", _termToolBtnStyle))
                ToggleExternalMode();
        }
        RegisterExtHit(extBtnRect, ToggleExternalMode); // 外部命中：⧉ 退回内嵌/再外置
        }

        // 时间（标题栏右，✕ 左侧）
        RefreshTime();
        // 给同一标题行的最小化按钮预留位置，避免时间文字与按钮叠加。
        GUI.Label(new Rect(px + pw - 150f, py + 14f, 56f, 20f), _timeDisplay, _termTimeStyle);

        // ——— ✕ 关闭按钮（右上角，像素方块风格） ———
        float closeSize = 32f + _fontScaleLevel * 2f;
        Rect closeRect = new Rect(px + pw - closeSize - 12f, py + 10f, closeSize, closeSize);
        // hover 时画红色方块背景
        if (closeRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(closeRect, new Color(0.80f, 0.25f, 0.25f, 0.35f));
        if (!_externalRender && GUI.Button(closeRect, "✕", _closeBtnStyle))
        {
            Close();
        }
        else if (_externalRender)
        {
            // 外置窗口的 X 是窗口关闭，不再调用 Unity 面板 Close() 淡出。
            GUI.Label(closeRect, "✕", _closeBtnStyle);
        }
        if (_externalRender)
        {
            // 最小化与 X 共用聊天标题行，避免再出现第二层“独立面板”标题栏。
            Rect minRect = new Rect(closeRect.x - closeRect.width - 6f, closeRect.y, closeRect.width, closeRect.height);
            UiTextureFactory.DrawPixelRect(minRect, new Color(0.30f, 0.22f, 0.45f, 0.42f));
            UiTextureFactory.DrawPixelRect(new Rect(minRect.x + 8f, minRect.center.y, minRect.width - 16f, 2f),
                new Color(0.78f, 0.66f, 0.98f, 0.9f));
            RegisterExtHit(minRect, ExternalChatWindow.Minimize);
            RegisterExtHit(closeRect, ExternalChatWindow.RequestClose);
        }

        // ——— 标题栏拖动（按住标题栏移动窗口，排除 ✕ / ◀ 返回 / 字体按钮防误触） ———
        if (!_externalRender && Event.current.type == EventType.MouseDown && Event.current.button == 0
            && titleBarRect.Contains(mp)
            && !closeRect.Contains(mp) && !backRect.Contains(mp) && !fontBtnRect.Contains(mp) && !extBtnRect.Contains(mp))
        {
            _isDragging = true;
            // ★ 修复：此处 px 已被 += SIDEBAR_W 右移，必须用窗口原点 _panelRect 计算偏移，
            //   否则窗口会偏离鼠标点击点固定距离（拖拽吸边）
            _dragOffset = mp - new Vector2(_panelRect.x, _panelRect.y);
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
        float toolRowY = py + titleH + 10f;
        float toolBtnW = 64f;
        float toolBtnH = 36f + _fontScaleLevel * 3f;
        float toolBtnGap = 6f;
        float toolTotalW = _tools.Length * toolBtnW + (_tools.Length - 1) * toolBtnGap;
        float toolStartX = px + (pw - toolTotalW) / 2f;

        int hoveredTool = -1;
        Rect hoveredToolRect = default;
        for (int i = 0; i < _tools.Length; i++)
        {
            Rect tbRect = new Rect(toolStartX + i * (toolBtnW + toolBtnGap), toolRowY, toolBtnW, toolBtnH);
            // 外置 RT 的 mp 是客户区局部坐标，不能再拿 Unity 屏幕坐标的 _panelRect 判断。
            bool tbHover = (_externalRender || _panelRect.Contains(mp)) && tbRect.Contains(mp);
            bool tbActive = _tools[i].label == "聊天";
            if (tbHover)
            {
                hoveredTool = i;
                hoveredToolRect = tbRect;
            }
            // 当前页使用稳定的低对比度底色，hover 只提高亮度，避免所有按钮同时抢焦点。
            if (tbActive || tbHover)
                UiTextureFactory.DrawPixelRect(tbRect, tbActive
                    ? new Color(0.46f, 0.34f, 0.70f, 0.30f)
                    : new Color(0.50f, 0.35f, 0.80f, 0.22f));
            UiTextureFactory.DrawPixelRect(new Rect(tbRect.x, tbRect.yMax - 1f, tbRect.width, 1f),
                tbActive ? new Color(0.78f, 0.64f, 1.00f, 0.9f)
                    : (tbHover ? new Color(0.66f, 0.50f, 0.95f, 0.8f) : new Color(0.40f, 0.28f, 0.65f, 0.3f)));
            if (GUI.Button(tbRect, _tools[i].icon, (tbActive || tbHover) ? _termToolBtnHoverStyle : _termToolBtnStyle))
            {
                var tool = _tools[i];
                if (!_externalRender && tool.panelType.HasValue)
                {
                    // ——— 重构：三个页面改为对话框内部视图（星空风格），不再弹独立灰窗 BallPanel ———
                    OpenSubPanel(tool.panelType.Value);
                }
                else if (!_externalRender && tool.label == "聊天")
                {
                    _inputFocused = true;
                    GUI.FocusControl("rightPanelInput");
                }
                else if (!_externalRender && tool.label == "收纳")
                {
                    LaunchPogget();
                }
            }
            // 外部命中：工具按钮（面板型直接开子面板；聊天聚焦输入；收纳忽略）
            var tl = _tools[i];
            if (tl.panelType.HasValue)
            {
                var pt = tl.panelType.Value;
                RegisterExtHit(tbRect, () => OpenSubPanel(pt));
            }
            else if (tl.label == "聊天")
            {
                // 外部命中：聊天按钮 = 聚焦输入（不弹原生输入栏——用户反馈白框突兀）
                RegisterExtHit(tbRect, () => { _inputFocused = true; });
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
        float avatarSize = 36f + _fontScaleLevel * 2f;  // 头像尺寸（随档位略增）
        Rect logView = new Rect(px + 8f, logY, logViewW, logH);

        // 日志区背景（略深于面板）
        UiTextureFactory.DrawPixelRect(logView, new Color(0.05f, 0.04f, 0.09f, 0.5f));

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

        if (_logLines.Count == 0 && !waiting)
        {
            float emptyY = Mathf.Max(72f, (logH - 52f) * 0.46f);
            GUI.Label(new Rect(24f, emptyY, logViewW - 48f, 24f), "本座已候命", _emptyStateTitleStyle);
            GUI.Label(new Rect(24f, emptyY + 28f, logViewW - 48f, 22f), "输入消息，开始与符玄对话", _emptyStateHintStyle);
        }

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
                // 头像文字直接使用完整头像矩形居中，避免旧的手工偏移造成“我”字漂移。
                GUI.Label(avatarRect, "我", _userAvatarStyle);
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
            System.Action mascotAction = () =>
            {
                _mascotJumpStart = Time.time;
                if (_chat != null) _chat.SendMessage("*你伸出手指，轻轻戳了戳符玄的额头*", null);
            };
            if (!_externalRender && Event.current.type == EventType.MouseDown && Event.current.button == 0 && mascotRect.Contains(mp))
            {
                mascotAction();
                Event.current.Use();
            }
            // 外置窗口没有 Unity IMGUI 鼠标事件，必须把小人也注册进外置命中表。
            RegisterExtHit(mascotRect, mascotAction);
            // 地面小阴影（跳跃时缩小）
            float shadowScale = (jumpAge >= 0f && jumpAge < 0.45f) ? 0.5f : 1f;
            GUI.color = new Color(0f, 0f, 0f, 0.35f * shadowScale);
            GUI.DrawTexture(new Rect(mascotRect.x + 8f, logView.yMax - 6f, mw - 16f, 4f), _whiteTex);
            GUI.color = _panelTint;
            // 形象本体（表情激活时用表情帧，否则眨眼切换睁/闭眼）
            GUI.DrawTexture(mascotRect, GetActiveMascotTex());
            // 表情差分徽章（右上角：AI 回复【表情:xxx】时弹出对应符号，4 秒后消失）
            if (!string.IsNullOrEmpty(_mascotEmotion))
            {
                Texture2D emblem = GetEmblemTex(_mascotEmotion);
                if (emblem != null)
                {
                    float bs = EMBLEM_SIZE;
                    Rect badgeBg = new Rect(mascotRect.xMax - bs - 5f, mascotRect.y - 5f, bs, bs);
                    UiTextureFactory.DrawPixelRect(badgeBg, new Color(0f, 0f, 0f, 0.78f)); // 黑色圆角底
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

        // 提示必须在日志背景、滚动内容和 CRT 扫描线之后绘制，确保文字不会被覆盖。
        if (hoveredTool >= 0)
        {
            string tipText = _tools[hoveredTool].label;
            float tipW = Mathf.Max(58f, _toolTipStyle.CalcSize(new GUIContent(tipText)).x + 8f);
            Rect tipRect = new Rect(hoveredToolRect.x + (hoveredToolRect.width - tipW) / 2f,
                hoveredToolRect.yMax + 4f, tipW, 24f);
            UiTextureFactory.DrawPixelRect(tipRect, new Color(0.08f, 0.06f, 0.14f, 0.98f));
            UiTextureFactory.DrawPixelRect(new Rect(tipRect.x, tipRect.y, tipRect.width, 2f),
                new Color(0.70f, 0.54f, 0.98f, 0.95f));
            GUI.Label(tipRect, tipText, _toolTipStyle);
        }

        // 新增日志 → 自动滚到底
        if (_pendingAutoScroll)
        {
            _pendingAutoScroll = false;
            _logScroll.y = float.MaxValue;
        }

        // ═══════════════════════════════════════
        //  底部终端输入行 — [像素符玄] > [输入框] (→)
        //  ⚠️ 视觉始终绘制（外置模式画面保持一致）；交互（TextField/发送/聚焦）仅内嵌模式响应
        // ═══════════════════════════════════════
        Rect inputBgRect = default, fxRect = default, sendBtnRect = default;
        {
        float inputY = py + ph - inputBarHeight - 14f;
        float inputX = px + 16f;
        float inputW = pw - 32f;

        // 输入栏不再贴边铺满，只保留一条很淡的分隔线，让消息区和输入区有呼吸感。
        UiTextureFactory.DrawPixelRect(
            new Rect(inputX, inputY - 8f, inputW, 1f),
            new Color(0.45f, 0.38f, 0.66f, 0.24f));

        // 符玄头像（输入框内最左，高清原图）★多模态资源：Resources/PixelFuXuan.png
        float fxSize = 40f;
        fxRect = new Rect(inputX, inputY + (inputBarHeight - fxSize) / 2f, fxSize, fxSize);
        GUI.DrawTexture(fxRect, _pixelFxTex);

        float tfH = 44f + _fontScaleLevel * 4f;
        float tfY = inputY + (inputBarHeight - tfH) / 2f;

        // 输入框（透明背景，文字直接绘在输入条上）
        float sendBtnSize = 40f;
        float tfX = inputX + fxSize + 10f;
        float tfW = inputW - fxSize - 10f - sendBtnSize - 10f;
        inputBgRect = new Rect(tfX, tfY, tfW, tfH);

        // 外置窗口使用透明原生 EDIT 作为 IME 宿主接收真实键盘/中文输入；可见文字、光标和背景统一由 Unity 绘制。
        // 仍每帧同步逻辑矩形，防止面板尺寸/DPI/视图切换后输入命中状态漂移。
        if (_externalRender)
        {
            ExternalChatWindow.SetInputRect(
                Mathf.RoundToInt(inputBgRect.x + 8f), Mathf.RoundToInt(inputBgRect.y + 2f),
                Mathf.RoundToInt(inputBgRect.width - 14f), Mathf.RoundToInt(inputBgRect.height - 4f));
        }

        // 输入框背景：默认低对比度，悬停/聚焦时才显示轻微描边。
        GUI.DrawTexture(inputBgRect, _inputBgTex);
        bool inputVisualActive = inputBgRect.Contains(mp)
            || (_externalRender ? ExternalChatWindow.IsInputFocused : GUI.GetNameOfFocusedControl() == "rightPanelInput");
        if (inputVisualActive)
            GUI.DrawTexture(inputBgRect, _inputGlowTex);

        float inputHorizontalScroll = 0f;
        bool externalInputGroupOpen = false;
        if (!_externalRender)
        {
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
        }
        else
        {
            // 外置模式：透明原生 EDIT 只负责输入法桥接，Unity 负责绘制可见文本。
            // 外置模式：原生 EDIT 仅作为输入法/键盘桥接；已提交文字和正在组词的文字均由 Unity 绘制。
            // 这样原生控件可以保持无黑框，同时用户不会丢失中文输入法的组词反馈。
            string composition = ExternalChatWindow.GetInputComposition();
            string visualText = _inputText ?? string.Empty;
            int visualCaretIndex = Mathf.Clamp(ExternalChatWindow.GetInputCaretIndex(), 0, visualText.Length);
            if (ExternalChatWindow.IsInputFocused && !string.IsNullOrEmpty(composition))
            {
                visualText += composition;
                visualCaretIndex = visualText.Length;
            }

            inputHorizontalScroll = UpdateInputHorizontalScroll(inputBgRect, visualText, visualCaretIndex);

            // 只在输入框内部裁剪。此前直接 GUI.Label(inputBgRect, text) 虽然能显示
            // 短文本，但无法把超出右边的内容向左移动，也会让组合字/光标越界。
            GUI.BeginGroup(inputBgRect);
            externalInputGroupOpen = true;
            Rect shiftedTextRect = new Rect(-inputHorizontalScroll, 0f,
                inputBgRect.width + inputHorizontalScroll + 64f, inputBgRect.height);
            GUI.Label(shiftedTextRect, _inputText, _termInputStyle);

            if (ExternalChatWindow.IsInputFocused && !string.IsNullOrEmpty(composition))
            {
                Vector2 compositionSize = _termInputStyle.CalcSize(new GUIContent(composition));
                float compositionTextX = GetInputTextEndX(inputBgRect, _inputText)
                    - inputBgRect.x - inputHorizontalScroll;
                float compositionRectX = compositionTextX - _termInputStyle.padding.left;
                GUI.Label(new Rect(compositionRectX, 0f, inputBgRect.width - compositionRectX + 64f, inputBgRect.height),
                    composition, _termInputStyle);
                float compositionUnderlineY = Mathf.Clamp(
                    (_termInputStyle.padding.top + compositionSize.y - 2f),
                    4f, inputBgRect.height - 4f);
                float compositionWidth = Mathf.Max(18f,
                    compositionSize.x - _termInputStyle.padding.left - _termInputStyle.padding.right);
                UiTextureFactory.DrawPixelRect(
                    new Rect(compositionTextX, compositionUnderlineY,
                        Mathf.Min(140f, compositionWidth), 2f),
                    new Color(0.86f, 0.76f, 1f, 0.9f));
            }
        }

        // 原生 EDIT 为避免黑框已移到屏外，不能依赖系统插入光标；在 IMGUI/RT 中绘制稳定的紫白光标，
        // 点击输入栏后即使文本为空也能明确看到“已聚焦”。内嵌模式也统一绘制，避免主题样式吞掉系统光标。
        bool inputCaretFocused = _externalRender
            ? ExternalChatWindow.IsInputFocused
            : GUI.GetNameOfFocusedControl() == "rightPanelInput";
        string caretText = _inputText ?? string.Empty;
        int caretIndex = caretText.Length;
        if (_externalRender)
        {
            string composition = ExternalChatWindow.GetInputComposition();
            if (!string.IsNullOrEmpty(composition))
            {
                // 组词阶段的光标落在组词末尾；普通编辑则跟随原生 EDIT 插入点。
                caretIndex = caretText.Length + composition.Length;
                caretText += composition;
            }
            else
            {
                caretIndex = Mathf.Clamp(ExternalChatWindow.GetInputCaretIndex(), 0, caretText.Length);
            }
        }
        if (_inputCaretBlinkStart < 0f
            || inputCaretFocused != _inputCaretFocusSnapshot
            || !string.Equals(caretText, _inputCaretTextSnapshot, StringComparison.Ordinal))
        {
            _inputCaretBlinkStart = Time.unscaledTime;
            _inputCaretTextSnapshot = caretText;
            _inputCaretFocusSnapshot = inputCaretFocused;
        }
        float caretBlinkPhase = inputCaretFocused
            ? Mathf.Repeat(Time.unscaledTime - _inputCaretBlinkStart, INPUT_CARET_BLINK_PERIOD)
            : 1f;
        bool inputCaretVisible = inputCaretFocused && caretBlinkPhase < INPUT_CARET_BLINK_PERIOD * 0.5f;
        float caretH = Mathf.Min(26f, inputBgRect.height - 12f);
        float caretY = inputBgRect.y + (inputBgRect.height - caretH) * 0.5f;
        float caretAbsoluteX = Mathf.Clamp(GetInputTextCaretX(inputBgRect, caretText, caretIndex) + 1f,
            inputBgRect.x + _termInputStyle.padding.left,
            inputBgRect.xMax - _termInputStyle.padding.right - 3f);
        // 外置模式的光标在输入框局部 Group 内绘制；传给 Win32 的候选栏锚点仍使用
        // 未平移的绝对逻辑坐标，否则中文候选框会跟着视口偏移到错误位置。
        float caretX = _externalRender
            ? caretAbsoluteX - inputBgRect.x - inputHorizontalScroll
            : caretAbsoluteX;
        if (_externalRender)
        {
            ExternalChatWindow.SetInputCaretRect(
                Mathf.RoundToInt(caretAbsoluteX), Mathf.RoundToInt(caretY), 2, Mathf.RoundToInt(caretH));
        }
        if (inputCaretVisible)
        {
            // 使用 Unity 的实际光标测量结果，避免 CalcSize 后再次叠加 padding 造成“字与光标之间多一个空格”。
            UiTextureFactory.DrawPixelRect(
                new Rect(caretX, _externalRender ? caretY - inputBgRect.y : caretY, 2f, caretH),
                new Color(0.86f, 0.76f, 1f, 0.96f));
        }
        if (externalInputGroupOpen)
            GUI.EndGroup();

        // ——— 发送按钮（太极图，符玄道法风，hover 紫色光晕） ———
        sendBtnRect = new Rect(tfX + tfW + 10f, inputY + (inputBarHeight - sendBtnSize) / 2f, sendBtnSize, sendBtnSize);
        bool sendHover = sendBtnRect.Contains(mp);
        // 单一圆形按钮和箭头，避免太极图与输入框描边抢视觉焦点。
        GUIStyle sendVisualStyle = sendHover ? _sendBtnHoverStyle : _sendBtnStyle;
        if (_externalRender)
            GUI.Label(sendBtnRect, waiting ? "■" : "→", sendVisualStyle);
        if (!_externalRender && GUI.Button(sendBtnRect,
            new GUIContent(waiting ? "■" : "→", waiting ? "停止本次回复" : "发送 (Enter)"), _sendBtnStyle))
        {
            if (waiting)
            {
                _chat?.CancelCurrentRequest();
                return;
            }
            string sendMsg = _inputText.Trim();
            if (sendMsg.Length > 0)
            {
                _inputText = "";
                if (_chat != null)
                    _chat.SendMessage(sendMsg, null);
                _inputFocused = true; // 发送后保持聚焦
            }
        }
        // 外部命中：发送按钮（文本取 _inputText，Phase A3 输入框对接后生效）
        RegisterExtHit(sendBtnRect, () =>
        {
            if (_chat != null && _chat.IsWaiting)
            {
                _chat.CancelCurrentRequest();
                return;
            }
            string sendMsg = _inputText.Trim();
            if (sendMsg.Length > 0)
            {
                _inputText = "";
                if (_chat != null)
                    _chat.SendMessage(sendMsg, null);
            }
        });

        // 空输入框提示
        bool showPlaceholder = _externalRender
            ? !ExternalChatWindow.IsInputFocused
            : GUI.GetNameOfFocusedControl() != "rightPanelInput";
        if (string.IsNullOrEmpty(_inputText) && string.IsNullOrEmpty(ExternalChatWindow.GetInputComposition()) && showPlaceholder)
        {
            GUI.Label(inputBgRect, "向符玄下达指令…", _termPlaceholderStyle);
        }
        // 外部命中：点击输入框 → 透明原生 EDIT 覆盖在输入框位置聚焦输入（视觉融合，无白框）
        RegisterExtHit(inputBgRect, () =>
        {
            // ★ 输入聚焦状态机第 1 步（codex 2026-08-17 建议 5.2）：Unity 侧命中输入区
            Debug.Log("[ExternalChat] input hit");
            ExternalChatWindow.SetInputRect(
                Mathf.RoundToInt(inputBgRect.x + 8f), Mathf.RoundToInt(inputBgRect.y + 2f),
                Mathf.RoundToInt(inputBgRect.width - 14f), Mathf.RoundToInt(inputBgRect.height - 4f));
            SetExternalInputCaretFromClick(inputBgRect, _externalActionClickPosition);
            ExternalChatWindow.FocusInput();
        });

        // 聚焦请求
        if (!_externalRender && _inputFocused)
        {
            _inputFocused = false;
            GUI.FocusControl("rightPanelInput");
        }
        } // ⚠️ 输入栏块结束

        // ——— 右下角拉伸手柄（可调窗口大小，QQ 式；用整体面板尺寸，含左侧会话栏） ———
        float handleSize = 20f;
        Rect resizeRect = new Rect(_panelRect.xMax - handleSize, _panelRect.yMax - handleSize, handleSize, handleSize);
        bool resizeHover = resizeRect.Contains(mp);
        // 手柄视觉：三条斜线
        float hc = resizeHover ? 1f : 0.6f;
        if (!_externalRender)
        {
        UiTextureFactory.DrawPixelRect(new Rect(_panelRect.xMax - 16f, _panelRect.yMax - 6f, 9f, 1f), new Color(0.66f, 0.50f, 0.95f, hc));
        UiTextureFactory.DrawPixelRect(new Rect(_panelRect.xMax - 21f, _panelRect.yMax - 11f, 9f, 1f), new Color(0.66f, 0.50f, 0.95f, hc * 0.8f));
        UiTextureFactory.DrawPixelRect(new Rect(_panelRect.xMax - 26f, _panelRect.yMax - 16f, 9f, 1f), new Color(0.66f, 0.50f, 0.95f, hc * 0.6f));

        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && resizeRect.Contains(mp))
        {
            _isResizing = true;
            Event.current.Use();
        }
        if (_isResizing && Event.current.type == EventType.MouseDrag)
        {
            float newW = Mathf.Clamp(Event.current.mousePosition.x - _panelRect.x, MIN_PANEL_W, MAX_PANEL_W);
            float newH = Mathf.Clamp(Event.current.mousePosition.y - _panelRect.y, MIN_PANEL_H, MAX_PANEL_H);
            _panelRect.width = newW;
            _panelRect.height = newH;
            panelWidth = newW;
            panelHeight = newH;
            Event.current.Use();
        }
        } // ⚠️ 关闭 !_externalRender 缩放块

        // ——— 窗口内点击 → 防穿透 ———
        // ★ 审批弹窗打开时跳过：弹窗按钮的 MouseDown 若在此被 Use() 吞掉，
        //   后续 DrawApprovalDialog 的 GUI.Button 永远收不到点击（无法同意/拒绝）
        if (!_externalRender && !_approvalDialogOpen && Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if (_panelRect.Contains(mp))
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

    private void DrawSessionListView(float px, float py, float pw, float ph, Vector2 mp)
    {
        RefreshSessionList();
        RefreshTime();

        float titleH = 76f;
        Rect titleBarRect = new Rect(px + 2f, py + 2f, pw - 4f, titleH);
        GUI.DrawTexture(titleBarRect, _titleBarPixelTex);
        UiTextureFactory.DrawPixelRect(new Rect(px + 2f, py + 2f + titleH, pw - 4f, 1f), new Color(0.58f, 0.42f, 0.88f, 0.6f));

        // —— 标题：符玄头像 + 名称 + 状态 ——
        float headSize = 48f;
        Rect headRect = new Rect(px + 12f, py + (titleH - headSize) / 2f, headSize, headSize);
        UiTextureFactory.DrawPixelRect(new Rect(headRect.x - 2f, headRect.y - 2f, headRect.width + 4f, headRect.height + 4f), new Color(0f, 0f, 0f, 0.65f));
        GUI.DrawTexture(headRect, _pixelFxTex);
        GUI.Label(new Rect(headRect.xMax + 12f, py + 9f, pw - 200f, 28f), "符玄·太卜司", _termTitleStyle);
        bool waiting = _chat != null && _chat.IsWaiting;
        GUI.Label(new Rect(headRect.xMax + 12f, py + 42f, pw - 200f, 22f), waiting ? "● 思考中…" : "● 就绪", _termStatusStyle);

        // —— 时间 + ✕ 关闭 ——
        // 给同一标题行的最小化按钮预留位置，避免时间文字与按钮叠加。
        GUI.Label(new Rect(px + pw - 180f, py + 20f, 60f, 22f), _timeDisplay, _termTimeStyle);
        float closeSize = 36f;
        Rect closeRect = new Rect(px + pw - closeSize - 14f, py + (titleH - closeSize) / 2f, closeSize, closeSize);
        if (closeRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(closeRect, new Color(0.80f, 0.25f, 0.25f, 0.35f));
        if (!_externalRender && GUI.Button(closeRect, "✕", _closeBtnStyle)) { Close(); }
        if (_externalRender)
        {
            GUI.Label(closeRect, "✕", _closeBtnStyle);
            Rect minRect = new Rect(closeRect.x - closeRect.width - 6f, closeRect.y, closeRect.width, closeRect.height);
            UiTextureFactory.DrawPixelRect(minRect, new Color(0.30f, 0.22f, 0.45f, 0.42f));
            UiTextureFactory.DrawPixelRect(new Rect(minRect.x + 8f, minRect.center.y, minRect.width - 16f, 2f),
                new Color(0.78f, 0.66f, 0.98f, 0.9f));
            RegisterExtHit(minRect, ExternalChatWindow.Minimize);
            RegisterExtHit(closeRect, ExternalChatWindow.RequestClose);
        }
        else
        {
            RegisterExtHit(closeRect, Close); // 内嵌面板关闭
        }

        // —— ⧉ 独立窗口切换（QQ 式：面板可被其他窗口遮挡；会话列表视图也有入口） ——
        Rect sExtBtnRect = new Rect(px + pw - closeSize - 64f, py + (titleH - 34f) / 2f, 40f, 34f);
        if (!_externalRender)
        {
        // ★ 常态背景方块（细线符号在深底上不可见，加底衬突出按钮区域）
        UiTextureFactory.DrawPixelRect(sExtBtnRect, _externalMode
            ? new Color(0.55f, 0.40f, 0.85f, 0.45f)   // 外置激活中 → 亮紫
            : new Color(0.30f, 0.22f, 0.45f, 0.30f)); // 未激活 → 暗紫
        if (sExtBtnRect.Contains(mp))
            UiTextureFactory.DrawPixelRect(sExtBtnRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        // ★ 图标：程序绘制两窗方块（不用 ⧉ 字形——等宽字体无字形渲染空白）
        if (_extWindowIconTex != null)
        {
            Rect sIconR = new Rect(sExtBtnRect.center.x - 11f, sExtBtnRect.center.y - 10f, 22f, 20f);
            GUI.DrawTexture(sIconR, _extWindowIconTex);
            if (!_externalRender && Event.current.type == EventType.MouseDown && Event.current.button == 0 && sExtBtnRect.Contains(mp))
            {
                Event.current.Use();
                ToggleExternalMode();
            }
        }
        else
        {
            if (!_externalRender && GUI.Button(sExtBtnRect, "独立", _termToolBtnStyle))
                ToggleExternalMode();
        }
        RegisterExtHit(sExtBtnRect, ToggleExternalMode); // 外部命中：⧉ 退出/进入独立窗口
        }

        // —— 标题栏拖拽（排除 ✕ / ⧉） ——
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0
            && titleBarRect.Contains(mp) && !closeRect.Contains(mp) && !sExtBtnRect.Contains(mp))
        {
            _isDragging = true;
            _dragOffset = mp - new Vector2(px, py);
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseUp)
        {
            _isDragging = false;   // ★ 会话列表视图也要复位，否则窗口一直吸在鼠标上
            _isResizing = false;
        }

        // —— 搜索/新建区（QQ 式：搜索胶囊 + 新建按钮占位） ——
        float searchY = py + titleH + 12f;
        float searchH = 48f;
        Rect searchRect = new Rect(px + 12f, searchY, pw - 76f, searchH);
        GUI.DrawTexture(searchRect, _inputBgTex);
        GUI.Label(new Rect(searchRect.x + 20f, searchRect.y + 10f, searchRect.width - 28f, 28f), "🔍 搜索会话", _termPlaceholderStyle);
        Rect newBtnRect = new Rect(px + pw - 60f, searchY, 48f, searchH);
        if (GUI.Button(newBtnRect, "＋", _termToolBtnStyle))
            Debug.Log("[RightPanel] 新建会话（多角色扩展预留）");
        RegisterExtHit(newBtnRect, () => Debug.Log("[RightPanel] 外部命中：新建会话（多角色扩展预留）"));

        // —— 会话列表（滚动，双击进入聊天） ——
        float listY = searchY + searchH + 12f;
        float listH = ph - (listY - py) - 80f;
        if (listH < 40f) listH = 40f;
        float itemH = 96f;
        Rect listView = new Rect(px + 6f, listY, pw - 12f, listH);
        UiTextureFactory.DrawPixelRect(listView, new Color(0.05f, 0.04f, 0.09f, 0.35f));
        float contentH = Mathf.Max(_sessions.Count * itemH + 8f, listH);
        Rect contentRect = new Rect(0f, 0f, listView.width - 8f, contentH);
        _sessionScroll = GUI.BeginScrollView(listView, _sessionScroll, contentRect, false, false, _invisibleScrollbar, _invisibleScrollbar);
        Vector2 localMp = new Vector2(mp.x - listView.x + _sessionScroll.x, mp.y - listView.y + _sessionScroll.y);

        for (int i = 0; i < _sessions.Count; i++)
        {
            var s = _sessions[i];
            Rect itemRect = new Rect(2f, 8f + i * itemH, contentRect.width, itemH - 8f);
            if (itemRect.Contains(localMp))
                UiTextureFactory.DrawPixelRect(itemRect, new Color(0.50f, 0.35f, 0.80f, 0.18f));
            UiTextureFactory.DrawPixelRect(new Rect(itemRect.x + 12f, itemRect.yMax - 1f, itemRect.width - 24f, 1f), new Color(0.45f, 0.35f, 0.65f, 0.18f));
            // 头像 60px 圆角方块
            float av = 60f;
            Rect avRect = new Rect(itemRect.x + 12f, itemRect.y + (itemH - 8f - av) / 2f, av, av);
            GUI.DrawTexture(avRect, s.avatar ?? _pixelFxTex);
            // 名称（粗金）+ 时间（右上）
            GUI.Label(new Rect(avRect.xMax + 14f, itemRect.y + 10f, contentRect.width - av - 130f, 28f), s.name, _termTitleStyle);
            GUI.Label(new Rect(itemRect.x + itemRect.width - 96f, itemRect.y + 16f, 84f, 22f), s.lastTime, _termTimeStyle);
            // 最后消息（灰，单行截断）
            string msg = s.lastMsg ?? "";
            if (msg.Length > 22) msg = msg.Substring(0, 22) + "…";
            GUI.Label(new Rect(avRect.xMax + 14f, itemRect.y + 46f, contentRect.width - av - 34f, 26f), msg, _termLogDimStyle);
            // 双击进入聊天（QQ 式交互）
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && itemRect.Contains(localMp) && Event.current.clickCount == 2)
            {
                Event.current.Use();
                EnterChat(i);
            }
            // 外部命中：会话项双击进聊天（矩形已含滚动偏移，命中表用同一坐标系）
            int idx = i;
            Rect extItemRect = new Rect(itemRect.x + listView.x - _sessionScroll.x, itemRect.y + listView.y - _sessionScroll.y, itemRect.width, itemRect.height);
            RegisterExtHit(extItemRect, () => EnterChat(idx), true);
        }
        GUI.EndScrollView();

        // —— 底部工具入口（设置/便签/报告/消耗 → 对话框内部子面板视图，QQ 底部工具栏位置） ——
        float toolY = py + ph - 76f;
        UiTextureFactory.DrawPixelRect(new Rect(px + 2f, toolY - 8f, pw - 4f, 1f), new Color(0.58f, 0.42f, 0.88f, 0.4f));
        float toolW = (pw - 48f) / 5f;
        var toolDefs = new (string label, BallPanel.PanelType type)[]
        {
            ("设置", BallPanel.PanelType.Settings),
            ("便签", BallPanel.PanelType.Reminders),
            ("报告", BallPanel.PanelType.Report),
            ("消耗", BallPanel.PanelType.Usage),
            ("忆境", BallPanel.PanelType.Memory)
        };
        int hoveredSessionTool = -1;
        Rect hoveredSessionToolRect = default;
        for (int i = 0; i < toolDefs.Length; i++)
        {
            Rect btnRect = new Rect(px + 12f + i * toolW, toolY + 12f, toolW - 8f, 50f);
            bool btnHover = btnRect.Contains(mp);
            if (btnHover)
            {
                hoveredSessionTool = i;
                hoveredSessionToolRect = btnRect;
            }
            if (btnHover)
                UiTextureFactory.DrawPixelRect(btnRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
            if (GUI.Button(btnRect, toolDefs[i].label, btnHover ? _termToolBtnHoverStyle : _termToolBtnStyle))
                OpenSubPanel(toolDefs[i].type);
            var type = toolDefs[i].type;
            RegisterExtHit(btnRect, () => OpenSubPanel(type)); // 外部命中：底部工具入口
        }
        if (hoveredSessionTool >= 0)
        {
            string tipText = toolDefs[hoveredSessionTool].label;
            float tipW = Mathf.Max(72f, _toolTipStyle.CalcSize(new GUIContent(tipText)).x + 8f);
            Rect tipRect = new Rect(hoveredSessionToolRect.x + (hoveredSessionToolRect.width - tipW) / 2f,
                hoveredSessionToolRect.y - 28f, tipW, 24f);
            UiTextureFactory.DrawPixelRect(tipRect, new Color(0.08f, 0.06f, 0.14f, 0.96f));
            UiTextureFactory.DrawPixelRect(new Rect(tipRect.x, tipRect.yMax - 2f, tipRect.width, 2f),
                new Color(0.70f, 0.54f, 0.98f, 0.9f));
            GUI.Label(tipRect, tipText, _toolTipStyle);
        }

        // 右键关闭（快捷收面板）
        if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
        {
            Close();
            Event.current.Use();
        }
    }

    private void DrawSessionSidebar(float px, float py, float w, float ph, Vector2 mp)
    {
        RefreshSessionList();
        GUI.DrawTexture(new Rect(px, py, w, ph), _inputBarPixelTex);
        UiTextureFactory.DrawPixelRect(new Rect(px + w - 1f, py, 1f, ph), new Color(0.58f, 0.42f, 0.88f, 0.5f));
        // 栏标题
        GUI.Label(new Rect(px + 20f, py + 16f, w - 40f, 30f), "会话", _termTitleStyle);
        UiTextureFactory.DrawPixelRect(new Rect(px + 12f, py + 58f, w - 24f, 1f), new Color(0.45f, 0.35f, 0.65f, 0.25f));
        // 会话条目（当前单角色，多角色扩展后此处渲染多个）
        float itemH = 84f;
        float listY = py + 66f;
        for (int i = 0; i < _sessions.Count; i++)
        {
            var s = _sessions[i];
            bool active = i == _activeSession;
            Rect itemRect = new Rect(px + 6f, listY + i * itemH, w - 12f, itemH - 8f);
            if (active)
                UiTextureFactory.DrawPixelRect(itemRect, new Color(0.55f, 0.40f, 0.85f, 0.30f));       // 选中高亮
            else if (itemRect.Contains(mp))
                UiTextureFactory.DrawPixelRect(itemRect, new Color(0.50f, 0.35f, 0.80f, 0.15f));       // 悬停
            float av = 44f;
            Rect avRect = new Rect(itemRect.x + 12f, itemRect.y + (itemH - 8f - av) / 2f, av, av);
            GUI.DrawTexture(avRect, s.avatar ?? _pixelFxTex);
            GUI.Label(new Rect(avRect.xMax + 12f, itemRect.y + 12f, w - av - 40f, 26f), s.name,
                active ? _termToolBtnHoverStyle : _termTitleStyle);
            string msg = s.lastMsg ?? "";
            if (msg.Length > 10) msg = msg.Substring(0, 10) + "…";
            GUI.Label(new Rect(avRect.xMax + 12f, itemRect.y + 46f, w - av - 36f, 22f), msg, _termLogDimStyle);
            // 单击切换会话（多角色切换）
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && itemRect.Contains(mp))
            {
                _activeSession = i;
                _pendingAutoScroll = true;
                Event.current.Use();
            }
        }
    }

    private void RefreshSessionList()
    {
        if (!_sessionDirty) return;
        _sessionDirty = false;
        _sessions.Clear();
        var s = new ChatSession
        {
            name = "符玄",
            avatar = _pixelFxTex,
            lastMsg = "—",
            lastTime = System.DateTime.Now.ToString("HH:mm")
        };
        if (_chat != null && _chat.HistoryCount > 0)
        {
            var hist = _chat.History;
            for (int i = hist.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(hist[i].content)) continue;
                s.lastMsg = ChatManager.CleanDisplayText(hist[i].content);
                if (s.lastMsg.Length > 24) s.lastMsg = s.lastMsg.Substring(0, 24) + "…";
                break;
            }
        }
        _sessions.Add(s);
    }
}
