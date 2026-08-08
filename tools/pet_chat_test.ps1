<#
.SYNOPSIS
    pet_chat_test.ps1 — 桌宠聊天 UI 规范化测试脚本
.DESCRIPTION
    通过「文件注入通道」向桌宠发送测试消息（绕过 UI 点击，与窗口位置无关），
    等待回复后后台截图并裁剪面板区域，输出规范化结果。

    注入通道原理：RightPanel.cs 的 CheckTestInbox() 在测试模式下
    (D:\DesktopPetData\.test_mode 存在) 每 0.25s 轮询 D:\DesktopPetData\inbox.txt，
    读到非空内容即作为用户消息发送并清空文件 → 历史变化自动重建气泡。

.PARAMETER Message
    要注入发送的消息文本。为空则不发送（仅截图）。

.PARAMETER WaitSec
    发送后等待回复/气泡渲染的秒数（默认 30，DeepSeek API 回复需时间）。

.PARAMETER Name
    截图命名，生成 shot_<Name>.png / panel_<Name>.png。

.PARAMETER ShotOnly
    仅截图（面板已打开、已有对话时不发送新消息）。

.PARAMETER KeepTestMode
    结束时保留 D:\DesktopPetData\.test_mode（默认自动删除，防止污染忆境）。

.PARAMETER NoAutoScroll
    截图的裁剪区域按固定坐标；如需适配其他分辨率可自行调整 PANEL_X/Y/W/H。

.EXAMPLE
    # 发送一条测试消息，等 35 秒后截图分析气泡
    powershell -File tools\pet_chat_test.ps1 -Message "测试一下气泡显示" -WaitSec 35 -Name chat1

.EXAMPLE
    # 不发送，直接截图当前面板
    powershell -File tools\pet_chat_test.ps1 -ShotOnly -Name now
#>
param(
    [string]$Message = "",
    [int]$WaitSec = 30,
    [string]$Name = "chat",
    [switch]$ShotOnly,
    [switch]$KeepTestMode
)

$ErrorActionPreference = "Stop"
$PY = "C:\Users\25295\AppData\Local\Programs\Python\Python312\python.exe"
$SHOT_DIR = "C:\Users\25295\.vscode_vision_screenshots"
$DATA_DIR = "D:\DesktopPetData"
$TOOLS = Join-Path $PSScriptRoot "."
$INBOX = Join-Path $DATA_DIR "inbox.txt"
$TEST_MODE_FILE = Join-Path $DATA_DIR ".test_mode"

# 裁剪放大倍数（面板边界由 find_panel.py 自动检测，窗口挪动/缩放均有效）
$PANEL_SCALE = 2

function Write-Status($tag, $msg) { Write-Host "[pet-test] $tag`t$msg" }

# ── 0. 前置检查 ───────────────────────────────────────────────────────────────
if (-not (Test-Path $PY)) { Write-Status "ERROR" "找不到 Python: $PY"; exit 1 }
if (-not (Test-Path $TOOLS)) { Write-Status "ERROR" "找不到工具目录: $TOOLS"; exit 1 }

# ── 1. 测试模式 ───────────────────────────────────────────────────────────────
$createdTestMode = $false
if (-not (Test-Path $TEST_MODE_FILE)) {
    New-Item -ItemType File -Path $TEST_MODE_FILE -Force | Out-Null
    $createdTestMode = $true
    Write-Status "mode" "已创建 .test_mode（测试后自动清理）"
} else {
    Write-Status "mode" ".test_mode 已存在"
}

try {
    # ── 2. 定位桌宠进程 ────────────────────────────────────────────────────────
    $proc = Get-Process DesktopPet -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) {
        Write-Status "ERROR" "未发现 DesktopPet 进程（先启动桌宠）"
        exit 1
    }
    $hwnd = $proc.MainWindowHandle
    Write-Status "proc" "PID=$($proc.Id) hwnd=$hwnd title='$($proc.MainWindowTitle)'"

    # ── 3. 注入消息（文件通道，无需点击） ─────────────────────────────────────
    if (-not $ShotOnly -and $Message.Trim().Length -gt 0) {
        Set-Content -Path $INBOX -Value $Message -Encoding UTF8 -NoNewline
        Write-Status "send" "已写入 inbox.txt: $Message"
    } else {
        Write-Status "send" "跳过发送（-ShotOnly 或未提供 -Message）"
    }

    # ── 4. 等待回复/渲染 ──────────────────────────────────────────────────────
    if (-not $ShotOnly -and $Message.Trim().Length -gt 0) {
        Write-Status "wait" "等待 $WaitSec 秒（AI 回复 + 气泡渲染）…"
        Start-Sleep -Seconds $WaitSec
    } else {
        Start-Sleep -Seconds 2
    }

    # ── 5. 后台截图 ───────────────────────────────────────────────────────────
    if (-not (Test-Path $SHOT_DIR)) { New-Item -ItemType Directory -Path $SHOT_DIR -Force | Out-Null }
    $shotPath = Join-Path $SHOT_DIR "shot_$Name.png"
    $panelPath = Join-Path $SHOT_DIR "panel_$Name.png"
    & $PY (Join-Path $TOOLS "screenshot_window.py") $proc.Id $shotPath
    if ($LASTEXITCODE -ne 0) { Write-Status "ERROR" "截图失败"; exit 1 }
    Write-Status "shot" "完整窗口截图: $shotPath"

    # ── 6. 自动定位面板并裁剪（窗口挪动/缩放均有效） ─────────────────────────
    $panelLine = & $PY (Join-Path $TOOLS "find_panel.py") $shotPath
    if ($panelLine -like "PANEL *") {
        $parts = $panelLine.Split(" ")
        $panX = [int]$parts[1]; $panY = [int]$parts[2]
        $panW = [int]$parts[3]; $panH = [int]$parts[4]
        Write-Status "panel" "自动检测面板: x=$panX y=$panY w=$panW h=$panH"
        & powershell -NoProfile -File (Join-Path $TOOLS "crop_image.ps1") `
            -Src $shotPath -Dst $panelPath `
            -X $panX -Y $panY -W $panW -H $panH -Scale $PANEL_SCALE
    } else {
        Write-Status "warn" "未检测到面板（$panelLine），回退到整窗截图"
        Copy-Item $shotPath $panelPath -Force
    }
    if ($LASTEXITCODE -ne 0) { Write-Status "ERROR" "裁剪失败"; exit 1 }
    Write-Status "panel" "面板裁剪: $panelPath"

    # ── 7. 规范化结果输出 ─────────────────────────────────────────────────────
    Write-Status "RESULT" "name=$Name message='$Message' shot=$shotPath panel=$panelPath"
    Write-Status "done" "ok"
} finally {
    # ── 清理：删除测试模式（防忆境污染） ──────────────────────────────────────
    if ($createdTestMode -and -not $KeepTestMode) {
        Remove-Item -Path $TEST_MODE_FILE -Force -ErrorAction SilentlyContinue
        Write-Status "cleanup" "已删除 .test_mode（如需保留请加 -KeepTestMode）"
    }
}
