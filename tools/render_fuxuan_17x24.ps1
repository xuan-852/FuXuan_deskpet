# ============================================================
# 符玄像素图 17x24 渲染工具 — 把颜色代码表格还原成真实 PNG
# 输入: 17x24 网格的颜色代码表（用户手工绘制）
# 输出:
#   PixelFuXuan_17x24.png          (17x24 原尺寸, 透明背景)
#   PixelFuXuan_17x24_preview.png  (8x 放大预览)
#   PixelFuXuan_17x24_grid.png     (8x 放大 + 网格线 + 色号标注, 校对用)
# ============================================================
param(
    [int]$PreviewScale = 8
)

# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──
. "$PSScriptRoot\init-utf8.ps1"

Add-Type -AssemblyName System.Drawing

$resDir = 'D:\Unity\projects\Desktop_per_pro\code\desktop_unity\Assets\Resources'
$outFile      = Join-Path $resDir 'PixelFuXuan_17x24.png'
$previewFile  = Join-Path $resDir 'PixelFuXuan_17x24_preview.png'
$gridFile     = Join-Path $resDir 'PixelFuXuan_17x24_grid.png'

# ---- 颜色映射（用户提供的准确 HEX）----
$pal = @{
    'D6'  = [System.Drawing.ColorTranslator]::FromHtml('#AC7BDE')
    'D7'  = [System.Drawing.ColorTranslator]::FromHtml('#8854B3')
    'D18' = [System.Drawing.ColorTranslator]::FromHtml('#A45EC7')
    'D19' = [System.Drawing.ColorTranslator]::FromHtml('#D8C3D7')
    'E1'  = [System.Drawing.ColorTranslator]::FromHtml('#FDD3CC')
    'E3'  = [System.Drawing.ColorTranslator]::FromHtml('#FFB7E7')
    'E9'  = [System.Drawing.ColorTranslator]::FromHtml('#E970CC')
    'E11' = [System.Drawing.ColorTranslator]::FromHtml('#FCDDD2')
    'E13' = [System.Drawing.ColorTranslator]::FromHtml('#B5006D')
    'E16' = [System.Drawing.ColorTranslator]::FromHtml('#FFF3EB')
    'E17' = [System.Drawing.ColorTranslator]::FromHtml('#FFE2EA')
    'E18' = [System.Drawing.ColorTranslator]::FromHtml('#FFC7DB')
    'E22' = [System.Drawing.ColorTranslator]::FromHtml('#B785A1')
    'H5'  = [System.Drawing.ColorTranslator]::FromHtml('#48464E')
    'H7'  = [System.Drawing.ColorTranslator]::FromHtml('#000000')
    'H8'  = [System.Drawing.ColorTranslator]::FromHtml('#E7D6DB')
    'H10' = [System.Drawing.ColorTranslator]::FromHtml('#EEE9EA')
    'H22' = [System.Drawing.ColorTranslator]::FromHtml('#CACAD4')
    'M7'  = [System.Drawing.ColorTranslator]::FromHtml('#B4A497')
    'M11' = [System.Drawing.ColorTranslator]::FromHtml('#9F7594')
}

# ---- 17x24 网格（| 分隔 17 列，_ 表示空白）----
# 用户表格: 行1=顶部 ... 行24=底部; 列1..17
$rows = @(
    '_|_|_|_|_|_|_|_|_|_|_|_|_|_|_|_|_',
    '_|_|_|M7|E18|_|_|_|_|_|E18|M7|_|_|_|_|_',
    '_|_|_|E18|M7|E18|_|_|_|_|M7|E18|_|_|_|_|_',
    '_|_|_|E18|_|M7|E18|M7|E18|M7|_|E18|E18|_|_|_|_',
    'M7|M7|M7|M7|E18|_|M7|M7|M7|_|E18|M7|M7|M7|M7|M7|_',
    '_|M7|_|_|E13|E13|E13|E13|E13|E13|E13|E13|_|_|M7|_|_',
    '_|D19|_|E13|E18|E3|E3|E18|E3|E3|E18|E13|_|_|D19|_|_',
    '_|D18|E13|E17|E18|E18|E18|E3|E18|E18|E18|E17|E13|D18|_|_|_',
    '_|E13|E18|E18|E17|E17|E17|E17|E17|E17|E17|E17|E18|E18|E13|_|_',
    '_|E13|E18|E18|E18|E18|E18|E22|E18|E18|E18|E18|E18|E13|_|_|_',
    '_|E13|E22|E18|E18|E18|E18|E16|E18|E18|E18|E18|E22|E13|_|_|_',
    'E13|E18|E18|E22|E22|E22|E16|D7|E16|E22|E22|E22|E18|E18|E13|_|_',
    'E18|E13|E18|E18|E16|M11|E16|E16|E16|M11|E16|E18|E18|E13|E18|_|_',
    '_|E18|E22|E18|E16|E11|E16|E16|E16|E16|E11|E16|E18|E22|E18|_|_',
    '_|_|E3|E22|E22|E1|E16|E16|E16|E1|E22|E22|E3|_|_|_|_',
    '_|_|_|E18|H7|H22|H22|M7|H22|H22|H7|M7|_|_|_|_|_',
    '_|_|H10|E18|H10|E16|E16|M7|E16|E16|H10|D18|H10|_|_|_|_',
    '_|_|H7|H22|E22|H10|M7|D18|M7|H10|H10|H22|H7|_|_|_|_',
    '_|_|H7|E16|H5|H5|H5|M7|H5|H5|H5|E16|H7|_|_|_|_',
    '_|_|_|D7|H5|H5|H5|H8|H5|H5|H5|D7|_|_|_|_|_',
    '_|_|_|D7|H22|H22|H5|E9|H5|H22|H22|D7|_|_|_|_|_',
    '_|_|_|_|D18|H10|H10|H7|H10|H10|D18|_|_|_|_|_|_',
    '_|_|_|_|D6|H7|H7|D6|H7|H7|D6|_|_|_|_|_|_',
    '_|_|_|_|_|_|_|_|_|_|_|_|_|_|_|_|_'
)
$cols = 17
$rowsCount = $rows.Count

