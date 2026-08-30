# 项目已知 Bug 与验收点（AI 开工前必读）

> **2026-08-30 当前核对**：本轮目标点式拖拽物理输入、响应降幅、停走物理收敛和问候时段一致性已通过 Quick/完整构建与隔离运行冒烟；头部停下瞬间的真实观感、问候语早/午/晚人工抽查，以及 GPU/帧率和裁切仍需可见播放器确认。P1 `destroyTJDevice` 是否在真实退出时彻底消失，仍按原验收要求保留观察项。此前记录的约 ¥5/天异常已确认来自其他项目复用同一个用户级 `DEEPSEEK_API_KEY`，不归因于桌宠项目。

> **文档作用**: 让 AI **第一时间**掌握两件事——① 项目当前**已知 Bug / 风险点**（哪些还没解决、哪些已修复但改相关代码时必须防回归、哪些是**刻意保持现状勿动**的）；② 改完代码后的**重要验收点**（怎么证明没改坏）。任何涉及"外置窗口 / 渲染 / 退出流程 / 测试模式 / Token 消耗"的改动，先读本文。
> **基本架构**: 本文与 [`token-cost-testing.md`](token-cost-testing.md)（消耗专项）、[`task-inventory.md`](task-inventory.md) 第十二节（N39 前老 Bug 审计）互补——本文聚焦 **2026-08-16 外置窗口阶段之后**的活跃问题与验收标准。
> **开发历史迭代**: 2026-08-16 外置面板 A1-A6/B1-B3 阶段集中产出 10+ 个渲染/交互 Bug（多数已修）；N44 测试模式禁云端新增一类"测试与生产行为偏差"；2026-08-26 完成 ¥5/天异常的跨项目 Key 归因。
> **编写注意事项**: 状态标注必须真实——「未确认」就是未确认，不许写成已修复；「勿修」项有明确理由，改动前需先读对应模块文档。

---

## 一、活跃问题与风险点（未解决 / 待确认）

| # | 问题 | 影响 | 状态 | 应对 |
|---|------|------|------|------|
| P1 | **destroyTJDevice 退出崩溃**（崩溃计数 105→108→120 持续增长） | 退出时引擎崩溃 → 可能触发反复重启 | 🟡 **已降级未确认**：`2cad357` 按 DisableExternalMode→Shutdown→释放 RT/NativeArray 顺序修复；退出时引擎崩溃是否彻底消失**未最终确认**（exit-time 崩溃不影响外部交互） | 多轮真实退出观察 Player.log；若复现按"退出崩溃"专项排查 |
| P2 | **schannel TLS 全坏（系统级）**：`SEC_E_NO_CREDENTIALS (0x8009030e)`，Node/curl/.NET 全部 HTTPS 失败（baidu.com 也连不上），浏览器 OK（BoringSSL） | DSH harness 切 GPT 被卡（`dsh-codex-auth` 已装但连不上）；**codex CLI（Rust/rustls）不受影响，可直接用** | 🔴 未解决 | 用户操作：重启 → `sfc /scannow` → `DISM /Online /Cleanup-Image /RestoreHealth` → 卸 SteamTools MITM 证书；修好前**不要**再尝试 DSH 代理配置 |
| P3 | **测试模式一刀切**：禁云端后测试里验证不到真实云端链路（缓存命中率/价格/响应质量），且"本地失败=功能缺失"而非回退云端 | 测试行为与生产有偏差，可能误判 bug | 🟡 已知限制（N44） | 见 `token-cost-testing.md` 铁律 5：唯一烧钱路径需用户确认 |
| P5 | **外置窗口退出顺序回归风险**：组件销毁顺序不应被当作唯一清理保障 | 窗口线程泄漏/崩溃 | 🟡 **已加固需防回归**：`DesktopPet.BeginShutdown` 统一托盘/测试/Unity 生命周期/对象销毁清理；`ExternalChatWindow` 增加建窗早期关闭请求保护 | 改退出或外置窗口生命周期代码后必须跑 Quick、完整构建、隔离冒烟测试 + 真实退出观察 |
| P6 | **动态分辨率降级未启用**：`GetResolutionScale` 恒 1.0；多屏检测已由 `WindowOverlay.isMultiMonitor` 实际实现 | 低端设备不做渲染分辨率降级 | 💡 有意保持 | 不要把分辨率降级写成当前能力；多屏检测不要按旧文档回退 |
| P7 | **3D 渲染不可用**：`HybridRenderer` 3D 分支 TODO、`Model3DRenderer` 注释与实现矛盾 | 恒走 Live2D | 💡 刻意保持现状 | **勿修**，改它浪费工时（见 `live2d-rendering.md` 注意③） |
| P8 | **默认空闲表情是 "surprise" 非 "curious"** | 文档/代码不一致 | 📝 已记录 | 改表情系统前先确认以代码为准 |
| P9 | **半透明特效 DWM 限制 / Z 顺序问题** | 视觉受限、透明间隙可看到下层窗口 | 💡 待研究 | 短期路线图项，勿急于实现 |
| P10 | **服务端（openclaw-bridge/Gateway/Ollama）需手动启动** | 依赖管理 | 💡 待后续 | 安装器阶段已做组件自动化（NSSM 注册） |
| P11 | **停下时头部偶发剧烈抖动、自动问候与时间段错位** | 停止瞬间视觉不稳；问候语可能出现早晚错配 | 🟡 **代码已修复，待可见播放器确认**：停止收敛跳过额外物理推进；问候共用有效小时并丢弃旧异步结果 | 连续走路→停下至少 10 次观察头发/头部；用调试小时分别验证清晨/中午/下午/夜晚，确认无旧问候迟到 |

