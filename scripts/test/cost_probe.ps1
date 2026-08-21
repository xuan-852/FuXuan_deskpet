<#
.SYNOPSIS
    Token 成本探针 — 云端模式跑桌宠 N 分钟，采样 usage_log.jsonl 验证省 token 架构真实花费

.DESCRIPTION
    目标：在【真实云端模式】下运行桌宠，让 TokenBudgetManager 限流、PromptContextBudget
    裁剪、ToolResultBudget 压缩真实生效，然后从 usage_log.jsonl 汇总各 source 的
    调用次数/tokens/费用，验证 v0.3 架构是否真的省钱。

    设计要点：
    - 用 FU_XUAN_DATA 指向隔离临时目录（生产记忆零污染）
    - 【不建 .test_mode】→ 云端不被拦截，预算闸门真实放行/拒绝
    - 【显式传 --cloud】→ 云端模式（日常 DesktopPet.exe 默认本地，避免误烧额度）
    - 注入用户活动（inbox 文本消息走 chat；@@emote 触发互动）驱动真实调用
    - 定时采样 usage_log + Player.log 的成本闸门拦截留痕
    - 结束自动 kill + 清理隔离目录

    用法:
      .\scripts\test\cost_probe.ps1                 # 默认跑 30 分钟
      .\scripts\test\cost_probe.ps1 -DurationMin 60 # 跑 60 分钟
      .\scripts\test\cost_probe.ps1 -KeepAlive      # 结束后保留桌宠运行

    前置:
      已构建 Build\DesktopPet.exe（含 TokenBudgetManager 接入）；
      DEEPSEEK_API_KEY / GLM_API_KEY 环境变量已配置（用户级）。
#>

[CmdletBinding()]
param(
    [int]$DurationMin = 30,
    [switch]$KeepAlive
)

$ErrorActionPreference = "Continue"
# ★ 项目根硬编码（Start-Job 等环境下 $PSScriptRoot/$MyInvocation 均不可靠）
$RootDir = "D:\Unity\projects\Desktop_per_pro"
$Exe = Join-Path $RootDir "Build\DesktopPet.exe"
$TestData = Join-Path $env:TEMP "fuxuan_cost_probe"
$Inbox = Join-Path $TestData "inbox.txt"
$UsageLog = Join-Path $TestData "usage_log.jsonl"
$MirrorLog = Join-Path $TestData "logs\player_log.txt"
$ReportDir = Join-Path $RootDir "logs\build"
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
$Report = Join-Path $ReportDir "cost_probe_report.txt"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Token 成本探针（云端模式 $($DurationMin) 分钟）"
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "exe      : $Exe"
Write-Host "数据目录 : $TestData（隔离，生产记忆零污染）"
Write-Host "模式     : 云端（无 .test_mode / --cloud）"
Write-Host ""

if (-not (Test-Path $Exe)) { Write-Host "[FAIL] 未找到 exe: $Exe" -ForegroundColor Red; exit 1 }

# ── 0. 准备隔离目录 ──
Remove-Item -Recurse -Force $TestData -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $TestData | Out-Null
New-Item -ItemType Directory -Force (Split-Path $MirrorLog -Parent) | Out-Null

# ── 1. 启动（云端模式，继承用户环境变量） ──
$env:FU_XUAN_DATA = $TestData
$env:DEEPSEEK_API_KEY = [Environment]::GetEnvironmentVariable("DEEPSEEK_API_KEY", "User")
$env:GLM_API_KEY = [Environment]::GetEnvironmentVariable("GLM_API_KEY", "User")
Write-Host "[start] 启动桌宠（DEEPSEEK key 配置=$([bool]$env:DEEPSEEK_API_KEY)）..."
Start-Process -FilePath $Exe -ArgumentList '--cloud' -WorkingDirectory (Split-Path $Exe -Parent)
Start-Sleep -Seconds 20
if (-not (Get-Process -Name DesktopPet -ErrorAction SilentlyContinue)) {
    Write-Host "[FAIL] 桌宠启动失败" -ForegroundColor Red
    exit 1
}
Write-Host "[ok] 桌宠已启动"

# ── 2. 活动注入（模拟真实用户，驱动 chat/idle 调用） ──
# 消息列表：穿插聊天消息 + 表情互动，触发 ChatManager 云端调用与预算闸门
$activities = @(
    "你好呀，今天心情怎么样？",
    "@@emote:happy",
    "帮我看看现在几点了？",
    "今天适合出门散步吗？",
    "@@emote:thinking",
    "你还记得我的名字吗？",
    "给我讲个笑话吧",
    "@@emote:blush",
    "现在的工作效率怎么样？",
    "周末有什么计划建议？"
)

