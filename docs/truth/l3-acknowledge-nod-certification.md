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
- Player 验收使用测试 inbox、临时数据根和模拟状态命令；它证明认证执行与状态闭环，不等于真实 OS/DWM 鼠标、真实桌面输入或真实显卡故障验收。
- 本技能不改旧 `play_action("nod")` 语义，不开放原始参数，也不把旧 `MotionPlanner` 模板的存在当作认证证据。

## 后续门槛

1. 在可见播放器中采集点头连续帧并完成语义、自然度和复位人工复核。
2. 如需向 LLM 暴露，补齐独立视觉证据、更新暴露白名单和自然语言端到端验收；在此之前保持隐藏。
3. 真实 OS/DWM 输入、真实设备故障和生产数据根部署仍需独立验收，不由本证据外推。
