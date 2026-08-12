# ============================================================
# 像素图反推工具 — 读取图片逐像素颜色，反推出颜色代码分布表
# 用法: powershell -ExecutionPolicy Bypass -File read_pixel_map.ps1 -Image "路径\图片.png"
# 输出: 控制台打印 几×几 的颜色代码表格（可复制回填给 fuxuan_pixel_art.ps1）
# ============================================================
param(
    [Parameter(Mandatory=$true)][string]$Image,
    [switch]$GridOnly        # 只输出网格表格，不输出调色板匹配信息
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\..\encoding\init-utf8.ps1"

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Image)) { Write-Error "图片不存在: $Image"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap($Image)
$w = $bmp.Width; $h = $bmp.Height
Write-Host "尺寸: ${w}x${h}"

# ---- 调色板（可扩充：从用户提供的颜色表里加）----
# 每个条目: 代码 -> (R, G, B, 描述)
$pal = @(
    @('M7',  0xC0,0xB0,0xA0,'浅棕/米色'),
    @('E18', 0xFF,0xC0,0xCB,'浅粉'),
    @('D19', 0xFF,0xB6,0xC1,'粉红'),
    @('E13', 0xFF,0x69,0xB4,'热粉'),
    @('E3',  0xFF,0x14,0x93,'深玫红'),
    @('E17', 0xFF,0x66,0xB2,'中粉'),
    @('E22', 0xFF,0xA0,0x7A,'浅橙'),
    @('E16', 0xFF,0x8C,0x00,'橙'),
    @('D18', 0xFF,0x63,0x47,'番茄红'),
    @('H22', 0x8B,0x45,0x13,'棕'),
    @('H7',  0x00,0x00,0x00,'黑'),
    @('H10', 0x80,0x80,0x80,'灰'),
    @('H5',  0x4B,0x00,0x82,'靛蓝'),
    @('H8',  0x48,0x3D,0x8B,'深蓝'),
    @('E9',  0x93,0x70,0xDB,'紫'),
    @('D6',  0x8A,0x2B,0xE2,'蓝紫'),
    @('E1',  0xFF,0xE4,0xE1,'浅肉色'),
    @('M11', 0xDE,0xB8,0x87,'棕褐')
)

function Get-NearestColorCode([int]$r, [int]$g, [int]$b) {
    $best = $null; $bestDist = [double]::MaxValue
    foreach ($c in $pal) {
        $dr = $r - $c[1]; $dg = $g - $c[2]; $db = $b - $c[3]
        $dist = $dr*$dr + $dg*$dg + $db*$db
        if ($dist -lt $bestDist) { $bestDist = $dist; $best = $c }
    }
    # 透明像素 → 空白
    return $best
}

$TOL = 20  # 匹配容差（欧氏距离），超过则标记为未知色 ???
$unknownSet = @{}

# ---- 生成网格表格 ----
$grid = @()
for ($y = 0; $y -lt $h; $y++) {
    $cells = @()
    for ($x = 0; $x -lt $w; $x++) {
        $p = $bmp.GetPixel($x, $y)
        if ($p.A -lt 128) { $cells += '_'; continue }
        $best = Get-NearestColorCode $p.R $p.G $p.B
        $dr = $p.R - $best[1]; $dg = $p.G - $best[2]; $db = $p.B - $best[3]
        $dist = [math]::Sqrt($dr*$dr + $dg*$dg + $db*$db)
        if ($dist -gt $TOL) {
            $code = "?$($p.R),$($p.G),$($p.B)"
            $cells += '???'
            $key = "#$($p.R.ToString('X2'))$($p.G.ToString('X2'))$($p.B.ToString('X2'))"
            $unknownSet[$key] = "R=$($p.R) G=$($p.G) B=$($p.B) (x=$x,y=$y)"
        } else {
            $cells += $best[0]
        }
    }
    $grid += $cells
}

# ---- 输出网格（对齐宽度）----
$maxLen = 3
Write-Host "`n===== 颜色代码分布表 (${w}x${h}) ====="
for ($y = 0; $y -lt $h; $y++) {
    $line = ''
    for ($x = 0; $x -lt $w; $x++) {
        $line += ('{0,' + $maxLen + '}' -f $grid[$y][$x]) + ' '
    }
    Write-Host $line
}

# ---- 输出未知色 ----
if ($unknownSet.Count -gt 0) {
    Write-Host "`n===== 未匹配调色板的颜色 (${TOL} 容差内无匹配) ====="
    foreach ($k in $unknownSet.Keys) {
        Write-Host "$k  $($unknownSet[$k])"
    }
} else {
    Write-Host "`n全部像素均匹配已知调色板 ✓"
}

$bmp.Dispose()
