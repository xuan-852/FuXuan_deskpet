# L3 `acknowledge_nod` 认证具身技能

> **证据日期**：2026-09-25
> **技能 ID**：`acknowledge_nod`
> **范围**：项目自有固定点头候选的曲线、认证准入、输入租约、行为账本、生命状态和隔离 Player 生命周期验证。

## 已验证事实

- 语义边界固定为“短时两次轻微向下点头后回到基线”；不表示摇头、歪头、鞠躬、挥手或任意参数动作。
- 候选资源为 `Assets/Resources/Live2D/CertifiedMotions/acknowledge_nod.json`，时长 2 秒，只写 `ParamAngleY`，曲线关键点为 `0 → 8 → 0 → 6 → 0`，分别位于约 `0.0s / 0.2s / 0.5s / 0.8s / 1.2s / 2.0s`。候选文本使用 SHA-256 `0300738a92a781be54e896aa44c65d961b9870218fae8aa47dde042df8c00395` 绑定。
- `CertifiedMotionLibrary` 将该技能声明为 `Body` 资源并接入通用 `EmbodiedRuntimeAdmission`。候选读取遵循“数据根优先、内置 `Resources` 回退”；两条路径都必须通过完整文本哈希、JSON 结构、候选 ID 和模型参数校验，篡改的数据根候选被拒绝。
- `Live2DRenderer.PlayCertifiedMotion("acknowledge_nod", ..., registerBehaviorIntent: true)` 是唯一测试与生产共用的认证执行入口。执行顺序包含 `certified-motion` 输入租约、认证准入、一个 `BehaviorIntent`、逐帧曲线提交、姿态恢复、租约/准入释放和终态写回。
- `RuntimeInputSimulator` 的 `@@sim:skill:acknowledge`（兼容 `skill:nod`）仅在 `.test_mode` 下调用同一认证执行器，不写原始参数，不调用 OS 鼠标 API。
- 确定性路由识别“点点头”“点头”“同意”“nod”“acknowledge”等明确身体请求，但由于当前 `LlmExposed=false`，自然语言路径会发布拒绝回执，不会启动隐藏的 `acknowledge_nod`。

## 可复核证据

- `build.ps1 -Quick` 通过。
- `build.ps1 -RunTests -CleanBeeCache -NoThrottle` 通过；新鲜 `test_results.xml` 的根测试结果报告 `failed=0`，覆盖候选资源、曲线语义、完整性、数据根覆盖优先级和隐藏 AI 路由拒绝。
- 新鲜隔离 Player 构建使用 `build.ps1 -CleanBeeCache -NoThrottle -OutputDir Build/acknowledge_nod_fresh_20260925`，构建脚本确认输出目录中的 Player 产物由本次调用刷新。
- `scripts/test/acknowledge_nod_runtime_drive.cjs` 使用临时 `FU_XUAN_DATA` 和 `.test_mode` 完成了：正常启动与完成、姿态/资源收束、稳定 Intent/Execution/Correlation/Request 身份、确定性自然语言隐藏路由、动作中取消、测试超时终态 `Expired`，以及走路输入租约冲突拒绝。驱动退出码为 0。
- 正常路径观察到 `BehaviorState=Executing` 与 `LifeState=Active`，随后同一执行关联进入 `BehaviorState=Completed` 与 `LifeState=Completed`；取消路径写回 `Cancelled`，超时路径写回 `Expired`，没有把失败或中断伪装成完成。

## 暴露与视觉边界

