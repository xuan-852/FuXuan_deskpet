using UnityEngine;

/// <summary>
/// 聊天气泡 — 在模型上方显示浮动的 AI 对话气泡
/// OnGUI 绘制，圆角 + 小尾巴 + 淡入淡出动画
/// 参考 Live2DPet (Electron/HTML) 设计风格
///
/// ⚠️ 优先级系统（防抢话）：
///   High   = AI 主动回复（不可被低优先级覆盖）
///   Normal = 提醒、交互回应
///   Low    = 闲话、定时问候
/// 高优先级消息显示期间，低优先级消息会被忽略；
/// 同优先级消息总是可以覆盖当前消息。
/// </summary>
public class ChatBubble : MonoBehaviour
{
    /// <summary>消息优先级（数字越大越优先）</summary>
    public enum MsgPriority
    {
        Low = 0,     // 闲话、定时问候
        Normal = 1,  // 提醒、交互回应
        High = 2     // AI 回复（不可被低优覆盖）
    }

    [Header("显示设置")]
    public float displayDuration = 5f;
    public float fadeInDuration = 0.3f;
    public float fadeOutDuration = 0.5f;

    [Header("尺寸")]
    public float maxWidth = 320f;
    public float minWidth = 160f;
    public float cornerRadius = 12f;
    public float tailHeight = 14f;
    public float tailWidth = 20f;
    public float topOffset = 70f;     // 模型头顶到气泡的距离（20 和 120 的中间值）
    public float shadowOffset = 3f;

    [Header("配色 — 符玄紫灰主题")]
    public Color bgColor = new Color(0.16f, 0.13f, 0.20f);         // 内背景
    public Color borderColor = new Color(0.10f, 0.08f, 0.14f);     // 外框
    public Color accentColor = new Color(0.72f, 0.48f, 0.84f);     // 紫色装饰线
    public Color textColor = new Color(0.95f, 0.92f, 0.97f);       // 浅紫白文字
    public Color shadowColor = new Color(0f, 0f, 0f, 0.25f);

    [Header("古风紫纹装饰")]
    public Color ornamentColor = new Color(0.72f, 0.48f, 0.84f, 0.60f); // 角饰色 ★ 提亮
    public Color starColor = new Color(0.85f, 0.65f, 0.95f, 1.0f);      // 星点色 ★★★ 满透明度
    public float ornamentSize = 28f;                                     // 角饰大小 ★ 放大

    // ============ 优先级系统 ============
    /// <summary>当前显示消息的优先级</summary>
    private MsgPriority _currentPriority = MsgPriority.Low;
    /// <summary>外部可查：当前是否在高优先级的 AI 回复中（供其他模块判断）</summary>
    public bool IsShowingHighPriority => _hasMessage && _currentPriority >= MsgPriority.High;

    private DesktopPet _pet;
    private Live2DRenderer _renderer;
    private string _currentText = "";
    private float _messageStartTime = -1f;
    private bool _hasMessage = false;

    // === 动画状态机 ===
    private enum BubbleState { Hidden, FadingIn, Showing, FadingOut }
    private BubbleState _state = BubbleState.Hidden;
    private float _animProgress = 0f;

    // === 缓存的气泡尺寸 ===
    private float _bubbleWidth;
    private float _bubbleHeight;
    private float _textWidth;
    private float _textHeight;

    // === 纹理 / 样式 ===
    private Texture2D _bgTex;         // 圆角矩形（内背景）
    private Texture2D _borderTex;     // 圆角矩形（外框）
    private Texture2D _shadowTex;     // 圆角矩形（阴影）
    private Texture2D _tailTex;       // 三角形小尾巴
    private Texture2D _accentTex;     // 1x1 紫色装饰条
    private Texture2D _ornamentTL;    // 左上角云纹
    private Texture2D _ornamentBR;    // 右下角云纹
    private Texture2D _starGlowTex;   // 单颗星发光点
    private GUIStyle _textStyle;

    // 闪烁星点数据：[x归一化, y归一化, 大小px, 闪烁速度, 相位偏移]
    private static readonly float[][] _starData = new float[][] {
        new float[]{0.12f, 0.18f, 8f, 2.5f, 0.0f},
        new float[]{0.85f, 0.12f, 7f, 1.8f, 1.2f},
        new float[]{0.20f, 0.80f, 9f, 3.0f, 0.8f},
        new float[]{0.75f, 0.78f, 6f, 2.0f, 2.1f},
        new float[]{0.50f, 0.22f, 5f, 1.5f, 3.5f},
        new float[]{0.08f, 0.50f, 8f, 2.8f, 0.3f},
        new float[]{0.95f, 0.55f, 6f, 2.2f, 1.8f},
        new float[]{0.32f, 0.50f, 5f, 3.2f, 2.7f},
        new float[]{0.68f, 0.35f, 7f, 1.2f, 4.0f},
        new float[]{0.50f, 0.65f, 6f, 2.6f, 0.9f},
        new float[]{0.40f, 0.08f, 5f, 3.5f, 1.5f},
        new float[]{0.62f, 0.88f, 6f, 1.9f, 3.1f},
    };
    private bool _needsRebuild = true;

