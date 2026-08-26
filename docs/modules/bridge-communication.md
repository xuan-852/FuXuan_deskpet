# 桥接通信 — C# ↔ Node.js ↔ Python 三层链路

> **文档作用**: 本模块文档描述桌宠「通信桥接」子系统的**代码真相**——Unity C#（`OpenClawBridge.cs`）↔ Node.js 桥接服务器（`openclaw_bridge.js`，:19876）↔ OpenClaw Gateway（WebSocket :18789）↔ Python 脚本（办公/LaTeX 生成）的完整链路、端点契约、鉴权方式、超时与健壮性机制。**改任何端点/工具/通信相关代码前必读**。
> **基本架构**: `OpenClawBridge.cs`（静态类，UnityWebRequest HTTP JSON）→ `openclaw_bridge.js`（Node http server，鉴权 `x-bridge-token`，请求 per-session 锁，任务心跳）→ ①WebSocket→OpenClaw Gateway（:18789，GATEWAY_TOKEN 自动从 openclaw.json 读取）②execSync→Python 脚本（临时 JSON 传参）。端点：`/health`/`/search`（GET）/`/task` 系列（POST+轮询+审批）/`/compile_latex`（POST）/`/generate_office`（POST）。端口 19876，PM2 管理（进程名 `openclaw-bridge`）。
> **开发历史迭代**: run#7 修复并发响应错位（请求串行锁）；8/5 认证失败（GATEWAY_TOKEN 自动轮换读取）；8/7 BOM 导致 JSON.parse 失败（strip BOM）；任务不可重试错误分类（烧 token 元凶修复）；2026-08-12 Phase A 新增 `/generate_office` 端点（Python 办公生成器链路）；2026-08-12 Phase B 任务可视化：实时事件订阅（tool/item/approval）→ steps 收集去重 + `GET /task/{id}` 返回 `steps`/`pendingApproval` + `POST /task/{id}/approve` 审批回执；2026-08-12 Phase C 并行化（per-session 锁）+ exec 审批打通（`exec.approval.resolve`）。
> **编写注意事项**: ①新端点必须鉴权（x-bridge-token）+ 放 404 前 + 更新 404 文案；②返回统一 `{success:bool,...}` 或 `{success:false,error:"..."}`；③PM2 进程勿手动 kill/start，改桥接后 `pm2 restart openclaw-bridge --update-env`；④PS 5.1 写 JSON 带 BOM，Python 读文件用 `utf-8-sig`；⑤JS 2 空格缩进，请求体必须 `Buffer.concat` 收集（body += chunk 会截断中文）；⑥密钥不入库，日志禁含 Token。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及桥接端点新增/修改、Unity 侧调用方法、Python 脚本接入、鉴权配置、超时调整的改动。
- **回答的问题**:
  - 通信链路长什么样？每个环节用什么协议？
  - 有哪些端点？各自参数/鉴权/超时是什么？
  - Token 怎么配置的？轮换机制是什么？
  - 任务系统（/task）怎么工作？心跳/熔断怎么做的？
- **关联文档**: `development-standards.md` 二章（组件通信规范，唯一权威）｜`modules/tool-engine.md`（依赖桥接的工具）｜`modules/office-tools.md`（办公生成链路）｜`code-truth-architecture.md` 6.3（OpenClawBridge 真相）

## 二、基本架构

### 2.1 通信链路总览

```
C# (OpenClawBridge.cs) --HTTP JSON, x-bridge-token--> openclaw_bridge.js (:19876)
                                                        |--WebSocket--> OpenClaw Gateway (:18789, GATEWAY_TOKEN)
                                                        |--execSync 临时JSON--> Python scripts (scripts/office/ 等)
```

### 2.2 鉴权与配置