- `LlmExposed` 当前保持 `false`。曲线结构、候选生命周期和隔离 Player 执行已经验证，但没有完成真人或双模型视觉自然度复核，因此不能把内部 `80/80` 评分字段当作外部自然度认证，也不能把技能加入生产 LLM 提示或可调用白名单。
- 2026-09-26 复测：使用 `Build/airborne_gate_20260926_final/DesktopPet.exe` 和隔离 `FU_XUAN_DATA` 运行 `scripts/test/acknowledge_nod_runtime_drive.cjs`，正常完成、取消、测试超时及行走冲突断言均通过。另采集 11 张原始模型画面帧，保存在本机忽略目录 `logs/nod_visual_captures/frames_1790419871939/`。用户观看该帧序列后明确澄清：**能够辨认出两次点头**；先前选择的“第二次点头不明显”并非准确描述，不作为验收结论。用户仅能确认两次动作可辨，无法据此判断节奏自然度或画面侧复位；真实窗口现场验收也尚未完成，不能据此开放 `LlmExposed`。工具会话启动的可见窗口未出现在用户实际观看的桌面；原始画面证据未入库。
- 为真实窗口人工观察新增 `scripts/test/observe_acknowledge_nod.cjs`：须由观察者自己的交互终端启动，使用独立临时 `FU_XUAN_DATA` 与 `.test_mode`，将模型移到主屏下方中部，等待观察者确认后重复播放三轮点头，并在测试退出时仅关闭本脚本启动的 Player。`node --check` 和同一脚本的 `--auto` 隔离验证已通过；自动验证记录模型停在主屏 `x=1235/2560` 附近、动作正常完成。交互终端的真人节奏/画面复位结论仍待记录。
- Player 验收使用测试 inbox、临时数据根和模拟状态命令；它证明认证执行与状态闭环，不等于真实 OS/DWM 鼠标、真实桌面输入或真实显卡故障验收。
- 本技能不改旧 `play_action("nod")` 语义，不开放原始参数，也不把旧 `MotionPlanner` 模板的存在当作认证证据。

## 2026-09-27 曲线 v2（真实窗口人工评审否决 v1 方向）

- **v1 真实窗口人工评审结论**（观察者本人交互终端运行 `scripts/test/observe_acknowledge_nod.cjs`，隔离数据根，三轮播放）：能认出两次动作=**不确定**；节奏=**自然**；复位=**正常**；定性结论=**「抬头是能看见的，但正常的点头不是这样子」**。原语记录在临时目录 `human-review.json`（`fuxuan_manual_nod_47912_*`）。
- 该定性结论即门槛 1 预设的「具体质量问题」，触发改曲线分支：v1 曲线 `0 → 8 → 0 → 6 → 0` 为**正角度**（Cubism `ParamAngleY` 正值=抬头），实机读作抬头而非点头，与本条目「向下点头」语义边界相反；离线双模型评审（80/80）未拦截该方向错误，佐证真人实机评审不可由离线评分替代。
- **v2 曲线**（点头动作学描述 → 参数转换）：点头=头部垂直屈伸（下巴向下收再回位）；回应确认类=小幅、中快节奏、幅度递减。转换为 `ParamAngleY` **负角度**低头两次：`(0,0) → 0.35s,-13 → 0.7s,0 → 0.98s,-9 → 1.26s,0 → 2.0s,0`，第一下 -13°、第二下 -9°（递减），单周期 0.70s/0.56s（约 1.5Hz），全程值域 `[-13, 0]`；四段三次贝塞尔缓入缓出（类型 1，官方 motion3 编码，`EmbodiedMotionCurve` 已支持）替代 v1 折线。
- **完整性重绑**：新 `CurveSha256 = 2abed6fa1076624d1a6df6d07f92295f0b9f9238b8f02ea98877b1a852d652ba`（UTF-8 无 BOM、LF 字节）。`PacketSha256` 保留 v1 评审包 `0300738a…`；`80/80` 双模型分数按 v1 记录暂挂，**不作为 v2 自然度证据**。`CertifiedMotionLibraryTests` 采样断言同步改为 v2 关键点，并固化「全程不得高于基线」守卫。
- **验证**：`build.ps1 -RunTests` EditMode 330 total / 326 passed / 0 failed / 4 ignored（failed=0 门禁通过）；新构建 `Build/nod_v2_20260927/DesktopPet.exe`；`observe_acknowledge_nod.cjs --auto` 隔离回放通过（started/cleanup/pose-restored，居中 x=1236/2560）。注意：旧产物 `airborne_gate_20260926_final` 内嵌 v1 哈希，对 v2 数据根文件返回完整性校验失败——运行时驱动必须使用曲线变更后的新构建。观察脚本默认 exe 已指向新构建。

## 2026-09-27 `LlmExposed` 开放（v2 曲线真人验收通过）

