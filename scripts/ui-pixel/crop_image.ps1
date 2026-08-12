param(
    [string]$Src,
    [string]$Dst,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 100,
    [int]$H = 100,
    [int]$Scale = 3
)
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($Src)
$bmp = New-Object System.Drawing.Bitmap($img)
$xx = [Math]::Min($X, [Math]::Max(0, $img.Width - $W))
$yy = [Math]::Min($Y, [Math]::Max(0, $img.Height - $H))
$rect = New-Object System.Drawing.Rectangle($xx, $yy, $W, $H)
$out = New-Object System.Drawing.Bitmap(($W * $Scale), ($H * $Scale))
$g = [System.Drawing.Graphics]::FromImage($out)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $out.Width, $out.Height)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$out.Save($Dst)
Write-Host "saved $Dst ($($out.Width)x$($out.Height))"
$g.Dispose(); $out.Dispose(); $bmp.Dispose(); $img.Dispose()
