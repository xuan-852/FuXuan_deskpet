# 修改说明 — 构建负载保护 / 测试门禁修复（2026-08-31）

> **文档作用**：记录本轮对 `build.ps1`、`BuildScript.cs`、`LocalToolRouter.cs` 及多个测试文件的修改——涵盖 **P0 构建过热/CPU 打满保护**、**`-RunTests` 不产出结果的门禁修复**、以及被其掩盖的 **4 个隐藏测试失败**的修复。
> **涉及范围**：`build.ps1`、`Assets/Editor/BuildScript.cs`、`Assets/Scripts/LocalToolRouter.cs`、`Assets/Editor/Tests/*`（6 个测试文件）、3 份文档（`build-workflow.md` / `task-inventory.md` / `project-bugs-and-acceptance.md`）。
> **验证结论**：本次全部改动均已在本机 full-access 下通过 —— 完整构建成功、`-RunTests` 全绿（160 用例 / 0 失败）、生产的 `DesktopPet.exe` 正常产出。

---

## 一、总览

本轮围绕「构建阶段的稳定性与可信度」做了三块工作，其中两块的根因是同一条链路：`build.ps1` 的 Tuanjie 调用方式。

| 修改 | 类型 | 说明 |
|------|------|------|
| P0 构建负载保护 | 性能/安全性 | 防止完整构建打满 CPU、过热/偶发重启 |
| `-RunTests` 门禁 | 缺陷修复 | 让测试真正执行并严格校验结果 |
| 4 个测试失败修复 | 缺陷修复 | 由「`-RunTests` 不再产出结果」暴露出的隐藏失败 |

---

## 二、P0：构建负载保护（解决构建打满 CPU / 过热）

### 问题
用户反馈：**每次重构都有高概率把 CPU 打满**，运行一久就触发 CPU 热保护。系统证据含 `Kernel-Power 41` 与 `WHEA-Logger 19 Internal parity error`（构建期硬件压力相关）。

### 根因（两个）
1. **`BuildScript.BuildDesktopPet()` 每次完整构建强制删除 `Library\Bee` 缓存** → 每次全量重编译（放弃增量），是重负载的主因。
2. **`build.ps1` 对 Tuanjie 构建无任何 CPU 限制** → Unity 拉起 Bee.Backend / Roslyn / IL2CPP 等子进程吃满全部核。

### 修复
**`Assets/Editor/BuildScript.cs`**：把 `Library\Bee` 缓存清理改为**显式开关**，默认保留走**增量构建**；抽成纯函数 `ShouldCleanBeeCache(string)` 便于单测。

```csharp
public static bool ShouldCleanBeeCache(string value)
    => string.Equals(value, "1", OrdinalIgnoreCase) || string.Equals(value, "true", OrdinalIgnoreCase);
```

**`build.ps1`** 新增构建负载保护（默认启用）：
- `-CleanBeeCache`：显式清 `Library\Bee` 强制全量（默认不清，增量）。
- `-MaxCores <n>`：限制可用逻辑核数（默认 = 半数逻辑核）。
- `-NoThrottle`：关闭节流（排查用）。
- 构建时把 Tuanjie 主进程 + 子进程（Bee.Backend/Roslyn/IL2CPP）的 **CPU 亲和掩码**限制到 `MaxCores` 核、优先级降为 **BelowNormal**，并以 2s 间隔监视子进程直到结束。

### 验证（i9-14900HX，32 逻辑核）
| 构建 | 结果 | 构建期 CPU 占用 |
|------|------|----------------|
| `build.ps1 -Quick` | `[OK] Build succeeded! (00:15)` | 均值 16~19% / 峰值 20~21% |
| `build.ps1`（完整构建） | `[OK] Build succeeded! (00:21)` + 产出 exe | 均值 15~19% / 峰值 21~26% |

> 此前未限制时经常打满 100%。限制到 16 核 + BelowNormal 后，配合增量构建（复用 `Library\Bee`），CPU 占用显著下降、不再持续满载。

### 遗留
- **硬件根因**（BIOS Intel 默认功耗 / XMP / 散热 / 主板供电 / PSU）非代码层可解决，仍需人工核查。
- `build.ps1` 目前只记录 CPU 占用 %，**未记录 CPU 温度/封装功耗/频率**（WMI 温度口部分硬件不可用），温度证据需人工观察或外部工具。
- 验收门槛「低负载/受保护完整构建连续通过至少 3 次」「无新 WHEA-Logger / Kernel-Power 41」需在可见播放器 + 人工观察下完成。

