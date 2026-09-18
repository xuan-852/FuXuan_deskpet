# 通用 Live2D 参数能力探测器指导文档

> **状态**：已批准。
>
> **上位决策**：[Live2D 平台与符玄私有 Fixture 范围决策](../../decisions/2026-09-18-live2d-platform-and-fuxuan-fixture-scope.md)、[L3 技能认证](l3-embodied-skill-certification.md)、[首版默认阈值](../../decisions/default-operational-thresholds.md)。
>
> **关联边界**：[运行时平台与模型适配器](live2d-runtime-platform-and-model-adapter.md)、[第三方与模型许可边界](../../third-party-and-model-licensing.md)、[符玄私有 fixture 归档案例](../../archive/fuxuan-private-fixture-case-study.md)。

## 目标与非目标

- **FR-PROBE-01**：提供与符玄无关的参数机械/视觉/时序/关系探测核心，输出可复核证据包。
- **FR-PROBE-02**：首版正式支持 Unity Cubism，兼具命令行入口与可嵌入 Unity 的核心库；为 Web/原生 Cubism 预留适配器接口。
- **FR-PROBE-03**：输入支持 Unity Prefab 和 `.model3.json`+资源目录；首轮人体骨架仅探测头、躯干、双臂/手。
- **FR-PROBE-04**：探测器永不直接修改正式映射；只产出候选证据和报告。

非目标：首版不认证非人形语义、不支持 Web/原生运行时、不执行运行时云端视觉、不读取用户桌面内容。

## 接口与数据

`IModelProbeAdapter` 必须提供模型/资源哈希、参数发现与范围、写入/读回/复位、干扰冻结、统一帧采集和能力声明。核心输出 `capability-report.json`，每项包含机械、视觉、时序、关系、语义候选、分类和证据哈希。

### Unity Cubism 首版已实现入口

以下 `FU_XUAN_*` 环境变量、固定 Fuxuan Prefab 和骨架候选是当前 legacy/fixture adapter 的实现事实，不是通用接口命名承诺：

