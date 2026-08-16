<#
.SYNOPSIS
    诊断 Tuanjie 启动卡死 + 恢复构建环境

.DESCRIPTION
    背景（2026-08-16）：build.ps1 -Quick 等待 5 分钟无新日志；直接启动 Tuanjie 进程存在
    但无窗口、CPU 几乎不动；指定的 direct_compile.log 未创建 —— 说明卡在编辑器启动阶段
    （授权握手 / Licensing Client / 项目初始化环境阻塞），尚未进入项目加载与 C# 编译。

    本脚本按顺序执行：
      1) 检查并关闭残留的 Tuanjie / Licensing Client / Crash Handler 进程
      2) 验证授权客户端能否正常启动（启动 4 秒后仍存活或退出码 0 = 正常）
      3) 用绝对路径 + 独立日志文件启动 Tuanjie batchmode 编译（带超时与退出码捕获）
      4) 收集证据：Editor.log / direct_compile.log / 进程残留状态
      5) 最后执行 .\build.ps1 -Quick 验证构建恢复

.PARAMETER UnityExe
    Tuanjie 编辑器绝对路径（默认 D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe）

.PARAMETER ProjectDir
    项目绝对路径（默认 D:\Unity\projects\Desktop_per_pro\code\desktop_unity）

.PARAMETER StartupTimeoutSec
    启动超时秒数（默认 240；诊断卡死时建议 90-120）

.PARAMETER CompileLog
    诊断编译日志绝对路径（默认 <repo>\logs\build\direct_compile.log）

.PARAMETER SkipLicenseCheck
    跳过授权客户端启动验证

.PARAMETER SkipFinalBuild
    跳过最后的 .\build.ps1 -Quick（只诊断不构建）

.EXAMPLE
    .\scripts\diagnose_tuanjie.ps1
    .\scripts\diagnose_tuanjie.ps1 -StartupTimeoutSec 90 -SkipFinalBuild
#>

[CmdletBinding()]
param(
    [string]$UnityExe = "D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe",
    [string]$ProjectDir = "D:\Unity\projects\Desktop_per_pro\code\desktop_unity",
    [int]$StartupTimeoutSec = 240,
    [string]$CompileLog = "",
    [switch]$SkipLicenseCheck,
    [switch]$SkipFinalBuild
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
$initUtf8 = Join-Path $PSScriptRoot "encoding\init-utf8.ps1"
if (Test-Path $initUtf8) { . $initUtf8 }

$ErrorActionPreference = "Continue"   # 诊断脚本：单项失败继续，不中断
$RootDir = Split-Path $PSScriptRoot -Parent          # <repo>（scripts 的上级）
if (-not $CompileLog) { $CompileLog = Join-Path $RootDir "logs\build\direct_compile.log" }
$EditorLog = Join-Path $env:LOCALAPPDATA "Tuanjie\Editor\Editor.log"
$LicenseClients = @(
    "D:\Unity\Tuanjie Hub\TuanjieLicensingClient_V1\Tuanjie.Licensing.Client.exe",
    "D:\Unity\Tuanjie Hub\Frameworks\LicensingClient\Tuanjie.Licensing.Client.exe",
    "C:\Program Files\Unity Hub\UnityLicensingClient_V1\Unity.Licensing.Client.exe",
    "C:\Program Files\Unity Hub\Frameworks\LicensingClient\Unity.Licensing.Client.exe"
)
$ResidualNames = @(
    "Tuanjie", "TuanjieCrashHandler32", "TuanjieCrashHandler64",
    "UnityCrashHandler32", "UnityCrashHandler64",
    "Tuanjie.Licensing.Client", "Unity.Licensing.Client"
)

function Write-Step([string]$msg) { Write-Host "`n========== $msg ==========" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red }

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Tuanjie 构建环境诊断 / 恢复"
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "UnityExe : $UnityExe"
Write-Host "Project  : $ProjectDir"
Write-Host "Compile  : $CompileLog"
Write-Host ""

# ---------- 预检 ----------
Write-Step "预检"
if (-not (Test-Path $UnityExe)) { Write-Fail "找不到 Tuanjie: $UnityExe"; exit 1 }
Write-Ok "Tuanjie.exe 存在"
if (-not (Test-Path (Join-Path $ProjectDir "Assets"))) { Write-Fail "项目目录无效（无 Assets）: $ProjectDir"; exit 1 }
Write-Ok "项目目录有效"
New-Item -ItemType Directory -Force -Path (Split-Path $CompileLog -Parent) | Out-Null

# ---------- 1. 关闭残留进程 ----------
Write-Step "1. 检查并关闭残留进程"
$found = $false
foreach ($name in $ResidualNames) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        $found = $true
        Write-Warn "残留进程: $($p.ProcessName) (PID $($p.Id)) -> 强制结束"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
}
if (-not $found) { Write-Ok "无残留 Tuanjie / Licensing / Crash Handler 进程" }
Start-Sleep -Seconds 1

# 说明：crashpad_handler（Chromium 系，可能是浏览器/其他应用）不在清理名单内，避免误杀无关进程。

