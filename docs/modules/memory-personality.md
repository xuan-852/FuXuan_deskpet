# 记忆与人格系统 — PetMemory、人格演化与知识库

> **文档作用**: 本模块文档描述桌宠「记忆与人格」子系统的**代码真相**——PetMemory 三层记忆、PersonalityManager 五维人格演化、KnowledgeBaseManager 本地 RAG 知识库，以及数据持久化文件地图。改记忆读写/人格演化/知识库相关代码前必读。
> **基本架构**: `PetMemory`（entries + coreFacts + conversationSummary 三层，输出 4 层格式化）；`PersonalityManager`（五维人格 × 三维关系 × 情绪联动，`pet_personality.json`）；`KnowledgeBaseManager`（Ollama nomic-embed-text 嵌入 + 余弦 TopK 检索，`knowledge_base.json`）；反思链路（CheckReflection → DoReflection → CommitReflection）。数据根目录硬编码 `D:\DesktopPetData\`（`DataPathConfig.cs`）。
> **开发历史迭代**: N39 修复两大缺口——反思链路实际接线（死回调 OnReflectRequest 删除）、知识库上下文实际注入（GetFormattedContext 返回 LastFormattedContext 缓存）；测试模式 IsTestMode 防污染（.test_mode 标记文件）；2026-08-12 P4 新增 PreferencesManager 偏好结构化存储（`pet_preferences.json` + set/query/remove 三工具）。
> **编写注意事项**: ①测试必须开 `.test_mode`（防污染 pet_memory.json 忆境 + pet_personality.json 人格计数），测后清理用 `scripts/openclaw/clean_test_pollution.cjs`；②人格触发词注意区分正负触发（"我的"/"我在"等 importantMarkers）；③`DriftTowardNeutral()` 存在但 ActionAgent 内无调用者（潜在死代码）；④知识库检索是协程异步填充缓存，同步 API 返回最近结果（可能有 1 帧延迟）。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及记忆读写、人格演化、知识库检索、数据持久化的改动。
- **回答的问题**:
  - 记忆分几层？怎么持久化？格式化输出是什么样？
  - 人格五维是什么？怎么演化？和情绪怎么联动？
  - 知识库 RAG 怎么工作的？
  - 哪些数据文件在哪？谁写的？
- **关联文档**: `code-truth-architecture.md` 六章（感知/记忆/窗口真相）+ 七章（数据持久化真相）｜`modules/ai-chat-system.md`（反思链路 + 记忆注入 prompt）｜`modules/action-agent.md`（MotionMemory 独立于本模块）｜`modules/tool-engine.md`（inspect_personality 工具）

## 二、基本架构

### 2.1 PetMemory — 三层记忆

| 层级 | 容量 | 内容 | 持久化 |
|------|------|------|--------|
| 核心事实 | ≤5 | 用户基本信息 | JSON |
| 重要记忆 | Top-20 | 重要性排序 | JSON |
| 近期琐事 | ≤10 | 最近交互 | JSON |

存储结构：`entries + coreFacts + conversationSummary`；格式化输出为 **4 层**：核心事实 → 【近日印象】→ Top5 重要 → 最近 3 条。

### 2.2 人格演化系统（PersonalityManager）

| 维度 | 初始值 | 范围 | 描述 | 正触发 | 负触发 |
|------|--------|------|------|--------|--------|
| diligence | 0.5 | 0-1 | 勤勉 vs 慵懒 | 工作/学习 | 游戏/娱乐 |
| warmth | 0.6 | 0-1 | 温暖 vs 高冷 | 感谢/称赞 | 负面情绪 |
| playfulness | 0.5 | 0-1 | 活泼 vs 稳重 | 游戏/语气词 | 学习/工作 |
| confidence | 0.5 | 0-1 | 自信 vs 谦逊 | 工具成功 | 工具失败 |
| curiosity | 0.6 | 0-1 | 求知 vs 淡然 | 搜索/提问 | — |

**三维关系**：信任(0.3) / 亲密(0.2) / 熟悉度(0.1, 对数增长)，learningRate=0.01
**人格↔情绪联动**：五维 × 权重 → EmotionState 四维偏移
**持久化**：`pet_personality.json`
> ⚠️ `DriftTowardNeutral()`（无交互回归）存在但 ActionAgent 内无调用者——潜在死代码。

### 2.3 知识库（KnowledgeBaseManager）

- 本地 RAG：Ollama `/api/embed` + nomic-embed-text 嵌入 → 余弦 TopK 检索
- 25+ 文件类型分块索引
- `knowledge_base.json` 持久化
- N39 修复：`GetFormattedContext()` 返回 `LastFormattedContext` 缓存（`SearchAndFormat` 协程填充），知识库内容已注入对话上下文

### 2.4 反思链路（N39 接线）

```
SendRequestCoroutine → CheckReflection (L518)
  → DoReflection (DeepSeek 提炼)
  → CommitReflection (写入记忆)
