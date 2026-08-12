# 桌宠任务执行可视化 — 项目方案

> **版本**: v0.1 (2026-08-12)
> **定位**: 「让桌宠成为类 OpenClaw 智能体入口」的实施蓝图——任务进度可见、关键步审批、结果结构化
> **决策记录**: 路线 A（加深集成，非复刻）+ MVP 最小闭环 + 关键步审批 + 结构化回传；进度显示位置 = **思考中头像部位（RightPanel 标题栏状态区）**，非对话气泡
> **依据**: 代码真相核实于 2026-08-12（见 §三）；开发规范见 [`development-standards.md`](development-standards.md)

---

## 一、背景与目标

### 1.1 背景

桌宠目前是「会聊天 + 白名单工具」的形态：`open_app`/`run_command` 被白名单锁死，**操作不了任意程序**。OpenClaw 已集成（桥接 `/task` 外包任务），但用户感知只有「发起 → 等待 → 收到文本结果」三步，中间黑盒。

### 1.2 目标（一句话）

让桌宠能像 OpenClaw 一样自主操作电脑完成任务，且**进度可见（步骤级，含工具名）、关键敏感操作可审批、结果结构化沉淀轨迹**。

### 1.3 已拍板的决策（2026-08-12 用户确认）

| 决策点 | 结论 |
|--------|------|
| 目标形态 | **路线 A：加深集成**——桌宠保持形象/语音/UI/记忆，操作全外包 OpenClaw，不复刻引擎 |
| 首期范围 | **MVP 最小闭环**：进度可见 + 结果回传（2-3 天） |
| 审批粒度 | **关键步审批**：每类敏感操作（exec/写文件/浏览器提交）前确认一次 |
| 结果形态 | **结构化 + 轨迹沉淀**：`{success, artifacts, 耗时, 步骤数}` 进轨迹库 |
| ⭐ 进度显示位置 | **「思考中头像部位」= RightPanel 标题栏状态区**（像素头像旁），**不占用对话气泡**（气泡说话观感差） |

---

## 二、现状代码真相（2026-08-12 实测核实）

### 2.1 桥接层（`code/desktop_unity/openclaw_bridge.js`）

| 项 | 位置 | 结论 |
|----|------|------|
| Gateway 连接 | L14-23 | `GatewayChatClient`（`D:\openclaw\node_modules\openclaw\dist\gateway-chat-BW6uyvQL.js`） |
| 事件回调 | L75-76, L101 | `chatClient.onEvent = handleGatewayEvent`，但 **只处理 `evt.event === 'chat'`** |
| 会话事件订阅 | — | **未调用 `subscribeSessionEvents()`**——`tool.call`/`exec.approval.requested` 等事件收不到 |
| `/task` 端点 | L866 | POST 发起，后台执行，心跳 `lastActivityAt`，取消，错误分类 |
| `/task/{id}` GET | L903 | 返回 `{status, result?, error?, fatal?, lastActivityAt?}`，**无 steps 字段** |
| 404 文案 | L1257 | 需随新端点更新 |

### 2.2 Gateway 事件能力（源码确认）

| 事件 | 含义 | 可行性 |
|------|------|--------|
| `tool.call` | 每次工具调用（含工具名+参数摘要） | ✅ `GatewayChatClient` 声明 `caps:[TOOL_EVENTS]`，`onEvent` 全量转发 |
| `exec.approval.requested` | exec 审批请求 | ✅ Gateway 推送，且有 `exec.approval.resolve` 回执方法（L202-207） |
| `sessions.subscribe` | 订阅会话事件 | ✅ 客户端方法已就绪（L108-110），桥接层**只需调用一次** |

### 2.3 Unity 侧（`Assets/Scripts/`）

| 项 | 位置 | 结论 |
|----|------|------|
| 思考中状态 | `RightPanel.cs:470-485` | 标题栏状态区：像素头像 + 状态点（紫呼吸=思考中/绿=就绪）+「● 思考中…」文字，由 `ChatManager.IsWaiting` 驱动 |
| 日志区思考提示 | `RightPanel.cs:659-662` | 日志区末尾「● 思考中…」 |
| 进度事件通道 | `ChatManager.cs:303,305` | `OnToolCalled(string)` / `OnToolResult(string,string)` 已定义，**RightPanel 未订阅**（L290-291 只订阅 `OnNewReply`/`OnExpressionTag`） |
| 任务外包工具 | `VisionKnowledgeTools.cs:271` | `openclaw_task`，已入危险清单走 `ToolConfirmManager` 审批 |
| 轨迹/模板库 | `TaskTrajectoryManager.cs:66` / `TaskTemplateManager.cs:41` | P5 已落地，可承接结构化结果 |
| 审批交互 | `ToolConfirmManager.cs:13` | 静态类弹窗（点宠物=允许/ESC=拒绝/60s 超时拒绝），可复用样式 |