# ---------- 2. 授权客户端启动验证 ----------
if (-not $SkipLicenseCheck) {
    Write-Step "2. 验证授权客户端能否启动"
    $licExe = $LicenseClients | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $licExe) {
        Write-Warn "未找到授权客户端（已检查 $($LicenseClients.Count) 个常见路径），跳过；Tuanjie 启动时若授权失败会在 Editor.log 留痕"
    } else {
        Write-Host "授权客户端: $licExe"
        $p = Start-Process -FilePath $licExe -PassThru -ErrorAction SilentlyContinue
        if (-not $p) { Write-Warn "授权客户端启动失败（Start-Process 异常）" }
        else {
            Start-Sleep -Seconds 4
            $p.Refresh()
            if (-not $p.HasExited) {
                Write-Ok "授权客户端进程存活（启动正常），将其结束避免常驻"
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            } else {
                if ($p.ExitCode -eq 0) { Write-Ok "授权客户端启动后正常退出（退出码 0）" }
                else { Write-Warn "授权客户端启动后闪退（退出码 $($p.ExitCode)）—— 可能授权服务异常，注意后续 Editor.log" }
            }
        }
    }
}

# ---------- 3. 绝对路径启动 Tuanjie 编译（带超时与退出码） ----------
Write-Step "3. 绝对路径启动 Tuanjie batchmode 编译"
if (Test-Path $CompileLog) { Remove-Item $CompileLog -Force }
$unityArgs = @(
    "-batchmode", "-quit",
    "-projectPath", $ProjectDir,
    "-logFile", $CompileLog,
    "-executeMethod", "BuildScript.VerifyCompile"
)
Write-Host "[exec] $UnityExe $($unityArgs -join ' ')"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru -NoNewWindow -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Fail "Start-Process 失败（$UnityExe）—— 检查路径与权限"
} else {
    Write-Host "[wait] 等待编辑器退出（超时 $($StartupTimeoutSec)s，超时则强杀）..."
    $exited = $proc.WaitForExit($StartupTimeoutSec * 1000)
    $sw.Stop()

    if (-not $exited) {
        Write-Fail "启动超时（> $($StartupTimeoutSec)s）：进程 PID $($proc.Id) 仍在运行 -> 强杀"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $timeout = $true
    } else {
        $proc.Refresh()
        Write-Host "编辑器已退出，耗时 $($sw.Elapsed.ToString('mm\:ss'))"
        # 退出码偶发读取为空（Start-Process -NoNewWindow 宿主交互问题）→ 以日志为准兜底
        if ($null -eq $proc.ExitCode) {
            if (Test-Path $CompileLog) { Write-Warn "退出码读取为空（宿主限制），但 direct_compile.log 已生成——以日志内容判定" }
            else { Write-Fail "退出码读取为空且 direct_compile.log 未生成——启动早期失败" }
        } elseif ($proc.ExitCode -eq 0) {
            Write-Ok "编译成功（退出码 0）—— 环境恢复正常"
        } else {
            Write-Warn "编译失败（退出码 $($proc.ExitCode)），查看日志定位"
        }
        $timeout = $false
    }
}

# ---------- 4. 收集证据 ----------
Write-Step "4. 收集证据"
if (Test-Path $CompileLog) {
    $sz = [math]::Round((Get-Item $CompileLog).Length / 1KB, 1)
    Write-Ok "direct_compile.log 已创建（${sz} KB）—— 说明已进入编辑器启动/项目加载阶段"
    Write-Host "--- direct_compile.log 尾部 25 行 ---"
    Get-Content $CompileLog -Tail 25
    Write-Host "-------------------------------------"
} else {
    Write-Fail "direct_compile.log 未创建 —— 编辑器在命令行解析/启动早期即阻塞（授权握手或启动器环境问题）"
}
if (Test-Path $EditorLog) {
    $sz = [math]::Round((Get-Item $EditorLog).Length / 1KB, 1)
    $mt = (Get-Item $EditorLog).LastWriteTime
    Write-Ok "Editor.log 存在（${sz} KB，最后写入 $mt）"
    Write-Host "--- Editor.log 尾部 15 行（授权/启动线索） ---"
    Get-Content $EditorLog -Tail 15
    Write-Host "------------------------------------------"
} else {
    Write-Warn "Editor.log 不存在（$EditorLog）—— 编辑器可能从未成功初始化"
}
$logSizeKb = 0
if (Test-Path $CompileLog) { $logSizeKb = [math]::Round((Get-Item $CompileLog).Length / 1KB, 1) }
Write-Host "[state] 编译日志大小: $logSizeKb KB"

# ---------- 5. 最终验证：build.ps1 -Quick ----------
if (-not $SkipFinalBuild) {
    Write-Step "5. 执行 .\build.ps1 -Quick 验证构建恢复"
    Push-Location $RootDir
    try {
        & .\build.ps1 -Quick
        Write-Host "[note] build.ps1 退出码: $LASTEXITCODE"
    } catch {
        Write-Fail "build.ps1 执行异常: $($_.Exception.Message)"
    } finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "========== 诊断完成 ==========" -ForegroundColor Cyan
if ($timeout) {
    Write-Host "结论线索: 第 3 步启动超时 + direct_compile.log 未创建 => 卡在编辑器启动早期（授权/启动器/环境阻塞）"
} elseif (Test-Path $CompileLog) {
    Write-Host "结论线索: direct_compile.log 已创建 => 编辑器能启动，问题在项目加载/编译阶段，看日志报错"
} else {
    Write-Host "结论线索: direct_compile.log 未创建但进程已退出 => 启动早期即失败，重点查 Editor.log 授权/初始化"
}
