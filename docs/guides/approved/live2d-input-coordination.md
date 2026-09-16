# Live2D 输入协调与单租约指导文档

> **状态**：已批准；本文件是 Live2D 外部动作输入改造的唯一实施依据。
> **上位决策**：[产品方向基线](../../decisions/2026-09-15-product-direction-baseline.md) L3、[具身智能指导](../../embodied-intelligence-guidance.md)。
> **代码真相**：[动作系统](../../modules/action-agent.md)、[Live2D 渲染](../../modules/live2d-rendering.md)。

## 1. 目标与非目标

- **FR-INPUT-01**：表情、旧预设动作和 AI 生成动作必须经同一个语义输入网关取得租约；任意时刻只允许一个外部 Live2D 输入拥有执行权。
- **FR-INPUT-02**：每个租约必须有递增 `requestId`、输入种类、所有者与终止原因，并写入开发诊断日志。
- **FR-INPUT-03**：完成、停止、超时和 Renderer 销毁必须释放对应租约；释放过期或非所有者租约不得影响当前请求。
- **FR-INPUT-04**：渲染器逐帧基线写入与语义 Mapper 写入必须提交给 `ParameterCommitBridge`；该桥接层是桌宠运行时的唯一 Cubism 参数赋值点。

非目标：不把 DeepSeek 单模型结论升为 `Certified`；不允许 LLM/工具提交原始 `Param*`；不改 `fuxuan_map.json`、模型资源、桌宠掉落/行走 Root 状态机；不让云端参与运行时仲裁。

## 2. 架构与接口

```text
Chat / Tool / Idle-compatible caller
        -> Live2DRenderer semantic gateway
        -> Live2DInputCoordinator (strict single lease)
        -> Legacy ActionController or MotionGenerator
        -> Live2DParameterMapper / renderer baseline
        -> ParameterCommitBridge
        -> Cubism
```

| 接口 | 输入 | 结果 | 约束 |
|---|---|---|---|
| `TryBegin(kind, owner, out lease)` | 语义类别与名称 | 租约或拒绝 | 活动租约存在时拒绝 |
| `Release(lease, reason)` | 原始租约 | 成功/失败 | 仅当前同 `requestId` 的所有者可释放 |
| `ReleaseAll(reason)` | 生命周期原因 | 清空当前租约 | 仅 Renderer 销毁/受控恢复使用 |
| `TryBeginGeneratedMotion` | 动作描述 | 生成动作租约 | 不暴露参数映射或 Cubism 对象 |
| `ParameterCommitBridge.Commit` | 已验证的参数 ID 与值 | 单次 Cubism 写入 | 仅 renderer/mapper 内部可调用；上层无此接口 |

固定输入类别为 `Expression`、`LegacyAction`、`GeneratedMotion`。当前是安全优先的全局互斥；资源级并行（如 Face 与 Hand）必须等能力目录、组合验收与 `ParameterCommitBridge` 均完成后，另行批准。

## 3. 状态、恢复与约束

状态机：`Idle -> Leased -> Executing -> Released`；拒绝保持 `Idle`，超时或销毁进入 `SafeRecovery` 后释放。表情替换或动作开始前，必须先以零淡出停止已有表情并释放其租约，不能让两段淡出/关键帧并写。

- **C-INPUT-01**：禁止新增绕过协调器的外部 `ActionController.PlayAction/PlayExpression`、`MotionGenerator` 或直接 Cubism 写入入口。
- **C-INPUT-02**：`Live2DRenderer` 行走、掉落、拖拽、物理与空闲仍是未仲裁的内部基线，但它们的最终参数赋值必须经 `ParameterCommitBridge`；它们不等同于已认证原语，修改其行为规则须另建任务包。
- **C-INPUT-03**：旧预设进入 `LegacyAction` 独占租约；不得与生成动作或表情并行。
- **C-INPUT-04**：取消、超时和异常退出不得让桌宠永久暂停或遗留输入锁；必须恢复现有安全姿态/行走链。
- **C-INPUT-05**：所有测试用 `FU_XUAN_DATA` 与 `.test_mode`，不得调用云端或污染生产记忆。

## 4. 验收

| ID | 场景 | 可复核证据 | 通过标准 |
|---|---|---|---|
| AC-INPUT-01 | 同时申请两个租约 | EditMode 测试/日志 | 第二个请求被拒绝且首租约未改变 |
| AC-INPUT-02 | 用错误租约释放 | EditMode 测试 | 当前租约仍然有效 |
| AC-INPUT-03 | 正常完成/超时/销毁 | 构建 + 生命周期日志 | 租约释放，既有恢复链可继续运行 |
| AC-INPUT-04 | 预设动作与 AI 生成动作冲突 | 隔离运行测试 | 只允许一个路径执行，无双写诊断 |
| AC-INPUT-05 | 静态入口审计 | `rg` + 代码审查 | 新外部调用均经语义网关；正式运行时仅桥接层直接赋值 Cubism 参数 |

## 5. 实施授权边界

允许修改：`Assets/Scripts/Embodied/`、`Live2DRenderer` 的语义适配边界、`ActionAgent`/动作工具入口、隔离 EditMode 测试、Live2D 与动作模块文档、任务包和自动文档索引。

禁止修改：正式参数映射、Live2D 资源、`DesktopPet` 物理状态机、生产记忆、云端运行时调用。若实施需要允许资源级并发、原始参数接口、或改变上述禁止范围，立即停止并交由人工决策。
