# L3 参数能力普查：机械基线证据

> **证据日期**：2026-09-15
>
> **范围**：当前映射副本一致性与本地验证入口可用性；不含视觉/语义/自然度认证。

## 已验证事实

- `Assets/Scripts/Live2DFramework/ParamMaps/fuxuan_map.json` 与 `Assets/Resources/Live2D/ParamMaps/fuxuan_map.json` 均包含 230 条 `entries`，格式为 2.0、schemaDate 为 2025-01-01。
- 两者 SHA-256 分别为 `2F80682CE6F798E4CC6D8D7122BBC165393D86D16C6BDDD489B0E7118F3F4E10` 与 `2FF81D1CEABCA2135F8F848F77041CFE525405ED22FABCE838134E13359EDD1A`，不一致。
- Resources 副本带 UTF-8 BOM；直接以未处理 BOM 的 Node `JSON.parse` 读取会失败。
- 现有 `Phase1Verifier`、`Live2DParameterVerifier` 和 `ParameterVisionScanner` 是 Unity Editor Window，需要人工选择含 `CubismModel` 的对象；当前没有已验证的批处理命令行入口。

## 结论

当前任何参数均不得因该映射或历史视觉分数被标记为 `Certified`。在确立单一映射源、自动镜像校验和隔离批处理写入/读回/复位证据前，云端视觉认证不得开始。

## 后续任务

扩展批处理入口以采集统一截图、局部差异和时序稳定性；仍不得写回映射或访问云端。

## 2026-09-15 首轮骨架机械结果

`UnityCubismProbeBatch.RunSkeletonMechanicalBatch` 在隔离数据根中成功执行，对 `ParamAngleX/Y/Z`、`ParamBodyAngleX/Y/Z`、`ParamBodyAngleX2/Y2/Z2`、`Param31/32/33/34/36/37` 各执行三轮最小/中间/最大写入与复位。15 项均为 `passed=true`，最大读回误差和复位误差均为 0。该结果仅证明 Cubism 参数层可写、可读、可复位，尚未证明可见性、人体语义、组合关系或自然度。

## 2026-09-16 独立窗口本地可见性结果

- 已构建并运行独立 `Live2DProbe.exe`：它只包含目标 Prefab 与 `ProbeWindowController`，不复用桌宠场景、行走、掉落、随机动作或透明桌面叠加层。完整批次在隔离 `.test_mode` 数据根完成，退出码 0，得到 15 个参数、每参 3 轮 `baseline/min/mid/max/reset`、共 225 张 PNG；全部 `resetStable=true`。
- 本地候选清单由 `node scripts/live2d-probe/summarize_local.cjs <隔离数据根>` 生成；阈值和“不写回映射”约束见 `docs/guides/approved/probe-evaluation-accuracy.md`。该清单不是语义认证。
- `visual-review-candidate`：`ParamAngleX/Y/Z`、`ParamBodyAngleX2/Y2/Z2`、`Param31`、`Param34`。其中 `ParamAngleX` 的独立构建复跑还经帧查看确认，最小/最大帧是左右转头，平均像素差分别为 2.319/2.361、复位差 0。
- `weak-visual-candidate`：`Param32`、`Param36`；`no-local-visible-evidence`：`Param33`、`Param37`。
- `ParamBodyAngleX/Y/Z` 在冻结非渲染参数写入者的探针条件下是零像素差。后续以 `FU_XUAN_PROBE_WRITER_MODE=physics` 复验：仅保留 `CubismPhysicsController`，每采样点稳定 8 帧并调用 `Stabilization()`；三轮均 `resetStable=true`、复位差 0，最小/最大平均像素差分别为 X=5.376/5.370、Y=13.831/3.297、Z=26.449/27.043。`ParamBodyAngleZ` 帧查看确认是相反方向的全身侧倾。三轴现可进入视觉语义复核候选，仍非 `Certified`。
- 所有参数继续保持 `semanticStatus=unassigned`、`mapWriteAllowed=false`；在取得模型帧外发授权并完成 DeepSeek 主判、GLM 交叉判及后续自然度门槛前，任何条目都不得标记为 `Certified`。

## 2026-09-16 云端视觉复核（部分完成）

- 在用户明确授权后，11 个强候选各上传 4 张隔离模型帧给 DeepSeek `deepseek-v4-flash`；全部 HTTP 200、结构化结果可解析、报告 `visible_change=true` 与 `reset_matches_baseline=true`。合计 19,111 tokens；证据和 UTF-8 汇总仅保存在隔离数据根的 `cloud_review/`。
- DeepSeek 的语义候选：`ParamAngleX/Z` 为头部左右转动，`ParamAngleY` 为小幅头部轴向偏转；`Param31/34` 为手臂/手部抬起姿态；传统 `ParamBodyAngleX/Y/Z` 与 `*2` 身体参数均报告躯干/上半身的侧倾或偏转。模型对 X/Y/Z 的命名并不完全一致，因此该结果只可作为“视觉语义候选”，不能自动改写参数轴向含义。
- GLM `glm-4.6v-flash` 曾成功复核 `ParamAngleX` 与 `ParamBodyAngleX2`，也报告可见变化和复位一致；其他候选连续返回 HTTP 429。显式退避后单项 `ParamAngleY` 仍为 429，因此停止重试。未取得 GLM 成功结果的条目仍缺少独立交叉判。
- 后续（同日）经用户授权，候选证据包 `screen_side_arm_raise` 的第二模型复核以同 Key `glm-4.5v` 完成（详见 [候选真相](l3-screen-side-arm-raise-candidate.md)）；其余普查条目的独立交叉判仍缺，不因该单例视为已完成。
- 结论：11 项可保留为 `Supporting` 的视觉/机械候选（非正式映射、非 `Certified`）；最终认证仍被“全部目标的第二模型成功复核 + 自然度门槛”阻断。

