# Stage 2 分层彩色绘稿验收报告

> **版本**：0.1.0-stage2-artwork-review-pending
> **日期**：2026-09-18
> **状态**：自动结构检查待完成；人工角色识别复核待项目所有者完成。
> **状态标签**：`FixtureOnly / CaseStudy / ReviewFailed / BlockedByEvidence`
> **范围**：仅评估原创 SVG 源、透明 PNG 预览、分层记录和绘稿边界。不证明 Cubism 工程、导出物、Unity 导入、Probe 参数或认证动作存在。

## 1. 输入与边界

- 源文件：`../artwork-source/fuxuan_derivative_v01_neutral.svg`
- 预览：`../artwork-source/fuxuan_derivative_v01_neutral.png`
- 图层清单：`../artwork-source/layer-manifest-v01.md`
- 来源登记：`../sources/source-register.md`
- 许可登记：`../sources/license-register.md`
- 任务包：`tasks/packages/fuxuan-derivative-pkg2-layered-art-v1.json`

本轮绘稿为项目内直接绘制的原创 SVG 路径。未导入或复制旧 `.moc3`、纹理、网格、曲线、物理、动作、官方媒体、外部纹理、字体或未知来源 AI 图像。公开发布、商业化、再分发和安装包交付仍被阻断。

## 2. 自动检查矩阵

| 检查 | 结果 | 证据/备注 |
|---|---|---|
| SVG/XML 可解析 | 通过 | XML 解析通过；根画布 `4096 x 4096`，含 42 个命名组 |
| PNG 存在且为 4096 x 4096 RGBA | 通过 | Sharp 元数据：4096 x 4096、4 通道、`hasAlpha=true` |
| 透明背景 | 通过 | SVG 无背景矩形；PNG alpha 最小值为 0、最大值为 255 |
| 图层可定位 | 通过 | 42 个必需命名组均存在；见 `layer-manifest-v01.md` |
| 外部资源引用为零 | 通过 | SVG 无 `<image>`、无 `href`/外链/`data:` 资源；仅声明 SVG/XML 命名空间 URL |
| 路径边界 | 通过 | 交付文件均位于 Stage 2 任务包许可范围 |
| 哈希记录 | 通过 | `artwork-source/SHA256SUMS` 与 v01 源、预览和文档一致 |
| 文档索引生成 | 未通过（非本任务阻断） | `node scripts/docs/generate_document_map.cjs` 以 1 退出；已知无关指南缺少“实施授权边界”，未修改该范围外文件 |

## 3. 人工视觉复核

项目所有者需以真实 PNG 预览检查以下项目：

1. 256px 缩略图下，脱离文字标签仍能识别整体角色设计与头身比例。
2. 正面中性姿势完整，脸、前后发、发饰、领口、上衣、袖、手、前后裙、腿、鞋和挂件均存在。
3. 浅色、深色和透明背景下无白边、黑边、透明污染或颜色泄漏。
4. 灰度轮廓能分开头部、发饰、长发、袖部、裙摆和下身。
5. 运动边界存在可用补画；隐藏前层后，相邻底层不出现明显空洞。
6. 配色、服饰层次与整体气质符合本轮高保真二创目标，同时不依赖旧模型或官方媒体像素。

## 4. 当前结论

自动结构检查已通过，但视觉验收**未通过**。单次渲染视觉门禁判定当前 PNG 未能呈现可接受的完整高保真 Live2D 角色设计，且预览背景呈现异常为白色，无法作为透明角色稿的用户可见验收证据。该结论优先于自动检查结果。

因此 Stage 2 保持 `ReviewFailed`：不得创建 Stage 3 Cubism 任务包，不得创建 `.cmo3`、参数、物理或导出物，也不得修改生产模型、正式映射、运行时脚本或 LLM 技能接口。需要先重新完成可见的高保真分层彩色角色稿并重新渲染，随后再进行独立视觉复核。