---

## 三、`-RunTests` 门禁修复（让测试真正执行）

### 问题
`build.ps1 -RunTests` 报 `[OK] Build succeeded!`，但 `logs/build/test_results.xml` 一直是旧的（2026-08-20 的 114/114），**从未真正执行测试**。

### 根因
`build.ps1` 在测试路径传了 `-runTests -testPlatform EditMode -testResults <file>`，但**同时传了 `-quit`**。`-quit` 会让 Tuanjie 在测试运行器写完结果前就提前退出 → 既不执行测试也不写结果。而 `build.ps1` 只看 `ExitCode` 判定成功（不看结果文件），于是误报成功。

### 修复
`build.ps1`：
1. `-RunTests` 路径**去掉 `-quit`**（`-Quick` 保留 `-quit` 做纯编译）。
2. 加**结果文件门禁**：跑前删除旧 `test_results.xml`；跑后校验根 `<test-run>` 的 `failed=0`（含 Ignore 时 NUnit 根结果可为 `Skipped:Ignored`，不算失败），否则退出码 1。不再信任退出码/编译成功。

### 验证
`build.ps1 -RunTests` → `[OK] EditMode 测试通过: ... (failed=0)` → `[OK] Build succeeded!`，结果文件为新鲜产出的 160 用例。

---

## 四、修复被 `-RunTests` 掩盖的 4 个隐藏测试失败

`-RunTests` 真正执行后，立即暴露 4 个此前从未被跑出来的失败。逐个判断根因并修复（3 个为测试过期/环境依赖，1 个为生产代码的安全缺口）。

### 4.1 `MemoryGovernanceTests.近似重复记忆合并而不是新增`
- **根因**：测试第一个 add 用 `source="system"`、`category=conversation`、`importance=5`，触发**有意的写入闸门** `system_conversation_too_weak`（`system`+`conversation`+`importance<7` 拒绝落库）。所以第一次 add 返回 False，`Assert.IsTrue` 失败。
- **修复**：把该 add 的 `source` 从 `system` 改为 `user`，使输入通过闸门、真正测到「近似记忆合并」路径。闸门本身是正确设计（其他测试也在测它）。

### 4.2 `LocalToolRouterTests.IntentAllowlistRejectsToolsOutsideCurrentRoute`
- **根因**：`file_delete` 出现在 `CommandTools`/`FallbackTools` 白名单里，`IsAllowed("file_delete", "command")` 返回 True；但测试（安全意图）要求它被拒。
- **修复（生产代码）**：`LocalToolRouter.IsAllowed` 现在对**危险工具**（`ToolRegistry.IsDangerous`：`file_delete`/`power`/`run_command`/`openclaw_task` 等）一律返回 False —— 危险工具不允许被本地 3B 模型自动规划执行，必须走主流程审批（`ToolConfirmManager`）。被拒时 `ChatManager` 只会输出「本地安全路由拒绝」提示，不会自动执行。
- **同步**：`EveryRegisteredToolHasANaturalLanguageRoute` 测试跳过危险工具（它们走审批而非自动白名单路由），与上述设计一致。

> ⚠️ **这是生产行为变更（安全方向）**：本地模型不再能一键执行破坏性工具，需走主流程审批。如需精确白名单而非一刀切排除，可再调整为逐工具控制。

### 4.3 `LocalRoleplayPromptBuilderTests.DefaultPromptUsesMicroContractAndCorrectAddressing`
- **根因**：prompt 模板已有「用多句短句组成**有内容的**完整回复」，但测试断言用的是旧串「用多句短句组成完整回复」（少了「有内容的」）→ `StringAssert.Contains` 失败。
- **修复**：更新测试断言为匹配当前模板的完整串（测试过期，非应用 bug）。

