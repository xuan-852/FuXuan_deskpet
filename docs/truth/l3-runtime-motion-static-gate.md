# L3 认证动作静止门禁与收尾竞态修复

认证动作的隔离运行时验证先发送 `@@sim:walk:stop`，再通过 `@@sim:status` 确认 `velocity=(0,0)`，才允许触发 `@@sim:certified-motion:<skillId>`。未确认静止时，驱动会失败而不是叠加动作。

`external_Hiyori_Hiyori_m06` 的隔离桌宠复验记录了：速度为零、准入、开始、`certified-motion-completed`、姿态还原和准入释放；未出现 `certified-motion-timeout`。问题根因是帧内超时检查先于协程最后一帧收尾发生，原先动作时长与超时值相等会造成误判。`EmbodiedRuntimeAdmission.CompletionGraceSeconds` 为认证动作增加 0.25 秒受控收尾窗口；超出窗口仍按超时安全恢复。

验证：`build.ps1 -Quick`、`build.ps1 -RunTests` 均通过（EditMode failed=0）；修复后由 `certified_motion_runtime_drive.cjs` 完成 m06 隔离真机复验。
