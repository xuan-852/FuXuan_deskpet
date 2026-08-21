# 具身动作系统 ActionAgent — 决策-执行-验证闭环

> **文档作用**: 本模块文档描述桌宠「具身智能」子系统的**代码真相**——ActionAgent 15 文件架构、MotionAgent 决策循环、MotionPlanner 模板 + MotionTranslator LLM 翻译、闭环验证体系（GLM-4V 评分 + MotionMemory 学习）、验证协议与历史成绩。改动作生成/验证/记忆相关代码前必读。
> **基本架构**: `MotionAgent`（tick 驱动决策）→ `MotionPlanner`（10 模板）/ `MotionTranslator`（DeepSeek 自然语言→关键帧，10 规则 + 10 特殊模式）→ `MotionGenerator`（协程插值播放）→ 闭环：截图 → 2×2 拼图 → GLM-4V 评分 → `MotionMemoryManager`（30 条容量）→ VERIFIED FEEDBACK 反馈给下次生成。关键目录：`Assets/Scripts/Live2DFramework/ActionAgent/`（15 文件）。
> **开发历史迭代**: N31 引入 DualModelValidator（名义双模型）；N34（2026-07-07）SPECIAL PATTERNS 优化（捂脸/捂嘴/叉腰/缩团/行礼）；N37 通过率 60%→70%；N38（2026-08-02）代码真相审计修正规则数 11→10、SPECIAL PATTERNS 12→10；N39 修复 BUG-1（睡眠时段判断）/BUG-5（Dual 回调简化）/BUG-7（示例角度制），删除 AutoMotionCollector 死代码；N40 T2 部位裁剪 schema。
> **编写注意事项**: ①验证协议是「历史数据存档」，评分须同步更新 `verification_report_2026-07-07.md` 或本模块文档；②GLM-4V 是唯一评分模型（Qwen-VL 已删除，`DualModelValidator` 名存实亡但文件未改名）；③MotionMemory 容量 30 条、负反馈 ≤2 分入反例（最多 10 条）；④验证截图存 `glm_collages/` 上限 50 张；⑤行走研究仅理论参考（Live2D 无腿部参数，未落地）。
>
> **质量遥测（2026-08-18）**：`QualityTelemetry` 记录 `motion_decision`、`motion_translation` 和 `motion_validation` 的来源（local/template/cloud/fallback）、解析结果、耗时、关键帧数量和 GLM 分数；不记录动作原文或参数快照。与 `validation_log.json` 的历史动作描述日志分离，统计命令为 `node scripts/log-analysis/summarize_quality.cjs D:\DesktopPetData`。`--cloud-baseline` 会跳过 MotionTranslator 的本地优先路径并暂停自主决策，`@@case:<id>` + `@@motion:<描述>` 可让指定动作结果按案例配对，详细流程见 `docs/quality-comparison-test-guide.md`。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及动作生成（MotionPlanner/MotionTranslator）、动作验证（闭环演武）、动作记忆（MotionMemory）、表情/空闲动作调度的改动。
- **回答的问题**:
  - ActionAgent 有哪些文件？各自职责？
  - 动作是怎么从自然语言变成 Live2D 关键帧的？
  - 闭环验证怎么跑？评分标准是什么？
  - 历史上动作质量如何迭代的？有哪些已知限制？
- **关联文档**: `code-truth-architecture.md` 五章（具身智能层真相）｜`modules/ai-chat-system.md`（闭环演武注入 prompt）｜`modules/live2d-rendering.md`（参数执行端）｜`modules/tool-engine.md`（动作工具 set_expression/play_action 等）

## 二、基本架构

### 2.1 ActionAgent 文件清单（15 个 .cs）

| 文件 | 职责 |
|------|------|
| `MotionAgent.cs` | 自主动作决策引擎（tick 驱动） |
| `MotionPlanner.cs` | 10 模板 + 6 曲线 + 3 阶段 |
| `MotionTranslator.cs` | LLM 自然语言→关键帧（10 规则 + 10 特殊模式） |
| `MotionGenerator.cs` | 协程插值播放 |
| `MotionMemoryManager.cs` | 闭环学习核心 |
| `DualModelValidator.cs` | GLM-4V 拼图评分（**单模型**，Qwen-VL 已删） |
| `VisionMotionVerifier.cs` | GLM-4V 视觉验证（10 测试序列） |
| `SafetyValidator.cs` | 参数安全校验 |
| `PersonalityManager.cs` | 人格演化 |
| `EmotionState.cs` | 情绪模型 |
| `IdleActionScheduler.cs` | 空闲动作调度 |
| `MotionVerifier.cs` | 参数一致性校验 |
| `LocalLLMClient.cs` | Ollama 本地决策回退 |
| `GpuLoadMonitor.cs` | GPU 负载检测 |
| `ActionReferenceManager.cs` | 动作引用管理 |