| 项 | 值 | 说明 |
|----|-----|------|
| `BRIDGE_PORT` | 19876 | env `BRIDGE_PORT`，默认 19876 |
| `BRIDGE_TOKEN` | env `BRIDGE_TOKEN` || GATEWAY_TOKEN | Unity 必须带 `x-bridge-token` 头 |
| `GATEWAY_TOKEN` | env `GATEWAY_TOKEN` || 自动从 `~/.openclaw/openclaw.json` 读取（strip BOM） | 跟随 gateway token 轮换，避免 8/5 认证失败 |
| `GATEWAY_URL` | `ws://127.0.0.1:18789` | OpenClaw Gateway WebSocket |
| `SESSION_KEY` | `agent:main:main` | Gateway 会话键 |
| `CHAT_TIMEOUT_MS` | 180000 | 默认聊天超时 |

> C# 侧 `BridgeToken` 只读环境变量 `BRIDGE_TOKEN`，**无内置默认值**（历史内置 Token 已泄漏轮换），缺失时返回空串并告警禁用桥接。

### 2.3 端点契约（代码真相）

| 端点 | 方法 | 鉴权 | 参数 | 响应 | 超时 |
|------|------|------|------|------|------|
| `/health` | GET | ✅（实际所有请求均需 token，见下方注） | — | `{status: ok/error, connected, error}` | 3s |
| `/search` | GET | ✅ | `?q=` | `{success, query, response, elapsed_ms}` | 180s |
| `/task` | POST | ✅ | `{task, mode?, timeoutMs?, maxSteps?}` | `{success, task_id, status}` | 立即返回 |
| `/task/{id}` | GET | ✅ | — | `{success, task_id, status, result?, error?, fatal?, lastActivityAt?, steps?, pendingApproval?}` | 轮询 |
| `/task/{id}/cancel` | POST | ✅ | — | `{success, task_id, status: cancelled}` | — |
| `/task/{id}/approve` | POST | ✅ | `{decision: allow-once/allow-always/deny}` | `{success, task_id, decision, status}` | — |
| `/compile_latex` | POST | ✅ | `{source?, output_path?, compiler?, title?, pin_to_desktop?, description?}` | `{success, pdf_path?, tex_path?, error?}` | C# 1800s |
| `/generate_office` | POST | ✅ | `{type: ppt/docx/xlsx, description, title?, theme?}` | `{success, path?, title?, folder_path?, error?}` | C# 300s |
| `/extract_pdf` | POST | ✅ | `{path, max_chars?}` | `{success, text?, pages?, chars?, is_scanned?, error?}` | C# 180s |

> ⚠️ `/health` 鉴权注：代码中鉴权检查（`authToken !== BRIDGE_TOKEN`）在路径分发**之前**，即 `/health` 实际也要求 `x-bridge-token`（实测 401）——与早期文档「免鉴权」描述不符，已按代码真相修正。
> ⚠️ `steps` 字段：工具调用轨迹数组 `[{tool, summary, ts}]`，桥接层实时事件（`stream: "tool"` phase start/result）按 `toolCallId` 去重收集，上限 `MAX_TASK_STEPS=200`。
> ⚠️ `pendingApproval` 字段：`{kind, id, slug, command, cwd, host, createdAtMs, expiresAtMs}`，`kind` ∈ `exec`（exec.approval.requested 独立事件）/ `plugin`（agent 事件 approval 流）；来自 Gateway `stream: "approval"` 事件（phase requested/resolved）与 `exec.approval.requested` 独立事件；任务完成/取消后自动清空。
> ⚠️ 失败统一返回 `{success:false, error:"..."}`；404 兜底文案：`{error: 'Not found. Use /search?q=, /compile_latex, /generate_office, /extract_pdf, /task[...], or /health'}`。

### 2.4 健壮性机制（openclaw_bridge.js）

