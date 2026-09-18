# 来源登记（source-register）

> **工作区**：`model-workspaces/fuxuan-derivative-v1/`
>
> **依据**：[符玄私有 Fixture 归档案例](../../../docs/archive/fuxuan-private-fixture-case-study.md) §3、§Stage 0
>
> **规则**：每个参考文件、链接、截图或外部模型必须有来源记录和用途边界；参考绑定版本（哈希），防止参考漂移。
>
> **登记日期**：2026-09-18

## 参考来源

| # | 来源 | 类型 | 绑定版本（SHA-256） | 允许用途 | 禁止用途 | 许可条目 | 状态 |
|---|---|---|---|---|---|---|---|
| REF-01 | 现有符玄模型包 `code/desktop_unity/Assets/Live2D/Models/Fuxuan/`（第三方免费 VTS 模型，仅导出物，无源工程） | 布局参考 + 行为观察 | 见下表 | 画布比例、部件占位框、相对尺寸、分层思路、锚点、遮挡关系、绘制顺序、纹理密度、参数命名组织与工作流经验的**抽象转写** | 原始像素、纹理、立绘局部、笔触、逐层抠图、网格、`.moc3`、工程、参数曲线、物理配置、动作关键帧、材质配置的复制或转换 | LR-03 | 已登记 |
| REF-02 | 现有模型包内 `使用说明（用前请看）.txt` | 许可主张文本 | `b92ce687dc59c6a415234b3f8ce3bd566c1b046edca6f9c27726e16cf011d95d` | 作为 LR-03 许可主张的原始文本依据 | 作为授权证明使用（其本身不构成对改作/分发的授权） | LR-03 | 已登记 |
| REF-03 | HoYoverse 公开内容记录 [Improv Tour Trailer: "Beyond Weal and Woe" \| Honkai: Star Rail](https://sg-public-api-static.hoyoverse.com/content_v2_user/app/113fe6d3b4514cdd/getContent?iInfoId=162612&sLangKey=en-us)，关联官方新闻页 `https://hsr.hoyoverse.com/en-us/news/162612` | 官方视觉参考 | 线上公开记录；访问日期 2026-09-18 | 仅用于人工核对角色识别点、服饰结构、配色与整体气质（Stage 1）；不得下载、嵌入或处理其媒体文件 | 直接抠图、临摹可识别像素、复制官方图像/截图/媒体、将其作为绘稿或纹理输入 | LR-01 | 已登记（视觉参考限定）：公开 API 记录标注 Fu Xuan 并提供 HoYoverse 托管媒体；该记录不提供二创、改作、发布或素材复用授权 |
| REF-04 | Live2D 官方文档、Cubism Editor/SDK 说明、官方示例模型（Hiyori/Haru 等）；已登记 [PRO/FREE 功能比较页](https://www.live2d.com/zh-CHS/cubism/comparison)（访问日期 2026-09-18） | 技术参考 | — | 参数组织方式、物理工作流、导出规范（示例模型同时是 L3 参考曲线既有来源，见 `docs/guides/approved/external-motion-reference-retargeting.md`） | 将示例模型像素/工程混入新模型源文件 | LR-05 | 部分登记（对比页已登记；Editor 具体版本待安装后登记） |
| REF-05 | 项目所有者口头确认（2026-09）：允许借用现有模型像素布局提高 AI 创作速度 | 决策记录 | — | 载体为计划 §3.2 的抽象参考范围定义 | 不得外推为对像素/工程复制的授权 | LR-03 | 已登记（以计划文本为记录） |

## REF-01 版本绑定哈希

登记日期 2026-09-18，路径 `code/desktop_unity/Assets/Live2D/Models/Fuxuan/`：

| 文件 | SHA-256 |
|---|---|
| `符玄.moc3` | `b4ddf3fbd6cd7f3e6eab7e82032548cd2feb3efe296bf3d63e20b97bfcab0ed4` |
| `符玄.model3.json` | `90adfa2360ea6f597da29c1e3290358e2cb8b140ea50956023b910b2ff0214e6` |
| `符玄.physics3.json` | `7405b711fecedfcc297cc510a52dd10624859968e7e3ccc283c82e3f6c5f8c58` |
| `符玄.cdi3.json` | `32d4100a2e8b6119ac2c36f33fd0627a6bb205990d624fa8f12afe110c5820e7` |
| `使用说明（用前请看）.txt` | `b92ce687dc59c6a415234b3f8ce3bd566c1b046edca6f9c27726e16cf011d95d` |

## 开放项

1. REF-03 权利部分：官方视觉参考已登记，但尚无可公开验证且包含有效操作性条款的二创/改作规则原文。依据项目所有者 2026-09-18 的限定决策，允许在本隔离工作区继续私有研发原创重绘；这不构成官方授权。公开发布、商业化、再分发、随安装包交付或提供源工程前，LR-01 必须重新取得可复核规则原文或权利人书面许可。
2. REF-04：~~登记 Live2D 工具链版本、授权层级与条款 URL~~ → 2026-09-18 用户决策 PRO 试用起步，授权层级已定；已安装 `D:\live2d\Live2D Cubism 5.3\CubismEditor5.exe`，版本 **5.3.04**。具体许可快照仍随账号/安装界面保存，进入 Stage 3 前复核。