- **v2 真人验收结论**：观察者在 `observe_acknowledge_nod.cjs --auto` 隔离回放（`Build/nod_v2_20260927`，播 1 轮两次点头）期间实时观看真实可见窗口，结论「**是能用的**」。该结论为真实现场人工评审，补齐 v2 曲线视觉证据；结构化三轮三问流程未重复执行（观察者选择以实时观看代替，记录如实说明证据形式）。
- **开放改动**：`CertifiedMotionLibrary` 中 `acknowledge_nod` 的 `LlmExposed` 置 `true`；`CertifiedMotionLibraryTests` 暴露白名单断言与 `LocalToolRouterTests` 确定性路由断言随之反转（原「未向 AI 暴露前安全拒绝」测试改为「暴露后确定性路由到认证技能」）。
- **自然语言端到端验收**（隔离 Player，`Build/nod_v2_exposed_20260927`，`acknowledge_nod_runtime_drive.cjs` 退出码 0）：「点点头给我看」→ `DoOllamaOnlyReply` 确定性身体路由（关键词计划 → 参数加固，无需模型可用）→ `request_body_skill` → `[EmbodiedRuntimeAdmission] admitted` → `BehaviorState/LifeState Active→Completed`（intent/execution/correlation/request 身份全程一致）→ `[EmbodiedSafeRecovery] pose-restored` → 确定性回执发布。取消（`Cancelled`）、超时（`Expired`）与行走租约冲突拒绝在开放后保持不变。
- **开放后的语义**：关键词「点点头/点头/同意/nod/acknowledge」现在触发真实认证动作执行；未暴露技能（如 `screen_side_arm_raise`）的确定性拒绝路径保持，两道白名单结构未变。
- 这是项目第一条「用户自然语言 → 认证身体动作 → 真人可见」端到端闭环；其证据均为隔离数据根 + `.test_mode` Player，生产数据根与真实桌面对话场景仍属后续门槛。

## 2026-09-27 曲线 v3（头体分离违和感修正）

- **用户反馈**：v2 实机观感「只有头在动，整体不自然、有违和感」。成因诊断：v1/v2 候选只写 `ParamAngleY` 一个参数，动作期间身体角度与眼球停在基线——真实点头是头颈-上躯干的链式运动且眼球做注视补偿（动画的跟随与重叠原则）；呼吸参数由运行时持续驱动（`Live2DRenderer` Perlin 呼吸），不是冻结源。
- **官方参考配比**（生产数据根认证动作采样求值）：`external_Hiyori_Hiyori_m06` 头部 -18° 时身体 -2.8（约 16%）；`external_Haru_haru_g_m20` 头部 -13° 时身体 -2.8（约 21%）；两者在头低点时 `ParamEyeBallY` 反向 +0.1~0.2。
- **v3 曲线**：`ParamAngleY` 保持 v2 形状（-13/-9，已获人工接受的最小变更）；新增 `ParamBodyAngleY` 单次平滑起伏 0 → -2.6（0.9s 处，滞后于头部第一 dip、覆盖第二 dip，1.75s 回基线——重体量慢起慢收）；新增 `ParamEyeBallY` 注视补偿 0 → +0.2（与头部两个 dip 同步，1.3s 回基线，保持看向用户）。资源台账 `Body` → `Face | Body`。
- **完整性重绑**：新 `CurveSha256 = cd47483d459ef1eb2d322f2c9f6b920465faf89ecc0ce9cf3dbbcc1fa333578d`；`PacketSha256` 沿用 v1 评审包不变。
- **验证**：EditMode 330 total / 326 passed / 0 failed / 4 ignored（新增身体/眼球关键点断言与曲线数=3 断言）；`Build/nod_v3_20260927` 驱动脚本退出码 0（自然语言路由 + 完整生命周期 + 取消/超时/行走冲突拒绝全部保持）。

## 2026-09-27 曲线 v4（移除合成眼动）

