# AGENTS.md — 符玄桌宠项目 AI 协作指南

> 本文件是 AI 编码代理（GitHub Copilot / Claude Code 等）的**快速入口**。
> 详细规范见 [`docs/development-standards.md`](docs/development-standards.md)（两者冲突时以详细版为准）。
> 📚 **文档总索引**：[`docs/README.md`](docs/README.md)——顶层权威文档表 + 10 个模块文档（`docs/modules/`）。
>
> **AI 文档加载规则**：本文件是唯一的 AI 入口。默认只读当前任务包（若有）、其 `primaryGuide`、直接链接的决策/真相文档、目标文件及直接依赖；通过 [`docs/generated/document-map.md`](docs/generated/document-map.md) 发现文档，不把 `docs/README.md` 当作每次任务的全文必读材料。专项文档按触发条件懒加载，具体规则见下方“AI 上下文边界”。

## 项目是什么

Unity（团结引擎 Tuanjie 2022.3.62t7）+ Live2D 的 Windows 桌面 AI 伴侣「符玄」，具备感知-决策-执行-记忆闭环，内置 63 个工具，通过本地 Node.js 桥接调用 OpenClaw AI 与 Python 办公生成器。

## 技术栈速览

| 层 | 位置 | 语言 |
|----|------|------|
| Unity 桌宠 | `code/desktop_unity/Assets/Scripts/` | C#（122 文件，架构见 `docs/code-truth-architecture.md`） |
| 桥接服务器 | `code/desktop_unity/openclaw_bridge.js` | Node.js，端口 19876，PM2 管理（进程名 `openclaw-bridge`） |
| 工具系统 | `Assets/Scripts/ToolEngine/` | `IPetTool` → `AsyncToolBase` → 反射自动发现，危险工具需审批 |
| Python 生成器 | `scripts/office/` 等 | python-pptx / python-docx / openpyxl |
| 文档 | `docs/` | 总索引 = `docs/README.md`；权威架构 = `code-truth-architecture.md`；模块细节 = `docs/modules/`（10 份，见下表） |

## 模块文档（docs/modules/，每份含「作用/架构/迭代/注意」四要素）

| 模块 | 文档 | 一句话定位 |
|------|------|-----------|
| AI 对话 | [`docs/modules/ai-chat-system.md`](docs/modules/ai-chat-system.md) | ChatManager 轮环、注入链、Token 优化 |
| 工具系统 | [`docs/modules/tool-engine.md`](docs/modules/tool-engine.md) | 63 工具、审批、benchmark |
| 动作系统 | [`docs/modules/action-agent.md`](docs/modules/action-agent.md) | 决策循环、动作验证闭环 |
| Live2D 渲染 | [`docs/modules/live2d-rendering.md`](docs/modules/live2d-rendering.md) | 渲染管线、参数映射、硬编码迁移 |
| 对话界面 | [`docs/modules/chat-ui.md`](docs/modules/chat-ui.md) | IMGUI 界面、像素化优化 |
| 桥接通信 | [`docs/modules/bridge-communication.md`](docs/modules/bridge-communication.md) | C# ↔ Node 桥 ↔ Gateway/Python |
| 记忆人格 | [`docs/modules/memory-personality.md`](docs/modules/memory-personality.md) | 三层记忆、五维人格、知识库 |
| 编码协议 | [`docs/modules/encoding-protocol.md`](docs/modules/encoding-protocol.md) | 全仓 UTF-8、BOM 坑、乱码排查 |
| 办公工具 | [`docs/modules/office-tools.md`](docs/modules/office-tools.md) | PPT/Word/Excel 生成链路 |
| 运行时状态 | [`docs/modules/runtime-readiness.md`](docs/modules/runtime-readiness.md) | 启动自检、请求状态、停止恢复、云端保护 |

## 构建 / 验证命令（改完必跑）

```powershell
.\build.ps1 -Quick        # C# 编译验证（每次改 C# 后）
.\build.ps1               # 完整构建 → Build/DesktopPet.exe
.\build.ps1 -RunTests     # Unity Editor 测试（EditMode）
node --check code/desktop_unity/openclaw_bridge.js   # 桥接 JS 语法
```

> **构建权限提示**：Tuanjie/Unity 构建需要本机/full-access 权限（授权服务、原生编译器和构建子进程）。受限沙箱中可能表现为无 C# 错误但外层脚本卡住或无新日志；此时应申请本机权限后重跑，并以本次新生成的日志和 `[OK] Build succeeded!` 判断，不要把无输出直接当成代码失败。

## 通信架构（改端点/工具前必读）

```
C# (OpenClawBridge.cs) --HTTP JSON, x-bridge-token--> openclaw_bridge.js (:19876)
                                                        |--WebSocket--> OpenClaw Gateway (:18789)
                                                        |--execSync 临时JSON--> Python scripts
```

