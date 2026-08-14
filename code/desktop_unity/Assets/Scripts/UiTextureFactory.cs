using UnityEngine;

/// <summary>
/// UI 纹理工厂 — RightPanel 拆分的静态工具类（2026-08-14）
/// 全部为无状态静态方法：运行时程序生成圆角/渐变/云纹/星空/太极/六芒星/气泡等纹理。
/// 原位于 RightPanel.cs（L2462-2777 / L3007-3091），拆分后 RightPanel 内调用点加 UiTextureFactory. 前缀。
/// 改 UI 视觉（颜色/圆角/纹理）优先改这里，勿在 RightPanel 内重复造轮子。
/// </summary>
public static class UiTextureFactory
{
    public static void DrawPixelRect(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    public static Texture2D MakeTex(int w, int h, Color color)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, color);
        tex.Apply();
        return tex;
    }

    /// <summary>创建圆形纹理（用于按钮背景）</summary>
    public static Texture2D MakeCircleTex(int size, Color color)
    {
        size = Mathf.Max(size, 4);
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float center = (size - 1) / 2f;
        float rad = center - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = dist <= rad ? color.a : Mathf.Lerp(color.a, 0f, (dist - rad) / 2f);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成圆角矩形纹理（复刻 ChatBubble 风格，用于输入框胶囊背景）</summary>
    public static Texture2D GenRoundedRect(int w, int h, float r, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
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

    /// <summary>生成圆角发光描边（SDF 边缘紫光，内部透明，叠在输入框背景上）</summary>
    public static Texture2D GenGlowRoundedRect(int w, int h, float r, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color t = new Color(0, 0, 0, 0);
        float hw = (w - 1f) / 2f;
        float hh = (h - 1f) / 2f;
        float rr = Mathf.Max(r - 1f, 0.5f); // 内缩一点，让发光带居中在边缘

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 圆角矩形 SDF（有符号距离，负=内部）
                float qx = Mathf.Abs(x - hw) - (hw - rr);
                float qy = Mathf.Abs(y - hh) - (hh - rr);
                float ax = Mathf.Max(qx, 0f);
                float ay = Mathf.Max(qy, 0f);
                float dist = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rr;

                if (dist <= 0f)
                {
                    tex.SetPixel(x, y, t); // 内部透明，透出圆角背景
                }
                else
                {
                    // 边缘 3px 紫色发光带：平滑渐弱
                    float a = Mathf.Clamp01(1f - (dist - 1f) / 3f);
                    a = a * a * c.a;
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
                }
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>创建竖直渐变纹理</summary>
    public static Texture2D MakeGradientTex(int w, int h, Color top, Color bottom, bool horizontal = false)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            Color c = Color.Lerp(top, bottom, t);
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    /// <summary>创建角落云纹图案（复刻 ChatBubble 风格）</summary>
    public static Texture2D GenCornerOrnament(int size, Color c, bool topLeft)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        Color t = new Color(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = topLeft ? x : (size - 1f - x);
                float py = topLeft ? y : (size - 1f - y);
                float d = Mathf.Sqrt((px * px + py * py) / (2f * (size - 1f) * (size - 1f)));
                float angle = Mathf.Atan2(py + 0.01f, px + 0.01f);
                float spiral = Mathf.Sin(angle * 3f + d * 10f) * 0.5f + 0.5f;
                float alphaMask = Mathf.Clamp01((1f - d) * 1.8f - 0.5f);
                float val = Mathf.Pow(spiral * alphaMask, 0.6f);
                bool draw = val > 0.20f && d < 0.85f;
                float a = draw ? Mathf.Clamp01(val * 1.5f) * c.a : 0f;
                tex.SetPixel(x, y, draw ? new Color(c.r, c.g, c.b, a) : t);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成星空纹理：透明底上随机散布星点（白色/金色/紫色）</summary>
    public static Texture2D MakeStarfieldTex(int w, int h, int starCount)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);
        // 确定性随机（固定种子，避免每次生成不同星空闪烁）
        var rng = new System.Random(42);
        for (int i = 0; i < starCount; i++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float a = 0.25f + (float)rng.NextDouble() * 0.65f;
            int t = rng.Next(3);
            Color c = t == 0 ? new Color(1f, 1f, 1f, a)
                    : t == 1 ? new Color(0.95f, 0.85f, 0.60f, a)   // 金色星
                             : new Color(0.75f, 0.60f, 1f, a);     // 紫色星
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    // ==================== 背景质感工具（2026-08-13 暗色星空升级） ====================

    /// <summary>径向光晕纹理：centerUV 处最亮，向外高斯衰减（用于左上角紫光晕）</summary>
    public static Texture2D MakeGlowTex(int size, Color c, float falloff, Vector2 centerUV)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float cx = centerUV.x * (size - 1), cy = centerUV.y * (size - 1);
        float maxD = size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - cx) / maxD, dy = (y - cy) / maxD;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = c.a * Mathf.Exp(-d * d * falloff * 6f);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, Mathf.Clamp01(a)));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>星云斑块：3 个随机大斑块（冷蓝/淡紫/暖紫三色按权重混合）低频雾，打破渐变色带，克制不抢星</summary>
    public static Texture2D MakeNebulaTex(int w, int h, int seed)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var rng = new System.Random(seed);
        float[] bx = new float[3], by = new float[3], br = new float[3], bi = new float[3];
        Color[] cols = { new Color(0.30f, 0.45f, 0.85f), new Color(0.52f, 0.36f, 0.74f), new Color(0.68f, 0.50f, 0.80f) };
        for (int i = 0; i < 3; i++)
        {
            bx[i] = (float)rng.NextDouble();
            by[i] = (float)rng.NextDouble();
            br[i] = 0.25f + (float)rng.NextDouble() * 0.35f;
            bi[i] = 0.5f + (float)rng.NextDouble();
        }
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1), v = y / (float)(h - 1);
                float val = 0f, r = 0f, g = 0f, b = 0f;
                for (int i = 0; i < 3; i++)
                {
                    float dx = (u - bx[i]) / br[i], dy = (v - by[i]) / br[i];
                    float d = dx * dx + dy * dy;
                    float wgt = bi[i] * Mathf.Exp(-d * 3f);
                    val += wgt;
                    r += cols[i].r * wgt; g += cols[i].g * wgt; b += cols[i].b * wgt;
                }
                val = Mathf.Clamp01(val * 0.5f);
                if (val > 0.001f) { r /= val; g /= val; b /= val; }
                tex.SetPixel(x, y, new Color(r, g, b, val * 0.26f));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>星点光晕：中心白过曝 → 边缘淡紫，柔和幂次衰减（光晕铺开更自然）</summary>
    public static Texture2D MakeStarGlowTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = Mathf.Pow(a, 1.9f) * 0.85f;   // 光晕收敛（中心亮芯+淡边缘），不抢白芯主次
                Color col = Color.Lerp(Color.white, new Color(0.80f, 0.70f, 1f, 0f), Mathf.Clamp01(d * 1.05f));
                tex.SetPixel(x, y, new Color(col.r, col.g, col.b, a));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>小星点：中心亮 + 上下左右十字微光（5×5）</summary>
    public static Texture2D MakeStarDotTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c);
                float a = 0f;
                if (dx <= 0.5f && dy <= 0.5f) a = 1f;      // 中心
                else if (dx <= 0.5f && dy <= 1.5f) a = 0.26f; // 竖臂（细弱，暗星不喧宾）
                else if (dy <= 0.5f && dx <= 1.5f) a = 0.26f; // 横臂
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>圆角细边框（九宫格）：外圆角矩形减去内圆角矩形，SDF 抗锯齿</summary>
    public static Texture2D GenRoundedBorderTex(int size, float radius, float lineW, Color c)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color t = new Color(0, 0, 0, 0);
        float innerRadius = Mathf.Max(radius - lineW, 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float dOuter = RoundedRectSdf(px, py, size, radius);                       // >0=外
                float dInner = RoundedRectSdf(px, py, size - lineW * 2f, innerRadius);     // <0=内
                float coverage = Mathf.Clamp01(-dOuter + 0.5f) * Mathf.Clamp01(dInner + 0.5f);
                float a = coverage * c.a;
                tex.SetPixel(x, y, a > 0.01f ? new Color(c.r, c.g, c.b, a) : t);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>圆角矩形有符号距离场：&lt;0=内部，&gt;0=外部，绝对值≈到轮廓距离</summary>
    public static float RoundedRectSdf(float px, float py, float size, float radius)
    {
        float half = (size - 1) / 2f;
        float qx = Mathf.Abs(px - half) - (half - radius);
        float qy = Mathf.Abs(py - half) - (half - radius);
        float ox = Mathf.Max(qx, 0f);
        float oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    /// <summary>生成太极图（黑白双鱼，发送按钮）</summary>
    public static Texture2D MakeTaijiTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float c = (size - 1) / 2f;
        float R = c - 0.5f;          // 外圆半径
        float r = R / 2f;            // 鱼身小圆半径
        float eye = r / 2.6f;        // 鱼眼半径
        Color black = new Color(0.13f, 0.10f, 0.17f, 0.96f);
        Color white = new Color(0.93f, 0.89f, 0.98f, 0.96f);
        Color clear = new Color(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                if (dx * dx + dy * dy > R * R) { tex.SetPixel(x, y, clear); continue; }
                Color col = (x < c) ? black : white;   // 左黑右白
                // 右上白鱼（圆心 (c, c-r)），内含黑眼
                float d1x = x - c, d1y = y - (c - r);
                if (d1x * d1x + d1y * d1y <= r * r) col = white;
                if (d1x * d1x + d1y * d1y <= eye * eye) col = black;
                // 左下黑鱼（圆心 (c, c+r)），内含白眼
                float d2x = x - c, d2y = y - (c + r);
                if (d2x * d2x + d2y * d2y <= r * r) col = black;
                if (d2x * d2x + d2y * d2y <= eye * eye) col = white;
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成卦象三爻（☰ 形，三横线，标题栏金色装饰）</summary>
    public static Texture2D GenHexagramTex(int w, int h, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        int barH = Mathf.Max(h / 10, 1);      // 每爻粗细
        int gap = Mathf.Max((h - 3 * barH) / 2, 1); // 爻间缝隙
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);
        for (int i = 0; i < 3; i++)
        {
            int y0 = i * (barH + gap);
            for (int y = y0; y < y0 + barH; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    /// <summary>生成带细边的圆角气泡纹理（SDF 圆角矩形，内部渐变 + 边框色）</summary>
    public static Texture2D GenBubbleTex(int w, int h, float r, Color fillTop, Color fillBottom, Color border)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        float hw = (w - 1f) / 2f, hh = (h - 1f) / 2f;
        float rr = Mathf.Max(r - 1f, 0.5f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float qx = Mathf.Abs(x - hw) - (hw - rr);
                float qy = Mathf.Abs(y - hh) - (hh - rr);
                float ax = Mathf.Max(qx, 0f), ay = Mathf.Max(qy, 0f);
                float dist = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rr;
                if (dist > 0f) { tex.SetPixel(x, y, clear); continue; }
                float ty = y / (float)(h - 1);
                Color col = Color.Lerp(fillTop, fillBottom, ty);
                // 边框带：距边缘 2px 内渐变过渡到边框色
                if (dist > -2f)
                    col = Color.Lerp(border, col, Mathf.Clamp01((dist + 2f) / 1.2f));
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        return tex;
    }
}
