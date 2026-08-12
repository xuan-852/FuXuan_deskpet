# AGENTS.md — 符玄桌宠项目 AI 协作指南

> 本文件是 AI 编码代理（GitHub Copilot / Claude Code 等）的**快速入口**。
> 详细规范见 [`docs/development-standards.md`](docs/development-standards.md)（两者冲突时以详细版为准）。
> 📚 **文档总索引**：[`docs/README.md`](docs/README.md)——顶层权威文档表 + 9 个模块文档（`docs/modules/`）。
>
> **AI 读文档优先级**：`AGENTS.md` → `docs/README.md` → `development-standards.md` → `code-truth-architecture.md` → 对应 `docs/modules/<模块>.md`

## 项目是什么

Unity（团结引擎 Tuanjie 2022.3.62t7）+ Live2D 的 Windows 桌面 AI 伴侣「符玄」，具备感知-决策-执行-记忆闭环，内置 55+ 工具，通过本地 Node.js 桥接调用 OpenClaw AI 与 Python 办公生成器。

## 技术栈速览

| 层 | 位置 | 语言 |
|----|------|------|
| Unity 桌宠 | `code/desktop_unity/Assets/Scripts/` | C#（84 文件，架构见 `docs/code-truth-architecture.md`） |
| 桥接服务器 | `code/desktop_unity/openclaw_bridge.js` | Node.js，端口 19876，PM2 管理（进程名 `openclaw-bridge`） |
| 工具系统 | `Assets/Scripts/ToolEngine/` | `IPetTool` → `AsyncToolBase` → 反射自动发现，危险工具需审批 |
| Python 生成器 | `scripts/office/` 等 | python-pptx / python-docx / openpyxl |
| 文档 | `docs/` | 总索引 = `docs/README.md`；权威架构 = `code-truth-architecture.md`；模块细节 = `docs/modules/`（9 份，见下表） |

## 模块文档（docs/modules/，每份含「作用/架构/迭代/注意」四要素）

| 模块 | 文档 | 一句话定位 |
|------|------|-----------|
| AI 对话 | [`docs/modules/ai-chat-system.md`](docs/modules/ai-chat-system.md) | ChatManager 轮环、注入链、Token 优化 |
| 工具系统 | [`docs/modules/tool-engine.md`](docs/modules/tool-engine.md) | 55+ 工具、审批、benchmark |
| 动作系统 | [`docs/modules/action-agent.md`](docs/modules/action-agent.md) | 决策循环、动作验证闭环 |
| Live2D 渲染 | [`docs/modules/live2d-rendering.md`](docs/modules/live2d-rendering.md) | 渲染管线、参数映射、硬编码迁移 |
| 对话界面 | [`docs/modules/chat-ui.md`](docs/modules/chat-ui.md) | IMGUI 界面、像素化优化 |
| 桥接通信 | [`docs/modules/bridge-communication.md`](docs/modules/bridge-communication.md) | C# ↔ Node 桥 ↔ Gateway/Python |
| 记忆人格 | [`docs/modules/memory-personality.md`](docs/modules/memory-personality.md) | 三层记忆、五维人格、知识库 |
| 编码协议 | [`docs/modules/encoding-protocol.md`](docs/modules/encoding-protocol.md) | 全仓 UTF-8、BOM 坑、乱码排查 |
| 办公工具 | [`docs/modules/office-tools.md`](docs/modules/office-tools.md) | PPT/Word/Excel 生成链路 |

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

- 端点：`/health`（免鉴权）、`/search`、`/compile_latex`、`/generate_office`（均带 `x-bridge-token`）
- 新端点必须鉴权 + 放 404 前 + 更新 404 文案；返回 `{success: bool, ...}` 或 `{success:false,error:"..."}`
- 办公文档输出：`D:\DesktopPetData\Documents\{标题}_{日期}_{随机}\`，成功后自动打开

## 铁律（违反会出事故）

1. **测试必须开测试模式**：建空文件 `D:\DesktopPetData\.test_mode`（防污染 pet_memory/pet_personality），测后删 + `node tools/clean_test_pollution.cjs`
2. **禁止空参数遍历调用所有工具**：`lock_screen` 真锁屏、`file_delete` 真删文件、`set_volume` 真改音量；空参测试只限只读白名单（`get_system_info`/`get_mouse_pos`/`get_clipboard`）
3. **测试中日志**：预期日志用 `LogAssert.Expect(LogType.Warning, "...")` 声明（Unity 把 Error/Warning 计为失败）
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
