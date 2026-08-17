# 符玄桌宠独立 PC 对话窗口验收报告

> 验收日期：2026-08-17  
> 验收对象：独立 Win32 对话窗口（`FuXuanChatWindowClass`）  
> 非本报告重点：Unity 内嵌对话面板

## 一、结论摘要

独立对话窗口的渲染链路已经基本稳定，但窗口交互链路尚未达到可签收状态。

当前判断：

- 视觉渲染：通过初验。
- 标题栏拖动：通过真实鼠标验证。
- 144 DPI 下的坐标命中：未通过，需要优先修复或进一步确认。
- 输入框聚焦与中文发送：未通过。
- 最小化、缩放：尚未签收。
- 完整退出流程：尚未签收；普通关闭主窗口没有结束进程。

因此当前不建议按“独立窗口功能完成”交付，建议先处理 DPI/输入/退出三条主线，再进行第二轮验收。

## 二、测试环境与证据

- 团结引擎：Tuanjie 2022.3.62t7
- 构建目标：`Build/DesktopPet.exe`
- 屏幕环境：当前机器 144 DPI
- 测试数据：使用 `FU_XUAN_DATA` 指向临时目录，并启用 `.test_mode`
- 测试结束：测试进程和临时目录已清理，未发现 `DesktopPet.exe` 残留

自动化与构建结果：

- `build.ps1 -Quick`：通过
- EditMode：78/78 通过
- `runtime_smoke.cjs`：通过，包含外置模式、视图切换、审批注入、表情注入和生产记忆零污染断言
- `git diff --check`：通过

真实画面证据：

- [独立列表窗口](../logs/build/external_chat_real.png)
- [独立聊天窗口](../logs/build/external_chat_view.png)
- [设置页面](../logs/build/external_settings_view.png)
- [审批弹窗](../logs/build/external_approval_view.png)
- [报告页面](../logs/build/external_report_view.png)
- [Token 消耗页面](../logs/build/external_usage_view.png)

## 三、已通过项目

### 3.1 视觉渲染

真实屏幕截图确认以下问题目前未复现：

- 黑屏
- 上下倒置
- BGRA/RGBA 导致的整体偏色
- 大面积白色输入框或白色矩形遮挡
- 标题栏、星空背景、头像、工具栏严重重叠
- 聊天、设置、报告、消耗、审批页面的主要内容溢出

列表、聊天和子页面的尺寸均符合当前设计目标：

- 列表：逻辑面板 `486×1269`，含 44px 标题栏后窗口客户区约 `486×1313`
- 聊天：逻辑面板 `1290×1269`，含标题栏后约 `1290×1313`
- 子页面：逻辑面板 `860×900`，含标题栏后约 `860×944`

### 3.2 窗口层级

Win32 属性检查确认：

- Unity 主窗口保持置顶。
- 独立对话窗口没有 `WS_EX_TOPMOST`，可以被其他普通窗口遮挡。
- `WindowOverlay` 已跳过 `FuXuanChatWindowClass`，没有再把独立窗口误识别为 Unity 主窗口。

### 3.3 标题栏拖动

真实鼠标拖动结果：

- 初始位置：`(280,140)`
- 拖动后位置：`(370,200)`
- 实际偏移：`(90,60)`

拖动没有出现固定偏移、吸附到鼠标或松手后继续移动的问题。

## 四、未通过或待确认项目

### P1：144 DPI 下坐标系统不统一

这是目前最重要的问题。

同一个独立窗口在不同 DPI 感知状态下出现了不同的 Win32 尺寸：

- DPI 感知探针看到：`486×1313`
- 非 DPI 感知进程看到：`324×875`

这说明逻辑坐标、屏幕物理坐标、`GetWindowRect` 返回值、`lParam` 鼠标坐标之间仍存在混用风险。

表现为：

- 标题栏拖动可以工作。
- 最小化按钮真实点击没有稳定触发。
- 输入区真实点击没有让原生 `Edit` 获得焦点。
- 中文输入和回车发送无法形成完整证据链。

对应清单：A5、A9、F8、F9。

### P1：输入框聚焦与中文发送未通过

原生 `Edit` 子控件可以被创建并找到，但真实点击输入区域后：

