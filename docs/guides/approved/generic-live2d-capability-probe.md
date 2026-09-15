# 通用 Live2D 参数能力探测器指导文档

> **状态**：已批准。
>
> **上位决策**：[L3 技能认证](l3-embodied-skill-certification.md)、[首版默认阈值](../../decisions/default-operational-thresholds.md)。

## 目标与非目标

- **FR-PROBE-01**：提供与符玄无关的参数机械/视觉/时序/关系探测核心，输出可复核证据包。
- **FR-PROBE-02**：首版正式支持 Unity Cubism，兼具命令行入口与可嵌入 Unity 的核心库；为 Web/原生 Cubism 预留适配器接口。
- **FR-PROBE-03**：输入支持 Unity Prefab 和 `.model3.json`+资源目录；首轮人体骨架仅探测头、躯干、双臂/手。
- **FR-PROBE-04**：探测器永不直接修改正式映射；只产出候选证据和报告。

非目标：首版不认证非人形语义、不支持 Web/原生运行时、不执行运行时云端视觉、不读取用户桌面内容。

## 接口与数据

`IModelProbeAdapter` 必须提供模型/资源哈希、参数发现与范围、写入/读回/复位、干扰冻结、统一帧采集和能力声明。核心输出 `capability-report.json`，每项包含机械、视觉、时序、关系、语义候选、分类和证据哈希。

### Unity Cubism 首版已实现入口

- 构建：`build.ps1 -ProbeWindow -OutputDir <隔离输出目录>`；输出 `Live2DProbe.exe`，仅包含 `ProbeWindowController` 和待测 Prefab，不复用桌宠场景、移动、掉落或 UI。
- 运行前置：必须设定独立 `FU_XUAN_DATA` 并在其中创建 `.test_mode`；可选 `FU_XUAN_PROBE_PARAMETER=<Cubism 参数 ID>` 缩小为单参数复核。
- 全参数普查：设定 `FU_XUAN_PROBE_SCOPE=all`（且不设单参数变量）时，按运行时 `CubismModel.Parameters` 的实际集合采集全部参数；默认仍只采集首轮骨架候选。全参数普查仍只输出证据，不写映射。
- 物理复验：对默认冻结模式中零差异的物理输入，可显式设定 `FU_XUAN_PROBE_WRITER_MODE=physics`。此时只额外保留 Cubism 物理控制器；探针每点稳定 8 帧并调用 SDK `Stabilization()`，禁止同时启用任何桌宠行为组件。
- 输出：`capability-report-probe-window.json`、`probe_window/*.png`，每参三轮 `baseline/min/mid/max/reset`（15 帧）以及本地像素差/复位稳定性指标。
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
