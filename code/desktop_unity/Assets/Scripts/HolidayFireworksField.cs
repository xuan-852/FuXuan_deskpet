using System;
using UnityEngine;

/// <summary>
/// 节日主题动态背景：按当前主题绘制低密度像素节日元素。
/// 只在节日主题中绘制；默认主题仍由 StarField 绘制星空与流星。
/// </summary>
public sealed class HolidayFireworksField
{
    private const int BurstCount = 5;
    private const int SparkCount = 12;
    private readonly Vector4[] _bursts = new Vector4[BurstCount];
    private readonly float[] _burstScales = new float[BurstCount];
    private Texture2D _sparkTex;
    private Color _primaryColor;
    private Color _secondaryColor;
    private Color _sparkColor;
    private GUIStyle _poetryStyle;
    private bool _initialized;
    private float _motionTime;

    public void Init(int seed, Color primary, Color secondary, Color spark)
    {
        ApplyTheme(primary, secondary, spark);
        var rng = new System.Random(seed);
        for (int i = 0; i < _bursts.Length; i++)
        {
            // x、爆点高度、周期速度、初始相位；上下分层，避免画面下半区留白。
            float layer = i / (BurstCount - 1f) * 0.48f;
            _bursts[i] = new Vector4(
                0.10f + (float)rng.NextDouble() * 0.80f,
                Mathf.Clamp(0.22f + layer + ((float)rng.NextDouble() - 0.5f) * 0.10f, 0.20f, 0.76f),
                0.26f + (float)rng.NextDouble() * 0.08f,
                (float)rng.NextDouble());
            // 尺寸错落：保留少量大烟花，也让远处的小烟花承担空间层次。
            _burstScales[i] = 0.78f + (float)rng.NextDouble() * 0.44f;
        }
        _motionTime = 0f;
        _initialized = true;
    }

    public void ApplyTheme(Color primary, Color secondary, Color spark)
    {
        _primaryColor = primary;
        _secondaryColor = secondary;
        _sparkColor = spark;
        if (_sparkTex != null) UnityEngine.Object.Destroy(_sparkTex);
        _sparkTex = UiTextureFactory.MakeTex(4, 4, Color.white);
    }

    public void UpdateMotion()
    {
        // 动态时间只在 Update 推进一次；不能在 IMGUI 的 Layout/Repaint 事件里推进，
        // 否则透明窗口的事件频率会让呼吸和位移动画出现停顿或跳变。
        _motionTime += Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
    }

