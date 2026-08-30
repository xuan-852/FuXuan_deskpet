# 工具系统 ToolEngine — 65 工具插件架构与稳定性报告

> **文档作用**: 本模块文档描述桌宠「工具系统」的**代码真相**——IPetTool 插件架构、ToolRegistry 反射自动发现、AsyncToolBase 异步基类、危险工具审批、12 个工具文件 65 个已注册工具的分类清单，以及 2026-08-08 全量稳定性测试报告（57 用例 0 真实 bug）。新增/修改/删除任何 AI 工具前必读。
> **基本架构**: `IPetTool`（接口）+ `ToolSchema`（参数 Schema）→ 实现类（12 个工具文件 + 7 个基础设施 .cs）→ `ToolRegistry`（AppDomain 反射自动发现 + 调度）→ `AsyncToolBase`（异步/协程基类）→ `ChatManager` 调用。云端通过 Function Calling 进入该链路；本地通过 `LocalToolRouter` 输出 JSON 计划后进入同一 `ToolCallInvoker`，不复制第二套工具实现。危险工具清单 `DangerousTools = {file_read, get_clipboard, take_screenshot, file_delete, power, lock_screen, run_command, set_volume, mute, openclaw_task}`，经 `ToolConfirmManager` 审批。关键文件：`Assets/Scripts/ToolEngine/`（20 个 .cs，含测试器 ToolBenchmarkRunner）。
> **开发历史迭代**: N40 起工具数 40+→52→55（新增 Pogget/LaTeX 等）；2026-08-08 `9b94c09` 修复 4 类 benchmark 问题（emoji 误判、DANGER_GUARD 真执行、run_command GBK 崩溃、file_move 链式失败）；55 工具全量测试 44 OK / 6 DANGER_GUARD / 5 SKIP / 2 ERROR（均预期）；2026-08-12 P1 收尾 55→**59**（+OfficeTools 3 个补录 + openclaw_task 补录，任务外包 /task 端点落地）；2026-08-12 P4 新增 3 个偏好工具 → **62**（set_preference / query_preferences / remove_preference，PreferencesManager 配套）；2026-08-12 P5 新增 3 个任务模板工具 → **65**（query_task_templates / save_task_template / remove_task_template，TaskTemplateManager 配套）+ openclaw_task 支持 template/template_args + 轨迹记录（TaskTrajectoryManager，太卜手札）。
> **编写注意事项**: ①测试**禁止空参数遍历调用所有工具**（lock_screen 真锁屏、file_delete 真删文件、set_volume 真改音量），空参测试只限低风险只读白名单（get_system_info/get_mouse_pos）；②新增工具自动被反射发现，无需手动注册；③工具执行须返回中文 ✅/❌ 前缀消息；④危险工具须入 DangerousTools 清单并走 ToolConfirmManager 审批；⑤run_command 输出必须 UTF-8 解码（`chcp 65001`），Unity Mono 无 I18N.CJK 会抛异常。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及工具新增、工具改名、危险工具清单、工具 Schema、工具稳定性验证的改动。
- **回答的问题**:
  - 工具是怎么被发现的？新增工具要做什么？
  - 65 个工具分别在哪几个文件？名字是什么？
  - 哪些是危险工具？审批流程怎么走？
  - 全量工具稳定性测试结果如何？有哪些已知环境限制？
- **关联文档**: `code-truth-architecture.md` 四章（工具系统真相）｜`modules/ai-chat-system.md`（工具子集 T4 注入）｜`modules/bridge-communication.md`（桥接依赖工具）｜`modules/action-agent.md`（动作工具）｜`development-standards.md`（工具新增标准流程）

## 二、基本架构

### 2.1 插件架构

