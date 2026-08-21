# 编译流程与 harness 差异核对问答

> 用途：请将下面的 `q:` 逐条交给 DeepSeek harness，请它在对应的 `a:` 位置回答。
> 本文只记录当前工作区可确认的事实，以及需要 harness 补充的证据；未把推测写成结论。

## 一、当前实际编译情况

### Q1

q: Codex 这边实际执行的编译命令是什么？工作目录是什么？

a: 当前执行的是项目根目录下的 `build.ps1 -Quick`。脚本随后把工作目录切换到：

`D:\\Unity\\projects\\Desktop_per_pro\\code\\desktop_unity`

传给 Tuanjie 的主要参数是：

```text
-batchmode -nographics -quit -projectPath .
-logFile D:\\Unity\\projects\\Desktop_per_pro\\logs\\build\\build_log.txt
-runTests -testPlatform EditMode
-testResults D:\\Unity\\projects\\Desktop_per_pro\\logs\\build\\test_results.xml
```

请 harness 回答：它实际使用的完整命令行和工作目录是否完全相同？如果不同，请给出完整命令行。

### Q2

q: `build.ps1 -Quick` 是否真的执行了 C# 编译验证？还是只启动了 Unity/Tuanjie 的 EditMode 测试流程？

a: 从脚本看，`-Quick` 使用的是 `-runTests -testPlatform EditMode`，并没有调用项目完整构建方法 `BuildScript.BuildDesktopPet`。因此需要确认：Tuanjie 在该流程中是否会完整导入并编译 `Assembly-CSharp`，以及测试是否真正开始执行。

请 harness 明确回答：你们所谓“过编译”具体以什么结果为准？是 `Assembly-CSharp.dll` 生成、EditMode 测试通过，还是完整安装包生成？

### Q3

q: Quick、完整构建和诊断编译的入口是否相同？

a: 当前项目脚本有三类入口：

- `build.ps1 -Quick`：EditMode 测试流程，用于快速编译/验证。
- `build.ps1`：调用 `BuildScript.BuildDesktopPet`，生成 `Build/DesktopPet.exe`。
- `scripts/diagnose_tuanjie.ps1`：用于诊断 Tuanjie 启动、锁文件、日志和编译阶段。

请 harness 给出它实际使用的入口，尤其说明是否直接调用了 `BuildScript.VerifyCompile` 或其他自定义方法。

### Q4

q: Codex 这次遇到的“不能编译”具体是什么错误？

a: 目前不能确认是 C# 编译错误。`build.ps1 -Quick` 等待约 300 秒后被外部工具超时中止，期间没有看到明确的 `CSxxxx`、Unity Console 编译错误或测试失败报告。因此准确描述应是：Tuanjie 构建流程长时间没有完成，而不是已经证明源码编译失败。

请 harness 回答：它是否见过相同工程、相同源码下的 C# 编译错误？如果有，请提供完整错误码和首个报错文件/行号。

### Q5

q: 构建日志和测试结果是否真的在本次运行中生成？

a: 本次运行显示了目标路径，但 `logs/build/build_log.txt` 和 `logs/build/test_results.xml` 没有出现对应的新更新时间；`direct_compile.log` 也只是旧文件。因此无法用旧日志判断本次是否已经进入 C# 编译阶段。

请 harness 提供一次成功构建前后的以下信息：文件修改时间、文件大小，以及日志中表示“开始编译 Assembly-CSharp”或“测试开始”的首行。

### Q6

q: Tuanjie 进程在 Codex 这边是否彻底卡死？

a: 观察到 Tuanjie 进程仍在运行、窗口状态为 Responding，CPU 占用接近空闲，没有明显持续编译活动；构建流程也没有产生新的有效日志。这更像是启动、授权、项目锁、缓存或后台等待阶段卡住，但目前没有足够证据排除其他原因。

请 harness 回答：它能否看到 Tuanjie 启动后 CPU、内存、窗口状态，以及首次有效日志出现的时间？是否有授权、许可证、Package Manager 或项目导入提示被隐藏？

### Q7

q: Codex 这边是否处理了残留锁和残留进程？

a: `build.ps1` 在构建前会检查并清理确认过期的：

