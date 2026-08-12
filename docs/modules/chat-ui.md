# 对话界面 UI — IMGUI 界面与像素化优化

> **文档作用**: 本模块文档描述桌宠「交互界面」子系统的**代码真相**——纯 IMGUI 架构（无 UGUI/无 Prefab）、四类界面元素（悬浮球/BallPanel/RightPanel/ChatBubble）、对话核心事件链，以及 17×24 像素符玄 × 对话界面的开源方案可移植汇总（换字体/换头像/加表情差分三件事）。改任何 UI 相关代码前必读。
> **基本架构**: 全部界面为 **IMGUI**（`OnGUI`/`GUI.DrawTexture`，无 UGUI、无 Prefab、无美术贴图管线），视觉元素（圆角气泡/云纹/星点/CRT 扫描线/像素边框）均运行时 `Texture2D.SetPixel` 程序生成。核心：`RightPanel.cs`（终端窗）、`ChatBubble.cs`（头顶气泡）、`ChatManager.cs`（Entry 历史 + SplitSentences 逐句事件）、`AutoChat.cs`（气泡驱动）。
> **开发历史迭代**: 2026-08-08 像素化调研（Fusion Pixel 字体/17×24 精灵表/ink 对话引擎）；P0 落地清单（半天）：17×24 头像 + FilterMode.Point + 整数倍绘制 + 程序描边；P1（1 天）：Fusion Pixel 12px + 表情差分 + 气泡小立绘；P2 可选：程序化微动/ink/像素本体模式。
> **编写注意事项**: ①不引入 UI 框架/UGUI/Preafab（架构铁则，ink 除外但 UI 仍自绘）；②像素图只能 ×2/×3/×4 **整数倍**放大 + `FilterMode.Point`，绝不平滑插值；③动态字体 fontSize 用 12/24/36 整数倍；④像素符玄角色版权属米哈游，仅个人自用/学习；⑤OFL-1.1 字体需随包保留许可证文本（`Assets/Resources/Licenses/`）。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及对话窗口、头顶气泡、输入栏、悬浮球、菜单面板、像素化视觉的改动。
- **回答的问题**:
  - 界面组件有哪些？各自在哪？什么尺寸？
  - 消息流怎么从 ChatManager 到气泡的？
  - 像素化改造的完整落地清单是什么？成本多少？
  - 引入外部素材的许可约束是什么？
- **关联文档**: `code-truth-architecture.md`（UI 表现层）｜`modules/ai-chat-system.md`（对话核心事件）｜`modules/live2d-rendering.md`（像素模式与本体的关系）｜`modules/action-agent.md`（言出法随触发 UI）

## 二、基本架构

### 2.1 界面元素清单

| 元素 | 触发 | 尺寸 | 功能 |
|------|------|------|------|
| 悬浮球 | 右下角粉✦ | 36×36 | 展开辐射菜单 |
| BallPanel | 点击悬浮球 | 420×580px | 设置/报告/便签 |
| RightPanel | `~`键 / 划过右边缘 | 220px | 聊/设/签/告 + 输入栏 |
| ChatBubble | AI 回复 / 提醒 / 闲话 | 自适应 | 手绘圆角 + 12 星点 |
| 输入栏 | 自动显示 | 固定坐标 | Windows 搜索风格 |

**消息优先级**：High(AI 回复) > Normal(提醒/交互) > Low(闲话/问候)

### 2.2 对话核心事件链

| 组件 | 文件 | 说明 |
|------|------|------|
| 头顶悬浮气泡 | `ChatBubble.cs` | 单条消息，跟随桌宠世界坐标，圆角+尾巴+云纹+星点，无头像 |
| 终端聊天窗口 | `RightPanel.cs` | "符玄@太卜司" 终端窗，QQ 式左右气泡列表 + 输入框 |
| 对话核心 | `ChatManager.cs` | `Entry{role,content}` 历史，`SplitSentences` 逐句 + `OnSentenceChanged` 事件 |
| 气泡驱动 | `AutoChat.cs` | `OnNewReply`/`OnSentenceChanged`/`OnRequestError` → 驱动气泡逐句播放 |

### 2.3 像素头像现状（RightPanel 内三处）

现有 `_pixelFxTex` 由 `LoadPixelFx()`（L1159）加载：优先 `Resources/PixelFuXuan.png`（高清立绘），回退代码生成 16×16 像素小人。

| 位置 | 行号 | 尺寸 | 现状 |
|------|------|------|------|
| 标题栏 | L339 | 30×30 + 黑方块描边 | `GUI.DrawTexture(_pixelFxTex)` |
| 消息列表（符玄气泡旁） | L509 | avatarSize（约 24） | `GUI.DrawTexture(_pixelFxTex)` |
| 输入框最左 | L553 | 56×56 + 黑方块描边 | `GUI.DrawTexture(_pixelFxTex)` |

**关键缺陷**：高清立绘平滑显示（`FilterMode.Bilinear`）与窗口像素边框/CRT 扫描线风格割裂；三处正方形裁切与 17×24 竖长比例冲突。

### 2.4 现有像素基建（可复用，勿重复造）

