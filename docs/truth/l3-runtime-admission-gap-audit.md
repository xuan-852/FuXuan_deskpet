# L3 运行时认证准入缺口审计

> **审计日期**：2026-09-16  
> **范围**：审计生产动作入口与认证准入的连接关系，并记录已批准的最小运行时封禁；不评价动作视觉质量。

## 已复核事实

`Live2DInputCoordinator` 已在生产渲染器中仲裁 `Expression`、`LegacyAction`、`GeneratedMotion` 和仅测试的 `CandidateTest`：`Live2DRenderer` 的表达式、旧动作和生成动作入口均先申请租约，`MotionAgent`、`MotionCoroutineTools` 与 `VisionMotionVerifier` 的生成动作路径也调用 `TryBeginGeneratedMotion`。

`CertifiedSkillRegistry`、`EmbodiedCoordinator` 和 `EmbodiedActionRequest` 已存在于 `Assets/Scripts/Embodied/`，但静态检索生产 `Assets/Scripts/` 未发现 `new CertifiedSkillRegistry` 或 `new EmbodiedCoordinator`。当前唯一具名候选 `screen_side_arm_raise` 保持 `Candidate`，未注册任何生产 `Certified` 技能。

因此当时的状态是：**输入互斥已接入生产；L3 的“仅认证技能可准入”尚未接入生产。** 这不影响候选不可调用的事实，也不构成 L3 完成声明。

**2026-09-17 更新**：上述缺口已部分闭合——`EmbodiedRuntimeAdmission` 已作为生产准入汇点实例化注册表与协调器，唯一执行路径（隔离 Param94 候选手势）已经 `ActionRequest` 准入并验证完成/取消双路径释放；证据见 [运行时认证准入接线](l3-runtime-admission-wiring.md)。「LLM/主动行为经准入调用认证技能」仍属后续任务包，注册表对 LLM 依旧不可达。

## 已解决决策：BD-L3-RUNTIME-01

批准的 L3 指导要求运行时只开放四层认证后的语义技能；同时现有旧动作和生成动作入口仍有可用行为。把它们立刻接入空的认证注册表会使全部旧动作被拒绝；继续原样运行则不能宣称已满足认证准入。

人工于 2026-09-16 选择 **A**：认证注册表为空期间，禁止 LLM/主动行为触发旧预设与旧生成动作；保留 L2 步行、物理和表情基线，旧动作作为 `LegacyCandidate` 离线复核后逐个迁入。

落地方式：

- `play_action` 与 `generate_motion` 从 `ToolRegistry` 正式注册、Function Schema、核心工具子集、本地路由及 benchmark 移除；直接遗留调用得到确定的“已禁用”结果；
- 对话内嵌动作标记仍会从文本中清理，但不再调用 `ForceAction`；
- `MotionAgent` 的旧生成、组合生成和生成式表情回退在生产运行时被拒绝；只在带 `.test_mode` 的隔离环境保留离线复核能力；
- 未修改 `Live2DRenderer` 的步行、物理和表情基线，未注册任何生产认证技能。

空注册表仍未接入生产；A 只是封住未认证入口，不代表“仅认证技能可准入”已完成，也不将候选技能暴露给 LLM。

## 可复核证据

- `rg` 检索显示 `Live2DRenderer` 的 `TryBegin` 入口及 `MotionAgent`、`MotionCoroutineTools`、`VisionMotionVerifier` 的 `TryBeginGeneratedMotion` 调用；
- 同一检索中，`new CertifiedSkillRegistry`、`new EmbodiedCoordinator` 仅出现在 EditMode 测试，生产脚本中没有实例化点；
- [认证基础真相](l3-certified-skill-foundation.md) 与 [画面侧单臂候选真相](l3-screen-side-arm-raise-candidate.md) 均记录注册表为空、候选不可调用。
