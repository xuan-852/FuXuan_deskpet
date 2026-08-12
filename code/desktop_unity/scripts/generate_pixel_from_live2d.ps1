# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\..\..\..\scripts\encoding\init-utf8.ps1"

Add-Type -AssemblyName System.Drawing

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# 构建候选路径（使用 Path.Combine 避免 Join-Path 数组/编码问题）
$candidates = @()
$candidates += [System.IO.Path]::Combine($root, 'Assets', 'Live2D', 'Models', 'Fuxuan', '符玄.4096', 'texture_00.png')
$candidates += [System.IO.Path]::Combine($root, 'Assets', 'StreamingAssets', 'Live2D', 'Fuxuan', '符玄.4096', 'texture_00.png')
$candidates += [System.IO.Path]::Combine($root, 'Assets', 'Live2D', 'Models', 'Fuxuan', '符玄.4096', 'texture_01.png')
$candidates += [System.IO.Path]::Combine($root, 'Assets', 'StreamingAssets', 'Live2D', 'Fuxuan', '符玄.4096', 'texture_01.png')

$src = $null
foreach ($p in $candidates) { if (Test-Path $p) { $src = $p; break } }
if ($null -eq $src) { Write-Error "No Live2D texture found. Checked: $($candidates -join ', ')"; exit 1 }
Write-Output "Using source: $src"

$img = [System.Drawing.Image]::FromFile($src)
$bmp = New-Object System.Drawing.Bitmap $img
$img.Dispose()

# find alpha bbox if exists
$w = $bmp.Width; $h = $bmp.Height
$minx = $w; $miny = $h; $maxx = 0; $maxy = 0
for ($y=0;$y -lt $h; $y+=4) {
    for ($x=0;$x -lt $w; $x+=4) {
        $c = $bmp.GetPixel($x,$y)
        if ($c.A -gt 16 -or ($c.R + $c.G + $c.B -lt 700)) {
            if ($x -lt $minx) { $minx = $x }
            if ($y -lt $miny) { $miny = $y }
            if ($x -gt $maxx) { $maxx = $x }
            if ($y -gt $maxy) { $maxy = $y }
        }
    }
}
if ($maxx -eq 0 -or $maxy -eq 0) {
    # fallback center square
    $side = [Math]::Min($w,$h)
    $minx = [int](($w-$side)/2)
    $miny = [int](($h-$side)/2)
    $maxx = $minx + $side - 1
    $maxy = $miny + $side - 1
}

$cropW = $maxx - $minx + 1
$cropH = $maxy - $miny + 1
# expand bbox a bit
$pad = [int]([Math]::Max( ( ($cropW + $cropH)/20 ), 20 ))
$minx = [Math]::Max(0, $minx - $pad)
$miny = [Math]::Max(0, $miny - $pad)
$maxx = [Math]::Min($w-1, $maxx + $pad)
$maxy = [Math]::Min($h-1, $maxy + $pad)
$cropW = $maxx - $minx + 1
$cropH = $maxy - $miny + 1

# make square
$side = [Math]::Max($cropW, $cropH)
$cx = [Math]::Max(0, $minx - [int](($side - $cropW)/2))
$cy = [Math]::Max(0, $miny - [int](($side - $cropH)/2))
if ($cx + $side -gt $w) { $cx = $w - $side }
if ($cy + $side -gt $h) { $cy = $h - $side }

$rect = New-Object System.Drawing.Rectangle($cx,$cy,$side,$side)
$cropBmp = New-Object System.Drawing.Bitmap $side, $side
$g = [System.Drawing.Graphics]::FromImage($cropBmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighSpeed
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($bmp, 0, 0, $rect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()

# resize to 32x32
$target = 32
$outBmp = New-Object System.Drawing.Bitmap $target, $target
$g2 = [System.Drawing.Graphics]::FromImage($outBmp)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighSpeed
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g2.DrawImage($cropBmp, 0, 0, $target, $target)
$g2.Dispose()

# save to Resources
$outDir = Join-Path $root "Assets\Resources"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$outPath = Join-Path $outDir "PixelFuXuan.png"
$outBmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Saved pixel avatar to $outPath"

# save preview 8x
$preview = New-Object System.Drawing.Bitmap ($target*8), ($target*8)
$g3 = [System.Drawing.Graphics]::FromImage($preview)
$g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g3.DrawImage($outBmp, 0, 0, $target*8, $target*8)
$g3.Dispose()
$previewPath = Join-Path $outDir "PixelFuXuan_preview.png"
$preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Saved preview to $previewPath"

$bmp.Dispose()
$cropBmp.Dispose()
$outBmp.Dispose()
$preview.Dispose()