## 二、已排除的费用归因问题（2026-08-26）

| 问题 | 最终结论 | 后续约束 |
|------|----------|----------|
| 约 ¥5/天的异常消耗 | 用户确认费用来自其他项目复用同一个用户级 `DEEPSEEK_API_KEY`，不是桌宠自身产生 | 不再把该异常作为桌宠 P1/P4 排查项；继续保留 `usage_log.jsonl`、`key_id` 和 `key_hash` 用于区分不同项目/Key 的账单归属 |

## 三、已修复 Bug 清单（改相关代码必须防回归）

> 2026-08-16 外置窗口阶段踩过的坑，全部已修复并提交。**改对应代码时逐条对照，防止回归**。

| Bug | 根因 | 修复提交 | 回归注意点 |
|-----|------|---------|-----------|
| 外置窗口全黑 | IMGUI 仅 `EventType.Repaint` 提交 RT，非 Repaint 早退 | `7945cc3` | 渲染管线必须保持 Repaint 门控 |
| 图像上下颠倒 | `SetDIBitsToDevice` biHeight 必须**正**（bottom-up），负值倒图 | `14727cf` | 改 SetBuffer/像素桥时勿改符号约定 |
| 颜色发橙 | RenderTexture 必须 **BGRA32**（ARGB32 通道序错） | `d1a5ee2` | AsyncGPUReadback→SetDIBitsToDevice 链路的 RT 格式锁死 BGRA32 |
| 标题栏拖不动/中途停 | `WM_NCHITTEST` 返回 HTCAPTION 后未显式转发 `WM_NCLBUTTONDOWN` 到 DefWindowProc | `917e99a` | 命中区表与 MoveLoop 逻辑勿动 |
| 标题栏按钮点击失效 | 拖动逻辑（LBUTTONDOWN 兜底）吞掉按钮事件 | `23bbb92` | 右上 68px 按钮区必须 HTCLIENT；勿加 LBUTTONDOWN 兜底 |
| 外置交互全部失效（点击/输入无反应） | `SetTextColor` 误声明在 **user32.dll**（实为 gdi32.dll）→ EntryPointNotFoundException 杀窗口线程 | `a4e6a79` | P/Invoke DLL 归属必须核对（gdi32/user32 易混） |
| 输入白框突兀 + 原生控件遮挡点击 | EDIT 带 CLIENTEDGE/BORDER 不透明 | `6ebd85f` | EDIT 必须透明（无 CLIENTEDGE、WM_CTLCOLOREDIT 白字 NULL_BRUSH） |
| 消耗子面板误渲染成聊天 | DrawPanelContent 子面板分支漏 `PanelView.Usage` | `882d8d3` | 新增子面板必须进分支判断 |
| 退出崩溃增长 | OnDestroy 释放顺序错误 | `2cad357` | 见 P1/P5 |
| ⧉ 按钮图标不可见 | mono 字体缺 U+29C9 字形 | `7945cc3` | 图标用 `UiTextureFactory.GenExtWindowTex` 像素生成，勿依赖字形 |
| 消耗面板数据重启丢失 | UsageStats 仅内存 | `08494dd` | 已由 UsageLogger JSONL 持久化 + 启动回灌解决 |
| 桌宠拖动中途停止/挣扎迟滞 | 点击穿透按当前宠物矩形刷新导致拖动丢输入；速度平滑过低且同一帧重复采样；物理输入未接入目标点/速度 | 本轮优化 | 拖动期间保持输入接收；位置边界约束；失焦中止；目标速度加速度限幅后提前写入 Cubism physics；最终观感仍需可见播放器确认 |
| 停止时头部剧烈抖动 | 停止过渡时 `Update()` 归零物理输入后，普通 `LateUpdate()` 额外 `ForceUpdateNow()` 再次推进物理，放大走路姿态到空闲姿态的切换 | 本轮修复 | `IDLE_BLEND_DURATION` / 上一帧行走标记期间跳过普通额外刷新；#4/#7 专用路径不变；仍需可见播放器验收 |
| 自动问候与当前时间段不符 | `AutoChat` 直接读取系统小时，未跟随 `TimeWeatherController.debugHourOverride`；`IdleChatGenerator` 旧异步结果可跨时段入队 | 本轮修复 | 有效小时统一；生成代际/时段守卫和明显不匹配词过滤；需按调试小时人工抽查 |

