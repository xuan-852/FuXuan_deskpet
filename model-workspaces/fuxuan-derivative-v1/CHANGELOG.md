# 变更日志（CHANGELOG）

格式参考[Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)；版本号按 `0.x.0-stageN` 递进，一个 Stage 至少一条记录。

## [0.3.0-fixture-frozen-review-failed] - 2026-09-18

### Fixture 范围冻结

- 本工作区从模型生产计划降级为 `FixtureOnly / CaseStudy`，不再作为 Cubism、Unity、生产映射或 LLM 接入任务的前置。
- Stage 2 视觉结论保持 `ReviewFailed`；许可不确定项保持 `BlockedByEvidence`。自动结构检查不能改变这两个结论。
- 后续任何生产候选恢复必须新建方向决策、批准指导、任务包、权利复核、人工视觉验收和回滚方案。

## [0.3.0-stage2-artwork-review-pending] - 2026-09-18

### Stage 2：原创分层彩色绘稿

- 依据项目所有者 2026-09-18 决策，私有研发范围允许公开官方视觉仅作人工参考，并制作原创重绘；该决策不构成官方授权，公开发布、商业化、再分发、安装包交付和源工程提供仍保持阻断。
- 创建 `artwork-source/fuxuan_derivative_v01_neutral.svg`，作为 4096×4096 透明画布上的原创、可编辑等价分层彩色设计源；未导入旧模型、官方媒体、外部纹理或未知来源图像。
- 创建 `artwork-source/layer-manifest-v01.md`、`artwork-source/fuxuan_derivative_v01_neutral.png`、`artwork-source/SHA256SUMS` 与 `acceptance/stage2-artwork-acceptance-report.md`；结构、尺寸、alpha、命名图层与哈希验证已完成，最终人工视觉复核仍待完成。
- 本 Stage 仍未创建 Cubism 工程、参数、物理、导出物、Unity 候选或认证动作；人工视觉复核通过前不得进入 Stage 3。

## [0.2.0-stage1-review-pending] - 2026-09-18

### Stage 1：视觉规格与画布布局

- 新增原创中性姿势布局执行表 `design/layer-layout.md`：固定 4096×4096 构图、中心线、部件包围盒、图层命名、绘制顺序、遮挡补画规则与动态余量；该表不是最终绘稿或运行时参数事实。
- 新增 Stage 2 绘稿输入约束 `artwork-source/README.md`，以及原创抽象布局线框 `artwork-source/fuxuan-neutral-layout-v01.svg`；未导入现有模型或任何外部角色图像、纹理、网格、工程、曲线或物理配置。
- 新增设计验收图 `acceptance/thumbnail-check-v01.svg`、`acceptance/silhouette-check-v01.svg` 和验收报告 `acceptance/model-acceptance-report.md`。检查图仅评估构图与轮廓，不能替代最终分层绘稿、Cubism 导出、Unity 导入或能力认证证据。
- 候选 HoYoLAB 官方角色和二创规则页面于 2026-09-18 因访问控制无法验证；其后登记 HoYoverse 可公开访问的符玄官方媒体记录，仅限人工视觉核对。该记录不含二创、改作、发布或素材复用授权；`sources/license-register.md` 的 `LR-01` 维持 `BlockedByEvidence`。在登记有效操作性规则条款或取得权利人书面许可，并完成用户人工视觉复核前，AC-MODEL-01 与 AC-MODEL-08 均未通过，不能进入 Stage 2。

## [0.1.0-stage0] - 2026-09-18

### Stage 0：项目与许可冻结

- 建立独立工作区 `model-workspaces/fuxuan-derivative-v1/`，与生产模型目录 `Assets/Live2D/Models/Fuxuan/` 完全隔离；四类边界（源文件/生成文件/隔离证据/生产接入文件）按计划 §6 目录约定。
- 上位指导文档《符玄二创 Live2D 模型生产计划》经项目所有者批准移入 `docs/guides/approved/`（2026-09-18）。
- 登记六个权利对象（`sources/license-register.md`）：角色 IP（miHoYo）、原创内容、现有模型包（仅抽象布局参考，禁止复制像素/工程/导出物）、未来外部素材（未登记不得进入）、Cubism 工具链、本项目代码。
- 登记参考来源（`sources/source-register.md`）：现有模型包以 SHA-256 绑定参考版本（moc3/model3.json/physics3.json/cdi3.json/使用说明）；官方视觉参考与 Live2D 工具链条款登记为开放项。
- 冻结首版规格（`design/model-spec.yaml` v0.1.0-stage0）：用途（仅私用研发）、画布 4096×4096 正面站姿、服饰完整交付规则、六项基础生命动态、五项服饰物理优先项、参数设计原则（新 ID 体系/语义分离/预留通道/单写入者）、非目标清单。
- 门槛结论：用途/改作/私用/分发/商业逐项有结论（见 license-register 汇总）；无许可不明素材混入；开放项 OI-01（官方参考 URL，阻塞 Stage 1）、OI-02（Cubism 工具链，阻塞 Stage 3）已登记。
- 建立任务包 `tasks/packages/fuxuan-derivative-pkg1-design-license-v1.json`（Stage 0/1，验收 AC-MODEL-01/08）。
- 2026-09-18 用户决策：Cubism Editor 采用 **PRO 试用起步**（LR-05、REF-04、OI-02 已同步更新）；官方 PRO/FREE 对比页已登记（https://www.live2d.com/zh-CHS/cubism/comparison，访问日期 2026-09-18）。已发现并启动 `D:\live2d\Live2D Cubism 5.3\CubismEditor5.exe`，版本 **5.3.04**；Stage 3 工具版本阻塞解除，许可界面快照仍需在正式建模前留档。
