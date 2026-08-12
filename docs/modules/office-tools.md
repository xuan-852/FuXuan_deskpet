# 办公工具链 — PPT / Word / Excel 三件套生成

> **文档作用**: 本模块文档描述桌宠「办公文档生成」子系统的**代码真相**——从用户需求描述到 .pptx/.docx/.xlsx 落盘的完整链路：AI 组织结构化内容（桥接服务器）→ 本地 Python 渲染（python-pptx/python-docx/openpyxl）→ 输出到 `D:\DesktopPetData\Documents\` 并自动打开。**改办公生成器/端点/工具类前必读**。
> **基本架构**: 工具层 `OfficeTools.cs`（GeneratePptTool/GenerateDocxTool/GenerateXlsxTool → `OfficeTools.RunOfficeGeneration`）→ 桥接 `OpenClawBridge.GenerateOfficeAsync`（HTTP POST /generate_office，300s）→ `openclaw_bridge.js` 三段式（`generateOfficeContent` AI 生成 JSON → 建输出目录 → `renderOfficeFile` execSync Python）→ `scripts/office/{ppt,docx,xlsx}_gen.py`。输出目录 `{标题}_{日期}_{随机}/`，成功后自动打开。
> **开发历史迭代**: 2026-08-12 Phase A 第一期办公工具链落地；修复 /generate_office 首次 500（脚本路径只上探一级 → 多候选探测）；PM2 下 resolvePython 加 USERPROFILE 兜底。
> **编写注意事项**: ①Python 脚本被 execSync 调用，输出**只能打 JSON**（stdout 即契约），日志走 console.error/stderr；②脚本定位靠多候选探测（OFFICE_SCRIPTS_DIR env → 硬编码 → cwd 上探 5 级 → argv[1] 上探 5 级）；③渲染超时 60s、AI 内容生成 180s，env 带 `PYTHONIOENCODING=utf-8 PYTHONUTF8=1`；④内容经临时 JSON 文件传递（避免命令行转义）；⑤AI 返回 JSON 用 `cleanJsonFence` 剥 Markdown 代码块。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及办公生成工具、/generate_office 端点、Python 生成器的改动。
- **回答的问题**:
  - 生成链路长什么样？每段超时多少？
  - 三种文档的结构化 JSON schema 是什么？
  - 脚本/解释器怎么定位的？
  - 输出目录怎么命名？成功后做什么？
- **关联文档**: `modules/bridge-communication.md`（/generate_office 端点契约 + 通用通信铁律）｜`modules/tool-engine.md`（generate_ppt/docx/xlsx 三个工具）｜`development-standards.md`（办公输出约定）

## 二、基本架构

### 2.1 完整链路

```
用户说「做个 PPT」→ 工具 generate_ppt (OfficeTools.cs)
  → OpenClawBridge.GenerateOfficeAsync(type, description, title, theme)   [300s]
  → POST /generate_office (openclaw_bridge.js)
      ① generateOfficeContent: AI 生成结构化 JSON (sendChatAndWait 180s, cleanJsonFence 剥代码块)
      ② 建输出目录: Documents\{标题}_{YYYYMMDD}_{base36时间戳}\
      ③ renderOfficeFile: execSync python {ppt,docx,xlsx}_gen.py <tmp.json> <outDir>  [60s]
  → 返回 {success, path, title, folder_path}
  → OfficeTools.RunOfficeGeneration 解析 → 自动打开文件 → 返回 ✅ 消息
