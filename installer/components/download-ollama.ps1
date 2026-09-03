param(
    [Parameter(Mandatory = $true)] [string]$OutputPath,
    [Parameter(Mandatory = $true)] [string]$LogPath,
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-DownloadLog([string]$Message) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Add-Content -LiteralPath $LogPath -Value ("[{0}] {1}" -f $stamp, $Message) -Encoding UTF8
}

$mirror = [Environment]::GetEnvironmentVariable('FU_OLLAMA_MIRROR_URL')
$urls = @(
    'https://ollama.com/download/OllamaSetup.exe'
)
if (-not [string]::IsNullOrWhiteSpace($mirror)) {
    $urls += $mirror.Trim()
} else {
    # 国内回退源。它可能继续重定向到 GitHub，仍由 curl 负责跟随重定向。
    $urls += 'https://ollama.ac.cn/download/OllamaSetup.exe'
}

$tempPath = "$OutputPath.download"
foreach ($url in $urls) {
    if ([string]::IsNullOrWhiteSpace($url)) { continue }
    try {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        Write-DownloadLog "尝试下载: $url"

        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($null -eq $curl) {
            throw '系统未找到 curl.exe，无法执行带重定向和重试的下载。'
        }

        & $curl.Source '--location' '--fail' '--retry' '3' '--retry-delay' '5' '--connect-timeout' '20' '--max-time' ([string]$TimeoutSec) '--output' $tempPath $url 2>&1 |
            ForEach-Object { Add-Content -LiteralPath $LogPath -Value ([string]$_) -Encoding UTF8 }
        if ($LASTEXITCODE -ne 0) {
            throw "curl 退出码 $LASTEXITCODE"
        }
        if (-not (Test-Path -LiteralPath $tempPath)) {
            throw '下载命令成功返回，但没有生成文件。'
        }
        $length = (Get-Item -LiteralPath $tempPath).Length
        if ($length -lt 1048576) {
            throw "下载文件过小（$length bytes），疑似错误页或截断文件。"
        }

        $signature = Get-AuthenticodeSignature -FilePath $tempPath
        Write-DownloadLog ("文件大小: {0} bytes; Authenticode: {1}" -f $length, $signature.Status)
        if ($signature.Status -ne 'Valid' -and $url -notlike 'https://ollama.com/*') {
            throw '国内回退源文件签名无效，拒绝执行。'
        }

        Move-Item -LiteralPath $tempPath -Destination $OutputPath -Force
        Write-DownloadLog "下载成功，来源: $url"
        exit 0
    } catch {
        Write-DownloadLog ("下载失败: " + $_.Exception.Message)
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}

Write-DownloadLog '所有下载源均失败。'
exit 1
