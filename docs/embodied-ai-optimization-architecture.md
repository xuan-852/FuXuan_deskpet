# 具身 AI 优化架构设计

> **定位**：把当前“动作决策 → 参数动作 → 事后视觉评分”的系统，演进为适合桌宠的、分层且可观测的具身控制闭环；本文件是设计方案，不把未实现能力写成代码事实。
>
> **适用范围**：`MotionAgent`、`MotionPlanner`、`MotionTranslator`、`MotionGenerator`、`Live2DRenderer`、`DesktopPet`、ActionAgent 工具与验证链路。
>
> **非目标**：不模拟机械臂驱动器，不宣称拥有真实三维碰撞/力反馈，不引入语音识别，不在测试中调用云端模型。
>
> **参考**：ROS 2 的“状态接口/命令接口/控制器仲裁”、VLA 的“感知+目标→短动作”、具身推理的“高层规划→低层技能→反馈重规划”。外部资料见文末。

---

## 一、问题定义与目标

### 1.1 当前能力与结构性缺口

当前系统已有本地自主决策、模板动作、自然语言关键帧翻译、AI 控制锁、动作截图、GLM 评分和 MotionMemory。它的执行层是可靠的 Unity/Live2D 参数写入，但动作决策、参数写入、互斥、结果判定分散在多个类中。

主要缺口：

1. 没有统一的“身体状态快照”，各模块直接读取 `Live2DParameterMapper`、`DesktopPet` 和渲染器状态。
2. 没有唯一的动作仲裁入口；预设动作、空闲动作、`control_body`、`generate_motion` 和自主动作各自申请锁或直接写参数。
3. 安全校验目前以范围钳制和告警为主，未形成独立的动作准入、资源占用、超时中止和恢复协议。
4. 当前闭环偏“动作结束后评分”，缺少执行中观测、失败分类与即时中止/重规划。
5. 记忆以 GLM 得分为中心，无法回答“动作为何没有执行、为何被抢占、为何不可见、为什么停止”。

### 1.2 优化目标

系统应达到：

- **可控**：任何身体命令只能经过一个执行器；调用方不可直接竞争 Live2D 参数。
- **可解释**：每个动作都有来源、目标、资源占用、状态迁移、停止原因和结果。
- **稳定**：动作、走路、空闲动画、表情与窗口恢复不会互相覆盖；异常必定回到安全中性状态。
- **低成本**：实时控制不依赖云端；本地模板和确定性规则优先；云端视觉仅作为显式、受预算保护的质量评估。
- **真实**：只基于当前模型实际参数能力选择动作；对不可表现动作明确拒绝或降级。

---

## 二、目标架构

```text
用户 / 聊天工具 / 自主调度 / 测试命令
                  │
                  ▼
        Embodied Intent Gateway
   统一 ActionRequest、参数校验、来源标记
                  │
                  ▼
       Embodied Coordinator（唯一仲裁）
  资源声明 · 优先级 · 状态机 · 取消 · 冷却
          │                    │
          ▼                    ▼
  Skill/Policy Router      Body State Store
 模板 / 本地策略 / 云端兜底   参数+走路+锁+新鲜度
          │                    ▲
          ▼                    │
        Action Executor ──── Execution Monitor
   参数插值、行走交接、恢复     执行中观测/超时/中止
          │
          ▼
 Live2DParameterMapper + DesktopPet + Live2DRenderer
          │
          ▼
 Outcome Evaluator → Embodied Experience Store → QualityTelemetry
  本地结果优先；可选 GLM 评分；不存敏感画面
```

### 2.1 与机械臂架构的对应关系

| 机械臂体系 | 桌宠优化后的对应层 | 责任 |
|---|---|---|
| 状态接口 | `BodyStateSnapshot` | 参数、动作、行走、锁、窗口可用性的一致快照。 |
| 命令接口 | `ActionRequest` | 唯一的、可校验的表情/技能/参数动作命令。 |
| 控制器管理器 | `EmbodiedCoordinator` | 仲裁资源、管理生命周期、取消冲突动作。 |
| 低层控制器 | `ActionExecutor` | 协程插值、参数写入、走路交接、复位。 |
| 安全监督器 | `EmbodiedSafetySupervisor` | 参数范围、互斥、能力、超时、恢复。 |
| 任务规划/VLA | `SkillPolicyRouter` | 选择模板、本地模型或受控云端翻译。 |
| 传感器反馈 | `ExecutionMonitor` | 参数是否写入、锁是否有效、渲染是否可用、动作是否完成。 |

