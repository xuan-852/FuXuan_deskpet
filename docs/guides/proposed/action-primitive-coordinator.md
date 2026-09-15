# 动作原语与单写入协调器指导文档

> **状态**：提案；不得据此修改运行时动作链，移入 `approved/` 前须满足 DG-11。
> **上位决策**：[产品方向基线](../../decisions/2026-09-15-product-direction-baseline.md) L3；[具身智能指导](../../embodied-intelligence-guidance.md)。
> **代码真相**：[动作系统](../../modules/action-agent.md)、[Live2D 渲染](../../modules/live2d-rendering.md)。

## 1. 目标与非目标

- **FR-PRIM-01**：建立只读能力目录到“动作原语 JSON”的受控转换。原语使用稳定 `capabilityKey`，不允许上层 LLM、工具或 JSON 直接提交 `Param*`。
- **FR-PRIM-02**：建立一个唯一的参数提交点；动作、表情、空闲、拖拽、行走和旧预设必须经资源仲裁后才可影响同一帧的 Live2D 参数。
- **FR-PRIM-03**：每个原语声明资源、幅度范围、起止姿态、互斥关系、取消与恢复策略，并可在隔离环境可复核。
- **FR-PRIM-04**：第一批只构建开发期 `CandidatePrimitive`，不向 LLM 常规开放，不自动写入 `fuxuan_map.json`。

非目标：本阶段不重写 244 参数的正式语义映射；不把 DeepSeek 单模型结果升级为 `Certified`；不取代桌宠 Root/掉落/行走状态机；不让云端参与运行时仲裁。

## 2. 当前冲突审计

当前存在多条直接写入路径，不能直接新增另一套 JSON 播放器：

| 写入者 | 当前入口 | 风险 | 迁移策略 |
|---|---|---|---|
| `Live2DRenderer` | 每帧姿态、物理覆盖、旧动作 | 与新姿态同帧覆盖 | 注册为基础层/Legacy 独占层 |
| `ActionPresetPlayer` | `_mapper.Set()` | 与新原语竞争身体和手臂 | 先适配为 `LegacyFullBody` 资源请求 |
| `MotionGenerator` | `_mapper.Set()` | LLM 关键帧可绕开资源锁 | 在 Coordinator 前拒绝或适配，不得并行 |
| `ExpressionManager` | `_mapper.Set()` | 面部可与身体安全并行，但会抢面部 | 声明 `Face` 资源，允许白名单叠加 |
| `IdleActionScheduler` | 参数化 JSON / 渲染器写入 | 与动作、行走抢写 | 改为低优先级 `Idle` 请求 |
| 工具/聊天入口 | `PlayAction` / `PlayExpression` / `SetParameterValue` | 可绕开新 API | 保留兼容入口但只转发 Gateway |

现有 `_actionLocked`、`_aiControlLocked` 与 `DesktopPet.Pause/Resume()` 是必要兼容保护，但不是完整仲裁：没有请求 ID、资源租约、层叠规则或单一提交点。它们在新架构落地前不得删除。

## 3. 范围、组件与公开接口

```text
Legacy / 工具 / Idle / LLM Intent
          -> EmbodiedActionGateway (仅语义请求)
          -> EmbodiedActionCoordinator (资源租约、取消、降级)
          -> ParameterCommitBridge (唯一参数提交)
          -> Live2DParameterMapper / Cubism
```

| 组件/接口 | 输入 | 输出/副作用 | 约束 |
|---|---|---|---|
| `ActionRequest` | `primitiveId`、强度、来源、requestId | 无原始参数 | LLM 只可构造此对象 |
| `CandidatePrimitive` | capabilityKey、目标值区间、资源、曲线 | 可播放定义 | 只引用能力目录已复核项 |
| `EmbodiedActionCoordinator.Request` | `ActionRequest` | Accepted/Queued/Rejected + requestId | 资源仲裁唯一入口 |
| `ParameterCommitBridge.CommitFrame` | 已仲裁层叠后的 semantic/capability targets | 一帧参数写入 | 仅此组件可以写原语参数 |
| `PoseState` | 已提交的姿态摘要、资源所有者 | 下次仲裁输入 | 不存原始用户内容 |