    public void DrawFireworks(float px, float py, float pw, float ph, float animAlpha)
    {
        string themeId = HolidayThemeRuntime.ActiveId;
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        // The compact list view still renders the background over the whole panel.
        // Reserve the title/search/session band before placing holiday elements.
        bool compact = pw < 700f;
        float topInset = compact ? Mathf.Clamp(ph * 0.14f, 110f, 180f) : 0f;
        float bottomInset = compact ? Mathf.Clamp(ph * 0.035f, 30f, 56f) : 0f;
        float scenePy = py + topInset;
        float scenePh = Mathf.Max(160f, ph - topInset - bottomInset);
        try
        {
            if (themeId == "cn_new_year")
            {
                DrawNewYearBackplate(px, scenePy, pw, scenePh, animAlpha);
                DrawFireworkBurst(px, scenePy, pw, scenePh, animAlpha);
                DrawNewYearPoetry(px, scenePy, pw, scenePh, animAlpha);
            }
            else if (themeId == "lantern_festival") DrawLanterns(px, scenePy, pw, scenePh, animAlpha);
            else if (themeId == "dragon_boat")
            {
                DrawDragonBoatPoetry(px, scenePy, pw, scenePh, animAlpha);
                DrawDragonBoatMugwort(px, scenePy, pw, scenePh, animAlpha);
                DrawDuanwuWaterside(px, scenePy, pw, scenePh, animAlpha);
            }
            else if (themeId == "qixi") DrawQixi(px, scenePy, pw, scenePh, animAlpha);
            else if (themeId == "mid_autumn") DrawMidAutumn(px, scenePy, pw, scenePh, animAlpha);
        }
        finally
        {
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }

    private void DrawLanterns(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = _motionTime;
        DrawLanternMoon(px, py, pw, ph, animAlpha, time);
        DrawLanternWillow(px, py, pw, ph, animAlpha, time);

        // “花市灯如昼”：三层错落的灯市比均匀排布更接近夜市纵深。
        Color marketBeam = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.42f);
        DrawPixelLine(new Vector2(px + pw * 0.30f, py + ph * 0.13f),
            new Vector2(px + pw * 0.96f, py + ph * 0.13f), 2f, marketBeam);
        for (int i = 0; i < 8; i++)
        {
            float layer = i % 3;
            float x = px + (0.36f + (i % 4) * 0.10f + layer * 0.010f) * pw;
            float y = py + (0.10f + layer * 0.25f + (i / 6) * 0.06f) * ph
                + Mathf.Sin(time * (0.72f + (i % 2) * 0.08f) + i * 1.31f) * (4f + layer * 2f);
            float size = Mathf.Clamp(Mathf.Min(pw, ph) * (0.050f + (i % 2) * 0.008f), 18f, 34f);
            int litIndex = Mathf.FloorToInt(time * 0.55f) % 8;
            float eventPulse = i == litIndex
                ? (0.06f + (0.5f + 0.5f * Mathf.Sin(time * 3.4f)) * 0.10f)
                : 0f;
            float glow = 0.12f + (0.5f + 0.5f * Mathf.Sin(time * 1.15f + i * 0.9f)) * 0.08f + eventPulse;
            DrawRect(new Rect(x - size * 0.85f, y - size * 0.15f, size * 1.7f, size * 1.9f),
                new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * glow));
            DrawRect(new Rect(x - size * 0.48f, y - size * 0.75f, size * 0.96f, 3f),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.92f));
            DrawRect(new Rect(x - size * 0.64f, y - size * 0.40f, size * 1.28f, size * 1.28f),
                new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.92f));
            DrawRect(new Rect(x - size * 0.40f, y - size * 0.19f, size * 0.80f, size * 0.82f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.62f));
            DrawRect(new Rect(x - size * 0.73f, y + size * 0.87f, size * 1.46f, 3f),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.96f));
            DrawRect(new Rect(x - 1f, y + size * 1.05f, 2f, size * 0.70f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.78f));
            // 细挂线和横梁让灯笼与“花市”发生关系，而不是漂浮的色块。
            DrawRect(new Rect(x - 1f, y - size * 1.42f, 2f, size * 0.70f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.68f));
        }

        DrawLanternReflections(px, py, pw, ph, animAlpha, time);
        DrawLanternPoetry(px, py, pw, ph, animAlpha, time);
    }

    private void DrawLanternReflections(float px, float py, float pw, float ph, float animAlpha, float time)
    {
        Color reflection = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.075f);
        Color reflectionGold = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.11f);
        for (int row = 0; row < 4; row++)
        {
            float y = py + ph * (0.70f + row * 0.065f);
            float width = pw * (0.46f - row * 0.07f);
            float x = px + pw * 0.50f - width * 0.5f + Mathf.Sin(time * 0.35f + row) * 5f;
            DrawRect(new Rect(x, y, width, 3f), reflection);
            if (row < 3)
                DrawRect(new Rect(x + width * 0.24f, y + 6f, width * 0.52f, 2f), reflectionGold);
        }
    }

    private void DrawLanternMoon(float px, float py, float pw, float ph, float animAlpha, float time)
    {
        float shortSide = Mathf.Min(pw, ph);
        float radius = Mathf.Clamp(shortSide * 0.095f, 36f, 64f);
        float moonX = px + pw * 0.57f;
        float moonY = py + ph * 0.18f;
        float breath = 0.78f + (0.5f + 0.5f * Mathf.Sin(time * 0.72f)) * 0.16f;
        Color moon = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * breath);
        Color halo = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.16f);
        Color shadow = new Color(0.08f, 0.02f, 0.07f, animAlpha * 0.78f);
        // 七段横向像素带组成月轮，边缘收窄后能读成圆月，而不是叠出的方块。
        float[] widths = { 0.72f, 1.25f, 1.70f, 2.00f, 1.70f, 1.25f, 0.72f };
        for (int i = 0; i < widths.Length; i++)
        {
            float y = moonY + (i - 3) * radius * 0.22f;
            DrawRect(new Rect(moonX - radius * widths[i] * 0.5f, y, radius * widths[i], radius * 0.28f), moon);
        }
        DrawRect(new Rect(moonX - radius * 1.04f, moonY - radius * 0.84f, radius * 2.08f, 2f), halo);
        DrawRect(new Rect(moonX - radius * 1.04f, moonY + radius * 0.82f, radius * 2.08f, 2f), halo);
        DrawRect(new Rect(moonX - radius * 0.06f, moonY - radius * 0.28f, radius * 0.20f, radius * 0.16f), shadow);
        DrawRect(new Rect(moonX + radius * 0.18f, moonY + radius * 0.16f, radius * 0.18f, radius * 0.14f), shadow);
    }

    private void DrawLanternWillow(float px, float py, float pw, float ph, float animAlpha, float time)
    {
        Color branch = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.72f);
        Color leaf = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.62f);
        Vector2 root = new Vector2(px + pw * 0.36f, py + ph * 0.14f);
        Vector2 fork = new Vector2(px + pw * 0.40f, py + ph * 0.20f);
        DrawPixelLine(root, fork, 3f, branch);
        DrawPixelLine(fork, new Vector2(px + pw * 0.43f, py + ph * 0.27f), 2f, branch);
        DrawPixelLine(fork, new Vector2(px + pw * 0.37f, py + ph * 0.29f), 2f, branch);
        DrawPixelLine(fork, new Vector2(px + pw * 0.46f, py + ph * 0.25f), 2f, branch);
        for (int i = 0; i < 5; i++)
        {
            float t = 0.16f + i * 0.16f;
            Vector2 point = Vector2.Lerp(fork, new Vector2(px + pw * 0.44f, py + ph * 0.29f), t);
            point.x += Mathf.Sin(time * 0.45f + i) * 2f;
            DrawPixelLine(point, point + new Vector2(-6f, 9f + i * 1.5f), 2f, leaf);
            DrawRect(new Rect(point.x - 7f, point.y + 8f + i * 1.5f, 8f, 3f), leaf);
            DrawRect(new Rect(point.x - 2f, point.y + 12f + i * 1.5f, 3f, 7f), leaf);
        }
    }

    private void DrawLanternPoetry(float px, float py, float pw, float ph, float animAlpha, float time)
    {
        EnsurePoetryStyle();
        Color previousColor = GUI.color;
        GUI.color = Color.white;
        float shortSide = Mathf.Min(pw, ph);
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.052f), 18, 42);
        _poetryStyle.fontSize = fontSize;
        float lineHeight = Mathf.Max(24f, fontSize * 1.14f);
        float columnGap = Mathf.Max(28f, fontSize * 1.55f);
        float breath = 0.54f + (0.5f + 0.5f * Mathf.Sin(time * 0.82f)) * 0.30f;
        Color ink = Color.Lerp(_sparkColor, new Color(1f, 0.94f, 0.78f, 1f), 0.66f);
        _poetryStyle.normal.textColor = new Color(ink.r, ink.g, ink.b, animAlpha * breath);

        // 欧阳修《生查子·元夕》：右列起读，列内自上而下，列间从右向左。
        string[] columns = pw >= 700f
            ? new[] { "去年元夜时", "花市灯如昼", "月上柳梢头", "人约黄昏后" }
            : new[] { "花市灯如昼", "月上柳梢头", "人约黄昏后" };
        float poetryRight = px + pw * 0.93f;
        float poetryTop = py + ph * 0.29f;
        for (int column = 0; column < columns.Length; column++)
        {
            string text = columns[column];
            float x = poetryRight - column * columnGap;
            float y = poetryTop;
            for (int row = 0; row < text.Length; row++)
            {
                // 只保留静态中轴线轻微错位；诗词文字不做位置动画，避免小窗口中出现跳动感。
                float axisOffset = ((column + row) % 3 - 1) * Mathf.Min(1.4f, fontSize * 0.055f);
                float charY = y + row * lineHeight;
                GUI.Label(new Rect(x - fontSize * 0.5f + axisOffset, charY,
                    fontSize + 5f, lineHeight + 2f), text.Substring(row, 1), _poetryStyle);
            }
        }
        GUI.color = previousColor;
    }

    private void EnsurePoetryStyle()
    {
        if (_poetryStyle != null) return;
        _poetryStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };
        _poetryStyle.font = Font.CreateDynamicFontFromOSFont(
            new[] { "STXingkai", "华文行楷", "KaiTi", "楷体", "STKaiti" }, 22);
    }

    /// <summary>
    /// 新春诗词（王安石《元日》）：右列为句首，列内自上而下，列间从右向左。
    /// 烟花集中在聊天主体右侧场景（0.36~0.96 宽），诗句锚定在聊天主体右侧安全区。
    /// </summary>
    private void DrawNewYearPoetry(float px, float py, float pw, float ph, float animAlpha)
    {
        EnsurePoetryStyle();
        Color previousColor = GUI.color;
        float shortSide = Mathf.Min(pw, ph);
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.042f), 16, 34);
        _poetryStyle.fontSize = fontSize;
        float lineHeight = Mathf.Max(22f, fontSize * 1.16f);
        float columnGap = Mathf.Max(26f, fontSize * 1.60f);
        float breath = 0.54f + (0.5f + 0.5f * Mathf.Sin(_motionTime * 0.86f)) * 0.30f;
        Color ink = Color.Lerp(_sparkColor, new Color(1f, 0.94f, 0.76f, 1f), 0.84f);
        _poetryStyle.normal.textColor = new Color(ink.r, ink.g, ink.b, animAlpha * breath);
        GUI.color = Color.white;

        // 大界面显示完整四句，小界面保留核心两句。
        string[] columns = pw >= 700f
            ? new[] { "爆竹声中一岁除", "春风送暖入屠苏", "千门万户曈曈日", "总把新桃换旧符" }
            : new[] { "爆竹声中一岁除", "春风送暖入屠苏" };
        // DrawFireworks 由整个面板入口调用，必须避开左侧会话列表（约 0~0.32）。
        float poetryRight = px + pw * 0.92f;
        float poetryTop = py + ph * 0.28f;
        for (int column = 0; column < columns.Length; column++)
        {
            string text = columns[column];
            float x = poetryRight - column * columnGap;
            float y = poetryTop;
            for (int row = 0; row < text.Length; row++)
            {
                // 中轴线附近的轻微错位：保留书写感，不把文字打散。
                float axisOffset = ((column + row) % 3 - 1) * 1.35f;
                float charY = y + row * lineHeight;
                GUI.Label(new Rect(x - fontSize * 0.5f + axisOffset, charY,
                    fontSize + 4f, lineHeight + 2f), text.Substring(row, 1), _poetryStyle);
            }
        }
        GUI.color = previousColor;
    }

    private void DrawNewYearBackplate(float px, float py, float pw, float ph, float animAlpha)
    {
        Color panel = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.09f);
        Color gold = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.18f);
        for (int i = 0; i < 3; i++)
        {
            float x = px + pw * (0.42f + i * 0.20f);
            float y = py + ph * 0.68f;
            float w = pw * 0.12f;
            float h = ph * 0.17f;
            DrawRect(new Rect(x, y, w, h), panel);
            DrawRect(new Rect(x + w * 0.48f, y, 2f, h), gold);
            DrawRect(new Rect(x, y + h * 0.50f, w, 2f), gold);
            DrawRect(new Rect(x - 5f, y - 4f, w + 10f, 3f), gold);
        }
    }

    private void DrawDragonBoat(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = _motionTime;

        for (int row = 0; row < 4; row++)
        {
            for (int segment = 0; segment < 5; segment++)
            {
                float x = px + (0.03f + segment * 0.21f) * pw;
                float y = py + (0.76f + row * 0.055f) * ph
                    + Mathf.Sin(time * 1.2f + segment * 0.9f + row) * (3f + row);
                float alpha = animAlpha * (0.34f - row * 0.045f);
                DrawRect(new Rect(x, y, pw * 0.16f, 3f),
                    new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, alpha));
                DrawRect(new Rect(x + pw * 0.04f, y - 4f, pw * 0.06f, 3f),
                    new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, alpha * 0.78f));
            }
        }

        float boatWidth = Mathf.Clamp(pw * 0.36f, 250f, 360f);
        float travelRange = Mathf.Max(1f, pw - boatWidth - 84f);
        float boatX = px + 42f + Mathf.PingPong(time * 34f, travelRange);
        float boatY = py + ph * 0.62f + Mathf.Sin(time * 1.6f) * 5f;
        Color outline = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.98f);
        Color hull = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.96f);
        Color gold = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.98f);
        Color leaf = new Color(Mathf.Min(1f, _primaryColor.r + 0.16f), Mathf.Min(1f, _primaryColor.g + 0.12f),
            Mathf.Min(1f, _primaryColor.b + 0.08f), animAlpha * 0.98f);

        // 原始构图的龙头在左、旗帜在右：向左行驶保持原向，向右行驶时水平翻转。
        float pingPhase = Mathf.Repeat(time * 34f / travelRange, 2f);
        bool movingRight = pingPhase <= 1f;
        Matrix4x4 boatPreviousMatrix = GUI.matrix;
        if (movingRight)
            GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), new Vector2(boatX + boatWidth * 0.5f, boatY + 42f));

        DrawRect(new Rect(boatX + 22f, boatY + 72f, boatWidth - 42f, 5f),
            new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.62f));
        DrawRect(new Rect(boatX + 58f, boatY + 80f, boatWidth - 118f, 3f),
            new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.62f));

        DrawRect(new Rect(boatX + 26f, boatY + 35f, boatWidth - 52f, 27f), outline);
        DrawRect(new Rect(boatX + 40f, boatY + 40f, boatWidth - 80f, 14f), hull);
        DrawRect(new Rect(boatX + 50f, boatY + 54f, boatWidth - 100f, 8f), gold);
        DrawRect(new Rect(boatX + 18f, boatY + 43f, 12f, 12f), outline);
        DrawRect(new Rect(boatX + boatWidth - 30f, boatY + 43f, 12f, 12f), outline);

        float headX = boatX + 5f;
        DrawRect(new Rect(headX + 7f, boatY + 13f, 29f, 30f), outline);
        DrawRect(new Rect(headX, boatY + 25f, 16f, 14f), outline);
        DrawRect(new Rect(headX + 10f, boatY + 7f, 7f, 9f), gold);
        DrawRect(new Rect(headX + 24f, boatY + 5f, 7f, 11f), gold);
        DrawRect(new Rect(headX + 16f, boatY + 21f, 6f, 6f), gold);
        DrawRect(new Rect(headX + 17f, boatY + 22f, 3f, 3f), new Color(1f, 0.35f, 0.18f, animAlpha));
        DrawRect(new Rect(headX - 3f, boatY + 39f, 11f, 3f), gold);
        DrawPixelLine(new Vector2(headX + 5f, boatY + 38f), new Vector2(headX - 5f, boatY + 48f), 2f,
            new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.92f));

        DrawRect(new Rect(boatX + boatWidth - 38f, boatY + 2f, 4f, 37f), gold);
        DrawRect(new Rect(boatX + boatWidth - 34f, boatY + 5f, 25f, 13f), hull);
        DrawRect(new Rect(boatX + boatWidth - 29f, boatY + 8f, 15f, 3f), gold);

        for (int i = 0; i < 3; i++)
        {
            float x = boatX + 88f + i * 65f;
            float bob = Mathf.Sin(time * 1.8f + i * 0.9f) * 2f;
            float y = boatY + 9f + bob;
            DrawRect(new Rect(x + 9f, y, 13f, 5f), outline);
            DrawRect(new Rect(x + 4f, y + 5f, 23f, 7f), leaf);
            DrawRect(new Rect(x, y + 12f, 31f, 9f), outline);
            DrawRect(new Rect(x + 5f, y + 12f, 21f, 5f), leaf);
            DrawRect(new Rect(x + 14f, y + 6f, 3f, 15f), gold);
            DrawRect(new Rect(x + 14f, y + 21f, 4f, 4f), gold);
        }

        GUI.matrix = boatPreviousMatrix;

        // 船桨在翻转矩阵外按屏幕坐标绘制，避免旋转/镜像叠加后飞到船体上方。
        for (int i = 0; i < 3; i++)
        {
            float localX = boatX + 88f + i * 65f;
            float bob = Mathf.Sin(time * 1.8f + i * 0.9f) * 2f;
            float localY = boatY + 9f + bob;
            float paddleWave = Mathf.Sin(time * 4.2f + i * 0.8f) * 7f;
            float pivotX = movingRight ? boatX + boatWidth - (localX - boatX + 15f) : localX + 15f;
            float paddleDirection = movingRight ? -1f : 1f;
            Vector2 pivot = new Vector2(pivotX, localY + 25f);
            Vector2 blade = new Vector2(pivotX + paddleDirection * 16f, localY + 53f + paddleWave);
            DrawPixelLine(pivot, blade, 3f, new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.90f));
            DrawRect(new Rect(blade.x - 5f, blade.y - 2f, 10f, 5f), gold);
        }
    }

    private void DrawDragonBoatMugwort(float px, float py, float pw, float ph, float animAlpha)
    {
        Color stem = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.82f);
        Color leaf = new Color(_primaryColor.r, Mathf.Min(1f, _primaryColor.g + 0.16f), _primaryColor.b, animAlpha * 0.92f);
        Color leafLight = new Color(Mathf.Min(1f, _primaryColor.r + 0.18f), Mathf.Min(1f, _primaryColor.g + 0.22f),
            Mathf.Min(1f, _primaryColor.b + 0.10f), animAlpha * 0.86f);
        DrawMugwortBundle(new Vector2(px + pw * 0.38f, py + ph * 0.72f), 1.08f, _motionTime, false, stem, leaf, leafLight);
        DrawMugwortBundle(new Vector2(px + pw * 0.94f, py + ph * 0.72f), 1.08f, _motionTime + 1.7f, true, stem, leaf, leafLight);
    }

    private void DrawMugwortBundle(Vector2 basePoint, float scale, float time, bool mirror,
        Color stem, Color leaf, Color leafLight)
    {
        float direction = mirror ? -1f : 1f;
        Vector2 top = basePoint + new Vector2(direction * 10f, -62f * scale);
        DrawPixelLine(basePoint, top, 3f * scale, stem);
        for (int i = 0; i < 5; i++)
        {
            float t = 0.16f + i * 0.16f;
            Vector2 joint = Vector2.Lerp(basePoint, top, t);
            joint.x += Mathf.Sin(time * 1.4f + i) * 2f;
            Vector2 leafTip = joint + new Vector2(direction * (17f + (i % 2) * 5f) * scale, -9f * scale);
            DrawPixelLine(joint, leafTip, 5f * scale, leaf);
            DrawPixelLine(joint + new Vector2(direction * 3f * scale, -1f * scale), leafTip, 2f * scale, leafLight);
        }
        DrawRect(new Rect(basePoint.x - 8f * scale, basePoint.y - 1f * scale, 16f * scale, 17f * scale), leaf);
        DrawRect(new Rect(basePoint.x - 4f * scale, basePoint.y + 3f * scale, 8f * scale, 7f * scale), stem);
        DrawRect(new Rect(basePoint.x - 1f * scale, basePoint.y - 14f * scale, 2f * scale, 13f * scale), stem);
    }

    private void DrawDragonBoatPoetry(float px, float py, float pw, float ph, float animAlpha)
    {
        if (_poetryStyle == null)
        {
            _poetryStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _poetryStyle.font = Font.CreateDynamicFontFromOSFont(
                new[] { "STXingkai", "华文行楷", "KaiTi", "楷体", "STKaiti" }, 22);
        }
        Color previousColor = GUI.color;
        float shortSide = Mathf.Min(pw, ph);
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.037f), 16, 32);
        _poetryStyle.fontSize = fontSize;
        float lineHeight = Mathf.Max(22f, fontSize * 1.18f);
        float columnGap = Mathf.Max(24f, fontSize * 1.55f);
        float breath = 0.56f + (0.5f + 0.5f * Mathf.Sin(_motionTime * 0.85f)) * 0.28f;
        Color ink = Color.Lerp(_sparkColor, new Color(0.92f, 1.00f, 0.92f, 1f), 0.86f);
        _poetryStyle.normal.textColor = new Color(ink.r, ink.g, ink.b, animAlpha * breath);
        GUI.color = Color.white;

        // 右列为词句开头，列内自上而下，列间从右向左。
        string[] columns = pw >= 700f
            ? new[] { "银塘朱槛曲尘波", "圆绿卷新荷", "兰条荐浴", "菖花酿酒", "天气尚清和", "好将沉醉酬佳节", "十分酒", "十分歌" }
            : new[] { "银塘朱槛曲尘波", "圆绿卷新荷", "菖花酿酒", "十分歌" };
        float poetryRight = px + pw * 0.82f;
        float poetryTop = py + ph * 0.15f;
        for (int column = 0; column < columns.Length; column++)
        {
            string text = columns[column];
            float x = poetryRight - column * columnGap;
            float y = poetryTop;
            for (int row = 0; row < text.Length; row++)
            {
                // 中轴线附近的轻微错位：保留书写感，不把文字打散。
                float axisOffset = ((column + row) % 3 - 1) * 1.35f;
                float charY = y + row * lineHeight;
                GUI.Label(new Rect(x - fontSize * 0.5f + axisOffset, charY,
                    fontSize + 4f, lineHeight + 2f), text.Substring(row, 1), _poetryStyle);
            }
        }
        GUI.color = previousColor;
    }

    private void DrawDuanwuWaterside(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = _motionTime;
        Color water = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.54f);
        Color waterLight = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.76f);
        Color rail = new Color(_sparkColor.r, _sparkColor.g * 0.74f, _sparkColor.b * 0.48f, animAlpha * 0.58f);
        Color leaf = new Color(Mathf.Min(1f, _primaryColor.r + 0.12f), Mathf.Min(1f, _primaryColor.g + 0.16f),
            Mathf.Min(1f, _primaryColor.b + 0.08f), animAlpha * 0.88f);
        Color leafLight = new Color(Mathf.Min(1f, _primaryColor.r + 0.22f), Mathf.Min(1f, _primaryColor.g + 0.24f),
            Mathf.Min(1f, _primaryColor.b + 0.12f), animAlpha * 0.76f);

        // 银塘与朱槛：先铺出词中水岸，再放置诗词和艾草。
        float railX = px + pw * 0.06f;
        float railY = py + ph * 0.28f;
        float railWidth = pw * 0.22f;
        DrawRect(new Rect(railX, railY, railWidth, 4f), rail);
        DrawRect(new Rect(railX + pw * 0.03f, railY + 7f, railWidth * 0.86f, 2f),
            new Color(rail.r, rail.g, rail.b, rail.a * 0.52f));
        for (int i = 0; i < 4; i++)
        {
            float postX = railX + railWidth * (i / 3f);
            DrawRect(new Rect(postX, railY - 2f, 3f, 22f), rail);
        }

        // 圆绿新荷：少量大轮廓，避免重新退化成高密度粒子。
        DrawLotus(new Vector2(px + pw * 0.43f, py + ph * 0.53f), 1.28f, time, leaf, leafLight);
        DrawLotus(new Vector2(px + pw * 0.65f, py + ph * 0.62f), 1.08f, time + 1.2f, leaf, leafLight);
        DrawLotus(new Vector2(px + pw * 0.86f, py + ph * 0.49f), 0.98f, time + 2.1f, leaf, leafLight);

        // 龙舟始终使用同一套屏幕坐标绘制，船体、龙头、旗帜和船桨一起换向，避免反向时出现飞线。
        DrawDuanwuDragonBoat(px, py, pw, ph, animAlpha);

        // 曲尘波：水波只做低对比的横向起伏，不干扰竖式诗词。
        for (int row = 0; row < 3; row++)
        {
            float y = py + ph * (0.74f + row * 0.070f);
            for (int segment = 0; segment < 4; segment++)
            {
                float x = px + pw * (0.36f + segment * 0.16f);
                float wave = Mathf.Sin(time * 0.85f + segment * 0.9f + row * 0.7f) * 3f;
                Vector2 start = new Vector2(x, y + wave);
                Vector2 end = new Vector2(x + pw * 0.12f, y + Mathf.Sin(time * 0.85f + segment * 0.9f + row * 0.7f + 0.8f) * 3f);
                DrawPixelLine(start, end, row % 2 == 0 ? 4f : 3f, row % 2 == 0 ? water : waterLight);
            }
        }

        // 十分酒：仅保留一个小型暖色酒盏，作为词意收束点。
        float cupX = px + pw * 0.87f;
        float cupY = py + ph * 0.70f + Mathf.Sin(time * 0.8f) * 2f;
        Color cup = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.78f);
        DrawRect(new Rect(cupX, cupY, 18f, 4f), cup);
        DrawRect(new Rect(cupX + 3f, cupY + 4f, 12f, 7f), cup);
        DrawRect(new Rect(cupX + 6f, cupY + 11f, 6f, 2f), cup);
    }

    private void DrawDuanwuDragonBoat(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = _motionTime;
        bool compact = pw < 700f;
        float boatWidth = Mathf.Clamp(pw * (compact ? 0.42f : 0.40f), compact ? 180f : 290f, compact ? 250f : 400f);
        float sceneLeft = px + pw * 0.36f;
        float sceneRight = px + pw * 0.96f;
        float travelRange = Mathf.Max(1f, sceneRight - sceneLeft - boatWidth);
        float travel = Mathf.PingPong(time * 12f, travelRange);
        float boatX = sceneLeft + travel;
        float boatY = py + ph * 0.55f + Mathf.Sin(time * 1.35f) * 3f;
        bool movingRight = Mathf.Repeat(time * 12f / travelRange, 2f) < 1f;

        Color outline = new Color(0.05f, 0.30f, 0.24f, animAlpha * 0.96f);
        Color hull = new Color(0.05f, 0.48f, 0.32f, animAlpha * 0.98f);
        Color hullLight = new Color(0.18f, 0.66f, 0.43f, animAlpha * 0.90f);
        Color gold = new Color(0.82f, 0.66f, 0.20f, animAlpha * 0.92f);
        Color paddle = new Color(0.88f, 0.72f, 0.25f, animAlpha * 0.88f);

        // 水下倒影比船体更弱，帮助船体从背景中脱出但不压住诗词。
        DrawRect(new Rect(boatX + 22f, boatY + 78f, boatWidth - 44f, 4f),
            new Color(outline.r, outline.g, outline.b, animAlpha * 0.52f));
        DrawRect(new Rect(boatX + 66f, boatY + 87f, boatWidth - 132f, 3f),
            new Color(gold.r, gold.g, gold.b, animAlpha * 0.42f));

        // 船身和两道金色船沿。
        DrawRect(new Rect(boatX + 22f, boatY + 37f, boatWidth - 44f, 28f), outline);
        DrawRect(new Rect(boatX + 36f, boatY + 42f, boatWidth - 72f, 15f), hull);
        DrawRect(new Rect(boatX + 49f, boatY + 55f, boatWidth - 98f, 7f), hullLight);
        DrawRect(new Rect(boatX + 41f, boatY + 65f, boatWidth - 82f, 4f), gold);

        float headX = movingRight ? boatX + boatWidth - 48f : boatX + 8f;
        float headDirection = movingRight ? 1f : -1f;
        // 龙头：鼻吻朝向运动方向，眼睛和角也随同一坐标系换向。
        DrawRect(new Rect(headX + (movingRight ? 8f : 10f), boatY + 14f, 30f, 30f), hull);
        DrawRect(new Rect(headX + (movingRight ? 34f : -8f), boatY + 27f, 14f, 13f), hullLight);
        DrawRect(new Rect(headX + (movingRight ? 17f : 20f), boatY + 6f, 7f, 10f), gold);
        DrawRect(new Rect(headX + (movingRight ? 30f : 8f), boatY + 5f, 7f, 11f), gold);
        DrawRect(new Rect(headX + (movingRight ? 25f : 14f), boatY + 22f, 6f, 6f), gold);
        DrawRect(new Rect(headX + (movingRight ? 28f : 13f), boatY + 23f, 3f, 3f),
            new Color(1f, 0.30f, 0.16f, animAlpha));
        DrawRect(new Rect(headX + (movingRight ? 40f : -13f), boatY + 40f, 12f, 3f), gold);

        float flagX = movingRight ? boatX + 20f : boatX + boatWidth - 24f;
        DrawRect(new Rect(flagX, boatY + 1f, 4f, 38f), gold);
        DrawRect(new Rect(flagX + (movingRight ? 4f : -26f), boatY + 4f, 26f, 14f), hullLight);
        DrawRect(new Rect(flagX + (movingRight ? 9f : -21f), boatY + 8f, 16f, 3f), gold);

        // 三名鼓手保持在船内，桨线固定连接船沿和水面，不再使用 GUI 矩阵镜像。
        for (int i = 0; i < 3; i++)
        {
            float seatX = boatX + 83f + i * 58f;
            float bob = Mathf.Sin(time * 1.8f + i * 0.9f) * 1.5f;
            float seatY = boatY + 11f + bob;
            DrawRect(new Rect(seatX + 10f, seatY, 12f, 5f), outline);
            DrawRect(new Rect(seatX + 5f, seatY + 5f, 22f, 8f), hullLight);
            DrawRect(new Rect(seatX, seatY + 13f, 32f, 8f), outline);
            DrawRect(new Rect(seatX + 6f, seatY + 13f, 20f, 4f), hullLight);

            float paddleWave = Mathf.Sin(time * 3.2f + i * 0.8f) * 3f;
            Vector2 pivot = new Vector2(seatX + 16f, seatY + 23f);
            Vector2 blade = pivot + new Vector2(headDirection * 15f, 43f + paddleWave);
            DrawPixelLine(pivot, blade, 3f, paddle);
            DrawRect(new Rect(blade.x - 5f, blade.y - 2f, 10f, 5f), paddle);
        }
    }

    private void DrawLotus(Vector2 center, float scale, float time, Color leaf, Color leafLight)
    {
        float sway = Mathf.Sin(time * 0.65f + center.x * 0.01f) * 2f;
        float x = center.x + sway;
        float y = center.y;
        DrawRect(new Rect(x - 29f * scale, y, 58f * scale, 8f * scale), leaf);
        DrawRect(new Rect(x - 21f * scale, y - 7f * scale, 42f * scale, 7f * scale), leafLight);
        DrawRect(new Rect(x - 11f * scale, y - 13f * scale, 22f * scale, 7f * scale), leaf);
        DrawRect(new Rect(x - 3f * scale, y - 18f * scale, 6f * scale, 6f * scale), leafLight);
        DrawRect(new Rect(x + 9f * scale, y + 8f * scale, 19f * scale, 3f * scale), leafLight);
        DrawRect(new Rect(x - 5f * scale, y - 25f * scale, 10f * scale, 5f * scale), leafLight);
        DrawRect(new Rect(x - 13f * scale, y - 21f * scale, 8f * scale, 5f * scale), leaf);
        DrawRect(new Rect(x + 5f * scale, y - 21f * scale, 8f * scale, 5f * scale), leaf);
        DrawPixelLine(new Vector2(x, y + 8f * scale), new Vector2(x - 1f * scale, y + 20f * scale),
            2f * scale, leafLight);
    }

    private void DrawQixi(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = _motionTime;
        Color star = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.94f);
        Color starSoft = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.78f);
        for (int i = 0; i < 16; i++)
        {
            float x = px + (0.38f + ((i * 37) % 86) / 100f * 0.58f) * pw;
            float y = py + (0.10f + ((i * 23) % 72) / 100f) * ph;
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 1.25f + i * 1.4f);
            float twinkle = 0.34f + 0.66f * pulse;
            float size = i % 4 == 0 ? 9f : 6f;
            if (i % 4 == 0)
            {
                Color halo = new Color(1.00f, 0.70f, 0.30f, animAlpha * (0.08f + pulse * 0.34f));
                DrawRect(new Rect(x - size * 2.4f, y - 2f, size * 4.8f, 4f), halo);
                DrawRect(new Rect(x - 2f, y - size * 2.4f, 4f, size * 4.8f), halo);
            }
            DrawRect(new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                new Color(star.r, star.g, star.b, animAlpha * twinkle));
            if (i % 4 == 0)
            {
                DrawRect(new Rect(x - size * 1.8f, y - 1f, size * 3.6f, 2f),
                    new Color(starSoft.r, starSoft.g, starSoft.b, animAlpha * twinkle * 0.55f));
                DrawRect(new Rect(x - 1f, y - size * 1.8f, 2f, size * 3.6f),
                    new Color(starSoft.r, starSoft.g, starSoft.b, animAlpha * twinkle * 0.45f));
            }
        }

        // 纤云弄巧：用两组错落的像素云带把星空和鹊桥区分开，避免只剩几条横线。
        Color cloud = new Color(0.42f, 0.50f, 0.82f, animAlpha * 0.62f);
        Color cloudLight = new Color(0.78f, 0.80f, 1f, animAlpha * 0.48f);
        for (int i = 0; i < 4; i++)
        {
            float cloudX = px + pw * (0.25f + i * 0.17f);
            float cloudY = py + ph * (0.31f + (i % 2) * 0.08f);
            float cloudW = pw * (0.14f + (i % 2) * 0.03f);
            DrawRect(new Rect(cloudX, cloudY, cloudW, 6f), cloud);
            DrawRect(new Rect(cloudX + cloudW * 0.14f, cloudY - 7f, cloudW * 0.58f, 6f), cloudLight);
            DrawRect(new Rect(cloudX + cloudW * 0.36f, cloudY - 13f, cloudW * 0.28f, 6f), cloud);
        }

        // 鹊桥是七夕的主视觉：连续拱桥连接织女星与牵牛星；位置保持静止，只让星光透明度呼吸。
        float bridgeY = py + ph * 0.66f;
        Color bridgeGlow = new Color(0.58f, 0.32f, 0.92f, animAlpha * 0.24f);
        Color bridge = new Color(0.76f, 0.42f, 0.96f, animAlpha * 0.94f);
        Color bridgeLight = new Color(1.00f, 0.78f, 0.42f, animAlpha * 0.86f);
        for (int i = 0; i < 13; i++)
        {
            float t = i / 12f;
            float x = px + pw * (0.23f + t * 0.54f);
            float y = bridgeY - Mathf.Sin(t * Mathf.PI) * ph * 0.075f;
            DrawRect(new Rect(x - 5f, y + 7f, pw * 0.060f, 10f), bridgeGlow);
            DrawRect(new Rect(x, y, pw * 0.070f, 8f), bridge);
            DrawRect(new Rect(x + pw * 0.012f, y - 8f, pw * 0.042f, 5f), bridgeLight);
            if (i < 12)
                DrawRect(new Rect(x + pw * 0.062f, y + 11f, pw * 0.020f, 4f),
                    new Color(0.42f, 0.58f, 1.00f, animAlpha * 0.76f));
        }
        float leftStarX = px + pw * 0.23f;
        float rightStarX = px + pw * 0.77f;
        float starY = bridgeY - ph * 0.045f;
        DrawRect(new Rect(leftStarX - 13f, starY - 13f, 26f, 26f),
            new Color(star.r, star.g, star.b, animAlpha * 0.24f));
        DrawRect(new Rect(rightStarX - 13f, starY - 13f, 26f, 26f),
            new Color(star.r, star.g, star.b, animAlpha * 0.24f));
        DrawRect(new Rect(leftStarX - 8f, starY - 8f, 16f, 16f), star);
        DrawRect(new Rect(rightStarX - 8f, starY - 8f, 16f, 16f), star);
        DrawRect(new Rect(leftStarX - 19f, starY - 3f, 38f, 6f), starSoft);
        DrawRect(new Rect(rightStarX - 19f, starY - 3f, 38f, 6f), starSoft);
        DrawRect(new Rect(leftStarX - 3f, starY - 19f, 6f, 38f), starSoft);
        DrawRect(new Rect(rightStarX - 3f, starY - 19f, 6f, 38f), starSoft);

        // 桥上的两只像素喜鹊剪影，增强“鹊桥”语义但不引入新贴图。
        Color magpie = new Color(0.40f, 0.46f, 0.78f, animAlpha * 0.94f);
        float birdY = bridgeY - ph * 0.12f;
        float leftBirdX = px + pw * 0.47f;
        float rightBirdX = px + pw * 0.55f;
        DrawPixelLine(new Vector2(leftBirdX - 19f, birdY), new Vector2(leftBirdX - 7f, birdY - 9f), 4f, magpie);
        DrawPixelLine(new Vector2(leftBirdX - 7f, birdY - 9f), new Vector2(leftBirdX + 7f, birdY), 4f, magpie);
        DrawPixelLine(new Vector2(leftBirdX + 7f, birdY), new Vector2(leftBirdX + 19f, birdY - 9f), 4f, magpie);
        DrawPixelLine(new Vector2(rightBirdX - 19f, birdY - 9f), new Vector2(rightBirdX - 7f, birdY), 4f, magpie);
        DrawPixelLine(new Vector2(rightBirdX - 7f, birdY), new Vector2(rightBirdX + 7f, birdY - 9f), 4f, magpie);
        DrawPixelLine(new Vector2(rightBirdX + 7f, birdY - 9f), new Vector2(rightBirdX + 19f, birdY), 4f, magpie);

        DrawQixiPoetry(px, py, pw, ph, animAlpha);
    }

    /// <summary>七夕诗词（秦观《鹊桥仙·纤云弄巧》开篇）：右列为句首，列内自上而下，列间从右向左。</summary>
    private void DrawQixiPoetry(float px, float py, float pw, float ph, float animAlpha)
    {
        EnsurePoetryStyle();
        Color previousColor = GUI.color;
        float shortSide = Mathf.Min(pw, ph);
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.042f), 16, 34);
        _poetryStyle.fontSize = fontSize;
        float lineHeight = Mathf.Max(22f, fontSize * 1.16f);
        float columnGap = Mathf.Max(26f, fontSize * 1.60f);
        float breath = 0.78f + (0.5f + 0.5f * Mathf.Sin(_motionTime * 0.84f)) * 0.22f;
        Color ink = new Color(1.00f, 0.92f, 0.72f, 1f);
        _poetryStyle.normal.textColor = new Color(ink.r, ink.g, ink.b, animAlpha * breath);
        GUI.color = Color.white;

        string[] columns = pw >= 700f
            ? new[] { "纤云弄巧", "飞星传恨", "银汉迢迢暗度" }
            : new[] { "纤云弄巧", "银汉迢迢暗度" };
        // 动态层的坐标原点是整个面板，诗词不能落入左侧会话列表。
        float poetryRight = px + pw * 0.90f;
        float poetryTop = py + ph * 0.26f;
        for (int column = 0; column < columns.Length; column++)
        {
            string text = columns[column];
            float x = poetryRight - column * columnGap;
            float y = poetryTop;
            for (int row = 0; row < text.Length; row++)
            {
                float axisOffset = ((column + row) % 3 - 1) * 1.30f;
                GUI.Label(new Rect(x - fontSize * 0.5f + axisOffset, y + row * lineHeight,
                    fontSize + 4f, lineHeight + 2f), text.Substring(row, 1), _poetryStyle);
            }
        }
        GUI.color = previousColor;
    }

    private void DrawMidAutumn(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = _motionTime;
        bool compact = pw < 700f;
        float moonX = px + pw * (compact ? 0.78f : 0.74f);
        float moonY = py + ph * (compact ? 0.18f : 0.17f);
        float moonBreath = 0.70f + (0.5f + 0.5f * Mathf.Sin(time * 0.75f)) * 0.18f;
        Color moon = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * moonBreath);
        Color moonShadow = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.42f);
        float moonWidth = compact ? 58f : 92f;
        float moonHeight = compact ? 46f : 70f;
        DrawRect(new Rect(moonX - moonWidth * 0.50f, moonY - moonHeight * 0.50f, moonWidth, moonHeight),
            new Color(moon.r, moon.g, moon.b, animAlpha * 0.12f));
        DrawRect(new Rect(moonX - moonWidth * 0.35f, moonY - moonHeight * 0.50f, moonWidth * 0.70f, moonHeight), moon);
        DrawRect(new Rect(moonX - moonWidth * 0.44f, moonY - moonHeight * 0.28f, moonWidth * 0.88f, moonHeight * 0.56f), moon);
        DrawRect(new Rect(moonX - moonWidth * 0.22f, moonY - moonHeight * 0.42f, moonWidth * 0.44f, moonHeight * 0.84f), moon);
        DrawRect(new Rect(moonX - moonWidth * 0.20f, moonY - moonHeight * 0.13f, moonWidth * 0.11f, moonHeight * 0.10f), moonShadow);
        DrawRect(new Rect(moonX + moonWidth * 0.10f, moonY + moonHeight * 0.08f, moonWidth * 0.12f, moonHeight * 0.08f), moonShadow);

        // 云朵只铺在右侧聊天区，作为月亮的中景层，并以慢速横移保持呼吸感。
        Color cloud = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.68f);
        for (int i = 0; i < 4; i++)
        {
            float x = px + pw * (0.39f + i * 0.17f) + Mathf.Sin(time * 0.5f + i) * 8f;
            float y = py + ph * (0.34f + (i % 2) * 0.17f);
            DrawRect(new Rect(x, y, 42f, 5f), cloud);
            DrawRect(new Rect(x + 10f, y - 5f, 24f, 5f),
                new Color(cloud.r, cloud.g, cloud.b, cloud.a * 0.78f));
        }

        DrawMidAutumnOsmanthus(px, py, pw, ph, animAlpha, time);

        // 玉兔使用更完整的像素轮廓，并用轻跳作为短时事件动效。
        float rabbitX = px + pw * 0.61f;
        float rabbitY = py + ph * 0.56f - Mathf.Max(0f, Mathf.Sin(time * 1.35f)) * 7f;
        Color rabbit = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.80f);
        Matrix4x4 rabbitPreviousMatrix = GUI.matrix;
        if (!compact)
            GUIUtility.ScaleAroundPivot(new Vector2(1.18f, 1.18f), new Vector2(rabbitX + 19f, rabbitY + 10f));
        DrawRect(new Rect(rabbitX + 8f, rabbitY - 15f, 7f, 18f), rabbit);
        DrawRect(new Rect(rabbitX + 22f, rabbitY - 13f, 7f, 16f), rabbit);
        DrawRect(new Rect(rabbitX + 6f, rabbitY, 28f, 24f), rabbit);
        DrawRect(new Rect(rabbitX, rabbitY + 8f, 38f, 15f), rabbit);
        DrawRect(new Rect(rabbitX + 3f, rabbitY + 23f, 10f, 5f), rabbit);
        DrawRect(new Rect(rabbitX + 25f, rabbitY + 23f, 10f, 5f), rabbit);
        DrawRect(new Rect(rabbitX + 27f, rabbitY + 6f, 4f, 4f),
            new Color(0.96f, 0.36f, 0.28f, animAlpha * 0.92f));
        DrawRect(new Rect(rabbitX - 9f, rabbitY + 15f, 10f, 6f), rabbit);
        GUI.matrix = rabbitPreviousMatrix;

        DrawMidAutumnPoetry(px, py, pw, ph, animAlpha);
    }

    /// <summary>中秋诗词（苏轼《水调歌头·明月几时有》节选）：右列为句首，列内自上而下，列间从右向左。</summary>
    private void DrawMidAutumnPoetry(float px, float py, float pw, float ph, float animAlpha)
    {
        EnsurePoetryStyle();
        Color previousColor = GUI.color;
        float shortSide = Mathf.Min(pw, ph);
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.042f), 16, 34);
        _poetryStyle.fontSize = fontSize;
        float lineHeight = Mathf.Max(22f, fontSize * 1.16f);
        float columnGap = Mathf.Max(26f, fontSize * 1.60f);
        float breath = 0.54f + (0.5f + 0.5f * Mathf.Sin(_motionTime * 0.82f)) * 0.30f;
        Color ink = Color.Lerp(_sparkColor, new Color(1f, 0.92f, 0.70f, 1f), 0.72f);
        _poetryStyle.normal.textColor = new Color(ink.r, ink.g, ink.b, animAlpha * breath);
        GUI.color = Color.white;

        string[] columns = pw >= 700f
            ? new[] { "明月几时有", "把酒问青天", "但愿人长久", "千里共婵娟" }
            : new[] { "明月几时有", "千里共婵娟" };
        // 动态层的坐标原点是整个面板，诗词不能落入左侧会话列表。
        float poetryRight = px + pw * 0.92f;
        float poetryTop = py + ph * 0.26f;
        for (int column = 0; column < columns.Length; column++)
        {
            string text = columns[column];
            float x = poetryRight - column * columnGap;
            float y = poetryTop;
            for (int row = 0; row < text.Length; row++)
            {
                float axisOffset = ((column + row) % 3 - 1) * 1.30f;
                GUI.Label(new Rect(x - fontSize * 0.5f + axisOffset, y + row * lineHeight,
                    fontSize + 4f, lineHeight + 2f), text.Substring(row, 1), _poetryStyle);
            }
        }
        GUI.color = previousColor;
    }

    private void DrawMidAutumnOsmanthus(float px, float py, float pw, float ph, float animAlpha, float time)
    {
        Color branch = new Color(_secondaryColor.r * 0.86f, _secondaryColor.g * 0.86f,
            _secondaryColor.b * 0.86f, animAlpha * 0.78f);
        Color leaf = new Color(0.25f, 0.42f, 0.37f, animAlpha * 0.78f);
        Color flower = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.82f);
        Vector2 root = new Vector2(px + pw * 0.38f, py + ph * 0.80f);
        Vector2 fork = new Vector2(px + pw * 0.50f, py + ph * 0.70f);
        DrawPixelLine(root, fork, 6f, branch);
        DrawPixelLine(fork, new Vector2(px + pw * 0.57f, py + ph * 0.62f), 4f, branch);
        DrawPixelLine(fork, new Vector2(px + pw * 0.43f, py + ph * 0.64f), 4f, branch);
        for (int i = 0; i < 6; i++)
        {
            float x = px + pw * (0.40f + i * 0.034f) + Mathf.Sin(time * 0.35f + i) * 2f;
            float y = py + ph * (0.73f - (i % 3) * 0.045f);
            DrawRect(new Rect(x, y, 16f, 5f), leaf);
            DrawRect(new Rect(x + (i % 2 == 0 ? 8f : -5f), y - 5f, 12f, 5f), leaf);
            DrawRect(new Rect(x + 3f, y - 11f, 5f, 5f), flower);
        }
    }

    private void DrawRect(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, _sparkTex);
    }

    private void DrawPixelLine(Vector2 start, Vector2 end, float thickness, Color color)
    {
        float safeThickness = Mathf.Max(1f, thickness);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / Mathf.Max(2f, safeThickness * 1.35f)));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(start, end, i / (float)steps);
            DrawRect(new Rect(Mathf.Round(point.x - safeThickness * 0.5f), Mathf.Round(point.y - safeThickness * 0.5f),
                safeThickness, safeThickness), color);
        }
    }

    private void DrawFireworkBurst(float px, float py, float pw, float ph, float animAlpha)
    {
        if (!_initialized || _sparkTex == null) return;
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        float time = _motionTime;

        for (int i = 0; i < _bursts.Length; i++)
        {
            Vector4 burst = _bursts[i];
            float cycle = Mathf.Repeat(time * burst.z + burst.w, 1f);
            float launchT = Mathf.Clamp01(cycle / 0.20f);
            float burstT = Mathf.Clamp01((cycle - 0.20f) / 0.46f);
            float sceneLeft = px + pw * 0.36f;
            float sceneRight = px + pw * 0.96f;
            float centerX = Mathf.Lerp(sceneLeft, sceneRight, burst.x);
            float centerY = py + (1.02f - launchT * (1.02f - burst.y)) * ph;
            Color tint = i % 2 == 0
                ? Color.Lerp(_primaryColor, _sparkColor, 0.58f)
                : Color.Lerp(_secondaryColor, _sparkColor, 0.42f);

            // 升空尾焰：细金线 + 亮点。
            if (cycle < 0.20f)
            {
                float tailAlpha = animAlpha * (0.54f + 0.40f * launchT);
                GUI.color = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, tailAlpha);
                GUI.DrawTexture(new Rect(centerX - 1f, centerY + 15f, 2f, Mathf.Max(5f, ph * 0.12f)), _sparkTex);
                GUI.color = new Color(1f, 0.96f, 0.64f, animAlpha * 0.96f);
                GUI.DrawTexture(new Rect(centerX - 2f, centerY + 9f, 4f, 7f), _sparkTex);
                continue;
            }

            // 爆裂阶段：火星先高速向外冲，再受重力沿抛物线下坠。
            float fade = 1f - Mathf.Clamp01((burstT - 0.62f) / 0.38f);
            float maxRadius = Mathf.Min(pw, ph) * 0.18f * _burstScales[i];
            // 中心黄点只作为短促闪光，不在整个爆裂阶段持续占据视觉中心。
            if (burstT < 0.14f)
            {
                float coreFlash = 1f - burstT / 0.14f;
                float coreSize = Mathf.Lerp(8f, 3f, burstT) * (0.92f + _burstScales[i] * 0.10f);
                GUI.color = new Color(1f, 0.92f, 0.56f, animAlpha * fade * coreFlash * 1.00f);
                GUI.DrawTexture(new Rect(centerX - coreSize * 0.5f, centerY - coreSize * 0.5f,
                    coreSize, coreSize), _sparkTex);
            }

            for (int s = 0; s < SparkCount; s++)
            {
                float angle = (Mathf.PI * 2f * s / SparkCount) + i * 0.37f
                    + Mathf.Sin(s * 2.31f + i * 1.17f) * 0.055f;
                float lengthScale = 0.80f + 0.20f * Mathf.Sin(s * 1.7f + i);
                Vector2 position = GetSparkPosition(new Vector2(centerX, centerY), angle,
                    burstT, maxRadius * lengthScale);
                Vector2 previousPosition = GetSparkPosition(new Vector2(centerX, centerY), angle,
                    Mathf.Max(0f, burstT - 0.08f), maxRadius * lengthScale);
                Vector2 trailPosition = GetSparkPosition(new Vector2(centerX, centerY), angle,
                    Mathf.Max(0f, burstT - 0.20f), maxRadius * lengthScale);
                float thickness = s % 3 == 0 ? 2.2f : 1.4f;
                float rayAlpha = animAlpha * Mathf.Max(0.34f, fade * (0.86f + 0.12f * Mathf.Sin(s * 1.3f + i)));
                DrawSegment(trailPosition, previousPosition, thickness * 0.72f,
                    new Color(tint.r, tint.g, tint.b, rayAlpha * 0.34f));
                DrawSegment(previousPosition, position, thickness,
                    new Color(tint.r, tint.g, tint.b, rayAlpha));

                float sparkSize = (s % 3 == 0 ? 5f : 3f) * (0.92f + _burstScales[i] * 0.10f);
                GUI.matrix = previousMatrix;
                GUI.color = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, rayAlpha * 0.9f);
                GUI.DrawTexture(new Rect(position.x - sparkSize * 0.5f, position.y - sparkSize * 0.5f,
                    sparkSize, sparkSize), _sparkTex);
            }
        }

        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }

    public void Dispose()
    {
        if (_sparkTex != null) UnityEngine.Object.Destroy(_sparkTex);
        _sparkTex = null;
        _poetryStyle = null;
        _initialized = false;
    }

    private Vector2 GetSparkPosition(Vector2 center, float angle, float burstT, float maxRadius)
    {
        // 爆炸初速较高，后段略减速；额外的重力项让向上的火星自然回落。
        float radialT = 1.16f * burstT - 0.16f * burstT * burstT;
        float radialDistance = maxRadius * radialT;
        float gravity = maxRadius * 0.78f * burstT * burstT;
        return center + new Vector2(
            Mathf.Cos(angle) * radialDistance,
            Mathf.Sin(angle) * radialDistance + gravity);
    }

    private void DrawSegment(Vector2 start, Vector2 end, float thickness, Color color)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        Vector2 delta = end - start;
        float length = delta.magnitude;
        if (length < 0.5f) return;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
        GUI.color = color;
        GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), _sparkTex);
        GUI.matrix = previousMatrix;
    }
}
