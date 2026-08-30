using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 像素符玄与聊天 UI 共用的节日主题运行时状态。
///
/// 边界：这里只服务像素形象、聊天面板和聊天气泡；不进入 Live2D 参数或渲染链路。
/// 主题切换只修改内存状态，纹理由消费者在 Revision 变化时重建并缓存。
/// </summary>
public static class HolidayThemeRuntime
{
    public sealed class Theme
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string PixelAccessoryId;
        public readonly Color PanelTop;
        public readonly Color PanelBottom;
        public readonly Color PanelGlow;
        public readonly Color PanelBorder;
        public readonly Color TitleTop;
        public readonly Color TitleMid;
        public readonly Color TitleBottom;
        public readonly Color InputBackground;
        public readonly Color InputHover;
        public readonly Color BubbleFxTop;
        public readonly Color BubbleFxBottom;
        public readonly Color BubbleFxBorder;
        public readonly Color BubbleUserTop;
        public readonly Color BubbleUserBottom;
        public readonly Color BubbleUserBorder;
        public readonly Color Accent;
        public readonly Color AccentHover;
        public readonly Color TextTitle;
        public readonly Color TextMain;

        public bool IsDefault => Id == "default";

        public Theme(string id, string displayName, string pixelAccessoryId,
            Color panelTop, Color panelBottom, Color panelGlow, Color panelBorder,
            Color titleTop, Color titleMid, Color titleBottom, Color inputBackground, Color inputHover,
            Color bubbleFxTop, Color bubbleFxBottom, Color bubbleFxBorder,
            Color bubbleUserTop, Color bubbleUserBottom, Color bubbleUserBorder,
            Color accent, Color accentHover, Color textTitle, Color textMain)
        {
            Id = id;
            DisplayName = displayName;
            PixelAccessoryId = pixelAccessoryId;
            PanelTop = panelTop;
            PanelBottom = panelBottom;
            PanelGlow = panelGlow;
            PanelBorder = panelBorder;
            TitleTop = titleTop;
            TitleMid = titleMid;
            TitleBottom = titleBottom;
            InputBackground = inputBackground;
            InputHover = inputHover;
            BubbleFxTop = bubbleFxTop;
            BubbleFxBottom = bubbleFxBottom;
            BubbleFxBorder = bubbleFxBorder;
            BubbleUserTop = bubbleUserTop;
            BubbleUserBottom = bubbleUserBottom;
            BubbleUserBorder = bubbleUserBorder;
            Accent = accent;
            AccentHover = accentHover;
            TextTitle = textTitle;
            TextMain = textMain;
        }
    }

    private static readonly Theme DefaultTheme = new Theme(
        "default", "默认主题", "",
        new Color(0.10f, 0.15f, 0.32f, 0.95f),
        new Color(0.04f, 0.07f, 0.16f, 0.95f),
        new Color(0.35f, 0.42f, 0.80f, 0.32f),
        new Color(0.55f, 0.65f, 1.00f, 0.90f),
        new Color(0.32f, 0.24f, 0.44f, 0.55f),
        new Color(0.15f, 0.11f, 0.22f, 0.95f),
        new Color(0.08f, 0.06f, 0.13f, 0.95f),
        new Color(0.16f, 0.13f, 0.27f, 0.92f),
        new Color(0.23f, 0.18f, 0.37f, 0.96f),
        new Color(0.45f, 0.33f, 0.62f, 0.96f),
        new Color(0.30f, 0.20f, 0.46f, 0.96f),
        new Color(0.88f, 0.78f, 0.55f, 0.95f),
        new Color(0.24f, 0.42f, 0.60f, 0.96f),
        new Color(0.14f, 0.28f, 0.44f, 0.96f),
        new Color(0.55f, 0.72f, 0.95f, 0.90f),
        new Color(0.55f, 0.40f, 0.85f, 1.00f),
        new Color(0.60f, 0.45f, 0.90f, 1.00f),
        new Color(0.90f, 0.80f, 0.58f, 1.00f),
        new Color(0.92f, 0.90f, 0.96f, 1.00f));

    private static readonly Theme ChineseNewYearTheme = new Theme(
        "cn_new_year", "新春主题", "cn_new_year_cap",
        new Color(0.30f, 0.08f, 0.14f, 0.97f),
        new Color(0.09f, 0.025f, 0.055f, 0.97f),
        new Color(0.78f, 0.16f, 0.12f, 0.28f),
        new Color(0.95f, 0.62f, 0.24f, 0.95f),
        new Color(0.58f, 0.15f, 0.12f, 0.72f),
        new Color(0.28f, 0.055f, 0.08f, 0.96f),
        new Color(0.12f, 0.018f, 0.035f, 0.98f),
        new Color(0.27f, 0.055f, 0.08f, 0.94f),
        new Color(0.43f, 0.10f, 0.12f, 0.96f),
        new Color(0.60f, 0.11f, 0.13f, 0.97f),
        new Color(0.36f, 0.045f, 0.07f, 0.97f),
        new Color(0.98f, 0.67f, 0.26f, 0.98f),
        new Color(0.78f, 0.22f, 0.17f, 0.96f),
        new Color(0.95f, 0.55f, 0.20f, 0.95f),
        new Color(0.48f, 0.12f, 0.12f, 0.96f),
        new Color(0.95f, 0.50f, 0.22f, 1.00f),
        new Color(1.00f, 0.68f, 0.28f, 1.00f),
        new Color(1.00f, 0.84f, 0.52f, 1.00f),
        new Color(0.98f, 0.92f, 0.82f, 1.00f));

    private static readonly Dictionary<string, Theme> Themes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase)
    {
        { "default", DefaultTheme },
        { "cn_new_year", ChineseNewYearTheme },
        { "spring_festival", ChineseNewYearTheme },
        { "cny", ChineseNewYearTheme }
    };

    private static bool _initialized;
    private static string _activeId = "default";
    private static int _revision;
    private static bool _manualOverride;

    public static int Revision { get { EnsureInitialized(); return _revision; } }
    public static string ActiveId { get { EnsureInitialized(); return _activeId; } }
    public static Theme Active { get { EnsureInitialized(); return Themes[_activeId]; } }
    public static bool IsHolidayActive { get { return !Active.IsDefault; } }

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        _manualOverride = false;
        SetActive(ResolveAutomaticTheme(DateTime.Now));
    }

    public static bool TrySetTheme(string requested, out string message)
    {
        EnsureInitialized();
        string normalized = (requested ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = GetStatus();
            return true;
        }

        if (normalized.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            message = "可用节日主题: default, cn_new_year, auto";
            return true;
        }

        if (normalized.Equals("off", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("none", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            _manualOverride = true;
            SetActive("default");
            message = GetStatus();
            return true;
        }

        if (normalized.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            _manualOverride = false;
            SetActive(ResolveAutomaticTheme(DateTime.Now));
            message = GetStatus();
            return true;
        }

        Theme theme;
        if (!Themes.TryGetValue(normalized, out theme))
        {
            message = "未知节日主题: " + normalized + "（可用: default, cn_new_year, auto）";
            return false;
        }

        _manualOverride = true;
        SetActive(theme.Id);
        message = GetStatus();
        return true;
    }

    public static string GetStatus()
    {
        EnsureInitialized();
        string mode = _manualOverride ? "手动" : "自动";
        return "当前节日主题: " + Active.DisplayName + " (" + Active.Id + ", " + mode + ")";
    }

    /// <summary>
    /// 自动日期表的第一版采用可维护的公历窗口；农历日期不要散落在 UI 代码里。
    /// 需要更精确的农历适配时，只改这里或迁移到配置文件，不影响绘制层。
    /// </summary>
    private static string ResolveAutomaticTheme(DateTime now)
    {
        DateTime start = new DateTime(now.Year, 1, 20);
        DateTime end = new DateTime(now.Year, 2, 20);
        return now >= start && now <= end ? "cn_new_year" : "default";
    }

    private static void SetActive(string id)
    {
        if (!Themes.ContainsKey(id)) id = "default";
        if (_activeId == id && _revision > 0) return;
        _activeId = id;
        _revision++;
        Debug.Log("[HolidayTheme] " + GetStatus());
    }

    /// <summary>将节日配件直接绘制到 17×24 基础像素帧上；透明像素不覆盖角色。</summary>
    public static void ApplyPixelAccessory(Color32[] pixels, int width, int height)
    {
        EnsureInitialized();
        if (pixels == null || width <= 0 || height <= 0 || Active.IsDefault) return;
        if (!string.Equals(Active.PixelAccessoryId, "cn_new_year_cap", StringComparison.OrdinalIgnoreCase)) return;

        Color32 outline = new Color32(105, 22, 38, 255);
        Color32 red = new Color32(210, 40, 62, 255);
        Color32 lightRed = new Color32(248, 74, 78, 255);
        Color32 gold = new Color32(255, 202, 70, 255);

        // 17×24 的帽子轮廓：顶部绒球、红色帽冠、金色帽檐和短流苏。
        Put(pixels, width, height, 8, 23, gold);
        Put(pixels, width, height, 7, 22, outline); Put(pixels, width, height, 8, 22, red); Put(pixels, width, height, 9, 22, outline);
        FillRow(pixels, width, height, 5, 11, 21, outline);
        FillRow(pixels, width, height, 6, 10, 21, red);
        FillRow(pixels, width, height, 4, 12, 20, outline);
        FillRow(pixels, width, height, 5, 11, 20, lightRed);
        FillRow(pixels, width, height, 3, 13, 19, outline);
        FillRow(pixels, width, height, 4, 12, 19, gold);
        Put(pixels, width, height, 14, 18, gold);
        Put(pixels, width, height, 14, 17, gold);
    }

    private static void FillRow(Color32[] pixels, int width, int height, int fromX, int toX, int y, Color32 color)
    {
        for (int x = fromX; x <= toX; x++) Put(pixels, width, height, x, y, color);
    }

    private static void Put(Color32[] pixels, int width, int height, int x, int y, Color32 color)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        pixels[y * width + x] = color;
    }
}