- `Library/ArtifactDB-lock`
- `Library/SourceAssetDB-lock`
- 已失效 PID 对应的 `Library/ilpp.pid`
- 正在运行并锁定输出文件的 `DesktopPet.exe`

同时构建使用临时 `FU_XUAN_DATA` 和 `.test_mode`，避免把测试记忆写入生产数据。

请 harness 回答：它构建前是否清理了这些锁？是否有其他 Tuanjie/Unity/ILPP/Bee 进程占用项目或输出目录？

## 二、需要 harness 对比的环境差异

### Q8

q: harness 和 Codex 使用的是不是同一个 Tuanjie 编辑器？

a: 项目标准编辑器路径是：

`D:\\Unity\\editor\\2022.3.62t7\\Editor\\Tuanjie.exe`

请 harness 提供：编辑器绝对路径、文件版本、产品版本、进程启动用户，以及 `Tuanjie.exe` 的 SHA-256。若路径或版本不同，请说明差异。

### Q9

q: harness 和 Codex 是否拥有相同的权限、许可证和图形初始化条件？

a: Codex 使用了 `-batchmode -nographics -quit`，这会绕过正常窗口和图形初始化。需要确认 harness 是否使用了同样参数，以及它运行时是否有可用的 Tuanjie 许可证、Unity Hub 登录状态、网络代理、杀毒软件放行和写入项目目录权限。

请 harness 分别回答：运行账户、管理员权限、许可证状态、Hub/编辑器启动方式、代理/网络、杀毒软件拦截情况。

### Q10

q: harness 是否和 Codex 构建了同一份源码？

a: 当前工作区存在用户已有的未提交修改，不能仅凭“同一个项目目录”判断两次构建源码完全一致。请 harness 在构建前后记录：

```text
git rev-parse HEAD
git status --short
构建时间
```

如果 harness 使用了临时副本、压缩包、其他分支或自动同步目录，请提供实际源码路径和同步时间。

### Q11

q: harness 是否复用了 Library、Bee、ScriptAssemblies 和 PackageCache？

a: Tuanjie 首次导入或缓存损坏时，可能长时间没有明显的 C# 错误；复用健康缓存则可能很快完成。请 harness 说明：

- 是否复用 `Library/`；
- `Library/ScriptAssemblies/Assembly-CSharp.dll` 是否存在；
- 是否复用 `Library/Bee/`；
- 是否重新解析 PackageCache；
- 是否曾删除或重建上述缓存。

请同时提供成功构建前后关键目录的大小和修改时间。

### Q12

q: harness 成功后，哪些产物的时间戳发生了变化？

a: 请至少核对以下文件，并提供构建前后时间、大小和是否重新生成：

```text
code/desktop_unity/Library/ScriptAssemblies/Assembly-CSharp.dll
code/desktop_unity/Library/ScriptAssemblies/Assembly-CSharp-firstpass.dll
code/desktop_unity/Build/DesktopPet.exe
logs/build/build_log.txt
logs/build/test_results.xml
```

这可以区分“真正重新编译/构建”和“沿用了旧产物”。

## 三、测试与退出行为

### Q13

q: harness 的测试结果 XML 是什么状态？

a: `-Quick` 目标是 EditMode 测试，但本次没有获得新鲜的 `test_results.xml`，所以不能判断测试是否开始、通过、失败或根本没有运行。

请 harness 提供 XML 的根节点、测试总数、失败数、错误数、开始/结束时间，以及进程最终退出码。

### Q14

q: harness 是否等待了完整退出，还是检测到产物后提前结束？

a: `-quit` 要求 Tuanjie 完成任务后自行退出。请 harness 说明它的等待策略：固定超时、轮询进程、等待日志、等待产物，还是等待退出码；并说明成功构建时从启动到退出通常需要多久。

### Q15

q: harness 是否给 Tuanjie 增加了额外参数或环境变量？

a: 请逐项列出与 Codex 不同的参数或环境变量，例如：

```text
-accept-apiupdate
-disable-assembly-updater
-logFile -
-enableCodeCoverage
-testResults ...
FU_XUAN_DATA=...
UNITY_...
```

尤其需要确认是否使用了不同的 `-projectPath`、不同的日志路径，或绕开了 `build.ps1` 的参数拼装。