> N39 已删除：`AutoMotionCollector.cs` 死代码（文件 + meta + 自动添加逻辑一并移除）。

### 2.2 决策循环

```
MotionAgent tick (High 4s / Med 8s / Low 15s / Sleep 30s)
  → ShouldDecide (密度/空闲/专注检测)
  → GatherContext (宠物状态/情绪/时间/用户活动)
  → DecideWithLLM / FallbackDecide (Ollama 失败≤3 次回退概率)
    → ExecuteDecision → 动作/表情/等待
```

> N39 BUG-1 修复：`IsSleepTime()` 按真实凌晨 1~7 点判断（`testMode` 下跳过）。

### 2.3 MotionTranslator（自然语言 → 关键帧）

- DeepSeek API，temp=0.3
- **10 条通用规则 + 10 种 SPECIAL PATTERNS**（9 种独特姿势，捂脸出现两次）
- 身体分组 Schema：HEAD / EYES / BROWS / MOUTH / ARMS / HANDS / FINGERS / LEGS / BODY
- 参数富化：自动补全关联肢体
- VERIFIED FEEDBACK：上次失败案例自动反馈给 LLM（MotionMemory 反例 TOP-3）
- N40 T2：body schema 按描述关键词裁剪部位（~16k→~5k tokens）

### 2.4 MotionPlanner（硬编码模板）

- 10 种模板：挥手 / 点头 / 摇头 / 鞠躬 / 伸懒腰 / 叉腰 / 捂脸 / 指 / 招手 / 合十
- 6 种插值曲线（`InterpolationType`）：**Linear / Smooth / EaseOut / EaseIn / Hold / Bounce**
- 3 阶段计划：淡入 → 保持 → 回归
- 6 种表情模板（happy/sad 等）

### 2.5 闭环验证体系（完整闭环）

```
generate_motion → 播放动作 → 截图 (20/40/60/80%)
  → 2×2 拼图 (存 glm_collages/, 上限 50)
  → GLM-4V 评分 (1-5, passThreshold=3)
  → 写入 MotionMemory
  → 下次生成参考高分案例 (TOP-3)
  → VERIFIED FEEDBACK 注入失败案例 (反例 TOP-3)
```

### 2.6 MotionMemoryManager 参数

| 机制 | 参数 |
|------|------|
| 容量 | 30 条（高分覆盖低分） |
| 淘汰 | 最低分 / 最久远 |
| 负反馈 | ≤2 分记入反例（最多 10 条） |
| 无望检测 | ≥5 次且最高分≤2 优先淘汰 |
| 冷却 | 120s 防复读 |

### 2.7 验证等级与通过标准

| 等级 | 定义 | 判定方法 |
|------|------|---------|
| PASS ✨ | 动作可被肉眼识别且符合语义 | 人类观察 + GLM-4V 2×2 拼图 |
| PASS ✅ | 参数序列物理合理 | 自动校验（范围/对称/平滑） |
| FAIL ❌ | 越界/抖动/不符合语义 | 自动校验 |

通过标准：🥇 完全通过（自动全达标 + 人类平均 ≥4）/ 🥈 基本通过（≥80% + 平均 ≥3）/ 🥉 需改进（≥60%）/ ❌ 未通过（<60% 或有崩溃）

### 2.8 空闲动作

9 种 JSON 配置驱动（id 1--9：歪头 / 微笑 / 挑眉 / 星辉* / 伸懒腰 / 委屈 / 法阵* / 害羞 / 困惑；*为硬编码特效）。天气/时段动态调整权重：夜晚微笑×0.3 / 雨哭×1.8 / 雪微笑×1.5；冷却系统防复读。

## 三、开发历史迭代

