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
        // 烟花使用 Time.time 计算周期，Update 只保留接口以保持与 RightPanel 动画生命周期一致。
    }

    public void DrawFireworks(float px, float py, float pw, float ph, float animAlpha)
    {
        string themeId = HolidayThemeRuntime.ActiveId;
        if (themeId == "cn_new_year")
        {
            DrawFireworkBurst(px, py, pw, ph, animAlpha);
            return;
        }
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        if (themeId == "lantern_festival") DrawLanterns(px, py, pw, ph, animAlpha);
        else if (themeId == "dragon_boat")
        {
            DrawDragonBoatPoetry(px, py, pw, ph, animAlpha);
            DrawDragonBoatMugwort(px, py, pw, ph, animAlpha);
            DrawDuanwuWaterside(px, py, pw, ph, animAlpha);
        }
        else if (themeId == "qixi") DrawQixi(px, py, pw, ph, animAlpha);
        else if (themeId == "mid_autumn") DrawMidAutumn(px, py, pw, ph, animAlpha);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }

    private void DrawLanterns(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        for (int i = 0; i < 6; i++)
        {
            // 灯笼只落在右侧聊天主视图，避免侵入左侧会话列表；上下三层形成元宵夜景的空间层次。
            float x = px + (0.40f + i * 0.105f) * pw;
            float y = py + (0.12f + (i % 3) * 0.25f) * ph
                + Mathf.Sin(time * 1.4f + i * 1.7f) * 11f;
            float size = 14f + (i % 2) * 4f;
            DrawRect(new Rect(x - size * 0.85f, y - size * 0.15f, size * 1.7f, size * 1.9f),
                new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.18f));
            DrawRect(new Rect(x - size * 0.45f, y - size * 0.65f, size * 0.90f, 3f),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.98f));
            DrawRect(new Rect(x - size * 0.62f, y - size * 0.35f, size * 1.24f, size * 1.25f),
                new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.92f));
            DrawRect(new Rect(x - size * 0.36f, y - size * 0.18f, size * 0.72f, size * 0.80f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.66f));
            DrawRect(new Rect(x - size * 0.70f, y + size * 0.90f, size * 1.40f, 3f),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.94f));
            DrawRect(new Rect(x - 1f, y + size * 1.05f, 2f, size * 0.70f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.84f));
            // 细短挂线增强“悬挂”关系，同时保持低密度像素风。
            DrawRect(new Rect(x - 1f, y - size * 1.30f, 2f, size * 0.65f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.60f));
        }
    }

    private void DrawDragonBoat(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;

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
        DrawMugwortBundle(new Vector2(px + pw * 0.38f, py + ph * 0.72f), 1.08f, Time.time, false, stem, leaf, leafLight);
        DrawMugwortBundle(new Vector2(px + pw * 0.94f, py + ph * 0.72f), 1.08f, Time.time + 1.7f, true, stem, leaf, leafLight);
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
        float breath = 0.62f + (0.5f + 0.5f * Mathf.Sin(Time.time * 0.85f)) * 0.20f;
        Color ink = Color.Lerp(_sparkColor, new Color(0.86f, 0.98f, 0.88f, 1f), 0.58f);
        _poetryStyle.normal.textColor = new Color(ink.r, ink.g, ink.b, animAlpha * breath);
        GUI.color = Color.white;

        // 右列为词句开头，列内自上而下，列间从右向左。
        string[] columns = shortSide >= 430f
            ? new[] { "银塘朱槛曲尘波", "圆绿卷新荷", "兰条荐浴", "菖花酿酒", "天气尚清和", "好将沉醉酬佳节", "十分酒", "十分歌" }
            : new[] { "银塘朱槛曲尘波", "圆绿卷新荷", "菖花酿酒", "十分歌" };
        float poetryRight = px + pw * 0.82f;
        float poetryTop = py + ph * 0.15f;
        for (int column = 0; column < columns.Length; column++)
        {
            string text = columns[column];
            float x = poetryRight - column * columnGap;
            float y = poetryTop + Mathf.Sin(Time.time * 0.32f + column * 0.8f) * 1.5f;
            for (int row = 0; row < text.Length; row++)
            {
                // 中轴线附近的轻微错位：保留书写感，不把文字打散。
                float axisOffset = ((column + row) % 3 - 1) * 1.35f;
                float drift = Mathf.Sin(Time.time * 0.42f + column * 0.73f + row * 0.51f) * 0.55f;
                float charY = y + row * lineHeight;
                GUI.Label(new Rect(x - fontSize * 0.5f + axisOffset + drift, charY,
                    fontSize + 4f, lineHeight + 2f), text.Substring(row, 1), _poetryStyle);
            }
        }
        GUI.color = previousColor;
    }

    private void DrawDuanwuWaterside(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        Color water = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.54f);
        Color waterLight = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.76f);
        Color rail = new Color(_sparkColor.r, _sparkColor.g * 0.74f, _sparkColor.b * 0.48f, animAlpha * 0.58f);
        Color leaf = new Color(Mathf.Min(1f, _primaryColor.r + 0.12f), Mathf.Min(1f, _primaryColor.g + 0.16f),
            Mathf.Min(1f, _primaryColor.b + 0.08f), animAlpha * 0.88f);
        Color leafLight = new Color(Mathf.Min(1f, _primaryColor.r + 0.22f), Mathf.Min(1f, _primaryColor.g + 0.24f),
            Mathf.Min(1f, _primaryColor.b + 0.12f), animAlpha * 0.76f);

        // 银塘与朱槛：先铺出词中水岸，再放置诗词和艾草。
        float railY = py + ph * 0.18f;
        DrawRect(new Rect(px + pw * 0.38f, railY, pw * 0.54f, 4f), rail);
        DrawRect(new Rect(px + pw * 0.42f, railY + 7f, pw * 0.46f, 2f),
            new Color(rail.r, rail.g, rail.b, rail.a * 0.52f));
        for (int i = 0; i < 4; i++)
        {
            float postX = px + pw * (0.42f + i * 0.15f);
            DrawRect(new Rect(postX, railY - 2f, 3f, 22f), rail);
        }

        // 圆绿新荷：少量大轮廓，避免重新退化成高密度粒子。
        DrawLotus(new Vector2(px + pw * 0.43f, py + ph * 0.53f), 1.28f, time, leaf, leafLight);
        DrawLotus(new Vector2(px + pw * 0.65f, py + ph * 0.62f), 1.08f, time + 1.2f, leaf, leafLight);
        DrawLotus(new Vector2(px + pw * 0.86f, py + ph * 0.49f), 0.98f, time + 2.1f, leaf, leafLight);

        // 龙舟始终使用同一套屏幕坐标绘制，船体、龙头、旗帜和船桨一起换向，避免反向时出现飞线。
        DrawDuanwuDragonBoat(px, py, pw, ph, animAlpha);

        // 曲尘波：水波只做低对比的横向起伏，不干扰竖式诗词。
        for (int row = 0; row < 4; row++)
        {
            float y = py + ph * (0.73f + row * 0.055f);
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
        float time = Time.time;
        float boatWidth = Mathf.Clamp(pw * 0.34f, 250f, 340f);
        float sceneLeft = px + pw * 0.36f;
        float sceneRight = px + pw * 0.96f;
        float travelRange = Mathf.Max(1f, sceneRight - sceneLeft - boatWidth);
        float travel = Mathf.PingPong(time * 18f, travelRange);
        float boatX = sceneLeft + travel;
        float boatY = py + ph * 0.55f + Mathf.Sin(time * 1.35f) * 3f;
        bool movingRight = Mathf.Repeat(time * 18f / travelRange, 2f) < 1f;

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
        float time = Time.time;
        Color star = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.86f);
        Color starSoft = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.72f);
        for (int i = 0; i < 16; i++)
        {
            float x = px + (0.38f + ((i * 37) % 86) / 100f * 0.58f) * pw;
            float y = py + (0.10f + ((i * 23) % 72) / 100f) * ph;
            float twinkle = 0.62f + 0.30f * Mathf.Sin(time * 2.2f + i * 1.4f);
            float size = i % 4 == 0 ? 7f : 4f;
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

        // 鹊桥是七夕的主视觉：由连续的紫色桥段连接两颗高亮星，缓慢上下起伏。
        float bridgeY = py + ph * 0.72f + Mathf.Sin(time * 0.8f) * 4f;
        Color bridge = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.64f);
        for (int i = 0; i < 6; i++)
        {
            float x = px + pw * (0.40f + i * 0.095f);
            float y = bridgeY + Mathf.Sin(i * 0.8f + time * 0.55f) * 4f;
            DrawRect(new Rect(x, y, pw * 0.075f, 4f), bridge);
            DrawRect(new Rect(x + pw * 0.025f, y - 5f, pw * 0.025f, 3f),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.55f));
        }
        DrawRect(new Rect(px + pw * 0.39f, bridgeY - 3f, 8f, 8f), star);
        DrawRect(new Rect(px + pw * 0.91f, bridgeY - 3f, 8f, 8f), star);
    }

    private void DrawMidAutumn(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        float moonX = px + pw * 0.78f;
        float moonY = py + ph * 0.18f;
        float moonBreath = 0.70f + (0.5f + 0.5f * Mathf.Sin(time * 0.75f)) * 0.18f;
        Color moon = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * moonBreath);
        Color moonShadow = new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.42f);
        DrawRect(new Rect(moonX - 34f, moonY - 26f, 68f, 52f),
            new Color(moon.r, moon.g, moon.b, animAlpha * 0.12f));
        DrawRect(new Rect(moonX - 24f, moonY - 18f, 48f, 36f), moon);
        DrawRect(new Rect(moonX - 18f, moonY - 24f, 36f, 48f), moon);
        DrawRect(new Rect(moonX - 30f, moonY - 10f, 60f, 20f), moon);
        DrawRect(new Rect(moonX - 14f, moonY - 7f, 7f, 5f), moonShadow);
        DrawRect(new Rect(moonX + 7f, moonY + 5f, 8f, 4f), moonShadow);

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
        DrawRect(new Rect(rabbitX + 8f, rabbitY - 15f, 7f, 18f), rabbit);
        DrawRect(new Rect(rabbitX + 22f, rabbitY - 13f, 7f, 16f), rabbit);
        DrawRect(new Rect(rabbitX + 6f, rabbitY, 28f, 24f), rabbit);
        DrawRect(new Rect(rabbitX, rabbitY + 8f, 38f, 15f), rabbit);
        DrawRect(new Rect(rabbitX + 3f, rabbitY + 23f, 10f, 5f), rabbit);
        DrawRect(new Rect(rabbitX + 25f, rabbitY + 23f, 10f, 5f), rabbit);
        DrawRect(new Rect(rabbitX + 27f, rabbitY + 6f, 4f, 4f),
            new Color(0.96f, 0.36f, 0.28f, animAlpha * 0.92f));
        DrawRect(new Rect(rabbitX - 9f, rabbitY + 15f, 10f, 6f), rabbit);
    }

    private void DrawMidAutumnOsmanthus(float px, float py, float pw, float ph, float animAlpha, float time)
    {
        Color branch = new Color(_secondaryColor.r * 0.72f, _secondaryColor.g * 0.72f,
            _secondaryColor.b * 0.72f, animAlpha * 0.72f);
        Color leaf = new Color(0.18f, 0.34f, 0.30f, animAlpha * 0.72f);
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
        float time = Time.time;

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
            float maxRadius = Mathf.Min(pw, ph) * 0.16f * _burstScales[i];
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
                float rayAlpha = animAlpha * fade * (0.86f + 0.12f * Mathf.Sin(s * 1.3f + i));
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
