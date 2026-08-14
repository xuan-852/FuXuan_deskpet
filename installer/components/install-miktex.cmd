@echo off
rem ============================================================
rem  FuXuan installer component - MiKTeX (compile_latex support)
rem  Optional. Silent install of basic MiKTeX + auto-install-on-use.
rem  Usage: install-miktex.cmd [/CHECK]
rem ============================================================
setlocal
set "APP=%~dp0..\..\"
set "CHECK=0"
if /I "%~1"=="/CHECK" set "CHECK=1"

if exist "C:\Program Files\MiKTeX\miktex\bin\x64\miktex.exe" goto :installed
if exist "C:\Program Files (x86)\MiKTeX\miktex\bin\miktex.exe" goto :installed

if %CHECK%==1 (
    echo [CHECK] MiKTeX: MISSING
    exit /b 0
)

echo [INFO] Installing MiKTeX basic (unattended, ~200MB)...
set "DEST=%APP%extras\basic-miktex-x64.exe"
if not exist "%DEST%" (
    echo [INFO] Downloading basic-miktex-x64.exe ...
    powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'https://miktex.org/download/ctan/systems/win32/miktex/setup/basic-miktex-x64.exe' -OutFile '%DEST%' -UseBasicParsing } catch { exit 1 }" >nul 2>&1
)
if not exist "%DEST%" (
    echo [WARN] MiKTeX download failed - skipping
    exit /b 0
)
"%DEST%" --unattended --auto-install=yes --auto-admin=yes
echo [OK] MiKTeX install finished (exit=%errorlevel%)
goto :eof

:installed
if %CHECK%==1 echo [CHECK] MiKTeX: INSTALLED
echo [SKIP] MiKTeX already installed
exit /b 0
