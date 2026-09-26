# 符玄项目差距评估报告（2026-09-26）

> 性质：目标与现状的差距评估（非验收证书）。目标基线取自 `docs/decisions/2026-09-15-product-direction-baseline.md`（L0–L6 能力边界）与 `docs/decisions/2026-09-24-product-experience-and-project-goals.md`（北极星、七级优先序、反目标）；现状取自分支 `fix/airborne-certified-motion` @ `f468e19` 的代码核对、`docs/truth/` 证据文档与当日构建/测试产物。前序报告：`project-evaluation-2026-08-31.md`、`project-evaluation-2026-09-06.md`。
>
> **上下文预算说明**：本任务性质为全项目差距评估，需要跨模块阅读目标决策文档、40+ 份 truth 文档、多个模块代码与测试产物，超出「一个任务包 + 一个指导 + 两份决策/真相文档」的默认预算，触发原因即任务本身的全局盘点性质。
>
> **评分立场**：延续 09-06 报告的结论——缺少真实 GUI 人工签字、长时资源曲线与干净机安装证据，不设百分制总分；本文按「层 × 差距项 × 证据」给出可核对结论。

---

## 一、总体结论

1. **工程卫生与治理面：显著改善，有当日证据。** 09-06 报告列出的差距项 E01–E06 已全部关闭（见 §四），EditMode 测试基线 330 用例 / 326 通过 / 0 失败 / 4 忽略（`logs/build/test_results.xml`，2026-09-26），当日仍有完整构建产出。decisions/guides/truth/tasks 分层治理体系运转中，truth 文档已积累 43 份。

2. **具身认证链（L3）：架构骨架闭环成立，但三个系统性缺口使其尚不能支撑北极星体验。** 认证路径本体（四层认证 → `EmbodiedRuntimeAdmission` 唯一准入汇点 → `PlayCertifiedMotion` → BehaviorIntent → LifeState 回写）代码可核对、闭环完整；但（a）8 项已认证动作 **0 项取得真人视觉自然度/复位现场结论**；（b）**统一仲裁未收口**——`self_review` 工具仍在生产白名单中绕过 ActionRequest 直驱旧动作路径；（c）L4 基线要求的**四意图分离只有 body 一维落地**且为字符串闭集。

3. **北极星（2026-09-24 定义）与现状的距离**：三大体验结果中，「交互清晰感」的具身侧第一环（用户自然语言 → 认证动作 → 真人可见）卡在视觉验收与 `LlmExposed` 决策两个已具雏形的收尾步骤；「工作完成感」的交付侧卡在安装服务注册/签名/干净机验收。二者均为收尾型差距而非方向型差距。

---

## 二、分层现状对照（目标基线 L0–L6）

| 层 | 基线要求（出处：09-15 baseline） | 现状（本轮核对） | 评级 |
|----|----|----|----|
| L0 治理 | 目录即状态、BlockedByDecision 等 8 规则 | decisions/guides/truth/tasks 分层运转，43 份 truth 文档 | ✅ 运转中 |
| L1 离线韧性 | 无网仍可显示交互、退出四态、安全模式 | 前期已建立；**本轮未复测** | ⚪ 本轮未核对 |
| L2 桌面物理 | 穿透/拖拽/抛掷/落地状态机、步行须认证 | 真实鼠标 walking→drag→throw→land 证据（2026-09-22）；物理命令、关机回调顺序、drag-response 迁移未验证 | 🟡 部分 |
| L3 认证动作 | 四要素认证、统一 ActionRequest 仲裁、并行上限 | 8 项认证 / 6 项 LLM 暴露 / **0 项真人视觉结论**；认证路径仲裁闭环，但 self_review 等旧路径仍在生产绕行 | 🟡 骨架闭环、证据链缺视觉维度 |
| L4 意图分离 | Reply/Tool/Body/Control 四意图结构化分离 | 仅 body 一维，字符串闭集（`LocalToolRouter.cs:74-77`）+ ChatManager 两处确定性覆盖；其余三维无代码实体 | 🔴 未落地主体 |
| L5 记忆情绪 | 情绪短时不沉积、记忆可见性 UI、低打扰问候 | 记忆可见性 UI 属 09-15 §8「未形成指导文档」六项之一 | 🔴 未启动 |
| L6 交付边界 | 办公不覆盖同名、更新不静默安装 | 办公链路可用；安装器阶段 4 未闭环（NSSM 下载失败 → 服务未注册；签名未完成） | 🟡 部分 |

---

## 三、差距清单

### P1 —— 直接阻断北极星三大体验结果

**G1 真人视觉验收缺口（0/8）**。L3 认证四要素中「写入/读回/复位」已有隔离 Player 证据，「视觉可见、时序自然」系统性缺真人现场结论。当前最接近的证据：2026-09-26 帧序列观看确认「能辨认出两次点头」（`docs/truth/l3-acknowledge-nod-certification.md` 未提交段），但不构成节奏自然度与画面复位结论。观察工具已备好且完成度高：`scripts/test/observe_acknowledge_nod.cjs`（受控 3 轮 + 结构化三问 + `human-review.json`）与 `scripts/observe_user_session.cjs`（真实使用只读旁观），均未提交、尚无真人结论记录。