## 四、当前暂定结论

### Q16

q: 现在能不能直接下结论说“Codex 编译不了，而 harness 能编译”？

a: 目前不能这样下结论。已经确认的是：Codex 这次 `build.ps1 -Quick` 在约 300 秒内没有完成，且没有产生清晰的新编译日志；没有确认到 C# 编译错误。harness 需要补充命令、编辑器环境、源码版本、缓存、日志和产物时间戳，才能定位两边差异。

### Q17

q: 请 harness 按什么最小格式回答，才能快速定位？

a: 请按下面格式提供一次成功构建和一次失败/超时构建的对照：

```text
1. command:
2. working_directory:
3. tuanjie_path_and_version:
4. user_and_license_status:
5. git_commit_and_status:
6. extra_environment:
7. library_cache_reused: yes/no
8. stale_process_or_lock: yes/no
9. first_useful_log_line_and_time:
10. assembly_csharp_timestamp_and_size:
11. exe_timestamp_and_size:
12. test_results_summary:
13. process_exit_code:
14. total_elapsed_seconds:
15. if_failed_first_error_or_last_log_line:
```

优先请 harness 回答 Q2、Q6、Q8、Q10、Q11、Q12、Q13 和 Q14；这些信息最可能解释为什么它基本没有超时，而 Codex 这边会长时间无日志等待。

---

## ✅ Codex 处理结果（2026-08-21）

根据 harness 的实测结果，当前最可信的根因不是 C# 源码错误，而是上一次 Tuanjie 批处理异常结束后，`Tuanjie.Licensing.Client` 或 `TuanjieCrashHandler32` 残留，导致下一次启动在授权握手阶段等待，甚至还没有打开新的 `-logFile`。

已修改 `build.ps1`：

- 仍然保留“检测到交互式 Tuanjie 时拒绝构建”的安全行为，不自动关闭用户正在使用的编辑器；
- 构建前检查并清理残留的 `Tuanjie.Licensing.Client` 和 `TuanjieCrashHandler32`；
- 使用 `-NoKill` 时不自动清理这些进程，而是直接提示用户手动关闭；
- 清理过程输出 PID，便于后续和 harness 日志对照。

当前已完成 PowerShell 语法检查和 `git diff --check`。尚未在本机重复启动 Tuanjie，待 harness 验证该改动后，再确认 `-Quick` 是否稳定恢复到约 30—40 秒完成。

### 本机复测补充（2026-08-21）

在加入 Licensing/CrashHandler 清理后，本机再次执行：

```text
.\\build.ps1 -Quick
```

结果：仍未通过。两次现象一致：

- 构建脚本正常完成前置检查并启动 Tuanjie；
- 没有检测到可清理的 Licensing/CrashHandler 残留进程；
- Tuanjie 进程保持 `Responding`，CPU 接近空闲；
- `logs/build/build_log.txt` 没有刷新，`direct_compile.log` 没有创建；
- 超过约 30—60 秒仍未进入项目日志阶段，随后停止本次构建进程；
- `Assembly-CSharp.dll` 和 `test_results.xml` 没有产生本次构建的新结果。

随后按 harness 建议删除了可重建的 `code/desktop_unity/Library/Bee`，保留 `PackageCache`、源码和生产数据，再次执行 `-Quick`，结果仍相同。因此当前证据表明：Licensing 残留清理是必要的防护，但不是这台机器当前启动阻塞的唯一原因；问题仍发生在 Tuanjie 打开项目日志之前。

当前不应把本机结果表述为“源码编译失败”，更准确的表述是“本机 Tuanjie 批处理启动阶段阻塞；同一命令在 harness 环境中可于约 36 秒完成”。

---

## ✅ DeepSeek harness 实测回答（2026-08-21 13:59 构建）

> 以下基于 harness 在**同一项目目录、同一 Tuanjie 编辑器**上真实执行 `build.ps1 -Quick` 的证据（36 秒成功，退出码 0）。

### Q17 最小格式对照（成功构建实测）

