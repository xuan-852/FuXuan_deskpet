# 桥接通信 — C# ↔ Node.js ↔ Python 三层链路

> **文档作用**: 本模块文档描述桌宠「通信桥接」子系统的**代码真相**——Unity C#（`OpenClawBridge.cs`）↔ Node.js 桥接服务器（`openclaw_bridge.js`，:19876）↔ OpenClaw Gateway（WebSocket :18789）↔ Python 脚本（办公/LaTeX 生成）的完整链路、端点契约、鉴权方式、超时与健壮性机制。**改任何端点/工具/通信相关代码前必读**。
> **基本架构**: `OpenClawBridge.cs`（静态类，UnityWebRequest HTTP JSON）→ `openclaw_bridge.js`（Node http server，鉴权 `x-bridge-token`，请求串行锁，任务心跳）→ ①WebSocket→OpenClaw Gateway（:18789，GATEWAY_TOKEN 自动从 openclaw.json 读取）②execSync→Python 脚本（临时 JSON 传参）。端点：`/health`（免鉴权）/`/search`（GET）/`/task` 系列（POST+轮询）/`/compile_latex`（POST）/`/generate_office`（POST）。端口 19876，PM2 管理（进程名 `openclaw-bridge`）。
> **开发历史迭代**: run#7 修复并发响应错位（请求串行锁）；8/5 认证失败（GATEWAY_TOKEN 自动轮换读取）；8/7 BOM 导致 JSON.parse 失败（strip BOM）；任务不可重试错误分类（烧 token 元凶修复）；2026-08-12 Phase A 新增 `/generate_office` 端点（Python 办公生成器链路）。
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
| `/health` | GET | **免鉴权** | — | `{status: ok/error, connected, error}` | 3s |
| `/search` | GET | ✅ | `?q=` | `{success, query, response, elapsed_ms}` | 180s |
| `/task` | POST | ✅ | `{task, mode?, timeoutMs?, maxSteps?}` | `{success, task_id, status}` | 立即返回 |
| `/task/{id}` | GET | ✅ | — | `{success, task_id, status, result?, error?, fatal?, lastActivityAt?}` | 轮询 |
| `/task/{id}/cancel` | POST | ✅ | — | `{success, task_id, status: cancelled}` | — |
| `/compile_latex` | POST | ✅ | `{source?, output_path?, compiler?, title?, pin_to_desktop?, description?}` | `{success, pdf_path?, tex_path?, error?}` | C# 1800s |
| `/generate_office` | POST | ✅ | `{type: ppt/docx/xlsx, description, title?, theme?}` | `{success, path?, title?, folder_path?, error?}` | C# 300s |

> ⚠️ 失败统一返回 `{success:false, error:"..."}`；404 兜底文案：`{error: 'Not found. Use /search?q=, /compile_latex, or /health'}`（**尚未包含 /generate_office，Phase A 遗留待更新**）。

### 2.4 健壮性机制（openclaw_bridge.js）

| 机制 | 说明 |
|------|------|
| **请求串行锁** `requestChain` | Gateway 事件只带 sessionKey、无法按 runId 匹配，并发请求会互相覆盖 waiter（run#7 曾发生响应错位）——串行化无性能损失（逐节生成本串行） |
| **任务心跳** `lastActivityAt` | 任何 chat 中间事件（工具调用/增量输出/进度汇报）都算 agent 活跃；桌宠轮询「有进展就重置，连续无进展才熔断」 |
| **不可重试错误分类** `classifyTaskError` | 连接类/超时类/元数据异常 → `fatal: true`；C# 侧见 `FATAL_PREFIX = "❌ [不可重试]"`，LLM 不再换说法反复重调（烧 token 元凶） |
| **GATEWAY_TOKEN 自动轮换** | 优先 env，否则读 openclaw.json（strip BOM 防 JSON.parse 失败） |
| **BOM 防护** | PowerShell Set-Content 写 BOM → `.replace(/^\uFEFF/, '')`（8/7 复现根因） |
| **任务预算** | `buildTaskPrompt` 步骤预算（maxSteps 默认 20）+ 长任务心跳汇报规则 |
| **CORS 关闭** | 不设 CORS 头——浏览器跨域读不到，Unity 原生客户端不受影响 |

### 2.5 C# 侧方法（OpenClawBridge.cs，静态类）

| 方法 | 端点 | 超时 | 返回 |
|------|------|------|------|
| `SearchWebAsync(query, 180)` | `/search` | 180s | 文本结果或 ❌ 消息 |
| `CheckHealthAsync()` | `/health` | 3s | bool |
| `CompileLatexAsync(source, outputPath, compiler, title, pinToDesktop, description)` | `/compile_latex` | 1800s | JSON 文本 |
| `GenerateOfficeAsync(type, description, title, theme)` | `/generate_office` | 300s | 完整 raw JSON |
| 任务系列（IsBusy/LastTaskId/LastTaskWasFatal） | `/task` | — | 轮询状态 |

## 三、开发历史迭代

| 日期 | 变更 |
|------|------|
| run#7 | 修复并发响应错位：请求串行锁 `requestChain`（Gateway 事件无法按 runId 匹配 waiter） |
| 8/5 | GATEWAY_TOKEN 认证失败：改为自动从 openclaw.json 读取（跟随轮换），strip BOM |
| 8/7 | BOM 导致 JSON.parse 失败（PowerShell Set-Content）→ `.replace(/^\uFEFF/, '')` |
| — | 任务不可重试错误分类（fatal）+ C# 侧 FATAL_PREFIX，修复 LLM 反复重调烧 token |
| 2026-08-12 | **Phase A 办公工具链**：新增 `/generate_office` 端点 + `GenerateOfficeAsync` + 三 Python 生成器（ppt/docx/xlsx） |

## 四、编写注意事项

1. **新增端点标准流程**：Python 脚本 → 桥接端点（**curl 验证**）→ OpenClawBridge.cs 方法 → ToolEngine 工具类 → `build.ps1 -Quick` → 测试 → 更新文档 → 提交（development-standards.md 第九章）
2. **新端点三必须**：必须鉴权（x-bridge-token）+ 必须放在 404 兜底**之前** + 必须更新 404 文案（当前 404 文案遗漏 /generate_office）
3. **统一返回契约**：成功 `{success:true, ...}`；失败 `{success:false, error:"..."}`；不要返回裸文本（除 /search 的 response 字段）
4. **PM2 铁律**：进程勿手动 kill/start（EADDRINUSE）；改桥接后 `pm2 restart openclaw-bridge --update-env`
5. **请求体收集**：Node 端必须 `Buffer.concat` 收集 body（`body += chunk` 会按 TCP 块截断中文）
6. **BOM 坑**：PS 5.1 `Out-File -Encoding utf8` 写 BOM → Python 读 JSON 用 `utf-8-sig`；JS 读配置 strip BOM
7. **Token 安全**：密钥只从环境变量读取（模板用 .example）；日志/输出禁含 Token；内置默认 Token 仅兜底并告警
8. **验证命令**：`node --check code/desktop_unity/openclaw_bridge.js`（JS 语法）；`curl http://127.0.0.1:19876/health`（健康检查）
9. **超时选择**：search 180s / compile_latex 1800s（分块生成 10-20 分钟）/ generate_office 300s（AI 组织 + 本地渲染 10-60s）；新端点按实际耗时给足余量
