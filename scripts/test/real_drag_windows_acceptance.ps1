param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Exe)) { throw "missing Player: $Exe" }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeInput {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
 public const uint LEFTDOWN=0x0002; public const uint LEFTUP=0x0004;
}
'@
$root=Join-Path $env:TEMP ("fuxuan_real_drag_{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $root '.test_mode') -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $root 'inbox.txt') -Force | Out-Null
$env:FU_XUAN_DATA=$root
$process=$null
$passed=$false
function Get-Log { $p=Join-Path $root 'logs/player_log.txt'; if(Test-Path $p){Get-Content $p -Raw}else{''} }
function Wait-Marker([string]$Marker,[int]$Timeout=90000) { $end=[DateTime]::UtcNow.AddMilliseconds($Timeout); do { if((Get-Log).Contains($Marker)){return}; Start-Sleep -Milliseconds 150 } while([DateTime]::UtcNow -lt $end); throw "timeout: $Marker" }
function Send-Command([string]$Command) { $p=Join-Path $root 'inbox.txt'; Set-Content -LiteralPath $p -Value $Command -NoNewline; Start-Sleep -Milliseconds 900; Set-Content -LiteralPath $p -Value '' -NoNewline; Start-Sleep -Milliseconds 100 }
function Get-State { Send-Command '@@sim:desktop-state'; $m=[regex]::Matches((Get-Log),'\[DesktopState\] version=(\d+) mode=([^ ]+) x=(-?\d+) y=(-?\d+).*dragging=(True|False)'); if($m.Count -eq 0){throw 'missing DesktopState'}; $x=$m[$m.Count-1]; [pscustomobject]@{Version=[int]$x.Groups[1].Value;Mode=$x.Groups[2].Value;X=[int]$x.Groups[3].Value;Y=[int]$x.Groups[4].Value;Dragging=$x.Groups[5].Value -eq 'True'} }
try {
 $process=Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
 Wait-Marker 'onGround=True' 30000
 Start-Sleep -Milliseconds 1000
 Send-Command '@@sim:idle-actions:off'
 Send-Command '@@sim:pause:600'
 Start-Sleep -Milliseconds 1200
 $process.Refresh();$handle=$process.MainWindowHandle
 if($handle -eq 0){throw 'window handle unavailable'}
 $rect=New-Object NativeInput+RECT
 if(-not [NativeInput]::GetWindowRect($handle,[ref]$rect)){throw 'GetWindowRect failed'}
 $before=Get-State
 $startX=[int]($rect.Left+$before.X+90)
 $startY=[int]($rect.Top+1600-($before.Y+80))
 [NativeInput]::SetCursorPos($startX,$startY)|Out-Null
 Start-Sleep -Milliseconds 900
 [NativeInput]::mouse_event([NativeInput]::LEFTDOWN,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 300
 $during=$false
 for($i=1;$i -le 12;$i++){[NativeInput]::SetCursorPos($startX+[int](220*$i/12),$startY-[int](120*$i/12))|Out-Null;Start-Sleep -Milliseconds 100;$s=Get-State;if($s.Dragging){$during=$true}}
 if(-not $during){throw 'real mouse drag state was never observed'}
 [NativeInput]::mouse_event([NativeInput]::LEFTUP,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 1200
 $after=Get-State
 if($after.Dragging){throw 'dragging remained true'}
 $log=Get-Log
 if(-not $log.Contains('drag-response')){throw 'drag admission missing'}
 if(-not $log.Contains('Released DragResponse/drag-response')){throw 'drag release missing'}
 if($after.X -eq $before.X -and $after.Y -eq $before.Y){throw 'pet did not move'}
 foreach($bad in @('NullReferenceException','MissingReferenceException','AssertionException','cleanup failure')){if($log.Contains($bad)){throw "unexpected log: $bad"}}
 Send-Command '@@test:quit'; Wait-Marker 'recovered: test-exit' 20000
 $passed=$true
 [pscustomobject]@{Result='passed';Root=$root;Before=$before;After=$after}
} finally {
 if($process -and -not $process.HasExited){try{Set-Content -LiteralPath (Join-Path $root 'inbox.txt') -Value '@@test:quit' -NoNewline}catch{};Start-Sleep -Milliseconds 1000;try{Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue}catch{}}
 if($passed){Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}else{Write-Error "preserved isolated test root: $root"}
}