```
IPetTool (接口: ToolName / ToolDescription / ToolParametersJson / IsAsync / Execute / ExecuteAsync)
  ↑ 实现 (12 个工具文件 + 7 个基础设施 + 1 测试器 .cs · 共 65 工具)
ToolRegistry (AppDomain.GetAssemblies 反射自动发现 + 调度)
  ↑ 委托
AsyncToolBase (异步/协程工具基类, ToolName 虚属性)
  ↑ ChatManager 调用 (云端 DoToolLoop / 本地 LocalToolRouter, 最多 10 轮云端回环)
```

### 2.1.1 注册与异步执行边界（2026-08-27）

- `ToolRegistry.Execute()` 与 `ExecuteAsync()` 在未经过场景中 `ToolCallInvoker.Awake()` 时会自动执行一次惰性初始化，避免编辑器测试或独立调用因初始化顺序返回“未知工具”。
- 异步工具的协程创建、推进和 `Current` 读取统一经过异常边界；工具异常转换为 `❌ 施法失败：...` 回调，避免异常直接打断 `ChatManager` 的工具回环。
- 工具本身的危险审批、参数校验和同步/异步分类不在该边界内改变，仍由 `ToolRegistry`、`ToolConfirmManager` 和各工具实现负责。

### 2.2.1 本地工具调用入口

本地模式不依赖 Ollama 的原生 `tools`/`tool_calls` 支持，而使用受控的文本规划协议：

```text
qwen2.5:3b（普通请求）/ qwen3:8b（PDF、Office、OpenClaw、多步骤请求） → {action, tool, arguments, reason}
           → LocalToolRouter 白名单
           → ToolConfirmManager（危险工具）
           → ToolCallInvoker → ToolRegistry → IPetTool
           → 压缩结果 → qwen3:8b 最终回复
```

`LocalToolRouter` 按意图只暴露常用小目录，拒绝未知工具和当前意图之外的工具；
因此本地模型获得“能执行”的能力，但不会获得绕过 Unity 安全层的权限。

### 2.2.2 自然语言工具覆盖（2026-08-25）

- 65 个已注册工具均至少出现在 `LocalToolRouter` 的一个自然语言意图目录中，并同步存在于 `ChatManager.IntentToolMap`；偏好、任务模板、动作复盘/验证、文件读写等此前容易漏路由的工具已补齐关键词和白名单。
- `DesktopPet` 启动时自动挂载 `PreferencesManager` 与 `TaskTemplateManager`，确保 `set/query/remove_preference` 和 `query/save/remove_task_template` 不会因单例未初始化而失效。
- `ToolBenchmarkRunner` 当前定义 65 个用例，其中 10 个 DANGER_GUARD、8 个 SKIP；危险工具只核验注册与危险标记，未执行真实副作用。历史隔离运行记录中的文件写入由 `ToolHelpers.IsPathAllowed` 按 Windows 安全策略拦截，知识库索引因测试目录不存在而返回预期错误，均不属于自然语言路由断链。
- 2026-08-25 的自然语言隔离验收为 5/5：系统信息、文件搜索、打开文件夹、剪贴板、Excel 均命中预期工具；其中剪贴板误路由曾被复现并由“高置信度规则先行”修复。
- 文档/PDF/OpenClaw 任务不再完全依赖本地 3B 模型转述：原始用户需求由 `TryHardenPlanArguments` 确定性回填；`openclaw_bridge.js` 额外限制 LaTeX 请求体 2 MiB、编译器为 xelatex/pdflatex/lualatex，输出路径仅允许配置的数据根目录 `DataPathConfig.DocumentsDir`。
- 规划模型按任务分层：轻量/普通歧义请求使用 `LocalLLMClient.ModelName`（默认 qwen2.5:3b），PDF、Office、OpenClaw 和多步骤请求使用 `LocalLLMClient.ChatModelName`（默认 qwen3:8b）；8B 不可用自动降级 3B，最终执行仍走同一白名单与审批链。

### 2.3 文件清单与工具分类（真实注册名，65 个）

