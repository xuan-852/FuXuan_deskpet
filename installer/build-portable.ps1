<#
.SYNOPSIS
    符玄桌宠 — 阶段1 便携目录打包脚本（安装包改造）
.DESCRIPTION
    把 Build 产物 + 桥接 + 脚本层组装成「拷贝即跑」的便携目录 installer\portable\
    关键设计：
      - 全部路径在 cmd 内用 %~dp0 相对解析，不依赖安装位置（换任意盘/目录均可）
      - 桥接/数据目录/脚本目录全部环境变量化（阶段0 已支持：FU_XUAN_DATA / OFFICE_SCRIPTS_DIR
        / KNOWLEDGE_SCRIPTS_DIR / OFFICE_PYTHON / OPENCLAW_NODE_MODULES / BRIDGE_PORT）
      - 运行时优先便携内置（bridge\node、scripts\python），缺失自动回退系统运行时
      - openclaw npm 包（300MB）拷入 bridge\node_modules，目标机无需再 npm 全局安装
.USAGE
    .\installer\build-portable.ps1                    # 默认全量（含 openclaw 包）
    .\installer\build-portable.ps1 -SkipOpenClaw      # 跳过 300MB openclaw 包（目标机需已装 openclaw）
    .\installer\build-portable.ps1 -IncludeNode       # 额外下载便携 Node（~30MB，网络需可用）
    .\installer\build-portable.ps1 -IncludePython     # 额外下载 Python embeddable + pip 装 7 包（~60MB）
.NOTES
    产物目录 installer\portable\ 不入库（.gitignore）；本脚本与 cmd 模板入库。
    风险项（方案 §5.3/§十）：Python embeddable 跑 PyMuPDF/PIL 需实测，失败改官方安装器方案。