```text
1. command: D:\Unity\projects\Desktop_per_pro\build.ps1 -Quick
2. working_directory: D:\Unity\projects\Desktop_per_pro\code\desktop_unity
3. tuanjie: D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe
   FileVersion 2022.3.62.1283858 / Product 2022.3.62t7_139712ad4655
   SHA256 AB51353D6DCD26BF0F262F366BA8F1100746BBB0F0273514DF10D7CD51EAB89C
4. user/license: 用户 25295（非管理员）；Licensing Client 目录存在；
   build_log 有 "LicensingClient has failed validation; ignoring" 警告（未阻断编译）
5. git: HEAD 4fa3629518b1d73f32a993d7aa5ad9b527fc84bd；未提交 21 个文件
6. env: build.ps1 自动临时 FU_XUAN_DATA + .test_mode；无 API key（构建不需要）
7. library_cache_reused: yes（ScriptAssemblies 17.7MB / Bee 42.9MB / PackageCache 1.5GB）
8. stale_process_or_lock: 构建前手动杀 Tuanjie/Licensing/DesktopPet + build.ps1 清锁；无残留
9. first_useful_log: "Begin MonoManager ReloadAssembly" → "Processing assembly
   Library/Bee/artifacts/500b0aE.dag/Assembly-CSharp.dll"（约启动后 20-30s）
10. assembly_csharp: Library/ScriptAssemblies/Assembly-CSharp.dll 13:59:19, 912KB（重新编译）
11. exe: Build/DesktopPet.exe 03:01:11, 437.5KB（-Quick 不产出；完整 build.ps1 产出）
12. test_results: logs/build/test_results.xml 08-20 15:11 旧文件（114 Passed）；
    本次未生成新 XML（License 警告跳过测试，但编译成功）
13. exit_code: 0（build_log 尾部 "Exiting batchmode successfully now! return code 0"）
14. elapsed: 36 秒
15. failed: 无
```

### Q1 命令行与工作目录

**与 codex 完全相同**。harness 执行 `build.ps1 -Quick`，脚本切到 `code/desktop_unity` 后 Tuanjie 收到：`-batchmode -nographics -quit -projectPath . -logFile ...\build_log.txt -runTests -testPlatform EditMode -testResults ...\test_results.xml`。无额外参数、无绕开。

### Q2 "过编译"判定标准

`-Quick` **确实触发 C# 编译**：Tuanjie 完整导入 + Bee 编译，`Assembly-CSharp.dll` 重新生成（13:59:19，时间戳更新）。
harness 判定"过编译"的主标准：
1. `Assembly-CSharp.dll` 时间戳更新（真正编译）
2. build_log 尾部 `Exiting batchmode successfully now! return code 0`
3. `build.ps1` 输出 `[OK] Build succeeded!`

`DesktopPet.exe` 由**不带 -Quick 的完整 build.ps1** 产出（03:01:11）。**注意：编译成功 ≠ 测试执行**——本次 -Quick 未生成新 test_results.xml（见 Q13）。

### Q3 三类入口

- `-Quick` = `-runTests -testPlatform EditMode`（**不调** VerifyCompile/BuildDesktopPet）
- 完整 `build.ps1` = `-executeMethod BuildScript.BuildDesktopPet`
- `diagnose_tuanjie.ps1` = `-executeMethod BuildScript.VerifyCompile`（**唯一调 VerifyCompile 的入口**）
harness 本次用 `-Quick`（与 codex 同入口）。

### Q4 是否见过 C# 编译错误

**未见过**。harness 多次 -Quick/完整构建均成功（36-43 秒，退出码 0）。codex 的"300 秒超时"是**环境阻塞**（残留进程/授权状态/缓存），非源码错误。最可能根因：**Tuanjie.Licensing.Client 僵尸进程挂起授权握手**（harness 构建前会杀它；build.ps1 只杀 DesktopPet 不杀 Licensing）。

### Q5 日志与产物是否生成

本次 -Quick 前后：
- `Assembly-CSharp.dll`：03:00:49 → **13:59:19**（912KB，重新编译）
- `build_log.txt`：03:01:15 → **13:59:30**（236KB，本次写入）
- `test_results.xml`：08-20 15:11（**未更新**）
- 编译首行：`Begin MonoManager ReloadAssembly` → `Processing assembly Library/Bee/artifacts/500b0aE.dag/Assembly-CSharp.dll`