```

### 2.2 三种文档 JSON Schema（AI 生成契约）

| 类型 | 顶层字段 | 关键约束 | Python 库 |
|------|----------|----------|-----------|
| **ppt** | `title / subtitle? / author? / theme? / sections[]` | sections ≥ 3 章节；每节 bullets 3~5 条；theme ∈ blue/green/purple/dark/orange（默认 blue） | python-pptx（16:9） |
| **docx** | `title / author? / intro? / blocks[]` | blocks 类型：h1/h2/h3/p/bullet/number/quote；正文中文首行缩进 | python-docx |
| **xlsx** | `title / sheets[]` | 每表 headers ≥ 2 列、rows ≥ 3 行真实数据；表头高亮、冻结首行、自动筛选 | openpyxl |

> 每种类型有校验：缺 sections/blocks/sheets 数组或空 → 明确报错「AI 未生成 XX 内容」。

### 2.3 关键实现细节（代码真相）

| 项 | 真相 |
|----|------|
| 输出目录 | `D:\DesktopPetData\Documents\{docTitle}_{YYYYMMDD}_{base36}\`（标题非法字符替换为 `_`） |
| Python 解释器定位 | `OFFICE_PYTHON` env → `%LOCALAPPDATA%\Programs\Python\Python312/311/310\python.exe` → PATH `where python`；PM2 下 LOCALAPPDATA 可能缺失，用 USERPROFILE 兜底 |
| 脚本定位（多候选） | `OFFICE_SCRIPTS_DIR` env（最高优先）→ 硬编码 `D:\Unity\projects\Desktop_per_pro\scripts\office\` → `process.cwd()` 上探 5 级 → `process.argv[1]` 目录上探 5 级（PM2 下 argv[1] 是容器脚本，必须兜底） |
| 传参方式 | 内容写临时 JSON 文件 `%TEMP%\office_*.json`（避免命令行转义问题），用完 unlink |
| 渲染环境 | `execSync` + `PYTHONIOENCODING=utf-8` + `PYTHONUTF8=1`，timeout 60s |
| 成功动作 | C# 侧 `Process.Start(UseShellExecute=true)` 自动打开文件 |
| 主题色（PPT） | blue: 1F4E79/2E86C1/DEEBF7、green、purple、dark、orange；统一微软雅黑 |

### 2.4 三个工具（ToolEngine/OfficeTools.cs）

| 工具 | 工具名 | 参数 | 触发场景 |
|------|--------|------|----------|
| GeneratePptTool | `generate_ppt` | description* / title? / theme? | 做 PPT、汇报、演示文稿 |
| GenerateDocxTool | `generate_docx` | description* / title? | Word 文档、文案、报告、通知、会议纪要 |
| GenerateXlsxTool | `generate_xlsx` | description* / title? | 表格、数据表、清单、统计表 |

共享逻辑 `OfficeTools.RunOfficeGeneration`：调桥接 → 解析 JSON → Debug.Log → 自动打开 → 返回成功/失败消息。

## 三、开发历史迭代

| 日期 | 变更 |
|------|------|
| 2026-08-12 | **Phase A 第一期**：三生成器 + /generate_office 端点 + 三工具类 + 自动打开 |
| 2026-08-12 | 修复首次 500：脚本路径只上探一级 → 多候选探测（env → 硬编码 → cwd 5 级 → argv[1] 5 级） |
| 2026-08-12 | PM2 下 Python 定位失败：resolvePython 加 USERPROFILE 兜底 |

## 四、编写注意事项

1. **Python stdout 即契约**：脚本被 execSync 捕获 stdout，**只能输出一行 JSON**（`{"success":true,"path":...}` 或 `{"success":false,"error":...}`）；日志打印必须走 `print(..., file=sys.stderr)` / logging
2. **脚本定位多候选**：新增生成器脚本时同时更新 `renderOfficeFile` 的 scriptMap；不要依赖 cwd（PM2 下不可靠）
3. **AI JSON 容错**：AI 可能包 Markdown 代码块 → `cleanJsonFence` 剥壳；解析失败时把前 300 字符打日志便于排查，报错提示用户换描述重试
4. **临时文件**：内容经临时 JSON 传递（防命令行转义/超长参数），用完必须 unlink；避免用命令行直接传中文内容
5. **编码**：Python 读 JSON 用 `utf-8-sig`（PS/桥接写文件可能带 BOM）；渲染环境已强制 PYTHONIOENCODING=utf-8
6. **校验**：description 必填、type ∈ {ppt,docx,xlsx}；AI 返回必须校验数组非空再进渲染
7. **验证链路**：curl POST /generate_office（先建 `.test_mode`）→ 检查 Documents 输出 → 桌面看自动打开；`node --check openclaw_bridge.js` + Python 脚本 `python -m py_compile`
