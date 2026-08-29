# 运行时状态与请求恢复

> **文档作用**: 描述桌宠启动自检、请求状态、取消/失败恢复、云端保护和外置窗口恢复；修改启动依赖、请求生命周期或退出流程前必读。
> **基本架构**: `RuntimeReadinessService` 负责本地/桥接/云端就绪状态，`ChatManager` 负责请求生命周期，`DesktopPet` 与 `WindowOverlay` 负责关闭、DWM 和置顶恢复。
> **开发历史迭代**: 2026-08-21 建立就绪层；2026-08-26 补充异常会话、DWM 重建和置顶看门狗；2026-08-27 补充桥接任务取消与请求失败最终态。
> **编写注意事项**: 云端就绪检查不得发起付费探测；测试必须使用隔离数据目录；构建被权限或宿主环境阻断时只能记录为未验证。

本模块记录 2026-08-21 起加入的运行时安全层，以下内容以当前代码和验证记录为准。

## 一、文档作用

本模块供修改启动依赖、请求生命周期、取消恢复、外置窗口和云端保护的开发者与 AI 代理使用。

## 二、基本架构

`RuntimeReadinessService` reports local Ollama, bridge, and cloud configuration state. Cloud readiness is configuration-only; it never makes a paid probe. Local readiness is true only when Ollama responds and the configured model is present in `/api/tags`. In a standalone player, `LocalLLMClient` can start the standard Ollama application/CLI when the first local health check finds the API offline, then performs one delayed retry.

`ChatManager` exposes a request lifecycle through `RequestStage`, `RequestStatusText`, and `OnRequestStatusChanged`. The stages cover thinking, local generation, cloud connection, streaming, tool execution, retry, error, and cancellation.

`CancelCurrentRequest()` immediately stops the active coroutine, clears queued messages, releases the waiting state, and leaves the input usable. The embedded and external chat buttons use the same cancellation path.

`ChatManager` now distinguishes a published final reply from a failed request. Only a published reply transitions the status back to `Idle` and starts quality review, reflection, personality evolution, and background knowledge work; failures keep `Error` visible. When a queued message starts, the completed coroutine clears its own reference before launching the next one, so the watchdog and cancellation path retain the new coroutine handle.

OpenClaw task cancellation has a separate bridge-level guard: `OpenClawBridge.RequestTaskCancellation()` is non-blocking and is called during `DesktopPet.BeginShutdown()`. The task loop checks it before polling, then confirms cancellation through `/task/{id}/cancel`; a 2xx transport response without `success=true` is not treated as a successful cancellation.

## 三、开发历史迭代

The title status changes as the request moves through the lifecycle, so a slow local model, a network retry, or a tool execution is distinguishable from a frozen window.

DesktopPet startup recovery uses a consecutive-abnormal-session watchdog. Production `PlayerPrefs` are not modified in `.test_mode`; a session that remains stable for 60 seconds clears the abnormal counter and the DWM fallback flag. After sleep or GPU recovery, `WindowOverlay` still performs a delayed DWM rebuild in fallback mode instead of returning early with an invisible layered window.

`WindowOverlay` also maintains a lightweight topmost watchdog. Every two seconds it reapplies `HWND_TOPMOST` without activating the window, restores a hidden main window, or reacquires a stale Unity handle and reapplies the full overlay configuration. This prevents external windows, tray restore, and DWM resets from silently lowering the pet's Z-order.

## 四、编写注意事项

### 成本保护

`--no-cloud` or `FU_XUAN_NO_CLOUD=1` blocks `ApiClient` requests before HTTP and token accounting. `--ollama` / `--local` continue to enforce local-only chat. No API key is stored in code or logs.

### 其他注意事项