| 机制 | 说明 |
|------|------|
| **请求锁** `requestChains`（per-session） | run#7 曾因并发请求互相覆盖 waiter 发生响应错位，当时用全局 `requestChain` 串行；2026-08-12 升级为 per-sessionKey 锁 Map：同一 sessionKey 串行、不同 sessionKey 并行（任务用独立 sessionKey `agent:main:task-<id>`，多任务实测并行启动差 88ms）；`sendChatAndWait(query, timeoutMs, onActivity, sessionKey)` 按 sessionKey 取/放锁 |
| **任务心跳** `lastActivityAt` | 任何 chat 中间事件（工具调用/增量输出/进度汇报）都算 agent 活跃；桌宠轮询「有进展就重置，连续无进展才熔断」 |
| **不可重试错误分类** `classifyTaskError` | 连接类/超时类/元数据异常 → `fatal: true`；C# 侧见 `FATAL_PREFIX = "❌ [不可重试]"`，LLM 不再换说法反复重调（烧 token 元凶） |
| **GATEWAY_TOKEN 自动轮换** | 优先 env，否则读 openclaw.json（strip BOM 防 JSON.parse 失败） |
| **BOM 防护** | PowerShell Set-Content 写 BOM → `.replace(/^\uFEFF/, '')`（8/7 复现根因） |
| **任务预算** | `buildTaskPrompt` 步骤预算（maxSteps 默认 20）+ 长任务心跳汇报规则 |
| **CORS 关闭** | 不设 CORS 头——浏览器跨域读不到，Unity 原生客户端不受影响 |
| **实时事件订阅** | Gateway WS `evt.event === 'agent'` → 按 `payload.stream` 分支：`"tool"`（phase:start/result, name, toolCallId, args, meta）、`"item"`（phase:start/end/update, itemId, kind, title, toolCallId）、`"approval"`（phase:requested/resolved, approvalId, approvalSlug, command, host, title）；`tool.call` 是轨迹导出事件名，实时不推送 |
| **steps 去重** | `seenToolCalls` Set 按 `toolCallId` 去重——同一工具调用 start+result 只记一条，避免重复步骤 |
| **审批回执** | `POST /task/{id}/approve` 校验 decision ∈ {allow-once, allow-always, deny} 后按 `pendingApproval.kind` 选决议 API：`kind='exec'` → `exec.approval.resolve`（RPC，`chatClient.client.request`），失败回退 plugin 通道；`kind='plugin'` → `resolvePluginApproval`（=`plugin.approval.resolve`），失败回退 exec 通道。⚠️ 2026-08-12 E2E 实测教训：exec 审批必须走 `exec.approval.resolve`，`plugin.approval.resolve` 不认识 exec 审批 id（报 `unknown or expired approval id`）；触发条件：openclaw.json `tools.exec.mode = "ask"` + security allowlist（**2026-08-12 已配置生效**） |
| **取消任务防崩溃** `cancelTask()` | ⚠️ 2026-08-13 修复：原实现直接 `w.reject(new Error('Task cancelled'))`，若 `sendChatAndWait` 尚未 `await responsePromise`（还停在 `chatClient.client.request` 内），reject 先于 catch 注册 → Node v15+ unhandled rejection 默认崩溃 → PM2 重启 14+ 次（search_web 失败叠加根因）。修复：`setImmediate(() => w.reject(...))` 延迟到当前微任务/宏任务栈跑完后再 reject，确保 catch 已挂上 |
| **断线自动重连** | ⚠️ 2026-08-13 新增：原 `onDisconnected` 只置 `connected=false`，无自动重连（仅靠 PM2 兜底）。修复：`reconnecting` 标志防重连风暴 + 断线后 1s 重连，失败 5s 后再试；waiter reject 同样 `setImmediate` 包装防 unhandled rejection |

> **LaTeX 安全边界（2026-08-25）**：`/compile_latex` 请求体上限 2 MiB；编译器仅允许 `xelatex`/`pdflatex`/`lualatex`；自定义 `output_path` 必须位于 `D:\DesktopPetData\Documents`，阻止模型参数越权写文件；AI 生成源码的外部引用检查不允许通过 `..` 越出编译目录。