**G2 统一仲裁未收口（self_review 绕过口在生产）**。L3 基线要求「动作统一经 ActionRequest 仲裁」，现状：
- `self_review` 工具仍注册（`Assets/Scripts/ToolEngine/MotionCoroutineTools.cs:497`）、在 `LocalToolRouter` 两处白名单中（`LocalToolRouter.cs:64,92`）、未列入 `DisabledToolReasons`（`ToolRegistry.cs:41-47` 仅禁 control_body/play_action/generate_motion）；执行时直接 `StopAllActionsAndExpressions()` + `PlayAction()`（`MotionCoroutineTools.cs:539,543`），完全绕开认证协调。
- 旧路径本体仍在生产：`PlayAction`（`Live2DRenderer.cs:5071-5130`，仅输入租约门禁）、AutoChat 困惑反射 `ForceAction("confuse")`（`AutoChat.cs:136-139`）、VisualHeartbeat 表情（`VisualHeartbeat.cs:180`）、9 类硬编码空闲动作（`Live2DRenderer.cs:3352/3398` → `IdleActionScheduler.cs:221`）。
- 输入租约层（`Live2DInputCoordinator`）与认证协调层（`EmbodiedCoordinator`）仍是两套体系（09-24 决策自认过渡态）；资源级并行/抢占未引入（`unified-life-control-loop-phase-b-desktop-state.md` 遗留项）。
- 已核对为真实缺口（非文档陈旧）：本文写作前逐条 grep 复核。

**G3 四意图分离未落地**。`ReplyIntent/ToolIntent/ControlIntent` 全仓库零代码实体；body 维度是字符串闭集 + 两处 ChatManager 覆盖（`ChatManager.cs:556`、`ChatManager.RequestLifecycle.cs:110`）。2026-09-18 真人首测失败（「摇摇头」被 3B 分类器误判）已由确定性 body 路由修复（隔离验证 315/311，`l4-body-intent-deterministic-routing.md`），但属补丁式闭集，不是基线要求的结构化意图层。

### P2 —— 可靠性、决策悬置与交付

**G4 `acknowledge_nod` 的 LlmExposed 决策悬置**。执行链完整、曲线上线（`CertifiedMotionLibrary.cs:63`，`LlmExposed=false`），关键词映射已含 `acknowledge_nod`（`LocalToolRouter.cs:262`）但被 `TryResolveCertifiedBodySkill` 跳过 → 用户自然语言「点头」请求终态拒绝。这是产品决策，依赖 G1 现场结论。

**G5 交付门槛全开放**。`install-service.cmd` 因 NSSM 在线下载失败 exit 10 → 桥接服务未注册；商业签名与依赖哈希锁定未完成（`docs/installer-plan.md` 阶段 4）；五节日主题 T3/T5 真实 GUI 签字未关闭；长时 CPU/GPU/内存采样无实测证据；09-06 的 E05 时延数字仍未测——且残留一处每请求同步点：`resolvePython()` 无缓存、内部 `execSync('where python')` 最坏阻塞 5s（`openclaw_bridge.js:556-575`）。

**G6 LLM 动作时长阈值待人工决策**。具身指导默认单次 ≤4 秒，已认证外部动作 5.5–10 秒（`l3-llm-embodied-integration.md` 遗留）。

**G7 硬件 P0 未核查**。构建期 i9 满载 95°C+、Kernel-Power 41 / WHEA-Logger 19 事件，软件负载保护已实现但硬件根因（BIOS/散热/PSU）未核查。

**G8 SoulLink 真实 engine 未验证**。离线 PoC（f468e19）停在 fixture 链路 + Node smoke；真实 `@soullink-emotion/engine` 导出边界未证明满足适配器契约，候选距运行时还隔着「隔离 Player capture → 认证 → 授权」数道闸。

### P3 —— 卫生与规划

**G9 E07 残留**：`AGENTS.md:17` 写 122 个 C# 文件、实际 143 个；git 仍跟踪 348 个 node_modules 文件（全部在 `code/desktop_unity/.codely-cli.bak/` 工具备份残留）；Assets 下约 133 个 .cs 与 5 个已跟踪 .ps1 无 BOM。
**G10 防误宣**：3D 渲染空壳 disabled、动态分辨率 `GetResolutionScale` 恒 1.0、跨屏行走策略——遗留规划能力，不得宣称可用。
**G11 六项方向无指导文档**（09-15 baseline §8）：其中记忆可见性 UI（L5 主体）与运行时恢复/安全模式（L1 深化）是层目标的主要缺口。
**G12 Motion Factory 未立项**：全仓库无该名称的文档或代码，属规划项。
**G13 认证行为层缺自主性维度**：注意力驱动、反射层触发、行为画像与用户可调频率均未实现（`l3-certified-skill-behavior-layer.md` 遗留）。