- 端点：`/health`、`/search`、`/task` 系列、`/compile_latex`、`/generate_office`、`/extract_pdf`（除 `/health` 外均经过 `x-bridge-token` 鉴权）；办公输出使用 `DataPathConfig.DocumentsDir`（默认 `%LOCALAPPDATA%\FuXuan\DesktopPetData\Documents\`，可由 `FU_XUAN_DATA` 覆盖），成功后自动打开
- 新端点必须鉴权 + 放 404 前 + 更新 404 文案；返回 `{success: bool, ...}` 或 `{success:false,error:"..."}`

## 铁律（违反会出事故）

1. **测试必须无记忆隔离**：优先用 `FU_XUAN_DATA` 指向临时目录启动桌宠（`node scripts/test/runtime_smoke.cjs` 已内置：隔离数据目录 + 生产记忆 mtime 零污染断言）；手动测试时至少建空文件 `.test_mode`（防污染 pet_memory/pet_personality/motion_memory/activity/validation），测后删 + 如污染则 `node scripts/backup_memory.cjs --all` 留底并清理。测试前可先备份：`node scripts/backup_memory.cjs`（保留生产记忆至少一份安全副本）
2. **禁止空参数遍历调用所有工具**：`lock_screen` 真锁屏、`file_delete` 真删文件、`set_volume` 真改音量；空参测试只限低风险只读白名单（`get_system_info`/`get_mouse_pos`）。剪贴板、文件内容和截图工具必须显式确认
3. **测试中日志**：预期日志用 `LogAssert.Expect(LogType.Warning, "...")` 声明（Unity 把 Error/Warning 计为失败）
4. **UI 测试不靠模拟鼠标点击**：坐标难定位、视觉模型不可靠。必须用终端链路触发——写 `DataPathConfig.InboxFile`（测试模式启用，手动测试可用 `FU_XUAN_DATA` 指向临时目录）：`@@view:settings|reminders|report|chat|list|back|open|close` 切页、`@@emote:xxx` 注入表情、`@@sim:status|click:center|drag:offset:dx,dy[,steps]` 模拟桌宠输入。新 UI 状态必须预留等价命令（规范见 `development-standards.md` §6.6）
5. **密钥不入库**：Bridge/Gateway 使用不同 Token；环境变量读取，模板用 `.example`；日志/输出禁含 Token
6. **PM2 进程勿手动 kill/start**：改桥接后 `pm2 restart openclaw-bridge --update-env`
7. **PS 5.1 写 JSON 会带 BOM**：Python 读文件用 `utf-8-sig`；中文路径验证用 `cmd /c dir /b`
8. **编码遵循 `.editorconfig`**：`.cs`/`.ps1`/`.cmd` 带 BOM，其余 UTF-8 无 BOM，`*.cmd` 用 CRLF
9. **安装器依赖安全**：外部安装器执行前必须验证 Authenticode；Bridge/Gateway 令牌不得复用；安装包服务固定使用随包 Node，不得回退到机器 PATH 中的 Node。

## AI 上下文边界（每次任务必须遵守）

1. **默认最小集**：先读本文件、任务包（若存在）、批准指导文档、目标文件和直接依赖；无任务包的小改动只读目标模块文档与对应验证规范。
2. **任务包优先**：任务包的 `contextManifest.requiredDocs` 是有序最小必读清单，`conditionalDocs` 只在其触发条件满足时加载；不得用旧的串行阅读顺序替代任务包边界。
3. **上下文预算**：默认上限为“一个任务包 + 一个批准指导 + 最多两个直接关联的决策/真相文档 + 目标代码/测试”。超过上限必须在任务记录或最终报告中写明触发原因。
4. **专项按需加载**：仅在命中条件时加载构建、节日皮肤、云端成本、质量测量、外置窗口、具身架构等专项文档；例如改 C# 才读构建规范，改桥接/工具才读通信规范，改具身动作才读具身指导。
5. **默认排除**：冻结路线图、旧任务清单、归档、历史报告和无关模块文档默认不读；只有任务明确涉及历史兼容、迁移或冲突核查时才读取。
6. **停止加载**：目标、约束、允许路径、接口依赖、验证证据和非目标明确后停止继续加载文档。
7. **阻断而非扩散**：必要文档缺失或互相矛盾时记录 `BlockedByDecision` 或 `BlockedByEvidence`，不得通过加载整棵文档树规避阻断。
8. **脏工作区基线**：开始任务时记录已有 `git diff --name-only`；范围检查只针对本次新增差异，不把用户已有改动当作越界。

## Git 提交

Conventional Commits：`<type>(<scope>): <中文描述>`。类型：feat/fix/docs/refactor/perf/test/build/chore。scope：bridge/tool/office/doc/build/test/chat/live2d/memory。一个提交一件事；破坏性变更加 `!`。

## 新增工具/端点的标准流程

Python 脚本 → 桥接端点（curl 验证）→ OpenClawBridge.cs 方法 → ToolEngine 工具类 → `build.ps1 -Quick` → 测试 → **测试通过后**更新 `docs/modules/` 对应模块文档 → 提交。

> AI 铁则：写完代码必须更新直接受影响的 md 文档，且**必须等测试通过后再写**（文档只记录已验证的代码真相）。交付前按本次新增 `git diff --name-only` 识别受影响模块；只同步直接受影响且已验证的 truth/module 文档。`docs/README.md` 仅在路径、角色或导航变化时同步；根 README、冻结路线图和旧任务清单不再默认同步。测试/构建被阻断时只能标记“未验证”。

> 新增模块时：在 `docs/modules/` 建文档（套用四要素模板，见 `docs/README.md` 第二节）→ 更新 `docs/README.md` 1.2 表 → 更新本文件模块表。
