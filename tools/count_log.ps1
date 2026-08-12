# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\init-utf8.ps1"

$c = Get-Content 'C:\Users\25295\AppData\LocalLow\DefaultCompany\desktop pet\Player.log'
$out = @()
$out += "总行数=$($c.Count)"
$out += "翻译成功=$((@($c | ? {$_ -like '*翻译成功*'}).Count))"
$out += "翻译失败=$((@($c | ? {$_ -like '*翻译失败*' -or $_ -like '*API 请求失败*'}).Count))"
$out += "本地模板=$((@($c | ? {$_ -like '*本地模板命中*'}).Count))"
$out += "决策=$((@($c | ? {$_ -like '*决策:*'}).Count))"
$out += "镜鉴=$((@($c | ? {$_ -like '*镜鉴*'}).Count))"
$out += "问候生成=$((@($c | ? {$_ -like '*问候生成*'}).Count))"
$out += "意图分类=$((@($c | ? {$_ -like '*意图*' -or $_ -like '*ClassifyIntent*'}).Count))"
$out += "反思=$((@($c | ? {$_ -like '*反思*'}).Count))"
$out += "施法=$((@($c | ? {$_ -like '*施法*'}).Count))"
$out += "闲话=$((@($c | ? {$_ -like '*闲话生成*' -or $_ -like '*闲话*完成*'}).Count))"
$out | Out-File -Encoding utf8 'd:\Unity\projects\Desktop_per_pro\tools\count_out.txt'
Write-Host "done"
