@echo off
rem ============================================================
rem FuXuan dependency check. Safe to run repeatedly.
rem Usage: verify-runtime.cmd [/QUIET]
rem ============================================================
setlocal EnableExtensions
set "APP=%~dp0..\.."
set "FAIL=0"
set "QUIET=0"
set "BRIDGE_PORT=19876"
if /I "%~1"=="/QUIET" set "QUIET=1"

if exist "%APP%\bridge\node_modules\openclaw\openclaw.mjs" (
    if "%QUIET%"=="0" echo [PASS] OpenClaw package is installed
) else (
    if "%QUIET%"=="0" echo [FAIL] OpenClaw package is missing
    set "FAIL=1"
)

rem Bridge validation must check the actual HTTP endpoint, not just a listening port.
netstat -ano | findstr /R /C:":%BRIDGE_PORT% .*LISTENING" >nul 2>&1
if errorlevel 1 (
    if "%QUIET%"=="0" echo [FAIL] FuXuan Bridge is not listening on %BRIDGE_PORT%
    set "FAIL=1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $r=Invoke-RestMethod -Uri ('http://127.0.0.1:%BRIDGE_PORT%/health') -TimeoutSec 3; if($r.status -eq 'ok'){exit 0}else{exit 1} } catch { exit 1 }" >nul 2>&1
    if errorlevel 1 (
        if "%QUIET%"=="0" echo [FAIL] FuXuan Bridge health check failed
        set "FAIL=1"
    ) else if "%QUIET%"=="0" echo [PASS] FuXuan Bridge is healthy on %BRIDGE_PORT%
)

sc.exe query FuXuanBridge | findstr /I "RUNNING" >nul 2>&1
if errorlevel 1 (
    if "%QUIET%"=="0" echo [WARN] FuXuanBridge service is not running (portable/manual bridge may still be valid)
) else if "%QUIET%"=="0" echo [PASS] FuXuanBridge service is running

netstat -ano | findstr /R /C:":18789 .*LISTENING" >nul 2>&1
if errorlevel 1 (
    if "%QUIET%"=="0" echo [FAIL] OpenClaw Gateway is not listening on 18789
    set "FAIL=1"
) else if "%QUIET%"=="0" echo [PASS] OpenClaw Gateway is listening

set "OLLAMA="
for /f "delims=" %%O in ('where ollama 2^>nul') do if not defined OLLAMA set "OLLAMA=%%O"
if not defined OLLAMA if exist "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" set "OLLAMA=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
if not defined OLLAMA if exist "%ProgramFiles%\Ollama\ollama.exe" set "OLLAMA=%ProgramFiles%\Ollama\ollama.exe"
if not defined OLLAMA (
    if "%QUIET%"=="0" echo [WARN] Ollama is not installed (local model unavailable)
    set "FAIL=1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 | Out-Null; exit 0 } catch { exit 1 }" >nul 2>&1
    if errorlevel 1 (
        if "%QUIET%"=="0" echo [FAIL] Ollama API is not ready
        set "FAIL=1"
    ) else if "%QUIET%"=="0" echo [PASS] Ollama API is ready
    "%OLLAMA%" list 2>nul | findstr /I /C:"qwen2.5:3b" >nul
    if errorlevel 1 (
        if "%QUIET%"=="0" echo [FAIL] qwen2.5:3b is missing
        set "FAIL=1"
    ) else if "%QUIET%"=="0" echo [PASS] qwen2.5:3b is present
    "%OLLAMA%" list 2>nul | findstr /I /C:"nomic-embed-text" >nul
    if errorlevel 1 (
        if "%QUIET%"=="0" echo [FAIL] nomic-embed-text is missing
        set "FAIL=1"
    ) else if "%QUIET%"=="0" echo [PASS] nomic-embed-text is present
)

if "%QUIET%"=="1" exit /b %FAIL%
echo.
if "%FAIL%"=="0" echo Dependency check completed.
if not "%FAIL%"=="0" echo Dependency check found blocking problems.
exit /b %FAIL%