| 文件 | 工具数 | 工具列表 | 类别 |
|------|--------|----------|------|
| `WebSystemTools.cs` | 14 | search_web / open_url / search / open_app / open_folder / get_system_info / lock_screen / set_volume / mute / get_mouse_pos / list_files / notify / run_command / power | 观星/封印/洞观/开阵 |
| `ClipboardFileTools.cs` | 14 | get_clipboard / set_clipboard / get_weather / file_open / file_move / file_copy / file_delete / file_rename / file_info / file_create / dir_create / file_read / search_files / search_file | 传音/摄形/调音/文件 |
| `ReminderAcademicTools.cs` | 8 | set_reminder / query_reminders / mark_reminder_done / delete_reminder / query_exams / query_scores / query_schedule / query_user_status | 卜算记事簿/传讯 |
| `Live2DSyncTools.cs` | 7 | set_expression / play_action / stop_action / inspect_motion_memory / inspect_personality / explore_body / control_body | 演武/表情/动作 |
| `VisionKnowledgeTools.cs` | 5 | take_screenshot / knowledge_search / knowledge_index / openclaw_search / openclaw_task | 摄形/藏书阁 RAG/OpenClaw 搜索+任务外包 |
| `OfficeTools.cs` | 3 | generate_ppt / generate_docx / generate_xlsx（经 OpenClawBridge 调 `/generate_office`，输出 `DataPathConfig.DocumentsDir`） | 办公文档生成 |
| `MotionCoroutineTools.cs` | 5 | generate_motion / explore_body_vision / run_verification / vis_verify / self_review | 异步动作生成(协程)/视觉验证 |
| `PoggetTool.cs` | 1 | launch_pogget（启动 `d:\pogget\Pogget.exe`） | 启动 Pogget |
| `PoggetAgentTool.cs` | 1 | pogget_agent（8 子命令：ping/list_containers/get_container_items/add_to_container/remove_from_container/create_container/organize_desktop/quickpanel_status） | Agent IPC |
| `LatexCompileTool.cs` | 1 | compile_latex（经 OpenClawBridge.CompileLatexAsync，输出 `DataPathConfig.DocumentsDir`） | 问天录 |
| `PreferenceTools.cs` | 3 | set_preference（key/value/source/note）/ query_preferences / remove_preference（偏好结构化存储，P4.2） | 心之所向 |
| `TaskTemplateTools.cs` | 3 | query_task_templates / save_task_template（name/template + 可选 description/category）/ remove_task_template（任务模板库 CRUD，P5.3，TaskTemplateManager 配套） | 太卜阵法图 |

> 📚 **knowledge_index 支持 PDF（2026-08-15）**：`indexExtensions` 已含 `.pdf`。PDF 索引链路：`KnowledgeBaseManager.IndexFile` 检测到 `.pdf` → `OpenClawBridge.ExtractPdfTextAsync`（桥接 `/extract_pdf` 端点）→ 本地 Python `scripts/knowledge/pdf_extract.py`（PyMuPDF 优先，中文 CMap 解码最佳；pypdf 兜底）提取文本层 → 走标准分块/嵌入/存储流程。实测：控制理论.pdf（159 页/17.2 万字符）→ 170 个分块入库，`knowledge_search` 可检索到「PID 控制器」「串级 PID」等原文。⚠️ 扫描版图片 PDF（无文本层）返回 `is_scanned:true`，提示需先 OCR 转文字。依赖：桥接服务器运行中 + Python 已装 PyMuPDF（`py -3.12 -m pip install pymupdf`）。

### 2.4 基础设施（7 个 .cs）

| 文件 | 职责 |
|------|------|
| `IPetTool.cs` | 工具接口 |
| `AsyncToolBase.cs` | 异步工具基类（ToolName 虚属性） |
| `ToolRegistry.cs` | 反射自动发现 + 调度；`DangerousTools = {file_read, get_clipboard, take_screenshot, file_delete, power, lock_screen, run_command, set_volume, mute, openclaw_task}` |
| `ToolSchema.cs` | JSON Schema 构建器 |
| `ToolHelpers.cs` | 工具辅助函数 |
| `ToolConfirmManager.cs` | 危险工具确认管理 |
| `GlmModels.cs` | GLM 模型配置 |

