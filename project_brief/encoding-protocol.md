# 统一编码协议（encoding-protocol）

> 2026-08-12 建立。**全项目强制 UTF-8**，取代「各工具各用各的编码」的混乱局面。
> 目标：任何环节、任何工具写出的中文，进入另一环节后保持不变。

## 一、协议总纲

```
[编辑器层]  .editorconfig  →  保存文件时强制 UTF-8
[文件层]    源文件全部 UTF-8（.cs/.ps1 带 BOM，其余无 BOM）
[Git 层]    .gitattributes →  入库统一 LF + UTF-8；core.quotepath false
[运行时层]  init-utf8.ps1  →  PowerShell 控制台/管道/HTTP body 全 UTF-8
            PYTHONIOENCODING=utf-8 → Python 输出 UTF-8
            Node/Unity 原生 UTF-8 → 无需处理
```

## 二、各环节具体约定

| 环节 | 约定 | 强制机制 |
|---|---|---|
| C# 源文件 (.cs) | UTF-8 **with BOM** | `.editorconfig [*.cs] charset=utf-8-bom` |
| PowerShell (.ps1) | UTF-8 **with BOM** + 头部 dot-source init-utf8.ps1 | `.editorconfig [*.ps1] charset=utf-8-bom` |
| Python (.py) | UTF-8 无 BOM；输出用 `PYTHONIOENCODING=utf-8` 或 `sys.stdout.reconfigure(encoding='utf-8')` | `.editorconfig` + 文档约定 |
| JS/JSON/MD | UTF-8 无 BOM | `.editorconfig` |
| 换行符 | 统一 LF（.cmd/.bat 除外，强制 CRLF） | `.gitattributes` |
| Git 提交 | UTF-8；`git config core.quotepath false` 显示中文 | 本仓库配置 |
| 网络传输 | `Content-Type: application/json; charset=utf-8`；body 用 UTF-8 字节 | `init-utf8.ps1` 的 `Invoke-Utf8Json` |
| SQLite 存储 | UTF-8（sql.js 默认） | — |

## 三、PowerShell 使用规范（最容易踩坑）

所有含中文的 .ps1 脚本**必须**：

1. 文件保存为 **UTF-8 with BOM**（VS Code 右下角选 UTF-8 with BOM，或 .editorconfig 自动）
2. 在 `param()` 之后加一行：
   ```powershell
   . "$PSScriptRoot\..\tools\init-utf8.ps1"   # 路径按实际位置调整
   ```
3. 发 HTTP 中文 body 时用工具函数：
   ```powershell
   $resp = Invoke-Utf8Json -Uri "http://localhost:3000/api/push" `
       -Token $miniToken -Body @{ type="chat_message"; title="测试"; body="中文" }
   ```

### 为什么不能直接用 Invoke-RestMethod -Body (string)？
PS 5.1 的字符串 body 按系统 ANSI（GBK）编码发送，服务器按 UTF-8 解析 → 中文变 `?`。
`init-utf8.ps1` 已把 `ContentType` 默认值设为 `charset=utf-8`，且提供 `Invoke-Utf8Json`
（内部转 UTF-8 字节数组），两者叠加彻底修复。

## 四、验证方法

```powershell
# 1. 检查 init-utf8 生效
. .\tools\init-utf8.ps1
[Console]::OutputEncoding.EncodingName   # 应显示 UTF-8
$OutputEncoding.EncodingName             # 应显示 Unicode (UTF-8)

# 2. 检查文件编码合规
.\tools\check_encoding.ps1 -Pattern "*.cs"

# 3. 扫描固化乱码
python tools/scan_garbled_cs.py   # 产出 garbled_scan_result.txt
```

## 五、已接入的脚本（共 17 个，全部含中文）

统一做法：文件保存为 **UTF-8 with BOM**，并在 `param()` 块之后（无 `param()` 则放脚本
最顶部注释之后）加一行 dot-source：

```powershell
. "$PSScriptRoot\..\..\tools\init-utf8.ps1"   # 相对层级按脚本所在目录调整
```

| 脚本 | 位置 |
|---|---|
| `build.ps1` | 项目根 |
| `test_chunked_latex.ps1` | 项目根 |
| `analyze_grid.ps1` / `analyze_ui.ps1` | `code/desktop_unity/screenshots/` |
| `generate_pixel_candidates_fixed.ps1` / `generate_pixel_from_live2d.ps1` / `generate_pixel_quick.ps1` | `code/desktop_unity/scripts/` |
| `download_samples.ps1` | `scripts/param_mapper/` |
| `analyze_log.ps1` / `check_encoding.ps1` / `count_log.ps1` / `fuxuan_pixel_art.ps1` / `pet_chat_test.ps1` / `pixelize.ps1` / `read_pixel_map.ps1` / `render_fuxuan_17x24.ps1` | `tools/` |

> 迁移工具：`tools/batch_utf8_protocol.py`（一次性批量加 BOM + 插 dot-source，已成功迁移 15 个存量脚本）。
> 验证结果：全部 16 个已接入脚本语法解析通过；`analyze_log.ps1`/`count_log.ps1`/`analyze_grid.ps1`
> 实机运行中文输出正常；全仓 20 个 ps1 编码 = 17 UTF8_BOM + 3 纯 ASCII（无中文，无需 BOM），零非合规文件。
> 新增含中文的 ps1 请沿用同一做法，勿回退为 GBK 或 UTF-8 无 BOM。
