@echo off
rem ============================================================
rem  FuXuan uninstaller - remove bridge Windows service (NSSM)
rem  Invoked by Inno Setup [UninstallRun] before files are deleted.
rem ============================================================
setlocal EnableExtensions
set "APP=%~dp0..\..\"
sc.exe query FuXuanBridge >nul 2>&1
if errorlevel 1 (
    echo [SKIP] Bridge service not present
    goto :cleanup
)
sc.exe stop FuXuanBridge >nul 2>&1
sc.exe delete FuXuanBridge >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Bridge service could not be removed
    exit /b 1
)
echo [OK] Bridge service removed

:cleanup
del /q "%APP%bridge\bridge-env.cmd" "%APP%bridge\run-bridge-service.cmd" >nul 2>&1
exit /b 0
