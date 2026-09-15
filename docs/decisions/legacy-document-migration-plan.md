# 旧文档迁移与归档计划

> **状态**：已确认迁移策略；尚未执行移动或删除。
>
> **依据**：[文档治理与任务分发规范](documentation-governance.md) DG-40、[产品方向基线](2026-09-15-product-direction-baseline.md) §1。

## 1. 迁移原则

1. 先建立新指导文档/代码真相及更新所有入站链接，再移动旧文件；不直接删除历史材料。
2. 旧文件中的“完成”表述不自动迁移为新代码真相，必须由独立证据重新确认。
3. `task-inventory.md`、`desktop-assistant-roadmap.md`、`optimization.md` 在替代物形成前冻结为只读参考，不能再承担新任务状态。
4. 带日期的评测、一次性修复和研究材料默认保留原始结论，迁入 `docs/archive/` 时写明替代入口。

## 2. 清单与去向

| 当前文件/组 | 新体系角色 | 当前动作 | 迁移完成条件 |
|---|---|---|---|
| `AGENTS.md`、`docs/README.md`、`development-standards.md` | 项目入口/开发规范 | 保留并逐步改为指向新索引 | 所有旧“接任务/状态”入口改为任务包与生成索引 |
| `code-truth-architecture.md`、`modules/*.md` | 旧代码事实入口 | 保留，不直接移动 | 每一模块经当前代码和测试重建到 `docs/truth/` 后标注替代链接 |
| `build-workflow.md`、`project-bugs-and-acceptance.md`、`token-*.md`、质量测试指南 | 现行专项规范/风险依据 | 保留 | 被对应 L1/L4 指导引用，并分离“规范”与“历史结论” |
| `holiday-skin-development-guide.md`、`holiday-skin-review-standard.md` | 专项指导与验收规范 | 保留 | 后续以同一模板补齐需求 ID/任务包边界 |
| `installer-plan.md`、`data-directory-cleanup-manifest-2026-08-21.md` | L6 输入材料 | 保留 | L6 批准指导建立后，明确哪些结论仍有效 |
| `task-inventory.md`、`desktop-assistant-roadmap.md`、`optimization.md`、`desktop-agent-task-progress-plan.md` | 旧任务/路线记录 | 冻结，禁止新增状态 | 生成索引与任务包承接活跃状态、所有入站链接替换后归档 |
| `embodied-ai-verification.md`、`verification_report_2026-07-07.md`、`walk_cycle_research.md`、`motion-performance-fix-2026-08-20.md` | L3 历史研究/验证 | 保留为候选证据 | L3 当前认证账本建立后归档，旧分数标为历史 |
| `build-test-pipeline-fixes-2026-08-31.md`、`build-harness-questions.md` | L1 历史修复/调查 | 保留 | L1 代码真相覆盖相关事实后归档 |
| `project-evaluation-*.md`、`ui-acceptance-report-*.md`、`quality-comparison-report-*.md`、`tool-benchmark-report-*.md`、`holiday-skin-evaluation-*.md` | 历史评测证据 | 保留 | 当前验收文档引用必要原始证据后归档 |
| `pixel-dialogue-optimization.md`、`reply-quality-evaluation-plan.md`、`encoding-map.md` | 专项研究/旧方案 | 保留待复核 | 有对应批准指导或确认无后续价值后归档 |
| `report.*` | 构建产物/技术报告 | 不在本次迁移范围 | 继续按其生成来源维护 |

## 3. 分批执行顺序

### 批次 A：防止继续失真

- 已建立新目录、模板、任务包格式和自动索引。
- 将所有新开发任务改为只通过 `tasks/packages/` 下发。
- 在索引与入口文档中把旧路线图/任务表明确为冻结参考。

### 批次 B：建立可替代的新文档

- L1 至 L6 逐份从 `guides/proposed/` 讨论、补齐并批准。
- 每个已实施模块再建立 `docs/truth/`，链接到具体提交、构建和测试证据。
- 旧模块说明不删除，直到新代码真相覆盖相同范围。

### 批次 C：链接审计与归档

- 使用 `rg` 检查每个待归档文件的入站 Markdown 链接。
- 更新链接或在旧路径放置指向归档路径/替代文档的短说明。
- 仅在索引、任务包和相关指南均不再依赖旧路径后移动到 `docs/archive/`。

## 4. 不可违反约束

- 任何移动前必须做 `git diff --check` 和链接审计；不得批量删除。
- 历史评测的日期、原始分数、限制与“待人工确认”结论不得被重写成当前结论。
- 归档不等于问题已关闭；未完成验收必须由新的指导文档或代码真相明确承接。
- 如果一个旧文档同时承载活跃规范和历史报告，先拆分，不直接归档。
