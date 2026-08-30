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
    /// <summary>
    /// 一个主题必须完整描述 UI 皮肤，而不是只覆盖几处颜色。
    /// RightPanel/ChatBubble 只能从这里取视觉色，避免默认紫色纹理在节日主题中残留。
    /// </summary>
    public sealed class ThemeSkin
    {
        public Color PanelTop, PanelBottom, PanelGlow, PanelBorder;
        public Color NebulaA, NebulaB, NebulaC;
        public Color TitleBar, TitleTop, TitleMid, TitleBottom;
        public Color InputBackground, InputHover, InputGlow;
        public Color BubbleFxTop, BubbleFxBottom, BubbleFxBorder;
        public Color BubbleUserTop, BubbleUserBottom, BubbleUserBorder;
        public Color Accent, AccentHover;
        public Color TextTitle, TextMain, TextMuted, TextDim, TextPlaceholder;
        public Color TextUser, TextPrompt, TextTooltip, TextStatus, TextTime;
        public Color AvatarBackground, AvatarText, InputBarBackground;
        public Color BorderPixel, LogRowAlt;
        public Color DecorationPrimary, DecorationSecondary, DecorationGold;
        public Color StatusReady, StatusBusy, StatusTask, Warning, ModalSurface;
        public Color StarTintA, StarTintB, StarTintC, StarEdge;
        public Color TaijiDark, TaijiLight;
    }

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
        public readonly ThemeSkin Skin;

        public bool IsDefault => Id == "default";

        public Theme(string id, string displayName, string pixelAccessoryId,
            Color panelTop, Color panelBottom, Color panelGlow, Color panelBorder,
            Color titleTop, Color titleMid, Color titleBottom, Color inputBackground, Color inputHover,
            Color bubbleFxTop, Color bubbleFxBottom, Color bubbleFxBorder,
            Color bubbleUserTop, Color bubbleUserBottom, Color bubbleUserBorder,
            Color accent, Color accentHover, Color textTitle, Color textMain, ThemeSkin skin)
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
            Skin = skin;
        }
    }

    private static readonly ThemeSkin DefaultSkin = new ThemeSkin
    {
        PanelTop = new Color(0.10f, 0.15f, 0.32f, 0.95f), PanelBottom = new Color(0.04f, 0.07f, 0.16f, 0.95f),
        PanelGlow = new Color(0.35f, 0.42f, 0.80f, 0.32f), PanelBorder = new Color(0.55f, 0.65f, 1.00f, 0.90f),
        NebulaA = new Color(0.30f, 0.45f, 0.85f, 1f), NebulaB = new Color(0.52f, 0.36f, 0.74f, 1f), NebulaC = new Color(0.68f, 0.50f, 0.80f, 1f),
        TitleBar = new Color(0.11f, 0.09f, 0.15f, 0.92f),
        TitleTop = new Color(0.32f, 0.24f, 0.44f, 0.55f), TitleMid = new Color(0.15f, 0.11f, 0.22f, 0.95f), TitleBottom = new Color(0.08f, 0.06f, 0.13f, 0.95f),
        InputBackground = new Color(0.16f, 0.13f, 0.27f, 0.92f), InputHover = new Color(0.23f, 0.18f, 0.37f, 0.96f), InputGlow = new Color(0.48f, 0.36f, 0.72f, 0.42f),
        BubbleFxTop = new Color(0.45f, 0.33f, 0.62f, 0.96f), BubbleFxBottom = new Color(0.30f, 0.20f, 0.46f, 0.96f), BubbleFxBorder = new Color(0.88f, 0.78f, 0.55f, 0.95f),
        BubbleUserTop = new Color(0.24f, 0.42f, 0.60f, 0.96f), BubbleUserBottom = new Color(0.14f, 0.28f, 0.44f, 0.96f), BubbleUserBorder = new Color(0.55f, 0.72f, 0.95f, 0.90f),
        Accent = new Color(0.55f, 0.40f, 0.85f, 1f), AccentHover = new Color(0.60f, 0.45f, 0.90f, 1f),
        TextTitle = new Color(0.90f, 0.80f, 0.58f, 1f), TextMain = new Color(0.92f, 0.90f, 0.96f, 1f),
        TextMuted = new Color(0.58f, 0.55f, 0.65f, 0.90f), TextDim = new Color(0.55f, 0.54f, 0.60f, 0.90f), TextPlaceholder = new Color(0.55f, 0.52f, 0.62f, 0.85f),
        TextUser = new Color(0.80f, 0.90f, 0.98f, 1f), TextPrompt = new Color(0.62f, 0.48f, 0.95f, 1f), TextTooltip = new Color(0.88f, 0.83f, 0.98f, 1f), TextStatus = new Color(0.66f, 0.62f, 0.76f, 0.90f), TextTime = new Color(0.58f, 0.55f, 0.65f, 0.90f),
        AvatarBackground = new Color(0.30f, 0.24f, 0.45f, 0.95f), AvatarText = new Color(0.85f, 0.80f, 0.98f, 1f), InputBarBackground = new Color(0.09f, 0.08f, 0.13f, 0.78f),
        BorderPixel = new Color(0.58f, 0.42f, 0.88f, 0.90f), LogRowAlt = new Color(0.14f, 0.10f, 0.22f, 0.35f),
        DecorationPrimary = new Color(0.55f, 0.40f, 0.85f, 1f), DecorationSecondary = new Color(0.50f, 0.35f, 0.80f, 1f), DecorationGold = new Color(0.92f, 0.82f, 0.56f, 0.92f),
        StatusReady = new Color(0.45f, 0.85f, 0.55f, 1f), StatusBusy = new Color(0.72f, 0.55f, 0.95f, 1f), StatusTask = new Color(0.95f, 0.78f, 0.40f, 1f), Warning = new Color(0.85f, 0.35f, 0.35f, 1f), ModalSurface = new Color(0.10f, 0.06f, 0.16f, 0.85f),
        StarTintA = Color.white, StarTintB = new Color(1f, 0.85f, 0.55f), StarTintC = new Color(0.85f, 0.70f, 1f), StarEdge = new Color(0.80f, 0.70f, 1f, 0f),
        TaijiDark = new Color(0.13f, 0.10f, 0.17f, 0.96f), TaijiLight = new Color(0.93f, 0.89f, 0.98f, 0.96f)
    };

    private static readonly ThemeSkin ChineseNewYearSkin = new ThemeSkin
    {
        PanelTop = new Color(0.30f, 0.08f, 0.14f, 0.97f), PanelBottom = new Color(0.09f, 0.025f, 0.055f, 0.97f),
        PanelGlow = new Color(0.78f, 0.16f, 0.12f, 0.28f), PanelBorder = new Color(0.95f, 0.62f, 0.24f, 0.95f),
        NebulaA = new Color(0.62f, 0.12f, 0.12f, 1f), NebulaB = new Color(0.76f, 0.22f, 0.10f, 1f), NebulaC = new Color(0.58f, 0.08f, 0.16f, 1f),
        TitleBar = new Color(0.28f, 0.055f, 0.08f, 0.96f),
        TitleTop = new Color(0.58f, 0.15f, 0.12f, 0.72f), TitleMid = new Color(0.28f, 0.055f, 0.08f, 0.96f), TitleBottom = new Color(0.12f, 0.018f, 0.035f, 0.98f),
        InputBackground = new Color(0.27f, 0.055f, 0.08f, 0.94f), InputHover = new Color(0.43f, 0.10f, 0.12f, 0.96f), InputGlow = new Color(0.95f, 0.55f, 0.20f, 0.48f),
        BubbleFxTop = new Color(0.60f, 0.11f, 0.13f, 0.97f), BubbleFxBottom = new Color(0.36f, 0.045f, 0.07f, 0.97f), BubbleFxBorder = new Color(0.98f, 0.67f, 0.26f, 0.98f),
        BubbleUserTop = new Color(0.48f, 0.12f, 0.12f, 0.96f), BubbleUserBottom = new Color(0.30f, 0.045f, 0.06f, 0.96f), BubbleUserBorder = new Color(0.95f, 0.50f, 0.22f, 0.95f),
        Accent = new Color(0.95f, 0.50f, 0.22f, 1f), AccentHover = new Color(1.00f, 0.68f, 0.28f, 1f),
        TextTitle = new Color(1.00f, 0.84f, 0.52f, 1f), TextMain = new Color(0.98f, 0.92f, 0.82f, 1f),
        TextMuted = new Color(0.95f, 0.70f, 0.48f, 0.90f), TextDim = new Color(0.72f, 0.42f, 0.36f, 0.90f), TextPlaceholder = new Color(0.82f, 0.55f, 0.48f, 0.88f),
        TextUser = new Color(1.00f, 0.88f, 0.72f, 1f), TextPrompt = new Color(1.00f, 0.68f, 0.28f, 1f), TextTooltip = new Color(1.00f, 0.84f, 0.62f, 1f), TextStatus = new Color(0.95f, 0.70f, 0.48f, 0.90f), TextTime = new Color(0.90f, 0.60f, 0.46f, 0.90f),
        AvatarBackground = new Color(0.48f, 0.12f, 0.12f, 0.95f), AvatarText = new Color(1.00f, 0.84f, 0.52f, 1f), InputBarBackground = new Color(0.20f, 0.035f, 0.05f, 0.86f),
        BorderPixel = new Color(0.95f, 0.50f, 0.22f, 0.90f), LogRowAlt = new Color(0.28f, 0.045f, 0.07f, 0.38f),
        DecorationPrimary = new Color(0.95f, 0.50f, 0.22f, 1f), DecorationSecondary = new Color(0.78f, 0.22f, 0.17f, 1f), DecorationGold = new Color(1.00f, 0.84f, 0.52f, 0.98f),
        StatusReady = new Color(0.55f, 0.88f, 0.48f, 1f), StatusBusy = new Color(1.00f, 0.68f, 0.28f, 1f), StatusTask = new Color(1.00f, 0.84f, 0.52f, 1f), Warning = new Color(1.00f, 0.35f, 0.22f, 1f), ModalSurface = new Color(0.20f, 0.035f, 0.05f, 0.88f),
        StarTintA = Color.white, StarTintB = new Color(1f, 0.84f, 0.52f), StarTintC = new Color(1f, 0.55f, 0.28f), StarEdge = new Color(1f, 0.72f, 0.28f, 0f),
        TaijiDark = new Color(0.28f, 0.04f, 0.06f, 0.96f), TaijiLight = new Color(1.00f, 0.80f, 0.42f, 0.96f)
    };

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
        new Color(0.92f, 0.90f, 0.96f, 1.00f), DefaultSkin);

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
        new Color(0.98f, 0.92f, 0.82f, 1.00f), ChineseNewYearSkin);

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

    /// <summary>
    /// 兼容尚未迁移到 ThemeSkin 的旧 UI 绘制点。
    /// 仅在非默认主题下把旧蓝/紫色映射到当前主题的对应层级；新的代码仍应直接使用 Skin 字段。
    /// </summary>
    public static Color ResolveLegacyUiColor(Color color)
    {
        EnsureInitialized();
        if (Active.IsDefault) return color;
        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        // Color.clear 的 RGB 都是 0，不能被当成紫色映射，否则透明 GUIStyle 背景会变成实体色块。
        bool bluePurple = max > 0.08f && color.b >= color.r * 1.08f && color.b >= color.g * 1.03f;
        if (!bluePurple) return color;

        Color mapped = max < 0.18f ? Active.Skin.ModalSurface
            : (max > 0.72f ? Active.Skin.AccentHover : Active.Skin.DecorationSecondary);
        return new Color(mapped.r, mapped.g, mapped.b, color.a);
    }

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

    /// <summary>创建独立透明节日配饰层；默认主题返回全透明层。</summary>
    public static Color32[] CreatePixelAccessoryLayer(int width, int height)
    {
        Color32[] layer = new Color32[Mathf.Max(0, width * height)];
        EnsureInitialized();
        if (width <= 0 || height <= 0 || Active.IsDefault) return layer;
        if (!string.Equals(Active.PixelAccessoryId, "cn_new_year_cap", StringComparison.OrdinalIgnoreCase)) return layer;

        Color32 outline = ToColor32(Active.Skin.DecorationSecondary, 0.82f);
        Color32 red = ToColor32(Active.Skin.BubbleFxTop, 1f);
        Color32 lightRed = ToColor32(Active.Skin.Accent, 1f);
        Color32 gold = ToColor32(Active.Skin.DecorationGold, 1f);

        // 17×24 的帽子轮廓：顶部绒球、红色帽冠、金色帽檐和短流苏。
        Put(layer, width, height, 8, 23, gold);
        Put(layer, width, height, 7, 22, outline); Put(layer, width, height, 8, 22, red); Put(layer, width, height, 9, 22, outline);
        FillRow(layer, width, height, 5, 11, 21, outline);
        FillRow(layer, width, height, 6, 10, 21, red);
        FillRow(layer, width, height, 4, 12, 20, outline);
        FillRow(layer, width, height, 5, 11, 20, lightRed);
        FillRow(layer, width, height, 3, 13, 19, outline);
        FillRow(layer, width, height, 4, 12, 19, gold);
        Put(layer, width, height, 14, 18, gold);
        Put(layer, width, height, 14, 17, gold);
        return layer;
    }

    /// <summary>将基础像素帧与独立配饰层合成；不会修改调用方传入的基础帧。</summary>
    public static Color32[] ComposePixelFrame(Color32[] basePixels, int width, int height)
    {
        if (basePixels == null || width <= 0 || height <= 0 || basePixels.Length < width * height) return basePixels;
        Color32[] result = new Color32[basePixels.Length];
        Array.Copy(basePixels, result, basePixels.Length);
        Color32[] accessory = CreatePixelAccessoryLayer(width, height);
        for (int i = 0; i < width * height; i++)
            if (accessory[i].a > 0) result[i] = accessory[i];
        return result;
    }

    /// <summary>兼容旧调用：将独立配饰合成回目标数组。</summary>
    public static void ApplyPixelAccessory(Color32[] pixels, int width, int height)
    {
        Color32[] composed = ComposePixelFrame(pixels, width, height);
        if (composed == null || pixels == null) return;
        Array.Copy(composed, pixels, pixels.Length);
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

    private static Color32 ToColor32(Color color, float alpha)
    {
        return new Color32((byte)(Mathf.Clamp01(color.r) * 255f), (byte)(Mathf.Clamp01(color.g) * 255f),
            (byte)(Mathf.Clamp01(color.b) * 255f), (byte)(Mathf.Clamp01(alpha) * 255f));
    }
}
