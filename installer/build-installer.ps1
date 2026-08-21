<#
.SYNOPSIS
    符玄桌宠 — 阶段2 安装器构建脚本（Inno Setup）
.DESCRIPTION
    1. 确保 portable 便携目录就绪（可 -SkipPack 跳过重新打包）
    2. 自动获取 Inno Setup 6 便携编译器到 installer\innosetup\（仅首次）
    3. 编译 fuxuan-installer.iss → installer\dist\FuXuanSetup-<版本>.exe
    4. -Test：额外编译 lowest 权限测试变体，并做「静默安装 → 校验 → 静默卸载」本地验证
.USAGE
    .\installer\build-installer.ps1                 # 打包 + 编译生产版
    .\installer\build-installer.ps1 -SkipPack       # 仅编译（portable 已存在）
    .\installer\build-installer.ps1 -Test -SkipPack # 编译 + 本地静默安装验证
.NOTES
    测试变体: PrivilegesRequired=lowest + /SKIPENV，不写环境变量、不申请 UAC，
    安装到 D:\FuXuanTest 验证文件落地与卸载清理（完整验收仍需干净 VM，见方案 §八）。
#>
param(
    [switch]$SkipPack,
    [switch]$Test,
    [string]$Version = "1.0.8"
)

. "$PSScriptRoot\..\scripts\encoding\init-utf8.ps1"
$ErrorActionPreference = "Stop"
$Host.UI.RawUI.ForegroundColor = "Cyan"
Write-Host "============================================"
Write-Host "   Fu Xuan - Installer Builder (stage 2)"
Write-Host "============================================"

$IsccDir = Join-Path $PSScriptRoot "innosetup"
$Iscc = Join-Path $IsccDir "ISCC.exe"
$Downloads = Join-Path $PSScriptRoot "downloads"
$Dist = Join-Path $PSScriptRoot "dist"

# ── 1. portable 就绪 ──
if (-not $SkipPack) {
    Write-Host "`n[1/4] 打包便携目录..."
    & (Join-Path $PSScriptRoot "build-portable.ps1") -IncludeNode
} else {
    $portExe = Join-Path $PSScriptRoot "portable\DesktopPet.exe"
    if (-not (Test-Path $portExe)) { Write-Host "[ERROR] portable 不存在（先运行 build-portable.ps1）" -ForegroundColor Red; exit 1 }
    Write-Host "`n[1/4] 使用现有 portable 目录"
}

# ── 2. 获取 Inno Setup ──
Write-Host "`n[2/4] 检查 Inno Setup 编译器..."
if (-not (Test-Path $Iscc)) {
    New-Item -ItemType Directory -Path $Downloads -Force | Out-Null
    $setupExe = Join-Path $Downloads "innosetup-6.7.3.exe"
    if (-not (Test-Path $setupExe)) {
        Write-Host "    下载 Inno Setup 6.7.3（GitHub 直链）..."
        try {
            Invoke-WebRequest -Uri "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe" -OutFile $setupExe -UseBasicParsing
        } catch {
            # 兜底：解析 jrsoftware.org 重定向页中的真实地址
            Write-Host "    直链失败，解析 jrsoftware.org 重定向页..."
            $page = Invoke-WebRequest -Uri "https://jrsoftware.org/download.php/is.exe" -UseBasicParsing
            $m = [regex]::Match($page.Content, 'https://github\.com/jrsoftware/issrc/releases/download/is-6[^"''\s]+\.exe')
            if (-not $m.Success) { Write-Host "[ERROR] 无法定位 Inno Setup 下载地址" -ForegroundColor Red; exit 1 }
            Invoke-WebRequest -Uri $m.Value -OutFile $setupExe -UseBasicParsing
        }
    }
    # 完整性校验：必须是真的 PE 文件（MZ 头），防止存到 HTML 重定向页
    $head = [System.IO.File]::ReadAllBytes($setupExe)[0..1]
    if ($head[0] -ne 0x4D -or $head[1] -ne 0x5A) {
        Write-Host "[ERROR] 下载文件不是可执行文件（可能是重定向页），请检查网络" -ForegroundColor Red
        Remove-Item $setupExe -Force -ErrorAction SilentlyContinue
        exit 1
    }
    Write-Host "    解压便携编译器到 $IsccDir ..."
    # /PORTABLE=1：仅解压不安装（不写注册表/不建快捷方式）
    & $setupExe /VERYSILENT /SUPPRESSMSGBOXES /PORTABLE=1 /DIR=$IsccDir | Out-Null
    if (-not (Test-Path $Iscc)) {
        # 兜底：常规静默安装到本地目录
        & $setupExe /VERYSILENT /SUPPRESSMSGBOXES /DIR=$IsccDir /NOICONS | Out-Null
    }
}
if (-not (Test-Path $Iscc)) { Write-Host "[ERROR] ISCC.exe 不可用" -ForegroundColor Red; exit 1 }
Write-Host "[OK] ISCC: $Iscc"