此对应仅借鉴软件架构：桌宠的“身体”是 Live2D 参数空间，不包含物理接触、力反馈或真实环境风险。

---

## 三、核心接口与数据契约

所有新接口应放在 `Assets/Scripts/Live2DFramework/ActionAgent/`；不得让聊天层、工具层或空闲动画层绕过 Coordinator 直接写身体参数。

### 3.1 `BodyStateSnapshot`：只读状态接口

```csharp
public sealed class BodyStateSnapshot {
    public long Version;
    public DateTime UtcTimestamp;
    public bool RendererReady;
    public bool IsWalking;
    public bool IsPaused;
    public bool IsPresetActionPlaying;
    public bool IsIdleActionPlaying;
    public bool IsAiControlLocked;
    public string ActiveActionId;
    public IReadOnlyDictionary<string, float> Parameters;
    public IReadOnlySet<string> ClaimedResources;
}
```

**功能要求**：

- FR-S01：每次动作开始、阶段切换、结束、取消与渲染器重建后更新版本号。
- FR-S02：快照必须带 UTC 时间和版本，过期快照不得用于执行新动作。
- FR-S03：默认只暴露语义参数及必要运行状态，不记录截图、聊天原文或用户窗口标题。

**约束**：

- C-S01：状态读取不触发 LLM、截图、磁盘写入或网络请求。
- C-S02：快照不可被调用方修改；只有状态存储器能发布新版本。

### 3.2 `ActionRequest`：命令接口

```csharp
public sealed class ActionRequest {
    public string RequestId;
    public ActionSource Source; // UserTool / Chat / Autonomous / Idle / Test / Recovery
    public ActionKind Kind;     // Expression / PresetSkill / ParameterPlan / Stop / Recovery
    public string Goal;
    public float DurationSec;
    public int Priority;
    public IReadOnlyDictionary<string, float> RequestedParameters;
    public IReadOnlySet<EmbodiedResource> Resources;
    public bool AllowCloudFallback;
    public string CorrelationId;
}
```

**功能要求**：

- FR-C01：`set_expression`、`play_action`、`stop_action`、`control_body`、`generate_motion`、`MotionAgent` 和 `IdleActionScheduler` 全部转换为 `ActionRequest`。
- FR-C02：命令必须声明来源、目标、持续时间、优先级和所需资源；缺失则拒绝执行。
- FR-C03：用户手动停止、退出、渲染失败和测试退出必须发出明确的取消/恢复请求。

**约束**：

- C-C01：`ActionRequest` 不是任意参数写入通道；未知参数、未注册技能、持续时间越界均被拒绝。
- C-C02：`AllowCloudFallback=false` 是默认值；自动动作和测试请求必须为 false。

### 3.3 资源声明与优先级

建议资源：`Locomotion`、`BodyPose`、`Face`、`Arms`、`Hands`、`SpecialEffects`、`Camera`。

优先级（数值越大越优先）：

| 来源 | 优先级 | 行为 |
|---|---:|---|
| `Recovery` / 应用退出 | 100 | 立即中止其他动作并复位。 |
| 用户 `stop_action` | 95 | 立即取消可取消动作。 |
| 用户显式 `control_body` / 工具动作 | 80 | 抢占自主与空闲动作。 |
| 聊天表达动作 | 70 | 可抢占空闲，不抢占用户显式控制。 |
| 自主动作 | 40 | 不抢占任何非自主动作。 |
| 空闲动作 | 20 | 仅在全部资源空闲时执行。 |

**约束**：

- C-R01：同一资源同一时刻只能由一个动作持有。
- C-R02：低优先级动作被抢占时必须进入 `Cancelled`，执行器必须恢复其写过的参数。
- C-R03：走路与全身姿势默认互斥；纯面部表情可与走路并行，但必须经过可配置白名单。