# 校验每行列数
for ($i = 0; $i -lt $rowsCount; $i++) {
    $t = $rows[$i].Split('|')
    if ($t.Count -ne $cols) {
        Write-Host "警告: 第 $($i+1) 行列数 = $($t.Count) (应为 $cols)"; 
    }
}

# ---- 生成原尺寸 PNG（17x24 透明背景）----
$bmp = New-Object System.Drawing.Bitmap $cols, $rowsCount
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)
$unknown = @{}
for ($y = 0; $y -lt $rowsCount; $y++) {
    $tokens = $rows[$y].Split('|')
    for ($x = 0; $x -lt $cols; $x++) {
        $code = $tokens[$x]
        if ($code -ne '_') {
            if ($pal.ContainsKey($code)) {
                $bmp.SetPixel($x, $y, $pal[$code])
            } else {
                if (-not $unknown.ContainsKey($code)) { $unknown[$code] = 0 }
                $unknown[$code]++
            }
        }
    }
}
$g.Dispose()
$bmp.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "已保存: $outFile (${cols}x$rowsCount)"
if ($unknown.Count -gt 0) {
    Write-Host "!!! 色表中缺失的代码:"
    $unknown.GetEnumerator() | ForEach-Object { Write-Host "    $($_.Key) x$($_.Value)" }
}

# ---- 放大预览（最近邻）----
$pvW = $cols * $PreviewScale
$pvH = $rowsCount * $PreviewScale
$pv = New-Object System.Drawing.Bitmap $pvW, $pvH
$g2 = [System.Drawing.Graphics]::FromImage($pv)
$g2.Clear([System.Drawing.Color]::Transparent)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g2.DrawImage($bmp, 0, 0, $pvW, $pvH)
$g2.Dispose()
$pv.Save($previewFile, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "预览: $previewFile"

# ---- 网格线 + 色号标注版（校对用）----
$gr = New-Object System.Drawing.Bitmap $pvW, $pvH
$g3 = [System.Drawing.Graphics]::FromImage($gr)
$g3.Clear([System.Drawing.Color]::White)
$g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g3.DrawImage($bmp, 0, 0, $pvW, $pvH)
# 网格线
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 0, 0, 0)), 1
for ($i = 0; $i -le $cols; $i++) {
    $g3.DrawLine($pen, $i * $PreviewScale, 0, $i * $PreviewScale, $pvH)
}
for ($j = 0; $j -le $rowsCount; $j++) {
    $g3.DrawLine($pen, 0, $j * $PreviewScale, $pvW, $j * $PreviewScale)
}
# 色号标注（字号 = scale 的 55%，最小 6）
$fontSize = [Math]::Max(6, [int]($PreviewScale * 0.55))
$font = New-Object System.Drawing.Font('Consolas', $fontSize, [System.Drawing.FontStyle]::Bold)
$brushWhite = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$brushBlack = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)
for ($y = 0; $y -lt $rowsCount; $y++) {
    $tokens = $rows[$y].Split('|')
    for ($x = 0; $x -lt $cols; $x++) {
        $code = $tokens[$x]
        if ($code -ne '_') {
            $cx = $x * $PreviewScale
            $cy = $y * $PreviewScale
            $rect = New-Object System.Drawing.RectangleF ($cx + 1), ($cy + 1), ($PreviewScale - 2), ($PreviewScale - 2)
            # 深色格用白字, 浅色格用黑字
            if ($pal.ContainsKey($code)) {
                $c = $pal[$code]
                $lum = 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B
                $useBlack = ($lum -gt 140) -or ($code -in @('H10','H22','H8','E16','E17','E18','E1','E11','D19'))
                if ($useBlack) { $b = $brushBlack } else { $b = $brushWhite }
                $g3.DrawString($code, $font, $b, $rect, [System.Drawing.StringFormat]::GenericDefault)
            }
        }
    }
}
$font.Dispose(); $brushWhite.Dispose(); $brushBlack.Dispose(); $pen.Dispose()
$g3.Dispose()
$gr.Save($gridFile, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "校对图: $gridFile"

$pv.Dispose(); $bmp.Dispose(); $gr.Dispose()
Write-Host "完成!"
