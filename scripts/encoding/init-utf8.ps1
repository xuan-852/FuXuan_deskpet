<#
.SYNOPSIS
    项目统一编码协议 —— PowerShell UTF-8 环境初始化
.DESCRIPTION
    所有含中文的 .ps1 脚本在头部 dot-source 本文件，即可统一解决
    PowerShell 5.1 的四类乱码问题：
      1. 控制台输出中文乱码     → 设置 [Console]::OutputEncoding
      2. 管道/重定向输出乱码    → 设置 $OutputEncoding + $PSDefaultParameterValues
      3. Invoke-RestMethod 发中文 body 变 '?' → 强制 UTF-8 body 编码
      4. Get-Content 读无 BOM UTF-8 文件乱码  → 默认按 UTF-8 读取

    用法（脚本第一行，注释之后）:
        . "$PSScriptRoot\..\encoding\init-utf8.ps1"   # 脚本在 scripts/ 下其他子目录时
        或（脚本与 init-utf8.ps1 同目录时，即 scripts/encoding/ 内）
        . "$PSScriptRoot\init-utf8.ps1"

    本文件必须保存为 UTF-8 with BOM（.editorconfig 已强制）。
.NOTES
    兼容 PowerShell 5.1 与 7+（pwsh 7 本身默认 UTF-8，本脚本自动跳过重复设置）。
#>

# ── 1. 控制台输出编码 → UTF-8（PS 5.1 默认 GBK/cp936）──
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

# ── 2. 管道/字符串编码 → UTF-8 ──
$OutputEncoding = [System.Text.Encoding]::UTF8

# Invoke-RestMethod / Invoke-WebRequest 发送 body 时强制 UTF-8 字节
$PSDefaultParameterValues['Invoke-RestMethod:ContentType'] = 'application/json; charset=utf-8'
$PSDefaultParameterValues['Invoke-WebRequest:ContentType']  = 'application/json; charset=utf-8'

# Get-Content 默认按 UTF-8 读取（PS 5.1 默认按 ANSI/GBK 读无 BOM UTF-8 文件 → 乱码）
$PSDefaultParameterValues['Get-Content:Encoding'] = 'UTF8'
# Out-File / Add-Content 默认按 UTF-8 写出（与 .editorconfig 协议一致）
$PSDefaultParameterValues['Out-File:Encoding']    = 'utf8'

# ── 3. chcp 65001 同步代码页（仅 PS 5.1 原生控制台需要）──
try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "chcp.com"
    $psi.Arguments = "65001"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    [System.Diagnostics.Process]::Start($psi) | Out-Null
} catch { }

# ── 4. 全局函数：UTF-8 安全发送 JSON body ──
<#
.SYNOPSIS
    以 UTF-8 字节数组发送 JSON POST，彻底避免中文变 '?'。
.USAGE
    $resp = Invoke-Utf8Json -Uri "http://localhost:3000/api/push" `
        -Token "xxx" -Body @{ title="测试"; body="中文内容" }
#>
function Invoke-Utf8Json {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [string]$Token,
        [Parameter(Mandatory = $true)][hashtable]$Body,
        [int]$TimeoutSec = 10
    )
    $json = $Body | ConvertTo-Json -Depth 10
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    Invoke-RestMethod -Uri $Uri -Method Post -Body $bytes `
        -ContentType "application/json; charset=utf-8" `
        -Headers $headers -TimeoutSec $TimeoutSec
}

Write-Verbose "[init-utf8] PowerShell 编码协议已生效: UTF-8" 
