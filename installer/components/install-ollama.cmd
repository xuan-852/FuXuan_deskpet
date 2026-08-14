@echo off
rem ============================================================
rem  FuXuan installer component - Ollama + local LLM models
rem  Silent install, then pull qwen2.5:3b (~1.9GB) and nomic-embed-text (~274MB).
rem  Skips pull if model already present (resumable). Non-fatal on failure.
rem  Usage: install-ollama.cmd [/CHECK]
rem ============================================================
setlocal
set "APP=%~dp0..\..\"
set "CHECK=0"
if /I "%~1"=="/CHECK" set "CHECK=1"

where ollama >nul 2>&1
set "HASOLL=0"
if %errorlevel%==0 set "HASOLL=1"

if %CHECK%==1 (
    if %HASOLL%==1 ( echo [CHECK] Ollama: INSTALLED ) else ( echo [CHECK] Ollama: MISSING )
    if %HASOLL%==1 ollama list 2>nul | findstr /C:"qwen2.5:3b" >nul && echo [CHECK] model qwen2.5:3b: present || echo [CHECK] model qwen2.5:3b: absent
    exit /b 0
)

if %HASOLL%==0 (
    echo [INFO] Installing Ollama (silent)...
    set "DEST=%APP%extras\ollama-setup.exe"
    if not exist "%DEST%" (
        echo [INFO] Downloading OllamaSetup.exe ...
        powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'https://ollama.com/download/OllamaSetup.exe' -OutFile '%DEST%' -UseBasicParsing } catch { exit 1 }" >nul 2>&1
    )
    if not exist "%DEST%" (
        echo [WARN] Ollama download failed - skipping
        exit /b 0
    )
    "%DEST%" /S
    rem wait for CLI to appear (silent install is async)
    for /l %%i in (1,1,30) do (
        where ollama >nul 2>&1 && goto :oll_ok
        ping -n 2 127.0.0.1 >nul
    )
    echo [WARN] Ollama CLI not detected after install - skipping models
    exit /b 0
)
:oll_ok
echo [INFO] Ensuring models are present (may download several GB)...
ollama list 2>nul | findstr /C:"qwen2.5:3b" >nul || ollama pull qwen2.5:3b
ollama list 2>nul | findstr /C:"nomic-embed-text" >nul || ollama pull nomic-embed-text
echo [OK] Ollama models ready.
exit /b 0
