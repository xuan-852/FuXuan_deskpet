# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\..\..\..\scripts\encoding\init-utf8.ps1"

Add-Type -AssemblyName System.Drawing

$root = 'D:\Unity\projects\Desktop_per_pro\code\desktop_unity'
$srcDirs = @(
    [System.IO.Path]::Combine($root, 'Assets', 'StreamingAssets', 'Live2D', 'Fuxuan', '符玄.4096'),
    [System.IO.Path]::Combine($root, 'Assets', 'Live2D', 'Models', 'Fuxuan', '符玄.4096')
)
$outDir = [System.IO.Path]::Combine($root, 'Assets', 'Resources')
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$targets = @()
foreach ($d in $srcDirs) {
    for ($i=0;$i -le 7;$i++) {
        $fn = 'texture_{0:d2}.png' -f $i
        $fp = [System.IO.Path]::Combine($d, $fn)
        if (Test-Path $fp) { $targets += $fp }
    }
}

if ($targets.Count -eq 0) { Write-Error "NO_TEXTURES_FOUND (checked: $($srcDirs -join ', '))"; exit 1 }

foreach ($t in $targets) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($t)
    $img = [System.Drawing.Image]::FromFile($t)
    $bmp = New-Object System.Drawing.Bitmap $img
    $img.Dispose()
    $w=$bmp.Width; $h=$bmp.Height
    # find alpha / dark bbox (coarse scan)
    $minx=$w; $miny=$h; $maxx=0; $maxy=0
    for ($y=0;$y -lt $h;$y+=6) {
        for ($x=0;$x -lt $w;$x+=6) {
            $c=$bmp.GetPixel($x,$y)
            if ($c.A -gt 16 -or ($c.R + $c.G + $c.B -lt 600)) {
                if ($x -lt $minx) { $minx=$x }
                if ($y -lt $miny) { $miny=$y }
                if ($x -gt $maxx) { $maxx=$x }
                if ($y -gt $maxy) { $maxy=$y }
            }
        }
    }
    if ($maxx -eq 0 -or $maxy -eq 0) {
        $side=[Math]::Min($w,$h)
        $minx=[int](($w-$side)/2); $miny=[int](($h-$side)/2)
        $maxx=$minx+$side-1; $maxy=$miny+$side-1
    }
    $cropW=$maxx-$minx+1; $cropH=$maxy-$miny+1
    $pad=[int]([Math]::Max((($cropW+$cropH)/18),20))
    $minx=[Math]::Max(0,$minx-$pad); $miny=[Math]::Max(0,$miny-$pad)
    $maxx=[Math]::Min($w-1,$maxx+$pad); $maxy=[Math]::Min($h-1,$maxy+$pad)
    $cropW=$maxx-$minx+1; $cropH=$maxy-$miny+1
    $side=[Math]::Max($cropW,$cropH)
    $cx=[Math]::Max(0,$minx - [int](($side-$cropW)/2))
    $cy=[Math]::Max(0,$miny - [int](($side-$cropH)/2))
    if ($cx + $side -gt $w) { $cx = $w - $side }
    if ($cy + $side -gt $h) { $cy = $h - $side }

    $rect = New-Object System.Drawing.Rectangle($cx,$cy,$side,$side)
    $cropBmp = New-Object System.Drawing.Bitmap $side, $side
    $g = [System.Drawing.Graphics]::FromImage($cropBmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.DrawImage($bmp, 0, 0, $rect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()

    $target=32
    $outBmp = New-Object System.Drawing.Bitmap $target, $target
    $g2 = [System.Drawing.Graphics]::FromImage($outBmp)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g2.DrawImage($cropBmp, 0, 0, $target, $target)
    $g2.Dispose()

    $base = "PixelFuXuan_candidate_" + $name + ".png"
    $outPath = [System.IO.Path]::Combine($outDir, $base)
    $outBmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $preview = New-Object System.Drawing.Bitmap ($target*8),($target*8)
    $g3 = [System.Drawing.Graphics]::FromImage($preview)
    $g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g3.DrawImage($outBmp,0,0,$target*8,$target*8)
    $g3.Dispose()
    $previewPath = [System.IO.Path]::Combine($outDir, "PixelFuXuan_candidate_" + $name + "_preview.png")
    $preview.Save($previewPath,[System.Drawing.Imaging.ImageFormat]::Png)

    $bmp.Dispose(); $cropBmp.Dispose(); $outBmp.Dispose(); $preview.Dispose()
    Write-Output "Generated: $outPath"
}

Get-ChildItem -Path $outDir -Filter 'PixelFuXuan_candidate_*_preview.png' | Select-Object Name, Length | Format-Table -AutoSize
Add-Type -AssemblyName System.Drawing

$root = 'D:\Unity\projects\Desktop_per_pro\code\desktop_unity'
$dirs = @(
    "$root\Assets\StreamingAssets\Live2D\Fuxuan\符玄.4096",
    "$root\Assets\Live2D\Models\Fuxuan\符玄.4096"
)
$outDir = "$root\Assets\Resources"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$generated = @()

foreach ($dir in $dirs) {
    if (-not (Test-Path $dir)) { continue }
    for ($i=0; $i -le 7; $i++) {
        $fn = ("texture_{0:d2}.png" -f $i)
        $fp = $dir + '\\' + $fn
        if (-not (Test-Path $fp)) { continue }
        try {
            $name = [System.IO.Path]::GetFileNameWithoutExtension($fp)
            $img = [System.Drawing.Image]::FromFile($fp)
            $bmp = New-Object System.Drawing.Bitmap $img
            $img.Dispose()
            $w=$bmp.Width; $h=$bmp.Height
            $minx=$w; $miny=$h; $maxx=0; $maxy=0
            for ($y=0;$y -lt $h;$y+=6) {
                for ($x=0;$x -lt $w;$x+=6) {
                    $c=$bmp.GetPixel($x,$y)
                    if ($c.A -gt 16 -or ($c.R + $c.G + $c.B -lt 600)) {
                        if ($x -lt $minx) { $minx=$x }
                        if ($y -lt $miny) { $miny=$y }
                        if ($x -gt $maxx) { $maxx=$x }
                        if ($y -gt $maxy) { $maxy=$y }
                    }
                }
            }
            if ($maxx -eq 0 -or $maxy -eq 0) {
                $side=[Math]::Min($w,$h)
                $minx=[int](($w-$side)/2); $miny=[int](($h-$side)/2)
                $maxx=$minx+$side-1; $maxy=$miny+$side-1
            }
            $cropW=$maxx-$minx+1; $cropH=$maxy-$miny+1
            $pad=[int]([Math]::Max((($cropW+$cropH)/18),20))
            $minx=[Math]::Max(0,$minx-$pad); $miny=[Math]::Max(0,$miny-$pad)
            $maxx=[Math]::Min($w-1,$maxx+$pad); $maxy=[Math]::Min($h-1,$maxy+$pad)
            $cropW=$maxx-$minx+1; $cropH=$maxy-$miny+1
            $side=[Math]::Max($cropW,$cropH)
            $cx=[Math]::Max(0,$minx - [int](($side-$cropW)/2))
            $cy=[Math]::Max(0,$miny - [int](($side-$cropH)/2))
            if ($cx + $side -gt $w) { $cx = $w - $side }
            if ($cy + $side -gt $h) { $cy = $h - $side }
            $rect = New-Object System.Drawing.Rectangle($cx,$cy,$side,$side)
            $cropBmp = New-Object System.Drawing.Bitmap $side, $side
            $g = [System.Drawing.Graphics]::FromImage($cropBmp)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $g.DrawImage($bmp, 0, 0, $rect, [System.Drawing.GraphicsUnit]::Pixel)
            $g.Dispose()

            $target=32
            $outBmp = New-Object System.Drawing.Bitmap $target, $target
            $g2 = [System.Drawing.Graphics]::FromImage($outBmp)
            $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $g2.DrawImage($cropBmp, 0, 0, $target, $target)
            $g2.Dispose()

            $base = "PixelFuXuan_candidate_" + $name + ".png"
            $outPath = Join-Path $outDir $base
            $outBmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
            $preview = New-Object System.Drawing.Bitmap ($target*8),($target*8)
            $g3 = [System.Drawing.Graphics]::FromImage($preview)
            $g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $g3.DrawImage($outBmp,0,0,$target*8,$target*8)
            $g3.Dispose()
            $previewPath = Join-Path $outDir ("PixelFuXuan_candidate_" + $name + "_preview.png")
            $preview.Save($previewPath,[System.Drawing.Imaging.ImageFormat]::Png)

            $bmp.Dispose(); $cropBmp.Dispose(); $outBmp.Dispose(); $preview.Dispose()
            $generated += $previewPath
            Write-Output "Generated: $outPath"
        } catch {
            Write-Output "Failed: $fp -> $_"
        }
    }
}
Write-Output "--- Generated previews ---"
Get-ChildItem -Path $outDir -Filter 'PixelFuXuan_candidate_*_preview.png' | Select-Object Name, Length | Format-Table -AutoSize