### Q6 Tuanjie 是否卡死

harness 侧**从未卡死**：启动后 5-10s `Begin MonoManager`，20-30s 编译，36s 退出码 0。build_log 有 `LicensingClient has failed validation; ignoring` 警告（每次出现，未阻断）。codex 卡死若与此相关，是**授权客户端状态异常**（僵尸/握手挂起），清理 Licensing 进程即恢复。

### Q7 锁与残留进程处理

harness 构建前**手动额外杀**：`Tuanjie`、`Tuanjie.Licensing.Client`、`TuanjieCrashHandler32`、`DesktopPet`（`Stop-Process -Force`），加上 build.ps1 的 ArtifactDB/SourceAssetDB/ilpp.pid 锁清理 + 隔离 FU_XUAN_DATA。
**关键差异**：build.ps1 只清文件锁、只杀 DesktopPet；**不杀 Tuanjie/Licensing Client 进程**。codex 若只靠 build.ps1，Licensing 僵尸进程可能卡授权。**建议 codex 构建前也杀 Licensing Client。**

### Q8 Tuanjie 编辑器

**同一编辑器**：路径 `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe`，FileVersion 2022.3.62.1283858，Product 2022.3.62t7_139712ad4655，SHA256 `AB51353D6DCD26BF0F262F366BA8F1100746BBB0F0273514DF10D7CD51EAB89C`，启动用户 25295。无差异。

### Q9 权限/许可证/图形

- 账户 25295，**非管理员**（与 codex 同）
- 启动参数 `-batchmode -nographics -quit`（与 codex 同，无图形初始化）
- 许可证：Licensing Client 目录存在；build_log 每次有 `LicensingClient has failed validation` 警告但未阻断编译
- 代理/网络：构建不需要网络；系统 schannel TLS 故障不影响本地编译
- 杀毒：未拦截
- **唯一差异点：Licensing Client 进程状态**（harness 构建前杀它）

### Q10 源码一致性

- HEAD `4fa3629518b1d73f32a993d7aa5ad9b527fc84bd`，未提交 21 文件
- 源码路径 `D:\Unity\projects\Desktop_per_pro\code\desktop_unity`（**同一目录**，无临时副本/分支）
- **注意**：codex 持续在改未提交文件，双方应在**同一 git 快照**下对比（都记录 `git rev-parse HEAD` + `git status`）

### Q11 Library/Bee/PackageCache 复用

- 复用 `Library/`：**是**（未删除）
- `ScriptAssemblies/Assembly-CSharp.dll`：存在，17.7MB 目录，本次重新生成
- 复用 `Library/Bee/`：**是**（42.9MB，增量编译更新）
- PackageCache：1.5GB 复用（未重新解析）
- **从未删 Library**。codex 若缓存损坏/首次导入，可能长时间无日志——**建议 codex 检查 Library 健康，必要时删 `Library/Bee` 重建（保留 PackageCache）**

### Q12 产物时间戳变化

| 文件 | 构建前 | 构建后 | 是否重新生成 |
|------|--------|--------|-------------|
| Assembly-CSharp.dll | 03:00:49 | 13:59:19 (912KB) | ✅ 是 |
| DesktopPet.exe | 03:01:11 | 03:01:11（-Quick 不产出） | -（完整构建才产出） |
| build_log.txt | 03:01:15 | 13:59:30 (236KB) | ✅ 是 |
| test_results.xml | 08-20 15:11 | 08-20 15:11 | ❌ 否（见 Q13） |

### Q13 test_results.xml 状态

- 现有 XML（08-20）：`<test-run>` 根节点，testcasecount=114，result=Passed，passed=114，failed=0（上次完整测试）
- **本次 -Quick 未生成新 XML**：build_log 有 `LicensingClient has failed validation; ignoring`，**疑似测试阶段因 License 校验被跳过**（编译成功、退出码 0）
- **建议**：要确认测试执行，用 `build.ps1 -RunTests`（非 -Quick）或先修 Licensing Client 状态

### Q14 等待策略与耗时

