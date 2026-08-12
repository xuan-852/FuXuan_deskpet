# 端到端验证脚本：中文长文档 → 分块生成 → xelatex 编译
# 用法: .\test_chunked_latex.ps1 [-Desc "需求文本"] [-DescFile 文件路径] [-Title "标题"] [-Mode auto|chunked]
param(
    [string]$Desc = "",
    [string]$DescFile = "",
    [string]$Title = "test_chunked_verify",
    [string]$Mode = "auto"
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\tools\init-utf8.ps1"

if ($DescFile -and (Test-Path $DescFile)) {
    $Desc = [System.IO.File]::ReadAllText($DescFile, (New-Object System.Text.UTF8Encoding $false))
}
if (-not $Desc) {
    $Desc = "生成中文 LaTeX 长文档《ESP32-S3 开发板学习文档》，面向嵌入式比赛初学者，语言通俗，约 50 页。章节包括：1.GPIO 入门与实践（含代码示例）2.定时器详解 3.触摸传感器 4.ADC 模拟转换 5.WiFi 连接 6.Bluetooth 低功耗 7.FreeRTOS 任务与调度 8.低功耗设计 9.综合实战项目（智能环境监测站）"
}

$bodyObj = @{
    source        = ""
    description   = $Desc
    title         = $Title
    compiler      = "xelatex"
    pin_to_desktop = $false
    mode          = $Mode
}
$body = $bodyObj | ConvertTo-Json -Compress
Write-Host "Sending request (UTF-8, $($body.Length) bytes, mode=$Mode)..." -ForegroundColor Cyan

$jsonFile = Join-Path $env:TEMP ("latex_req_" + [guid]::NewGuid().ToString("N") + ".json")
[System.IO.File]::WriteAllText($jsonFile, $body, (New-Object System.Text.UTF8Encoding $false))
Write-Host ("Sending request (UTF-8, {0} bytes, mode={1})..." -f $body.Length, $Mode) -ForegroundColor Cyan

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& curl.exe -s -X POST -H "Content-Type: application/json; charset=utf-8" --data-binary "@$jsonFile" --max-time 850 -w "`nHTTP_STATUS:%{http_code}" http://127.0.0.1:19876/compile_latex
$sw.Stop()
Write-Host ("`nFinished in {0}s" -f [math]::Round($sw.Elapsed.TotalSeconds, 1)) -ForegroundColor Cyan
Remove-Item $jsonFile -ErrorAction SilentlyContinue
