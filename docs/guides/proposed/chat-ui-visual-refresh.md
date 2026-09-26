# 聊天面板视觉刷新指导（proposed）

> **状态**: proposed（未批准实施；本分支仅落地 groundwork 与 opt-in 试点主题）
> **作用**: 记录 2026-09-27 对聊天面板皮肤现状的审计结论、审美失败的根因分析、令牌化派生架构与分阶段刷新计划；作为后续主题迁移与视觉验收的依据。
> **关联**: `docs/decisions/2026-09-24-product-experience-and-project-goals.md`（对话优雅感）、`docs/modules/chat-ui.md`、`docs/modules/live2d-rendering.md`。

## 一、现状审计（代码核对）

- 主题体系位于 `Assets/Scripts/HolidayThemeRuntime.cs`：`Theme`（19 个**位置参数**颜色 + Skin）+ `ThemeSkin`（约 50 个命名色字段），另有 `ResolveLegacyUiColor` 兼容旧蓝紫色绘制点。
- 存量主题：`default`（暗紫蓝）+ 5 个节日主题；其中 4 个由 `CreateFestivalSkin` 用 Lerp 派生，新春为手工全量。
- 主题清单在 3 处硬编码：`HolidayThemeRuntime` list 消息、`DeveloperCommandSet` 帮助、`RightPanel.SubPanels` 设置页。

**审美失败的根因**（从颜色数据推断，待截图复核）：
1. **多饱和强调色互相竞争**：单主题同时存在金边框 + 橙强调 + 红面板 + 紫提示 + 绿状态，无层级。
2. **边框与光晕 alpha 失控**：`PanelBorder`/`BubbleBorder` alpha 0.90–0.98（实心彩条），`PanelGlow` 0.28–0.32 叠加星云三层渐变——在透明桌面上叠成泥。
3. **文字对比不足**：`TextMuted/Dim/Time` 亮度 0.55–0.72 直接压在半透明深底上。
4. **纯饱和原色**（如烟花 (0.98, 0.12, 0.08)）不做色调管理。
5. **结构根因**：19 个位置参数构造器 + 50 个互不约束的手调色，任何一次改色都是盲改。

## 二、已落地的 groundwork（本分支）

1. **`ThemeComposer.cs`（新增）**：语义令牌 `ThemePalette`（10 个字段：三层表面、强调色对、三级文字、状态色对）→ 完整 `ThemeSkin` 派生。派生规则固化审美决策：
   - 单一强调色；边框一律 hairline（alpha ≤ 0.55）；光晕 ≤ 0.15；
   - 文字三级亮度阶梯；装饰/烟花走强调色去饱和 tint；
   - 斑马行用中性白 0.025 而非彩色。
2. **试点主题 `ink`（墨韵，opt-in）**：深靛墨面 + 鎏金强调，`/tell theme ink` 或 `墨韵` 激活；不改动 default 与节日主题（零回归）。设置页与 `/tell theme` 帮助已同步。
3. 验证：`build.ps1 -Quick` 编译通过（2026-09-27）。**视觉验收未做**——需真实 Player 截图 + 人工对比评审后才可讨论转正。

## 三、后续阶段（批准后）

- **P1 视觉验收 ink**：隔离数据根启动 → `@@sim:screenshot` 采集聊天/子面板截图 → 与 default 并排人工评审；通过后考虑设为可选推荐主题。
- **P2 default 主题令牌化**：用 Composer 重建 default（保持现有色相基调、修正 alpha/对比问题），同样走截图评审。
- **P3 节日主题迁移**：5 个节日主题迁移到 Composer 派生（`ThemePalette` + 每主题少量覆盖），走节日皮肤评审标准（`holiday-skin-review-standard.md`）。
- **P4 结构清理**：`Theme` 19 位置参数构造器标记过时；`ResolveLegacyUiColor` 的旧蓝紫映射随迁移逐步退役。

## 四、非目标

- 不改布局/交互结构（IMGUI 面板结构、像素化管线不动）；
- 不引入图片资源或运行时纹理（纯色/渐变令牌化）；
- 不动 Live2D 模型与节日像素配饰体系。

## 五、验收边界

任何主题变更必须：隔离数据根截图（聊天视图 + 设置页 + 气泡）→ 人工对比签字 →EditMode 通过 → 才可合并；"编译通过"不构成视觉变更的完成依据。
