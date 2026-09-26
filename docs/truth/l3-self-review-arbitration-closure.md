# L3 self_review 仲裁绕过口关闭

> **验证日期**：2026-09-27（改动于 2026-09-26 完成）

## 背景

`SelfReviewTool`（`Assets/Scripts/ToolEngine/MotionCoroutineTools.cs:495`）是「播放动作模板并送 GLM-4V 评分」的自省工具。它执行时直接 `renderer.StopAllActionsAndExpressions()` + `renderer.PlayAction(actionName)`（同文件 539/543 行），完全绕开 `EmbodiedCoordinator` → `EmbodiedRuntimeAdmission` 的认证仲裁链，与产品方向基线（2026-09-15）L3 层「动作统一经 `ActionRequest` 仲裁」冲突。2026-09-26 全项目差距评估（`docs/project-evaluation-2026-09-26.md` G2）将其确认为当前最大的仲裁绕过口；2026-09-18 真人首测中本地规划器也曾误选该工具（见 `l4-body-intent-deterministic-routing.md` 背景）。

## 改动

1. `ToolRegistry.DisabledToolReasons` 增加 `self_review`（`ToolRegistry.cs`）：该工具不再注册、不进入 Function Schema；任何遗留 `ToolRegistry.Execute/ExecuteAsync("self_review")` 调用返回确定性拒绝文案并说明替代路径（`request_body_skill`），与 `control_body`/`play_action`/`generate_motion` 的既有禁用机制一致。
2. `LocalToolRouter` 的 `OperationTools` 与 `FallbackTools` 白名单摘除 `self_review`：本地规划与云端严格意图子集都不再把它列为候选。

类本体与 benchmark 条目保留（同 `play_action` 先例）。Editor 侧 `SelfTrainingManager` 的训练后自评对比直连 GLM API、不经 `ToolRegistry`，训练工作流不受影响；`ToolResultBudget` 中该工具的回填预算 case 已无执行来源，为惰性分类器，保留不动。

## 验证

`build.ps1 -RunTests` EditMode 全量回归通过：330 total、326 passed、0 failed、4 ignored（`logs/build/test_results.xml`，2026-09-27 刷新，门禁 `failed=0`）。编译与测试在同一会话内完成。

## 边界与遗留

- 「评审这个动作」类用户请求暂无认证替代路径：工具摘除后该意图将退化为文字回复，直到认证动作链提供等价的评审能力（如离线双模型评审或隔离 Player capture 证据链）。
- 遗留动作路径的其余部分（表情 `InputLeaseOnly`、9 类硬编码空闲动作、AutoChat/VisualHeartbeat 反射、`PlayAction` 本体）尚未迁移，按评估报告 §六-2 顺序逐类推进；`self_review` 关闭只移除了**工具面**绕过口，不是统一仲裁的终点。
- 2026-09-27 补充：`LocalToolRouter.TryBuildKeywordPlan` 中残留的旧 `play_action` 关键词分支（「播放/做一个 + 动作/挥手/点头/微笑」规划注定被禁用的工具）已移除；身体动作的唯一规划入口是 `request_body_skill` 确定性路由，未匹配认证技能的请求按 L4 退化为文字。EditMode 330 total / 326 passed / 0 failed / 4 ignored 回归通过。
