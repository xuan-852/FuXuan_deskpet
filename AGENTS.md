# AGENTS.md — 符玄桌宠项目 AI 协作指南

> 本文件是 AI 编码代理（GitHub Copilot / Claude Code 等）的**快速入口**。
> 详细规范见 [`docs/development-standards.md`](docs/development-standards.md)（两者冲突时以详细版为准）。
> 📚 **文档总索引**：[`docs/README.md`](docs/README.md)——顶层权威文档表 + 10 个模块文档（`docs/modules/`）。
>
> **AI 读文档优先级**：`AGENTS.md` → `docs/README.md` → `development-standards.md` → `build-workflow.md`（**改 C# 后构建 / 构建卡死时先读**）→ `code-truth-architecture.md` → `docs/token-cost-testing.md`（涉及云端调用/测试/排查烧钱时**必须先读**）→ `docs/token-saving-architecture.md`（设计/修改成本控制时**必须先读**）→ `docs/quality-measurement-test-guide.md`（编译后测量本地质量时**必须先读**）→ `docs/quality-comparison-test-guide.md`（本地/云端配对对照时**必须先读**）→ `docs/project-bugs-and-acceptance.md`（改外置窗口/渲染/退出/测试代码前**必须先读**）→ 对应 `docs/modules/<模块>.md`

## 项目是什么

Unity（团结引擎 Tuanjie 2022.3.62t7）+ Live2D 的 Windows 桌面 AI 伴侣「符玄」，具备感知-决策-执行-记忆闭环，内置 65 个工具，通过本地 Node.js 桥接调用 OpenClaw AI 与 Python 办公生成器。

## 技术栈速览

| 层 | 位置 | 语言 |
|----|------|------|
| Unity 桌宠 | `code/desktop_unity/Assets/Scripts/` | C#（119 文件，架构见 `docs/code-truth-architecture.md`） |
| 桥接服务器 | `code/desktop_unity/openclaw_bridge.js` | Node.js，端口 19876，PM2 管理（进程名 `openclaw-bridge`） |
| 工具系统 | `Assets/Scripts/ToolEngine/` | `IPetTool` → `AsyncToolBase` → 反射自动发现，危险工具需审批 |
| Python 生成器 | `scripts/office/` 等 | python-pptx / python-docx / openpyxl |
| 文档 | `docs/` | 总索引 = `docs/README.md`；权威架构 = `code-truth-architecture.md`；模块细节 = `docs/modules/`（10 份，见下表） |

## 模块文档（docs/modules/，每份含「作用/架构/迭代/注意」四要素）

| 模块 | 文档 | 一句话定位 |
|------|------|-----------|
| AI 对话 | [`docs/modules/ai-chat-system.md`](docs/modules/ai-chat-system.md) | ChatManager 轮环、注入链、Token 优化 |
| 工具系统 | [`docs/modules/tool-engine.md`](docs/modules/tool-engine.md) | 65 工具、审批、benchmark |
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

## 通信架构（改端点/工具前必读）

```
C# (OpenClawBridge.cs) --HTTP JSON, x-bridge-token--> openclaw_bridge.js (:19876)
                                                        |--WebSocket--> OpenClaw Gateway (:18789)
                                                        |--execSync 临时JSON--> Python scripts
```

- 端点：`/health`、`/search`、`/compile_latex`、`/generate_office`（当前实现均经过 `x-bridge-token` 鉴权）
- 新端点必须鉴权 + 放 404 前 + 更新 404 文案；返回 `{success: bool, ...}` 或 `{success:false,error:"..."}`
- 办公文档输出：`D:\DesktopPetData\Documents\{标题}_{日期}_{随机}\`，成功后自动打开

## 铁律（违反会出事故）

1. **测试必须无记忆隔离**：优先用 `FU_XUAN_DATA` 指向临时目录启动桌宠（`node scripts/test/runtime_smoke.cjs` 已内置：隔离数据目录 + 生产记忆 mtime 零污染断言）；手动测试时至少建空文件 `.test_mode`（防污染 pet_memory/pet_personality/motion_memory/activity/validation），测后删 + 如污染则 `node scripts/backup_memory.cjs --all` 留底并清理。测试前可先备份：`node scripts/backup_memory.cjs`（保留生产记忆至少一份安全副本）
2. **禁止空参数遍历调用所有工具**：`lock_screen` 真锁屏、`file_delete` 真删文件、`set_volume` 真改音量；空参测试只限只读白名单（`get_system_info`/`get_mouse_pos`/`get_clipboard`）
3. **测试中日志**：预期日志用 `LogAssert.Expect(LogType.Warning, "...")` 声明（Unity 把 Error/Warning 计为失败）
4. **UI 测试不靠模拟鼠标点击**：坐标难定位、视觉模型不可靠。必须用终端链路触发——写 `D:\DesktopPetData\inbox.txt`（测试模式启用）：`@@view:settings|reminders|report|chat|list|back|open|close` 切页、`@@emote:xxx` 注入表情。新 UI 状态必须预留等价命令（规范见 `development-standards.md` §6.6）
4. **密钥不入库**：环境变量读取，模板用 `.example`；日志/输出禁含 Token
5. **PM2 进程勿手动 kill/start**：改桥接后 `pm2 restart openclaw-bridge --update-env`
6. **PS 5.1 写 JSON 会带 BOM**：Python 读文件用 `utf-8-sig`；中文路径验证用 `cmd /c dir /b`
7. **编码遵循 `.editorconfig`**：`.cs`/`.ps1`/`.cmd` 带 BOM，其余 UTF-8 无 BOM，`*.cmd` 用 CRLF

## Git 提交

Conventional Commits：`<type>(<scope>): <中文描述>`。类型：feat/fix/docs/refactor/perf/test/build/chore。scope：bridge/tool/office/doc/build/test/chat/live2d/memory。一个提交一件事；破坏性变更加 `!`。

## 新增工具/端点的标准流程

Python 脚本 → 桥接端点（curl 验证）→ OpenClawBridge.cs 方法 → ToolEngine 工具类 → `build.ps1 -Quick` → 测试 → **测试通过后**更新 `docs/modules/` 对应模块文档 → 提交。

> AI 铁则：写完代码必须更新对应 md 文档，且**必须等测试通过后再写**（文档只记录已验证的代码真相）。

> 新增模块时：在 `docs/modules/` 建文档（套用四要素模板，见 `docs/README.md` 第二节）→ 更新 `docs/README.md` 1.2 表 → 更新本文件模块表。
