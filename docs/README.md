# 📚 docs/ 文档总索引

> **2026-09-06 项目测评**：[`project-evaluation-2026-09-06.md`](project-evaluation-2026-09-06.md) 记录全项目工程检查范围、脚本/办公/桥接实测、安装与测试隔离缺陷，以及尚未完成的验收门槛；不替代历史专项报告或人工签字。

> **索引状态**: 2026-09-04 复核；代码改动完成并通过相应验证后，必须同步更新模块文档与受影响的顶层文档。
> **文档作用**: 本文件是 `docs/` 目录的**导航地图**——告诉 AI 与开发者每份文档的作用、归属模块、阅读优先级，以及统一的文档编写模板。
> **基本架构**: 三层结构——① 顶层权威文档（架构/规范/路线图/清单）→ ② `modules/` 模块文档（每模块一份，四要素）→ ③ 构建产物（report.* 等，勿手改）。
> **开发历史迭代**: 2026-08-12 由「平铺 14 份文档」重构为「索引 + 模块化」结构，全部模块文档统一四要素模板；当前 `modules/` 共 10 份模块文档。
> **编写注意事项**: 新增模块文档必须套用下方模板；修改架构/规范类文档需同步更新本索引与 `AGENTS.md`；`report.*` 是 LaTeX 构建产物，改动源文件 `report.tex` 而非 `report.md`。
> **AI 调试入口**: 测试模式下通过数据根目录的 `inbox.txt` 写入 `@@sim:*`/`@@input:*` 可模拟桌宠点击、拖动、读取运行时状态并由 Unity 保存截图；命令协议与安全边界见 `development-standards.md` §6.6。

## 当前项目文档基线（2026-09-04）

- 2026-09-12 本地工具路由已完成隔离修复：搜索项目 README 与打开桌面均经自然语言 inbox 实测；白名单统一、Everything 缺失降级和 Shell 启动错误反馈详见 `modules/tool-engine.md`。Quick、EditMode（failed=0）与隔离运行验证通过，正式数据未改动。
- 当前文档同步提交：`96f199d`；节日代码与自动验证归档提交：`06a51d2`。
- 节日动态呼吸与重绘修复提交：`b531d03`；修复后五主题隔离评测、完整构建、EditMode 和运行时冒烟均通过。
- 节日主题快捷命令提交：`014e7b1`；聊天输入框支持 `/tell theme <主题ID>`，设置页同步显示主题 ID 和 `off/auto/status/list` 用法。
- 设置页主题说明布局提交：`e09c2b1`；说明底板、文字对比度和与任务权重区域的间距已完成最终截图复核。
- 主题快捷命令只在本地处理，不进入 LLM、聊天历史或忆境；自动化取证仍使用测试模式下的 `@@sim:holiday:*`。
- 正式节日范围固定为新春 `cn_new_year`、元宵 `lantern_festival`、端午 `dragon_boat`、七夕 `qixi`、中秋 `mid_autumn`。五个主题均已完成代码实现、隔离自动验证、四类 Unity 截图和视觉预审，综合预评分为 91/92/91/91/91，当前均无 P0/P1/P2。
- 2026-09-04 视觉优化复测已补齐紧凑窗口安全区、按宽度响应式诗词列数和五主题层次/动效微调；最终隔离评测 16 张截图、EditMode `failed=0`、完整构建和数据隔离检查通过。该复测未重新签发正式评分，真实 GUI 双击/拖拽签字门槛保持不变。
- 2026-09-04 系统关机退出修复已补齐 `WM_QUERYENDSESSION`/`WM_ENDSESSION` 到统一 `BeginShutdown` 的链路；隔离消息探针、Quick、完整构建和运行时冒烟通过，真实关机/注销仍需人工观察。
- “已实现/自动验证/截图预审”与“最终验收完成”严格分开：五个主题仍待真实 GUI 双击展开和拖拽/收回人工签字，T3/T5 未关闭，G1～G5 不能提前标记完成。
- 带日期的评价和修复报告保留原结论；当前状态以本索引、节日评价报告附录、审核标准和任务清单为准。截图与临时测试目录不入库。