> ⚠️ **代码中不存在的旧文档工具**：`get_time`、`get_pet_status`、`get_system_status`、`show_reminder`、`send_notification`、`write_note`、`messenger`、`write_memory`、`get_memories`、`start_conversation`、`open_web`、`capture_screen`、`reminder_*`（旧文件 MemoryTools/ReminderTools/AcademicTools/KnowledgeTools/BodyTools 均已不存在）。以本表为准。

### 2.5 意图 → 工具白名单映射（ChatManager 侧）

- **chat** / **emotion** → 不发任何 tools
- **command** → launch_pogget、pogget_agent、open_app、open_url、open_folder、search、search_web、openclaw_search、openclaw_task、lock_screen、set_volume、mute、power、get_system_info、get_mouse_pos、list_files、run_command、notify、get_clipboard、set_clipboard、file_*、take_screenshot
- **knowledge** → search_web、search、openclaw_search、openclaw_task、knowledge_search、compile_latex、get_weather、generate_ppt、generate_docx、generate_xlsx、query_*、inspect_*、explore_body*
- **operation** → set_expression、play_action、stop_action、generate_motion、inspect_*、explore_body*、take_screenshot、knowledge_index

### 2.6 危险工具审批流程

`DangerousTools`（10 个：file_read / get_clipboard / take_screenshot / file_delete / power / lock_screen / run_command / set_volume / mute / openclaw_task）→ `ToolConfirmManager` 弹确认 → 用户同意才执行。测试器验证时**只验证 IsDangerous/HasTool 标记，绝不执行**。

> ℹ️ `openclaw_task`（太卜神行法，`VisionKnowledgeTools.cs`）把复杂多步任务外包给 OpenClaw 智能体（浏览器/命令行），经 `OpenClawBridge.ExecuteTaskAndWaitAsync` 提交 + 心跳轮询（默认 300s 无进展判卡死自动取消）。ChatManager 侧有成本熔断：`_openclawTaskFatalSeen`——一旦任务返回不可重试错误（`❌ [不可重试]`），本轮禁止再次调用，防 LLM 换说法反复重试烧 token。参数：task（必填）/ mode（agent/browser）/ timeout_seconds / max_steps / heartbeat_seconds / max_idle_heartbeats / **template**（任务模板名，P5.3 优先于 task，从 `TaskTemplateManager` 展开占位符）/ **template_args**（模板占位符参数 JSON 字符串，如 `{"url":"https://example.com"}`）。P5.2 起执行完毕后自动记录轨迹到 `TaskTrajectoryManager`（task_trajectories.json，成功/失败/耗时/工具步数），下次同类任务提交前经 `BuildReferenceText` 附加「成功经验/失败教训」参考（成功 2 条 + 失败 1 条，referenceCount 计数），供 OpenClaw 借鉴。
> 🎯 **工具步数回传**（2026-08-12 Phase B）：`RecordTrajectory` 签名追加 `int stepCount = 0`（`TaskTrajectoryEntry.stepCount` 字段，0=未知），成功路径传 `OpenClawBridge.LastTaskStepCount`（`ExecuteTaskAndWaitAsync` 结束时由 `ActiveStepCount` 快照）——任务执行了多少步工具调用会沉淀进轨迹库，供后续同类任务参考「这个任务用了几步」。配套 RightPanel 进度显示 + 审批弹窗（见 chat-ui.md）。

## 三、开发历史迭代

