<#
.SYNOPSIS
    符玄桌宠 — 标准构建脚本
.DESCRIPTION
    在本地直接调用 Tuanjie 引擎执行构建。
    不需要 subst 虚拟盘，自动处理 Tuanjie 路径拼接 Bug。

    用法:
        .\build.ps1                     # 完整构建（默认）
        .\build.ps1 -Quick              # 仅验证编译
        .\build.ps1 -RunTests           # 运行 Editor 测试套件
        .\build.ps1 -OutputDir "D:\tmp" # 指定输出目录
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Quick
    .\build.ps1 -RunTests
#>

param(
    [string]$UnityExe = "D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe",
    [string]$LogFile = "D:\Unity\projects\Desktop_per_pro\logs\build\build_log.txt",
    [string]$DataRoot = "",
    [switch]$Quick,
    [switch]$RunTests,
    [switch]$NoKill
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\scripts\encoding\init-utf8.ps1"

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.ForegroundColor = "Cyan"
Write-Host "============================================"
Write-Host "      Fu Xuan Desktop Pet - Build Script"
Write-Host "============================================"
Write-Host ""

# ---- Resolve paths ----
$RootDir = "D:\Unity\projects\Desktop_per_pro"
$ProjectDir = Join-Path $RootDir "code\desktop_unity"
$DefaultOutputDir = Join-Path $RootDir "Build"

# ---- Isolate Unity build-time data ----
# Tuanjie loads runtime assemblies while opening the project.  Without an
# isolated FU_XUAN_DATA/.test_mode pair, editor/test startup can touch the
# production data directory and hang before -logFile is created.  Reuse an
# explicitly supplied isolated root; otherwise create a disposable one.
$OriginalDataRoot = [Environment]::GetEnvironmentVariable("FU_XUAN_DATA", "Process")
$BuildDataRoot = $DataRoot
if ([string]::IsNullOrWhiteSpace($BuildDataRoot)) { $BuildDataRoot = $OriginalDataRoot }
$CreatedBuildDataRoot = $false
$BuildDataRootIsTestMode = $false
if (-not [string]::IsNullOrWhiteSpace($BuildDataRoot)) {
    $BuildDataRootIsTestMode = Test-Path -LiteralPath (Join-Path $BuildDataRoot ".test_mode")
}
if ([string]::IsNullOrWhiteSpace($BuildDataRoot) -or -not $BuildDataRootIsTestMode) {
    $BuildDataRoot = Join-Path $env:TEMP ("fuxuan_build_{0}" -f ([guid]::NewGuid().ToString("N")))
    New-Item -ItemType Directory -Force -Path $BuildDataRoot | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $BuildDataRoot ".test_mode") | Out-Null
    $CreatedBuildDataRoot = $true
}
$env:FU_XUAN_DATA = $BuildDataRoot
Write-Host "[Build] Isolated data: $BuildDataRoot"

# ---- Ensure log dir exists ----
New-Item -ItemType Directory -Force -Path (Split-Path $LogFile -Parent) | Out-Null

# ---- Pre-checks ----
if (-not (Test-Path $UnityExe)) {
    $Host.UI.RawUI.ForegroundColor = "Red"
    Write-Host "[ERROR] Unity not found: $UnityExe"
    exit 1
}
Write-Host "[OK] Unity: $UnityExe"

if (-not (Test-Path (Join-Path $ProjectDir "Assets"))) {
    $Host.UI.RawUI.ForegroundColor = "Red"
    Write-Host "[ERROR] Invalid project path (no Assets): $ProjectDir"
    exit 1
}
Write-Host "[OK] Project: $ProjectDir"

# ---- Remove stale Tuanjie project locks ----
# A killed batchmode editor can leave these locks behind.  The next editor
# then waits before creating -logFile, which looks like a compiler hang.
$ActiveUnityProc = Get-Process -Name "Tuanjie" -ErrorAction SilentlyContinue
if ($ActiveUnityProc) {
    $ActivePids = ($ActiveUnityProc | ForEach-Object { $_.Id }) -join ", "
    $Host.UI.RawUI.ForegroundColor = "Red"
    Write-Host "[ERROR] Tuanjie 正在运行 (PID: $ActivePids)，请先关闭后再构建"
    exit 1
}
$UnityLockPaths = @(
    (Join-Path $ProjectDir "Library\ArtifactDB-lock"),
    (Join-Path $ProjectDir "Library\SourceAssetDB-lock")
)
foreach ($lockPath in $UnityLockPaths) {
    if (Test-Path -LiteralPath $lockPath) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction Stop
        Write-Host "[BUILD] Removed stale Unity lock: $lockPath"
    }
}