- Ollama bootstrap is disabled in the Unity Editor and `.test_mode`, uses `OLLAMA_EXE` first and then the standard per-user/Program Files paths, and has a 60-second launch cooldown to avoid spawning duplicate processes during repeated readiness checks.
- DesktopPet autostart is stored as a Windows `REG_SZ`; the registry bridge must encode values as UTF-16LE. The reader keeps a UTF-8 fallback so values written by older builds are migrated on the next successful startup.
- Build/test force-termination must not be treated as proof of a DesktopPet crash. The watchdog therefore resets after a stable session and skips production crash state entirely in `.test_mode`.

### 验证方法

Run the project diagnostic after adding or changing C#:

```powershell
.\scripts\diagnose_tuanjie.ps1 -StartupTimeoutSec 120
.\build.ps1 -RunTests -DataRoot $isolatedData
```

The data root must be temporary and contain `.test_mode`; production memory and personality files must not be used by automated tests. The Tuanjie wrapper may report an empty process exit code in the host environment, so `direct_compile.log` and the actual compiler error list are the authoritative compile evidence.

## 2026-08-21 verification

- Host user `FU\\25295`: `build.ps1 -RunTests` completed with exit code 0 and refreshed the build log; the editor did not refresh `test_results.xml`, so the existing 114/114 XML is not counted as a fresh test run.
- Host user `FU\\25295`: full `build.ps1` completed in 45 seconds and regenerated `Build/DesktopPet.exe` at 14:48:27.
- `node --check code/desktop_unity/openclaw_bridge.js`: passed.
- `node scripts/test/runtime_smoke.cjs --verbose`: passed. The isolated run covered view switching, external-panel clicks, approval injection, three window sizes, zero `NullReferenceException`, and unchanged production memory mtimes; the temporary test directory was removed afterward.
- 2026-08-24: `build.ps1 -Quick` passed under the real user account after the autostart registry encoding fix and Ollama bootstrap change. A fresh EditMode XML result was not produced by this invocation, so the historical test count is not treated as a new test run.
- 2026-08-26: `build.ps1 -Quick` and full `build.ps1` passed after the crash-watchdog/DWM recovery change. Isolated `runtime_smoke.cjs --verbose` passed with zero NRE and zero production-memory pollution; the production player logged stable-session confirmation after 60 seconds and completed DWM transparency setup.
- 2026-08-26: `build.ps1 -Quick` and full `build.ps1` passed after adding the non-activating topmost watchdog. The rebuilt player started successfully and reported DWM transparency setup; elevated process inspection confirmed the player remained running in the interactive session.
- 2026-08-26: `DesktopPet` now routes tray exit, test exit, `OnApplicationQuit`, and `OnDestroy` through one idempotent `BeginShutdown` path; it closes the external window thread before Unity render resources are destroyed and avoids manually invoking `OnDestroy`.
- 2026-08-26: `ExternalChatWindow.Shutdown()` now records the shutdown request before checking `IsCreated`; a window thread that is still between startup and `IsCreated=true` exits before completing native-window initialization, and an already-exiting thread cannot be duplicated by `EnsureCreated()`.
- 2026-08-26: Quick and full build passed; the rebuilt `Build/DesktopPet.exe` passed isolated `runtime_smoke.cjs --verbose` with all view/external-click/approval/emote paths, three window sizes, zero NRE, and unchanged production-memory mtimes. The EditMode XML was not refreshed and remains historical 114/114, so it is not counted as a fresh test result.
- 2026-08-27: Quick and full build passed after bridge task-cancellation/error-classification changes; isolated `runtime_smoke.cjs --verbose` passed with zero NRE, complete self-exit, and zero production-memory pollution. The EditMode XML remains historical 114/114 and is not counted as a fresh test result.
- 2026-08-27: `ChatManager` request finalization now keeps failed requests in `RequestStage.Error`, skips success-only quality/background work, and clears the completed coroutine reference before starting a queued request. Quick/full build and isolated `runtime_smoke.cjs --verbose` passed; the EditMode XML remains historical 114/114.
