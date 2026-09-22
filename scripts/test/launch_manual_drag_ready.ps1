$root = Join-Path $env:TEMP ("fuxuan_manual_drag_ready_{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $root '.test_mode') -Force | Out-Null
$inbox = Join-Path $root 'inbox.txt'
New-Item -ItemType File -Path $inbox -Force | Out-Null
$env:FU_XUAN_DATA = $root
$exe = 'D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe'
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru
$logPath = Join-Path $root 'logs\player_log.txt'
$deadline = [DateTime]::UtcNow.AddSeconds(60)
while ([DateTime]::UtcNow -lt $deadline) {
  if (Test-Path $logPath) {
    $log = Get-Content $logPath -Raw
    if ($log.Contains('[DesktopPet] 落地')) { break }
  }
  Start-Sleep -Milliseconds 250
}
Start-Sleep -Milliseconds 1200
Set-Content -LiteralPath $inbox -Value '@@sim:idle-actions:off' -NoNewline
Start-Sleep -Milliseconds 900
Set-Content -LiteralPath $inbox -Value '' -NoNewline
Start-Sleep -Milliseconds 500
Set-Content -LiteralPath $inbox -Value '@@sim:pause:600' -NoNewline
Start-Sleep -Milliseconds 900
Set-Content -LiteralPath $inbox -Value '' -NoNewline
Start-Sleep -Milliseconds 1000
Write-Output ("ROOT=" + $root + "`nPID=" + $p.Id)