### 2.5 C# 侧方法（OpenClawBridge.cs，静态类）

| 方法 | 端点 | 超时 | 返回 |
|------|------|------|------|
| `SearchWebAsync(query, 180)` | `/search` | 180s | 文本结果或 ❌ 消息 |
| `CheckHealthAsync()` | `/health` | 3s | bool |
| `CompileLatexAsync(source, outputPath, compiler, title, pinToDesktop, description)` | `/compile_latex` | 1800s | JSON 文本 |
| `GenerateOfficeAsync(type, description, title, theme)` | `/generate_office` | 300s | 完整 raw JSON |
| `ExtractPdfTextAsync(pdfPath, maxChars=500000)` | `/extract_pdf` | 180s | JSON 文本（含 text/pages/chars；扫描版带 is_scanned:true） |
| `ExecuteTaskAndWaitAsync(task, mode, maxSteps)` | `/task` 提交+轮询 | 心跳熔断 | 任务结果文本；每轮轮询调 `RefreshTaskProgress` 更新进度状态 |
| `RefreshTaskProgress(obj)` | `/task/{id}` 轮询解析 | — | 解析 `steps` JArray → `ActiveStepCount`/`ActiveStepLabel`（`第n步: tool summary`，summary 去换行截 48 字符）；解析 `pendingApproval` → `PendingApproval`（id 变化才刷新） |
| `ApproveTaskAsync(taskId, decision)` | `/task/{id}/approve` | — | 校验 decision ∈ 三值后 POST；设置 `LastApprovalOk`/`LastError` |
| 任务系列（IsBusy/LastTaskId/LastTaskWasFatal/HasActiveTask/ActiveTaskId/ActiveStepCount/ActiveStepLabel/LastTaskStepCount/PendingApproval/LastApprovalOk） | `/task` | — | 轮询状态 + 实时进度 + 审批回执（后台 Task.Run 线程写，主线程 OnGUI 只读的原子字段） |

`OpenClawBridge` 的公共错误边界由 `GetResponseError()` 和 `ErrorJson()` 统一处理：HTTP 失败优先读取 Node 返回的 `error` 字段；需要返回 JSON 的路径统一使用 Newtonsoft.Json 序列化，避免错误文本中的引号、换行或中文破坏 JSON 契约。该边界覆盖搜索、健康检查、LaTeX、办公、PDF、任务轮询/取消/审批等调用。

## 三、开发历史迭代