### 3.4 动作生命周期

```text
Requested → Validating → Queued → Preparing → Executing
                                      │           │
                                      ▼           ▼
                                 Rejected     Settling
                                                   │
                           Cancelled / TimedOut ← ┼ → Completed
                                                   │
                                                   ▼
                                              SafeRecovery
```

**硬约束**：无论正常完成、超时、异常、被抢占还是窗口重建，最终必须经过 `SafeRecovery`，释放资源、恢复参数、恢复符合条件的走路状态。

---

## 四、功能需求

### F1：统一动作仲裁（P0）

**需求**：实现 `EmbodiedCoordinator`，作为唯一入口与状态机拥有者。

- 接受 `Submit(ActionRequest)`、`Cancel(requestId, reason)`、`GetSnapshot()`。
- 按优先级与资源占用决定立即执行、排队、抢占或拒绝。
- 每个状态转换写入结构化、无敏感数据的遥测事件。
- 对超时、渲染器失效和协程异常保证恢复。

**验收**：

1. 同时提交自主动作与 `control_body`，用户动作获执行，自主动作记录为已抢占。
2. 动作期间触发 `stop_action`，所有申请的资源在一个帧预算内释放，参数恢复到动作前值。
3. 动作期间调用退出/窗口重建，不遗留 AI 锁、移动锁或暂停状态。

### F2：状态存储与执行监测（P0）

**需求**：将现在分散的锁状态、当前动作、行走状态和关键参数汇总为版本化状态；执行中监测而非事后猜测。

- 动作开始后验证预期资源确实被申请。
- 每个关键帧检查参数写入成功、渲染器仍可用、锁未被非法释放。
- 监测到无进度、参数持续被覆盖、渲染不可用时停止动作，标记明确失败原因。

**验收**：使用测试模式注入“渲染器不可用”“动作锁提前丢失”“外部覆盖参数”，断言不会残留移动锁和不恢复的姿势。

### F3：技能库与能力注册（P1）

**需求**：把模板、表情、特效、自然语言别名统一为 `EmbodiedSkillRegistry`。

- 技能声明：名称、别名、所需资源、默认时长、能力等级、参数范围、是否可自主触发、是否可被验证。
- 只从当前模型真实 `fuxuan_map.json` 可用参数构建技能。
- 标记不可表现动作，例如需要腿部真实循环、不可用手部参数或不存在的部位。

**验收**：请求不存在技能或不可表现动作时，返回“当前模型能力不足”及可用替代动作，而不是生成不可执行关键帧。

### F4：策略路由与成本控制（P1）

路由优先级：

```text
显式预设技能 → 本地规则/模板 → 本地模型关键帧 → 云端翻译（可选） → 拒绝并解释
```

- `MotionAgent` 只产生目标级请求，不直接启动生成器。
- 云端翻译仅适用于用户主动要求的非模板动作，且须通过 `TokenBudgetManager`。
- 测试模式、`FU_XUAN_NO_CLOUD=1`、自主动作和空闲动作一律不云端兜底。
- 每次路由记录 `template/local/cloud/rejected` 来源、延迟、解析结果和拒绝原因。

**验收**：断网和无 API Key 时，预设/本地能力仍工作；自主系统不产生云端 usage 记录。

### F5：结果评估与经验存储（P1）

结果按三级评估：

1. **执行完整性（本地、必做）**：是否开始、按时完成、参数是否复位、是否被抢占。
2. **语义代理指标（本地、必做）**：关键参数是否达到技能声明的可见阈值，例如抬手必须超过注册的最小角度。
3. **视觉语义质量（可选云端）**：GLM 评审截图，仅在用户主动验证或显式同意的质量评测中运行。

经验记录字段应从“仅动作名/得分”扩展为：技能版本、策略来源、状态前后版本、结果码、失败类别、关键参数摘要、可选视觉分数。

**约束**：

- C-E01：不得把全量截图、完整聊天内容、活动窗口标题写入长期动作记忆。
- C-E02：低分视觉结果不能直接判定控制器故障；必须与本地完整性结果分开。
- C-E03：云端评分失败属于 `EvaluationUnavailable`，不是动作失败。

