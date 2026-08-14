@echo off
rem ============================================================
rem  FuXuan uninstaller - remove bridge Windows service (NSSM)
rem  Invoked by Inno Setup [UninstallRun] before files are deleted.
rem ============================================================
setlocal
set "APP=%~dp0..\..\"
set "NSSM=%APP%extras\nssm.exe"
if not exist "%NSSM%" (
    echo [WARN] nssm.exe missing - cannot remove service via NSSM
    sc query FuXuanBridge >nul 2>&1 && sc stop FuXuanBridge >nul 2>&1 && sc delete FuXuanBridge >nul 2>&1
    echo [OK] Service removed via sc (fallback)
    exit /b 0
)
"%NSSM%" status FuXuanBridge >nul 2>&1
if %errorlevel%==0 (
    "%NSSM%" stop FuXuanBridge >nul 2>&1
    "%NSSM%" remove FuXuanBridge confirm >nul 2>&1
    echo [OK] Bridge service removed
) else (
    echo [SKIP] Bridge service not present
)
exit /b 0
