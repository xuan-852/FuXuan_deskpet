# L3 官方样例动作参考来源审计

> **证据日期**：2026-09-18
>
> **范围**：仅审计本地已存在的 Cubism Web Samples 中 Haru/Hiyori 动作，未下载新素材，未改变素材或其分发状态。

## 来源与许可证据

| 字段 | 事实 |
|---|---|
| 来源 | Live2D 官方 `CubismWebSamples`，本地 `_src/CubismWebSamples-develop` 副本 |
| 确定 URL | <https://github.com/Live2D/CubismWebSamples> |
| 许可文件 | `_src/CubismWebSamples-develop/LICENSE.md` |
| 许可主张 | 该文件将 `Samples/Resources/Haru`、`Samples/Resources/Hiyori` 列于 Free Material License 项下，并链接 Free Material License Agreement |
| 许可 URL | <https://www.live2d.com/eula/live2d-free-material-license-agreement_en.html> |
| 样例动作 | `Samples/Resources/Haru/motions/*.motion3.json`、`Samples/Resources/Hiyori/motions/*.motion3.json` |
| 核对日期 | 2026-09-18 |

## 结论与边界

这些本地已留存官方样例可作为首个**离线参考**的来源证据，用于提取时序和语义特征并生成隔离候选；不构成将原始动作、样例模型或派生曲线随产品再分发的授权结论。

任何生产部署仍须逐项复核 Free Material License 的适用条件、曲线派生和产品分发方式；未完成复核前，原始动作和派生曲线继续不入版本库、不放入安装包。