## 三、重要验收点（改完代码按此验收）

### 3.1 构建链路（每次改 C# 后必跑）
```powershell
.\build.ps1 -Quick        # 编译验证，必须 [OK] Build succeeded
.\build.ps1               # 完整构建 → Build/DesktopPet.exe（最终交付前跑）
node --check code/desktop_unity/openclaw_bridge.js   # 改桥接 JS 后
```
> ⚠️ 构建/启动桌宠在 DSH 沙箱下需 `danger-full-access`（Start-Process 被 workspace-write 拒绝）。
> 本轮还修复了 `build.ps1` 对 `Library\ilpp.pid` 的误判：PID 被复用或指向无关 `conhost` 时会清理过期标记，只有识别为 ILPP/Bee/UnityLinker/IL2CPP 进程才阻止并行构建。

### 3.2 冒烟测试（外置窗口/渲染/UI 改动后必跑）
```powershell
node scripts/test/runtime_smoke.cjs   # 隔离目录 + .test_mode + 生产记忆零污染断言
```
**通过标准（4 条，缺一 FAIL）**：
1. 全部 `@@view` 命令 + 外置交互链路被处理（`[TestInbox]` 留痕：open/chat/settings/back/report/reminders/list/external/embed/close + extclick + approval + emote）
2. 三档窗口尺寸切换出现（486x1269 / 1290x1269 / 860x900）
3. Player.log **零 NullReferenceException**（其他 Exception 计警告）
4. 生产记忆文件 mtime 全程未变（无记忆测试核心保证）

### 3.3 外置面板「标准窗口行为」验收清单（用户核心诉求）
| 验收项 | 通过标准 |
|--------|---------|
| 可被遮挡 | 外置窗口**不是**置顶，其他窗口可盖住（区别于宠物本体置顶） |
| 拖动 | 标题栏（44px 区）按住可拖动，松手不漂移 |
| 缩放 | 右下角 20px 角区可拖拽缩放 |
| 最小化/关闭 | 标题栏右侧按钮点击有效（最小化到任务栏/关闭） |
| 输入 | 点击输入框可聚焦打字，中文正常 |
| DPI | 高分屏下物理↔逻辑坐标换算正确（×DPI ratio） |
| 会话交互 | 双击会话项进聊天、工具行进子面板、◀ 返回、审批注入可用 |
| 渲染 | 星空标题栏 + 面板内容正常，无黑屏/倒图/偏色 |

### 3.4 测试模式与 Token 验收（见 `token-cost-testing.md` 全文）
- 测试模式运行后：隔离目录 `usage_log.jsonl` **只有 `src=local` 行**（零云端）
- Player.log 有 `🛡 已拦截云端调用` 留痕
- 本地 Ollama 调用照常记录（cost=0）
- 生产 `D:\DesktopPetData\usage_log.jsonl` 是消耗真相，别拿测试日志推断

### 3.5 测试铁律（违反出事故）
1. **测试必须无记忆隔离**：`FU_XUAN_DATA` 指向临时目录（smoke 已内置）或至少建 `.test_mode`；测后删 + 污染则 `node scripts/backup_memory.cjs --all` 留底清理
2. **UI 测试不靠模拟鼠标点击**：用 inbox 终端链路（`@@view:...`/`@@emote:`/`@@approval:`/`@@view:extclick:x,y`/`@@sim:*`）；新 UI 状态必须预留等价命令
3. **禁止空参数遍历调用所有工具**：只限低风险只读白名单（get_system_info/get_mouse_pos）；剪贴板、文件内容和截图工具必须显式确认
4. **密钥不入库**：环境变量读取，日志/输出禁含 token
5. **PS 5.1 写 JSON 带 BOM**：Python 读 `utf-8-sig`；`.cs`/`.ps1`/`.cmd` 带 BOM，其余 UTF-8 无 BOM

### 3.6 提交与文档验收
- Conventional Commits：`<type>(<scope>): <中文描述>`，一个提交一件事
- **写完代码必须更新对应 md 文档，且必须等测试通过后再写**（文档只记录已验证的代码真相）
- 新模块/新顶层文档：更新 `docs/README.md` 索引 + `AGENTS.md`（如本文件与 `token-cost-testing.md` 的挂法）

---

## 相关文档
- [`token-cost-testing.md`](token-cost-testing.md) — Token 消耗专项（生产 vs 测试、铁律、痛点）
- [`task-inventory.md`](task-inventory.md) 第十二节 — N39 前 9 个老 Bug 审计（已全部修复/澄清）
- [`modules/chat-ui.md`](modules/chat-ui.md) — 外置窗口/面板实现细节（渲染桥、命中区）
- [`modules/live2d-rendering.md`](modules/live2d-rendering.md) — 渲染管线、勿修项（3D/P0 安全网）
- [`AGENTS.md`](../AGENTS.md) — 铁律与构建命令