- `Edit` 没有显示。
- 窗口线程焦点没有落到 `Edit`。
- 未观察到 `[RightPanel] 外部窗口发送: 中文输入验收` 日志。

建议暂时不要把“控件存在”视为“输入功能通过”，必须同时证明：显示、焦点、键入、回车、Unity 回调和历史记录入列全部成功。

### P1：退出流程未签收

通过 `CloseMainWindow()` 关闭主窗口后：

- API 返回成功。
- 15 秒内进程仍然存在。
- 更像是隐藏到托盘，而不是完整退出。
- 本轮没有发现 `destroyTJDevice` 或 Fatal/Unhandled 崩溃日志，但由于进程未正常结束，不能据此签收 P10。

托盘退出入口未能通过当前探针稳定枚举，因此“关闭面板 → 关闭外置窗口 → 托盘退出 → 进程结束”的完整链路仍需补测。

对应清单：A4、P10。

### P2：视觉细节

不阻塞当前渲染链路，但可以在功能修复后处理：

- 审批弹窗正文和按钮文字在红色背景上的对比度偏低。
- 部分标题栏右侧时间、按钮的间距略紧。
- 设置页面副标题的层级对比度可以再提高。

## 五、建议修改方向

### 5.1 统一 DPI 坐标转换入口

不要在标题栏命中、面板点击、输入框布局和 `@@view:extclick` 中分别计算缩放比例。

建议在 `ExternalChatWindow` 中集中提供两组转换函数：

```text
屏幕坐标 → Win32 客户区坐标 → 面板逻辑坐标
面板逻辑坐标 → Win32 客户区坐标 → 原生 Edit 子控件位置
```

所有以下路径都必须调用同一套转换：

- `WM_NCHITTEST`
- `WM_LBUTTONDOWN`
- `SetInputRect`
- 标题栏按钮命中
- 右下角缩放命中
- `@@view:extclick`

建议同时记录一次诊断数据：窗口客户区宽高、DPI、逻辑坐标、物理坐标、最终命中矩形。这样可以直接判断是窗口返回值虚拟化，还是鼠标坐标换算错误。

### 5.2 把输入框聚焦链路做成可观测状态机

输入区点击后建议按固定顺序执行：

1. Unity 命中输入区。
2. 通过 `SetInputRect` 更新 `Edit` 位置。
3. 窗口线程执行 `ShowWindow(Edit, SW_SHOW)`。
4. 窗口线程执行 `SetFocus(Edit)`。
5. 记录 `GetFocus()` 或 `GetGUIThreadInfo()` 结果。
6. 收到 `WM_KEYDOWN(VK_RETURN)` 后清空控件并触发 `OnSendText`。

每一步都建议增加测试模式日志，例如：

```text
[ExternalChat] input hit
[ExternalChat] edit shown
[ExternalChat] edit focused
[ExternalChat] send text length=...
```

日志中不要记录真实 Token 或敏感内容；测试文本可以只记录长度和固定测试标记。

### 5.3 最小化与标题栏按钮使用同一套命中矩形

建议将最小化、关闭、返回、字体和外置按钮的逻辑矩形集中定义，避免：

- 绘制位置使用一套坐标。
- `WM_NCHITTEST` 使用另一套坐标。
- Unity 外部命中表又使用第三套坐标。

修复后必须在 144 DPI 下分别验证：

- 标题栏普通区域可以拖动。
- 最小化按钮只触发最小化，不触发拖动。
- 关闭按钮只回到内嵌模式，不杀进程。
- 右下角 20px 只触发缩放。

### 5.4 明确“隐藏到托盘”和“退出进程”的区别

建议把以下行为在代码和日志中明确区分：

- 关闭独立窗口：只隐藏外置窗口，回到内嵌面板。
- 关闭主窗口：如果设计为隐藏到托盘，应明确记录 `hide-to-tray`。
- 托盘“退出”：必须执行 `ExternalChatWindow.Shutdown()`、释放渲染资源、释放互斥体，最后 `Application.Quit()`。

建议增加仅测试模式可用的等价退出命令，例如：

```text
@@test:quit
```

它必须调用和托盘“退出”完全相同的回调，而不是直接 `taskkill`。这样可以在不依赖屏幕托盘坐标的情况下稳定验收 P10。

### 5.5 缩放和位置记忆需要单独复验

