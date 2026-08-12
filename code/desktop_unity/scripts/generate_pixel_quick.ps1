# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\..\..\..\tools\init-utf8.ps1"

Add-Type -AssemblyName System.Drawing

$src = 'D:\Unity\projects\Desktop_per_pro\code\desktop_unity\Assets\StreamingAssets\Live2D\Fuxuan\符玄.4096\texture_03.png'
if (-not (Test-Path $src)) { Write-Error "Source not found: $src"; exit 1 }
$img = [System.Drawing.Image]::FromFile($src)
$bmp = New-Object System.Drawing.Bitmap $img
$img.Dispose()
# center square crop
$w=$bmp.Width; $h=$bmp.Height; $side=[Math]::Min($w,$h)
$cx=[int](($w-$side)/2); $cy=[int](($h-$side)/2)
$rect = New-Object System.Drawing.Rectangle($cx,$cy,$side,$side)
$crop = New-Object System.Drawing.Bitmap $side, $side
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($bmp, 0, 0, $rect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()

$target=32
$out = New-Object System.Drawing.Bitmap $target, $target
$g2 = [System.Drawing.Graphics]::FromImage($out)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($crop, 0, 0, $target, $target)
$g2.Dispose()

$outDir = 'D:\Unity\projects\Desktop_per_pro\code\desktop_unity\Assets\Resources'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$outPath = Join-Path $outDir 'PixelFuXuan.png'
$out.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Saved: $outPath"
# preview
$preview = New-Object System.Drawing.Bitmap ($target*8), ($target*8)
$g3 = [System.Drawing.Graphics]::FromImage($preview)
$g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g3.DrawImage($out, 0, 0, $target*8, $target*8)
$g3.Dispose()
$previewPath = Join-Path $outDir 'PixelFuXuan_preview.png'
$preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Saved preview: $previewPath"

$bmp.Dispose(); $crop.Dispose(); $out.Dispose(); $preview.Dispose()
