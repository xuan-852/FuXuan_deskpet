# 对话界面 UI — IMGUI 界面与像素化优化

> **文档作用**: 本模块文档描述桌宠「交互界面」子系统的**代码真相**——纯 IMGUI 架构（无 UGUI/无 Prefab）、四类界面元素（悬浮球/BallPanel/RightPanel/ChatBubble）、对话核心事件链，以及 17×24 像素符玄 × 对话界面的开源方案可移植汇总（换字体/换头像/加表情差分三件事）。改任何 UI 相关代码前必读。
> **基本架构**: 全部界面为 **IMGUI**（`OnGUI`/`GUI.DrawTexture`，无 UGUI、无 Prefab、无美术贴图管线），视觉元素（圆角气泡/云纹/星点/CRT 扫描线/像素边框）均运行时 `Texture2D.SetPixel` 程序生成。核心：`RightPanel.cs`（终端窗）、`ChatBubble.cs`（头顶气泡）、`ChatManager.cs`（Entry 历史 + SplitSentences 逐句事件）、`AutoChat.cs`（气泡驱动）。2026-08-12 起 RightPanel 支持 OpenClaw 任务进度可视化：标题栏状态区（思考中部位）步骤显示 + 模态审批弹窗（todo 5/6）。
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

**OpenClaw 任务进度显示**（2026-08-12，方案七 todo 5/6）：

| 元素 | 位置 | 状态 | 视觉 |
|------|------|------|------|
| 任务步骤 | RightPanel 标题栏状态区（思考中部位，L471-489 附近） | 任务中 | 金色呼吸 `⚙ 第n步: tool summary`（Color(0.95,0.78,0.40)）；状态优先级 **任务步骤 > 思考中 > 就绪** |
| 步骤日志 | 日志区系统行（kind=2） | 新步骤 | `[openclaw] 第n步: tool summary` 灰字追加 |
| 审批弹窗 | OnGUI 末尾模态（最上层） | 待审批 | 全面板 62% 黑色遮罩 + 居中红边（0.85,0.35,0.35）弹窗：命令高亮 + 60s 倒计时自动拒绝 + 三按钮「✓ 允许一次 / ↻ 总是允许 / ✕ 拒绝」 |

> 数据流：`OpenClawBridge` 后台轮询 `RefreshTaskProgress` 写静态原子属性 → RightPanel `Update` 第 4c 步 `CheckOpenClawTaskProgress()`（新步骤写日志、新审批开弹窗 + `_approvalShownAt` 计时、60s 超时 `AutoDenyApproval`）→ 按钮调 `ResolveApproval(decision)`（用 `ActiveTaskId`→`LastTaskId` 兜底）→ `ApproveTaskAsync` POST 回执。任务结束自动关弹窗、清 `PendingApproval`。

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

### 2.6 QQ 式两级界面（会话列表 ⇄ 聊天，2026-08-13）

**视图模型**：`PanelView` 枚举（`SessionList`=第一级窄条会话列表 / `Chat`=第二级展开）。热键打开**默认落 SessionList**（`Toggle()` 强制），双击会话条目进 `Chat`，◀ 返回收窄，✕ 关闭。

**尺寸常量**（QQ Win32 实测 324×846 窄条为基准，**边长 ×1.5** 放大）：

| 常量 | 值 | 含义 |
|------|-----|------|
| `SESSION_LIST_W/H` | 486 × 1269 | 第一级窄条（324×1.5 / 846×1.5） |
| `CHAT_PANEL_W/H` | 1290 × 1269 | 第二级展开（420 + 870） |
| `SIDEBAR_W` | 420 | 第二级左侧会话栏（280×1.5） |
| `MIN_PANEL_W` | 300 | 拖拽最小宽度下限 |