| 版本 | 日期 | 变更 |
|------|------|------|
| N31 | — | 引入 DualModelValidator（名义双模型 Qwen-VL+GLM-4V，拒绝 <2.5 分动作） |
| N34 | 2026-07-07 | SPECIAL PATTERNS 优化：捂脸（arm_mid 升面部）、捂嘴（ONE_HAND + mouth_open_y=0）、叉腰（arm_lower 0.15→≥0.4）、缩团（body_angle_y 弓身前倾）、行礼（body_angle_x→body_angle_y） |
| N37 | 2026-07-13 | 通过率 60%→~70%，平均分 2.7→3.2/5.0 |
| N38 | 2026-08-02 | 代码真相审计：通用规则 11→**10 条**、SPECIAL PATTERNS 12→**10 种**（捂脸重复）；`validation_log.json` 未实现（实际 Debug.Log 输出） |
| N39 | 2026-08-02 | BUG-1 睡眠时段按真实凌晨 1~7 点；BUG-5 DualModelValidator 回调签名 4 参删除恒 0 qwenScore；BUG-7 示例参数改角度制（20~28°）；删除 AutoMotionCollector |
| N40 | 2026-08-08 | T2 body schema 部位裁剪（-70% 请求体）；验证协议结构与历史评分保留 |

### 历史成绩（v1 基线 2026-07-07，考官 glm-4.6v）

| ID | 动作 | 基线分 | 优化后(07-13) |
|----|------|--------|---------------|
| T1 | 害羞捂脸 | ⭐⭐ | ⭐⭐⭐ |
| T2 | 挺胸叉腰 | ⭐ | ⭐⭐⭐ |
| T3 | 惊讶捂嘴 | ⭐⭐ | ⭐⭐⭐ |
| T4 | 忧郁远望 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| T5 | 俏皮眨眼 | — | ⭐⭐（眼不对称仍困难） |
| T6 | 行礼 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| T7 | 吓到缩团 | ⭐ | ⭐⭐ |
| T8 | 骄傲抬头 | ⭐⭐⭐ | ⭐⭐⭐⭐ |
| T9 | 歪头思考 | ⭐⭐ | ⭐⭐（手托下巴难模拟） |
| T10 | 合十祈祷 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |

**总体**: 通过率 60% → ~70%，平均分 2.7 → 3.2/5.0

## 四、编写注意事项

1. **验证协议文档定位**：`embodied-ai-verification.md`（现并入本文档三章）是**验证方法与历史数据存档**，协议结构不因 N39/N40 改变；改 MotionTranslator/SPECIAL PATTERNS/SafetyValidator/MotionPlanner 模板后必须跑回归验证
2. **GLM-4V 是唯一评分模型**：`DualModelValidator.cs` 文件仍叫 Dual 但实际仅 GLM-4V（付费 glm-4.6v，免费 glm-4v-flash 回退），`qwenScore` 恒 0——**不要**为它加回 Qwen 逻辑，除非明确要双模型
3. **验证截图管理**：2×2 拼图存 `glm_collages/` 上限 50 张，超限需清理
4. **`validation_log.json` 不存在**：早期文档声称写入该文件，代码未实现——验证报告经 `Debug.Log` 输出，不要找这个文件
5. **行走循环研究未落地**：`walk_cycle_research.md`（Animator Island / 《动画师生存手册》理论）是纯研究笔记，Live2D 模型无腿部参数，行走映射仅理论参考
6. **验证运行方式**：聊天输入 `\verify` 运行完整验证套件，或输入动作描述（如"害羞地捂脸"）触发自动播放+验证
7. **测试模式**：验证套件涉及 GLM 截图与 Memory 写入，测试须开 `.test_mode` 防污染 MotionMemory 高分/反例
8. **动作时冻结行走**：动作播放期间调用 `_pet.Pause/Resume()` 防冲突，勿在动作中修改物理状态

### AI 动作与走路交接修复（2026-08-22）

- `MotionAgent` 的参数动作/复合动作统一经过 AI 动作交接：先锁定地面移动并暂停宠物，再启用 `Live2DRenderer` AI 控制锁；动作结束按相反顺序恢复。
- `Live2DRenderer.Update/LateUpdate` 在 AI 控制锁期间不再补写走路淡出帧，避免动作第一帧被走路姿态覆盖，减少身体与后发丝的卡顿。
- 释放 AI 锁后保留既有走路淡入机制，让物理系统从稳定中性姿态恢复到走路姿态，而不是在同一帧叠加两套参数。
- 验证：`build.ps1 -Quick` 和隔离运行冒烟测试通过；动作视觉连续性仍需用户在真实 Live2D 窗口中复核。