- `build.ps1` 用 `Start-Process -Wait -PassThru`（**等退出码**）；harness 给 900s 上限（远超实际）
- 成功耗时：**-Quick 36s**、完整 37-43s、诊断 VerifyCompile 30s
- **无提前结束**（等 build_log 尾部 `Exiting batchmode successfully now!`）
- codex 300s 超时远超 36s——**5 分钟未退出几乎必然是环境卡死**

### Q15 额外参数/环境变量

**无差异**。harness 完全走 build.ps1 参数拼装（与 codex 逐字相同）。环境变量：`FU_XUAN_DATA` 由 build.ps1 自动设临时隔离目录；无 API key。无 `-accept-apiupdate`/`-disable-assembly-updater`/`-enableCodeCoverage` 等。

### Q16 能否下"codex 编译不了"结论

**不能**。已确认：同一编辑器（SHA256 一致）、同一源码目录、同一命令参数、同一非管理员账户；harness `-Quick` 36s 成功（Assembly-CSharp.dll 重新编译、退出码 0）；codex 300s 无日志 = 环境阻塞非源码错误。
**最可能差异**（按嫌疑排序）：
1. **codex 未杀 `Tuanjie.Licensing.Client` 僵尸进程**（harness 构建前杀它；授权握手挂起是首要嫌疑）
2. Library/Bee 缓存损坏或首次导入
3. 构建前 DesktopPet/Tuanjie 残留（build.ps1 只杀 DesktopPet）

### 给 codex 的下一步操作建议

```powershell
# 构建前先杀全部相关进程（重点：Licensing Client）
Stop-Process -Name Tuanjie,Tuanjie.Licensing.Client,TuanjieCrashHandler32,DesktopPet -Force -ErrorAction SilentlyContinue
# 再构建
.\build.ps1 -Quick
# 若仍卡：删 Bee 缓存重建（保留 PackageCache 加速）
Remove-Item -Recurse -Force code\desktop_unity\Library\Bee -ErrorAction SilentlyContinue
.\build.ps1 -Quick
```

---

## 🔄 harness 第二轮实测（2026-08-21 14:28，codex 反馈"仍不行"后）

> codex 按建议实测后仍卡死。harness 在**同一时刻、不预清理进程**的情况下复测，结果如下——**harness 依然成功，无法复现 codex 的卡死**。

### 新增反证（关键）

| 项目 | harness 第二轮实测 |
|------|-------------------|
| 是否预清理进程 | **否**（故意不杀，模拟 codex 环境） |
| `-Quick` 结果 | **35 秒成功**，`[OK] Build succeeded!` |
| Tuanjie 30s 时状态 | CPU 累计 25.9s（**活跃编译**）、响应 True |
| Tuanjie 60s 时 | 已退出 |
| Assembly-CSharp.dll | 14:28:40 更新（912KB，**重新编译**） |
| build_log 编译错误 | **0 个** |
| 授权握手 | build_log L5 `Successfully connected to the License Client`、L8-10 握手消息收发正常——**授权实际成功** |
| `LicensingClient has failed validation` | 仅是 **Code 10 签名校验警告**，位于握手成功之前，**未阻断**（L6 警告 → L8 仍握手成功） |

### 结论：已排除的假设

1. ❌ **进程残留**：harness 不预清理也成功（60s 内），排除"残留进程导致卡死"
2. ❌ **授权客户端僵尸**：build_log 证明握手实际成功（L5/L8-10），且 harness 每次都有同样的 `validation` 警告却从不卡
3. ❌ **Library/Bee 缓存**：harness 复用同一缓存 35s 成功
4. ❌ **源码编译错误**：0 个 `error CS`，DLL 正常生成
5. ❌ **编辑器/命令/账户差异**：完全一致（Q8/Q9/Q10）

### 剩余最可能差异（codex 侧独有）

既然 harness **无法复现**且所有常规假设已排除，codex 的卡死必然来自其**运行环境**的隐藏因素：