- 构建：`build.ps1 -ProbeWindow -OutputDir <隔离输出目录>`；输出 `Live2DProbe.exe`，仅包含 `ProbeWindowController` 和待测 Prefab，不复用桌宠场景、移动、掉落或 UI。
- 运行前置：必须设定独立 `FU_XUAN_DATA` 并在其中创建 `.test_mode`；可选 `FU_XUAN_PROBE_PARAMETER=<Cubism 参数 ID>` 缩小为单参数复核。
- 保守幅度复验可在单参数模式额外设定 `FU_XUAN_PROBE_VALUE_RANGE=<minimum>,<maximum>`（使用不依赖区域设置的小数格式）。范围必须处于原生范围内、包含基线且最小值小于最大值；探针仍采集基线/最小/中间/最大/复位三轮，报告只记录实际测试范围。
- 连续可感知性验证可在上述单参数范围基础上设定 `FU_XUAN_PROBE_SWEEP_STEPS=2..60`；探针从基线平滑扫至范围最大值再回到基线，或以 `FU_XUAN_PROBE_SWEEP_TARGET=min` 显式扫向最小值，保存逐帧 PNG 与 `parameter-sweep-report.json`。它只证明离线视觉连续性和复位，不取代运行时协程、抢占或性能验收。
- 全参数普查：设定 `FU_XUAN_PROBE_SCOPE=all`（且不设单参数变量）时，按运行时 `CubismModel.Parameters` 的实际集合采集全部参数；默认仍只采集首轮骨架候选。全参数普查仍只输出证据，不写映射。
- 物理复验：对默认冻结模式中零差异的物理输入，可显式设定 `FU_XUAN_PROBE_WRITER_MODE=physics`。此时只额外保留 Cubism 物理控制器；探针每点稳定 8 帧并调用 SDK `Stabilization()`，禁止同时启用任何桌宠行为组件。
- 输出：`capability-report-probe-window.json`、`probe_window/*.png`，每参三轮 `baseline/min/mid/max/reset`（15 帧）以及本地像素差/复位稳定性指标。
- 骨架组合验证：设定 `FU_XUAN_PROBE_COMBINATIONS=skeleton` 时，独立窗口只执行固定的躯干+头部、左右手臂、躯干+单臂证据组。每组以成员真实最小/最大值采集单项、组合、复位，三轮重复后写入 `skeleton-combination-report.json`；报告同时列出每个成员相对基线的最小/最大平均像素差，不能以组合帧替代成员有效性。该模式不写正式映射、不开放运行时动作。
- 任意已发现参数的局部组合可设定 `FU_XUAN_PROBE_COMBINATION_IDS=<参数ID1>,<参数ID2>[,...]`；探针会拒绝少于两个或重复的 ID，并写入独立 `custom-combination-report.json`。它与固定骨架组共用三轮、成员单项、组合最小/最大、复位证据格式；双参数额外采集 `min/max` 与 `max/min` 两个交叉角点，避免因两项的有效方向相反而遗漏真实组合。该入口始终只用于验证关系，不赋予语义或修改映射。
- 自定义组合可选 `FU_XUAN_PROBE_COMBINATION_RANGE_SCALE=(0,1]`，按每项参数“基线到原生最小/最大”的比例缩放后再采样；未设置时为 `1`。该开关只缩放隔离探针取样，不改变模型范围、正式映射或运行时参数。
- 经用户明确授权的离线视觉复核可运行 `review_skeleton_combinations.cjs <隔离数据根>`。它只发送每组第一轮的 `baseline/combined_min/combined_max/reset` 模型帧给 DeepSeek，并按提示词与帧哈希缓存结果；组合自然度通过不等于成员归因、资源归属或 `Certified`。
- 成员归因复核可运行 `review_skeleton_members.cjs <隔离数据根>`。它复用组合试验已采集的 `baseline/min_member/max_member/reset` 帧，只输出成员影响区域、方向、左右独立性与复位判断；单一视觉模型的“高置信”只能提升 `Supporting` 证据，绝不能写入正式映射或升级为 `Certified`。
- 本地初筛：`node scripts/live2d-probe/summarize_local.cjs <隔离数据目录>` 输出 `capability-report-probe-window.local-summary.json`；只用于安排视觉复核，永远不写回映射。
- 能力目录：全参数评价完成后运行 `build_parameter_catalog.cjs <隔离数据目录>`，生成只读 `parameter-capability-catalog.json`，供后续 JSON 动作配置查询。目录明确 `mapWriteAllowed=false`，不替代人工确认的正式语义映射。
- 当前适配范围：首个 Fuxuan Prefab 和骨架候选集合；通用 `IModelProbeAdapter`、`.model3.json` 输入和 Web/原生适配仍未实现，不能把本入口宣传为已完成的全模型通用工具。

## 硬约束

- **C-PROBE-01**：核心不得引用 `UnityEditor`、项目路径、符玄参数名或 API Key。
- **C-PROBE-02**：源码映射是唯一可编辑源；Resources 镜像只能自动生成并在构建/普查前做哈希一致性门禁。
- **C-PROBE-03**：云端视觉只消费隔离生成的模型帧，先经本地差异筛选；单批不超过 6 元，总预算遵从 L3。
- **C-PROBE-04**：任何缺失机械、视觉、时序证据的参数不得为 `Certified`。

## 验收

| ID | 证据 | 标准 |
|---|---|---|
| AC-PROBE-01 | Unity Prefab 与 `.model3.json` 隔离样本 | 均可发现参数、输出哈希与报告 |
| AC-PROBE-02 | 写/读/复位/重复性报告 | 无映射写回、无生产数据污染 |
| AC-PROBE-03 | 镜像一致性测试 | 漂移阻断；镜像由唯一源生成 |
| AC-PROBE-04 | 人体骨架首轮截图/指标 | 头、躯干、双臂/手形成候选清册，不提前认证 |

## 实施授权边界

允许新增独立探测核心、Unity Cubism 适配器、CLI、镜像校验、隔离测试和证据文档。禁止修改正式映射、接入云端视觉、向 LLM 开放结果或采集用户数据；这些均需独立任务包。
