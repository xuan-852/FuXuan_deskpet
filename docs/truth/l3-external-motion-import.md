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

## 首个候选流水线认证结果（2026-09-17）

`external_Hiyori_Hiyori_m02`（官方 Hiyori_m02 重定向到符玄模型，5.93 秒头部摇摆+眨眼待机动作）走完全部四层：

- **取证**：探针新增 `FU_XUAN_PROBE_MOTION_PLAYBACK` 回放模式（`EmbodiedMotionCurve` 求值器，线性/贝塞尔/阶跃，编码对官方 4812 条曲线全量验证）。隔离探针 16 步采 18 帧：峰值像素差 9.979、相邻差 9.929、**复位差 0.000、resetStable=true**。机械层依据全参数普查（23 条曲线参数全部通过写/读/复位取证）。
- **语义声明流程教训**：首轮评审曾把该动作套用单臂上抬声明，双模型一致判 `unsupported`（评审器本身工作正常，声明错误）。此后语义边界声明改为**候选包的必备字段**（`constraints.semanticBoundary`），由主代理基于帧审查拟定、双模型独立验证。
- **复核**：修正声明后重建候选包（SHA-256 `674bdaec06ac91bd63638f44970ead7707a837fd8debacfbea990fd186832604`），双模型一致 `supported`：**DeepSeek 85/100、GLM glm-4.5v 92/100**，均 high 置信、连续、复位稳定、无故障、不低于步行基线。
- **结论**：四层全部 `Passed`，主代理裁决成立。认证记录 `HeadSwayBlinkIdleCertification` 已注册进生产准入汇点（资源 `Face|Body`，时长 5.93s，自然度保守取 85）。**注册为数据层事实：该技能尚无生产执行器（回放取证在隔离探针完成），LLM 工具面仍为零。**

## 后续候选

其余 77 个导入候选按双确认曲线覆盖排序推进；`scripts/live2d-probe/capture_motion_candidate.cjs <probe-exe> <candidate.json> <skill-id> <semantic-claim>` 已把取证→候选包→双模型评审固化为单命令流水线。

## 边界与约束（持续有效）

- 全部候选（含已认证者）`mapWriteAllowed=false`；未匹配的私有参数曲线被丢弃，动作在符玄上会缺发丝/手臂形态细节（物理与后续参数重映射补足）。
- 认证在符玄模型上重新取证，官方素材只提供时序曲线与动作设计；任何候选未经四层认证不得进入运行时。
- 原始素材与派生曲线数据不入版本库（许可禁止再分发）；产品分发按「作品内嵌」并附版权声明。
