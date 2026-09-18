# Stage 1 模型设计验收报告

> **版本**：0.1.0-stage1-review-pending
> **日期**：2026-09-18
> **状态**：未通过；`BlockedByEvidence` 与人工视觉复核待完成。
> **范围**：仅评估 Stage 1 的原创设计规格和布局证据。本文不证明分层绘稿、Cubism 工程、导出物、Unity 导入、Probe 参数或认证动作已经存在。

## 1. 依据与边界

- 归档计划：[符玄私有 Fixture 归档案例](../../../docs/archive/fuxuan-private-fixture-case-study.md)
- 任务包：`tasks/packages/fuxuan-derivative-pkg1-design-license-v1.json`
- 来源登记：[source-register.md](../sources/source-register.md)
- 许可登记：[license-register.md](../sources/license-register.md)
- 设计规格：[model-spec.yaml](../design/model-spec.yaml)
- 布局执行表：[layer-layout.md](../design/layer-layout.md)
- 绘稿输入约束：[artwork-source/README.md](../artwork-source/README.md)

本阶段没有导入外部角色图像、现有模型纹理、网格、Cubism 工程、`.moc3`、参数曲线或物理配置。验收图均为原创的抽象布局图，不是最终角色绘稿。

## 2. 设计证据清单

| 证据 | 用途 | 当前结论 |
|---|---|---|
| `../artwork-source/fuxuan-neutral-layout-v01.svg` | 4096 x 4096 中性正面构图、部件边界、中心线、运动余量 | 已生成；仅布局线框 |
| `thumbnail-check-v01.svg` | 缩略图下的主质量、发饰、袖部和裙摆比例检查 | 已生成；待人工判定 |
| `silhouette-check-v01.svg` | 灰度轮廓下的头部、袖部、裙摆和下身分离检查 | 已生成；待人工判定 |
| `../design/layer-layout.md` | 图层命名、绘制顺序、遮挡和补画约束 | 已生成；待 Stage 2 绘稿核对 |
| `../design/parameter-dictionary.md` | 未来模型的参数语义候选 | 已生成；不是运行时参数事实 |
| `../design/physics-plan.md` | 未来低幅物理意图 | 已生成；不是物理配置 |

## 3. 验收矩阵

| 验收 ID | 要求 | 本阶段可复核输入 | 状态 | 未满足项 |
|---|---|---|---|---|
| AC-MODEL-01 | 不依赖文字标签即可识别角色，服饰结构完整 | 布局线框、缩略图检查稿、灰度轮廓检查稿、部件和遮挡规格、受限官方视觉参考 | 待人工复核 | 尚无正式分层绘稿；人工识别度、服装完整性、配色和整体气质尚未确认；角色衍生创作权利结论仍被 LR-01 阻断 |
| AC-MODEL-08 | 每项外部参考有来源、许可结论和允许用途 | 来源/许可登记、隔离工作区、禁止复制规则 | `BlockedByEvidence` | 官方视觉参考 URL 已登记为人工核对用途，但尚未取得可公开验证、包含有效操作性条款的二创/改作规则原文或权利人书面许可 |

## 4. 人工视觉复核项目

以下项目必须以 `fuxuan-neutral-layout-v01.svg`、`thumbnail-check-v01.svg`、`silhouette-check-v01.svg` 及后续原创分层绘稿为输入进行人工复核：

1. 256px 缩略图是否能脱离文字标签识别角色。
2. 正面中性姿势是否符合预期，并保留头部、呼吸、眨眼、张嘴、衣摆运动所需的补画空间。
3. 灰度轮廓是否能稳定区分头部发饰、长发、袖部、裙摆与下身。
4. 服装是否完整覆盖领口、上衣、袖口、腰部、前后裙摆、腿部、鞋和关键挂件。
5. 配色、发饰几何和整体气质是否具备足够识别度。该项在 `REF-03` 关闭前不能判为通过。

## 5. 阻断与恢复

- **权利与官方视觉依据**：`REF-03` 已登记为受限的人工视觉核对来源；`LR-01` 仍为 `BlockedByEvidence`。尚未获得包含有效二创/改作授权条款的公开规则原文或权利人书面许可。不得将外部视觉素材放入 `artwork-source/`，也不得声称“已验证高还原”或已获角色衍生创作许可。
- **人工视觉验收**：未获得人工复核前，AC-MODEL-01 保持待验收；不得进入 Cubism 建模或将布局图当作最终绘稿。
- **绘稿前置**：Stage 2 必须独立建立任务包，并提供具有可追溯来源的可编辑分层源文件、透明预览、图层清单和遮挡检查记录。

## 6. 下一阶段边界

Stage 1 仅允许继续补充来源登记、设计规格和验收记录。只有在 AC-MODEL-01 与 AC-MODEL-08 的阻断条件关闭后，才允许下发 Stage 2 分层绘稿任务。Cubism 源工程、导出、Unity 候选资源、正式映射和 LLM 能力暴露均不属于本阶段。
