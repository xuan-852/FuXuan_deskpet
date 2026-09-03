# Holiday theme OS-level screenshot capture (review, isolated, zero production pollution).
# Unity ScreenCapture returns black here; use Graphics.CopyFromScreen to grab composited frame.
# Requires full-access sandbox (screen read is blocked by workspace sandbox).
param(
    [string]$Theme = "cn_new_year",
    [string]$Label = "static"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$exe = "D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe"
$data = Join-Path $env:TEMP ("fuxuan_capture_" + [guid]::NewGuid().ToString("N"))
$shotDir = "C:\Users\25295\AppData\Local\Temp\fuxuan_capture_shots"

Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $data | Out-Null
New-Item -ItemType File -Force -Path (Join-Path $data ".test_mode") | Out-Null
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
taskkill /IM DesktopPet.exe /F /T 2>&1 | Out-Null
Start-Sleep 2

$env:FU_XUAN_DATA = $data
$proc = Start-Process $exe -PassThru
Write-Host "launched PID=$($proc.Id)"

Start-Sleep -Seconds 18
Write-Host "wait done"

$inbox = Join-Path $data "inbox.txt"
[System.IO.File]::WriteAllText($inbox, "")
function Send-Inbox([string]$cmd, [int]$ms) { [System.IO.File]::WriteAllText($inbox, $cmd); Start-Sleep -Milliseconds $ms }
function Grab-Screen([string]$name) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
    $out = Join-Path $shotDir ($name + ".png")
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host ("shot: {0} ({1} bytes)" -f $out, (Get-Item $out).Length)
}

Send-Inbox "@@view:open" 2000
Send-Inbox ("@@sim:holiday:" + $Theme) 1800
Grab-Screen ($Theme + "_" + $Label)
Send-Inbox "@@sim:holiday:off" 1600
Grab-Screen ($Theme + "_recovery")
Write-Host "shotDir: $shotDir"
taskkill /IM DesktopPet.exe /F /T 2>&1 | Out-Null