# IL post-processing also leaves a PID marker after a forcibly terminated
# editor.  Only remove it when the recorded PID is no longer alive; an active
# marker is left untouched so two editors cannot run against the same Library.
$IlppPidPath = Join-Path $ProjectDir "Library\ilpp.pid"
if (Test-Path -LiteralPath $IlppPidPath) {
    $IlppPidText = (Get-Content -LiteralPath $IlppPidPath -Raw -ErrorAction Stop).Trim()
    $IlppPid = 0
    if ([int]::TryParse($IlppPidText, [ref]$IlppPid)) {
        if (-not (Get-Process -Id $IlppPid -ErrorAction SilentlyContinue)) {
            Remove-Item -LiteralPath $IlppPidPath -Force -ErrorAction Stop
            Write-Host "[BUILD] Removed stale ILPP PID marker: $IlppPidPath (PID $IlppPid not running)"
        } else {
            Write-Host "[ERROR] Active ILPP process found (PID $IlppPid); refusing to remove marker" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[WARN] Ignoring malformed ILPP PID marker: $IlppPidPath" -ForegroundColor Yellow
    }
}

# ---- Detect running DesktopPet (would lock output exe and fail the build) ----
$PetProc = Get-Process -Name "DesktopPet" -ErrorAction SilentlyContinue
if ($PetProc) {
    $Pids = ($PetProc | ForEach-Object { $_.Id }) -join ", "
    $Host.UI.RawUI.ForegroundColor = "Yellow"
    Write-Host "[WARN] DesktopPet 正在运行 (PID: $Pids)，会锁定输出文件导致构建失败"
    if ($NoKill) {
        $Host.UI.RawUI.ForegroundColor = "Red"
        Write-Host "[ERROR] 已加 -NoKill，请先手动关闭 DesktopPet 再构建"
        exit 1
    }
    Write-Host "[BUILD] 自动终止 DesktopPet 进程..."
    $PetProc | Stop-Process -Force
    Write-Host "[OK] DesktopPet 已终止"
}

# ---- Determine build/test mode ----
if ($RunTests -or $Quick) {
    $Label = if ($RunTests) { "Run Tests" } else { "Quick (compile + EditMode harness)" }
    $TestResultsFile = Join-Path $RootDir "logs\build\test_results.xml"
    $unityArgs = @(
        "-batchmode"
        "-nographics"
        "-projectPath", "."
        "-logFile", $LogFile
        "-runTests"
        "-testPlatform", "EditMode"
        "-testResults", $TestResultsFile
    )
} else {
    $Label = "Full build"

    $unityArgs = @(
        "-batchmode"
        "-nographics"
        "-quit"
        "-projectPath", "."
        "-logFile", $LogFile
        "-executeMethod", "BuildScript.BuildDesktopPet"
    )
}

# ---- Save current dir and CD to project ----
$OldCwd = Get-Location
try {
    Set-Location $ProjectDir

    Write-Host "[Build] $Label ..."
    Write-Host "[Build] CWD: $(Get-Location)"
    Write-Host "[Build] Args: $($unityArgs -join ' ')"
    Write-Host ""

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -NoNewWindow -Wait -PassThru
    $sw.Stop()

    $exitCode = $process.ExitCode
    $elapsed = $sw.Elapsed.ToString("mm\:ss")

    if ($exitCode -eq 0) {
        $Host.UI.RawUI.ForegroundColor = "Green"
        Write-Host "[OK] Build succeeded! ($elapsed)"

        if (-not $Quick) {
            $exe = Join-Path $DefaultOutputDir "DesktopPet.exe"
            if (Test-Path $exe) {
                $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
                Write-Host "[OK] Output: $exe ($size MB)"
            } else {
                Write-Host "[WARN] Build succeeded but DesktopPet.exe not found at expected path"
            }
        }
        exit 0
    } else {
        $Host.UI.RawUI.ForegroundColor = "Red"
        Write-Host "[FAIL] Build failed with exit code $exitCode ($elapsed)"
        if (Test-Path $LogFile) {
            Write-Host ""
            Write-Host "--- Last 20 lines of log ---"
            Get-Content $LogFile -Tail 20
            Write-Host "-----------------------------"
        }
        exit $exitCode
    }
} finally {
    if ($CreatedBuildDataRoot -and (Test-Path -LiteralPath $BuildDataRoot)) {
        Remove-Item -LiteralPath $BuildDataRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $OriginalDataRoot) {
        Remove-Item Env:FU_XUAN_DATA -ErrorAction SilentlyContinue
    } else {
        $env:FU_XUAN_DATA = $OriginalDataRoot
    }
    Set-Location $OldCwd
}
