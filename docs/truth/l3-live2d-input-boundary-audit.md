# L3 Live2D 输入边界审计

> **审计日期**：2026-09-16  
> **状态**：已解决（2026-09-16，人工选择 A；不否定已验证的离线探针和候选动作测试）

## 范围与方法

本次为静态实现审计，范围限于桌宠运行时输入路径；不把 `Assets/Scripts/Editor/` 或 `Assets/Scripts/Live2DProbe/` 的编辑器/离线探针写入计入运行时旁路。

审计复核了 `Live2DRenderer`、`ParameterCommitBridge`、`Live2DInputCoordinator`、生成动作调用方与 `ToolEngine/Live2DSyncTools.cs`。证据检索使用 `rg`，结论只陈述当前代码结构，不代表动作已通过语义或自然度认证。

## 已确认的运行时路径

| 路径 | 当前实现 | 审计结论 |
|---|---|---|
| 渲染器参数提交 | `Live2DRenderer.SetParameter` 经 `ParameterCommitBridge` 提交 | 符合「桥接为 Cubism 运行时写入汇点」的当前实现 |
| 表情、旧动作、生成动作 | 由 `Live2DInputCoordinator` 分别取得 `Expression`、`LegacyAction`、`GeneratedMotion` 租约 | 已有单一租约入口 |
| 生成动作结束 | `MotionCoroutineTools`、`MotionAgent`、`VisionMotionVerifier` 在已取得生成动作租约的路径中结束租约 | 具备可复核的正常结束路径 |

## BlockedByDecision：BBD-INPUT-01

### 冲突来源与需求

- 批准指导文档 [`live2d-input-coordination.md`](../guides/approved/live2d-input-coordination.md) 的 `FR-INPUT-01` 要求外部表情、旧动作和生成动作进入一个语义网关和单租约；`C-INPUT-01` 禁止新增外部旁路 `ActionController`、`MotionGenerator` 或直接 Cubism 写入。
- 具身智能 L3 的既定边界是不允许 LLM 直接输出或控制原始 Live2D 参数，只能请求已认证的语义技能。
- 文档治理 `DG-30` 要求代码真相、批准指导文档和全局决策发生方向冲突时停止实现，记录 `BlockedByDecision` 并等待人工决定。

### 决策前代码证据（历史）

`ToolEngine/Live2DSyncTools.cs` 中的 `ControlBodyTool`（`control_body`）仍将工具输入公开为 `params` 对象和顶层数值参数，随后直接调用 `mapper.Set(...)`：

- 表情模板首关键帧：约第 237 行；
- 调用方提供的参数：约第 281 行；
- 该路径仅调用旧的 `renderer.SetAiControlLock()`，未取得 `Live2DInputCoordinator` 的租约。

此外，`Live2DRenderer.SetParameterValue(string, float)` 仍是公开原始参数入口。当前全文检索只发现其声明，未发现运行时调用方；它尚未构成已发生的竞争写入，但会保留未来绕开语义网关的公开面。

### 影响

保留 `control_body` 的生产可用原始 `params` 输入，会让工具/LLM 可绕过租约仲裁、认证技能注册表和动作自然度门槛。即使为它补上租约，也不能解决「LLM 直接操作原始参数」这一方向冲突。

因此，在决定前不得把该工具接入新的统一输入层、不得将其视为可认证技能，也不得用它为运行时自动行为提供参数写入能力。

### 待人工决定的选项

1. **A（建议）**：从正式工具集中关闭原始 `params`/顶层参数；未来只接受已认证的语义技能。注册表尚无对应技能时，明确拒绝请求而非降级为原始写入。
2. **B**：原始参数仅保留在 `.test_mode` 的开发诊断入口，生产工具注册中禁用；语义技能仍需后续认证后开放。
3. **C**：临时保留生产 `control_body` 原始参数入口，同时为其补租约。此方案仍违反原始参数边界，不建议采用；若选择它，需要由人工明确确认该临时例外的期限、可调用者和退出条件。

### 人工决定与落实

人工于 2026-09-16 选择 **A**：从正式工具集中关闭原始 `params` 与顶层参数；只有完成认证的语义技能才能在未来重新开放身体动作能力。

- 删除 `ControlBodyTool`，使反射注册不再发现该工具；
- `ToolRegistry` 保留 `control_body` 的显式禁用原因：它不出现在 Function Schema 中，遗留同步/异步调用返回“已禁用”而不是参数写入；
- 从 `LocalToolRouter` 的操作和回退白名单、`ToolBenchmarkRunner` 用例中移除该名称；
- 运行时 LLM 提示词不再注入原始参数知识，改为明确“当前没有可调用身体控制技能”；
- 移除无调用方的公开 `Live2DRenderer.SetParameterValue` 原始参数入口。

隔离 EditMode 验证通过（failed=0），其中 `ControlBody_禁用且不出现在LLM工具Schema中` 断言该工具已禁用、未注册、未进入 Schema/本地路由，并会返回明确拒绝。静态检索确认 `ToolEngine/` 不再包含 `mapper.Set(...)`，运行时脚本中也无 `ControlBodyTool` 或 `SetParameterValue(...)`。

## 输入协调验收状态（2026-09-16）

| 验收项 | 当前证据 | 状态 |
|---|---|---|
| AC-INPUT-01：并发租约拒绝 | 隔离 EditMode `Live2DInputCoordinatorTests` 通过；运行时复核也记录生成动作占用时拒绝 `LegacyAction/stretch` | 已验证 |
| AC-INPUT-02：错误租约释放 | 隔离 EditMode `Live2DInputCoordinatorTests` 覆盖错误 `requestId` 不影响当前租约 | 已验证 |
| AC-INPUT-03：完成、超时/销毁收束 | 正常旧动作完成记录 `action-completed`；测试退出期间活动旧动作记录 `action-test-exit` 并在退出前完成收束日志 | 部分验证：测试退出已验证；真实 Windows 关机/注销仍未验证 |
| AC-INPUT-04：旧动作与生成动作冲突 | 隔离运行时双向复核：生成动作占用时旧动作被拒绝；旧动作占用时生成动作被拒绝，并正常完成释放 | 已验证 |
| AC-INPUT-05：静态入口审计 | 运行时渲染写入桥接与已知表情/旧动作/生成动作路径已审计；`control_body` 已删除并由禁用拒绝保护，公开 `SetParameterValue` 已移除 | 已验证（仍不代表 L3 全部完成） |

该表不是 L3 全部完成声明。未认证技能仍不得开放给 LLM 或自主行为；真实 Windows 关机/注销的生命周期证据也仍待单列验收。