| 版本 | 日期 | 变更 |
|------|------|------|
| N20+ | — | 工具集 40+ → 52 个 |
| N39 | 2026-08-02 | 工具数修正 40+→52 |
| N40 | 2026-08-08 | 工具数修正 52→**55**（含 Pogget/LaTeX 等新工具）；T4 工具子集注入（55→27） |
| N40 | 2026-08-08 | 全量稳定性测试提交 `9b94c09`：修复 4 类问题（见下） |
| P1 | 2026-08-12 | 工具数修正 55→**59**：补录 `OfficeTools.cs` 3 个（generate_ppt/docx/xlsx，此前漏登记）+ `VisionKnowledgeTools.cs` 补录 openclaw_task；`/task` 外包端点落地（提交/轮询/取消/心跳），ChatManager knowledge 白名单补 generate_* 三工具；桥接 404 文案补 /generate_office、/task |
| P5 | 2026-08-12 | 工具数 62→**65**：新增 `TaskTemplateTools.cs` 3 个模板工具 + `TaskTemplateManager.cs`（task_templates.json，5 预置模板）+ `TaskTrajectoryManager.cs`（task_trajectories.json，轨迹库）；openclaw_task 支持 template/template_args + 自动轨迹记录与参考文本附加；EditMode 测试 21 个（P5TrajectoryTests） |
| 2026-08-12 | **Phase B 任务可视化**（方案七）：`RecordTrajectory` + `TaskTrajectoryEntry` 追加 `stepCount`（工具步数，0=未知），成功路径回传 `OpenClawBridge.LastTaskStepCount`；RightPanel 进度显示 + 审批弹窗配套（桥接层见 bridge-communication.md，UI 见 chat-ui.md） |
| 2026-08-27 | **ToolRegistry 调度边界收敛**：同步/异步执行入口支持惰性初始化；异步工具协程创建与推进异常统一转为失败回调；快速构建、完整构建和隔离冒烟通过。 |

### 2026-08-08 Benchmark 修复清单（提交 9b94c09）

| # | 问题 | 根因 | 修复 | 验证 |
|---|------|------|------|------|
| 1 | **39 个成功结果误判 ERROR** | Unity Mono `StartsWith` 默认 culture-sensitive，zh-CN 下 `✅`/`🖥️` 前缀被 `StartsWith("❌")` 匹配为 True | 判定改 `StartsWith("❌", StringComparison.Ordinal)` | ERROR 39→2，误判归零 |
| 2 | **DANGER_GUARD 真执行** | 测试器直接调用 `ToolRegistry.Execute` | 改为只验证 `IsDangerous`/`HasTool`，绝不执行 | 6 个危险工具 0ms 全部"仅验证" |
| 3 | **run_command GBK 崩溃** | `Encoding.GetEncoding(936)` Unity Mono 无 I18N.CJK 抛异常 | `chcp 65001` + UTF-8 解码 | whoami 成功返回用户名 |
| 4 | **file_move 链式失败** | file_rename 用例 new_name 无目录拼到根目录 | 改为 `_bench_c.txt` | move 3ms 通过，无残留 |

### 全量测试结果（57 用例）

| 指标 | 结果 |
|------|------|
| 总用例 | 57（55 工具各 1 用例 + run_command 双用例；OfficeTools 3 个经独立端点验证未入 benchmark） |
| 通过（OK） | **44（77%）** |
| 危险拦截（DANGER_GUARD） | 6（10.5%）— 只验证标记，未执行 |
| 跳过（SKIP） | 5（8.8%）— notify 弹窗/launch_pogget 独立进程/compile_latex、vis_verify、run_verification 分钟级流程 |
| 失败（ERROR） | **2（3.5%）** — 均为预期/环境限制 |
| **真实 bug 失败** | **0** |

### 2 个 ERROR 明细（预期/环境限制）

| 工具 | 结果 | 判定 |
|------|------|------|
| `run_command format c:` | `❌ 此术涉及高危操作，需主人亲口确认` | ✅ 预期拦截，高危命令白名单防护生效 |
| `take_screenshot` | 摄形失败（2ms） | ⚠️ 环境限制——后台/无窗口会话无法截屏 |

### 耗时分布（44 个 OK 用例，均值 1361ms / 中位数 10ms）