    void Start()
    {
        _pet = GetComponent<DesktopPet>();
        if (_pet == null) _pet = FindObjectOfType<DesktopPet>();
        _renderer = GetComponent<Live2DRenderer>();
        if (_renderer == null) _renderer = FindObjectOfType<Live2DRenderer>();
    }

    // ============================================================
    //  公开接口
    // ============================================================

    /// <summary>显示一条消息（优先级 Normal）。如果当前有高优先级消息则忽略。</summary>
    public void ShowMessage(string text, float duration = 5f)
    {
        ShowMessage(text, duration, MsgPriority.Normal);
    }

    /// <summary>显示一条消息（指定优先级）。高优消息不会被低优覆盖。</summary>
    public void ShowMessage(string text, float duration, MsgPriority priority)
    {
        // 低优先级消息不能覆盖高优先级
        if (_hasMessage && priority < _currentPriority)
            return;

        _currentText = text;
        displayDuration = duration;
        _messageStartTime = Time.time;
        _hasMessage = true;
        _state = BubbleState.FadingIn;
        _animProgress = 0f;
        _currentPriority = priority;
        _needsRebuild = true;
    }

    /// <summary>仅更新文本和时长（不重置淡入动画）— 用于逐句切换</summary>
    public void UpdateText(string text, float duration = 0f)
    {
        _currentText = text;
        if (duration > 0f)
        {
            // 延长剩余显示时间
            _messageStartTime = Time.time;
            displayDuration = duration;
        }
        _needsRebuild = true;
    }

    /// <summary>延长当前消息的显示时间（用于长回复逐句播放）</summary>
    public void ExtendDuration(float extraSeconds)
    {
        if (_hasMessage)
        {
            _messageStartTime = Time.time;
            displayDuration = extraSeconds;
        }
    }

    /// <summary>立即隐藏气泡</summary>
    public void Hide()
    {
        if (_state == BubbleState.Hidden) return;
        _state = BubbleState.FadingOut;
        _animProgress = 0f;
    }

    /// <summary>气泡是否正在显示（含淡入/淡出动画过程中）</summary>
    public bool IsShowing
    {
        get { return _hasMessage && _state != BubbleState.Hidden; }
    }

    // ============================================================
    //  Update — 驱动动画状态机
    // ============================================================

    void Update()
    {
        if (!_hasMessage) return;

        switch (_state)
        {
            case BubbleState.FadingIn:
                _animProgress += Time.deltaTime / fadeInDuration;
                if (_animProgress >= 1f) { _animProgress = 1f; _state = BubbleState.Showing; }
                break;

            case BubbleState.Showing:
                if (Time.time - _messageStartTime > displayDuration) Hide();
                break;

            case BubbleState.FadingOut:
                _animProgress += Time.deltaTime / fadeOutDuration;
                if (_animProgress >= 1f)
                {
                    _state = BubbleState.Hidden;
                    _hasMessage = false;
                    _currentText = "";
                }
                break;
        }
    }

    // ============================================================
    //  OnGUI — 绘制气泡
    // ============================================================

