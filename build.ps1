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

# P0 构建负载保护：CleanBeeCache（显式清 Library\Bee）、MaxCores（限制核数）、NoThrottle（关闭节流）
param(
    [string]$UnityExe = "D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe",
    [string]$LogFile = "D:\Unity\projects\Desktop_per_pro\logs\build\build_log.txt",
    [string]$DataRoot = "",
    [switch]$Quick,
    [switch]$RunTests,
    [switch]$NoKill,
    [switch]$CleanBeeCache,
    [int]$MaxCores = 0,
    [switch]$NoThrottle
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

# ---- P0 构建负载保护：Bee 缓存清理开关（BuildScript.cs 读取） ----
if ($CleanBeeCache) {
    $env:BEE_CLEAN_CACHE = "1"
    Write-Host "[Build] 显式清理 Library\Bee 缓存（强制全量重建）"
} else {
    Remove-Item Env:BEE_CLEAN_CACHE -ErrorAction SilentlyContinue
    Write-Host "[Build] 保留 Library\Bee 缓存，走增量构建（需全量请加 -CleanBeeCache）"
}

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

# Tuanjie can leave its licensing helper alive after a batchmode timeout.  A
# stale helper may block the next editor during the licensing handshake before
# -logFile is opened, which looks like a compiler hang.  Keep the editor check
# above conservative (never terminate an interactive editor), but reset only
# the helper/crash-handler processes that are safe to restart before a build.
$BuildHelperProcs = @(
    (Get-Process -Name "Tuanjie.Licensing.Client" -ErrorAction SilentlyContinue),
    (Get-Process -Name "TuanjieCrashHandler32" -ErrorAction SilentlyContinue)
) | Where-Object { $null -ne $_ }
if ($BuildHelperProcs) {
    $BuildHelperPids = ($BuildHelperProcs | ForEach-Object { $_.Id }) -join ", "
    if ($NoKill) {
        $Host.UI.RawUI.ForegroundColor = "Red"
        Write-Host "[ERROR] 检测到 Tuanjie 构建辅助进程 (PID: $BuildHelperPids)，已加 -NoKill，请先关闭后再构建"
        exit 1
    }

    $Host.UI.RawUI.ForegroundColor = "Yellow"
    Write-Host "[WARN] 检测到上次构建遗留的 Tuanjie 辅助进程 (PID: $BuildHelperPids)"
    Write-Host "[BUILD] 终止 Licensing/CrashHandler 辅助进程，避免授权握手阻塞..."
    $BuildHelperProcs | Stop-Process -Force
    Write-Host "[OK] Tuanjie 构建辅助进程已清理"
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
# editor.  A PID can be reused by an unrelated process (and on Windows the
# marker may point at a short-lived conhost wrapper), so a live PID alone is
# not enough to prove that ILPP is still running.  Keep a marker only when the
# recorded process identifies itself as an ILPP/Bee post-processor.
$IlppPidPath = Join-Path $ProjectDir "Library\ilpp.pid"
if (Test-Path -LiteralPath $IlppPidPath) {
    $IlppPidText = (Get-Content -LiteralPath $IlppPidPath -Raw -ErrorAction Stop).Trim()
    $IlppPid = 0
    if ([int]::TryParse($IlppPidText, [ref]$IlppPid)) {
        $IlppProc = Get-Process -Id $IlppPid -ErrorAction SilentlyContinue
        $IlppInfo = $null
        try {
            $IlppInfo = Get-CimInstance Win32_Process -Filter "ProcessId=$IlppPid" -ErrorAction Stop
        } catch {
            # Process metadata is best-effort; the process-name check below
            # still handles the normal case on older PowerShell installations.
        }
        $IlppIdentity = @(
            if ($IlppProc) { [string]$IlppProc.ProcessName }
            if ($IlppInfo) { [string]$IlppInfo.Name; [string]$IlppInfo.CommandLine }
        ) -join " "
        $IsKnownIlpp = $IlppIdentity -match '(?i)(ilpp|bee\.backend|unitylinker|il2cpp)'
        $IsPidReused = $false
        try {
            $MarkerTime = (Get-Item -LiteralPath $IlppPidPath -ErrorAction Stop).LastWriteTime
            $ProcessStartTime = $IlppProc.StartTime
            # A process started well after the marker cannot be the process
            # that created it.  The margin avoids clock-resolution races.
            $IsPidReused = $ProcessStartTime -gt $MarkerTime.AddSeconds(5)
        } catch {
            # If timestamps are unavailable, retain the conservative block.
        }

        if (-not $IlppProc) {
            Remove-Item -LiteralPath $IlppPidPath -Force -ErrorAction Stop
            Write-Host "[BUILD] Removed stale ILPP PID marker: $IlppPidPath (PID $IlppPid not running)"
        } elseif ($IsPidReused) {
            Remove-Item -LiteralPath $IlppPidPath -Force -ErrorAction Stop
            Write-Host "[WARN] Removed stale ILPP PID marker: PID $IlppPid was reused by '$IlppIdentity' after the marker was written" -ForegroundColor Yellow
        } elseif ($IsKnownIlpp) {
            Write-Host "[ERROR] Active ILPP process found (PID $IlppPid); refusing to remove marker" -ForegroundColor Red
            exit 1
        } else {
            Write-Host "[ERROR] Live ILPP marker points to an unidentified process (PID ${IlppPid}: '$IlppIdentity'); refusing to remove marker" -ForegroundColor Red
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
$TestResultsFile = Join-Path $RootDir "logs\build\test_results.xml"
if ($RunTests) {
    $Label = "Run Tests (EditMode)"
    # 先删旧结果，确保能校验本次新鲜结果；去掉 -quit（否则测试运行器会提前退出不写结果）
    Remove-Item $TestResultsFile -Force -ErrorAction SilentlyContinue
    $unityArgs = @(
        "-batchmode"
        "-nographics"
        "-projectPath", "."
        "-logFile", $LogFile
        "-runTests"
        "-testPlatform", "EditMode"
        "-testResults", $TestResultsFile
    )
} elseif ($Quick) {
    $Label = "Quick (compile + EditMode harness)"
    $unityArgs = @(
        "-batchmode"
        "-nographics"
        "-quit"
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

# ---- P0 构建负载保护：CPU 亲和掩码 + 降优先级 + 子进程监视 ----
# 限制构建进程及其后代（Bee.Backend/Roslyn/IL2CPP 等）可用核数并降低优先级，
# 避免 Unity 构建打满全部核、长时间触发热保护/偶发重启。
function Get-BuildThreadThrottleMask {
    param([int]$TotalCores, [int]$MaxCores)
    if ($MaxCores -le 0) { $MaxCores = [Math]::Max(2, [Math]::Ceiling($TotalCores / 2)) }
    if ($MaxCores -gt $TotalCores) { $MaxCores = $TotalCores }
    $mask = 0
    for ($i = 0; $i -lt $MaxCores; $i++) { $mask = $mask -bor (1 -shl $i) }
    return @{ Mask = $mask; Cores = $MaxCores }
}

function Set-ProcessThrottle {
    param([int]$ProcId, [int]$Mask)
    try {
        $p = Get-Process -Id $ProcId -ErrorAction Stop
        $p.ProcessorAffinity = [IntPtr]$Mask
        $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal
        return $true
    } catch { return $false }
}

function Get-BuildDescendantPids {
    param([int]$RootPid)
    $result = @($RootPid)
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($pr in @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)) {
            if ($result -contains $pr.ParentProcessId -and -not ($result -contains $pr.ProcessId)) {
                $result += $pr.ProcessId; $changed = $true
            }
        }
    }
    return $result
}
try {
    Set-Location $ProjectDir

    Write-Host "[Build] $Label ..."
    Write-Host "[Build] CWD: $(Get-Location)"
    Write-Host "[Build] Args: $($unityArgs -join ' ')"
    Write-Host ""

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -NoNewWindow -PassThru

    # ---- P0 构建负载保护：限制 CPU 亲和 + 降优先级 + 监视子进程 ----
    $throttle = -not $NoThrottle
    $mask = 0
    $effCores = 0
    if ($throttle) {
        $totalCores = [Environment]::ProcessorCount
        $t = Get-BuildThreadThrottleMask -TotalCores $totalCores -MaxCores $MaxCores
        $mask = $t.Mask; $effCores = $t.Cores
        $null = Set-ProcessThrottle -ProcId $process.Id -Mask $mask
        Write-Host ("[Throttle] 构建负载保护: 逻辑核={0}, 限制={1} 核, 亲和掩码=0x{2:X}, 优先级=BelowNormal" -f $totalCores, $effCores, $mask) -ForegroundColor Yellow
    }

    $cpuSamples = @()
    while (-not $process.HasExited) {
        if ($throttle) {
            foreach ($childId in (Get-BuildDescendantPids -RootPid $process.Id)) {
                if ($childId -ne $process.Id) { $null = Set-ProcessThrottle -ProcId $childId -Mask $mask }
            }
        }
        try {
            $v = (Get-Counter "\Processor(_Total)\% Processor Time" -SampleInterval 1 -MaxSamples 1).CounterSamples[0].CookedValue
            if ($null -ne $v) { $cpuSamples += $v }
        } catch { /* 计数器不可用时忽略 */ }
        Start-Sleep -Milliseconds 2000
    }
    $sw.Stop()

    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $elapsed = $sw.Elapsed.ToString("mm\:ss")

    # 兜底：Start-Process -NoNewWindow 下 ExitCode 偶发为空（build-workflow 已知坑）。
    # 此时按 build-log 内容判定成功/失败，避免把实际成功的构建误判为失败。
    if ($null -eq $exitCode) {
        $logFailed = $false
        if (Test-Path $LogFile) {
            $logText = Get-Content $LogFile -Raw -ErrorAction SilentlyContinue
            $logFailed = $logText -match '(?m)\berror\s+CS\d{4}\b|Scripts have compiler errors|Build failed|BuildFailed'
        }
        $exitCode = if ($logFailed) { 1 } else { 0 }
        Write-Host ("[Throttle] ExitCode 为空，按 build-log 判定: {0}" -f $(if ($logFailed) { "失败" } else { "成功" })) -ForegroundColor Yellow
    }

    if ($cpuSamples.Count -gt 0) {
        $avg = [Math]::Round(($cpuSamples | Measure-Object -Average).Average, 0)
        $max = [Math]::Round(($cpuSamples | Measure-Object -Maximum).Maximum, 0)
        Write-Host ("[Throttle] 构建期 CPU 占用: 均值 {0}%, 峰值 {1}%，保护=({2} 核/{3} 逻辑核)" -f $avg, $max, $effCores, [Environment]::ProcessorCount) -ForegroundColor Yellow
    }

    # ---- RunTests 门禁：必须校验根节点的 test-run result 与 failed 计数 ----
    if ($RunTests) {
        $testRan = $false
        $testPassed = $false
        $testFailed = 0
        if (Test-Path $TestResultsFile) {
            $tr = Get-Content $TestResultsFile -Raw -ErrorAction SilentlyContinue
            # 取根 <test-run ... result="X" ... failed="N">（不是内层子套件的 result="Passed"）。
            # 只要失败数为 0 即视为通过；含 Ignore 时 NUnit 根结果可能是 "Skipped:Ignored"，不算失败。
            if ($tr -match '<test-run[^>]*\bresult="([^"]+)"[^>]*\bfailed="(\d+)"') {
                $testRan = $true
                $testFailed = [int]$Matches[2]
                $testPassed = ($testFailed -eq 0)
            }
        }
        if (-not $testRan -or -not $testPassed) {
            $Host.UI.RawUI.ForegroundColor = "Red"
            Write-Host "[FAIL] EditMode 测试未通过: $TestResultsFile（ran=$testRan, failed=$testFailed）"
            if (Test-Path $TestResultsFile) { Get-Content $TestResultsFile -TotalCount 2 }
            exit 1
        }
        Write-Host "[OK] EditMode 测试通过: $TestResultsFile (failed=0)" -ForegroundColor Green
    }

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
    Remove-Item Env:BEE_CLEAN_CACHE -ErrorAction SilentlyContinue
    Set-Location $OldCwd
}
