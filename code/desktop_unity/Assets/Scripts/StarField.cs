using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 星空背景系统 — RightPanel 拆分的自包含类（2026-08-14）
/// 原位于 RightPanel.cs（L2779-3006）的分层星点：银河斜带 5 星群 + 流星拖尾 + 呼吸微闪。
/// 由 RightPanel 持有实例并驱动：Init(seed) 一次性初始化 → Update 中每帧 UpdateStarMotion() → OnGUI 中 DrawStars(..., animAlpha)。
/// animAlpha 由外部传入（淡入淡出全局透明度），不直接依赖 RightPanel 内部状态。
/// 改星空视觉（星数/颜色/拖尾/速度）优先改这里。
/// </summary>
public class StarField
{
    // ——— 纹理（Init 时创建）———
    private Texture2D _starGlowTex;      // 大星光晕（36×36 径向渐变，中心白→边缘淡紫）
    private Texture2D _midGlowTex;       // 中星光晕（24×24）
    private Texture2D _starDotTex;       // 小星点（5×5 十字微光）
    private Texture2D _starCoreTex;      // 大星核心过曝白点（5×5，让大星有"亮芯"主次）

    // ——— 星点数据 ———
    private Vector4[] _bigStars;         // 大星：x,y(0~1 归一化),alpha,type(0白/1金/2紫)
    private Vector4[] _midStars;         // 中星：x,y(0~1 归一化),alpha,type
    private Vector4[] _smallStars;       // 小星：x,y(0~1 归一化),alpha,type
    private Vector2[] _bigVel;           // 大星漂移速度（归一化/帧，随机方向慢速）
    private Vector2[] _midVel;           // 中星漂移速度
    private Vector2[] _smallVel;         // 小星漂移速度
    private float[] _bigPhase;           // 大星波浪扰动相位
    private float[] _midPhase;           // 中星波浪扰动相位
    private float[] _smallPhase;         // 小星波浪扰动相位
    private List<Vector2>[] _bigTrail;   // 大星尾迹（index0=最新采样点）
    private List<Vector2>[] _midTrail;   // 中星尾迹
    private List<Vector2>[] _smallTrail; // 小星尾迹
    private float _trailTimer;            // 尾迹采样计时（时间累积，每 0.1s 推一个采样点，帧率无关）

    /// <summary>一次性初始化：创建星点纹理 + 生成分层星点（固定种子）</summary>
    public void Init(int seed)
    {
        _starGlowTex = UiTextureFactory.MakeStarGlowTex(36);
        _midGlowTex = UiTextureFactory.MakeStarGlowTex(24);
        _starDotTex = UiTextureFactory.MakeStarDotTex(5);
        _starCoreTex = UiTextureFactory.MakeTex(5, 5, Color.white);
        InitStarPositions(seed);
    }

    /// <summary>初始化分层星点（固定种子，银河带结构：5 星群沿右上→左下斜带聚集，其余真空；群内高斯散布+最小间距防堆叠）</summary>
    private void InitStarPositions(int seed)
    {
        var rng = new System.Random(seed);
        // 5 个星群中心：3 个沿银河斜带（右上→左下，GUI 坐标 y 向下）+ 2 个游离群，带 jitter
        //  右上角 = x 大 y 小；左下角 = x 小 y 大
        Vector2[] cc = new Vector2[5];
        float[][] baseC = new float[][]
        {
            new float[] { 0.82f, 0.16f },  // 带起点（右上）
            new float[] { 0.50f, 0.48f },  // 带中段
            new float[] { 0.18f, 0.80f },  // 带终点（左下）
            new float[] { 0.85f, 0.78f },  // 游离群1（右下）
            new float[] { 0.14f, 0.20f }   // 游离群2（左上）
        };
        for (int i = 0; i < 5; i++)
        {
            cc[i] = new Vector2(
                Mathf.Clamp01(baseC[i][0] + (float)rng.NextDouble() * 0.08f - 0.04f),
                Mathf.Clamp01(baseC[i][1] + (float)rng.NextDouble() * 0.08f - 0.04f));
        }

        // 大星：5 群 × 3~4 颗（σ≈0.035 紧团），群内互不重叠 minDist 0.03
        _bigStars = new Vector4[18];
        for (int i = 0; i < _bigStars.Length; i++)
        {
            Vector2 c = cc[i % 5];
            float x = 0.5f, y = 0.5f;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                x = Mathf.Clamp01(c.x + Gaussian(rng) * 0.035f);
                y = Mathf.Clamp01(c.y + Gaussian(rng) * 0.035f);
                if (!TooClose(x, y, _bigStars, i, 0.03f)) break;
            }
            _bigStars[i] = new Vector4(x, y, 0.75f + (float)rng.NextDouble() * 0.25f, rng.Next(3));
        }