# 便携解压会缺中文语言文件（29 语言独缺 Chinese），从官方仓库补下
$cnLang = Join-Path $IsccDir "Languages\ChineseSimplified.isl"
if (-not (Test-Path $cnLang)) {
    Write-Host "    补充中文语言文件 ChineseSimplified.isl ..."
    try {
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/ChineseSimplified.isl" -OutFile $cnLang -UseBasicParsing
    } catch {
        Write-Host "[WARN] 中文语言文件下载失败，安装器将仅英文显示" -ForegroundColor Yellow
    }
}

# ── 3. 编译生产版 ──
New-Item -ItemType Directory -Path $Dist -Force | Out-Null
Write-Host "`n[3/4] 编译生产安装器（admin 权限）..."
& $Iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot "fuxuan-installer.iss")
if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] 生产版编译失败" -ForegroundColor Red; exit $LASTEXITCODE }
$prodSetup = Join-Path $Dist "FuXuanSetup-$Version.exe"
Write-Host "[OK] 生产安装器: $prodSetup ($([math]::Round((Get-Item $prodSetup).Length/1MB,1)) MB)"

# ── 4. 测试变体 + 本地静默验证 ──
if ($Test) {
    Write-Host "`n[4/4] 编译测试变体并本地验证..."
    & $Iscc "/DMyAppVersion=$Version" "/DPrivileges=lowest" "/DOutputSuffix=-test" (Join-Path $PSScriptRoot "fuxuan-installer.iss")
    if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] 测试版编译失败" -ForegroundColor Red; exit $LASTEXITCODE }
    $testSetup = Join-Path $Dist "FuXuanSetup-$Version-test.exe"
    $testDir = "D:\FuXuanTest"

    # 4a. 静默安装（最低权限 + 跳过环境变量/组件脚本，避免污染用户环境与安装真实软件）
    Write-Host "    静默安装到 $testDir ..."
    if (Test-Path $testDir) { Remove-Item $testDir -Recurse -Force }
    $args = "/VERYSILENT /SUPPRESSMSGBOXES /SKIPENV /SKIPCOMPONENTS /NORESTART /DIR=`"$testDir`""
    $proc = Start-Process -FilePath $testSetup -ArgumentList $args -Wait -PassThru
    if ($proc.ExitCode -ne 0) { Write-Host "[ERROR] 静默安装失败 exit=$($proc.ExitCode)" -ForegroundColor Red; exit 1 }

    # 4b. 校验文件落地
    Write-Host "    校验安装产物..."
    $checks = @(
        @("DesktopPet.exe", "$testDir\DesktopPet.exe"),
        @("version.txt", "$testDir\version.txt"),
        @("bridge", "$testDir\bridge\openclaw_bridge.js"),
        @("bridge node", "$testDir\bridge\node\node.exe"),
        @("scripts\office", "$testDir\scripts\office\ppt_gen.py"),
        @("scripts\knowledge", "$testDir\scripts\knowledge\pdf_extract.py"),
        @("openclaw 包", "$testDir\bridge\node_modules\openclaw\dist\gateway-chat-BW6uyvQL.js")
    )
    $ok = $true
    foreach ($c in $checks) {
        if (Test-Path $c[1]) { Write-Host "    [OK] $($c[0])" } else { Write-Host "    [FAIL] $($c[0]) 缺失: $($c[1])"; $ok = $false }
    }
    if (-not $ok) { Write-Host "[ERROR] 安装产物校验失败" -ForegroundColor Red; exit 1 }

    # 4c. 静默卸载并校验清理
    $unins = Join-Path $testDir "unins000.exe"
    if (Test-Path $unins) {
        Write-Host "    静默卸载..."
        $proc2 = Start-Process -FilePath $unins -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -Wait -PassThru
        if ($proc2.ExitCode -ne 0) { Write-Host "[WARN] 卸载 exit=$($proc2.ExitCode)" }
        Start-Sleep -Seconds 2
        if (Test-Path $testDir) { Write-Host "    [WARN] 卸载后目录仍存在（请检查卸载日志或被占用文件）" } else { Write-Host "    [OK] 卸载后目录已清理" }
    } else {
        Write-Host "    [WARN] 未找到 unins000.exe（安装可能未完成）"
    }
    Write-Host "`n[PASS] 阶段2 本地验证完成：编译 + 静默安装 + 产物校验 + 静默卸载 ✅"
}

Write-Host "`n============================================"
Write-Host "[OK] 构建完成。dist: $Dist"
Write-Host "============================================"
