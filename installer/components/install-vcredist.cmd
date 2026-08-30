@echo off
rem ============================================================
rem  FuXuan installer component - VC++ 2015-2022 x64 runtime
rem  Required by TuanjiePlayer.dll. Silent install. Skips if present.
rem  Usage: install-vcredist.cmd [/CHECK]
rem    /CHECK  -> print state only, never installs
rem ============================================================
setlocal
set "APP=%~dp0..\..\"
set "CHECK=0"
if /I "%~1"=="/CHECK" set "CHECK=1"

reg query "HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" >nul 2>&1
if %errorlevel%==0 (
    if %CHECK%==1 echo [CHECK] VC++ 2015-2022 x64: INSTALLED
    exit /b 0
)
if %CHECK%==1 (
    echo [CHECK] VC++ 2015-2022 x64: MISSING
    exit /b 0
)

echo [INFO] Installing VC++ 2015-2022 x64 runtime (silent)...
set "DEST=%APP%extras\vc_redist.x64.exe"
if not exist "%DEST%" (
    echo [INFO] Downloading vc_redist.x64.exe ...
    powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile '%DEST%' -UseBasicParsing } catch { exit 1 }" >nul 2>&1
)
if not exist "%DEST%" (
    echo [WARN] VC++ download failed - skipping (pet may fail to start)
    exit /b 0
)
set "FU_VCREDIST_SETUP=%DEST%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=Get-AuthenticodeSignature -LiteralPath $env:FU_VCREDIST_SETUP; if($s.Status -ne 'Valid'){ exit 1 }" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] VC++ installer signature is invalid; refusing to execute "%DEST%"
    del /q "%DEST%" >nul 2>&1
    exit /b 40
)
"%DEST%" /install /quiet /norestart
echo [OK] VC++ install finished (exit=%errorlevel%)
exit /b 0
