# L4 身体意图确定性路由（本地链路可达认证技能）

> **证据日期**：2026-09-18  
> **任务包**：`l4-body-intent-certified-skill-routing-v1`（补充实施）  
> **背景**：2026-09-18 真人端到端首测失败——「摇摇头给我看」被 3B 分类器误判为 knowledge，本地规划器选中 knowledge 白名单内的 `self_review`，回复走离线回退，`request_body_skill` 从未进入候选。

## 修复内容

- `LocalToolRouter.IsExplicitBodyRequest`：确定性识别强祈使身体请求（「摇摇头」「笑一个」等完整短语；或动作词 + 请求语气词组合，防止「搜索摇头的原理」误路由）。
- `LocalToolRouter.TryResolveCertifiedBodySkill`：关键词 → 认证技能映射表，只接受 `LlmExposed` 且准入可用的技能；无匹配返回 false，安全退化为文字。
- `TryBuildKeywordPlan`：身体请求直接产出 `request_body_skill` 确定性计划。
- `TryHardenPlanArguments`（request_body_skill）：模型给出的 `skill_id` 只有在已认证且已暴露时放行；否则用确定性匹配修复，仍失败即终态拒绝，绝不降级为原始参数。
- `ChatManager` 两处覆盖：本地规划路径与云端首轮 `_lastIntent` 在强祈使请求时确定性置为 `body`，使 body 闭集（`request_body_skill`/`stop_action`）进入工具子集与提示词。

## 可复核证据

- `build.ps1 -RunTests`：EditMode **227 total、226 passed、failed=0、1 ignored**；`LocalToolRouterTests` 新增三用例：显式请求识别与误路由防护（「搜索摇头的原理」不触发）、确定性映射到认证技能（摇头→m02、笑一个→m10）、参数加固拒绝未暴露技能并可被确定性匹配修复。

## 边界

- 拒绝与无匹配均为终态退化文字，无原始参数入口、无云端调用、无生产数据写入（AC-L4-01）。
- 技能关键词表是路由层确定性配置；技能库扩充时需同步维护。