- `DrawPixelRect()`（L1000）：2px 紫硬边 + 四角加粗
- `GenPixelFx(scale)`（L1173）：代码生成像素小人（`FilterMode.Point`）
- `_scanlineTex`、`_titleBarPixelTex`、`_inputBarPixelTex`：CRT/像素渐变
- 头像资源：`Resources/PixelFuXuan*.png` 系列 + 8 张 `candidate_texture_*.png`

### 2.5 17×24 像素角色定位

宽高比 ≈ 0.71（瘦高站立半身），GBA 角色 / Shimeji / Undertale 风格。
- **主用途** → 对话头像（消息列表/输入框/头顶气泡小立绘），×2/×3 整数倍放大锐利
- **次用途** → 桌宠本体（需 ×5~×6 放大，且与 Live2D 是两种美术，建议先做头像，本体切换做成可选项）
- 建议补 2~4 帧：眨眼/说话口型/点头，共用同一精灵表

## 三、开发历史迭代

| 版本 | 日期 | 变更 |
|------|------|------|
| — | — | 纯 IMGUI 界面 + 程序生成视觉（圆角/云纹/星点/CRT） |
| N40 | 2026-08-08 | 17×24 像素化调研完成（`pixel-dialogue-optimization.md`）：开源方案汇总 + P0/P1/P2 落地清单 |

### 像素化落地清单（按成本排序，2026-08-08）

| 阶段 | 内容 | 工作量 |
|------|------|--------|
| **P0** | ①新增 `Resources/PixelFuXuan_17x24.png`（LoadPixelFx 优先加载 + FilterMode.Point）②三处头像整数倍绘制（标题栏 ×1 居中 / 消息列表 ×2 / 输入框 ×2）③程序描边 `GenOutline` 替换黑方块底衬 | 半天 |
| **P1** | ④Fusion Pixel 12px 进 Resources（GUIStyle 赋 font，fontSize 12/24 整数倍）⑤表情差分 2~4 帧（OnRequestStarted→思考中 / OnNewReply→开心 / OnRequestError→困惑）⑥头顶气泡加小符玄（sin 呼吸浮动 y ±2px） | 1 天 |
| **P2** | ⑦程序化微动动画（正弦浮动/2° 倾斜/两帧眨眼）⑧ink 接入本地对话（`戳戳额头`等，UI 仍自绘）⑨像素本体模式（×5 放大 + Shimeji 式状态机） | 以后再说 |

### 开源方案选型（可移植性排序）

| 方案 | 选型 | 理由 |
|------|------|------|
| 像素字体 | **Fusion Pixel 12px 等宽**（`TakWolf/fusion-pixel-font`，OFL-1.1） | 泛中日韩、等宽/比例双模式、OTF 放 Resources 即用 |
| 精灵表工具 | Aseprite / LibreSprite（GPLv2 仅作编辑工具） | 导出 17×24*N 精灵表 + FilterMode.Point |
| 放大算法 | xBRZ / HQ4X（仅 ≥5 倍且嫌锯齿时） | 聊胜于无 |
| 对话引擎 | **ink / ink-unity-integration**（MIT） | 纯 C# DLL 当对话状态机，UI 完全自绘（OnGUI 不用动） |
| 桌宠状态机参考 | Shimeji（kilkakon/shimeji-windows） | 闲逛/拖拽/群聚状态划分可抄 |
| 像素 UI 包 | **跳过** | 气泡/边框程序生成且风格已统一，外部贴图破坏一致性 |

## 四、编写注意事项

1. **架构铁则**：本项目是**纯 IMGUI**（OnGUI/GUI.DrawTexture），**不引入 UGUI/Preafab/UI 框架**；ink 是例外（只当对话状态机，UI 仍自绘），Yarn Spinner/Fungus 强依赖 UGUI **不用**
2. **像素放大铁律**：17×24 只能 ×2/×3/×4 **整数倍**放大（34×48 / 51×72 / 68×96）+ `FilterMode.Point` 最近邻，**绝不平滑插值**（Bilinear = 糊）
3. **字体整数倍**：动态字体 `GUIStyle.fontSize` 用 12/24/36 整数倍，配合 IMGUI 屏幕像素对齐天然锐利；推荐 Fusion Pixel 12px 等宽（与 `_monoFont` 终端排版一致）
4. **许可合规**：Fusion Pixel/Zpix/Ark/Fusion Bold 为 OFL-1.1（可免费商用、可嵌入游戏，**必须随包保留 OFL 许可证文本**放 `Assets/Resources/Licenses/`）；17×24 像素符玄角色版权属米哈游（**仅限个人自用/学习**，同人分发注意合规）；AI 生成像素画建议人工修一遍
5. **头像三处引用**：`LoadPixelFx()`（RightPanel L1159）是三处头像唯一入口，改头像资源只动这里；`GenPixelFx`/`DrawPixelRect` 是已有基建，勿重复造
6. **测试模式**：UI 自动化测试须开 `.test_mode`，且注意 `LogAssert.Expect` 声明预期日志（Unity 把 Error/Warning 计为失败）
7. **消息优先级**：新增 UI 弹出类型时遵循 High(AI 回复) > Normal(提醒/交互) > Low(闲话/问候)，防止闲话打断重要回复
