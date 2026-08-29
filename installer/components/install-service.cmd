@echo off
rem ============================================================
rem  FuXuan installer component - bridge Windows service (NSSM)
rem  Registers FuXuanBridge: auto-start + crash restart.
rem  Usage: install-service.cmd [/CHECK] [/REMOVE]
rem ============================================================
setlocal EnableExtensions EnableDelayedExpansion
set "APP=%~dp0..\..\"
set "BRIDGE=%APP%bridge"
set "NSSM=%APP%extras\nssm.exe"
set "CHECK=0"
set "REMOVE=0"
if /I "%~1"=="/CHECK" set "CHECK=1"
if /I "%~1"=="/REMOVE" set "REMOVE=1"

rem ---- CHECK must be read-only: never download or change anything ----
if "%CHECK%"=="1" goto :check
if "%REMOVE%"=="1" goto :remove

rem ---- ensure nssm.exe for install/upgrade ----
if not exist "%NSSM%" (
    echo [INFO] Downloading nssm 2.24 ...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $z = Join-Path $env:TEMP 'nssm-2.24.zip'; Invoke-WebRequest -Uri 'https://nssm.cc/release/nssm-2.24.zip' -OutFile $z -UseBasicParsing; Expand-Archive $z (Join-Path $env:TEMP 'nssm224') -Force; New-Item -ItemType Directory -Force -Path '%APP%extras' ^| Out-Null; Copy-Item (Join-Path $env:TEMP 'nssm224\nssm-2.24\win64\nssm.exe') '%NSSM%' -Force } catch { exit 1 }" >nul 2>&1
)
if not exist "%NSSM%" (
    echo [ERROR] nssm.exe unavailable - bridge service was not registered
    exit /b 10
)

rem ---- upgrade: recreate the service so command line and environment are fresh ----
sc.exe query FuXuanBridge >nul 2>&1
if not errorlevel 1 (
    echo [INFO] Existing FuXuanBridge found; refreshing service configuration...
    "%NSSM%" stop FuXuanBridge >nul 2>&1
    sc.exe stop FuXuanBridge >nul 2>&1
    "%NSSM%" remove FuXuanBridge confirm >nul 2>&1
    if errorlevel 1 (
        sc.exe delete FuXuanBridge >nul 2>&1
        if errorlevel 1 goto :nssm_fail
    )
)

rem ---- read user environment written by Inno Setup ----
set "FU_XUAN_DATA="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v FU_XUAN_DATA 2^>nul ^| findstr /I "FU_XUAN_DATA"') do set "FU_XUAN_DATA=%%b"
set "BRIDGE_TOKEN="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v BRIDGE_TOKEN 2^>nul ^| findstr /I "BRIDGE_TOKEN"') do set "BRIDGE_TOKEN=%%b"
set "OFFICE_SCRIPTS_DIR="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v OFFICE_SCRIPTS_DIR 2^>nul ^| findstr /I "OFFICE_SCRIPTS_DIR"') do set "OFFICE_SCRIPTS_DIR=%%b"
set "KNOWLEDGE_SCRIPTS_DIR="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v KNOWLEDGE_SCRIPTS_DIR 2^>nul ^| findstr /I "KNOWLEDGE_SCRIPTS_DIR"') do set "KNOWLEDGE_SCRIPTS_DIR=%%b"
set "OFFICE_PYTHON="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v OFFICE_PYTHON 2^>nul ^| findstr /I "OFFICE_PYTHON"') do set "OFFICE_PYTHON=%%b"
set "BRIDGE_PORT="
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v BRIDGE_PORT 2^>nul ^| findstr /I "BRIDGE_PORT"') do set "BRIDGE_PORT=%%b"
if not defined BRIDGE_PORT set "BRIDGE_PORT=19876"

rem LocalSystem cannot read the installing user's OpenClaw config.
set "GATEWAY_TOKEN="
for /f "usebackq delims=" %%t in (`powershell -NoProfile -Command "$p=Join-Path $env:USERPROFILE '.openclaw\openclaw.json'; if(Test-Path $p){try{$c=Get-Content $p -Raw -Encoding UTF8^|ConvertFrom-Json; $c.gateway.auth.token}catch{}}"`) do set "GATEWAY_TOKEN=%%t"
if not defined GATEWAY_TOKEN for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v OPENCLAW_GATEWAY_TOKEN 2^>nul ^| findstr /I "OPENCLAW_GATEWAY_TOKEN"') do set "GATEWAY_TOKEN=%%b"

if not defined BRIDGE_TOKEN (
    echo [ERROR] BRIDGE_TOKEN is missing from HKCU\Environment; service was not registered
    exit /b 11
)
if not defined GATEWAY_TOKEN (
    echo [ERROR] OpenClaw Gateway token is missing; service was not registered
    exit /b 12
)
if not exist "%BRIDGE%\openclaw_bridge.js" (
    echo [ERROR] Bridge entrypoint is missing: "%BRIDGE%\openclaw_bridge.js"
    exit /b 13
)
if not exist "%BRIDGE%\node_modules\openclaw\openclaw.mjs" (
    echo [ERROR] Bundled OpenClaw package is missing: "%BRIDGE%\node_modules\openclaw"
    exit /b 14
)

