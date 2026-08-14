@echo off
rem ============================================================
rem  FuXuan installer component - OpenClaw Gateway
rem  The openclaw npm package is BUNDLED under bridge\node_modules,
rem  so no global npm install is needed. This script only ensures
rem  the Gateway is running on ws://127.0.0.1:18789 (bridge depends on it).
rem  Usage: install-openclaw.cmd [/CHECK]
rem ============================================================
setlocal
set "APP=%~dp0..\..\"
set "CHECK=0"
if /I "%~1"=="/CHECK" set "CHECK=1"

netstat -ano | findstr /R /C:"18789.*LISTENING" >nul 2>&1
set "GW=0"
if %errorlevel%==0 set "GW=1"

if %CHECK%==1 (
    if %GW%==1 ( echo [CHECK] OpenClaw Gateway: RUNNING (18789) ) else ( echo [CHECK] OpenClaw Gateway: NOT RUNNING )
    exit /b 0
)
if %GW%==1 ( echo [SKIP] Gateway already running & exit /b 0 )

set "NODE=%APP%bridge\node\node.exe"
if not exist "%NODE%" set "NODE=node"
set "CLI=%APP%bridge\node_modules\openclaw\openclaw.mjs"
if not exist "%CLI%" (
    echo [WARN] bundled openclaw CLI not found - skip
    exit /b 0
)
echo [INFO] Starting OpenClaw Gateway via bundled CLI...
start "FuXuanGateway" /D "%APP%bridge" "%NODE%" "%CLI%" gateway start
echo [OK] Gateway start requested - verify ws://127.0.0.1:18789 shortly
exit /b 0