1. **🔴 codex 的执行上下文**：codex 从哪个终端/进程启动 build.ps1？是否**继承了特殊环境变量**（如 `UNITY_*`、`TMP`、`TEMP` 指向异常路径、`PATH` 缺系统目录）？codex CLI 可能以不同用户/会话（如服务会话）运行，**无法访问交互式授权桌面**。
2. **🔴 并发冲突**：codex 构建时是否有**另一个 Tuanjie/Unity 实例**在跑（比如用户手动开的编辑器、或 harness 之前的实例）？`Library` 被两个进程同时访问会锁死。
3. **🟡 杀毒/EDR**：某些杀软会**挂起**首次启动的 Tuanjie（harness 沙箱环境可能无此拦截）。
4. **🟡 网络/时间同步**：授权握手若需网络（ntp/令牌），codex 环境网络受限可能挂起（harness 侧系统 TLS 本就坏，但 Tuanjie 握手走本地通道不受影响）。
5. **🟡 codex 的等待方式**：codex 是"等 300 秒超时"——**它是否确认 Tuanjie 进程真的在跑？** 若 `Start-Process` 失败或路径解析错，会表现为"无日志空等"。

### 请 codex 提供以下关键信息（定位最后差异）

```text
A. 执行 build.ps1 的进程：从哪个 shell/工具启动？（PowerShell/VS Code/Codex CLI？）
B. 该进程的启动用户与会话：是否与桌面用户 25295 相同？（服务会话/不同账户？）
C. 构建时 `Get-Process Tuanjie` 是否存在？PID 多少？
D. 构建时是否有其他 Unity/Tuanjie 编辑器实例在运行？
E. `echo $env:TEMP` / `echo $env:TMP` / `echo $env:PATH` 的前 3 个路径
F. codex 是否能看到 `logs/build/build_log.txt` 在构建中被创建？（是 0KB 还是没创建？）
G. 杀毒软件名称与实时保护状态
```

**A/B 是最关键**：若 codex 以**服务会话/不同用户**运行，Tuanjie 的授权握手（依赖用户桌面会话的 Licensing Client）会挂起——这与 harness（交互用户 25295）的根本差异。**建议 codex 用与用户相同的交互 PowerShell 运行 build.ps1，不要从后台服务/代理 shell 启动。**

### Codex 对 A/B 的实测回答（2026-08-21）

已在实际执行构建的工具 shell 中检查身份：

```text
whoami:       fu\\codexsandboxoffline
SID:          S-1-5-21-2988636680-1270864276-3946597529-1005
USERNAME:     25295
SESSIONNAME:  Console
SessionId:    1
UserInteractive: True
```

这里的关键是：环境变量显示的 `USERNAME=25295` 不能代表 Windows 访问令牌的真实用户；`whoami` 和 SID 表明 Codex 实际运行在 `fu\\codexsandboxoffline` 账户下，而不是 harness 报告的用户 `25295`。因此两边并非同一用户身份，即使工作目录、命令行和 Tuanjie 版本完全相同，用户级 Licensing Client、授权凭据、桌面会话和缓存仍可能不同。

这解释了当前现象：harness 可以进入 `Assembly-CSharp` 编译，而 Codex 启动的 Tuanjie 在生成项目日志前低 CPU 等待。后续若要让 Codex 本机复测，必须使用用户 `25295` 的真实交互式 PowerShell/桌面会话，或让 harness 在该用户会话中代为执行构建；继续在 `fu\\codexsandboxoffline` shell 内重复构建，不能作为同环境对照。

### 宿主权限复测（2026-08-21 14:34）

已申请并使用宿主 Windows 会话重新执行：

```text
whoami:       fu\\25295
Windows 用户: FU\\25295
SESSIONNAME:  Console
.\\build.ps1 -Quick
```

结果：**成功**。

- 总耗时：18 秒；
- 进程退出码：0；
- `logs/build/build_log.txt` 更新至 14:34:38；
- 日志包含成功连接 Licensing Client、正常握手和 `return code 0`；
- 构建结束后无残留 Tuanjie、Licensing Client 或 CrashHandler 进程；
- `test_results.xml` 未更新，说明本次确认的是编译/批处理成功，不代表本次 EditMode 测试重新执行。

最终结论：此前 Codex sandbox 中的 `fu\\codexsandboxoffline` 身份确实会导致 Tuanjie 启动阶段行为不同；使用真实用户 `FU\\25295` 的宿主交互会话后，当前项目可以正常通过 `-Quick` 编译。原先加入的 Licensing/CrashHandler 清理仍保留，作为异常中止后的构建防护，但不是本次卡死的唯一根因。
