# 精确检测文件编码：UTF-8 BOM / UTF-8 无BOM / GBK / 纯ASCII / 其他
param(
    [string]$Root = "D:\Unity\projects\Desktop_per_pro",
    [string]$Pattern = "*.cs"
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\init-utf8.ps1"

$files = Get-ChildItem $Root -Recurse -Filter $Pattern -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(Library|Build|obj|node_modules|\.git|Logs)\\' }

$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)  # 严格：非法字节抛异常
$gbk = [System.Text.Encoding]::GetEncoding(936)

$stats = @{}
$result = @()
$total = 0
foreach ($f in $files) {
    $total++
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $enc = ""
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $enc = "UTF8_BOM"
    } else {
        $hasHigh = $false
        foreach ($b in $bytes) { if ($b -ge 0x80) { $hasHigh = $true; break } }
        if (-not $hasHigh) {
            $enc = "ASCII"
        } else {
            # 严格 UTF-8 解码测试
            try {
                $null = $strictUtf8.GetString($bytes)
                $enc = "UTF8_NO_BOM"
            } catch {
                # GBK 往返测试：解码再编码，字节一致则是合法 GBK
                try {
                    $s = $gbk.GetString($bytes)
                    $reBytes = $gbk.GetBytes($s)
                    if ([System.Linq.Enumerable]::SequenceEqual([byte[]]$bytes, [byte[]]$reBytes)) {
                        $enc = "GBK"
                    } else {
                        $enc = "OTHER_OR_MIXED"
                    }
                } catch { $enc = "OTHER_OR_MIXED" }
            }
        }
    }
    if (-not $stats.ContainsKey($enc)) { $stats[$enc] = 0 }
    $stats[$enc]++
    if ($enc -ne "UTF8_BOM" -and $enc -ne "ASCII") {
        $result += [PSCustomObject]@{ File = $f.FullName.Replace($Root, ""); Enc = $enc }
    }
}

"=== $Pattern 共 $total 个文件 ==="
$stats.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Key): $($_.Value) files" }
""
"=== 非 UTF8_BOM 且非 ASCII 的文件（按编码分组）==="
$result | Group-Object Enc | ForEach-Object {
    "--- $($_.Name): $($_.Count) ---"
    $_.Group | Select-Object -First 40 | ForEach-Object { "  $($_.File)" }
    if ($_.Count -gt 40) { "  ... 等 $($_.Count) 个" }
}