rem ---- generate service environment and launcher ----
> "%BRIDGE%\bridge-env.cmd" echo @echo off
>>"%BRIDGE%\bridge-env.cmd" echo set "FU_XUAN_DATA=%FU_XUAN_DATA%"
>>"%BRIDGE%\bridge-env.cmd" echo set "BRIDGE_TOKEN=%BRIDGE_TOKEN%"
>>"%BRIDGE%\bridge-env.cmd" echo set "GATEWAY_TOKEN=%GATEWAY_TOKEN%"
>>"%BRIDGE%\bridge-env.cmd" echo set "OPENCLAW_GATEWAY_TOKEN=%GATEWAY_TOKEN%"
>>"%BRIDGE%\bridge-env.cmd" echo set "OPENCLAW_NODE_MODULES=%BRIDGE%\node_modules\openclaw"
>>"%BRIDGE%\bridge-env.cmd" echo set "BRIDGE_PORT=%BRIDGE_PORT%"
if defined OFFICE_SCRIPTS_DIR >>"%BRIDGE%\bridge-env.cmd" echo set "OFFICE_SCRIPTS_DIR=%OFFICE_SCRIPTS_DIR%"
if defined KNOWLEDGE_SCRIPTS_DIR >>"%BRIDGE%\bridge-env.cmd" echo set "KNOWLEDGE_SCRIPTS_DIR=%KNOWLEDGE_SCRIPTS_DIR%"
if defined OFFICE_PYTHON >>"%BRIDGE%\bridge-env.cmd" echo set "OFFICE_PYTHON=%OFFICE_PYTHON%"

> "%BRIDGE%\run-bridge-service.cmd" echo @echo off
>>"%BRIDGE%\run-bridge-service.cmd" echo setlocal
>>"%BRIDGE%\run-bridge-service.cmd" echo cd /d "%%~dp0"
>>"%BRIDGE%\run-bridge-service.cmd" echo call "%%~dp0bridge-env.cmd"
>>"%BRIDGE%\run-bridge-service.cmd" echo set "NODE=%%~dp0node\node.exe"
>>"%BRIDGE%\run-bridge-service.cmd" echo if not exist "%%NODE%%" for /f "delims=" %%%%N in ^('where node 2^>nul^'^) do if not defined NODE set "NODE=%%%%N"
>>"%BRIDGE%\run-bridge-service.cmd" echo if not exist "%%NODE%%" ^(
>>"%BRIDGE%\run-bridge-service.cmd" echo   echo [ERROR] Node.js runtime not found 1^>^>"%%~dp0service.err.log"
>>"%BRIDGE%\run-bridge-service.cmd" echo   exit /b 20
>>"%BRIDGE%\run-bridge-service.cmd" echo ^)
>>"%BRIDGE%\run-bridge-service.cmd" echo "%%NODE%%" "%%~dp0openclaw_bridge.js"
>>"%BRIDGE%\run-bridge-service.cmd" echo exit /b %%ERRORLEVEL%%

rem LocalSystem must read the wrapper, but ordinary users must not read the tokens.
icacls "%BRIDGE%\bridge-env.cmd" /inheritance:r /grant:r "SYSTEM:F" "Administrators:F" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Could not protect bridge-env.cmd permissions; service was not registered
    del /q "%BRIDGE%\bridge-env.cmd" "%BRIDGE%\run-bridge-service.cmd" >nul 2>&1
    exit /b 15
)

rem ---- register with NSSM and check every operation ----
echo [INFO] Registering bridge service FuXuanBridge...
"%NSSM%" install FuXuanBridge "%COMSPEC%" /c call "%BRIDGE%\run-bridge-service.cmd" >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" set FuXuanBridge AppDirectory "%BRIDGE%" >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" set FuXuanBridge AppExit Default Restart >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" set FuXuanBridge Start SERVICE_AUTO_START >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" set FuXuanBridge AppStdout "%BRIDGE%\service.log" >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" set FuXuanBridge AppStderr "%BRIDGE%\service.err.log" >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" set FuXuanBridge AppRotateFiles 1 >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" set FuXuanBridge AppRotateBytes 10485760 >nul
if errorlevel 1 goto :nssm_fail
"%NSSM%" start FuXuanBridge >nul
if errorlevel 1 goto :nssm_fail

echo [INFO] Waiting for bridge health on 127.0.0.1:%BRIDGE_PORT%...
for /l %%I in (1,1,30) do (
    powershell -NoProfile -Command "$h=@{'x-bridge-token'=$env:BRIDGE_TOKEN}; try{$r=Invoke-RestMethod -Uri ('http://127.0.0.1:'+$env:BRIDGE_PORT+'/health') -Headers $h -TimeoutSec 2; if($r.status -eq 'ok'){exit 0}else{exit 1}}catch{exit 1}" >nul 2>&1
    if not errorlevel 1 goto :ready
    ping -n 2 127.0.0.1 >nul
)
echo [ERROR] Bridge service started but /health did not become ready. See "%BRIDGE%\service.err.log".
exit /b 16

:ready
echo [OK] Bridge service registered, running, and healthy
exit /b 0

:nssm_fail
echo [ERROR] NSSM failed while registering FuXuanBridge (exit=%ERRORLEVEL%)
"%NSSM%" status FuXuanBridge
exit /b 17

:check
sc.exe query FuXuanBridge >nul 2>&1
if errorlevel 1 (
    echo [CHECK] Bridge service: MISSING
    exit /b 1
)
sc.exe query FuXuanBridge | findstr /I "RUNNING" >nul
if errorlevel 1 (
    echo [CHECK] Bridge service: INSTALLED but NOT RUNNING
    exit /b 1
)
echo [CHECK] Bridge service: RUNNING
exit /b 0

:remove
sc.exe query FuXuanBridge >nul 2>&1
if errorlevel 1 (
    echo [SKIP] Bridge service not present
    exit /b 0
)
sc.exe stop FuXuanBridge >nul 2>&1
sc.exe delete FuXuanBridge >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Could not remove FuXuanBridge
    exit /b 18
)
echo [OK] Bridge service FuXuanBridge removed
exit /b 0