$deadline = (Get-Date).AddMinutes($DurationMin)
$sampleCount = 0
$activityIdx = 0
$lastActivityTime = Get-Date

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 30

    # 每 90 秒注入一条活动（驱动调用）
    if (((Get-Date) - $lastActivityTime).TotalSeconds -ge 90 -and $activityIdx -lt $activities.Count) {
        $act = $activities[$activityIdx]
        try {
            Set-Content -Path $Inbox -Value $act -Encoding UTF8
            Write-Host "[inject] $act" -ForegroundColor Yellow
        } catch { Write-Host "[warn] inbox 写入失败: $($_.Exception.Message)" }
        $activityIdx++
        $lastActivityTime = Get-Date
    }

    # 每 2 分钟采样一次 usage_log
    $sampleCount++
    if ($sampleCount % 4 -eq 0) {
        if (Test-Path $UsageLog) {
            $lines = Get-Content $UsageLog -ErrorAction SilentlyContinue
            $totalCost = 0.0; $bySource = @{}
            foreach ($l in $lines) {
                try {
                    $o = $l | ConvertFrom-Json
                    $totalCost += [double]$o.cost
                    if ($bySource.ContainsKey($o.src)) { $bySource[$o.src]++ } else { $bySource[$o.src] = 1 }
                } catch {}
            }
            $srcStr = ($bySource.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ", "
            Write-Host ("[sample] 已运行 {0} 分钟 | 调用 {1} 次 ({2}) | 累计费用 ¥{3:F4}" -f `
                [math]::Round(((Get-Date) - (Get-Date).AddMinutes(-$DurationMin)).TotalMinutes), `
                $lines.Count, $srcStr, $totalCost) -ForegroundColor Green
        }
    }
}

# ── 3. 结束：收尾采样 + 汇总报告 ──
Write-Host ""
Write-Host "========== 探针结束，汇总 ==========" -ForegroundColor Cyan

$report = @()
$report += "Token 成本探针报告（云端模式 $($DurationMin) 分钟）"
$report += "时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$report += "数据目录: $TestData"
$report += ""

if (Test-Path $UsageLog) {
    $lines = Get-Content $UsageLog -ErrorAction SilentlyContinue
    $report += "=== usage_log.jsonl 共 $($lines.Count) 次云端/本地调用 ==="
    $report += "| source | 次数 | prompt | hit | comp | cost(¥) |"
    $report += "|--------|------|--------|-----|------|---------|"
    $totals = @{ count = 0; prompt = 0; hit = 0; comp = 0; cost = 0.0 }
    $bySrc = @{}
    foreach ($l in $lines) {
        try {
            $o = $l | ConvertFrom-Json
            $totals.count++
            $totals.prompt += [long]$o.prompt
            $totals.hit += [long]$o.hit
            $totals.comp += [long]$o.comp
            $totals.cost += [double]$o.cost
            $key = $o.src
            if (-not $bySrc.ContainsKey($key)) { $bySrc[$key] = @{ count=0; prompt=0; hit=0; comp=0; cost=0.0 } }
            $bySrc[$key].count++
            $bySrc[$key].prompt += [long]$o.prompt
            $bySrc[$key].hit += [long]$o.hit
            $bySrc[$key].comp += [long]$o.comp
            $bySrc[$key].cost += [double]$o.cost
        } catch {}
    }
    foreach ($k in $bySrc.Keys | Sort-Object) {
        $s = $bySrc[$k]
        $report += "| $k | $($s.count) | $($s.prompt) | $($s.hit) | $($s.comp) | $($s.cost.ToString('F4')) |"
    }
    $hitRate = 0
    if (($totals.prompt + $totals.hit) -gt 0) { $hitRate = [math]::Round($totals.hit * 100.0 / ($totals.prompt + $totals.hit), 1) }
    $report += ""
    $report += "合计: $($totals.count) 次, prompt=$($totals.prompt), hit=$($totals.hit), comp=$($totals.comp)"
    $report += "缓存命中率: $hitRate%  累计费用: ¥$($totals.cost.ToString('F4'))"
    $report += ""
    $report += "=== 成本闸门拦截留痕（Player.log） ==="
    if (Test-Path $MirrorLog) {
        $gates = Get-Content $MirrorLog | Select-String -Pattern '成本闸门' | Select-Object -Last 10
        if ($gates) { $report += ($gates | ForEach-Object { $_.Line.Trim() }) } else { $report += "(无拦截记录)" }
    } else { $report += "(镜像日志未生成)" }
} else {
    $report += "[FAIL] usage_log.jsonl 未生成——云端调用可能被拦截，检查 .test_mode / --ollama"
}

$report | Set-Content -Path $Report -Encoding UTF8
Write-Host ($report -join "`n")

# ── 4. 清理 ──
if (-not $KeepAlive) {
    Stop-Process -Name DesktopPet -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-Item -Recurse -Force $TestData -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "[ok] 已结束桌宠并清理隔离目录"
} else {
    Write-Host ""
    Write-Host "[ok] -KeepAlive：桌宠保持运行，隔离目录保留于 $TestData"
}
Write-Host "[report] 完整报告: $Report"