### F6：可观测性、回放与测试（P0）

新增仅追加的 `embodied_events.jsonl`，每条仅含：UTC、requestId、source、kind、skillId、state、reason、duration、资源、策略来源、参数摘要哈希、状态版本、评分摘要。

- 不记录用户原文、截图、密钥、完整参数快照或前台窗口标题。
- 提供测试模式 `@@embodied:status|submit:<skill>|cancel:<id>|fault:<type>`。
- 提供回放器：从事件序列验证状态机迁移与资源释放，不重新驱动真实模型。

**验收**：事件日志能解释任一动作的“谁发起、为何被拒绝/中止、是否恢复”；测试不修改生产记忆、不发云端请求。

---

## 五、安全、隐私与稳定性约束

| 编号 | 约束 |
|---|---|
| C-01 | Unity 主线程是唯一写 Live2D 参数的线程；模型/网络回调只能提交请求，不得直接 `mapper.Set`。 |
| C-02 | 每一个写参数动作必须有有限超时；超时后进入 `SafeRecovery`。 |
| C-03 | `Stop`、应用退出、系统会话结束、渲染器失效优先级最高，不能被 LLM 或普通动作阻塞。 |
| C-04 | 参数值最终仍由 `Live2DParameterMapper` 范围钳制；Coordinator 的校验是额外防线，不替代底层钳制。 |
| C-05 | 不允许自主动作调用云端；云端动作翻译/视觉验证必须走现有预算、用量日志和显式配置。 |
| C-06 | `.test_mode` 与 `FU_XUAN_DATA` 隔离是强制前提；禁止用生产记忆训练/验证动作。 |
| C-07 | 不允许通过本架构扩大系统权限、屏幕读取范围或操作系统控制能力。 |
| C-08 | 任何跨模块资源锁必须有所有者、关联 requestId 和自动过期；禁止无期限锁。 |

---

## 六、分阶段实施计划

### Phase A：先做底座，不改变外部行为

- 增加 `BodyStateSnapshot`、`ActionRequest`、`EmbodiedCoordinator`、资源枚举、状态机和事件结构。
- 将现有 AI 控制锁、动作锁、移动锁状态镜像到 Coordinator，但暂保留旧调用路径作为兼容适配器。
- 实现纯 EditMode 状态机/仲裁/超时/抢占测试。

**完成条件**：不改变动作视觉表现；所有现有接口都能被适配器转为请求；冲突路径可在日志中解释。

### Phase B：迁移控制入口与恢复协议

- 依次迁移 `play_action`、`set_expression`、`control_body`、`generate_motion`、`IdleActionScheduler`、`MotionAgent`。
- `MotionGenerator` 只由 `ActionExecutor` 创建和驱动。
- 统一动作结束、取消、窗口恢复、退出的 `SafeRecovery`。

**完成条件**：不存在生产路径绕过 Coordinator 直接持续写参数；并发/取消/退出回归通过。

### Phase C：技能注册、策略路由与本地语义评估

- 将模板与可用参数整理为技能注册表。
- 用本地可见阈值替代“只有 GLM 分数才算结果”。
- 扩展 MotionMemory 为结果分类与技能版本化经验库。

**完成条件**：无云端环境下，系统能稳定选择、执行、评估和解释预设技能。

### Phase D：可选视觉评估与质量实验

- 仅在显式启用的生产质量实验中调用 GLM。
- 建立本地结果与 GLM 语义得分的配对报告；不让单次视觉分数自动改变安全规则。

**完成条件**：有明确授权、成本记录、隔离对照与人工视觉复核；未达到这些条件不将视觉评分写作功能验收。

---

## 七、已确认的产品边界与默认策略

以下结论来自 2026-09-15 的产品讨论；它们是身体能力普查与后续实现的约束，而不是当前代码事实。

