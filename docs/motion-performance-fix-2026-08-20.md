# Motion and performance fix record (2026-08-20)

## Confirmed causes

- `MotionGenerator` restored only parameters absent from the final keyframe. Final-keyframe body and head values could remain after an AI exploration motion and combine with the walking pose.
- AI control did not suppress walking-pose writes, so the transient motion and locomotion code could compete for the same parameters.
- `PerformanceMonitor.currentTier` started as `High`; calling `SetTier(High)` returned early. With VSync disabled, the intended frame cap was therefore not applied.

## Implemented safeguards

- Restore every parameter touched by a completed transient motion to its value at motion start.
- Exclude AI-controlled frames from locomotion pose updates and use one cleanup path for explicit release and timeout.
- Apply the initial performance tier settings unconditionally; the default target is 30 FPS.
- Give locomotion priority over automatic idle actions: an idle action cannot start while walking, and a resumed walk interrupts an active automatic action before locomotion parameters are written.
- Treat forced legacy idle actions as exclusive actions: pause physical movement while they run and restore only the pause introduced by the action.

## Legacy action visual check

- `@@idle:1..9` triggered all nine existing idle actions in a real player.
- `@@shot:<name>` captured start/peak/end frames plus an `after_settle` frame in the isolated `action_captures` directory.
- The two intentionally hardcoded effects remain #4 `star_spin` (five phases) and #7 `magic_circle` (five phases with Spring/Perlin motion); the other seven actions remain JSON-driven.

## Verification

- `build.ps1 -Quick`: passed.
- EditMode command returned 0 in the isolated test directory; the Tuanjie host did not refresh `logs/build/test_results.xml`, so the stale XML count is not treated as a fresh measurement.
- Full desktop build: passed.
