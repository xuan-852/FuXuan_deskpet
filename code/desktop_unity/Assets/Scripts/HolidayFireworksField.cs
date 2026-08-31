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
        if (themeId == "new_year_day")
        {
            Matrix4x4 confettiPreviousMatrix = GUI.matrix;
            Color confettiPreviousColor = GUI.color;
            DrawNewYearConfetti(px, py, pw, ph, animAlpha);
            GUI.matrix = confettiPreviousMatrix;
            GUI.color = confettiPreviousColor;
            return;
        }

        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        if (themeId == "lantern_festival") DrawLanterns(px, py, pw, ph, animAlpha);
        else if (themeId == "dragon_boat")
        {
            DrawDragonBoatPoetry(px, py, pw, ph, animAlpha);
            DrawDragonBoatMugwort(px, py, pw, ph, animAlpha);
            DrawDragonBoat(px, py, pw, ph, animAlpha);
        }
        else if (themeId == "qixi") DrawQixi(px, py, pw, ph, animAlpha);
        else if (themeId == "mid_autumn") DrawMidAutumn(px, py, pw, ph, animAlpha);
        else if (themeId == "halloween") DrawHalloween(px, py, pw, ph, animAlpha);
        else if (themeId == "christmas") DrawChristmas(px, py, pw, ph, animAlpha);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }

    private void DrawLanterns(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        for (int i = 0; i < 6; i++)
        {
            float x = px + (0.10f + i * 0.16f) * pw;
            float y = py + (0.13f + (i % 3) * 0.16f) * ph
                + Mathf.Sin(time * 1.4f + i * 1.7f) * 7f;
            float size = 14f + (i % 2) * 4f;
            DrawRect(new Rect(x - size * 0.85f, y - size * 0.15f, size * 1.7f, size * 1.9f),
                new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.10f));
            DrawRect(new Rect(x - size * 0.45f, y - size * 0.65f, size * 0.90f, 3f),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.86f));
            DrawRect(new Rect(x - size * 0.62f, y - size * 0.35f, size * 1.24f, size * 1.25f),
                new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.76f));
            DrawRect(new Rect(x - size * 0.36f, y - size * 0.18f, size * 0.72f, size * 0.80f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.45f));
            DrawRect(new Rect(x - size * 0.70f, y + size * 0.90f, size * 1.40f, 3f),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.82f));
            DrawRect(new Rect(x - 1f, y + size * 1.05f, 2f, size * 0.70f),
                new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.72f));
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
        DrawMugwortBundle(new Vector2(px + 54f, py + ph * 0.72f), 1.08f, Time.time, false, stem, leaf, leafLight);
        DrawMugwortBundle(new Vector2(px + pw - 54f, py + ph * 0.72f), 1.08f, Time.time + 1.7f, true, stem, leaf, leafLight);
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
                clipping = TextClipping.Clip
            };
            _poetryStyle.font = Font.CreateDynamicFontFromOSFont(
                new[] { "STXingkai", "华文行楷", "KaiTi", "楷体", "STKaiti" }, 22);
        }
        Color previousColor = GUI.color;
        float shortSide = Mathf.Min(pw, ph);
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.026f), 14, 30);
        _poetryStyle.fontSize = fontSize;
        float lineHeight = Mathf.Max(24f, fontSize * 1.45f);
        float breath = 0.16f + (0.5f + 0.5f * Mathf.Sin(Time.time * 0.85f)) * 0.14f;
        _poetryStyle.normal.textColor = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * breath);
        GUI.color = Color.white;
        float poetryY = py + ph * 0.22f;
        GUI.Label(new Rect(px + 24f, poetryY, pw - 48f, lineHeight), "端午临中夏，时清日复长", _poetryStyle);
        if (shortSide >= 560f)
        {
            _poetryStyle.normal.textColor = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * (breath * 0.62f));
            GUI.Label(new Rect(px + 24f, poetryY + lineHeight, pw - 48f, lineHeight), "盐梅已佐鼎，曲糵且传觞", _poetryStyle);
        }
        GUI.color = previousColor;
    }

    private void DrawQixi(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        for (int i = 0; i < 18; i++)
        {
            float x = px + (0.07f + ((i * 37) % 86) / 100f) * pw;
            float y = py + (0.10f + ((i * 23) % 72) / 100f) * ph;
            float twinkle = 0.45f + 0.45f * Mathf.Sin(time * 2.2f + i * 1.4f);
            float size = i % 4 == 0 ? 5f : 3f;
            DrawRect(new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * twinkle));
            if (i % 4 == 0)
            {
                DrawRect(new Rect(x - size * 1.5f, y - 1f, size * 3f, 2f),
                    new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * twinkle * 0.35f));
            }
        }
        float bridgeY = py + ph * 0.72f + Mathf.Sin(time * 0.8f) * 4f;
        for (int i = 0; i < 7; i++)
            DrawRect(new Rect(px + pw * (0.13f + i * 0.12f), bridgeY + Mathf.Sin(i * 0.8f) * 3f,
                pw * 0.09f, 2f), new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.32f));
    }

    private void DrawMidAutumn(float px, float py, float pw, float ph, float animAlpha)
    {
        float moonX = px + pw * 0.78f;
        float moonY = py + ph * 0.18f;
        Color moon = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.72f);
        DrawRect(new Rect(moonX - 24f, moonY - 18f, 48f, 36f), moon);
        DrawRect(new Rect(moonX - 18f, moonY - 24f, 36f, 48f), moon);
        DrawRect(new Rect(moonX - 30f, moonY - 10f, 60f, 20f), moon);
        DrawRect(new Rect(moonX - 14f, moonY - 7f, 7f, 5f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.35f));
        DrawRect(new Rect(moonX + 7f, moonY + 5f, 8f, 4f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.30f));
        for (int i = 0; i < 5; i++)
        {
            float x = px + pw * (0.12f + i * 0.20f) + Mathf.Sin(Time.time * 0.5f + i) * 8f;
            float y = py + ph * (0.34f + (i % 2) * 0.17f);
            DrawRect(new Rect(x, y, 34f, 4f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.22f));
            DrawRect(new Rect(x + 10f, y - 4f, 20f, 4f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.16f));
        }
        float rabbitX = px + pw * 0.62f;
        float rabbitY = py + ph * 0.55f;
        DrawRect(new Rect(rabbitX, rabbitY, 18f, 14f), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.52f));
        DrawRect(new Rect(rabbitX + 3f, rabbitY - 9f, 4f, 10f), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.52f));
        DrawRect(new Rect(rabbitX + 11f, rabbitY - 8f, 4f, 9f), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.52f));
    }

    private void DrawHalloween(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        for (int i = 0; i < 4; i++)
        {
            float x = px + pw * (0.14f + i * 0.24f);
            float y = py + ph * (0.70f + (i % 2) * 0.12f);
            DrawRect(new Rect(x, y, 24f, 20f), new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.72f));
            DrawRect(new Rect(x + 5f, y - 4f, 14f, 4f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.74f));
            DrawRect(new Rect(x + 5f, y + 7f, 5f, 5f), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.90f));
            DrawRect(new Rect(x + 15f, y + 7f, 5f, 5f), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.90f));
            DrawRect(new Rect(x + 8f, y + 14f, 9f, 3f), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.82f));
        }
        for (int i = 0; i < 4; i++)
        {
            float x = px + pw * (0.12f + i * 0.25f) + Mathf.Sin(time * 0.7f + i) * 12f;
            float y = py + ph * (0.22f + (i % 2) * 0.18f);
            DrawRect(new Rect(x, y, 18f, 3f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.70f));
            DrawRect(new Rect(x - 5f, y + 3f, 7f, 3f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.62f));
            DrawRect(new Rect(x + 16f, y + 3f, 7f, 3f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.62f));
        }
    }

    private void DrawChristmas(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        for (int i = 0; i < 24; i++)
        {
            float x = px + ((i * 43) % 94) / 100f * pw;
            float y = py + Mathf.Repeat(((i * 29) % 100) / 100f * ph + time * (12f + i % 3 * 5f), ph);
            float size = i % 5 == 0 ? 5f : 3f;
            DrawRect(new Rect(x, y, size, size), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * (0.38f + (i % 4) * 0.10f)));
        }
        for (int i = 0; i < 5; i++)
        {
            float x = px + pw * (0.10f + i * 0.19f);
            float y = py + ph * (0.67f + (i % 2) * 0.11f);
            DrawRect(new Rect(x, y, 38f, 3f), new Color(_secondaryColor.r, _secondaryColor.g, _secondaryColor.b, animAlpha * 0.55f));
            DrawRect(new Rect(x + 8f, y + 4f, 5f, 5f), new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, animAlpha * 0.82f));
            DrawRect(new Rect(x + 27f, y + 4f, 5f, 5f), new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, animAlpha * 0.82f));
        }
    }

    private void DrawNewYearConfetti(float px, float py, float pw, float ph, float animAlpha)
    {
        float time = Time.time;
        for (int i = 0; i < 18; i++)
        {
            float x = px + ((i * 47) % 92) / 100f * pw;
            float y = py + Mathf.Repeat(((i * 31) % 100) / 100f * ph + time * (18f + i % 4 * 4f), ph);
            float width = i % 3 == 0 ? 8f : 4f;
            Color color = i % 2 == 0 ? _primaryColor : _secondaryColor;
            DrawRect(new Rect(x, y, width, 3f), new Color(color.r, color.g, color.b, animAlpha * 0.66f));
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
            float centerX = px + burst.x * pw;
            float centerY = py + (1.02f - launchT * (1.02f - burst.y)) * ph;
            Color tint = (i % 2 == 0) ? _primaryColor : _secondaryColor;

            // 升空尾焰：细金线 + 亮点。
            if (cycle < 0.20f)
            {
                float tailAlpha = animAlpha * (0.28f + 0.32f * launchT);
                GUI.color = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, tailAlpha);
                GUI.DrawTexture(new Rect(centerX - 1f, centerY + 15f, 2f, Mathf.Max(5f, ph * 0.12f)), _sparkTex);
                GUI.color = new Color(1f, 0.96f, 0.64f, animAlpha * 0.82f);
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
                GUI.color = new Color(1f, 0.92f, 0.56f, animAlpha * fade * coreFlash * 0.92f);
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
                float rayAlpha = animAlpha * fade * (0.52f + 0.22f * Mathf.Sin(s * 1.3f + i));
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
