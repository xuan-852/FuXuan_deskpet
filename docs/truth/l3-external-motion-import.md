# L3 外部动作素材导入（官方示例 motions）

> **证据日期**：2026-09-17
> **任务包**：`l3-external-motion-import-v1`
> **范围**：把 Live2D 官方示例动作导入为**未认证**的外部 `LegacyCandidate` 定义；不含认证结论、不写映射、不入运行时。

## 来源与许可（DG-31 可追溯）

- 素材仓库：https://github.com/Live2D/CubismWebSamples （develop 分支，8 个示例模型共 78 个 `.motion3.json`：Haru 27、Wanko 12、Hiyori 10、Natori 8、Mao 8、Mark 6、Rice 4、Ren 3）。
- 许可：[Live2D Free Material License Agreement](https://www.live2d.com/eula/live2d-free-material-license-agreement_en.html)（v1.6，2025-02-03；本条目验证日期 **2026-09-17**）。关键结论：原创角色素材可用于发布的作品（小规模/个人商用与非商用均可）；**禁止将素材数据本身再分发**；衍生作品须附版权声明。
- 合规决定：原始 motion 文件、下载压缩包与**派生曲线定义全部留在本地**（`assets/external_motions/` 已加入 `.gitignore`）；仓库只提交导入工具与聚合统计。未来随产品分发时按许可以「作品内嵌」方式携带并附声明。

## 导入器与导入结果

`scripts/live2d-probe/import_motion3.cjs <motions-root> <capability-catalog.json> <output-dir>`：解析 motion3.json 的参数曲线 → 与隔离能力目录（244 参数全普查）做存在性匹配 → 幅度钳位到各参数探明范围 → 每动作输出候选定义（`l3-external-motion-candidate/v1`，`certification.status=uncertified`）+ 聚合报告。

- 78/78 解析成功；**0 个全覆盖**、2376 条未匹配曲线——未匹配参数（236 个唯一 ID）全部为示例模型私有参数（`ParamHairFront/Back/Side`、`ParamArmLA/RA/LB/RB`、`ParamFaceForm`、`ParamBustY` 等），符玄模型的对应部位使用不同参数 ID（发丝/裙摆由 physics3.json 驱动，手臂为 `Param31–37/94` 等）。
- 匹配到的曲线集中在**标准参数**：`ParamAngleX/Y/Z`、`ParamEyeBallX/Y`、`ParamEyeLOpen/ROpen`、`ParamBrowLY/RY`、`ParamBreath`、`ParamMouthOpenY`、`ParamEyeLSmile` 等——这些全部在符玄探明可用参数集中。
- 钳位点 720 个（示例模型幅度超出符玄探明范围的采样点已截断）。

## 认证优先序

以「双模型一致确认可见参数（36 项）的曲线数」排序：头部/面部动作优先（每动作约 7 条双确认曲线），首选 15 个动作清单在 `assets/external_motions/first_batch_shortlist.json`（本地）。示例：`haru_g_idle`（10s）、`Hiyori_m05`（8.6s）、`Natori mtn_00`（8s）。

## 边界与后续

- 全部候选 `uncertified`、`mapWriteAllowed=false`；未匹配的私有参数曲线被丢弃，动作在符玄上会缺发丝/手臂形态细节（物理与后续参数重映射补足）。
- 后续认证：机械层可用探针批处理自动跑（写/读/复位）；视觉层按候选包流程采帧 + 双模型评审；自然度门槛不变。
- 导入动作的时序曲线来自官方素材，作为候选技能的时序参考；四层认证全部在符玄模型上重新取证。