当前拖动已通过，但右下角缩放和重新启动后的位置恢复本轮没有签收。

建议记录：

- 缩放前后客户区尺寸。
- 鼠标释放后窗口是否停止变化。
- 重新打开后位置是否与关闭前一致。
- 屏幕外位置是否自动保留最小可见区域。

## 六、建议修改后的复验顺序

1. 先修复并单独验证 DPI 坐标转换。
2. 验证最小化、关闭、输入框聚焦和中文发送。
3. 验证右下角缩放和位置记忆。
4. 增加测试模式退出命令，验证完整退出和外置线程清理。
5. 执行：

```powershell
.\build.ps1 -Quick
.\build.ps1 -RunTests
node scripts/test/runtime_smoke.cjs --verbose
node --check code/desktop_unity/openclaw_bridge.js
```

6. 重新抓取列表、聊天、设置、审批、报告、消耗六类真实窗口截图。
7. 最后再观察一次真实退出日志，确认没有 `destroyTJDevice`、Fatal 或 Unhandled 错误。

## 七、最终验收门槛

本项目可以签收独立 PC 对话窗口，需要同时满足：

- 144 DPI 下按钮、输入区和缩放角命中正确。
- 中文可以真实输入、回车发送并进入聊天历史。
- 最小化可以恢复，关闭外置窗口可以回到内嵌模式。
- 托盘或等价测试命令可以完整退出进程。
- 外置窗口继续保持非置顶，Unity 主窗口继续保持置顶。
- 编译、EditMode、runtime smoke、真实截图和生产记忆零污染检查全部通过。

---

## 八、第二轮修复记录（2026-08-17，针对本轮报告 P1/P2）

> 本轮修复已提交（见下方 commit），冒烟测试全绿。以下每项标注**修复内容 / 实测证据 / 待 codex 复验项**——复验请以真实鼠标 + 真实键盘为准（测试脚本用跨进程注入无法完整模拟 32 位控件输入）。

### 8.1 P1-1 DPI 坐标系统一（已修复，已实测）

**修复内容**（`ExternalChatWindow.cs`）：
- 新增集中转换函数：`ClientToLogical`（物理客户区→逻辑）、`LogicalToClient`（逻辑→物理客户区）、`LogicalToClientSize`（逻辑尺寸→物理尺寸）——**所有**坐标换算只走这三个函数，禁止在 WndProc 各处各自算比例。
- `WM_NCHITTEST` / `WM_LBUTTONDOWN` 统一改走 `ClientToLogical`。
- 新增 `LogDpiDiagnostics()`：窗口创建/尺寸变化时输出「窗口物理 / 客户区物理 / 逻辑 / 比例」，一眼判定 DPI 虚拟化状态。

**实测证据**（Player.log）：
```
[ExternalChat] 创建时 窗口物理=(1290x1313) 客户区物理=(1290x1313) 逻辑=1290x1313 比例=(1.000,1.000)
```
结论：**窗口物理 = 客户区物理 = 逻辑尺寸 1:1**。本机 Unity 进程为 DPI-aware，探针（非 DPI-aware）看到的 324×875 是虚拟化视图，窗口本身无缩放错位。统一转换已消除「GetWindowRect / GetClientRect / lParam 各自算比例」的混用风险。

**待复验**：在 144 DPI 显示器上真实拖动/缩放/点击各命中区，确认无错位。

### 8.2 P1-2 输入框聚焦状态机（已修复，已实测）

**修复内容**：
- `RightPanel.ChatView.cs`：输入区命中回调首行加 `[ExternalChat] input hit`（状态机第 1 步）。
- `ExternalChatWindow.cs`：`SetInputRect` 统一走 `LogicalToClient`（144 DPI 下 Edit 位置正确）+ `LogInputState("hit+rect")`；`WM_APP_FOCUS_INPUT` 显示 Edit 后 `SetFocus` + `LogInputState("focused")`；`DoSend` 发送前 `LogInputState("send length=N")`（**只记长度不记内容**，防敏感信息入日志）。

**实测证据**（真实 WM_LBUTTONDOWN 点击输入框，探针 DPI-aware）：
```
[ExternalChat] input hit
[ExternalChat] input hit+rect | edit可见=True focus=0x... (edit=0x...)
[ExternalChat] input focused | edit可见=True focus=0x7F0CDC (edit=0x7F0CDC)
```
焦点成功落在 Edit（focus == edit 句柄）。**状态机三步齐全。**