---

## 四、与 2026-09-06 评估的对比

| 09-06 差距项 | 当前状态 | 证据 |
|----|----|----|
| E01 安装脚本 BOM | ✅ 已修复 | `installer/components/download-ollama.ps1` 头 3 字节 EF BB BF |
| E02 测试读真实剪贴板/空参遍历 | ✅ 已修复 | `ToolEngineTests.cs` 剪贴板零命中；空参遍历仅限 `safeReadonlyTools`（`:335`） |
| E03 强杀生产实例/删除无边界 | ✅ 已修复 | `build.ps1:135-152` 只杀构建辅助进程；`runtime_smoke.cjs:89-108` `assertSafeTestDataRoot` 五重边界 |
| E04 零污染断言漏报 | ✅ 已修复 | 数据根解析与 `DataPathConfig.cs:49-66` 逐条一致；SHA-256 + 存在性双断言（`runtime_smoke.cjs:209-329`） |
| E05 桥接同步阻塞 | ✅ 基本修复 | 办公/PDF/LaTeX 均 `execFileAsync`；残留 `resolvePython` 每请求最多 5s（G5 引用） |
| E06 非原子写入 | ✅ 已修复 | `AtomicFileWriter.WriteAllText` + `.bak` 回退（`PetMemory.cs:646`、`KnowledgeBaseManager.cs:749`） |
| E07 口径不一致 | 🟡 部分修复 | 判定口径已对齐；AGENTS 文件数 / node_modules / BOM 残留（G9） |
| 真实 GUI 签字 / 长时采样 / 干净 VM / 硬件 P0 / E05 时延 | 🔴 仍开放 | 见 G5、G7 |

**重心迁移**：09-06 后工作重心从节日皮肤/工程卫生转入 Live2D L3 认证体系（09-15 起约 40 份新 truth 文档）；G1–G3 属该链路自身长出的新差距，非 09-06 遗留。另一实质改进：09-18 真人首测暴露的自然语言→身体路由失败已修复并有隔离验证证据。

---

## 五、有证据的已达成交付（健康面）

- 测试基线健康：330/326/0/4（当日 XML），构建当日活跃（`logs/build/build_log.txt` 14:39）。
- 空中/步行认证门禁：当天修复（`a8552da`）、当天用 `airborne_gate_20260926_final` 产物复测全过——问题响应周期已缩短到一天内。
- 真实 Windows 鼠标 walking→drag→throw→land 全链证据（2026-09-22）。
- 确定性 body 路由：Ollama 不可用时自然语言仍能确定性触发认证曲线；故障注入四终态验证。
- 数据可靠性：原子写入 + 冒烟防污染断言与 `DataPathConfig` 严格对齐。
- 认证架构先行的设计质量：四层认证、双白名单（运行时认证 × LLM 暴露）、曲线 SHA-256 绑定，均为代码可核对事实。

---

## 六、建议优先级

1. **G1 视觉验收收口**（工具已备好，边际成本最低）：用 `observe_acknowledge_nod.cjs` 取得真人三问结论 → 顺势完成 **G4** LlmExposed 决策 → 打通第一条「自然语言 → 认证动作 → 真人可见」端到端体验，作为北极星的第一份现场证据。
2. **G2 仲裁收口**：第一步仅禁 `self_review` 生产路径（改一行 DisabledToolReasons + 白名单摘除，低成本）；遗留路径按「表情 → 空闲 → AutoChat/心跳反射 → PlayAction 本体」逐类迁入，迁移开始前先补迁移指导文档。
3. **G3 意图结构化**：在统一桥接协议/工具风险分级的指导文档框架内设计 IntentEnvelope，避免字符串闭集继续扩张。
4. **G5 交付线并行推进**：NSSM 离线随包化 + 服务注册 + 签名 + 干净 VM 一条龙收口；顺手缓存 `resolvePython()`。
5. **G7 长时采样 + 硬件核查**（可与 4 同批安排）。
6. **G12 Motion Factory**：立项前先按 09-15 §8 规则产出指导文档，避免先码代码后补认证。

---

## 七、评估限制

- 本文为只读评估：未执行构建/测试，引用的是当日已有产物（测试 XML 2026-09-26 04:08、构建日志 14:39）。
- 未做真实 GUI 现场观察（正是 G1 指出的缺口本身）。
- L1 层未在本轮核对范围内。
- 以下两项来自前次会话记忆、未在本轮代码中复核，列作待核实：Sentinel H0007 环境报告未确认/未修复；星辉（Star Spin）参数回归是否仍存在。
- 工作区存在未提交改动（`docs/truth/l3-acknowledge-nod-certification.md` 修改 + 两个未跟踪观察脚本），本文将其计为「进行中」状态。