---

## 0. 文档治理架构（迁移中）

从 2026-09-15 起，项目采用“决策 → 指导 → 任务包 → 代码真相”的分层方式。目录位置是唯一状态来源；不要手工维护另一份完成状态表。

```text
docs/decisions/        跨模块决策与方向边界
docs/guides/proposed/  讨论中的功能指导
docs/guides/approved/  可下发实施任务的功能指导
tasks/packages/        机器可读任务包（只引用 approved 指导）
docs/truth/            已验证的代码/运行时事实
docs/archive/          迁移后的历史材料
docs/generated/        自动生成索引，禁止手工编辑
```

- 必读：[产品方向基线](decisions/2026-09-15-product-direction-baseline.md) 与 [文档治理规范](decisions/documentation-governance.md)。
- 功能文档使用 [指导文档模板](templates/feature-guide.md)；任务使用 [任务包模板](../tasks/templates/task-package.example.json)。
- 运行 `node scripts/docs/generate_document_map.cjs` 更新 [自动索引](generated/document-map.md)。
- 现有顶层规划、路线图、报告和 `modules/` 尚处迁移前形态：除已明确引用的代码事实外，不能以它们的“完成”描述代替新体系的验收证据。

---

## 一、文档地图

### 1.1 顶层权威文档（优先阅读）

