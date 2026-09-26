using UnityEngine;

/// <summary>
/// 语义设计令牌 → 完整 ThemeSkin 派生器（2026-09-27 聊天面板视觉刷新 groundwork）。
///
/// 现有主题的审美问题源于手工调 ~50 个互不约束的颜色：多饱和强调色互相竞争、
/// 边框/光晕 alpha 高达 0.9+、文字层级对比不足。本派生器把这些决策固化成规则：
///   - 每个主题只声明 10 个语义令牌，其余全部派生，保证层级一致；
///   - 单一强调色 + 低饱和辅助色，禁止第二强调色；
///   - 边框一律 hairline（alpha ≤ 0.55），光晕 alpha ≤ 0.15，取代大面积高透光晕；
///   - 文字三级亮度阶梯（主/次/弱），正文对比按深底亮字校准；
///   - 装饰与烟花用强调色的去饱和 tint，不再使用纯饱和原色。
///
/// 新主题一律经本派生器创建；存量 5 个节日主题迁移另行评审（指导文档见
/// docs/guides/proposed/chat-ui-visual-refresh.md）。
/// </summary>
public sealed class ThemePalette
{
    public string Id;
    public string DisplayName;
    /// <summary>最深表面：面板底部、弹窗、输入条底。</summary>
    public Color SurfaceDeep;
    /// <summary>主表面：面板主体。</summary>
    public Color Surface;
    /// <summary>浮起表面：输入框、头像底。</summary>
    public Color SurfaceRaised;
    /// <summary>唯一强调色（如鎏金）。</summary>
    public Color Accent;
    /// <summary>强调色低饱和版：边框、描边、装饰。</summary>
    public Color AccentSoft;
    public Color TextPrimary;
    public Color TextSecondary;
    public Color TextTertiary;
    public Color Success;
    public Color Warning;
}

public static class ThemeComposer
{
    /// <summary>墨韵：深靛墨面 + 鎏金强调，符玄占卜者审美的克制版。</summary>
    public static readonly ThemePalette Ink = new ThemePalette
    {
        Id = "ink",
        DisplayName = "墨韵",
        SurfaceDeep = new Color(0.049f, 0.055f, 0.082f),
        Surface = new Color(0.086f, 0.094f, 0.133f),
        SurfaceRaised = new Color(0.114f, 0.125f, 0.180f),
        Accent = new Color(0.867f, 0.729f, 0.416f),
        AccentSoft = new Color(0.718f, 0.651f, 0.494f),
        TextPrimary = new Color(0.929f, 0.922f, 0.894f),
        TextSecondary = new Color(0.659f, 0.655f, 0.624f),
        TextTertiary = new Color(0.463f, 0.463f, 0.502f),
        Success = new Color(0.435f, 0.682f, 0.494f),
        Warning = new Color(0.878f, 0.541f, 0.353f),
    };

    public static HolidayThemeRuntime.ThemeSkin Compose(ThemePalette p)
    {
        Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);
        Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, t);
        HolidayThemeRuntime.ThemeSkin s = new HolidayThemeRuntime.ThemeSkin();

        // 表面层：不透明哑光，去掉彩色 glow 堆叠
        s.PanelTop = WithA(p.Surface, 0.97f);
        s.PanelBottom = WithA(p.SurfaceDeep, 0.97f);
        s.PanelGlow = WithA(p.Accent, 0.10f);
        s.PanelBorder = WithA(p.AccentSoft, 0.38f);
        s.NebulaA = WithA(Lerp(p.Surface, p.Accent, 0.10f), 0.80f);
        s.NebulaB = WithA(Lerp(p.Surface, p.AccentSoft, 0.06f), 0.80f);
        s.NebulaC = WithA(Lerp(p.SurfaceDeep, p.AccentSoft, 0.04f), 0.80f);

        // 标题栏：融入面板，顶部一条 hairline 提示而不做三段渐变
        s.TitleBar = WithA(p.SurfaceDeep, 0.96f);
        s.TitleTop = WithA(p.AccentSoft, 0.20f);
        s.TitleMid = s.TitleBar;
        s.TitleBottom = WithA(p.SurfaceDeep, 0.98f);

        // 输入区
        s.InputBackground = WithA(p.SurfaceRaised, 0.94f);
        s.InputHover = WithA(Lerp(p.SurfaceRaised, p.Accent, 0.10f), 0.96f);
        s.InputGlow = WithA(p.Accent, 0.14f);
        s.InputBarBackground = WithA(p.SurfaceDeep, 0.82f);

        // 气泡：AI = 面向表面，用户 = 向强调色微移；边框都走 hairline
        s.BubbleFxTop = WithA(Lerp(p.Surface, p.Accent, 0.10f), 0.97f);
        s.BubbleFxBottom = WithA(Lerp(p.SurfaceDeep, p.Accent, 0.05f), 0.97f);
        s.BubbleFxBorder = WithA(p.AccentSoft, 0.45f);
        s.BubbleUserTop = WithA(Lerp(p.SurfaceRaised, p.Accent, 0.16f), 0.97f);
        s.BubbleUserBottom = WithA(Lerp(p.Surface, p.Accent, 0.10f), 0.97f);
        s.BubbleUserBorder = WithA(p.AccentSoft, 0.50f);

        // 强调与文字阶梯
        s.Accent = p.Accent;
        s.AccentHover = Lerp(p.Accent, Color.white, 0.18f);
        s.TextTitle = p.Accent;
        s.TextMain = p.TextPrimary;
        s.TextMuted = WithA(p.TextSecondary, 0.95f);
        s.TextDim = WithA(p.TextTertiary, 0.95f);
        s.TextPlaceholder = WithA(p.TextTertiary, 0.85f);
        s.TextUser = p.TextPrimary;
        s.TextPrompt = p.Accent;
        s.TextTooltip = WithA(p.TextPrimary, 0.95f);
        s.TextStatus = p.TextSecondary;
        s.TextTime = WithA(p.TextTertiary, 0.90f);

        // 杂项表面
        s.AvatarBackground = WithA(Lerp(p.Surface, p.Accent, 0.14f), 0.95f);
        s.AvatarText = p.Accent;
        s.BorderPixel = WithA(p.AccentSoft, 0.55f);
        s.LogRowAlt = new Color(1f, 1f, 1f, 0.025f);

        // 装饰：一律去饱和 tint
        s.DecorationPrimary = p.AccentSoft;
        s.DecorationSecondary = Lerp(p.AccentSoft, p.TextTertiary, 0.35f);
        s.DecorationGold = p.Accent;

        // 状态色
        s.StatusReady = p.Success;
        s.StatusBusy = p.Accent;
        s.StatusTask = Lerp(p.Accent, p.Warning, 0.35f);
        s.Warning = p.Warning;
        s.ModalSurface = WithA(p.SurfaceDeep, 0.92f);

        // 星尘/烟花/太极装饰（节日层）：跟随强调色去饱和
        s.StarTintA = p.TextPrimary;
        s.StarTintB = p.Accent;
        s.StarTintC = Lerp(p.Accent, p.TextSecondary, 0.40f);
        s.StarEdge = WithA(p.Accent, 0f);
        s.FireworkPrimary = p.AccentSoft;
        s.FireworkSecondary = Lerp(p.AccentSoft, p.TextTertiary, 0.30f);
        s.FireworkSpark = Lerp(p.Accent, Color.white, 0.30f);
        s.TaijiDark = WithA(p.SurfaceDeep, 0.96f);
        s.TaijiLight = WithA(p.TextPrimary, 0.96f);
        return s;
    }
}