资源固定为：`Locomotion`、`Torso`、`Head`、`Face`、`LeftArm`、`RightArm`、`Hands`、`SecondaryMotion`、`Effects`。同资源同帧只能有一个所有者；`Face` 仅在白名单中可与 `Head`/手臂并行；未经组合验收的一律互斥。

## 4. 数据、状态与版本

`CandidatePrimitive` 至少包含：`id`、`version`、`capabilityKeys`、`resources`、`intensityRange`、`keyframes`、`enter/settle/recovery`、`fallbackPrimitiveId`、`evidenceRefs`、`status`。

状态机：`Requested -> Validating -> Queued -> Preparing -> Executing -> Settling -> Completed`；可转入 `Rejected`、`Cancelled`、`TimedOut -> SafeRecovery`。资源租约必须含 `requestId`、owner、过期时间和取消原因。

能力目录仅可作为生成候选的离线输入。运行时注册表必须是版本化、人工确认过的最小子集；第一批所有 `status` 为 `candidate`，`llmCallable=false`。

## 5. 硬约束

- **C-PRIM-01**：禁止新增任何绕过 Coordinator 的 `_mapper.Set()`、`SetParameterValue()` 或 Cubism 参数写入。
- **C-PRIM-02**：旧写入路径不得静默与新原语并行；未迁移路径按 `LegacyFullBody` 资源互斥处理。
- **C-PRIM-03**：取消、超时、窗口恢复和模型重载必须释放租约，走 `SafeRecovery`，恢复行走/空闲的合法状态而非统一清零。
- **C-PRIM-04**：`EffectOnly`、`unclassified-visible`、`not-certified` 参数不得成为人体动作原语主控制；可单独成为受控视觉效果候选。
- **C-PRIM-05**：测试使用 `FU_XUAN_DATA` + `.test_mode`；云端仅离线验收帧，不参与运行时。
- **C-PRIM-06**：能力目录中的模型猜测不能自动变成 `fuxuan_map.json` 或 Resources 镜像映射。

## 6. 失败、降级与恢复

- 缺少原语、能力或资源：拒绝请求，返回已验证的降级原语或仅文本/表情反馈。
- 写入者检测到未持有租约：阻断该帧写入并记录开发诊断；生产不得抛异常卡住桌宠。
- 播放超时、取消、模型重载：停止提交、释放全部租约、恢复允许的 `DesktopPet` 状态并收敛到当前安全姿态。
- 与 Legacy 动作冲突：新请求排队最多 1 秒；随后取消旧动作并进入 `SafeRecovery`，不得两者叠写。

## 7. 可观测性与验收

| 验收 ID | 条件 | 证据 | 通过标准 |
|---|---|---|---|
| AC-PRIM-01 | 任意入口发起动作 | request/lease/transition 日志 | 每次均有 requestId、来源、资源与终态 |
| AC-PRIM-02 | 新原语与旧预设/行走/Idle 冲突 | 隔离运行截图 + 所有者日志 | 无同资源双写、无残留锁、恢复正常 |
| AC-PRIM-03 | 取消、超时、窗口恢复 | 隔离冒烟 + 状态快照 | 必经 SafeRecovery，宠物可继续交互 |
| AC-PRIM-04 | 第一批头/身体/单臂原语 | DeepSeek 离线验收帧 + 本地曲线指标 | 自然度门槛通过后才升为运行时可用 |
| AC-PRIM-05 | LLM/工具边界 | API 与静态搜索 | 无入口可提交原始 `Param*` 或绕过 Coordinator |

## 8. 实施授权边界

- **允许修改（批准后）**：新 `Embodied/` 协调层、动作原语资源、`Live2DRenderer` 适配边界、ActionAgent/预设/Idle/工具入口的转发、对应隔离测试与模块文档。
- **禁止修改**：`fuxuan_map.json`、Resources 映像、DesktopPet 物理规则、生产记忆、云端运行时调用；除非另有批准任务。
- **BlockedByDecision**：若必须决定旧 `MotionGenerator` 是完全下线还是永久作为 LegacyFullBody 适配器，或必须把候选参数升为正式语义映射，停止实现并交由人工决定。