| 文档 | 作用 | 阅读时机 |
|------|------|---------|
| [`README.md`](../README.md) | GitHub 项目介绍（特性亮点/架构图/快速上手/展示，**GitHub 默认渲染页**） | 访客首次了解项目 |
| [`AGENTS.md`](../AGENTS.md) | AI 协作快速入口（9 条铁律） | **每次开工前** |
| [`development-standards.md`](development-standards.md) | 唯一权威开发规范（9 章） | 写任何代码前 |
| [`build-workflow.md`](build-workflow.md) | **编译工作流（AI 必读）**（构建入口/卡死处理/验证闭环/坑清单） | **改 C# 后构建、或构建卡死时** |
| [`build-test-pipeline-fixes-2026-08-31.md`](build-test-pipeline-fixes-2026-08-31.md) | **修改说明**：构建负载保护 + `-RunTests` 门禁修复 + 4 个隐藏测试失败修复（P0/测试管线） | **查看本轮构建/测试改动与验证结论时** |
| [`token-cost-testing.md`](token-cost-testing.md) | **Token 消耗与测试指南**（生产 vs 测试区别、消耗铁律、痛点状态） | **涉及云端调用/测试/排查烧钱前** |
| [`api-key-billing-attribution.md`](api-key-billing-attribution.md) | **API Key 归属与官方账单核对**（运行时 Key 指纹、用量日志归属、安全边界） | **核对云端消耗归属前** |
| [`token-saving-architecture.md`](token-saving-architecture.md) | **省 Token 基础架构**（请求分级、上下文预算、成本闸门与阶段路线） | **设计/修改 Token 成本控制前** |
| [`quality-measurement-test-guide.md`](quality-measurement-test-guide.md) | **编译与本地质量采样说明**（新构建核验、Ollama 采样、质量/成本汇总） | **编译后测量本地模型质量前** |
| [`quality-comparison-test-guide.md`](quality-comparison-test-guide.md) | **本地 / 云端配对对照说明**（纯云端基线、案例编号、质量差值） | **测量本地与云端质量差异前** |
| [`quality-comparison-report-2026-08-18.md`](quality-comparison-report-2026-08-18.md) | **本地/云端质量对照测试报告**（60 案例实测：成功率/延迟/成本对比 + 局限） | **查看质量对照结论时** |
| [`reply-quality-evaluation-plan.md`](reply-quality-evaluation-plan.md) | **回复内容质量测评与实现说明**（5 维 rubric、规则/本地/云端裁判、质量遥测） | **评价回复质量或修改裁判链路时** |
| [`project-bugs-and-acceptance.md`](project-bugs-and-acceptance.md) | **项目已知 Bug 与验收点**（活跃问题/已修防回归清单/验收标准） | **改外置窗口/渲染/退出/测试代码前** |
| [`ui-acceptance-checklist.md`](ui-acceptance-checklist.md) | **UI 验收清单（考评师版）**（排版/功能/进阶/回归红线，含多模态验证项） | **UI 回归验收 / 交付签发前** |
| [`ui-external-window-test-plan-2026-08-17.md`](ui-external-window-test-plan-2026-08-17.md) | **外置独立面板专项测评方案**（真实鼠标/键盘优先，点击/拖动 P0 项） | **外置窗口交互回归（codex 第三轮）** |
| [`code-truth-architecture.md`](code-truth-architecture.md) | 代码真相架构审计（六层架构） | 改架构/子系统前 |
| [`embodied-ai-optimization-architecture.md`](embodied-ai-optimization-architecture.md) | **具身 AI 优化设计**（分层仲裁、状态/命令接口、安全约束与实施阶段） | 规划或修改 ActionAgent 架构前 |
| [`embodied-intelligence-guidance.md`](embodied-intelligence-guidance.md) | **具身智能指导文档**（已确认边界、能力普查、LLM 控制与验收规范） | 设计、实现或验收具身智能前 |
| [`decisions/2026-09-15-product-direction-baseline.md`](decisions/2026-09-15-product-direction-baseline.md) | **产品方向决策基线**（L0–L6 已确认目标、边界与待细化项；非代码真相） | 编写功能指导文档、下发任务或处理方向冲突前 |
| [`decisions/documentation-governance.md`](decisions/documentation-governance.md) | **文档治理与任务分发规范**（目录即状态、任务包边界、冲突阻断与生成索引） | 新建/迁移文档、下发任务包或验收前 |
| [`generated/document-map.md`](generated/document-map.md) | **自动文档与任务索引**（由脚本生成，禁止手工编辑） | 快速查看当前决策、指导文档、代码真相与任务包 |
| [`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md) | **冻结的旧路线图**（v0.3，2026-08-29） | 只查历史背景；新方向以决策/指导文档为准 |
| [`decisions/2026-09-18-live2d-platform-and-fuxuan-fixture-scope.md`](decisions/2026-09-18-live2d-platform-and-fuxuan-fixture-scope.md) | **Live2D 平台与符玄私有 Fixture 范围决策** | 处理模型生产、适配、发布或方向冲突前 |
| [`guides/approved/live2d-runtime-platform-and-model-adapter.md`](guides/approved/live2d-runtime-platform-and-model-adapter.md) | **Live2D 运行时平台与模型适配指南** | 设计模型适配器、Probe、认证与降级前 |
| [`guides/approved/generic-live2d-capability-probe.md`](guides/approved/generic-live2d-capability-probe.md) | **通用 Live2D 能力探测规范** | 对合法模型做隔离参数与视觉证据采集前 |
| [`third-party-and-model-licensing.md`](third-party-and-model-licensing.md) | **第三方与模型许可边界** | 导入、打包、公开或分发模型与外部素材前 |
| [`archive/fuxuan-private-fixture-case-study.md`](archive/fuxuan-private-fixture-case-study.md) | **旧符玄模型生产计划的归档案例** | 只查失败过程；不作为活跃模型生产入口 |
| [`archive/workspace-change-audit-2026-09-18.md`](archive/workspace-change-audit-2026-09-18.md) | **工作区变更审计与留档分层** | 查看 2026-09-18 大规模变更的分类、保留边界和验证阻断 |
| [`installer-plan.md`](installer-plan.md) | 安装包与分发方案（Inno Setup、组件安装、移植障碍清单） | 打包/分发/换机部署前 |
| [`data-directory-cleanup-manifest-2026-08-21.md`](data-directory-cleanup-manifest-2026-08-21.md) | 数据分类、整理映射与安装/卸载生命周期约定（默认根目录由 `DataPathConfig` 决定） | 整理用户数据或修改安装器前 |
| [`task-inventory.md`](task-inventory.md) | **冻结的旧任务清单**（N40+，65 工具） | 只查历史；新任务只能使用 `tasks/packages/` |
| [`optimization.md`](optimization.md) | **冻结的旧优化路线** | 只查历史；新优化以批准指导文档和代码真相为准 |
| [`holiday-skin-development-guide.md`](holiday-skin-development-guide.md) | 节日皮肤设计、实现、测试、任务目标与交付规范 | 新增或修改节日主题前 |
| [`holiday-skin-review-standard.md`](holiday-skin-review-standard.md) | 节日皮肤视觉、功能、性能、安全、标准验收流程与截图审核标准 | 节日主题验收、提交或发布前 |
| [`holiday-skin-evaluation-2026-08-31.md`](holiday-skin-evaluation-2026-08-31.md) | 删除前 8 主题历史逐主题评价 + 当前 5 主题状态附录 | 查看历史评分和当前验收状态 |
| [`project-evaluation-2026-08-31.md`](project-evaluation-2026-08-31.md) | 2026-08-31 历史工程治理与节日皮肤评价；当前入口见附录 | 查看历史代码/文档评审结论时 |

### 1.2 模块文档（modules/，每份含四要素）

| 模块 | 文档 | 覆盖范围 |
|------|------|---------|
| AI 对话 | [`modules/ai-chat-system.md`](modules/ai-chat-system.md) | ChatManager、ApiClient、言出法随、Token 优化历史 |
| 工具系统 | [`modules/tool-engine.md`](modules/tool-engine.md) | ToolEngine、65 工具、审批、benchmark 报告 |
| 动作系统 | [`modules/action-agent.md`](modules/action-agent.md) | ActionAgent、MotionPlanner/Translator、验证闭环、行走研究 |
| Live2D 渲染 | [`modules/live2d-rendering.md`](modules/live2d-rendering.md) | Live2DRenderer、参数映射、硬编码迁移清单 |
| 对话界面 | [`modules/chat-ui.md`](modules/chat-ui.md) | RightPanel、ChatBubble、像素化优化 |
| 桥接通信 | [`modules/bridge-communication.md`](modules/bridge-communication.md) | OpenClawBridge.cs、openclaw_bridge.js、Python 调用链 |
| 记忆人格 | [`modules/memory-personality.md`](modules/memory-personality.md) | PetMemory、人格演化、知识库 |
| 编码协议 | [`modules/encoding-protocol.md`](modules/encoding-protocol.md) | .editorconfig、BOM、乱码排查历史 |
| 办公工具 | [`modules/office-tools.md`](modules/office-tools.md) | PPT/Word/Excel 生成器、/generate_office 端点 |
| 运行时状态 | [`modules/runtime-readiness.md`](modules/runtime-readiness.md) | 启动自检、请求状态、停止恢复、云端保护（已按四要素模板整理） |

### 1.3 构建产物（勿手改）

| 文件 | 说明 |
|------|------|
| `report.md` / `report.tex` / `report.pdf` | LaTeX 技术报告（改源 `report.tex`，`report.md` 由工具生成） |
| `report.aux/.out/.toc/.log` | LaTeX 中间产物（gitignore） |

---

## 二、模块文档统一模板（四要素）

每个 `modules/` 文档必须包含以下四个区块（顺序固定）：

```markdown
# <模块名> — <一句话定位>

> **文档作用**: 这个模块是做什么的 / 谁该读 / 解决什么问题
> **基本架构**: 组件清单、数据流、关键文件路径
> **开发历史迭代**: 版本时间线（N 号 + 日期 + 关键变更/修复）
> **编写注意事项**: 改这个模块时的铁律 / 常见坑 / 验证方法

---

## 一、文档作用
## 二、基本架构
## 三、开发历史迭代
## 四、编写注意事项
```

### 四要素编写要点

| 要素 | 要点 |
|------|------|
| **文档作用** | 一句话定位 + 谁该读 + 与哪些文档关联；避免与架构文档重复 |
| **基本架构** | 只写**代码真相**（以 `Assets/Scripts/` 实际代码为准），组件表 + 数据流 + 关键文件路径 |
| **开发历史迭代** | 按时间倒序或正序均可（全库统一用正序），标注任务号（N 号）与提交号；历史存档报告归入此节 |
| **编写注意事项** | 铁律（副作用/危险操作）、常见坑（BOM/编码/超时）、验证命令、测试模式要求 |

---

## 三、维护规范

1. **新增模块**：在 `modules/` 新建文档 → 套用四要素模板 → 在 1.2 节加一行 → 更新 `AGENTS.md` 技术栈表
2. **修改模块文档**：功能级改动**测试通过后**再更新对应 `modules/` 文档（先代码后文档），并核对 1.2 节索引表
3. **修改顶层文档**：确认是否影响索引（标题/路径/作用变化时同步更新）
4. **文档优先级**（AI 读取顺序）：`AGENTS.md` → `docs/README.md` → `development-standards.md` → `build-workflow.md` → `code-truth-architecture.md` → `token-cost-testing.md` → `token-saving-architecture.md` → 质量测试指南 → `project-bugs-and-acceptance.md` → 对应模块文档
5. **数据真实性**：模块文档中的组件名/工具数/行号必须以代码为准（参考 `code-truth-architecture.md` 的审计方法），禁止沿用过时描述
## 2026-08-30 文档同步

## 2026-09-10 安装包状态同步

v1.0.13 已以当前完整构建重建 portable、EXE、ZIP 和 SHA256；Windows PowerShell 5.1 已能解析随包的 Ollama 下载脚本，且内置 Node 固定为 v22.22.3。`D:\Fuxuan` 本机静默安装的文件落地和 OpenClaw 初始化通过；NSSM 未随包提供、在线下载失败时桥接服务不会注册，因此干净机服务验收及商业签名仍为发布阻塞项。详见 [`installer-plan.md`](installer-plan.md) 与 [`task-inventory.md`](task-inventory.md)。

安装包/OpenClaw 桥接链路已完成一轮实现收敛，详见 [`installer-plan.md`](installer-plan.md) 与 [`modules/bridge-communication.md`](modules/bridge-communication.md)。本轮代码和安装产物均已通过对应验证；任务状态已同步到 [`task-inventory.md`](task-inventory.md)。

本轮安全修复已通过 `node --check` 与 full-access `build.ps1 -Quick`：Bridge/Gateway Token 分离，本地文件路径和重解析点受限，文件内容/剪贴板/截图读取需确认，桥接请求与任务资源有上限；安装组件拒绝令牌复用、固定使用内置 Node.js，并在执行 Ollama/MiKTeX/VC++ 外部安装器前验证 Authenticode。安装服务最小权限、安装包签名和依赖哈希仍未完成发布验收。

本轮 Live2D/问候修复已同步到 [`modules/live2d-rendering.md`](modules/live2d-rendering.md)、[`modules/ai-chat-system.md`](modules/ai-chat-system.md)、[`project-bugs-and-acceptance.md`](project-bugs-and-acceptance.md)、[`optimization.md`](optimization.md)、[`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md) 与 [`task-inventory.md`](task-inventory.md)：停走物理收敛、自动问候时段守卫和 ILPP PID 身份校验已通过 Quick、完整构建和隔离运行时冒烟；可见播放器观感仍待人工确认。

本轮节日适配（2026-08-30～09-04）已同步到 [`modules/chat-ui.md`](modules/chat-ui.md)、[`modules/runtime-readiness.md`](modules/runtime-readiness.md)、[`optimization.md`](optimization.md)、[`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md) 与 [`task-inventory.md`](task-inventory.md)：以完整 `ThemeSkin` 管理 17×24 像素符玄、聊天窗口、气泡和子面板的 5 个中国传统节日主题，独立配饰与动态背景不修改 Live2D；Quick、完整构建、隔离冒烟和逐主题 Unity 截图闭环均通过。当前五个主题均待负责人完成真实 GUI 双击/拖拽复核后关闭 T3/T5，不能仅凭 `@@sim` 提前标记为最终完成。
