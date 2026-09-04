# 编译工作流（AI 必读：如何正确构建本项目的 Unity 桌宠）

> **文档作用**: 让任何 AI 代理（codex / Copilot / Claude 等）**第一次就能正确编译**本项目。本文固化 2026-08-17 实战经验：Tuanjie batchmode 偶发启动卡死（授权/残留进程导致），以及绕过它的标准手段。**改任何 C# 代码后按本文流程验证**；构建卡死时按「卡死处理」章节行动，**不要重复直接重试**。
> **基本架构**: 构建入口 `build.ps1`（包装 Tuanjie.exe batchmode）；诊断脚本 `scripts/diagnose_tuanjie.ps1`（清理残留+验授权+试编译）；验证闭环 = Quick → 完整构建 → EditMode → 隔离冒烟。
> **开发历史迭代**: 2026-08-17 多次遇到「Tuanjie 启动 4 分钟无日志、log 未创建、CPU 不动」——根因是环境时序（残留进程/授权客户端状态），非代码问题；诊断脚本 + 杀残留后一次通过。

> **当前验证基线（2026-09-04）**：最新代码基线已通过 Quick、完整构建、EditMode 和隔离运行时冒烟；五个节日主题另有逐主题 Unity 四图和命令日志证据。节日文档本轮只做状态同步，不重复构建；真实 GUI 双击展开、拖拽/收回仍属于人工验收门槛。

---

> **2026-08-19 构建修复**：`build.ps1 -Quick` 现在复用 EditMode harness 编译路径，并自动使用临时 `FU_XUAN_DATA/.test_mode`；Quick/完整构建均带 `-nographics`。Tuanjie 必须在 full-access 环境启动，否则 Windows 沙箱可能在授权初始化阶段卡死且不创建日志。构建脚本会在确认没有 Tuanjie 进程后清理 `ArtifactDB-lock` / `SourceAssetDB-lock`，并在确认 ILPP PID 已退出后清理陈旧的 `Library\ilpp.pid`。

> **AI 权限要求（重要）**：构建不是普通的沙箱内命令。Tuanjie/Unity 启动、授权握手、原生编译器和构建子进程需要访问本机进程、授权服务和项目 `Library`；如果 AI 代理在受限沙箱中执行，常见表现是“构建未成功”但没有 C# 编译错误、外层 PowerShell 长时间不返回，或日志停留在旧时间。遇到这种情况，应立即申请本机/full-access 权限后重新执行同一构建命令；不要把外层超时或无输出直接判断为代码编译失败。只有看到本次运行产生的新 `direct_compile.log`/`build_log.txt`，并出现 `[OK] Build succeeded!` 或明确的编译错误，才可以判定结果。

## 一、编译入口速查

| 命令 | 用途 | 何时跑 |
|------|------|--------|
| `.\build.ps1 -Quick` | 仅 C# 编译验证（不产出 exe） | **每次改 C# 后必跑** |
| `.\build.ps1` | 完整构建 → `Build/DesktopPet.exe` | 交付/需要真机运行时 |
| `.\build.ps1 -RunTests` | EditMode 测试 → `logs/build/test_results.xml` | 改动涉及逻辑/UI 时 |
| `node --check code/desktop_unity/openclaw_bridge.js` | 桥接 JS 语法 | 改 JS 后 |
| `.\scripts\diagnose_tuanjie.ps1` | 构建环境诊断/恢复（见卡死处理） | **构建卡死时** |

> ⚠️ **环境固定**：Unity = `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe`（32 位）；项目 = `D:\Unity\projects\Desktop_per_pro\code\desktop_unity`。不要换路径、不要装第二个编辑器。

## 二、标准流程（改完 C# 后）

```powershell
# 1. 编译验证（必须 [OK] Build succeeded!）
.\build.ps1 -Quick

# 2. 需要真机时完整构建
.\build.ps1

# 3. 逻辑/UI 改动跑 EditMode
.\build.ps1 -RunTests
#    查看 logs\build\test_results.xml：result="Passed" total=78 passed=78 failed=0

# 4. UI/外置窗口改动跑隔离冒烟（自动用隔离数据目录，生产记忆零污染）
node scripts/test/runtime_smoke.cjs
#    [PASS] 运行时冒烟测试通过：全部命令处理 + 三档尺寸切换 + 零 NRE + 生产记忆零污染 ✅

# 5. 启动桌宠人工验证（继承用户环境变量）
Start-Process .\Build\DesktopPet.exe

#    Live2D 拖拽视觉回归：测试模式写入隔离数据根目录 inbox.txt
#    @@sim:drag:offset:180,0,12
#    @@sim:screenshot:right_drag
#    再执行左拖及镜像后的左右拖动，比较 test_screenshots/ 中的 Unity 截图
```

## P0：高强度构建导致过热或整机重启