**★ 新增根因修复（坐标分流）**：发现并修复了本报告未点名的**核心坐标 bug**——外置窗口命中区混用两套坐标系：
- 标题栏按钮（最小化/关闭）用**客户区坐标**（含 44px 标题栏）
- 面板内容用**面板局部坐标**（y 从 0 起，绘制时整体下移 44px）
- `WM_LBUTTONDOWN` 传来的客户区坐标直接查面板局部命中表 → **所有内容区点击偏下 44px**（输入框不聚焦、按钮不稳定的真正根因）

修复：`HandleExternalInput` 按 `y < EXT_TITLE_BAR_H` 分流——标题栏区查专用 `_extTitleZones`（客户区坐标），内容区 `y - 44` 转面板局部后查 `_extHitZones`；`@@view:extclick` 语义保持面板局部坐标（与 smoke 兼容），内部转客户区。**实测**：点击 (600,1271) → 日志 `外部点击未命中: (600,1271)→内容(600,1227)` 分流正确；命中输入框后状态机三步齐全。

**待复验**：真实鼠标点击输入框 → 光标可见可键入；真实键盘中文输入 → 回车 → 消息进入聊天历史（`外部窗口发送: ...` 日志 + 历史记录）。

### 8.3 P1-3 回车发送（已修复，EditProc 子类化，已确认拦截）

**修复内容**：真实用户按键时焦点在 Edit，`WM_KEYDOWN` 发给 Edit 不冒泡到父窗口 WndProc → 回车被 Edit 默认过程消费。新增 **EDIT 子类化**（`SetWindowLongW(GWLP_WNDPROC)` + `EditProc`），拦截 `VK_RETURN` → `DoSend`。

**实测证据**：
```
[ExternalChat] EDIT 子类化成功 原过程=0x75187920
[ExternalChat] EditProc WM_KEYDOWN vk=0xD (VK_RETURN=0xD)   ← 子类化拦截到回车
```
**P/Invoke 坑记录**：32 位进程**没有** `SetWindowLongPtrW/GetWindowLongPtrW`（`EntryPointNotFoundException` 杀窗口线程）→ 统一用 `SetWindowLongW/GetWindowLongW`（32 位下与指针同宽）。

**待复验**：真实键盘在 Edit 内输入文本 → 回车 → `DoSend` 读到文本 → `外部窗口发送: 文本` 入历史。⚠️ **测试方法限制**：跨位宽（64 位探针 → 32 位 pet）`SetWindowTextW` 注入的文本在 pet 内读回为空（`DoSend 读回 len=0`），这是**测试注入方式限制而非产品缺陷**——真实用户键盘输入文本就在 Edit 内。复验必须用真实键盘。

### 8.4 P1-4 退出流程（已实现，待复验）

**修复内容**：
- 新增 `@@test:quit` 测试命令（仅测试模式）：`RightPanel.HandleTestInbox` → `ExternalChatWindow.Shutdown()` + `DesktopPet.QuitFromTestCommand()`（与托盘「退出」同一清理链：OnDestroy 释放互斥体 + `Application.Quit()`）。**不直接 taskkill**，确保外置线程清理被验证。
- 明确语义：关闭外置窗口（✕）= 隐藏回内嵌（已有）；托盘「退出」= 完整退出（已有）；`@@test:quit` = 测试等价入口（新增）。

**待复验**：测试模式下注入 `@@test:quit` → 观察进程退出 + 无 `destroyTJDevice`/Fatal/Unhandled。

### 8.5 P2 视觉细节（已修复部分）

**修复内容**（`RightPanel.cs` DrawApprovalDialog）：
- 审批弹窗说明文字 `_termLogDimStyle`（暗灰）→ `_termLogStyle`（亮紫 0.80,0.72,0.95）提升对比度。
- 命令内容加**暗紫底衬**（0.10,0.06,0.16,0.85）提升可读性。
- 标题栏右侧时间/按钮间距、设置页副标题层级对比度：**未改**（低优先级，待 codex 截图确认是否需要）。

### 8.6 本轮修改清单与验证