**视图分发**（OnGUI 顶部，RightPanel L652-660）：SessionList 直接 `DrawSessionListView` 并 return；Chat 先 `DrawSessionSidebar(px,py,SIDEBAR_W,ph,mp)` 画左会话栏，**然后 `px += SIDEBAR_W; pw -= SIDEBAR_W`** 把聊天区整体右移 420px（⚠️ 第二级拖拽必须意识到 px 已偏移，见下方拖拽修复）。

**布局细节**：第一级标题栏 76px（头像 48 + 状态 + 时间 + ✕）+ 搜索胶囊 48px + 会话列表（滚动 itemH=96, av=60）+ 底部工具 50px；第二级左栏标题 30px、itemH=84、av=54、单击切换 `_activeSession`；聊天标题栏 titleH=54（◀ backRect 34×34、头像 42、状态/卦象、字体按钮 40×34、✕ 32+）。

**字体 QQ 化**：Microsoft YaHei（`_monoFont`），标题 19 / 状态 17 / 时间 17 / 工具按钮 18 / 日志 18 / 气泡 17（padding 12,12,10,10）/ 输入 18 / 提示 20。字体档位按钮（A→A2→A3→A4，`CycleFontScale`）在标题栏时间左侧。

**窗口尺寸切换**：`ApplyViewSize()` 按视图切宽（486⇄1290），**左上角保持**，超界自动收拢（`Mathf.Min` 夹到屏内），打 `视图切换 → {视图}，窗口={w}x{h} @ ({x},{y})` 日志（自动化验证靠它读窗口位置）。

**★ 拖拽实现与坑（2026-08-13 修复验证）**：

| 位置 | 代码 | 说明 |
|------|------|------|
| 第一级标题栏 | `_dragOffset = mp - new Vector2(px, py)` | px/py 即窗口原点（未偏移），正确 |
| 第二级标题栏 | `_dragOffset = mp - new Vector2(_panelRect.x, _panelRect.y)` | ⚠️ **必须用窗口原点**——此处 px 已被 `+= SIDEBAR_W` 右移 420px，若用 `new Vector2(px, py)` 会把 420px 混入偏移，窗口固定在离鼠标点击点 420px 处拖动（用户报的「固定在离边框多远的地方」bug 根因） |
| 防误触 | `!closeRect.Contains(mp) && !backRect.Contains(mp) && !fontBtnRect.Contains(mp)` | 拖标题栏时排除 ✕ / ◀ 返回 / 字体按钮 |
| 防吸鼠标 | MouseUp 复位 `_isDragging=false; _isResizing=false`（两个视图都要）+ Update 中 `!Input.GetMouseButton(0)` 强制结束 | 漏复位会导致窗口一直吸在鼠标上 |

拖拽更新在 `Update`（L317-336）：`newPos = mp - _dragOffset`，`Mathf.Clamp` 到屏内后写 `_panelRect.x/y`。**验证**：Chat @ (1037,166) 拖标题栏 (1300,190)→(1200,290)，`_dragOffset=(263,24)`，终点精确落 (937,266)（与理论值一致，跟手无偏移）。

### 2.7 设置/便签/报告页内子面板 + 淡入淡出 + 工具提示（2026-08-13）

**子面板页内化**：设置/便签/报告不再开独立的灰色 BallPanel 窗口，而是在 RightPanel 对话框内以 860×900 子面板视图呈现（`SUB_PANEL_W/H`）。新增 `PanelView.Settings / Reminders / Report` 三个视图 + `IsSubPanelView()` 判断，由 `OpenSubPanel(BallPanel.PanelType)` 打开（记录 `_prevView` 供 ◀ 返回），`BackFromSubPanel()` 返回来源视图。BallPanel.cs 保留仅用于 `DragHandler` 兼容与 `PanelType` 枚举。

