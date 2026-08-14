@echo off
rem ============================================================
rem  FuXuan installer component - bridge Windows service (NSSM)
rem  Registers FuXuanBridge: auto-start + crash restart.
rem  A wrapper (run-bridge-service.cmd + bridge-env.cmd) is generated
rem  into {app}\bridge so the service (running as LocalSystem) gets the
rem  correct env vars even though it cannot read HKCU\Environment.
rem  Usage: install-service.cmd [/CHECK] [/REMOVE]
rem ============================================================
setlocal
set "APP=%~dp0..\..\"
set "NSSM=%APP%extras\nssm.exe"
set "CHECK=0"
set "REMOVE=0"
if /I "%~1"=="/CHECK" set "CHECK=1"
if /I "%~1"=="/REMOVE" set "REMOVE=1"

rem ---- ensure nssm.exe ----
if not exist "%NSSM%" (
    echo [INFO] Downloading nssm 2.24 ...
    powershell -NoProfile -Command "try { $z = Join-Path $env:TEMP 'nssm-2.24.zip'; Invoke-WebRequest -Uri 'https://nssm.cc/release/nssm-2.24.zip' -OutFile $z -UseBasicParsing; Expand-Archive $z (Join-Path $env:TEMP 'nssm224') -Force; Copy-Item (Join-Path $env:TEMP 'nssm224\nssm-2.24\win64\nssm.exe') '%NSSM%' -Force } catch { exit 1 }" >nul 2>&1
)
if not exist "%NSSM%" (
    echo [WARN] nssm.exe unavailable - bridge service NOT registered
    exit /b 0
)

"%NSSM%" status FuXuanBridge >nul 2>&1
set "HASSVC=%errorlevel%"

if %REMOVE%==1 (
    if %HASSVC%==0 (
        "%NSSM%" stop FuXuanBridge >nul 2>&1
        "%NSSM%" remove FuXuanBridge confirm >nul 2>&1
        echo [OK] Bridge service FuXuanBridge removed
    ) else (
        echo [SKIP] Bridge service not present
    )
    exit /b 0
)

if %CHECK%==1 (
    if %HASSVC%==0 ( echo [CHECK] Bridge service: INSTALLED ) else ( echo [CHECK] Bridge service: MISSING )
    exit /b 0
)
if %HASSVC%==0 ( echo [SKIP] Bridge service already registered & exit /b 0 )

rem ---- read env from HKCU\Environment (installed by the installer) ----
set "FU_XUAN_DATA="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v FU_XUAN_DATA 2^>nul ^| findstr /I "FU_XUAN_DATA"') do set "FU_XUAN_DATA=%%b"
set "BRIDGE_TOKEN="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v BRIDGE_TOKEN 2^>nul ^| findstr /I "BRIDGE_TOKEN"') do set "BRIDGE_TOKEN=%%b"
set "OPENCLAW_NODE_MODULES="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v OPENCLAW_NODE_MODULES 2^>nul ^| findstr /I "OPENCLAW_NODE_MODULES"') do set "OPENCLAW_NODE_MODULES=%%b"
set "OFFICE_SCRIPTS_DIR="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v OFFICE_SCRIPTS_DIR 2^>nul ^| findstr /I "OFFICE_SCRIPTS_DIR"') do set "OFFICE_SCRIPTS_DIR=%%b"
set "KNOWLEDGE_SCRIPTS_DIR="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v KNOWLEDGE_SCRIPTS_DIR 2^>nul ^| findstr /I "KNOWLEDGE_SCRIPTS_DIR"') do set "KNOWLEDGE_SCRIPTS_DIR=%%b"
set "OFFICE_PYTHON="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v OFFICE_PYTHON 2^>nul ^| findstr /I "OFFICE_PYTHON"') do set "OFFICE_PYTHON=%%b"
rem GATEWAY_TOKEN from openclaw config (bridge normally auto-reads from ~/.openclaw)
set "GATEWAY_TOKEN="
for /f "usebackq delims=" %%t in (`powershell -NoProfile -Command "$p=Join-Path $env:USERPROFILE '.openclaw\openclaw.json'; if(Test-Path $p){try{$c=Get-Content $p -Raw -Encoding UTF8|ConvertFrom-Json; $c.gateway.auth.token}catch{}}"`) do set "GATEWAY_TOKEN=%%t"

rem ---- generate bridge-env.cmd (env for the service process) ----
> "%APP%bridge\bridge-env.cmd" echo @echo off
if defined FU_XUAN_DATA >>"%APP%bridge\bridge-env.cmd" echo set "FU_XUAN_DATA=%FU_XUAN_DATA%"
if defined BRIDGE_TOKEN >>"%APP%bridge\bridge-env.cmd" echo set "BRIDGE_TOKEN=%BRIDGE_TOKEN%"
if defined OPENCLAW_NODE_MODULES >>"%APP%bridge\bridge-env.cmd" echo set "OPENCLAW_NODE_MODULES=%OPENCLAW_NODE_MODULES%"
if defined OFFICE_SCRIPTS_DIR >>"%APP%bridge\bridge-env.cmd" echo set "OFFICE_SCRIPTS_DIR=%OFFICE_SCRIPTS_DIR%"
if defined KNOWLEDGE_SCRIPTS_DIR >>"%APP%bridge\bridge-env.cmd" echo set "KNOWLEDGE_SCRIPTS_DIR=%KNOWLEDGE_SCRIPTS_DIR%"
if defined OFFICE_PYTHON >>"%APP%bridge\bridge-env.cmd" echo set "OFFICE_PYTHON=%OFFICE_PYTHON%"
if defined GATEWAY_TOKEN >>"%APP%bridge\bridge-env.cmd" echo set "GATEWAY_TOKEN=%GATEWAY_TOKEN%"
if not defined BRIDGE_TOKEN echo [WARN] BRIDGE_TOKEN missing - bridge auth may fail

rem ---- generate run-bridge-service.cmd (service entry) ----
> "%APP%bridge\run-bridge-service.cmd" echo @echo off
>>"%APP%bridge\run-bridge-service.cmd" echo cd /d "%%~dp0"
>>"%APP%bridge\run-bridge-service.cmd" echo call "%%~dp0bridge-env.cmd"
>>"%APP%bridge\run-bridge-service.cmd" echo "%%~dp0node\node.exe" "%%~dp0openclaw_bridge.js"

rem ---- register with NSSM ----
echo [INFO] Registering bridge service FuXuanBridge...
"%NSSM%" install FuXuanBridge "%COMSPEC%" /c call "%APP%bridge\run-bridge-service.cmd" >nul
"%NSSM%" set FuXuanBridge AppDirectory "%APP%bridge" >nul
"%NSSM%" set FuXuanBridge AppExit Default Restart >nul
"%NSSM%" set FuXuanBridge Start SERVICE_AUTO_START >nul
"%NSSM%" set FuXuanBridge AppStdout "%APP%bridge\service.log" >nul
"%NSSM%" set FuXuanBridge AppStderr "%APP%bridge\service.err.log" >nul
"%NSSM%" set FuXuanBridge AppRotateFiles 1 >nul
"%NSSM%" set FuXuanBridge AppRotateBytes 10485760 >nul
"%NSSM%" start FuXuanBridge >nul
echo [OK] Bridge service registered and started
exit /b 0