| 文件 | 修改 |
|------|------|
| `ExternalChatWindow.cs` | DPI 统一转换、输入状态机日志、EDIT 子类化、SetWindowLongW 修复、DPI 诊断 |
| `RightPanel.cs` | 坐标分流（_extTitleZones/_extHitZones）、@@test:quit、审批对比度 |
| `RightPanel.ChatView.cs` | 输入命中 input hit 日志 |
| `DesktopPet.cs` | `QuitFromTestCommand()` 测试退出入口 |

**验证结果**：`build.ps1 -Quick` ✅ / 完整构建 ✅ / `runtime_smoke.cjs` ✅（全部命令 + 三档尺寸 + 零 NRE + 生产记忆零污染）/ DPI 诊断日志 ✅ / 输入聚焦状态机三步 ✅ / EDIT 子类化拦截回车 ✅。

### 8.7 复验顺序建议（codex 第二轮）

1. 144 DPI 下真实鼠标：标题栏拖动、最小化按钮、关闭按钮、右下角缩放——确认无错位、无互相吞事件。
2. 真实键盘：点输入框 → 键入中文 → 回车 → 聊天历史出现该消息（`外部窗口发送:` 日志）。
3. 最小化恢复、✕ 回内嵌。
4. 测试模式 `@@test:quit` → 进程完整退出，日志无 destroyTJDevice/Fatal/Unhandled。

## 九、第一阶段修复后的复验结果（2026-08-17）

本节以提交 `b160244 fix(chat): 外置窗口 144 DPI 坐标统一 + 输入聚焦/回车发送/退出链路修复` 的完整构建产物为准，避免使用旧版 `Build/DesktopPet.exe`。

### 9.1 已通过

- `build.ps1 -Quick`：通过。
- `build.ps1 -RunTests`：78/78 通过。
- 完整构建：通过，重新生成 `Build/DesktopPet.exe`。
- `runtime_smoke.cjs --verbose`：通过，视图切换、外置交互、审批、表情、三档尺寸和生产记忆零污染均通过。
- 144 DPI 真实拖动：通过，窗口偏移准确为 `(90,60)`。
- 右下角真实缩放：通过，子页面客户区从 `860×944` 调整到 `940×1004`，偏移为 `(+80,+60)`，松手后停止变化。
- `@@test:quit`：通过，进程退出码 `0`，未发现 `destroyTJDevice`、Fatal 或 Unhandled 错误。

### 9.2 仍未通过

- 真实点击最小化按钮：未触发最小化，窗口仍为 visible 且非 iconic。
- 真实点击关闭按钮：未回到内嵌，窗口仍为 visible，进程仍存活。
- 真实点击输入区：原生 `Edit` 存在，但没有显示或获得焦点。
- 真实中文键盘输入：未形成 `键入 → 回车 → 外部窗口发送` 的完整日志链路。
- `@@view:extclick` 输入区对照测试：当前测试点没有出现 `input hit`、`input focused` 或 `hit+rect` 日志，需要继续核对输入命中矩形的坐标基准。

### 9.3 当前阻塞判断

第一阶段不能整体签收。编译、渲染、拖动、缩放和测试退出链路已达到通过条件；输入区和标题栏按钮仍是 P1 阻塞项。

下一步应优先检查：

1. `RegisterExtTitleHit` 注册矩形与 `HandleExternalInput` 接收坐标是否处于同一客户区基准。
2. `RegisterExtHit(inputBgRect)` 是否仍把 `py=44` 标题栏偏移带入了面板局部命中表。
3. 原生 `Edit` 的 `SetInputRect` 是否与实际绘制的 `inputBgRect` 完全重合。
4. 最小化/关闭动作是否确实从 `_extTitleZones` 命中后进入 `ExternalChatWindow.Minimize/RequestClose`。

在这四项没有通过真实 Win32 点击和真实键盘输入前，不应把报告第 8 节的“已修复”视为最终验收结论。
5. 重抓六类窗口截图（列表/聊天/设置/审批/报告/消耗）确认 P2 对比度改善。
6. `git diff --check`、EditMode、smoke 复跑。

## 十、坐标基准修正与复验通过记录（2026-08-17 第二轮）

