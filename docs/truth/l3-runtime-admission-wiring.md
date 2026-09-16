# L3 运行时认证准入接线

> **证据日期**：2026-09-17
> **任务包**：`l3-runtime-admission-b-v1`（上位指导：[L3 具身技能认证](../guides/approved/l3-embodied-skill-certification.md)）
> **范围**：生产准入汇点实例化 + 唯一执行路径接入 `ActionRequest` 准入；不含 LLM 开放、不含旧动作迁移。

## 已验证事实

`Assets/Scripts/Embodied/EmbodiedRuntimeAdmission.cs` 是生产运行时唯一认证技能准入汇点：

- 静态持有 `CertifiedSkillRegistry` 与 `EmbodiedCoordinator`，启动即注册首个认证记录 `screen_side_arm_raise`（[认证技能基础](l3-certified-skill-foundation.md)）；注册被拒时准入保持为空并 `LogError`，绝不降级放行；
- `TryBeginSkill` 只接受注册表内且四层证据 `Passed` 的技能；构造 `EmbodiedActionRequest`（语义目标 + 技能资源声明 + 候选时长超时），经协调器按 `RightArm` 资源仲裁；同一时刻仅允许一个准入请求（`admission-request-active`）；未认证技能一律 `skill-not-certified`；
- `CompleteSkill` 完成与取消共用释放路径，重复释放或释放非当前请求被忽略。

执行层接入：`Live2DRenderer.StartTestParam94Gesture`（仅 `.test_mode` 隔离运行时的 `@@sim:gesture:param94` 入口）在取得 `CandidateTest` 输入租约后必须先获得准入才启动协程；准入被拒则释放租约并拒绝。完成、取消、渲染器禁用与退出全部经 `FinishTestParam94Gesture` 收束，准入随租约同步释放。未修改静态门禁、租约仲裁与步行基线。

## 可复核证据（2026-09-17）

- `build.ps1 -Quick` 通过；`build.ps1 -RunTests` EditMode 200 用例、199 passed、**failed=0**、1 ignored，含新增 `RuntimeAdmissionTests` 4 项（未认证拒绝、单请求占用、释放后可再入、错误释放被忽略）；
- 完整构建（`-CleanBeeCache` 后通过，Bee 增量 DAG 曾对新增文件陈旧致 CS0246，重跑自愈）输出隔离目录 Player，`Assembly-CSharp.dll` 含 `EmbodiedRuntimeAdmission`；
- `node scripts/test/param94_gesture_drive.cjs`（已加入 `@@sim:walk:stop` 预置与准入日志断言）：player_log 出现 `Accepted CandidateTest/param94-gesture#1` → `admitted: screen_side_arm_raise` → `released: candidate-test-completed` → cleanup，13 帧采集完整；
- `node scripts/test/param94_cancel_recovery_drive.cjs`：`admitted` → `released: candidate-test-cancelled`，取消路径准入释放，随后行走恢复确认。

## 边界与未完成项

- 本接线不改变生产行为：手势执行器仍只有 `.test_mode` 隔离入口，LLM 工具面仍为零，未认证能力不可达；
- 准入超时字段已随请求传入，但协调器尚无独立超时计时器，由执行器生命周期兜底（统一 `SafeRecovery` 属后续阶段）；
- 生产 LLM/主动行为开放身体技能属于独立任务包，须在本准入、执行层与自然度复验（运行时协程视觉对照）齐备后再决策。