#>
param(
    [switch]$IncludeNode,        # 下载便携 Node 运行时到 bridge\node
    [switch]$IncludePython,      # 下载 Python embeddable 到 scripts\python 并 pip 安装 7 包
    [switch]$SkipOpenClaw,       # 跳过拷贝 openclaw npm 包（省 300MB）
    [string]$OutDir = "$PSScriptRoot\portable",
    [string]$NodeVersion = "v22.22.3",   # ⚠️ 必须 ≥22.22.3：OpenClaw 要求 SQLite 3.51.3+，旧版 Node 内置 3.47.2 有 WAL 损坏 bug（2026-08-14 实测 v22.14.0 启动报错）
    [string]$PythonVersion = "3.12.10"   # 便携 Python 版本（不存在则回退 3.12.8）
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\..\scripts\encoding\init-utf8.ps1"

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.ForegroundColor = "Cyan"
Write-Host "============================================"
Write-Host "   Fu Xuan Desktop Pet - Portable Builder"
Write-Host "           (installer plan: stage 1)"
Write-Host "============================================"

$RootDir    = Split-Path $PSScriptRoot -Parent
$ProjectDir = Join-Path $RootDir "code\desktop_unity"
$BuildDir   = Join-Path $RootDir "Build"
$Downloads  = Join-Path $PSScriptRoot "downloads"

function New-CleanDir([string]$dir) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

function Copy-Tree([string]$src, [string]$dst) {
    if (-not (Test-Path $src)) { Write-Host "[WARN] 源不存在，跳过: $src"; return }
    if ((Get-Item $src).PSIsContainer) {
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        Copy-Item "$src\*" $dst -Recurse -Force
    } else {
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        Copy-Item $src (Join-Path $dst (Split-Path $src -Leaf)) -Force
    }
    Write-Host "[OK] $src -> $dst"
}

# ── 1. 校验前置 ──
$exe = Join-Path $BuildDir "DesktopPet.exe"
if (-not (Test-Path $exe)) {
    Write-Host "[ERROR] 未找到构建产物: $exe（先运行 .\build.ps1）" -ForegroundColor Red
    exit 1
}

# ── 2. 清空并重建便携目录 ──
New-CleanDir $OutDir
Write-Host "[Build] 便携目录: $OutDir"

# ── 3. 拷贝桌宠本体（整目录：exe + _Data + TuanjiePlayer.dll + MonoBleedingEdge 等根级支撑文件，缺一不可）──
Write-Host "`n── 桌宠本体 ──"
Copy-Tree $BuildDir $OutDir

# ── 4. 拷贝桥接 ──
Write-Host "`n── 桥接层 ──"
Copy-Tree (Join-Path $ProjectDir "openclaw_bridge.js") (Join-Path $OutDir "bridge")
# openclaw npm 包（桥接运行依赖，目标机免全局安装）
if (-not $SkipOpenClaw) {
    Copy-Tree "D:\openclaw\node_modules\openclaw" (Join-Path $OutDir "bridge\node_modules\openclaw")
} else {
    Write-Host "[SKIP] 未拷贝 openclaw 包（-SkipOpenClaw）"
}

# ── 5. 拷贝脚本层 ──
Write-Host "`n── Python 脚本层 ──"
foreach ($sub in @("office", "latex", "knowledge")) {
    Copy-Tree (Join-Path $RootDir "scripts\$sub") (Join-Path $OutDir "scripts\$sub")
}

# ── 5b. 组件安装脚本（阶段3：VC++/OpenClaw/Ollama/MiKTeX/Everything/NSSM 服务）──
Write-Host "`n── 组件脚本（extras\components）──"
Copy-Tree (Join-Path $PSScriptRoot "components") (Join-Path $OutDir "extras\components")

# ── 5c. 安装验收脚本（extras\acceptance，目标机用内置 Node 运行）──
Write-Host "`n── 验收脚本（extras\acceptance）──"
Copy-Tree (Join-Path $PSScriptRoot "verify-acceptance.cjs") (Join-Path $OutDir "extras\acceptance")

# ── 6. 可选：便携 Node / Python ──
New-Item -ItemType Directory -Path $Downloads -Force | Out-Null
if ($IncludeNode) {
    Write-Host "`n── 下载便携 Node $NodeVersion ──"
    $zip = Join-Path $Downloads "node-$NodeVersion-win-x64.zip"
    $url = "https://nodejs.org/dist/$NodeVersion/node-$NodeVersion-win-x64.zip"
    try {
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        Expand-Archive $zip (Join-Path $OutDir "bridge\node") -Force
        # 展开后层级是 node-vX-win-x64\ → 上移一层
        $inner = Join-Path $OutDir "bridge\node\node-$NodeVersion-win-x64"
        if (Test-Path $inner) { Get-ChildItem $inner | Move-Item -Destination (Join-Path $OutDir "bridge\node") -Force; Remove-Item $inner -Force }
        Write-Host "[OK] 便携 Node: bridge\node\node.exe"
    } catch {
        Write-Host "[WARN] Node 下载失败（$($_.Exception.Message)），将回退系统 node"
    }
}
if ($IncludePython) {
    Write-Host "`n── 下载便携 Python embeddable $PythonVersion ──"
    $zip = Join-Path $Downloads "python-$PythonVersion-embed-amd64.zip"
    $url = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
    try {
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        $pyDir = Join-Path $OutDir "scripts\python"
        New-Item -ItemType Directory -Path $pyDir -Force | Out-Null
        Expand-Archive $zip $pyDir -Force
        # 追加 site-packages 与 office 到 ._pth（embeddable 默认不加载 site-packages）
        $pth = Join-Path $pyDir "python$($PythonVersion.Split('.')[0..1] -join '')._pth"
        if (Test-Path $pth) {
            $lines = Get-Content $pth
            if (-not ($lines -contains "Lib\site-packages")) {
                Add-Content $pth "Lib\site-packages"
                Add-Content $pth "..\office"
            }
        }
        # get-pip + 安装 7 个依赖
        $getPip = Join-Path $Downloads "get-pip.py"
        Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip -UseBasicParsing
        & (Join-Path $pyDir "python.exe") $getPip --target (Join-Path $pyDir "Lib\site-packages") | Out-Null
        & (Join-Path $pyDir "python.exe") -m pip install --target (Join-Path $pyDir "Lib\site-packages") `
            python-docx==1.2.0 openpyxl==3.1.5 python-pptx==1.0.0 Pillow==12.3.0 PyMuPDF==1.28.2 pypdf==6.14.2 requests==2.33.0 | Out-Null
        Write-Host "[OK] 便携 Python: scripts\python\python.exe（7 包已装）"
    } catch {
        Write-Host "[WARN] Python embeddable 安装失败（$($_.Exception.Message)），将回退系统 python"
    }
}

# ── 7. 生成启动脚本（%~dp0 相对路径，BOM + CRLF）──
Write-Host "`n── 生成启动脚本 ──"
function Write-CmdFile([string]$path, [string]$content) {
    $content = $content -replace "`r`n", "`n" -replace "`n", "`r`n"
    [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($true)))
}

$setEnv = @"
@echo off
rem ============================================================
rem  FuXuan portable - environment setup (session only, idempotent)
rem  Highest priority = pre-existing user env vars (never overwrite)
rem ============================================================
set "ROOT=%~dp0"
if not defined FU_XUAN_DATA set "FU_XUAN_DATA=D:\DesktopPetData"
if not defined OFFICE_SCRIPTS_DIR set "OFFICE_SCRIPTS_DIR=%ROOT%scripts\office"
if not defined KNOWLEDGE_SCRIPTS_DIR set "KNOWLEDGE_SCRIPTS_DIR=%ROOT%scripts\knowledge"
if exist "%ROOT%scripts\python\python.exe" if not defined OFFICE_PYTHON set "OFFICE_PYTHON=%ROOT%scripts\python\python.exe"
if exist "%ROOT%bridge\node_modules" if not defined OPENCLAW_NODE_MODULES set "OPENCLAW_NODE_MODULES=%ROOT%bridge\node_modules"
if not defined BRIDGE_PORT set "BRIDGE_PORT=19876"
"@
Write-CmdFile (Join-Path $OutDir "set-env.cmd") $setEnv

$startBridge = @"
@echo off
rem ============================================================
rem  FuXuan portable - bridge launcher
rem  Requires: OpenClaw Gateway on ws://127.0.0.1:18789
rem  (openclaw package is bundled under bridge\node_modules)
rem ============================================================
setlocal
set "ROOT=%~dp0"
call "%ROOT%set-env.cmd"
if exist "%ROOT%bridge\node\node.exe" (
    "%ROOT%bridge\node\node.exe" "%ROOT%bridge\openclaw_bridge.js"
) else (
    node "%ROOT%bridge\openclaw_bridge.js"
)
"@
Write-CmdFile (Join-Path $OutDir "start-bridge.cmd") $startBridge

$stopBridge = @"
@echo off
rem Stop the portable bridge listening on BRIDGE_PORT (default 19876)
setlocal
set "PORT=19876"
if not "%BRIDGE_PORT%"=="" set "PORT=%BRIDGE_PORT%"
powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort %PORT% -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }"
echo Bridge on port %PORT% stopped (if it was running).
"@
Write-CmdFile (Join-Path $OutDir "stop-bridge.cmd") $stopBridge

$startPet = @"
@echo off
rem Launch FuXuan pet from the portable directory
setlocal
set "ROOT=%~dp0"
call "%ROOT%set-env.cmd"
start "" /D "%ROOT%" "%ROOT%DesktopPet.exe"
"@
Write-CmdFile (Join-Path $OutDir "start-pet.cmd") $startPet

# ── 8. version.txt + README ──
$version = "v1.0-portable-$(Get-Date -Format 'yyyyMMdd-HHmm')"
[System.IO.File]::WriteAllText((Join-Path $OutDir "version.txt"), $version + "`r`n", (New-Object System.Text.UTF8Encoding($true)))

$readme = @"
# FuXuan Desktop Pet - Portable Build (Stage 1)

Version: $version

## 快速开始
1. 启动桥接:  双击 start-bridge.cmd（需 OpenClaw Gateway 运行在 127.0.0.1:18789）
2. 启动桌宠:  双击 start-pet.cmd
3. 停止桥接:  双击 stop-bridge.cmd

## 目录
- DesktopPet.exe / DesktopPet_Data\  桌宠本体
- bridge\                            桥接服务器（含 openclaw npm 包，目标机免全局安装）
- bridge\node\                       便携 Node（可选，缺失回退系统 node）
- scripts\                           办公/LaTeX/知识脚本层
- scripts\python\                    便携 Python（可选，缺失回退系统 python）
- set-env.cmd                        环境变量（FU_XUAN_DATA 等，会话级，不覆盖已有变量）

## 环境变量（可预先设置以覆盖默认值）
- FU_XUAN_DATA           数据目录（默认 D:\DesktopPetData）
- BRIDGE_PORT            桥接端口（默认 19876）
- OFFICE_PYTHON          办公脚本 Python（默认便携或系统 python）
- OPENCLAW_NODE_MODULES  openclaw 包位置（默认便携内置）
- POGGET_EXE             收纳工具路径（可选）

## 依赖（目标机仍需）
- OpenClaw Gateway（npm i -g openclaw + gateway start）
- DeepSeek / GLM API 密钥（环境变量）
- VC++ 2015-2022 x64 运行库（桌宠本体）
"@
[System.IO.File]::WriteAllText((Join-Path $OutDir "README.md"), $readme, (New-Object System.Text.UTF8Encoding($false)))

# ── 9. 汇总 ──
Write-Host "`n============================================"
Write-Host "[OK] 便携目录构建完成: $OutDir"
$size = [math]::Round(((Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "[OK] 总大小: $size MB"
Write-Host "============================================"