**三个子面板内容**：- **设置（DrawSettingsSubPanel）**：⚙任务权重 5 行（`DrawWeightRowGui`，读写 `DesktopPet` 的 taskWeight 字段，✓应用权重即时生效）+ 📦预设（好动 3,3,3,3,1 / 均衡 2,2,2,2,2 / 安静 1,1,1,1,6）+ 💾持久化（💿保存配置 / 🗑清空忆境，走 PetConfig/PetMemory）；
- **报告（DrawReportSubPanel）**：🔄刷新 / 📋复制 + `MotionMemoryManager.Instance.GetStatistics()` 统计展示（try/catch 兜底空数据）；
- **便签（DrawRemindersSubPanel）**：✚新建（文本 + 时间输入）/ 🔄刷新 / ✅已完成⇄⏳看待办切换 / 列表项 MarkDone / DeleteReminder。
- **消耗（DrawUsageSubPanel，2026-08-15）**：💰 Token 统计——近 1 小时 + 累计两个口径（调用次数/输入输出 tokens/缓存命中率/估算费用）。数据源 `UsageStats.cs`（内存累计，`ApiClient.ExtractUsageSummary` 每次带 usage 的响应自动 `Record`，价格常量 DeepSeek 非高峰价 ¥2/0.5/3 每 M 可调）；本地 Ollama 不计入（免费）。测试命令 `@@view:usage`。

**淡入淡出**：`_isOpen / _closing / _animAlpha / _panelTint`（每帧 `GUI.color = _panelTint` 施加全局透明度），`FADE_SPEED=5f`；Update 中推进 alpha，`_closing && _animAlpha<=0.001f` 时隐藏面板；`Toggle()` 第二次按下取消淡出，`Close()` 置 `_closing=true`。**坑**：① 绘制星星/拖尾等自设颜色的代码必须显式 `* _animAlpha`（它们覆盖 `GUI.color`）；② 任何 `GUI.color` 赋值后须在分支结束/OnGUI 末尾恢复 `Color.white`，否则全局淡入淡出失效。

**工具提示（hover tooltip）**：鼠标悬停工具行「设/签/告/耗」按钮时右侧浮出说明文字（工具按钮行 `toolY = py + ph - 76`，btnRect 高 50，实际 y≈1276-1326）。

**★ 终端测试链路（铁律 §6.6）**：UI 自动化不依赖模拟鼠标点击。`CheckTestInbox()` 在测试模式（`D:\DesktopPetData\.test_mode` 存在）下每 0.25s 轮询 `D:\DesktopPetData\inbox.txt`：

| 命令 | 效果 |
|------|------|
| `@@view:settings\|reminders\|report\|usage` | 打开对应页内子面板（usage=Token 消耗统计） |
| `@@view:chat` | 切聊天视图（无会话时建默认会话） |
| `@@view:list` | 切回会话列表 |
| `@@view:back` | 子面板 ◀ 返回来源视图 |
| `@@view:open\|close` | 打开 / 淡出关闭面板 |
| `@@view:external\|embed` | 进入 / 退出独立聊天窗口（等价标题栏 ⧉ 按钮） |
| `@@emote:xxx` | 注入表情（不走 LLM） |
| 其他文本 | 作为用户消息发送（走 LLM） |

命令处理在 `HandleTestViewCommand()`（未知命令 `Debug.LogWarning` 列出支持列表），命令执行留痕 `[TestInbox] @@view 命令: xxx` 于 Player.log。**新增 UI 视图/按钮时必须在命令表中补等价命令。**

### 2.9 独立聊天窗口（2026-08-15，大工程 Phase 1）— QQ 式可被遮挡

**背景**：用户要求聊天窗口「不需要始终置顶，按 QQ 那样可被其他窗口遮挡」，而桌宠本体（透明窗）保持置顶不变。方案：**原生 Win32 窗口**承载聊天，与桌宠窗口解耦。

**架构**（`ExternalChatWindow.cs`，311 行，静态类 + 后台 STA 线程）：

