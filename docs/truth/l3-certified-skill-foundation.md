# L3 认证技能基础骨架

> **证据日期**：2026-09-16  
> **范围**：四层认证准入与内存资源协调基础；不执行动作、不写参数、不注册生产技能。

`Assets/Scripts/Embodied/CertifiedSkillFoundation.cs` 已提供：

- `SkillCertificationRecord`：机械、视觉、语义、自然度、自然度分数、模型/映射版本和资源声明；
- `CertifiedSkillRegistry`：只有四层均通过、自然度不少于 75、且版本证据齐全的条目才允许注册；
- `EmbodiedActionRequest`：只含技能 ID、语义目标、优先级、超时和资源，**没有**原始参数或关键帧字段；
- `EmbodiedCoordinator`：仅接受注册表中存在且资源声明一致的技能；冲突资源拒绝，完成时释放占用。

隔离 EditMode 测试通过（failed=0）：未经四层认证的候选无法注册；测试用的完整认证记录可占用右臂资源，第二个同资源请求被拒绝，完成后释放。测试记录仅存在内存，不构成生产 `Certified` 技能。

当前生产注册表尚未挂接任何技能，故运行时仍没有可调用的身体动作。下一阶段是将候选证据、虚拟骨架和自然度量表接入此骨架，而不是直接接入旧 MotionPlanner 或参数映射。

## 自然度硬门槛（2026-09-16）

`NaturalnessGate` 已将首版默认阈值落实为本地门禁：必须同时有可定位的证据、时序连续、复位稳定、无资源冲突、不低于硬编码步行基线，且评审分数不少于 75。任一条件缺失均拒绝。隔离 EditMode 测试通过（failed=0），其中“100 分但低于步行基线”仍被拒绝。该组件不生成评分、不调用云端，评分和证据仍须由后续固定时序评审产生。

候选阶段的证据引用与认证结论分离：`CandidateEvidenceReference` 以 `Supporting`、`PendingReview`、`Passed`、`Rejected` 等显式状态表达材料进度。进入复核队列不代表通过四层认证；只有四层均已 `Passed` 的候选才可被转换为待注册的认证记录，随后仍由 `CertifiedSkillRegistry` 重复校验四层、自然度分数和版本证据。

2026-09-16 对首个候选的 DeepSeek 离线复核得到语义支持与自然度 85/100；同日 `glm-4.6v-flash` 因限流改用授权替代 `glm-4.5v` 完成独立交叉复核（92/100，与 DeepSeek 全字段一致、无分歧），四层证据据此提升为 `Passed`。

`ScreenSideArmRaiseCertification.CreateRecord()` 已生成首个生产认证记录（自然度保守取 85，含 moc3 与映射源版本证据），隔离 EditMode `FirstSkillCertificationTests` 验证注册成功且协调器按 `RightArm` 资源仲裁。

**2026-09-17 第二个认证技能**：外部动作候选 `external_Hiyori_Hiyori_m02`（头部摇摆+眨眼待机，5.93s）四层通过（双模型 85/92 一致 supported，包 `674bdaec…`），记录 `HeadSwayBlinkIdleCertification` 注册进准入汇点，资源 `Face|Body`。`EmbodiedRuntimeAdmission` 同步改为集合式活动请求（不相交资源可并行准入，C-L3-02）；该技能尚无生产执行器，回放取证在隔离探针完成，LLM 工具面仍为零。

`CandidateReviewAssessment` 定义离线评审结果的最小可追溯边界：必须匹配候选技能、绑定 64 位复核包 SHA-256、声明评审者与模型、独立给出语义和自然度结论，并通过 `NaturalnessGate`。它最多只能“贡献认证证据”，不能改写候选状态、注册技能或发起动作。隔离 EditMode 测试通过（failed=0）：缺包指纹、100 分但低于步行基线均被拒绝；满足所有离线评审条件也仍不等同于候选已认证。
