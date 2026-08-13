# 编码协议 — 全项目强制 UTF-8 与乱码防治

> **文档作用**: 本模块文档描述桌宠项目「编码规范」的**代码真相**——全项目强制 UTF-8 的四层机制（.editorconfig / .gitattributes / init-utf8.ps1 / PYTHONIOENCODING）、各环节具体编码约定、两个历史乱码源及修复、PowerShell 使用规范。**任何涉及中文文件写入、HTTP body 发送、跨语言数据传递的改动前必读**。
> **基本架构**: 编辑器层 `.editorconfig` 强制（.cs/.ps1 带 BOM，其余 UTF-8 无 BOM，*.cmd CRLF）→ 文件层源文件全 UTF-8 → Git 层 `.gitattributes`（LF + UTF-8，core.quotepath false）→ 运行时层 `init-utf8.ps1`（PowerShell 控制台/管道/HTTP body 全 UTF-8）+ `PYTHONIOENCODING=utf-8`。
> **开发历史迭代**: 2026-08-12 排查「AI 通信中文乱码」——全仓数据链路实测全 UTF-8，乱码仅两个历史遗留源（MotionTranslator.cs GBK 固化乱码、PS Invoke-RestMethod 中文变 ?）；同批建立统一编码协议 + 迁移 15 个存量 ps1 脚本（batch_utf8_protocol.py）。
> **编写注意事项**: ①含中文的 .ps1 必须 UTF-8 with BOM + dot-source init-utf8.ps1；②发中文 body 用 `Invoke-Utf8Json`（PS 5.1 字符串 body 按 GBK 发送）；③PS 写 JSON 带 BOM，Python 读文件用 `utf-8-sig`；④新增 C# 文件建议 UTF-8 with BOM 与存量一致。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何创建/修改含中文的源文件、跨语言（C#/PS/Python/JS）传参、HTTP body、数据库存储的改动。
- **回答的问题**:
  - 全项目编码约定是什么？强制机制是什么？
  - 乱码从哪来？怎么排查？
  - PowerShell 为什么最容易踩坑？怎么正确写？
  - 如何验证编码合规？
- **关联文档**: `development-standards.md` 八章（编码规范）｜`modules/bridge-communication.md`（BOM 坑、Python 读 utf-8-sig）｜`modules/action-agent.md`（MotionTranslator 乱码修复出处）｜历史存档：`encoding-map.md`（乱码排查地图）、`encoding-protocol.md`（协议全文，本模块是其浓缩真相版）

## 二、基本架构

### 2.1 四层强制机制

```
[编辑器层]  .editorconfig  →  保存文件时强制 UTF-8
[文件层]    源文件全部 UTF-8（.cs/.ps1 带 BOM，其余无 BOM）
[Git 层]    .gitattributes →  入库统一 LF + UTF-8；core.quotepath false
[运行时层]  init-utf8.ps1  →  PowerShell 控制台/管道/HTTP body 全 UTF-8
            PYTHONIOENCODING=utf-8 → Python 输出 UTF-8
            Node/Unity 原生 UTF-8 → 无需处理
```

### 2.2 各环节编码约定

| 环节 | 约定 | 强制机制 |
|---|---|---|
| C# 源文件 (.cs) | UTF-8 **with BOM** | `.editorconfig [*.cs] charset=utf-8-bom` |
| PowerShell (.ps1) | UTF-8 **with BOM** + 头部 dot-source init-utf8.ps1 | `.editorconfig [*.ps1] charset=utf-8-bom` |
| Python (.py) | UTF-8 无 BOM；`PYTHONIOENCODING=utf-8` 或 `sys.stdout.reconfigure(encoding='utf-8')` | `.editorconfig` + 文档约定 |
| JS/JSON/MD | UTF-8 无 BOM | `.editorconfig` |
| 换行符 | 统一 LF（.cmd/.bat 强制 CRLF） | `.gitattributes` |
| Git 提交 | UTF-8；`git config core.quotepath false` 显示中文 | 本仓库配置 |
| 网络传输 | `Content-Type: application/json; charset=utf-8`；body 用 UTF-8 字节 | `init-utf8.ps1` 的 `Invoke-Utf8Json` |
| SQLite 存储 | UTF-8（sql.js 默认） | — |

