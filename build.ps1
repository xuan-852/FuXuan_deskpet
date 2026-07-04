<#
.SYNOPSIS
    符玄桌宠 — 标准构建脚本
.DESCRIPTION
    在本地直接调用 Tuanjie 引擎（Unity 团结引擎）执行构建，无需 subst 虚拟盘。
    用法:
        .\build.ps1                     # 完整构建（默认）
        .\build.ps1 -Quick              # 仅验证编译（不输出可执行文件）
        .\build.ps1 -SkipPreCheck       # 跳过前置检查
        .\build.ps1 -OutputDir "D:\tmp" # 指定输出目录
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Quick -SkipPreCheck
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$UnityExe = "D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe",

    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = "D:\Unity\projects\Desktop_per_pro\code\desktop_unity",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "D:\Unity\projects\Desktop_per_pro\Build",

    [Parameter(Mandatory = $false)]
    [string]$LogFile = "D:\Unity\projects\Desktop_per_pro\build_log.txt",

    [switch]$Quick,
    [switch]$SkipPreCheck
)

# ============================================================
#  颜色输出辅助
# ============================================================
$Host.UI.RawUI.ForegroundColor = "Cyan"
Write-Host "╔══════════════════════════════════════════════════╗"
Write-Host "║      符玄桌宠 — 标准构建流程                     ║"
Write-Host "╚══════════════════════════════════════════════════╝"
Write-Host ""
$Host.UI.RawUI.ForegroundColor = "White"

# ============================================================
#  前置检查
# ============================================================
if (-not $SkipPreCheck)
{
    # 检查 Unity 可执行文件
    if (-not (Test-Path $UnityExe))
    {
        $Host.UI.RawUI.ForegroundColor = "Red"
        Write-Host "[❌] Unity 引擎未找到: $UnityExe"
        $Host.UI.RawUI.ForegroundColor = "White"
        exit 1
    }
    Write-Host "[✅] Unity 引擎: $UnityExe"

    # 检查项目目录
    if (-not (Test-Path "$ProjectPath\Assets"))
    {
        $Host.UI.RawUI.ForegroundColor = "Red"
        Write-Host "[❌] 项目路径无效: $ProjectPath (不存在 Assets 目录)"
        $Host.UI.RawUI.ForegroundColor = "White"
        exit 1
    }
    Write-Host "[✅] 项目路径: $ProjectPath"
}

# ============================================================
#  清理旧的构建产物
# ============================================================
if (-not $Quick)
{
    if (Test-Path $OutputDir)
    {
        Write-Host "[⏳] 清理旧构建产物: $OutputDir"
        Remove-Item -Path "$OutputDir\*" -Recurse -Force -ErrorAction SilentlyContinue
    }
    else
    {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
}

# ============================================================
#  构造 Unity 命令行参数
# ============================================================
$unityArgs = @(
    "-batchmode"
    "-quit"
    "-projectPath", "`"$ProjectPath`""
    "-logFile", "`"$LogFile`""
)

if ($Quick)
{
    # Quick 模式只做编译验证（不执行 BuildPlayer，而是依赖 Unity 的导入和编译）
    $unityArgs += "-executeMethod", "BuildScript.VerifyCompile"
    $buildLabel = "编译验证"
}
else
{
    $unityArgs += "-executeMethod", "BuildScript.BuildDesktopPet"
    $buildLabel = "完整构建"
}

$Host.UI.RawUI.ForegroundColor = "Yellow"
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host "  开始 $buildLabel ..."
Write-Host "  输出目录: $OutputDir"
Write-Host "  日志文件: $LogFile"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
$Host.UI.RawUI.ForegroundColor = "White"

# ============================================================
#  执行构建
# ============================================================
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$cmdLine = "& `"$UnityExe`" $($unityArgs -join ' ')"
Write-Host "[⏳] 命令行: $cmdLine"
Write-Host ""

$process = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -NoNewWindow -Wait -PassThru

$sw.Stop()
$elapsed = $sw.Elapsed

# ============================================================
#  结果解析
# ============================================================
Write-Host ""
$exitCode = $process.ExitCode

if ($exitCode -eq 0)
{
    $Host.UI.RawUI.ForegroundColor = "Green"
    Write-Host "[✅] $buildLabel 成功！耗时 $($elapsed.TotalSeconds.ToString('F1')) 秒"

    # 完整模式下验证产物
    if (-not $Quick)
    {
        $exePath = "$OutputDir\DesktopPet.exe"
        if (Test-Path $exePath)
        {
            $fileInfo = Get-Item $exePath
            $sizeMB = [math]::Round($fileInfo.Length / 1MB, 1)
            Write-Host "[✅] 可执行文件: $exePath ($sizeMB MB)"
        }
        else
        {
            $Host.UI.RawUI.ForegroundColor = "Yellow"
            Write-Host "[⚠️]  构建成功但未找到 DesktopPet.exe，请检查 BuildScript.cs 中的输出路径"
        }
    }

    $Host.UI.RawUI.ForegroundColor = "White"
    exit 0
}
else
{
    $Host.UI.RawUI.ForegroundColor = "Red"
    Write-Host "[❌] $buildLabel 失败！退出码: $exitCode (耗时 $($elapsed.TotalSeconds.ToString('F1')) 秒)"
    Write-Host ""

    # 从日志中提取最后 20 行错误信息
    if (Test-Path $LogFile)
    {
        Write-Host "── 日志末尾（错误摘要）──"
        $Host.UI.RawUI.ForegroundColor = "DarkRed"
        Get-Content $LogFile -Tail 20
        $Host.UI.RawUI.ForegroundColor = "White"
        Write-Host "──────────────────────────"
        Write-Host "完整日志: $LogFile"
    }

    $Host.UI.RawUI.ForegroundColor = "White"
    exit $exitCode
}