| 档位 | 数量 | 占比 | 代表 |
|------|------|------|------|
| ⚡ <50ms | 26 | 59% | get_mouse_pos 0ms、query_reminders 0ms、file_* 2-3ms |
| 🔄 50ms–1s | 9 | 20% | open_folder 88ms、query_schedule 504ms、open_app 829ms |
| 🚀 1–5s | 4 | 9% | run_command 1003ms、generate_motion 4029ms |
| 🐢 >5s | 5 | 11% | openclaw_search 11s、search_web 16s（网络/GLM 固有） |

### 2026-08-30 安全加固

- `ToolHelpers.IsPathAllowed` 现在统一拒绝系统/保留目录、重解析点和符号链接路径；文件读取、文件信息、列目录、搜索、打开和重命名均在执行前检查。
- `file_read`、`get_clipboard`、`take_screenshot` 纳入 `DangerousTools`，必须经过用户确认；`ToolBenchmarkRunner` 对三者只验证危险标记，不执行真实读取或截图。
- `ChatManager` 工具参数/结果日志和本地工具结果统一脱敏，敏感工具直接省略内容，普通日志中的 token/password/secret 等字段替换为 `[REDACTED]`。

## 四、编写注意事项

1. **测试禁止空参数遍历调用所有工具**：`lock_screen` 真锁屏、`file_delete` 真删文件、`set_volume` 真改音量、`mute` 真静音、`power` 真关机——**低风险只读白名单**（get_system_info / get_mouse_pos）才可空参测试；剪贴板、文件内容和截图必须显式确认
2. **新增工具三件套**：Python 脚本（如需）→ 桥接端点（curl 验证）→ OpenClawBridge.cs 方法 → ToolEngine 工具类 → `build.ps1 -Quick` → 测试 → 更新文档 → 提交（详见 development-standards.md 第八章）
3. **反射自动发现**：工具类实现 `IPetTool` 即被 `ToolRegistry` 自动注册，无需手动注册表；但**旧文档中的工具名列表不会自动更新**——新增/改名后必须同步更新本文档与 report.md
4. **返回值格式**：成功返回 `✅ <中文描述>`，失败返回 `❌ <中文描述>`（Unity Mono culture-sensitive StartsWith 陷阱已修复，但新判定代码仍须用 `StringComparison.Ordinal`）
5. **危险工具**：必须入 `DangerousTools` 清单；execSync 类调用注意输出编码（run_command 用 `chcp 65001` + UTF-8 解码，勿用 `Encoding.GetEncoding(936)`）
6. **危险命令白名单**：run_command 有高危命令拦截（format c: 等），测试这类用例预期返回「此术涉及高危操作」而非成功
7. **验证方法**：全量稳定性测试用 `ToolBenchmarkRunner`（`.benchmark` 开关触发，真机运行）；测试后清理 `_bench*` 残留、还原剪贴板、回退 pet_memory/personality（备份 `_test_backup_*`）

### 本地模型课表路由补齐（2026-08-22）

- `query_schedule` 已加入本地模型默认回退白名单、无参数工具恢复白名单和关键词触发词。
- `课表`、`课程表`、`课程安排`、`上课`、`课程` 与“查看/查询/今天/第 N 周”等组合会生成受限 `query_schedule` 计划；“第 N 周”会解析为 `{ "week": N }`。
- 明确说“打开/进入/访问/跳转到课表”时，`LocalLLMAgentService` 会直接生成受限 `open_url` 计划，打开 `http://localhost:3000`（`D:\C\小程序\server\src\dashboard.html` 对应的 Web 看板），不让轻量模型在“查询”和“打开网页”之间猜测。
- `open_url` 已加入知识意图白名单，最终仍经过 ChatManager 的意图校验、工具注册和 URL 协议白名单；查询课表仍复用 `ServerPollService → /api/pet/schedule` 的只读链路。
- 已补充 EditMode 路由单测，覆盖课表查询与课表网页打开的分流。