### 2.3 各环节编码实测分布（2026-08-12 排查）

| 环节 | 实测 |
|---|---|
| C# 源文件（262 个） | 171 UTF8_BOM / 88 UTF8_NO_BOM / 3 ASCII——Unity(Roslyn) 对无 BOM 也按 UTF-8 编译，无 BOM 不产生乱码 |
| Node.js / Python / JS | 全 UTF-8，正常 |
| SQLite (njust.db) | UTF-8，中文存取完好 |
| PowerShell 5.1 终端 | **GBK (cp936)** ⚠️ 显示 UTF-8 日志会乱，发送字符串 body 中文变 `?` |

## 三、开发历史迭代

| 日期 | 变更 |
|------|------|
| 2026-08-12 | 排查「AI 通信中文乱码」→ 全仓实测 + 建立统一编码协议（encoding-protocol.md） |
| 2026-08-12 | 修复两个乱码源：①MotionTranslator.cs L987/L1007 GBK 固化乱码（PUA 字符铁证）②PS Invoke-RestMethod 字符串 body 按 GBK 发送 |
| 2026-08-12 | batch_utf8_protocol.py 迁移 15 个存量 ps1（加 BOM + 插 dot-source）；全仓 20 个 ps1 = 17 UTF8_BOM + 3 ASCII，零非合规 |

## 四、编写注意事项（铁律）

1. **含中文的 .ps1 三必须**：①文件存 UTF-8 **with BOM**（否则 PS 5.1 按 GBK 解析中文串报错）②`param()` 后加 `. "$PSScriptRoot\..\encoding\init-utf8.ps1"`（路径按实际调整）③发 HTTP 中文 body 用 `Invoke-Utf8Json`
2. **PS 5.1 发中文 body**：`Invoke-RestMethod -Body (string)` 按系统 ANSI(GBK) 发送 → 服务器按 UTF-8 解析 → 中文变 `?`。必须 `[System.Text.Encoding]::UTF8.GetBytes($json)` + `-ContentType "application/json; charset=utf-8"`
3. **PS 写 JSON 带 BOM**：PS 5.1 `Out-File -Encoding utf8` 会写 BOM → Python 读文件必须 `utf-8-sig`；JS 读配置 strip BOM（`.replace(/^\uFEFF/, '')`）
4. **源文件乱码识别**：GBK 固化乱码含 PUA 字符（U+E000-F8FF）= 100% 乱码铁证；用 `scripts/encoding/scan_garbled_cs.py` 扫描
5. **新增文件编码**：新增 C# 建议 UTF-8 with BOM（与存量 171 个一致）；.cmd 用 CRLF；其余 UTF-8 无 BOM
6. **看日志**：**首选 `D:\DesktopPetData\logs\player_log.txt`**（N42 起全量镜像，每次追加即刷盘，Error/Exception 带堆栈，超 10MB 截断尾部 3000 行，启动带 `===== 桌宠启动 =====` 标记）；Player.log 是 UTF-8：`Get-Content -Encoding UTF8`，但 PS 5.1 终端 GBK 显示 emoji 仍可能乱，重定向到文件用编辑器看
7. **验证命令**：
   ```powershell
   . .\scripts\encoding\init-utf8.ps1
   [Console]::OutputEncoding.EncodingName   # 应显示 UTF-8
   .\scripts\encoding\check_encoding.ps1 -Pattern "*.cs"   # 编码合规检查
   python scripts/encoding/scan_garbled_cs.py              # 扫描固化乱码
   ```