| 日期 | 变更 |
|------|------|
| run#7 | 修复并发响应错位：请求串行锁 `requestChain`（Gateway 事件无法按 runId 匹配 waiter） |
| 8/5 | GATEWAY_TOKEN 认证失败：改为自动从 openclaw.json 读取（跟随轮换），strip BOM |
| 8/7 | BOM 导致 JSON.parse 失败（PowerShell Set-Content）→ `.replace(/^\uFEFF/, '')` |
| — | 任务不可重试错误分类（fatal）+ C# 侧 FATAL_PREFIX，修复 LLM 反复重调烧 token |
| 2026-08-12 | **Phase A 办公工具链**：新增 `/generate_office` 端点 + `GenerateOfficeAsync` + 三 Python 生成器（ppt/docx/xlsx） |
| 2026-08-12 | **Phase B 任务可视化（OpenClaw 类智能体入口）**：实时事件订阅（tool/item/approval）→ steps 去重收集（seenToolCalls）→ `GET /task/{id}` 返回 `steps`/`pendingApproval` → `POST /task/{id}/approve` 审批回执（decision ∈ allow-once/allow-always/deny）→ C# 侧 `OpenClawBridge` 新增 ActiveStepCount/ActiveStepLabel/ActiveTaskId/PendingApproval/LastApprovalOk 等静态原子属性 + `RefreshTaskProgress`/`ApproveTaskAsync` → 实测 steps=2 干净输出（echo step1/step2） |
| 2026-08-12 | **并行化 + exec 审批打通**：全局 `requestChain` → per-session `requestChains`（同 sessionKey 串行、跨 sessionKey 并行，多任务实测差 88ms；任务独立 sessionKey `agent:main:task-<id>`）→ `pendingApproval` 加 `kind` 标记（exec/plugin）→ 审批决议按 kind 选 API（exec→`exec.approval.resolve` / plugin→`plugin.approval.resolve`）→ 配置 `tools.exec.mode=ask` → E2E 实测：提交 hostname 任务 → pendingApproval(kind=exec) 到达 bridge → approve 回执 success:true → 任务 done 且返回 hostname 输出（此前回执失败 `unknown or expired approval id`，根因：exec 审批误用 plugin.approval.resolve） |
| 2026-08-15 | **PDF 文本提取端点**：新增 `/extract_pdf`（POST，`{path, max_chars?}`）+ `ExtractPdfTextAsync` + Python 脚本 `scripts/knowledge/pdf_extract.py`（双引擎：PyMuPDF 优先——中文内嵌子集字体 CMap 解码最佳；pypdf 兜底）。供「藏书阁」knowledge_index 索引 PDF。实测：控制理论.pdf 159 页 17.2 万字符提取成功，中文完整。扫描版 PDF（无文本层）返回 `is_scanned:true` |
| 2026-08-17 | **修复 `/task` 首次连接无响应**：Gateway 未连接时，旧实现只等待 `connect_()` 的失败回调，连接成功后未继续创建任务或发送 `task_id`，导致 C# 提交请求超时；改为 `await connect_()`，成功后继续入队，失败仍返回 503。 |
| 2026-08-25 | **PDF/本地模型保护**：本地高置信度工具规则前置；文档与 OpenClaw 任务回填用户原始需求；`/compile_latex` 增加请求体、编译器、输出目录和外部引用越界保护。 |
| 2026-08-26 | **C# 桥接错误契约收敛**：统一透传 Node 结构化错误并用 JSON 序列化生成失败响应；新增本地测试覆盖引号、换行、中文和 `is_scanned` 标志，避免错误响应非法化。 |

## 四、编写注意事项

1. **新增端点标准流程**：Python 脚本 → 桥接端点（**curl 验证**）→ OpenClawBridge.cs 方法 → ToolEngine 工具类 → `build.ps1 -Quick` → 测试 → 更新文档 → 提交（development-standards.md 第九章）
2. **新端点三必须**：必须鉴权（x-bridge-token）+ 必须放在 404 兜底**之前** + 必须更新 404 文案（当前 404 文案遗漏 /generate_office）
3. **统一返回契约**：成功 `{success:true, ...}`；失败 `{success:false, error:"..."}`；不要返回裸文本（除 /search 的 response 字段）
4. **PM2 铁律**：进程勿手动 kill/start（EADDRINUSE）；改桥接后 `pm2 restart openclaw-bridge --update-env`
5. **请求体收集**：Node 端必须 `Buffer.concat` 收集 body（`body += chunk` 会按 TCP 块截断中文）
6. **BOM 坑**：PS 5.1 `Out-File -Encoding utf8` 写 BOM → Python 读 JSON 用 `utf-8-sig`；JS 读配置 strip BOM
7. **Token 安全**：密钥只从环境变量读取（模板用 .example）；日志/输出禁含 Token；内置默认 Token 仅兜底并告警
8. **验证命令**：`node --check code/desktop_unity/openclaw_bridge.js`（JS 语法）；`curl http://127.0.0.1:19876/health`（健康检查）
9. **超时选择**：search 180s / compile_latex 1800s（分块生成 10-20 分钟）/ generate_office 300s（AI 组织 + 本地渲染 10-60s）/ extract_pdf 180s（大 PDF 提取可能超 60s）；新端点按实际耗时给足余量
