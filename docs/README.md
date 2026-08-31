# 📚 docs/ 文档总索引

> **索引状态**: 2026-08-31 复核；代码改动完成并通过相应验证后，必须同步更新模块文档与受影响的顶层文档。
> **文档作用**: 本文件是 `docs/` 目录的**导航地图**——告诉 AI 与开发者每份文档的作用、归属模块、阅读优先级，以及统一的文档编写模板。
> **基本架构**: 三层结构——① 顶层权威文档（架构/规范/路线图/清单）→ ② `modules/` 模块文档（每模块一份，四要素）→ ③ 构建产物（report.* 等，勿手改）。
> **开发历史迭代**: 2026-08-12 由「平铺 14 份文档」重构为「索引 + 模块化」结构，全部模块文档统一四要素模板；当前 `modules/` 共 10 份模块文档。
> **编写注意事项**: 新增模块文档必须套用下方模板；修改架构/规范类文档需同步更新本索引与 `AGENTS.md`；`report.*` 是 LaTeX 构建产物，改动源文件 `report.tex` 而非 `report.md`。
> **AI 调试入口**: 测试模式下通过数据根目录的 `inbox.txt` 写入 `@@sim:*`/`@@input:*` 可模拟桌宠点击、拖动、读取运行时状态并由 Unity 保存截图；命令协议与安全边界见 `development-standards.md` §6.6。

---

## 一、文档地图

### 1.1 顶层权威文档（优先阅读）

| 文档 | 作用 | 阅读时机 |
|------|------|---------|
| [`README.md`](../README.md) | GitHub 项目介绍（特性亮点/架构图/快速上手/展示，**GitHub 默认渲染页**） | 访客首次了解项目 |
| [`AGENTS.md`](../AGENTS.md) | AI 协作快速入口（9 条铁律） | **每次开工前** |
| [`development-standards.md`](development-standards.md) | 唯一权威开发规范（9 章） | 写任何代码前 |
| [`build-workflow.md`](build-workflow.md) | **编译工作流（AI 必读）**（构建入口/卡死处理/验证闭环/坑清单） | **改 C# 后构建、或构建卡死时** |
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
| [`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md) | 项目演进路线图（v0.3，2026-08-29） | 规划新功能前 |
| [`installer-plan.md`](installer-plan.md) | 安装包与分发方案（Inno Setup、组件安装、移植障碍清单） | 打包/分发/换机部署前 |
| [`data-directory-cleanup-manifest-2026-08-21.md`](data-directory-cleanup-manifest-2026-08-21.md) | 数据分类、整理映射与安装/卸载生命周期约定（默认根目录由 `DataPathConfig` 决定） | 整理用户数据或修改安装器前 |
| [`task-inventory.md`](task-inventory.md) | 项目任务清单（N40+，65 工具） | 接任务/汇报进度时 |
| [`optimization.md`](optimization.md) | 当前已验证优化、后续优先级与统一验收标准 | 规划重构、性能、稳定性或成本优化前 |
| [`holiday-skin-development-guide.md`](holiday-skin-development-guide.md) | 节日皮肤设计、实现、测试与交付规范 | 新增或修改节日主题前 |
| [`holiday-skin-review-standard.md`](holiday-skin-review-standard.md) | 节日皮肤视觉、功能、性能、安全与截图审核标准 | 节日主题验收、提交或发布前 |
| [`holiday-skin-evaluation-2026-08-31.md`](holiday-skin-evaluation-2026-08-31.md) | 节日皮肤逐主题评价（**对照审核标准，真实抓图**，视觉均值 48.6/60） | 查看 8 主题逐项视觉评分与验收结论时 |
| [`project-evaluation-2026-08-31.md`](project-evaluation-2026-08-31.md) | 当前项目工程治理与节日皮肤实现评价报告 | 查看本轮代码/文档评审结论时 |

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

安装包/OpenClaw 桥接链路已完成一轮实现收敛，详见 [`installer-plan.md`](installer-plan.md) 与 [`modules/bridge-communication.md`](modules/bridge-communication.md)。本轮代码和安装产物均已通过对应验证；任务状态已同步到 [`task-inventory.md`](task-inventory.md)。

本轮安全修复已通过 `node --check` 与 full-access `build.ps1 -Quick`：Bridge/Gateway Token 分离，本地文件路径和重解析点受限，文件内容/剪贴板/截图读取需确认，桥接请求与任务资源有上限；安装组件拒绝令牌复用、固定使用内置 Node.js，并在执行 Ollama/MiKTeX/VC++ 外部安装器前验证 Authenticode。安装服务最小权限、安装包签名和依赖哈希仍未完成发布验收。

本轮 Live2D/问候修复已同步到 [`modules/live2d-rendering.md`](modules/live2d-rendering.md)、[`modules/ai-chat-system.md`](modules/ai-chat-system.md)、[`project-bugs-and-acceptance.md`](project-bugs-and-acceptance.md)、[`optimization.md`](optimization.md)、[`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md) 与 [`task-inventory.md`](task-inventory.md)：停走物理收敛、自动问候时段守卫和 ILPP PID 身份校验已通过 Quick、完整构建和隔离运行时冒烟；可见播放器观感仍待人工确认。

本轮节日适配（2026-08-30～31）已同步到 [`modules/chat-ui.md`](modules/chat-ui.md)、[`optimization.md`](optimization.md)、[`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md) 与 [`task-inventory.md`](task-inventory.md)：以完整 `ThemeSkin` 管理 17×24 像素符玄、聊天窗口、气泡和子面板的 8 个节日主题，独立配饰与动态背景不修改 Live2D；Quick、完整构建、隔离冒烟和 8 个主题截图闭环均通过。
