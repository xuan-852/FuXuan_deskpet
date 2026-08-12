# ============================================================
# 符玄像素图还原工具 — 把颜色代码表格还原成真实 PNG
# 输入: 10x24 网格的颜色代码表（用户网上找的符玄像素图）
# 输出: PixelFuXuan_pixel.png (8x20 内容区, 透明背景)
#        PixelFuXuan_pixel_preview.png (放大 8x 预览)
# ============================================================
param(
    [int]$PreviewScale = 8
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\init-utf8.ps1"

Add-Type -AssemblyName System.Drawing

$resDir = 'D:\Unity\projects\Desktop_per_pro\code\desktop_unity\Assets\Resources'
$outFile = Join-Path $resDir 'PixelFuXuan_pixel.png'
$previewFile = Join-Path $resDir 'PixelFuXuan_pixel_preview.png'

# ---- 颜色映射（用户提供 RGB 近似值）----
$pal = @{
    'M7'  = [System.Drawing.ColorTranslator]::FromHtml('#C0B0A0')  # 浅棕/米色
    'E18' = [System.Drawing.ColorTranslator]::FromHtml('#FFC0CB')  # 浅粉
    'D19' = [System.Drawing.ColorTranslator]::FromHtml('#FFB6C1')  # 粉红
    'E13' = [System.Drawing.ColorTranslator]::FromHtml('#FF69B4')  # 热粉
    'E3'  = [System.Drawing.ColorTranslator]::FromHtml('#FF1493')  # 深玫红
    'E17' = [System.Drawing.ColorTranslator]::FromHtml('#FF66B2')  # 中粉
    'E22' = [System.Drawing.ColorTranslator]::FromHtml('#FFA07A')  # 浅橙
    'E16' = [System.Drawing.ColorTranslator]::FromHtml('#FF8C00')  # 橙
    'D18' = [System.Drawing.ColorTranslator]::FromHtml('#FF6347')  # 番茄红
    'H22' = [System.Drawing.ColorTranslator]::FromHtml('#8B4513')  # 棕
    'H7'  = [System.Drawing.ColorTranslator]::FromHtml('#000000')  # 黑
    'H10' = [System.Drawing.ColorTranslator]::FromHtml('#808080')  # 灰
    'H5'  = [System.Drawing.ColorTranslator]::FromHtml('#4B0082')  # 靛蓝
    'H8'  = [System.Drawing.ColorTranslator]::FromHtml('#483D8B')  # 深蓝
    'E9'  = [System.Drawing.ColorTranslator]::FromHtml('#9370DB')  # 紫
    'D6'  = [System.Drawing.ColorTranslator]::FromHtml('#8A2BE2')  # 蓝紫
    'E1'  = [System.Drawing.ColorTranslator]::FromHtml('#FFE4E1')  # 浅肉色
    'M11' = [System.Drawing.ColorTranslator]::FromHtml('#DEB887')  # 棕褐
    # ── 表格里出现但颜色表缺失，以下为推测近似色（可改）──
    'E11' = [System.Drawing.ColorTranslator]::FromHtml('#E8A25D')  # 浅橙（推测）
    'D7'  = [System.Drawing.ColorTranslator]::FromHtml('#8B008B')  # 深品红（推测）
}

# ---- 10x24 网格（| 分隔 10 列，_ 表示空白）----
$rows = @(
    '_|_|_|_|_|_|_|_|_|_',
    '_|_|_|_|_|_|_|_|_|_',
    '_|_|M7|E18|_|E18|M7|E18|M7|E18',
    '_|_|E18|M7|E18|_|M7|E18|M7|E18',
    '_|_|E18|_|M7|E18|M7|E18|M7|E18',
    '_|_|M7|_|M7|E13|E13|E13|E13|E13',
    '_|_|_|D19|E13|E13|E3|E3|E18|E3',
    '_|_|D18|E13|E17|E18|E18|E3|E18|E18',
    '_|_|E13|E18|E18|E17|E17|E17|E17|E17',
    '_|_|E13|E18|E18|E18|E18|E22|E18|E18',
    '_|_|E13|E18|E18|E22|E22|E22|E16|E18',
    '_|_|E18|E13|E18|E13|M11|E16|E16|E16',
    '_|_|E18|E22|E18|E16|E11|E16|E16|E11',
    '_|_|E3|E22|E22|E1|E16|E16|E16|E1',
    '_|_|_|E18|H22|H22|M7|H22|H22|H7',
    '_|_|H10|E18|H10|E16|E16|M7|E16|E16',
    '_|_|H7|H22|E22|H10|M7|D18|M7|H10',
    '_|_|H7|E16|H5|H5|H5|M7|H8|H5',
    '_|_|D7|H5|H5|H5|H5|H8|H5|H5',
    '_|_|D7|H22|H22|H5|E9|H5|H22|H22',
    '_|_|D18|H10|H10|H7|H10|H10|H7|H10',
    '_|_|_|D6|H7|H7|D6|H7|H7|H7',
    '_|_|_|_|_|_|_|_|_|_',
    '_|_|_|_|_|_|_|_|_|_'
)

$cols = 10
$rowsCount = $rows.Count

# 计算内容边界（裁掉全空行列）
$minX = $cols; $maxX = -1; $minY = $rowsCount; $maxY = -1
for ($y = 0; $y -lt $rowsCount; $y++) {
    $tokens = $rows[$y].Split('|')
    for ($x = 0; $x -lt $cols; $x++) {
        if ($tokens[$x] -ne '_') {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$cropW = $maxX - $minX + 1
$cropH = $maxY - $minY + 1
Write-Host "内容区: x=$minX..$maxX y=$minY..$maxY => ${cropW}x${cropH}"

# 生成内容区 PNG（透明背景）
$bmp = New-Object System.Drawing.Bitmap $cropW, $cropH
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)
for ($y = 0; $y -lt $cropH; $y++) {
    $tokens = $rows[$minY + $y].Split('|')
    for ($x = 0; $x -lt $cropW; $x++) {
        $code = $tokens[$minX + $x]
        if ($code -ne '_' -and $pal.ContainsKey($code)) {
            $bmp.SetPixel($x, $y, $pal[$code])
        }
    }
}
$g.Dispose()
$bmp.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "已保存: $outFile"

# 放大预览
$pvW = $cropW * $PreviewScale
$pvH = $cropH * $PreviewScale
$pv = New-Object System.Drawing.Bitmap $pvW, $pvH
$g2 = [System.Drawing.Graphics]::FromImage($pv)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($bmp, 0, 0, $pvW, $pvH)
$g2.Dispose()
$pv.Save($previewFile, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "预览: $previewFile"

$pv.Dispose(); $bmp.Dispose()
Write-Host "完成!"