    void OnGUI()
    {
        if (!_hasMessage || _pet == null || _state == BubbleState.Hidden) return;

        // ---- 计算动画值 ----
        float alpha = 1f;
        float scale = 1f;
        switch (_state)
        {
            case BubbleState.FadingIn:
                alpha = _animProgress;
                scale = Mathf.Lerp(0.92f, 1f, _animProgress);
                break;
            case BubbleState.FadingOut:
                alpha = 1f - _animProgress;
                scale = Mathf.Lerp(1f, 0.92f, _animProgress);
                break;
        }

        if (_needsRebuild) RebuildTextures();
        if (_bgTex == null) return;

        // ---- 定位 ----
        float centerX = _pet.petX + _pet.petWidth / 2f;
        // 气泡跟随模型视觉位置（考虑垂直偏移）
        float visualOffset = (_renderer != null) ? _renderer.verticalOffset : 0f;
        float bubbleBottom = Mathf.Max(_pet.petY + visualOffset - topOffset, 10f);

        float bx = centerX - _bubbleWidth / 2f;
        float by = bubbleBottom - _bubbleHeight - tailHeight;
        bx = Mathf.Clamp(bx, 5f, Screen.width - _bubbleWidth - 5f);
        by = Mathf.Clamp(by, 5f, Screen.height - _bubbleHeight - tailHeight - 5f);

        // 气泡中心 — 用于缩放
        float bubbleCenterX = bx + _bubbleWidth / 2f;
        float cx = bubbleCenterX;
        float cy = by + _bubbleHeight / 2f;

        // 尾巴水平偏移量：当气泡被屏幕边缘卡住时，尾巴独立指向角色头部
        // （角色 centerX 与气泡实际中心 bubbleCenterX 的差值，限制在气泡宽度内）
        float tailOffsetX = Mathf.Clamp(centerX - bubbleCenterX, -_bubbleWidth / 2f + tailWidth + 4f, _bubbleWidth / 2f - tailWidth - 4f);

        float sw = _bubbleWidth * scale;
        float sh = _bubbleHeight * scale;
        Rect bgRect = new Rect(cx - sw / 2f, cy - sh / 2f, sw, sh);

        Color orig = GUI.color;

        // ——— 阴影 ———
        GUI.color = new Color(0f, 0f, 0f, 0.22f * alpha);
        GUI.DrawTexture(new Rect(bgRect.x + shadowOffset, bgRect.y + shadowOffset, bgRect.width, bgRect.height), _shadowTex);

        // ——— 外框 ———
        GUI.color = new Color(borderColor.r, borderColor.g, borderColor.b, borderColor.a * alpha);
        GUI.DrawTexture(bgRect, _borderTex);

        // ——— 内背景 ———
        Rect innerRect = new Rect(bgRect.x + 1, bgRect.y + 1, bgRect.width - 2, bgRect.height - 2);
        GUI.color = new Color(bgColor.r, bgColor.g, bgColor.b, bgColor.a * alpha);
        GUI.DrawTexture(innerRect, _bgTex);

        // ——— 紫色装饰线 ———
        GUI.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.75f * alpha);
        GUI.DrawTexture(new Rect(bgRect.x + 5, bgRect.y + 2, bgRect.width - 10, 3f), _accentTex);

        // ——— 左上角云纹 ———
        if (_ornamentTL != null)
        {
            GUI.color = new Color(ornamentColor.r, ornamentColor.g, ornamentColor.b, ornamentColor.a * alpha);
            GUI.DrawTexture(new Rect(bgRect.x + 3, bgRect.y + 5, ornamentSize, ornamentSize), _ornamentTL);
        }

        // ——— 右下角云纹（水平翻转 = 镜像） ———
        if (_ornamentBR != null)
        {
            GUI.color = new Color(ornamentColor.r, ornamentColor.g, ornamentColor.b, ornamentColor.a * alpha);
            GUI.DrawTexture(new Rect(bgRect.x + bgRect.width - 3 - ornamentSize, bgRect.y + bgRect.height - 3 - ornamentSize, ornamentSize, ornamentSize), _ornamentBR);
        }

        // ——— 闪烁星点（每颗独立呼吸动画） ———
        if (_starGlowTex != null)
        {
            foreach (var s in _starData)
            {
                // 强烈脉冲闪烁：0.0 → 1.0 → 0.0，每颗星独立节奏
                float sinVal = Mathf.Sin(Time.time * s[3] + s[4]);
                float pulse = sinVal * sinVal; // 平方让峰值更尖锐，谷值归零
                float twinkle = Mathf.Lerp(0.05f, 1.0f, pulse);
                float starA = starColor.a * alpha * twinkle;
                if (starA < 0.02f) continue;
                float starSz = s[2];
                float px = bgRect.x + s[0] * bgRect.width - starSz / 2f;
                float py = bgRect.y + s[1] * bgRect.height - starSz / 2f;
                GUI.color = new Color(starColor.r, starColor.g, starColor.b, starA);
                GUI.DrawTexture(new Rect(px, py, starSz, starSz), _starGlowTex);
            }
        }

        // ——— 小尾巴（气泡底部，水平跟随角色头部指向） ———
        float tailX = (bx + _bubbleWidth / 2f) + tailOffsetX - tailWidth / 2f;
        float tailY = bgRect.y + bgRect.height;
        GUI.color = new Color(bgColor.r, bgColor.g, bgColor.b, bgColor.a * alpha);
        GUI.DrawTexture(new Rect(tailX, tailY, tailWidth, tailHeight), _tailTex);