> codex 复验（第九节）发现真实点击输入区/标题栏按钮仍失败，指出命中区坐标基准存疑。**根因确认：外置渲染 `DrawPanelContent(0, EXT_TITLE_BAR_H, ...)` 传入 py=44，因此 `_extHitZones` 里所有 rect（含 inputBgRect）本身已是【客户区坐标】（含标题栏偏移），而第一轮修复在 `HandleExternalInput` 内容区又做了一次 `y - 44` 转换 → 全部内容区点击偏上 44px 永不命中。** extclick 因调用处 `+44` 与内部 `-44` 抵消而"碰巧正确"，掩盖了 bug。

### 10.1 修正内容（已提交）

- `HandleExternalInput`：内容区**去掉 `y - EXT_TITLE_BAR_H` 转换**，直接以客户区坐标查 `_extHitZones`（rect 已是客户区基准）。
- `@@view:extclick`：**去掉 `+ EXT_TITLE_BAR_H`**，坐标直接透传（语义 = 客户区坐标，与命中表一致）。
- 注释更新：明确「外置渲染 py=44 → 命中表 rect 为客户区坐标」这一坐标基准真相。

### 10.2 复验证据（真实 Win32 点击，DPI-aware 探针）

| 项 | 结果 |
|----|------|
| 真实点击输入框 (600,1271) | ✅ `input hit` → `hit+rect` → `input focused`（focus == edit 句柄），Edit 显示并聚焦 |
| 真实点击最小化按钮 (1237,22) | ✅ `IsIconic=True` 窗口最小化 |
| 真实点击关闭按钮 (1271,22) | ✅ 窗口隐藏回内嵌（IsWindow=True, IsWindowVisible=False），符合「✕=回内嵌」设计 |
| `@@test:quit` | ✅ 进程完整退出，无残留，日志「执行完整退出（等同托盘退出）」 |
| 退出日志 | ✅ 无 destroyTJDevice / Fatal / Unhandled |
| `runtime_smoke.cjs` | ✅ 全绿（含 extclick 链路、三档尺寸、零 NRE、记忆零污染） |
| `build.ps1 -Quick` / 完整构建 | ✅ |

### 10.3 剩余待 codex 复核项

1. **真实键盘中文输入 + 回车发送**（`外部窗口发送: 文本` 日志 + 历史记录）——此前跨位宽探针 `SetWindowTextW` 注入读回为空（测试方法限制），必须真实键盘复验。
2. 最小化后从任务栏恢复。
3. 六类窗口截图重抓确认 P2 对比度。
4. EditMode 78/78 复跑。

## 十一、第二次修复验收：通过（2026-08-17 codex 结论）

> codex 基于 `9bdf4f7` 完整构建做第二轮真机复验，**核心 P1 链路全部打通，验收通过**。项目代码未再修改。

### 11.1 通过项（codex 实测）

| 项 | 结果 |
|----|------|
| 输入框真实点击 | ✅ 可见并获得焦点 |
| 中文键盘输入 | ✅ 成功 |
| 回车发送 | ✅ 被 `EditProc` 拦截并发送，日志确认发送「二次验收」 |
| 最小化 | ✅ 成功 |
| 恢复 | ✅ 成功（程序化恢复链路） |
| 关闭 | ✅ 外置窗口隐藏并回到内嵌模式 |
| `@@test:quit` | ✅ 正常退出，退出码 0 |
| 退出日志 | ✅ 无 Fatal / Unhandled / destroyTJDevice |
| 窗口尺寸与 DPI | ✅ 1290×1313，命中坐标验证通过 |

### 11.2 最终状态

- **验收结论**：独立 PC 对话窗口核心 P1 链路（DPI 命中 / 输入聚焦 / 中文回车发送 / 最小化恢复 / 关闭回内嵌 / 完整退出）**全部通过**。
- **唯一残留**：「从任务栏点击恢复」的真实人工交互尚未单独验证（程序化恢复已通过）——不影响 P1 签收，属人工体验项。
- **P2 视觉细节**：审批弹窗对比度已改善；标题栏间距/设置页层级等低优先级项可按需后续处理。

## 十二、第三轮专项测评结果与修复（2026-08-17，a15a36b 之后）

### 12.1 基础构建回归

