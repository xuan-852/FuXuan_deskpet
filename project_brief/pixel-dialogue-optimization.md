# 17×24 像素符玄 × 对话界面优化 —— 开源方案可移植汇总

> 调研日期：2026-08-08
> 结论先行：本项目对话 UI 是**纯 IMGUI**（`OnGUI`/`GUI.DrawTexture`，无 UGUI、无 Prefab、无美术贴图管线），
> 全部视觉（圆角气泡、云纹、星点、CRT 扫描线、像素边框）都是运行时 `Texture2D.SetPixel` 程序生成。
> 这决定了"像素化"的移植思路：**不引入任何 UI 框架/Preafab 包，只做三件事——换字体、换头像纹理、加表情状态**。

---

## 一、现状盘点（对话界面代码）

### 1.1 两套对话 UI（全部 IMGUI）

| 组件 | 文件 | 说明 |
|---|---|---|
| 头顶悬浮气泡 | `Assets/Scripts/ChatBubble.cs` | 单条消息，跟随桌宠世界坐标，圆角+尾巴+云纹+星点，**无头像** |
| 终端聊天窗口 | `Assets/Scripts/RightPanel.cs` | "符玄@太卜司" 终端窗，QQ 式左右气泡列表 + 输入框，`~`/`F2`/`\` 打开 |
| 对话核心 | `Assets/Scripts/ChatManager.cs` | `Entry{role,content}` 历史，`SplitSentences` 逐句 + `OnSentenceChanged` 事件 |
| 气泡驱动 | `Assets/Scripts/AutoChat.cs` | `OnNewReply`/`OnSentenceChanged`/`OnRequestError` → 驱动气泡逐句播放 |

### 1.2 像素头像现状（RightPanel 内三处）

现有 `_pixelFxTex` 由 `LoadPixelFx()`（L1159）加载：**优先 `Resources/PixelFuXuan.png`（高清立绘），回退代码生成 16×16 像素小人**。

| 位置 | 行号 | 尺寸 | 现状 |
|---|---|---|---|
| 标题栏 | L339 | 30×30 + 黑方块描边 | `GUI.DrawTexture(_pixelFxTex)` |
| 消息列表（符玄气泡旁） | L509 | `avatarSize`（约 24） | `GUI.DrawTexture(_pixelFxTex)` |
| 输入框最左 | L553 | 56×56 + 黑方块描边 | `GUI.DrawTexture(_pixelFxTex)` |

**关键缺陷**：现在是"高清立绘平滑显示"（`FilterMode.Bilinear`），和窗口的像素边框/CRT 扫描线**风格割裂**；三处都是正方形裁切，17×24 的竖长比例放进去会拉伸/裁掉。

### 1.3 现有像素基建（可复用，勿重复造）

- `DrawPixelRect()`（L1000）：2px 紫硬边 + 四角加粗 —— 像素边框已就位
- `GenPixelFx(scale)`（L1173）：代码生成像素小人（`FilterMode.Point`）
- `_scanlineTex`、`_titleBarPixelTex`、`_inputBarPixelTex`：CRT/像素渐变
- 头像资源：`Resources/PixelFuXuan*.png` 系列 + 8 张 `candidate_texture_*.png`

---

## 二、17×24 是什么 & 定位建议

17×24 是经典**像素角色立绘帧**尺寸（GBA 角色 / Shimeji 角色 / Undertale 风格），宽高比 ≈ 0.71（瘦高站立半身）。

**建议定位**：
1. **主用途 → 对话头像**（消息列表 / 输入框 / 头顶气泡的小立绘）——正好是半身像比例，×2/×3 整数倍放大后锐利
2. **次用途 → 桌宠本体**（替代/并列 Live2D）——17×24 太小，需 ×5~×6 放大，且与 Live2D 骨骼动画是两种美术，建议**先做对话头像，本体切换做成可选项**
3. **强烈建议同时补 2~4 帧**：同姿势的「眨眼」「说话口型」「点头」，与单帧共用同一精灵表，表情差分就靠它

---

## 三、开源方案调研汇总（按可移植性排序）

### A. 像素字体 —— 对话文本质感的决定性一步 ⭐⭐⭐

| 方案 | 仓库 | 规格 | 许可 | 移植成本 |
|---|---|---|---|---|
| **缝合像素字体 Fusion Pixel** | `github.com/TakWolf/fusion-pixel-font`（3k★） | 8/10/12px 三档；**等宽/比例两模式**；泛中日韩（简繁日韩全覆盖） | 字体 OFL-1.1，构建程序 MIT | **极低**：OTF 放入 `Resources`，`GUIStyle.font` 赋值即可 |
| **最像素 Zpix** | `github.com/SolidZORO/zpix-pixel-font` | 12px 中文（含日文假名），单字号 | OFL-1.1 | 极低，同上 |
| **方舟像素 Ark Pixel**（Fusion 上游） | `github.com/TakWolf/ark-pixel-font` | 10/12px | OFL-1.1 | 极低 |
| **缝合粗体 Fusion Bold** | `github.com/pixel-font-studio/fusion-bold-pixel-font` | 算法粗体，适合标题/气泡强调 | OFL-1.1 | 极低 |

要点：
- **OFL-1.1 = 可免费商用 + 可嵌入游戏**，唯一义务是随包保留许可证文本
- 项目 GUI 用 `GUIStyle`（非 TMP），动态字体直接赋 `font` 即可；**务必用整数倍 fontSize**（12/24/36），配合 IMGUI 屏幕像素对齐，天然锐利
- 推荐主选 **Fusion Pixel 12px 等宽模式**（与终端排版 `_monoFont` 一致），比例模式做气泡正文

### B. 像素精灵制作工具（把 17×24 扩成多帧精灵表）

| 方案 | 说明 | 许可 |
|---|---|---|
| **Aseprite** | 像素动画业界标准：精灵表、洋葱皮、调色板、预览 | 商业（Steam） |
| **LibreSprite** | Aseprite 开源分支，功能接近 | **GPLv2（工具软件，不链接进游戏，无传染）** |
| 导出格式 | 单张 `17*24*N` 横向/纵向精灵表 PNG + `FilterMode.Point` | — |

### C. 像素渲染/放大原则 ⭐

- **铁律**：`17×24` 只能 **×2/×3/×4 整数倍** 放大（34×48 / 51×72 / 68×96），`FilterMode.Point` 最近邻，**绝不平滑插值**
- **Unity 官方 2D Pixel Perfect**（`com.unity.2d.pixel-perfect`）：相机/画面像素对齐。本项目 IMGUI 在屏幕空间天然像素对齐，**非必需**，仅未来若做"像素模式本体"时参考
- **xBRZ / HQ4X 放大算法**（社区开源实现）：单帧小图智能放大保留轮廓。仅当放大倍率 ≥5 且嫌锯齿时启用，聊胜于无
- **像素描边**：1px 深色描边可用程序扫描实现（遍历邻域非透明像素上色），替换现在 RightPanel 的"黑方块底衬"（L341/L556），精致一个量级

### D. 对话/叙事引擎（可选：本地彩蛋对话、离线小剧场）⭐⭐

| 方案 | 仓库 | 许可 | 移植评估 |
|---|---|---|---|
| **ink / ink-unity-integration** | `github.com/inkle/ink`（4.9k★） | MIT | **最合适**：引擎是纯 C# DLL（`ink-engine-runtime`），只当"对话状态机"用，**UI 完全自绘（我们的 OnGUI 不用动）**。适合管理"戳戳额头→哼""摸摸头→谢谢"这类本地脚本对话，替代 hardcode |
| **Yarn Spinner** | `github.com/YarnSpinnerTool/YarnSpinner-Unity`（2.8k★） | MIT | 功能强（剧本式、分支、变量），但 Unity 版**强依赖 UGUI/TextMeshPro 组件**，与本项目纯 IMGUI 架构冲突，**不建议** |
| **Fungus** | `github.com/snozbot/fungus` | 开源 | 可视化流程图对话，Unity 4 时代经典，维护放缓，同样偏 UGUI |

### E. 桌宠行为参考（像素状态机灵感）⭐

- **Shimeji**（经典开源桌宠，Java；Windows 分支 `github.com/kilkakon/shimeji-windows`）：大量 **16~24px 小像素精灵**，行为状态机（闲逛/拖拽/群聚/扔到桌面边缘）——像素符玄未来做"本体模式"时直接抄它的状态划分
- 项目内已有驱动事件可接：`IdleChatGenerator`（闲话）、`ProactiveMessageScheduler`（主动关心）、`ServerPollService`（推送）、`ChatManager.OnRequestStarted/OnRequestError/OnNewReply`

### F. 像素 UI 素材包（**本项目不需要**，仅记录）

itch.io 大量 CC0 像素 UI 包（对话框/按钮/边框）。但本项目气泡/边框是**程序生成且风格已统一**，引入外部 9-slice 贴图反而破坏一致性，跳过。

---

## 四、落地清单（按成本排序，全部可移植进现有 IMGUI）

### P0 —— 纯代码，半天内（不动资产管线）

1. **新增 `Resources/PixelFuXuan_17x24.png`**，改 `LoadPixelFx()`（RightPanel L1159）：优先加载它，`FilterMode.Point`；
   现有 `PixelFuXuan.png` 高清立绘降级为 fallback
2. **三处头像改为整数倍绘制**：
   - L339 标题栏 30×30 → 用 17×24（×1），`Rect` 居中于 30×30 区域
   - L509 消息列表 → 17×24（×2 = 34×48）或 ×1，`avatarSize` 改为竖长
   - L553 输入框 56px → 17×24（×2 = 34×48），垂直居中
3. **像素描边替换黑底衬**：写 `GenOutline(texture, color, 1)` 程序描边，替换 L341/L556 的 `DrawPixelRect` 黑方块

### P1 —— 质感跃升（1 天）

4. **Fusion Pixel 12px 进 `Resources`**：`_termTitleStyle`/`_termLogStyle`/气泡 `GUIStyle` 全部赋 `font`，`fontSize` 用 12/24 整数倍；气泡/输入框高度按新字高微调
5. **表情差分**：补 2~4 帧（普通/开心/困惑/思考中），接入：
   - `ChatManager.OnRequestStarted` → 思考中表情（输入框/标题栏头像）
   - `OnNewReply` → 开心表情
   - `OnRequestError` / `AutoChat.IsConfusedReply` → 困惑表情
6. **头顶气泡加小符玄**：`ChatBubble.OnGUI` 在气泡尾巴下方画 17×24（×1）小立绘，说话时 `sin` 呼吸浮动（y ±2px）

### P2 —— 可选增强（以后再说）

7. **程序化微动动画**：单帧也能活 —— Update 里正弦浮动/2° 倾斜；眨眼用两帧程序生成（在 `GenPixelFx` 基础上做）
8. **ink 接入本地对话**：`戳戳额头/摸摸头/晚安` 等脚本对话迁移到 `.ink` 文件，`ink-engine-runtime.dll` 当状态机，渲染仍走 ChatBubble/RightPanel（D 节方案）
9. **像素本体模式**：17×24 做桌宠本体（×5 放大 + Shimeji 式状态机），Live2D/像素可切换 —— 需先评估 Live2D 隐藏时的骨架（`Live2DRenderer.verticalOffset` 等）

---

## 五、许可与合规提示

| 项 | 说明 |
|---|---|
| Fusion Pixel / Zpix / Ark / Fusion Bold | OFL-1.1：可免费商用、可嵌入游戏；**随包保留 OFL 许可证文本**（放 `Assets/Resources/Licenses/`） |
| LibreSprite | GPLv2，仅作**编辑工具**使用，产物（PNG）不受传染 |
| ink | MIT，可随意 |
| 17×24 像素符玄 | 角色属《崩坏：星穹铁道》版权（米哈游），**仅限个人自用/学习**；若做同人分发注意合规；AI 生成的像素画建议人工修一遍避免版权瑕疵 |
| 本报告 | 供内部决策，无外部依赖 |

---

## 六、一句话结论

> 17×24 像素符玄的**最佳切入点是对话头像 + 表情差分**（P0+P1），配合 **Fusion Pixel 12px 字体**，
> 一个下午即可让"终端窗 + 头顶气泡"完成像素化统一；不动 UI 框架、不动资产管线，纯 IMGUI 内即可闭环。