        // 中星：5 群 × 4~5 颗（σ≈0.05），群内互不重叠 minDist 0.025
        _midStars = new Vector4[24];
        for (int i = 0; i < _midStars.Length; i++)
        {
            Vector2 c = cc[i % 5];
            float x = 0.5f, y = 0.5f;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                x = Mathf.Clamp01(c.x + Gaussian(rng) * 0.05f);
                y = Mathf.Clamp01(c.y + Gaussian(rng) * 0.05f);
                if (!TooClose(x, y, _midStars, i, 0.025f)) break;
            }
            _midStars[i] = new Vector4(x, y, 0.55f + (float)rng.NextDouble() * 0.35f, rng.Next(3));
        }

        // 小星：34 颗群内（σ≈0.06）+ 6 颗游离；群内防重叠 minDist 0.015
        _smallStars = new Vector4[40];
        for (int i = 0; i < _smallStars.Length; i++)
        {
            float x = (float)rng.NextDouble(), y = (float)rng.NextDouble();
            if (i < 34)
            {
                Vector2 c = cc[i % 5];
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    x = Mathf.Clamp01(c.x + Gaussian(rng) * 0.06f);
                    y = Mathf.Clamp01(c.y + Gaussian(rng) * 0.06f);
                    if (!TooClose(x, y, _smallStars, i, 0.015f)) break;
                }
            }
            _smallStars[i] = new Vector4(x, y, 0.35f + (float)rng.NextDouble() * 0.4f, rng.Next(3));
        }

        // ——— 运动与尾迹 ——— 只有前 5 颗大星是“流星”：统一沿银河带方向（右上→左下）斜飘+长拖尾；
        //  其余大星与全部中/小星静止（仅呼吸微闪），避免 82 条随机拖尾交叉成网（流星才拖尾原则）
        //  方向角 2.35rad≈135°：cos 负 sin 正 = 向左下移动（GUI y 向下），与银河带同向
        InitMotion(out _bigVel, out _bigPhase, out _bigTrail, _bigStars, 22, 5, 2.35f, 0.0022f, 0.0010f, rng);
        InitMotion(out _midVel, out _midPhase, out _midTrail, _midStars, 0, 0, 0f, 0f, 0f, rng);
        InitMotion(out _smallVel, out _smallPhase, out _smallTrail, _smallStars, 0, 0, 0f, 0f, 0f, rng);
        _trailTimer = 0f;
    }

    /// <summary>初始化一层星点的运动：前 meteorCount 颗为流星（统一方向 meteorDir + 微散角 + 慢速），其余静止；
    /// 每星带波浪相位与尾迹缓冲</summary>
    private static void InitMotion(out Vector2[] vels, out float[] phases, out List<Vector2>[] trails,
                                   Vector4[] stars, int cap, int meteorCount, float meteorDir,
                                   float speedMin, float speedVar, System.Random rng)
    {
        int n = stars.Length;
        vels = new Vector2[n];
        phases = new float[n];
        trails = new List<Vector2>[n];
        for (int i = 0; i < n; i++)
        {
            if (i < meteorCount)
            {
                float ang = meteorDir + (float)(rng.NextDouble() * 0.25 - 0.125);   // 统一方向+轻微散角
                float spd = speedMin + (float)rng.NextDouble() * speedVar;
                vels[i] = new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd);
            }
            else vels[i] = Vector2.zero;   // 静止星（无拖尾）
            phases[i] = (float)(rng.NextDouble() * Math.PI * 2.0);
            trails[i] = new List<Vector2>(cap + 2);
        }
    }

    /// <summary>检查 (x,y) 与 stars[0..count) 最小距离 ≥ minDist（防星点堆叠成芝麻团）</summary>
    private static bool TooClose(float x, float y, Vector4[] stars, int count, float minDist)
    {
        float m2 = minDist * minDist;
        for (int j = 0; j < count; j++)
        {
            float dx = stars[j].x - x, dy = stars[j].y - y;
            if (dx * dx + dy * dy < m2) return true;
        }
        return false;
    }

    /// <summary>标准正态随机（Box-Muller）</summary>
    private static float Gaussian(System.Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
    }

    /// <summary>绘制分层星点：大星 34px 光晕+7px 白芯（呼吸）+ 中星 24px（弱闪）+ 小星 5px 十字（暗）；每层带慢速漂移方块拖尾（优雅星尘）</summary>
    public void DrawStars(float px, float py, float pw, float ph, float animAlpha)
    {
        if (_starGlowTex == null || _bigStars == null) return;
        Color prev = GUI.color;
        float t = Time.time;
        Color[] tints = { Color.white, new Color(1f, 0.85f, 0.55f), new Color(0.85f, 0.70f, 1f) };
        // 大星：拖尾（4px 方块渐隐）→ 34px 光晕 + 7px 白芯（点状亮星），明显呼吸
        for (int i = 0; i < _bigStars.Length; i++)
        {
            Vector4 s = _bigStars[i];
            float sx = px + s.x * pw, sy = py + s.y * ph;
            Color tint = tints[(int)s.w];
            DrawTrail(_bigTrail, i, px, py, pw, ph, tint, s.z, 4f, animAlpha);
            float twinkle = 0.82f + 0.18f * Mathf.Sin(t * 1.5f + i * 1.7f);
            GUI.color = new Color(tint.r, tint.g, tint.b, s.z * twinkle * animAlpha);
            GUI.DrawTexture(new Rect(sx - 17f, sy - 17f, 34f, 34f), _starGlowTex);
            if (_starCoreTex != null)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(s.z * twinkle + 0.25f) * animAlpha);
                GUI.DrawTexture(new Rect(sx - 3.5f, sy - 3.5f, 7f, 7f), _starCoreTex);
            }
        }
        // 中星：拖尾（3px）→ 24px 光晕，弱闪
        if (_midGlowTex != null && _midStars != null)
        {
            for (int i = 0; i < _midStars.Length; i++)
            {
                Vector4 s = _midStars[i];
                float sx = px + s.x * pw, sy = py + s.y * ph;
                Color tint = tints[(int)s.w];
                DrawTrail(_midTrail, i, px, py, pw, ph, tint, s.z, 3f, animAlpha);
                float twinkle = 0.72f + 0.16f * Mathf.Sin(t * 2.1f + i * 2.3f);
                GUI.color = new Color(tint.r, tint.g, tint.b, s.z * twinkle * animAlpha);
                GUI.DrawTexture(new Rect(sx - 12f, sy - 12f, 24f, 24f), _midGlowTex);
            }
        }
        // 小星：拖尾（2px）→ 5px 十字（暗星，压低亮度制造主次）
        if (_starDotTex != null && _smallStars != null)
        {
            for (int i = 0; i < _smallStars.Length; i++)
            {
                Vector4 s = _smallStars[i];
                float sx = px + s.x * pw, sy = py + s.y * ph;
                Color tint = tints[(int)s.w];
                DrawTrail(_smallTrail, i, px, py, pw, ph, tint, s.z, 2f, animAlpha);
                float twinkle = 0.58f + 0.2f * Mathf.Sin(t * 2.3f + i * 0.9f);
                GUI.color = new Color(tint.r, tint.g, tint.b, s.z * twinkle * animAlpha);
                GUI.DrawTexture(new Rect(sx - 2.5f, sy - 2.5f, 5f, 5f), _starDotTex);
            }
        }
        GUI.color = prev;
    }

    /// <summary>星点运动：仅流星层采样尾迹；静止星跳过，越界回绕
    /// ★ 2026-08-16 修复1：位移乘以 deltaTime——否则固定帧步进，60fps 内嵌快 / 15fps 外置慢
    /// ★ 修复2：尾迹采样改为「时间累积」（每 ~0.1s 一个点），帧率无关——否则 60fps 渲染时
    ///   22 点尾迹只覆盖 0.37s（变短），15fps 时覆盖 1.5s（变长），与外置渲染节流组合后长度不稳定</summary>
    public void UpdateStarMotion()
    {
        if (_bigStars == null || _bigVel == null) return;
        float t = Time.time;
        float dt = Time.deltaTime;
        // 时间累积采样：每 ~0.033s 一个尾迹点（帧率无关）
        // ★ 间距 = 每秒位移 × 采样间隔 = 60·vx × 0.033 ≈ 2·vx，还原内嵌 60fps「每 2 帧一点」的自然连续拖尾
        _trailTimer += dt;
        bool sample = _trailTimer >= 0.033f;
        if (sample) _trailTimer = 0f;
        UpdateLayer(_bigStars, _bigVel, _bigPhase, _bigTrail, 22, 0.00042f, sample, dt);
        UpdateLayer(_midStars, _midVel, _midPhase, _midTrail, 0, 0.00046f, sample, dt);
        UpdateLayer(_smallStars, _smallVel, _smallPhase, _smallTrail, 0, 0.0005f, sample, dt);
    }

    private void UpdateLayer(Vector4[] stars, Vector2[] vels, float[] phases, List<Vector2>[] trails,
                             int cap, float wobble, bool sample, float dt)
    {
        if (stars == null || vels == null) return;
        float t = Time.time;
        // ★ 时间归一：速度常量按 60fps 基准设计（每帧位移），乘 dt*60 后任意帧率速度恒定
        float timeScale = dt * 60f;
        for (int i = 0; i < stars.Length; i++)
        {
            if (vels[i].sqrMagnitude < 1e-8f) continue;   // 静止星（非流星）不移动不采样
            Vector4 s = stars[i];
            Vector2 v = vels[i];
            float ph = phases[i];
            float vx = (v.x + Mathf.Sin(t * 0.5f + ph) * wobble) * timeScale;
            float vy = (v.y + Mathf.Cos(t * 0.45f + ph * 1.31f) * wobble) * timeScale;
            s.x = Mathf.Repeat(s.x + vx, 1f);
            s.y = Mathf.Repeat(s.y + vy, 1f);
            stars[i] = s;
            if (sample && trails != null && trails[i] != null)
            {
                trails[i].Insert(0, new Vector2(s.x, s.y));
                if (trails[i].Count > cap) trails[i].RemoveAt(trails[i].Count - 1);
            }
        }
    }

    /// <summary>绘制一层拖尾：旧→新小方块渐隐渐亮（方块粒子质感与星体一致）</summary>
    private void DrawTrail(List<Vector2>[] trails, int i, float px, float py, float pw, float ph,
                           Color tint, float baseA, float size, float animAlpha)
    {
        if (_starCoreTex == null || trails == null) return;
        var tr = trails[i];
        if (tr == null || tr.Count == 0) return;
        for (int k = tr.Count - 1; k >= 0; k--)   // 最旧 → 最新
        {
            float fade = 0.05f + 0.5f * (1f - (float)k / tr.Count);
            GUI.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(baseA * fade) * animAlpha);
            GUI.DrawTexture(new Rect(px + tr[k].x * pw - size * 0.5f, py + tr[k].y * ph - size * 0.5f, size, size), _starCoreTex);
        }
    }
}