        // ——— 文字（居中） ———
        GUI.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
        Rect textRect = new Rect(bgRect.x + 14, bgRect.y + 14, _textWidth, _textHeight);
        GUI.Label(textRect, _currentText, _textStyle);

        GUI.color = orig;
    }

    // ============================================================
    //  纹理生成
    // ============================================================

    private void RebuildTextures()
    {
        _needsRebuild = false;

        // 1) 初始化样式 & 计算尺寸
        _textStyle = new GUIStyle
        {
            normal = { textColor = textColor },
            fontSize = 16,
            wordWrap = true,
            alignment = TextAnchor.UpperCenter,
            richText = true
        };

        _textWidth = maxWidth - 28;
        _textHeight = _textStyle.CalcHeight(new GUIContent(_currentText), _textWidth);
        _bubbleWidth = Mathf.Clamp(_textWidth + 28, minWidth, maxWidth);
        _textWidth = _bubbleWidth - 28;  // 重新计算（因为宽度变了）
        _textHeight = _textStyle.CalcHeight(new GUIContent(_currentText), _textWidth);
        _bubbleHeight = _textHeight + 28 + 4;

        // 2) 生成纹理
        int w = Mathf.RoundToInt(_bubbleWidth);
        int h = Mathf.RoundToInt(_bubbleHeight);
        int r = Mathf.RoundToInt(cornerRadius);

        _bgTex = GenRoundedRect(w, h, r, bgColor);
        _borderTex = GenRoundedRect(w, h, r, borderColor);
        _shadowTex = GenRoundedRect(w, h, r, new Color(0f, 0f, 0f, 0.22f));
        _accentTex = MakeTex(1, 1, accentColor);
        _tailTex = GenTriangle(Mathf.RoundToInt(tailWidth), Mathf.RoundToInt(tailHeight), bgColor);
        _ornamentTL = GenCornerOrnament(Mathf.RoundToInt(ornamentSize), ornamentColor, true);
        _ornamentBR = GenCornerOrnament(Mathf.RoundToInt(ornamentSize), ornamentColor, false);
        _starGlowTex = GenStarGlow(24, starColor);
    }

    // ---------- 圆角矩形 ----------

    private static Texture2D GenRoundedRect(int w, int h, float r, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
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

    // ---------- 三角形（朝下的小尾巴） ----------

    private static Texture2D GenTriangle(int w, int h, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        Color t = new Color(0, 0, 0, 0);
        float cx = (w - 1f) / 2f;

        for (int y = 0; y < h; y++)
        {
            float progress = (float)y / h; // 0 = 顶, 1 = 底
            float halfW = (1f - progress) * (w - 1f) / 2f;
            for (int x = 0; x < w; x++)
            {
                tex.SetPixel(x, y, (x >= cx - halfW && x <= cx + halfW) ? c : t);
            }
        }
        tex.Apply();
        return tex;
    }

    // ---------- 纯色 1×1 ----------

    private static Texture2D MakeTex(int w, int h, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                tex.SetPixel(x, y, c);
        tex.Apply();
        return tex;
    }

    // ---------- 角落云纹图案（古风卷草纹，加粗显眼版） ----------

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
                // 距离角落的归一化距离
                float d = Mathf.Sqrt((px * px + py * py) / (2f * (size - 1f) * (size - 1f)));
                float angle = Mathf.Atan2(py + 0.01f, px + 0.01f);
                // 螺旋卷草：粗线条 + 高对比
                float spiral = Mathf.Sin(angle * 3f + d * 10f) * 0.5f + 0.5f;
                float alphaMask = Mathf.Clamp01((1f - d) * 1.8f - 0.5f);
                float val = Mathf.Pow(spiral * alphaMask, 0.6f);
                // 降低阈值让更多像素显示，加粗线条
                bool draw = val > 0.20f && d < 0.85f;
                float a = draw ? Mathf.Clamp01(val * 1.5f) * c.a : 0f;
                tex.SetPixel(x, y, draw ? new Color(c.r, c.g, c.b, a) : t);
            }
        }
        tex.Apply();
        return tex;
    }

    // ---------- 单颗星发光点纹理（高斯光晕，亮核版） ----------

    private static Texture2D GenStarGlow(int size, Color c)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        Color t = new Color(0, 0, 0, 0);
        float cx = (size - 1f) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - cx) / cx;
                float dy = (y - cx) / cx;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float brightness = Mathf.Exp(-dist * dist * 2.5f);
                float a = Mathf.Clamp01(brightness) * c.a;
                tex.SetPixel(x, y, a > 0.01f ? new Color(c.r, c.g, c.b, a) : t);
            }
        }
        tex.Apply();
        return tex;
    }
}