| 环节 | 实现 | 说明 |
|------|------|------|
| 窗口 | `CreateWindowExW` + `RegisterClassW`（**非置顶** `WS_OVERLAPPEDWINDOW`，无 `WS_EX_TOPMOST`） | 可拖动/可遮挡/可最小化，标题「符玄 · 对话」 |
| 线程 | 后台线程 `FuXuanChatWindow`（STA + `GetMessageW` 消息循环） | 不阻塞 Unity 主线程 |
| 渲染桥 | Unity 主线程 `DrawExternalChatToTexture()`：IMGUI → RenderTexture → `ReadPixels` BGRA → `SetBuffer` → 窗口线程 `WM_PAINT` 里 `SetDIBitsToDevice`（15fps 节流） | 聊天历史实时显示；`ValidateRect` 防 WM_PAINT 风暴 |
| 输入 | **原生 EDIT 控件**（IDC_EDIT=101）+ 发送按钮（IDC_SEND=102）；Enter/按钮 → `DoSend` → `MainThreadDispatcher.Run` → 主线程 `OnSendText` → `ChatManager.SendMessage` | 不注入 IMGUI 键盘事件，输入可靠 |
| 关闭 | ✕（`WM_CLOSE`）= 隐藏窗口 + 主线程 `OnClosed` → `DisableExternalMode` 退回内嵌 | 窗口生命周期归 Unity 管 |

**状态切换**：标题栏 ⧉ 按钮 / `@@view:external` → `EnableExternalMode()`（订阅事件 + `Show(640, 480+44)`）；`@@view:embed` / ✕ → `DisableExternalMode()`（退订 + `Hide()`）。外部模式激活时 OnGUI 聊天分支改画到 RenderTexture（`_externalRender` 抑制屏幕事件处理，防幻影点击），屏幕不再画聊天。

**★ 已踩的坑（真机验证）**：

1. `GetModuleHandleW` 在 **kernel32.dll**（不是 user32.dll）→ `EntryPointNotFoundException`；
2. `RegisterClassExW` 收 `WNDCLASSEX`（首字段 cbSize），传 `WNDCLASS` 结构会失败 → 改用 `RegisterClassW`；
3. `PAINTSTRUCT` 含 `System.Drawing.Rectangle` 不可封送（`BeginPaint` 包装层 NRE）→ 改用 `GetDC`/`ReleaseDC` + `ValidateRect` 直接画；
4. **`Show()` 必须先赋值尺寸再 `EnsureCreated()`**——窗口线程按当时的 `_width/_height` 建窗，后赋值会丢输入栏 44px 高度（640×480 而非 640×524）；
5. `GetWindowTextW`/`SetWindowTextW` 必须 `CharSet.Unicode`，否则中文经 ANSI 封送变乱码（真机发送中文验证发现）；
6. `MainThreadDispatcher` 必须随 `DesktopPet` 自动挂载（`DesktopPet.cs` 加了 `AddComponent<MainThreadDispatcher>`），否则窗口线程回调进队列没人排空，发送静默丢失。

**验证标准**（`scripts/test/runtime_smoke.cjs` 已含 `@@view:external/embed` 链路 + 3 个独立窗口标记）：窗口可见且 `exStyle` 无 0x8（非置顶）；桌宠主窗 `UnityWndClass` 仍带 `WS_EX_TOPMOST`；中文输入端到端不乱码；✕ 关闭后隐藏。手动验证用 Win32 枚举（`EnumWindows` 按类名 `FuXuanChatWindowClass` 找窗 + `GetDlgItem` 拿 EDIT/BUTTON + `SendMessageW WM_SETTEXT`/`WM_COMMAND` 注入），不移动真实鼠标。



### 2.8 RightPanel 拆分（2026-08-14，文件 3520 → 1666 行）

`RightPanel.cs` 曾为 3,520 行全项目第二大文件，按职责拆分为 **1 主文件 + 3 分部文件**（全部保持行为逐字节不变，EditMode 78/78 验证通过）：

