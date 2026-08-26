# 编译工作流（AI 必读：如何正确构建本项目的 Unity 桌宠）

> **文档作用**: 让任何 AI 代理（codex / Copilot / Claude 等）**第一次就能正确编译**本项目。本文固化 2026-08-17 实战经验：Tuanjie batchmode 偶发启动卡死（授权/残留进程导致），以及绕过它的标准手段。**改任何 C# 代码后按本文流程验证**；构建卡死时按「卡死处理」章节行动，**不要重复直接重试**。
> **基本架构**: 构建入口 `build.ps1`（包装 Tuanjie.exe batchmode）；诊断脚本 `scripts/diagnose_tuanjie.ps1`（清理残留+验授权+试编译）；验证闭环 = Quick → 完整构建 → EditMode → 隔离冒烟。
> **开发历史迭代**: 2026-08-17 多次遇到「Tuanjie 启动 4 分钟无日志、log 未创建、CPU 不动」——根因是环境时序（残留进程/授权客户端状态），非代码问题；诊断脚本 + 杀残留后一次通过。

---

> **2026-08-19 构建修复**：`build.ps1 -Quick` 现在复用 EditMode harness 编译路径，并自动使用临时 `FU_XUAN_DATA/.test_mode`；Quick/完整构建均带 `-nographics`。Tuanjie 必须在 full-access 环境启动，否则 Windows 沙箱可能在授权初始化阶段卡死且不创建日志。构建脚本会在确认没有 Tuanjie 进程后清理 `ArtifactDB-lock` / `SourceAssetDB-lock`，并在确认 ILPP PID 已退出后清理陈旧的 `Library\ilpp.pid`。

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
```

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
4. **ILPP 陈旧 PID**：强杀 Tuanjie 后若 `Library\ilpp.pid` 仍存在，先确认其中 PID 已不存在；新版 `build.ps1` 会自动安全清理，活动 PID 则拒绝删除。
4. **32 位进程无 `SetWindowLongPtrW/GetWindowLongPtrW`**（`EntryPointNotFoundException` 杀线程）——P/Invoke 用 `SetWindowLongW/GetWindowLongW`。
5. **PS 5.1 无 `?:` 三元运算符**——脚本里避免（构建脚本本身已兼容）。
6. **`direct_compile.log` 留痕**：诊断脚本在 `logs/build/direct_compile.log` 留下最近一次试编译日志，作为判断依据。

## 四、验证闭环与验收门槛

| 层级 | 命令 | 通过标准 |
|------|------|---------|
| 编译 | `.\build.ps1 -Quick` | `[OK] Build succeeded! (mm:ss)` |
| 完整构建 | `.\build.ps1` | `[OK] Output: Build/DesktopPet.exe` |
| EditMode | `.\build.ps1 -RunTests` | `test_results.xml` 中 `result=Passed` 且 `failed=0`；现有历史产物为 114/114 |
| 冒烟 | `node scripts/test/runtime_smoke.cjs` | `[PASS]` + 生产记忆零污染 |
| 人工 | 启动 exe | 桌宠落地、功能正常 |

> **AI 铁则**：功能级改动**必须先编译+测试通过**，再更新模块文档；文档只记录已验证的代码真相（AGENTS.md）。

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
