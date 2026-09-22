param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeDrag {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public const uint LEFTDOWN=0x0002; public const uint LEFTUP=0x0004;
}
'@
$root=Join-Path $env:TEMP ("fuxuan_real_walking_drag_{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $root '.test_mode') -Force | Out-Null
$inbox=Join-Path $root 'inbox.txt'; New-Item -ItemType File -Path $inbox -Force | Out-Null
$env:FU_XUAN_DATA=$root
$logPath=Join-Path $root 'logs\player_log.txt'
$p=Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
function LogText { if(Test-Path -LiteralPath $logPath){Get-Content -LiteralPath $logPath -Raw}else{''} }
function WaitAscii([string]$marker,[int]$timeout=90000) { $end=[DateTime]::UtcNow.AddMilliseconds($timeout); while([DateTime]::UtcNow -lt $end){ if((LogText).Contains($marker)){return}; Start-Sleep -Milliseconds 150 }; throw "timeout: $marker" }
function Send([string]$command,[int]$wait=900) { Set-Content -LiteralPath $inbox -Value $command -NoNewline; Start-Sleep -Milliseconds $wait; Set-Content -LiteralPath $inbox -Value '' -NoNewline; Start-Sleep -Milliseconds 120 }
try {
  [NativeDrag]::SetProcessDPIAware() | Out-Null
  WaitAscii 'Accepted Walking/walk-pose/walking-state' 90000
  Send '@@sim:idle-actions:off'
  Send '@@sim:walk:right'
  Start-Sleep -Milliseconds 700
  Send '@@sim:desktop-state'
  Start-Sleep -Milliseconds 300
  $text=LogText
  $matches=[regex]::Matches($text,'\[DesktopState\] version=(\d+) mode=([^ ]+) x=(-?\d+) y=(-?\d+) velocity=\((-?\d+),(-?\d+)\) onGround=(True|False) dragging=(True|False)')
  if($matches.Count -eq 0){throw 'missing desktop state'}
  $m=$matches[$matches.Count-1]
  $petX=[int]$m.Groups[3].Value; $petY=[int]$m.Groups[4].Value
  if($m.Groups[2].Value -ne 'Walking' -or $m.Groups[7].Value -ne 'True'){throw ('not walking/grounded: ' + $m.Value)}
  $p.Refresh(); $rect=New-Object NativeDrag+RECT
  [NativeDrag]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
  Start-Sleep -Milliseconds 500
  if(-not [NativeDrag]::GetWindowRect($p.MainWindowHandle,[ref]$rect)){throw 'GetWindowRect failed'}
  $windowW=$rect.Right-$rect.Left
  $windowH=$rect.Bottom-$rect.Top
  $startX=$rect.Left+[int](($petX+50)*$windowW/1920.0)
  $startY=$rect.Top+[int](($petY+85)*$windowH/1600.0)
  Write-Output ("ROOT=$root`nPID=$($p.Id)`nPET=$petX,$petY`nWINDOW=$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)`nCLICK=$startX,$startY SCALE=$windowW/$windowH")
  [NativeDrag]::SetCursorPos($startX,$startY)|Out-Null
  Start-Sleep -Milliseconds 700
  [NativeDrag]::mouse_event([NativeDrag]::LEFTDOWN,0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 250
  for($i=1;$i -le 15;$i++){ [NativeDrag]::SetCursorPos($startX+[int](260*$i/15),$startY-[int](100*$i/15))|Out-Null; Start-Sleep -Milliseconds 120 }
  Start-Sleep -Milliseconds 400
  [NativeDrag]::mouse_event([NativeDrag]::LEFTUP,0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 1200
  $after=LogText
  $evidence=$after | Select-String -Pattern 'Handoff Walking|DragResponse|drag-release|拖动已启动|拖动已拒绝|抛掷:' | Select-Object -Last 30
  $evidence
  if(-not ($after.Contains('[DragHandoff] walking-to-drag accepted') -and $after.Contains('DragResponse/drag-response'))){ throw 'real mouse DragHandler evidence missing' }
  Send '@@test:quit' 700
  WaitAscii 'recovered: test-exit' 20000
  Write-Output 'RESULT=completed'
} finally {
  if($p -and -not $p.HasExited){ try { Set-Content -LiteralPath $inbox -Value '@@test:quit' -NoNewline } catch {}; Start-Sleep -Milliseconds 1200; try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch {} }
  Write-Output "LOG=$logPath"
}
