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
    [string]$LogFile = "D:\Unity\projects\Desktop_per_pro\build_log.txt",
    [switch]$Quick,
    [switch]$RunTests,
    [switch]$NoKill
)

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
if ($RunTests) {
    $Label = "Run Tests"
    $TestResultsFile = Join-Path $RootDir "build_test_results.xml"
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
    $BuildMethod = if ($Quick) { "BuildScript.VerifyCompile" } else { "BuildScript.BuildDesktopPet" }
    $Label = if ($Quick) { "Quick (compile check)" } else { "Full build" }

    $unityArgs = @(
        "-batchmode"
        "-quit"
        "-projectPath", "."
        "-logFile", $LogFile
        "-executeMethod", $BuildMethod
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
    Set-Location $OldCwd
}