- **用户反馈**：v3 身体跟随解决了头体分离违和（「其他感觉差不多」），但「眼睛会乱动」。成因：v3 的 `ParamEyeBallY` 补偿做成两次起伏（0→0.2→0.1→0.2→0），叠加动作起止与漂移基线（Perlin 慢漂）的接驳，读作扫视/乱瞟。已核对认证动作期间基础层 Perlin 眼动与鼠标注视均被 `_actionLocked` 租约抑制（`UpdateIdleAnimation` 与 LateUpdate 注视叠加层），动作期间唯一眼动来源就是该补偿曲线本身。
- **v4 决策**：移除 `ParamEyeBallY` 曲线（2 曲线：`ParamAngleY` + `ParamBodyAngleY`，形状均不变），资源台账回到 `Body`。理由：克制的 2 秒点头中视线定格比任何合成眼动自然；官方动作写眼球参数属展示型长动作的技法，不适用于短促确认动作。若后续需要注视稳定，应做单相位持续补偿并在运行时解决基线接驳（曲线起点=基线值），属独立改进。
- **完整性重绑**：新 `CurveSha256 = 81a28f2627e00ea155a24644976bcd8b78df44ce2b279bd87b5d0df1c4effbf2`。
- **验证**：EditMode 330 total / 326 passed / 0 failed / 4 ignored（曲线数=2 断言，移除眼球断言）；`Build/nod_v4_20260927` 驱动脚本退出码 0。

## 2026-09-27 基础层修正：闲置眼球微动幅度（v4 复评跟进）

- **用户反馈**：v4 中眼球不再乱漂，但「漂移到最左边和最右边，像是固定在左边和右边」。
- **根因**（基础层，非候选曲线）：闲置眼球微动常数 `EYE_X=3f / EYE_Y=2f`，经 `(Perlin-0.5)×幅度` 输出 -1.5~+1.5 / -1~+1，超出眼球参数 -1..1 量程被钳制——闲置眼球会长期漂到并卡在最左/最右（Perlin 高原期）。认证动作期间基线冻结继承该极端位置，点头时即"盯着一侧"。对照头部微动（0.6°/0.4°）可见其为配置失误。
- **修正**：`EYE_X=0.6f`（±0.3）、`EYE_Y=0.4f`（±0.2），闲置眼球在量程中带做轻微微动。候选文件不变（`81a28f26…` 绑定继续有效），无需重走完整性绑定。
- **验证**：EditMode 330 total / 326 passed / 0 failed / 4 ignored；`Build/nod_v5_20260927` 驱动脚本退出码 0。

## 2026-09-27 生产缺口修复：用户显式请求抢占自主行走

- **生产验收发现**：用户在生产数据根实机对话中说「点点头」，因桌宠正在自主闲逛被拒（"非静止"）。隔离驱动的既有流程总是先强制停走再触发，掩盖了该缺口——这是生产验收的第一份真实缺陷证据。
- **策略**：用户显式请求优先于自主行走。`RequestBodySkillTool` 改为异步（`IsAsync=true`）：首次执行被拒且命中「稳定静止」或「动作通道被占用」、且 `CanYieldLocomotionForUserRequest()` 为真（确实在行走、在地面、未拖拽、无其他动作占用）时，`StopIdleLocomotionForUserRequest()`（`pet.ForceStop()`）→ 等待 `IsStationaryForBodyRequest()`（≤2.5s）→ 重试一次。空中、拖拽、表情等其他占用仍立即终态拒绝；`@@sim` 直连执行器路径与空闲行为层的行走拒绝语义不变。
- **两个实现要点**（驱动失败两次换来的）：① 行走渐入窗口内 `petVx` 尚未非零，语义门禁可能放行而由输入租约层拒绝——两种拒绝文案都必须触发让位重试；② 移动任务清零不等于通道释放，行走租约收步释放滞后约 0.4s，重试必须等 `Live2DInputCoordinator.HasActiveLease == false`。
- **验证**：EditMode 330 total / 326 passed / 0 failed / 4 ignored；驱动新增行走抢占场景（`@@sim:walk:left` → 自然语言「点点头给我看」→ started/cleanup/pose-restored）全量退出码 0（`Build/nod_v6_20260927`）。生产数据根的真实对话确认仍待用户执行。

## 后续门槛

1. 生产环境部署验收：生产数据根（非 `.test_mode`）下用户实机确认「行走中被叫停→点头」体验与整体自然度；真实 OS/DWM 输入共存与设备故障场景仍需独立验收，不由本证据外推。
2. 生产构建发布：`Build/nod_v6_20260927` 为当前验证基线；正式发布构建需走完整构建与安装链路（服务注册/签名门槛见 `docs/project-evaluation-2026-09-26.md` G5）。
