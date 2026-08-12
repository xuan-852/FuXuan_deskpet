# ============================================================
# 符玄像素头像生成器 (PixelFuXuan Generator)
# 用法: powershell -ExecutionPolicy Bypass -File pixelize.ps1 -InputImage "路径\图片.png" [-Size 32] [-CropCenter] [-BgTolerance 24]
# 功能: 把任意图片转为硬边像素风头像，输出到 Assets/Resources/PixelFuXuan.png
# ============================================================
param(
    [Parameter(Mandatory=$true)][string]$InputImage,
    [int]$Size = 32,
    [switch]$CropCenter,      # 居中裁剪(正方形)，默认按最长边
    [int]$BgTolerance = 24,   # 背景透明阈值(0-255)
    [int]$BlurBefore = 2,     # 先模糊降噪再缩小，2=轻微
    [switch]$KeepAlpha        # 保留原图透明度，否则自动抠背景
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\init-utf8.ps1"

Add-Type -AssemblyName System.Drawing

$outDir  = 'D:\Unity\projects\Desktop_per_pro\code\desktop_unity\Assets\Resources'
$outFile = Join-Path $outDir 'PixelFuXuan.png'
$previewFile = Join-Path $outDir 'PixelFuXuan_preview.png'

if (-not (Test-Path $InputImage)) { Write-Error "输入图片不存在: $InputImage"; exit 1 }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$img = [System.Drawing.Image]::FromFile($InputImage)
$src = New-Object System.Drawing.Bitmap $img
$img.Dispose()

$w = $src.Width; $h = $src.Height
Write-Host "输入: $w x $h px, 输出: ${Size}px"

# --- 1. 预处理: 可选模糊降噪 ---
if ($BlurBefore -gt 0) {
    $blurred = New-Object System.Drawing.Bitmap $w, $h
    $gb = [System.Drawing.Graphics]::FromImage($blurred)
    $gb.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
    $gb.DrawImage($src, 0, 0, $w, $h)
    $gb.Dispose()
    $src.Dispose()
    $src = $blurred
}

# --- 2. 确定裁剪区域(正方形) ---
$side = [Math]::Min($w, $h)
if ($CropCenter) {
    $cx = [int](($w - $side) / 2)
    $cy = [int](($h - $side) / 2)
} else {
    # 智能: 从顶部开始裁(头像通常在顶部), 让侧边对齐
    $cx = [int](($w - $side) / 2)
    $cy = 0
    # 若底部含透明/背景多，稍微上移
    for ($y = $h - 1; $y -ge $h - 8; $y--) {
        $hasPixel = $false
        for ($x = 0; $x -lt $w; $x += 8) {
            if ($src.GetPixel($x, $y).A -gt 10) { $hasPixel = $true; break }
        }
        if ($hasPixel) { break }
        $cy = [Math]::Max(0, $cy - 8)
    }
}

$crop = New-Object System.Drawing.Bitmap $side, $side
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($src, 0, 0, (New-Object System.Drawing.Rectangle($cx, $cy, $side, $side)), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()

# --- 3. 缩小到目标尺寸(最近邻=硬像素) ---
$out = New-Object System.Drawing.Bitmap $Size, $Size
$g2 = [System.Drawing.Graphics]::FromImage($out)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($crop, 0, 0, $Size, $Size)
$g2.Dispose()

# --- 4. 背景透明化(自动抠背景) ---
if (-not $KeepAlpha) {
    # 采样四角颜色作为背景色
    $corner = $out.GetPixel(0, 0)
    $bgR = $corner.R; $bgG = $corner.G; $bgB = $corner.B
    for ($y = 0; $y -lt $Size; $y++) {
        for ($x = 0; $x -lt $Size; $x++) {
            $c = $out.GetPixel($x, $y)
            $dr = [Math]::Abs($c.R - $bgR)
            $dg = [Math]::Abs($c.G - $bgG)
            $db = [Math]::Abs($c.B - $bgB)
            if (($dr + $dg + $db) -lt $BgTolerance * 3) {
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, $bgR, $bgG, $bgB))
            }
        }
    }
}

$out.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "已保存: $outFile"

# --- 5. 生成 8x 预览 ---
$pv = New-Object System.Drawing.Bitmap ($Size * 8), ($Size * 8)
$g3 = [System.Drawing.Graphics]::FromImage($pv)
$g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g3.DrawImage($out, 0, 0, $Size * 8, $Size * 8)
$g3.Dispose()
$pv.Save($previewFile, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "预览: $previewFile"

$src.Dispose(); $crop.Dispose(); $out.Dispose(); $pv.Dispose()
Write-Host "完成!"