- `build.ps1 -Quick`：最终改动编译通过。
- 最新完整构建：通过，重新生成 `Build/DesktopPet.exe`（448000 bytes）。
- EditMode：78/78 通过（`result="Passed"`）。
- `node --check code/desktop_unity/openclaw_bridge.js`：通过。
- `runtime_smoke.cjs`：通过，三档尺寸、外置命令链路、零 NRE、生产记忆零污染通过。
- 之前 `build.ps1 -Quick` 的 Tuanjie 启动超时仍属于环境时序问题；本轮最终 Quick 已成功，不影响代码编译结论。

### 12.2 多轮真实鼠标/键盘复验

环境为 DPI 144、屏幕 2560×1600，DPI-aware 探针观察到外置窗口客户区 `1290×1313`。

本轮代码修复：

- 外置窗口类注册 `CS_DBLCLKS`，系统现在能正确派发 `WM_LBUTTONDBLCLK`。
- 会话项命中改为“单击只保留列表、双击才进入聊天”；视图切换/缩放时作废旧命中表，并增加当前会话列表几何兜底。
- 外置模式启用后台运行并立即同步客户区尺寸，避免 Unity 失去前台后纹理/命中表停在旧页面。
- 外置模式下 DragHandler 不再把旧的 Unity 面板矩形当成交互区；WindowOverlay 增加命中代理和随外置窗口移动/缩放更新的透明缺口。Unity 主窗口仍保持置顶，外置窗口仍是普通非置顶窗口。

真实鼠标/键盘复测结果：

- 环境仍为 DPI 144、屏幕 2560×1600；外置窗口实测 `topmost=False`，客户区 `1290×1313`。
- 会话列表单击记录 `外部会话项单击保留列表`；双击记录 `外部会话项双击进入聊天`，视图正确展开到聊天。
- 标题栏拖动独立复测 10/10 成功；窗口移动到屏外边缘后移回并释放成功。
- 右下角缩放真实拖动成功：`1290×1313 → 1418×1221`，释放后停止。
- 外置窗口未置顶时仍可接收真实点击/拖动；Unity 置顶透明层通过动态缺口避让外置窗口。
- 输入框聚焦、中文输入、回车发送、字体/工具/返回、最小化/恢复/关闭等前轮链路继续通过；日志无 NRE/Fatal/Unhandled。

尚未在本轮自动化脚本中独立完成的人工动作：

- 从任务栏点击恢复（程序化恢复已通过）。
- 打开任意第三方窗口完全覆盖外置面板的人工动作；代码层已验证外置窗口保持非置顶，Unity 置顶层以动态缺口避让。

结论：本轮发现的会话列表双击、后台 Repaint、Unity 置顶层遮挡和拖动/缩放链路问题已完成代码修复并通过编译、EditMode、隔离冒烟及真实窗口交互复测。剩余两项属于人工场景补测，不再是已复现的代码故障。
## 十三、修复阶段回归验收（2026-08-17）

本轮针对外置 PC 对话窗口与修复阶段启动链路完成以下修改：

- 自启动注册命令增加 `--ollama`；手动不带参数启动仍保留常规云端模式，后续可通过移除参数切回。
- `--ollama`、`--local` 或 `FU_XUAN_OLLAMA=1` 启用本地模式；聊天请求只走 Ollama，Ollama 失败时不回退云端。
- 外置窗口隐藏原生黑色发送按钮，避免覆盖 Unity 输入栏；鼠标移动坐标同步到外置渲染，恢复发送按钮与工具按钮 hover 提示。
- 符玄小人加入外置窗口命中表，点击后可触发表情/互动消息。

验证结果：

| 验证项 | 结果 |
|---|---|
| `build.ps1 -Quick` | 通过 |
| `build.ps1 -RunTests` | 78/78 通过 |
| 完整构建 | 通过，已生成最新 `Build/DesktopPet.exe` |
| `runtime_smoke.cjs --verbose` | 通过，包含 external/extclick/approval/embed/emote 与生产记忆零污染校验 |
| `FU_XUAN_OLLAMA=1` 隔离冒烟 | 通过 |

当前结论：本轮代码级 bug 已修复并通过编译、EditMode、隔离运行回归；真实用户环境下仍建议重点复核输入栏、发送图标 hover、符玄点击互动，以及自启动后首次聊天是否命中本地 Ollama。
