# L3 虚拟骨架候选账本

> **证据日期**：2026-09-16  
> **状态**：候选证据结构化完成；零运行时节点开放。

`VirtualSkeletonCandidateLedger.CreateCurrentEvidenceLedger()` 将当前模型证据组织为四个节点：

| 节点 | 证据 | 状态 | 资源 | 运行时可用 |
|---|---|---|---|---|
| `root.desktop-motion` | 已测步行基线 | Conditional | Movement | 否 |
| `head.orientation` | ParamAngleX 动态证据 | Supporting | Face | 否 |
| `torso.physics-lean` | ParamBodyAngleX 物理证据 | Conditional | Body | 否 |
| `arm.screen-side-raise` | Param94 动态证据 | Supporting | RightArm | 否 |

节点只引用证据 ID，不是正式参数映射；`screen-side` 是画面描述，不能推断为模型人体左右臂。`TryGetRuntimeNode` 只会返回 `Certified` 节点。

隔离 EditMode 测试通过（failed=0）：当前四节点均不能作为运行时节点；独立构造的 `Certified` 节点才可返回。该账本不注册技能、不执行动作、不写映射或生产数据。
