# 📚 docs/ 文档总索引

> **文档作用**: 本文件是 `docs/` 目录的**导航地图**——告诉 AI 与开发者每份文档的作用、归属模块、阅读优先级，以及统一的文档编写模板。
> **基本架构**: 三层结构——① 顶层权威文档（架构/规范/路线图/清单）→ ② `modules/` 模块文档（每模块一份，四要素）→ ③ 构建产物（report.* 等，勿手改）。
> **开发历史迭代**: 2026-08-12 由「平铺 14 份文档」重构为「索引 + 模块化」结构，全部模块文档统一四要素模板；当前 `modules/` 共 10 份模块文档。
> **编写注意事项**: 新增模块文档必须套用下方模板；修改架构/规范类文档需同步更新本索引与 `AGENTS.md`；`report.*` 是 LaTeX 构建产物，改动源文件 `report.tex` 而非 `report.md`。

---

## 一、文档地图

### 1.1 顶层权威文档（优先阅读）

| 文档 | 作用 | 阅读时机 |
|------|------|---------|
| [`README.md`](../README.md) | GitHub 项目介绍（特性亮点/架构图/快速上手/展示，**GitHub 默认渲染页**） | 访客首次了解项目 |
| [`AGENTS.md`](../AGENTS.md) | AI 协作快速入口（7 条铁律） | **每次开工前** |
| [`development-standards.md`](development-standards.md) | 唯一权威开发规范（9 章） | 写任何代码前 |
| [`build-workflow.md`](build-workflow.md) | **编译工作流（AI 必读）**（构建入口/卡死处理/验证闭环/坑清单） | **改 C# 后构建、或构建卡死时** |
| [`token-cost-testing.md`](token-cost-testing.md) | **Token 消耗与测试指南**（生产 vs 测试区别、消耗铁律、痛点状态） | **涉及云端调用/测试/排查烧钱前** |
| [`api-key-billing-attribution.md`](api-key-billing-attribution.md) | **API Key 归属与官方账单核对**（运行时 Key 指纹、用量日志归属、安全边界） | **核对云端消耗归属前** |
| [`token-saving-architecture.md`](token-saving-architecture.md) | **省 Token 基础架构**（请求分级、上下文预算、成本闸门与阶段路线） | **设计/修改 Token 成本控制前** |
| [`quality-measurement-test-guide.md`](quality-measurement-test-guide.md) | **编译与本地质量采样说明**（新构建核验、Ollama 采样、质量/成本汇总） | **编译后测量本地模型质量前** |
| [`quality-comparison-test-guide.md`](quality-comparison-test-guide.md) | **本地 / 云端配对对照说明**（纯云端基线、案例编号、质量差值） | **测量本地与云端质量差异前** |
| [`quality-comparison-report-2026-08-18.md`](quality-comparison-report-2026-08-18.md) | **本地/云端质量对照测试报告**（60 案例实测：成功率/延迟/成本对比 + 局限） | **查看质量对照结论时** |
| [`reply-quality-evaluation-plan.md`](reply-quality-evaluation-plan.md) | **回复内容质量测评方案**（人设/记忆/时间/相关性/约束 5 维 rubric + 裁判机制） | **评价"回复像不像符玄"时** |
| [`project-bugs-and-acceptance.md`](project-bugs-and-acceptance.md) | **项目已知 Bug 与验收点**（活跃问题/已修防回归清单/验收标准） | **改外置窗口/渲染/退出/测试代码前** |
| [`ui-acceptance-checklist.md`](ui-acceptance-checklist.md) | **UI 验收清单（考评师版）**（排版/功能/进阶/回归红线，含多模态验证项） | **UI 回归验收 / 交付签发前** |
| [`ui-external-window-test-plan-2026-08-17.md`](ui-external-window-test-plan-2026-08-17.md) | **外置独立面板专项测评方案**（真实鼠标/键盘优先，点击/拖动 P0 项） | **外置窗口交互回归（codex 第三轮）** |
| [`code-truth-architecture.md`](code-truth-architecture.md) | 代码真相架构审计（六层架构） | 改架构/子系统前 |
| [`desktop-assistant-roadmap.md`](desktop-assistant-roadmap.md) | 项目演进路线图（v0.2） | 规划新功能前 |
| [`installer-plan.md`](installer-plan.md) | 安装包与分发方案（Inno Setup、组件安装、移植障碍清单） | 打包/分发/换机部署前 |
| [`data-directory-cleanup-manifest-2026-08-21.md`](data-directory-cleanup-manifest-2026-08-21.md) | `D:\DesktopPetData` 数据分类、整理映射与安装/卸载生命周期约定 | 整理用户数据或修改安装器前 |
| [`task-inventory.md`](task-inventory.md) | 项目任务清单（N40+，65 工具） | 接任务/汇报进度时 |

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
| 运行时状态 | [`modules/runtime-readiness.md`](modules/runtime-readiness.md) | 启动自检、请求状态、停止恢复、云端保护 |

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
4. **文档优先级**（AI 读取顺序）：`AGENTS.md` → `development-standards.md` → `code-truth-architecture.md` → `token-cost-testing.md` → `token-saving-architecture.md` → 对应模块文档
5. **数据真实性**：模块文档中的组件名/工具数/行号必须以代码为准（参考 `code-truth-architecture.md` 的审计方法），禁止沿用过时描述