### 4.4 `DataPathConfigTests.EnsureDataRootCreatesOnlyResolvedDirectory`
- **根因**：本机存在旧目录 `D:\DesktopPetData`，而 `ResolveDataRoot()` 设计为「配置路径不存在且有旧目录时回退旧目录」，于是 `FU_XUAN_DATA=_tempRoot`（不存在）被解析到旧目录，`_tempRoot` 未创建 → 断言 `Directory.Exists(_tempRoot)` 失败。属**环境依赖**。
- **修复**：把断言改为检查「解析后的 `DataPathConfig.DataRoot`」目录存在，与环境无关；`EnsureDataRoot` 的契约（确保解析目录存在）不变。

---

## 五、顺带解决的工程问题

在排查中发现的关联问题一并修复：

- **`.ps1` 的 BOM 被剥掉导致解析失败**：用 `edit` 工具改 `build.ps1` 会去掉文件头的 UTF-8 BOM，无 BOM 时 PowerShell 解析器会把 UTF-8 中文按其它字节序解读，破坏 `param()` 结构 → 报「函数参数列表缺少 )」「缺少 }」等。已把 BOM 补回（`.editorconfig` 也要求 `.ps1` 带 BOM）。**注意：后续用 `edit` 改 `.ps1` 后需重新补 BOM。**
- **`Start-Process -NoNewWindow` 下 `ExitCode` 偶发为空**：导致构建成功后误判 `[FAIL]`。已在 `build.ps1` 加「按 `build-log` 内容兜底判定」（build-workflow 已知坑）。

---

## 六、改动文件清单

| 文件 | 改动 |
|------|------|
| `build.ps1` | P0 负载保护（增量 + `-MaxCores` + `-NoThrottle` + 亲和/优先级/子进程监视 + CPU% 记录）；`-RunTests` 去 `-quit` + 结果门禁；退出码兜底；BOM 修复 |
| `Assets/Editor/BuildScript.cs` | `Library\Bee` 清理改显式 `ShouldCleanBeeCache`，默认增量 |
| `Assets/Scripts/LocalToolRouter.cs` | `IsAllowed` 排除危险工具（需审批） |
| `Assets/Editor/Tests/BuildScriptBeeCacheTests.cs` | **新增**：Bee 缓存清理决策单测（默认增量 / 显式清理） |
| `Assets/Editor/Tests/MemoryGovernanceTests.cs` | 合并测试输入改 `user` 源 |
| `Assets/Editor/Tests/LocalToolRouterTests.cs` | 「每个工具都有路由」跳过危险工具 |
| `Assets/Editor/Tests/LocalRoleplayPromptBuilderTests.cs` | 微契约断言匹配当前模板 |
| `Assets/Editor/Tests/DataPathConfigTests.cs` | 断言改为解析后的 DataRoot（环境无关） |
| `docs/build-workflow.md` | P0 保护实现说明 + EditMode 验收门禁更新 |
| `docs/task-inventory.md` | P0 / EditMode 状态同步 + 2026-08-31 修复记录 |
| `docs/project-bugs-and-acceptance.md` | P0 状态：软件保护已落地，硬件未隔离 |

---

## 七、验证记录

- `node --check` 相关 JS、`build.ps1` AST 解析 → 通过。
- `.\build.ps1 -Quick` → `[OK] Build succeeded! (00:15)`，节流 16 核，CPU 均值 16~19%。
- `.\build.ps1`（完整构建）→ `[OK] Build succeeded! (00:21)`，产出 `DesktopPet.exe`，CPU 均值 15~19%/峰值 21~26%。
- `.\build.ps1 -RunTests` → `[OK] EditMode 测试通过 (failed=0)`，结果文件为 **160 用例 / 159 passed / 0 failed / 1 ignored**（ignore 为 `MissingConfiguredDirectoryFallsBackToExistingDefault` 在无默认目录机器上自我跳过，非失败）。
- 测试结果与生产数据隔离（`FU_XUAN_DATA` 临时目录 + `.test_mode`），生产记忆零污染。
- 无残留 DesktopPet/Tuanjie 进程。

---

## 八、待办 / 注意

1. **硬件根因**（BIOS/散热/PSU）需人工核查，软件开发层保护只是缓解。
2. **`LocalToolRouter` 行为变更**：危险工具不再被本地 3B 模型自动执行（需审批），若需逐工具精细控制请告知。
3. **BOM 坑**：以后用 `edit` 改 `.ps1` 后要确认 BOM 仍在（`EF BB BF`）。
4. 温度/功耗观测需人工或外部工具补齐，方能满足 P0 完整验收门槛。
