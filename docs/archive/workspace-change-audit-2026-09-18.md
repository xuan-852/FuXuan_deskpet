# 2026-09-18 工作区变更审计与留档分层

> **状态**：审计记录；不代表未验证代码已经验收或允许发布。
>
> **范围**：2026-09-18 工作区中的 Live2D 平台化、具身动作、Probe、研究证据、符玄 fixture 和本地产物。
>
> **上位边界**：[Live2D 平台与符玄私有 Fixture 范围决策](../decisions/2026-09-18-live2d-platform-and-fuxuan-fixture-scope.md)、[文档治理与任务分发规范](../decisions/documentation-governance.md)、[第三方与模型许可边界](../third-party-and-model-licensing.md)。

## 1. 审计结论

原工作树显示的约 8.7 万行变更并不等于 8.7 万行有效平台代码。主要增量来自外部动作候选语料、截图、日志、SDK 包和本地工具文件。收紧 `.gitignore` 后，这些文件仍保留在本机，但不再混入普通 Git 变更；可见未跟踪集合从 652 个文件、约 85,778 行文本收敛到 60 个文件、约 2,559 行文本。

`ProbeWindow.unity` 的 1,251 行增加与 1,251 行删除来自场景生成器重新保存后的 Unity local file ID 重排：Prefab 与脚本 GUID 保持不变，没有确认出需要保留的语义变化。其差异已保存到本地忽略目录 `logs/audit/`，工作树中的场景已恢复到 HEAD。

没有执行删除证据、`git rm --cached`、历史重写、强制清理、提交或推送。已有跟踪模型资源不会因 `.gitignore` 自动退出 Git 索引。

## 2. 留档分层

### 2.1 主线候选：应保留并单独验证

- 平台范围决策、运行时模型适配指南、许可边界和 README 导航。
- 认证曲线哈希、运行时准入与 LLM 暴露分离。
- 单写入者租约、writer inventory、请求关联字段、终态原因和桌面身体快照。
- 测试模式的终端入口与按真实完成标记等待的运行时驱动。
- 对应 Editor Tests、隔离运行证据和模块文档。

这些内容有明确的安全或架构价值，但 C# 改动只有在 Quick、EditMode 和适用的隔离运行验证通过后才能写成已验证代码真相。

### 2.2 研究证据：可复核但不得升级为生产能力

- 外部动作来源审计、特征包、受约束重定向计划和参考 manifest。
- 招手组合能力普查、隔离候选和真人评审任务。
- `Candidate`、`Supporting`、`NeedsEvidence`、`ReviewFailed` 等状态材料。

这些文件只能证明其声明的有限研究事实；不得据此写正式映射、认证动作、生产数据、LLM 技能或安装包资产。

### 2.3 失败案例：仓库内显式归档

`model-workspaces/fuxuan-derivative-v1/` 与 `docs/archive/fuxuan-private-fixture-case-study.md` 保留为 `FixtureOnly / CaseStudy / ReviewFailed / BlockedByEvidence`。自动结构检查不构成视觉验收；任何恢复生产都必须另建决策、批准指南、任务包、权利复核、人工视觉验收和回滚方案。

fixture 中的 PNG/SVG 是待人工权利决定的衍生案例证据。未经权利复核，不得公开发布、商业分发、随安装包交付或提供源工程。

### 2.4 仅本地保留：不进入普通提交

- `assets/external_motions/` 原始包、导入语料和生成候选语料；
- `logs/`、`code/desktop_unity/screenshots/` 和构建/视觉临时输出；
- Cubism SDK Unity package、第三方模型源、纹理、导出包和参考目录；
- `.zcode/`、根 `.vscode/`、临时 npm `package*.json`、`node_modules/` 和预览图。

忽略仅用于降低误提交风险，不代表这些材料可公开、可再分发或已被删除。

## 3. 已识别但未静默修复的运行时风险

- 当前认证登记与“本机曲线已部署且哈希有效”仍是不同概念；LLM 可见列表和工具执行前应最终以部署可用性复核，不能仅依赖元数据登记。
- idle 租约、显式 legacy 动作和 CandidateTest 的优先级/释放顺序需要集成验证，避免自动 idle 阻塞显式动作。
- 协调器未来若启用非零优先级抢占，必须有执行器取消和姿势恢复契约，不能只释放协调状态。
- `CandidateTest` 当前默认 writer 标识会落到 `generated-motion`，审计分类不够准确。
- `DesktopBodySnapshot` 在暂停/锁切换时可能在下一次发布前滞后，需要集成测试确认并修复。

这些问题不应被文档包装成已解决；应作为后续独立代码变更处理。

## 4. 治理修正

- 认证动作生产部署文档保持在 `proposed/`；未将“提案、未经批准”文件错误提升到 `approved/`。
- 平台适配指南补充稳定需求/验收 ID、验收矩阵和“实施授权边界”。
- Probe 指南明确当前 `FU_XUAN_*`、固定 Prefab 和骨架集合是 legacy/fixture adapter 事实；通用 adapter、任意 `.model3.json` 输入及运行时热切换仍未实现。
- 旧符玄生产计划进入 archive，并明确历史授权已经撤销；fixture 内入站链接改指归档案例。
- 文档总索引增加平台决策、适配指南、Probe、许可边界和归档案例入口。

## 5. 验证记录与发布边界

已完成：

- 当前 `tasks/packages/` 下 44 个任务包 JSON 以 `utf-8-sig` 解析，0 个错误。
- Probe 场景差异的 GUID/local-ID 审计。
- 运行时代码/测试与文档治理双重只读审查。
- 外部大语料、截图、日志、SDK 和本地工具文件的忽略命中检查。

验证结果：

- `git diff --check` 通过；仅报告现有工作副本的 CRLF→LF 提示，没有空白错误。
- 44 个任务包 JSON 重新解析通过，0 个错误；旧符玄 active-guide 引用扫描为 0。
- `node scripts/docs/generate_document_map.cjs` 通过并重新生成索引；一项仍指向未批准部署提案的任务包已改指现行 Probe 指南。
- `\.\build.ps1 -Quick` 已启动但被正在运行的 Tuanjie Editor（PID 2932）阻断；未关闭用户进程，因此 C# 编译仍标记“未验证”。
- 因 Quick 未通过前置门禁，`\.\build.ps1 -RunTests` 与隔离运行脚本未执行。

仍待执行：

- 关闭现有 Tuanjie Editor 后重新运行 `\.\build.ps1 -Quick`；
- Quick 通过后运行 `\.\build.ps1 -RunTests`；
- 再运行适用的隔离运行脚本，且必须使用临时 `FU_XUAN_DATA` 与 `.test_mode`。

若文档地图仍被无关文档阻断，应如实保留失败，不得修改无关内容掩盖结果。任何测试未运行或失败的代码与文档只能标记“未验证”。

## 6. 建议提交拆分

1. 平台方向、许可、README、fixture 归档与 `.gitignore`；
2. 认证曲线完整性和非执行技能暴露修正；
3. 统一控制循环、输入租约、桌面身体状态及测试；
4. Probe/外部动作研究脚本、任务包和证据；
5. 文档索引与模块同步（仅在相应验证通过后）。

原始截图、日志、SDK 包、第三方源文件和大规模候选语料不进入上述提交。
