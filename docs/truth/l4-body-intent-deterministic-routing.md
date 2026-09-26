# L4 身体意图确定性路由（本地链路可达认证技能）

> **证据日期**：2026-09-25
> **任务包**：`l4-body-intent-certified-skill-routing-v1`（补充实施）  
> **背景**：2026-09-18 真人端到端首测失败——「摇摇头给我看」被 3B 分类器误判为 knowledge，本地规划器选中 knowledge 白名单内的 `self_review`，回复走离线回退，`request_body_skill` 从未进入候选。

## 修复内容

- `LocalToolRouter.IsExplicitBodyRequest`：确定性识别强祈使身体请求（「摇摇头」「笑一个」等完整短语；或动作词 + 请求语气词组合，防止「搜索摇头的原理」误路由）。
- `LocalToolRouter.TryResolveCertifiedBodySkill`：关键词 → 认证技能映射表，只接受 `LlmExposed` 且准入可用的技能；无匹配返回 false，安全退化为文字。
- `TryBuildKeywordPlan`：身体请求直接产出 `request_body_skill` 确定性计划。
- `TryHardenPlanArguments`（request_body_skill）：模型给出的 `skill_id` 只有在已认证且已暴露时放行；否则用确定性匹配修复，仍失败即终态拒绝，绝不降级为原始参数。
- `ChatManager` 两处覆盖：本地规划路径与云端首轮 `_lastIntent` 在强祈使请求时确定性置为 `body`，使 body 闭集（`request_body_skill`/`stop_action`）进入工具子集与提示词。
- `request_body_skill` 通过 Renderer 的受控入口提交请求；Renderer 在唯一 `EmbodiedRuntimeAdmission` 成功后登记 `BehaviorIntent` 与 `SkillExecutionHandle`，回写 `EmbodiedActionRequest.BehaviorExecutionId`，不由 Coordinator 再次准入。

## 可复核证据

- 原 L4 路由任务的 `build.ps1 -RunTests`：EditMode **313 total、309 passed、failed=0、4 ignored**；其中 `BehaviorIntentTests` 15/15 通过，覆盖已准入身体请求关联与生命周期。
- 2026-09-25 当前工作区复验：隔离 EditMode **315 total、311 passed、failed=0、4 ignored**；最终 Player 新产物构建于 `Build/privacy_body_final/DesktopPet.exe`。`scripts/test/request_body_skill_runtime_drive.cjs` 用认证 m06 曲线在独立 `FU_XUAN_DATA` 和 `.test_mode` 下经 ToolRegistry 调用 `request_body_skill`，观察到唯一准入、Renderer 曲线启动、同一执行关联的 `Executing→Completed`、LifeState `Active→Completed`、姿势还原与 `[BodySkillUI] terminal=Completed`。同一驱动还验证前台类别统计默认关闭、开启和再关闭。

- 2026-09-25 补充隔离 Player 验收：本地 Ollama 不可用时，聊天输入「歪歪头给我看」仍通过确定性 `body` 路由调用 `request_body_skill`，认证 m06 曲线实际启动。第一次自然语言动作经 `stop_action` 取消，随后再次输入同一句话得到新的 execution ID 并完成。未认证的「挥挥手给我看」得到明确拒绝，未启动动作。后续故障注入又验证 `Expired`（测试推进超时）、`Cancelled`（Renderer 禁用再启用）和 `RecoveryFailed`（活动中模型失效），`BehaviorCoordinator` 与 `[BodySkillUI]` 对同一 execution ID 的终态一致且各写一次；故障注入不等于真实设备故障验收。
- 同日 `acknowledge_nod` 已加入关键词识别和认证准入，但保持 `LlmExposed=false`。隔离 Player 验证「点点头给我看」只发布确定性拒绝回执，未启动隐藏点头；测试 inbox 的 `skill:acknowledge` 则走同一 `PlayCertifiedMotion` 执行器完成正常、取消、超时和走路租约冲突验收。该分离证明“识别身体请求”不会绕过 AI 暴露白名单。
