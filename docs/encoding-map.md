# 项目编码模式地图（encoding-map）

> 2026-08-12 排查「AI 通信中文乱码」后整理。核心结论：**项目数据链路全程 UTF-8，乱码只有两个历史遗留源**。

## 一、各环节编码一览

| 环节 | 编码 | 说明 |
|---|---|---|
| C# 源文件（262 个） | 171 UTF8_BOM / 88 UTF8_NO_BOM / 3 ASCII | Unity (Roslyn) 对 UTF8_NO_BOM 也按 UTF-8 编译，**无 BOM 不产生乱码** |
| Node.js 服务器 (server/src) | 全 UTF-8 | express.json() 默认 UTF-8；sql.js 存 UTF-8 |
| SQLite 数据库 (njust.db) | UTF-8 | 实测中文存取完好 |
| Unity 网络层 (HttpClient / UnityWebRequest) | UTF-8 | `ReadAsStringAsync()` 默认 UTF-8 解码；`Encoding.UTF8.GetBytes` 发送 |
| Unity 文件读写 | 大多显式 UTF-8 | 个别 `File.ReadAllText` 无参（默认 UTF-8+BOM 检测）；`KnowledgeBaseManager` 有 UTF-8→GBK 自动回退（合理） |
| es.exe 搜索 | UTF-8 BOM | 旧版 es.exe 无 `-utf8` 开关，用 `-export-txt -utf8-bom` 中转（I18N.CJK 缺失绕行方案） |
| Python 脚本 | 740 ASCII / 89 UTF8_NO_BOM | 正常 |
| JS 脚本 | 28 ASCII / 3 UTF8_NO_BOM | 正常 |
| PowerShell 5.1 终端 | **GBK (cp936)** ⚠️ | 显示 UTF-8 日志会乱，发送字符串 body 中文会变 `?` |

## 二、两个真实乱码源（均已修复）

### 1. 源文件固化的 GBK 乱码（历史遗留）
- **位置**：`MotionTranslator.cs` L987 / L1007（全项目仅此 2 处）
- **成因**：某次编辑器以 GBK 读 UTF-8 文件并保存，把「🦋为」「自动注入」等字面量固化成「鈿犱负」「鑷\ue044姩娉ㄥ叆」乱码字符（含 PUA 字符 U+E000-F8FF，是 100% 乱码铁证）
- **影响**：仅日志显示乱码，运行时数据（LLM 返回）正常
- **修复**：重写为正确中文

### 2. PowerShell 发送中文变 `?`
- **位置**：`Invoke-RestMethod -Body (string)` 推送到 `/api/push`
- **成因**：PS 5.1 字符串 body 默认按非 UTF-8（系统 ANSI）编码发送，服务器按 UTF-8 解析 → 中文变 `?`
- **实测**：字符串 body 推送后数据库存 `"?? ????","??????????????"`；`-Body ([byte[]]utf8Bytes)` + `-ContentType "application/json; charset=utf-8"` 后数据库存 `"编码测试","这是编码验证消息，中文必须正常"` ✓
- **修复**：发送中文时显式 `[System.Text.Encoding]::UTF8.GetBytes($json)`

## 三、排查工具（scripts/encoding/）
- `check_encoding.ps1`：按文件类型统计 UTF8_BOM / UTF8_NO_BOM / GBK / ASCII 分布
- `scan_garbled_cs.py`：PUA 字符 + GBK→UTF-8 还原双重检测，扫描 .cs 固化乱码

## 四、运维提示
- 看桌宠日志用 `Get-Content -Encoding UTF8`（Player.log 是 UTF-8），但 PS 5.1 终端按 GBK 显示中文 emoji 仍可能乱，重定向到文件用编辑器看更稳
- 写 .ps1 脚本含中文必须存 **UTF-8 with BOM**（否则 PS 5.1 按 GBK 解析中文串报错）
- 新增 C# 文件建议 UTF-8 with BOM，与存量 171 个文件一致