## 2026-09-16 DeepSeek 单模型补测

- 用户决定当前批次以 DeepSeek V4 单模型继续，不再等待 GLM 限流恢复；该决定只改变探索排序，不改变“禁止自动写回/禁止 `Certified`”约束。
- `Param32`：左手腕紫色绳结局部出现，语义 `unknown`，归为 `EffectOnly`。
- `Param33`：左手腕紫色绳结显示开关，归为 `EffectOnly`；这也证明全局像素差近零不能直接等同于不可见。
- `Param36`：左臂/左手由自然下垂到抬起前伸，归为手臂姿态探索候选。
- `Param37`：手臂/手部小幅位移，语义仍 `unknown`，保留为低优先级辅助候选，不进入骨架主控制层。

## 2026-09-16 全参数 DeepSeek 能力普查

- 独立探针以 `FU_XUAN_PROBE_SCOPE=all` 从运行时 `CubismModel.Parameters` 发现并采集 244 个参数；冻结模式下每参数三轮 `baseline/min/mid/max/reset`，共 3660 帧。进程正常退出，244 项均 `resetStable=true`。
- `batch_deepseek_review.cjs` 以 20 项可恢复批次完成 244/244 DeepSeek V4 视觉评价；单项证据缓存、批次进度和 UTF-8 汇总均位于隔离数据根。全量 DeepSeek usage 合计 423,328 tokens，结果无缺失。
- `build_parameter_catalog.cjs` 已生成只读 `parameter-capability-catalog.json`：87 个 `pose-candidate`、67 个 `effect-only`、64 个 `unclassified-visible`、26 个 `no-visible-evidence`。目录只用于后续 JSON 动作配置的查询与人工/后续验收排序；全部参数仍为 `not-certified`，且 `mapWriteAllowed=false`。
- 此次全参数普查是冻结写入者下的基础层；已单独完成传统 `ParamBodyAngleX/Y/Z` 的物理模式复验。其他依赖物理、组合参数或时序的条目不因本轮静态采样被判为不可用。
## 2026-09-16 保守动态连续性复验

- 以独立 `Live2DProbe.exe` 的单参数扫动模式，在隔离 `.test_mode` 数据根中复验了两个后续组合候选。`ParamAngleX` 使用 `[-15, 15]`、12 步、从基线扫向 `15` 再回基线：峰值平均像素差 `1.721964`、复位差 `0`；帧序列分析的相邻 RGB 均值/峰值为 `0.3472/0.3520`，相位峰值 `1.7245`。`Param94` 使用同一保守区间与步数：峰值平均像素差 `2.085081`、复位差 `0`；相邻 RGB 均值/峰值为 `0.7585/0.9114`，相位峰值 `2.0882`。
- 两次探针均正常退出且输出 25 张时序帧。结果只证明离线参数变化在该采样密度下连续、可见并可复位；它不证明运行时插值、组合自然度、人体语义或 `Certified` 状态，所有参数继续保持 `mapWriteAllowed=false`。

## 2026-09-16 保守动态组合复验

- 独立探针新增通用同步组合扫动入口，并以 `ParamAngleX + Param94` 在 `0.25` 保守范围、12 步、三轮中执行同步 `baseline → targets → baseline`。实际 targets 为 `7.5/15`，共保存 75 帧；组合峰值平均像素差 `3.238710`、最大复位差 `0`。全序列相邻 RGB 均值/峰值 `0.9336/1.1142`，相位峰值 `3.2435`。
- 首轮基线、峰值、复位帧直接审查未见明显穿模、层级反转或残留：画面右侧单臂外展/抬起可与轻微头部偏转并存，复位回到基线。该人工帧审查仅支持保留为 `Supporting` 组合候选；它不替代完整时序自然度评审、运行时资源仲裁或 `Certified` 状态。
- 同一同步扫动在 `physics` 写入模式下复验 `ParamBodyAngleX + Param94`：`0.1` 缩放的实际 targets 为 `1/6`，三轮 75 帧，组合峰值平均像素差 `2.934924`、最大复位差 `0`；相邻 RGB 均值/峰值 `0.4680/0.5513`，相位峰值 `2.9388`。基线/峰值/复位帧审查未见躯干物理跟随覆盖手臂、明显穿模或残留；躯干可见变化较轻微，故该结果只能作为受物理前置条件约束的 `Conditional/Supporting` 组合证据。