| 文件 | 行数 | 职责 |
|---|---|---|
| `RightPanel.cs` | 1,666 | 主控：生命周期/状态机/视图分发/样式初始化/像素头像/审批弹窗 |
| `RightPanel.ChatView.cs` | 657 | 分部（partial）：会话字段 + `DrawChatArea`（聊天区整体）+ 会话列表/侧栏/刷新 |
| `RightPanel.SubPanels.cs` | 573 | 分部（partial）：子面板（设置/便签/报告）+ `InitSubPanelStyles` |
| `UiTextureFactory.cs` | 413 | **独立静态类**：17 个纹理生成函数（圆角/渐变/云纹/星空/太极/六芒星/气泡） |
| `StarField.cs` | 272 | **独立类**：星空系统（分层星点/流星拖尾），`_animAlpha` 改为参数传入 |

**拆分要点**：
- 纯静态纹理生成 → `UiTextureFactory`（public static class，调用点加前缀）
- 自包含状态的星空 → `StarField`（`Init(seed)` / `UpdateStarMotion()` / `DrawStars(..., animAlpha)`）
- 强耦合实例状态的子面板/聊天区 → **partial class**（跨文件共用私有成员，零风险）
- 坑：聊天区提取时 `bgRect`（OnGUI 局部变量）被带入，改用等价字段 `_panelRect`（语义=窗口矩形）；新文件 `.meta` 由 Unity 首次编译自动生成
- **坑（2026-08-14 真机验证发现）**：`StarField _starField` 字段拆分后必须**实例化**（`= new StarField()`）。漏了会引发双重故障：① `InitStyles` 内 `_starField.Init(42)` 首帧 NRE，且 `_stylesReady=true` 在抛异常**之前**已置位 → 900 行后全部样式/纹理永不创建（半初始化锁定）；② 面板打开后 `_starField.UpdateStarMotion()` 每帧 NRE，OnGUI 在视图分发前中断 → 面板只剩背景、列表/聊天/输入框不渲染。EditMode（nographics）跑不到 OnGUI 测不出，**必须真机开面板验证**（`@@view:open` 后查 Player.log 无 `NullReferenceException` 洪流）

## 三、开发历史迭代

| 版本 | 日期 | 变更 |
|------|------|------|
| — | — | 纯 IMGUI 界面 + 程序生成视觉（圆角/云纹/星点/CRT） |
| N40 | 2026-08-08 | 17×24 像素化调研完成（`pixel-dialogue-optimization.md`）：开源方案汇总 + P0/P1/P2 落地清单 |
| 2026-08-12 | **OpenClaw 任务可视化**（方案七）：标题栏状态区步骤显示（金色呼吸，优先级 任务>思考中>就绪）+ 日志区 `[openclaw]` 系统行 + 模态审批弹窗（红边三按钮，60s 自动拒绝，`DrawApprovalDialog`） |
| 2026-08-13 | **QQ 式两级界面**：热键打开默认「会话列表」窄条（第一级），双击条目展开「左会话栏+右聊天区」（第二级），◀ 返回收窄；尺寸按 QQ 实测 324×846 基准 **1.5 倍放大**（486×1269 / 1290×1269 / SIDEBAR_W=420），字体全量 QQ 化（Microsoft YaHei，标题 19 / 气泡 17 / 输入 18）；**修复第二级拖拽偏移 bug**（`_dragOffset` 改用窗口原点 `_panelRect` 计算，排除 ✕/◀/字体按钮误触，MouseUp 复位 + Update 防吸保险），拖拽跟手验证通过 |
| 2026-08-15 | **独立聊天窗口（大工程 Phase 1）**：原生 Win32 窗口（非置顶、可被遮挡、QQ 式）+ IMGUI→RenderTexture→BGRA 像素桥（15fps）+ 原生 EDIT 输入 + 发送按钮 → `MainThreadDispatcher` → `ChatManager`；标题栏 ⧉ 切换 + `@@view:external/embed` 终端命令；桌宠主窗保持置顶不变。详见 §2.9 |

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