```

记忆重要性评估 / 反思提炼已实际驱动（曾有的 `OnReflectRequest` 死回调已删除）。

### 2.5 数据持久化地图（根目录 `D:\DesktopPetData\`，DataPathConfig.cs）

| 文件 | 写入方 | 说明 |
|------|--------|------|
| `pet_config.json` | PetConfig | 宠物配置 |
| `pet_memory.json` | PetMemory | 记忆（3 层结构） |
| `pet_personality.json` | PersonalityManager | 人格五维 |
| `pet_preferences.json` | PreferencesManager | 主人偏好（50 条上限） |
| `task_trajectories.json` | TaskTrajectoryManager | 任务执行轨迹（30 条上限，P5.2） |
| `task_templates.json` | TaskTemplateManager | 任务模板（30 条上限，P5.3） |
| `reminders.json` | ReminderManager | 提醒 |
| `motion_memory.json` | MotionMemoryManager | 演武心经（30 条上限） |
| `activity_log.json` | ActivityTracker | 30 天活动日志 |
| `knowledge_base.json` | KnowledgeBaseManager | RAG 知识库 |
| `Documents/` | LatexCompileTool / OfficeTools | LaTeX/办公输出 |
| `ActionRefs/` | ActionReferenceManager | 参考图（512×512 PNG 仅注释） |
| `glm_collages/` | DualModelValidator | 2×2 拼图（上限 50 张） |
| `.test_mode` | 手动创建 | 测试模式标记（存在 = IsTestMode） |

### 2.6 感知侧（ActivityTracker，关联注入）

- 2s 轮询前台窗口；8 类关键词匹配（coding/gaming/studying/browsing/entertainment/communication/idle/other）；30 天留存 `activity_log.json`
- 摘要经 system prompt 注入对话（AI 对话系统 2.3 节注入链第 2 项）

### 2.7 主人偏好（PreferencesManager，P4.2）

- **设计定位**：与记忆（事件流）互补——偏好是「去重、可覆盖、常驻」的结构化条目；文件 `pet_preferences.json`
- **条目结构**：`{key, value, source(user/infer), note, updatedAt}`；同 key 覆盖更新；上限 50 条淘汰最旧
- **注入**：`FormatForPrompt()` → ChatManager.BuildSystemPrompt（人格注入之后），标题「【本座谨记 · 主人偏好】」
- **工具**：`set_preference`（记/改）/ `query_preferences`（查）/ `remove_preference`（删），均为同步工具，自动注册（ToolRegistry 反射）
- **测试注意**：EditMode 下 `AddComponent` 不触发 Awake，测试需手动反射注册单例 + 调用 Load()；测试键用 `test_` 前缀并在 TearDown 清理

### 2.8 记忆治理层（2026-08-21，第一阶段）

`MemoryGovernance` 是不访问 Unity 生命周期和文件的纯逻辑层，`PetMemory` 负责持久化和生命周期。第一阶段保持旧 `pet_memory.json` 字段兼容，并为新旧记忆补齐以下元数据：

| 字段 | 作用 |
|---|---|
| `id` | 稳定标识，旧记录载入时自动生成 |
| `source` | `user` / `local_model` / `tool` / `reflection` / `system` |
| `confidence` | 记忆可信度 0-1 |
| `lastAccessAt` / `accessCount` | 记录被检索情况，暂不因每次访问落盘 |
| `expiresAt` | 可选过期时间 |

当前治理流程为：

```text
AddMemory → 长度过滤 → 规范化 → 同类近似去重/合并 → 重要度与可信度更新 → 持久化
当前用户问题 → 关键词/中文词元重叠 + 重要度 + 可信度 + 时间衰减 → 选择有限记忆 → PromptContextBudget
```

具体行为：

- 空白或过短内容不写入；摘要最长 240 字；
- 同类、同话题的近似记忆合并，不重复占用条目；新证据可提升已有记忆的可信度和重要度；
- `ChatManager.BuildSystemPrompt()` 使用当前用户问题检索记忆，不再每轮固定注入同一批普通 Top-N；核心事实仍保留；
- 过期记忆不参与检索；访问次数只在内存中更新，避免每轮对话产生磁盘写入；
- `.test_mode` 下允许内存态测试，但 `PetMemory.Save()` 直接阻断，防止污染生产记忆。

当前仍未实现跨类型事实冲突解决和本地反思完全替代云端；记忆管理 UI 已补齐为只读浏览 + 安全治理入口。

### 2.9 忆境管理 UI（2026-08-21）

`RightPanel` 的底部工具栏新增「忆境」入口，打开 `PanelView.Memory`（`DrawMemorySubPanel`）。面板只读展示核心事实与长期记忆的摘要、类别/来源、重要度、可信度、访问次数、记录时间和过期状态。

「清理过期」调用 `PetMemory.RemoveExpiredMemories()`；「清空忆境」必须再次点击确认，并同时清空长期记忆、核心事实和对话摘要。UI 不直接编辑 JSON，也不在浏览时调用会增加访问次数的检索 API。测试模式可通过 `@@view:memory` 打开该页，使用隔离 `FU_XUAN_DATA`，不会读取或改写生产记忆。

## 三、开发历史迭代

| 版本 | 日期 | 变更 |
|------|------|------|
| N31-N37 | — | 三层记忆、五维人格、知识库 RAG 建立 |
| N39 | 2026-08-02 | ①反思链路接线（CheckReflection→DoReflection→CommitReflection，删除 OnReflectRequest 死回调）②知识库上下文实际注入（LastFormattedContext 缓存替代 STUB） |
| N40 | 2026-08-08 | 新增 `IsTestMode`（.test_mode 标记文件）防自动化测试污染记忆/人格；clean_test_pollution.cjs 清理工具 |
| P4.2 | 2026-08-12 | 新增 PreferencesManager 偏好结构化存储（`pet_preferences.json`，50 条上限淘汰最旧）+ set/query/remove 三工具 + prompt 注入；P4PerceptionTests 10 用例 |
| P5.2 | 2026-08-12 | 新增 TaskTrajectoryManager 任务执行轨迹库（`task_trajectories.json`，30 条上限淘汰最少引用/最旧，bigram Jaccard 相似检索，参考文本附加 referenceCount 计数）+ prompt 注入；P5TrajectoryTests 21 用例 |
| P5.3 | 2026-08-12 | 新增 TaskTemplateManager 任务模板库（`task_templates.json`，30 条上限，5 预置模板）+ query/save/remove 三工具 + openclaw_task 模板参数 |
| Memory Governance P1 | 2026-08-21 | 新增 `MemoryGovernance`；PetMemory 元数据、写入过滤、近似去重、相关性检索、时间衰减、测试模式落盘保护；ChatManager 按当前问题选择记忆 |
| Memory UI P1 | 2026-08-21 | RightPanel 新增「忆境」页：核心事实/长期记忆只读浏览、过期清理、二次确认清空、`@@view:memory` 隔离测试入口 |

## 四、编写注意事项

1. **测试必须开测试模式**：建空文件 `D:\DesktopPetData\.test_mode`（防污染 pet_memory/pet_personality），测后删 + `node scripts/openclaw/clean_test_pollution.cjs`（备份→删测试记忆→回退 totalInteractions→重算 familiarity）
2. **人格触发词敏感**：importantMarkers 触发词（"我的""我在"等）会推高人格计数——测试消息会永久改变人格，务必测试模式隔离
3. **知识库异步缓存**：`GetFormattedContext()` 是同步 API 返回协程填充的缓存（可能有 1 帧延迟），不要改成同步阻塞检索
4. **数据根目录硬编码**：所有持久化文件根在 `D:\DesktopPetData\`（`DataPathConfig.cs`），不要散落新路径；新增文件先查数据地图表
5. **反思链路不要回退**：历史曾有死回调（OnReflectRequest 恒 null），现在已接线——改 ChatManager 时保持 SendRequestCoroutine → CheckReflection → DoReflection → CommitReflection 链路
6. **`DriftTowardNeutral()` 无调用者**：如需无交互人格回归，需要显式接入（目前是死代码）
7. **验证方法**：查看 `pet_memory.json` 结构（entries/coreFacts/conversationSummary）与 `pet_personality.json` 五维值；测试后确认无新增测试记忆、totalInteractions 无变化

## 五、第一阶段验证记录

- `build.ps1 -Quick`：宿主用户 `FU\\25295` 编译成功，`Assembly-CSharp.dll` 和 `Assembly-CSharp-Editor.dll` 均重新处理；
- `build.ps1`：完整构建成功，生成最新 `Build/DesktopPet.exe`；
- `node scripts/test/runtime_smoke.cjs --verbose`：通过全部 UI/外置窗口链路，零 `NullReferenceException`，生产记忆 mtime 未变化；
- `@@view:memory` 已纳入隔离运行时冒烟链路，实际打开/返回记忆页通过；
- `MemoryGovernanceTests.cs` 已导入并参与 Editor 测试程序集编译；Tuanjie 本次仍未刷新 `test_results.xml`，因此不把旧的 114/114 计作本轮单元测试结果。