> **核心结论**：三件事（进度/审批/结构化）都只需改**桥接层 + Unity 两端**，不动 OpenClaw 任何配置，不复刻引擎。改动面完全可控。

---

## 三、总体设计

### 3.1 数据流（目标态）

```mermaid
flowchart LR
    U[用户对桌宠说<br/>下载XX并测试] --> CM[ChatManager]
    CM -->|openclaw_task 工具| BR[openclaw_bridge.js :19876]
    BR -->|POST /task| GW[(OpenClaw Gateway :18789)]
    GW -->|tool.call 事件| BR2[桥接层订阅会话事件<br/>收集 steps[]]
    BR2 -->|GET /task/{id} 返回 steps| UC[Unity 轮询]
    UC -->|显示到标题栏状态区| RP[RightPanel 思考中部位]
    GW -->|exec.approval.requested| BR3[桥接层缓存审批请求]
    BR3 -->|GET /task/{id} 带 pending_approval| CM2[Unity 弹确认]
    CM2 -->|POST /task/{id}/approve| BR4[exec.approval.resolve 回执]
    BR4 --> GW
    GW -->|任务完成| BR5[结构化结果]
    BR5 -->|轨迹沉淀| TM[TaskTrajectoryManager]
```

### 3.2 分端设计

#### A. 桥接层（`openclaw_bridge.js`）

1. **订阅会话事件**：连接后调用 `chatClient.subscribeSessionEvents()`（幂等，失败重连重试）
2. **事件分流**：`handleGatewayEvent` 扩展——
   - `tool.call` → 追加到对应 task 的 `entry.steps[]`（`{tool, summary, ts}`），刷新 `lastActivityAt`
   - `exec.approval.requested` → 存入 `entry.pendingApproval`（`{id, command, args}`），挂起等待回执
   - `chat` → 维持现有逻辑不变
3. **task entry 扩展**：`steps: []`、`pendingApproval: null`、`approvalResolved: false`
4. **`/task/{id}` GET 增强**：返回 `steps[]`、`pendingApproval?`（当有审批请求时）
5. **`/task/{id}/approve` 新端点**（POST，鉴权）：`{decision: allow|deny}` → 调用 `chatClient.resolvePluginApproval(id, decision)` → 回执后清除 `pendingApproval`
6. **404 文案更新**：追加 `/task/{id}/approve`
7. **竞态处理**：任务取消/完成时清理 pending 审批；approve 端点校验 task 存在且确有 pending

#### B. Unity 侧

1. **`OpenClawBridge.cs`**：`PollTaskAsync` 返回值扩展——解析 `steps`/`pendingApproval` 字段（新结构体 `TaskProgressInfo`）
2. **`ChatManager.cs`**：任务进行中时，将步骤通过**新事件** `OnTaskProgress(string stepText)` 广播（复用 `OnToolCalled` 语义，但来源是 OpenClaw 外包任务，避免与本地工具混淆）
3. **`RightPanel.cs` 标题栏状态区（思考中部位）**：
   - 订阅 `OnTaskProgress`；有任务步骤时，状态文字从「● 思考中…」升级为「⚙ 第N步: 工具名 摘要」（字号缩小、省略号截断）
   - 步骤明细写入日志区（kind=2 系统/工具），保持滚动
   - 无任务时维持现有「● 思考中…/● 就绪」逻辑
4. **审批弹窗**：`ChatManager` 检测到 `pendingApproval` → 调用 `ToolConfirmManager` 风格弹窗显示命令 → 允许/拒绝 → 回 `approve` 端点（复用现有审批 UI 交互，60s 超时默认拒绝）
5. **结果沉淀**：任务完成 → 结构化结果（success/artifacts/durationMs/stepsCount）注入 `TaskTrajectoryManager` 新增轨迹

### 3.3 关键设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 进度数据来源 | Gateway `tool.call` 事件（桥接层收集） | 比轮询轨迹文件实时；客户端已带 TOOL_EVENTS 能力 |
| 进度显示位置 | RightPanel 标题栏状态区 + 日志区 | 用户指定「思考中头像部位」，不污染对话气泡 |
| 审批通道 | 复用 `/task` 家族端点 | 不新增独立审批端点面，鉴权/404 一处维护 |
| 审批粒度 | 每类敏感操作首次确认 | `exec.approval.requested` 本身就是按命令来的，天然支持 |
| 轨迹沉淀时机 | 任务完成时 | 结构化结果完整后一次性写入，避免半截轨迹 |

---

## 四、MVP 范围与验收标准

### 4.1 MVP 范围