用户反馈：问题只在高强度完整构建阶段出现，CPU 可能瞬时满载并达到 95°C 以上，偶发导致 Windows 重启。当前系统证据包含 `Kernel-Power 41` 和处理器 `WHEA-Logger 19 Internal parity error`，因此不能简单视为“构建正常满载”；在根因隔离前，完整构建属于 P0 风险。

### 已实现（2026-08-31 构建负载保护）

`build.ps1` 已内置构建负载保护（默认启用），可从命令行覆盖：

| 参数 | 作用 |
|------|------|
| `-CleanBeeCache` | **显式**清理 `Library\Bee` 缓存并强制全量重建；默认**保留**缓存走增量（避免每次全量重编译打满 CPU） |
| `-MaxCores <n>` | 限制构建可用逻辑核数（默认 = 半数逻辑核，`0` 表示自动） |
| `-NoThrottle` | 关闭 CPU 亲和/降优先级节流（不推荐，用于排查） |

除了 `-CleanBeeCache`，其余为默认行为：`build.ps1` 会限制 Tuanjie 主进程及其子进程（Bee.Backend / Roslyn / IL2CPP 等）的 **CPU 亲和掩码**到 `MaxCores` 核并把优先级降为 **BelowNormal**，并以 2s 间隔监视子进程直到构建结束；构建期间记录系统 CPU 占用均值/峰值。

> 实测（2026-08-31，i9-14900HX 32 逻辑核）：完整构建限制为 16 核 + BelowNormal，构建期 CPU **均值 15~19% / 峰值 21~26%**（此前未限制时经常打满 100%），增量构建复用缓存，构建仍成功产出 exe。

### 仍待处理
- **硬件根因**（BIOS Intel 默认功耗、XMP、散热、主板供电、PSU）尚未核查，非代码层面可解决。
- `build.ps1` 当前只记录 CPU 占用%，**未记录 CPU 温度/封装功耗/频率**（WMI 温度口在部分硬件不可用），温度/功耗证据需人工观察或外部工具。
- 验收门槛「低负载/受保护完整构建连续通过至少 3 次」「无新 WHEA-Logger / Kernel-Power 41」需在可见播放器 + 人工观察下完成。

### 保护规则（沿用）
1. 日常改动优先使用 `.\build.ps1 -Quick`；确需完整构建时用默认节流保护。
2. 清理 `Library\Bee` 只在 `-CleanBeeCache` 显式传参（确认缓存损坏/构建异常）时执行，不要默认清缓存。
3. 未完成 BIOS/散热/PSU 核查前，不得用反复重试构建作为验证方式。

## 三、构建卡死处理（重点：2026-08-17 多次遇到）

### 症状

- Tuanjie 进程启动后 **4 分钟无新日志**
- 指定的 `-logFile`（如 `direct_*.log`）**没有创建**
- CPU 基本不动
- 或授权客户端（`Tuanjie.Licensing.Client.exe`）状态异常

### 根因

**不是代码编译错误**（`direct_compile.log` 未创建 = 还没到项目加载阶段），而是 **Tuanjie 启动早期被授权握手 / 残留进程 / Licensing Client 状态阻塞**——环境时序问题，偶发。

### 标准行动（按顺序，卡住时**不要直接重复 build.ps1**）

```powershell
# ① 先诊断：自动清理残留 + 验证授权客户端 + 带超时试编译
.\scripts\diagnose_tuanjie.ps1 -StartupTimeoutSec 120

# 如果 direct_compile.log 未创建，使用 full-access PowerShell 重跑：
# build.ps1 / Tuanjie 的授权初始化可能被 workspace 沙箱拦截。

# 诊断脚本会输出关键结论：
#   - direct_compile.log 已创建 → 环境正常，编辑器能启动 → 直接构建
#   - direct_compile.log 未创建 → 启动早期卡死 → 看授权/残留
#   - 授权客户端存活 → 授权握手正常

# ② 环境健康后正常构建
.\build.ps1

# ③ 若仍卡（极少）：手动杀干净重试一次
Stop-Process -Name Tuanjie -Force -ErrorAction SilentlyContinue
Stop-Process -Name TuanjieCrashHandler32 -Force -ErrorAction SilentlyContinue
Stop-Process -Name Tuanjie.Licensing.Client -Force -ErrorAction SilentlyContinue
.\build.ps1
```

### 判断"卡在哪个阶段"（日志定位法）

| 现象 | 结论 |
|------|------|
| `-logFile` 未创建 + CPU 不动 | 启动早期（授权握手/启动器）→ 环境问题 |
| log 已创建但无 `Begin MonoManager ReloadAssembly` | 项目加载前（License/Subsystem） |
| log 有 `error CSxxxx` | 真实编译错误 → 修代码 |
| log 有 `Scripts have compiler errors` | 编译错误 → 读错误行修代码 |

### 已踩过的坑（P/Invoke / 环境）