| 主题 | 已确认策略 |
|---|---|
| 身体组织 | 以虚拟人体骨架为主；桌面位置/移动可作为 `Root`。头发、裙摆、衣物等默认为随主骨架运动的附属模块；特效不冒充人体部位。 |
| 姿势连续性 | 使用持久 `PoseState`。动作完成后保留自然余韵，按注意力与后续意图渐进收束；中性姿势是安全锚点，不是每次动作的强制终点。 |
| 隐私与互动 | 第一阶段注意力仅来自鼠标位置、停留、点击、拖拽和桌宠直接交互；不读取屏幕内容、活动窗口标题、剪贴板或其他应用数据。 |
| 交互层级 | 注意力反应和轻触互动默认可用；直接操控只在用户主动进入互动模式后启用。 |
| 不可表达动作 | 默认优雅降级到已认证的近似技能；仅在无安全替代方案时拒绝，并将技术原因留在调试记录而非打扰普通用户。 |
| 自主行为 | 本地反射层优先，行为层使用本地规则，LLM 仅低频提出意图；Coordinator 是唯一执行裁决者。第一阶段 Root 移动只响应明确鼠标线索或用户引导，不自主漫游。 |
| 行为风格 | 提供上层可配置的安静/平衡/活跃行为画像与占比接口。认证测试使用“平衡偏活跃”画像，以更充分暴露参数幅度、组合、抢占和恢复问题；运行时默认值可后续由用户偏好决定。 |
| 文字对话联动 | 预留阅读、思考、回复前停顿、注视和回应手势的身体接口；是否在生产默认开启由能力普查后的认证结果决定。语音识别不属于本架构目标。 |
| 云端视觉认证 | 本地图像与运行时证据是前提；DeepSeek Flash 是视觉主判，GLM Flash 为尽力旁证且不阻塞。二者分歧进入仲裁队列，由开发阶段人工智能审阅证据；仍无法确认时再请求产品负责人审核。运行时不依赖云端视觉。 |
| 云端预算与调度 | 首轮 DeepSeek 可用额度上限为 27 元，保留 3 元保护余额；GLM Flash 单次、短超时、无高频重试。仅在低峰队列执行，证据和模型输出按内容哈希缓存并支持断点续跑。 |
| 证据保留 | 正常样本仅保留哈希、指标和结论；失败或争议截图保留 30 天后清理。 |
| 抢占 | `stop_action`、退出和恢复请求立即抢占；普通用户动作最多等待当前动作自然收束 1 秒，之后抢占。 |

### 7.1 第一版身体能力普查的完成定义

第一版不预设少量动作清单，而是基于当前模型实际数据完成能力发现：

1. 对语义地图及运行时真实参数做全量存在性、写入/读回、视觉变化、方向、复位和重复性探测。
2. 对主骨架候选及高价值组合做双视觉模型语义认证，优先覆盖头、眼、躯干、双臂、手、走路交接与附属模块跟随。
3. 输出参数事实表、视觉影响图、骨架挂接图、冲突矩阵与版本化能力清册。
4. 将结果分为 `Certified`、`Supporting`、`Conditional`、`Unreliable`、`EffectOnly`、`Unsupported`；只有 `Certified` 能力可供 LLM 的常规身体意图调用。

在普查结果生成前，不宣称任意映射参数或历史动作模板已经可靠可用。

---

## 八、外部架构参考

- ROS 2 Control：控制器管理器、硬件状态/命令接口、控制循环、命令限位和失败回退。<https://control.ros.org/master/doc/ros2_control/controller_manager/doc/userdoc.html>
- ROS 2 Controller Chaining：控制器资源声明和命令接口占用。<https://control.ros.org/rolling/doc/ros2_control/controller_manager/doc/controller_chaining.html>
- RT-2：将视觉、语言与离散机器人动作 token 统一的 VLA 方法。<https://deepmind.google/blog/rt-2-new-model-translates-vision-and-language-into-action/>
- Gemini Robotics 1.5：高层具身推理模型编排、低层 VLA 执行的双层 agentic 架构。<https://deepmind.google/blog/gemini-robotics-15-brings-ai-agents-into-the-physical-world/>
- OpenVLA：视觉+指令输出 7 DoF 机器人动作、接入既有控制栈的开源路径。<https://github.com/openvla/openvla>

> 外部机器人论文和平台用于借鉴分层、状态、仲裁、反馈和安全边界；不意味着本项目要接入 ROS、机械臂、VLA 模型或增加任何硬件能力。
