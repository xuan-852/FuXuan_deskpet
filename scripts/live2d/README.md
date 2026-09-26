# Live2D tooling platform

First slice: Node CLI orchestration around the existing isolated Unity `Live2DProbe`; the C# probe remains authoritative for model loading and Cubism parameter access.

## Validate an isolated run

Copy `manifest.example.json`, set absolute `model.model3Json`, `probe.executable`, and `run.outputRoot` paths, then run:

```powershell
node scripts/live2d/cli/live2d-tools.cjs validate --manifest C:/absolute/path/run.json
```

The output root is created only when absent/empty or already marked with `.test_mode`; a non-empty unknown directory is refused. The state file records the model3 JSON SHA-256, probe configuration, report SHA-256 and expected parameter count. Probe failures persist `status: failed`; only a verified `scanned` state with matching model/config/report fingerprints may resume. No production mapping or runtime action data is written.

## Platform sample scan

```powershell
node scripts/live2d/cli/live2d-tools.cjs scan --manifest C:/absolute/path/run.json
```

The CLI sets `FU_XUAN_DATA`, `FU_XUAN_PROBE_SCOPE=all` and `FU_XUAN_PROBE_WRITER_MODE`, runs the existing probe with configured arguments, validates `capability-report-probe-window.json` against the expected parameter count, and generates `capability-catalog/v1`. A previously completed output root is not overwritten; `--resume` rebuilds the catalog from a cached report only when the model hash matches. This first slice does not yet orchestrate cloud visual review or semantic candidate selection.

Environment overrides: `LIVE2D_MODEL3_JSON`, `LIVE2D_PROBE_EXE`, `LIVE2D_OUTPUT_ROOT`, `LIVE2D_PROVIDER`, `LIVE2D_BATCH_SIZE`, `LIVE2D_TIMEOUT_MS` (explicit CLI flags take precedence where provided). Cloud credentials are read only by review workers from provider-specific environment variables; never put them in manifests.

## Commands

- `validate --manifest <abs-path> [--output-root <abs-path>]`
- `scan --manifest <abs-path> [--output-root <abs-path>] [--resume]`
- `experiment --manifest <abs-path> [--output-root <abs-path>] [--resume]`
- `status --manifest <abs-path> [--output-root <abs-path>]`

For `experiment`, add an `experiment` object to the manifest:

```json
{
  "type": "combination",
  "parameterIds": ["ParamAngleX", "ParamAngleY", "ParamAngleZ"],
  "rangeScale": 0.25,
  "steps": 12
}
```

For a sequence use `type: "sequence"`, `wristScale`, `steps`, and `repeats`. For member ablation use `type: "ablation"`, at least three `parameterIds`, `rangeScale`, and `steps`; the platform runs the full group and one run with each member removed. Ablation reports also calculate `deltaPeak` and `relativeContribution`, but these are `EvidenceOnly` scores and do not establish semantic ownership or certify an action. The output is `live2d-experiment-report/v1` and remains `productionStatus: not-certified`; this is evidence only, not an action certification.

Optional local ROI evidence can be added to an experiment manifest with normalized rectangles for `face`, `left_arm`, `right_arm`, and `torso`; the report records peak RGB delta, adjacent-frame delta, coverage, reset stability, and `EvidenceOnly` status. ROI reads only the current Probe report's explicit frame list, so stale files in `probe_window` are ignored; ablation reports keep separate `full`/`without-*` ROI evidence. Sequence reports additionally expose `phaseRoiEvidence` for stages such as `baseline`, `raise_arm`, `set_hand`, `wrist_swing`, `return_arm`, `reset_hand`, and the exact `reset` stage. ROI failures are recorded as derived evidence failures without changing the completed experiment's `not-certified` safety state. ROI output is mechanical evidence only and does not infer body-part semantics.

Run platform smoke test: `node scripts/live2d/test/platform_cli_smoke.cjs` and ROI smoke test: `node scripts/live2d/test/roi_analysis_smoke.cjs`.