1. **构建必须在沙箱 `danger-full-access` 下执行**：`Start-Process Tuanjie.exe` 被 workspace-write 沙箱拒绝（`Access is denied`）。首次被拒后**同命令升级重试一次**。
2. **`build.ps1 -Quick` 只验证编译，不更新 `Build/DesktopPet.exe`**——需要真机时**必须跑完整 `.\build.ps1`**，否则运行的是旧 exe（2026-08-17 实测踩过）。
3. **退出码读取**：`Start-Process -NoNewWindow` 下 `$proc.ExitCode` 偶发读取为空（宿主交互限制）——以 `-logFile` 是否存在 + 内容判定，不要当成构建失败。
4. **ILPP 陈旧 PID**：强杀 Tuanjie 后若 `Library\ilpp.pid` 仍存在，新版 `build.ps1` 会校验进程身份和启动时间；已退出、PID 复用的标记会安全清理，活动或身份不明且无法证明复用的进程则拒绝删除。
5. **32 位进程无 `SetWindowLongPtrW/GetWindowLongPtrW`**（`EntryPointNotFoundException` 杀线程）——P/Invoke 用 `SetWindowLongW/GetWindowLongW`。
6. **PS 5.1 无 `?:` 三元运算符**——脚本里避免（构建脚本本身已兼容）。
7. **`direct_compile.log` 留痕**：诊断脚本在 `logs/build/direct_compile.log` 留下最近一次试编译日志，作为判断依据。

### ILPP PID 标记的身份校验

`Library\ilpp.pid` 只记录 PID，Windows 进程退出后该 PID 可能被无关进程（常见为 `conhost`）复用。因此 `build.ps1` 不再把“PID 仍存在”直接当作活动 ILPP：进程身份匹配 ILPP/Bee/UnityLinker/IL2CPP 时阻止构建；若进程启动时间明显晚于标记写入时间，则判定为 PID 复用并安全清理；身份不明且无法证明复用时仍保守阻止，避免两个编辑器并发操作同一 `Library`。

## 四、验证闭环与验收门槛

| 层级 | 命令 | 通过标准 |
|------|------|---------|
| 编译 | `.\build.ps1 -Quick` | `[OK] Build succeeded! (mm:ss)` |
| 完整构建 | `.\build.ps1` | `[OK] Output: Build/DesktopPet.exe` |
| EditMode | `.\build.ps1 -RunTests` | `test_results.xml` 根节点 `failed=0`（含 Ignore 时根结果可为 `Skipped:Ignored`，仍算通过）；门禁会在跑前删旧结果、跑后校验；**2026-08-31 已修复**（原因 `-runTests` 与 `-quit` 同传导致测试运行器提前退出不写结果） |
| 冒烟 | `node scripts/test/runtime_smoke.cjs` | `[PASS]` + 生产记忆零污染 |
| 人工 | 启动 exe | 桌宠落地、功能正常 |

> **AI 铁则**：功能级改动**必须先编译+测试通过**，再更新模块文档；文档只记录已验证的代码真相（AGENTS.md）。

### 2026-09-04 最新验证记录

- `build.ps1 -Quick`：通过，`[OK] Build succeeded!`，CPU 峰值 23%。
- `build.ps1`：通过，`[OK] Build succeeded!`，CPU 峰值 32%，产出 `Build/DesktopPet.exe`。
- `build.ps1 -RunTests`：通过，EditMode `failed=0`；结果为 160 用例、159 passed、0 failed、1 ignored（环境目录缺失导致的忽略不计为失败）。
- `node scripts/test/runtime_smoke.cjs --verbose`：隔离运行通过，生产数据未参与，测试后无残留 `DesktopPet` 进程。
- 五个节日主题：各自完成 `list/status/off`、四类 Unity 截图和退出检查；OS 级截图仅作辅助，正式视觉证据以 Unity 截图为准。
- 本轮为文档同步，不重新运行上述构建；若后续修改 C#，必须重新执行完整闭环。

## 五、给 codex 等代理的执行建议

1. **卡住先诊断，别盲目重试**：`diagnose_tuanjie.ps1` 30 秒内给出结论，比反复 `build.ps1` 干等 4 分钟高效。
2. **构建后核对 exe 时间戳**：`Get-Item Build\DesktopPet.exe | Select LastWriteTime`——确认包含最新提交（避免旧 exe 误测）。
3. **改动涉及外置窗口/渲染**：完整构建 + 冒烟 + 真机人工，三层都过才算完成。
4. **失败时记录**：把 `direct_compile.log` 尾部 20 行 + 卡死现象写进验收报告，供下一轮定位。

---

## 相关文档
- [`development-standards.md`](development-standards.md) §6.1 — 构建与测试命令规范
- [`code-truth-architecture.md`](code-truth-architecture.md) §八 — 构建与测试真相
- [`project-bugs-and-acceptance.md`](project-bugs-and-acceptance.md) §3.1 — 构建链路验收点
- [`ui-acceptance-checklist.md`](ui-acceptance-checklist.md) §六 — 验收执行脚本骨架
- `scripts/diagnose_tuanjie.ps1` — 构建环境诊断/恢复脚本（本文档核心工具）
