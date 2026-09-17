# L3 LLM 接入具身控制（架构试验）

> **证据日期**：2026-09-17
> **范围**：LLM 经认证技能白名单接入身体控制的 MVP 架构；主动/自主身体行为开放仍待决策。

## 数据积累（2026-09-17 批量认证）

经探针回放取证 + 主代理帧审查拟定语义声明 + 双模型独立评审，新增 4 个认证动作（全部 `supported`、连续、复位稳定、无故障、不低于步行基线、high 置信）：

| 技能 | 时长 | DeepSeek | GLM | 包 SHA-256（前 8） |
|---|---|---|---|---|
| `external_Hiyori_Hiyori_m05`（转头张嘴） | 8.6s | 82 | 92 | `266f87ff` |
| `external_Hiyori_Hiyori_m06`（侧倾眨眼） | 5.37s | 82 | 92 | `1c5b4f6e` |
| `external_Haru_haru_g_idle`（轻微待机） | 10s | 85 | 92 | `a21ee43b` |
| `external_Haru_haru_g_m10`（闭眼微笑） | 5.5s | 85 | 92 | `546596eb` |
| `external_Haru_haru_g_m20`（右侧微笑） | 6s | 88 | 92 | `292f8c8b` |

加上此前 `external_Hiyori_Hiyori_m02`（85/92）与 `screen_side_arm_raise`（85/92 单臂上抬），**认证技能库共 7 项**（其中 `CertifiedMotionLibrary` 为 6 项外部动作），登记包指纹齐全。`external_Hiyori_Hiyori_m06` 的专项证据见 [Hiyori m06 认证](l3-hiyori-m06-certification.md)。

## 新架构实现

- **生产执行器** `Live2DRenderer.PlayCertifiedMotion(skillId)`：静态门禁（静止/无动作/非 AI 锁）→ `GeneratedMotion` 输入租约 → `EmbodiedRuntimeAdmission.TryBeginSkill` 准入 → 多参数曲线实时播放（`EmbodiedMotionCurve`，`EmbodiedPoseState` 登记还原基线）→ 完成时姿势还原、租约与准入释放、移动锁解除；取消/禁用/退出经统一收束。曲线数据从数据根 `certified_motions/<skillId>.json` 加载（不入版本库，许可约束）。
- **LLM 工具** `request_body_skill`（ToolEngine）：描述自动枚举认证技能与语义边界；只接受白名单内 skill_id，未认证请求即终态拒绝；无原始参数入口。它仅出现在 `LocalToolRouter` 的 `body` 意图闭集（另一个成员是 `stop_action`），不属于普通 `operation` 或跨意图安全白名单。
- **测试链路** `@@sim:certified-motion:<skillId>`（仅 `.test_mode`）供确定性隔离驱动。

## 可复核证据（2026-09-17）

- `build.ps1 -RunTests`：EditMode **215 用例、214 passed、failed=0**、1 ignored；`CertifiedMotionLibraryTests` 验证条目、全部注册准入、body 专属白名单与语义提示词，`LocalToolRouterTests` 验证 body 闭集拒绝普通操作和危险工具。
- 隔离真机驱动 `scripts/test/certified_motion_runtime_drive.cjs`：日志链完整——`admitted: external_Hiyori_Hiyori_m02` → `[CertifiedMotion] started (5.93s, 23 params)` → **23 参数姿势还原** → `released: certified-motion-completed` → cleanup；7 帧截图留证。

## 边界与待决

- LLM 只能选择认证技能、按语义边界描述请求；`body` 仅由用户明确要求桌宠本人做动作时使用，规划失败退化为文字；拒绝是终态；结果以开始/拒绝状态返回，完成事实由运行时收束日志承载（C-L3-06 不回传参数轨迹）。
- 指导文档默认 LLM 单次动作提案 ≤4 秒；本批动作 5.5–10 秒，当前仅限**用户明确请求的执行**与隔离试验。**主动行为与 >4s 动作的默认阈值需人工决策后调整**。
- 主动/自主身体行为（IdleChat 联动、情绪驱动动作）未开放，属后续任务包。
