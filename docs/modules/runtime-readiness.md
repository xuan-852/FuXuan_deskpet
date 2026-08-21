# Runtime readiness and request recovery

This module documents the UX safety layer added on 2026-08-21.

## Architecture

`RuntimeReadinessService` reports local Ollama, bridge, and cloud configuration state. Cloud readiness is configuration-only; it never makes a paid probe. Local readiness is true only when Ollama responds and the configured model is present in `/api/tags`.

`ChatManager` exposes a request lifecycle through `RequestStage`, `RequestStatusText`, and `OnRequestStatusChanged`. The stages cover thinking, local generation, cloud connection, streaming, tool execution, retry, error, and cancellation.

`CancelCurrentRequest()` immediately stops the active coroutine, clears queued messages, releases the waiting state, and leaves the input usable. The embedded and external chat buttons use the same cancellation path.

## Iteration and recovery

The title status changes as the request moves through the lifecycle, so a slow local model, a network retry, or a tool execution is distinguishable from a frozen window.

## Cost protection

`--no-cloud` or `FU_XUAN_NO_CLOUD=1` blocks `ApiClient` requests before HTTP and token accounting. `--ollama` / `--local` continue to enforce local-only chat. No API key is stored in code or logs.

## Notes

## Verification

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