**做**：
1. 桥接层订阅会话事件 + `tool.call` → `steps[]` 收集
2. `/task/{id}` GET 返回 `steps` / `pendingApproval`
3. `/task/{id}/approve` 新端点 + `exec.approval.resolve` 回执
4. RightPanel 思考中部位显示步骤进度（标题栏状态区 + 日志区）
5. Unity 审批弹窗复用 + 回执
6. 结构化结果 → `TaskTrajectoryManager` 沉淀

**不做**（后置）：离线命令沙箱（路线 B）、浏览器截图回传、任务模板可视化、完全复刻（路线 C 否决）

### 4.2 验收标准（可测试）

1. 对桌宠说「下载 xx 并测试」，RightPanel 思考中部位实时显示 `⚙ 第N步: 工具名 摘要`
2. 遇到 exec/写文件等敏感操作，**执行前弹出确认**（允许/拒绝/超时拒绝），允许后命令才执行
3. 任务结束返回结构化结果，`TaskTrajectoryManager` 新增一条轨迹
4. 取消/失败路径：pending 审批被清理，无悬挂审批
5. 全部测试开测试模式（`.test_mode`），跑完 `clean_test_pollution.cjs` 清理
6. 修改后 `build.ps1 -Quick` 编译通过；桥接层 `node --check` 通过

---

## 五、文件改动清单（预估）

| 文件 | 改动 |
|------|------|
| `code/desktop_unity/openclaw_bridge.js` | 订阅会话事件、事件分流、task entry 扩展、GET 增强、/approve 端点、404 文案 |
| `Assets/Scripts/Bridge/OpenClawBridge.cs`（路径待确认） | PollTask 解析 steps/pendingApproval，新增 approve 调用 |
| `Assets/Scripts/ChatManager.cs` | OnTaskProgress 事件、pendingApproval 处理、审批回执、结果沉淀调用 |
| `Assets/Scripts/UI/RightPanel.cs`（路径待确认） | 标题栏状态区步骤显示、日志区步骤追加 |
| `Assets/Scripts/ToolEngine/...` | ToolConfirmManager 复用或轻量封装审批弹窗 |
| `docs/` | 测试通过后更新 `modules/bridge-communication.md`、`modules/chat-ui.md`、`modules/tool-engine.md` |

> 注：具体文件路径以实际代码为准（上面标注「路径待确认」的项在开工第一步定位）。

---

## 六、风险与铁律

| 风险 | 对策 |
|------|------|
| Gateway 事件协议版本差异 | 桥接层已锁 `GatewayChatClient` 具体版本，改动只增不减，兼容旧字段 |
| 审批挂起导致任务卡死 | 60s 超时默认拒绝 + 任务取消清理 pending |
| 步骤事件风暴（高频 tool.call） | 桥接层按 task 聚合 + 步数上限（如 200 步截断）；Unity 侧节流渲染 |
| 进度显示与「思考中」状态冲突 | 状态区文案分优先级：任务步骤 > 思考中 > 就绪 |

**铁律（来自 AGENTS.md / development-standards.md）**：
1. 测试必须开测试模式（`.test_mode`），测后删 + `clean_test_pollution.cjs`
2. 禁止空参数遍历调用所有工具
3. 预期日志用 `LogAssert.Expect` 声明
4. 密钥不入库；日志/输出禁含 token
5. 桥接改动后 `pm2 restart openclaw-bridge --update-env`，勿手动 kill/start
6. 新端点必须鉴权 + 放 404 前 + 更新 404 文案
7. PS 5.1 写 JSON 带 BOM，Python 读 `utf-8-sig`
8. 编码遵循 `.editorconfig`（`.cs`/`.ps1`/`.cmd` 带 BOM，其余无 BOM）

---

## 七、实施顺序（MVP）

| 步骤 | 内容 | 验证 |
|------|------|------|
| 1 | 桥接层订阅会话事件 + tool.call 收集 steps | `node --check` + curl 观察日志 |
| 2 | GET /task/{id} 返回 steps/pendingApproval | curl 带真实任务验证 |
| 3 | /task/{id}/approve 端点 + resolve 回执 | curl 模拟审批 |
| 4 | Unity 端 PollTask 解析 + OnTaskProgress | `build.ps1 -Quick` |
| 5 | RightPanel 思考中部位步骤显示 | 手动跑任务看效果 |
| 6 | 审批弹窗 + 回执闭环 | 端到端测试 |
| 7 | 结构化结果 → 轨迹库 | 测试 + 检查轨迹文件 |
| 8 | 全量测试 + 更新模块文档 | `build.ps1 -RunTests` + 文档四要素更新 |

> **AI 铁则**：每一步代码改动后立即验证（编译/curl/测试），**全部测试通过后才更新 md 模块文档**（文档只记录已验证的代码真相）。
