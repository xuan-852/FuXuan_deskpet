# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\init-utf8.ps1"

$log = 'C:\Users\25295\AppData\LocalLow\DefaultCompany\desktop pet\Player.log'
$c = Get-Content $log
Write-Output "=== API 请求失败 最近 15 条 ==="
@($c | Where-Object { $_ -like '*API 请求失败*' }) | Select-Object -Last 15 | ForEach-Object { $_.Substring(0, [Math]::Min(200, $_.Length)) }
Write-Output "=== 闲话/IdleChat 相关 最近 10 条 ==="
@($c | Where-Object { $_ -like '*闲话*' -or $_ -like '*IdleChat*' }) | Select-Object -Last 10 | ForEach-Object { $_.Substring(0, [Math]::Min(200, $_.Length)) }
Write-Output "=== 反思 相关 最近 5 条 ==="
@($c | Where-Object { $_ -like '*反思*' }) | Select-Object -Last 5 | ForEach-Object { $_.Substring(0, [Math]::Min(200, $_.Length)) }
Write-Output "=== 时间范围 ==="
($c | Select-Object -First 1).Substring(0, [Math]::Min(150, ($c | Select-Object -First 1).Length))
($c | Select-Object -Last 1).Substring(0, [Math]::Min(150, ($c | Select-Object -Last 1).Length))
