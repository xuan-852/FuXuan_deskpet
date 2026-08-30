@echo off
rem ============================================================
rem FuXuan installer component - Ollama and local models
rem Usage: install-ollama.cmd [/CHECK]
rem ============================================================
setlocal EnableExtensions
set "APP=%~dp0..\.."
set "CHECK=0"
if /I "%~1"=="/CHECK" set "CHECK=1"
set "LOG=%TEMP%\fuxuan-ollama-install.log"
set "OLLAMA="
set "DOWNLOAD_TIMEOUT_SEC=600"
set "PULL_TIMEOUT_SEC=1200"

rem Initialize the log before probing, so installer launch is observable.
>"%LOG%" echo FuXuan Ollama setup started %DATE% %TIME%
>>"%LOG%" echo [INFO] searching for an existing Ollama executable.
call :find_ollama
if defined OLLAMA >>"%LOG%" echo [INFO] existing executable found: %OLLAMA%
if not defined OLLAMA >>"%LOG%" echo [INFO] existing executable not found.

if "%CHECK%"=="1" (
    if defined OLLAMA (echo [CHECK] Ollama: PRESENT) else (echo [CHECK] Ollama: MISSING)
    if defined OLLAMA (
        call :api_ready
        if errorlevel 1 (echo [CHECK] Ollama API: NOT READY) else (echo [CHECK] Ollama API: READY)
        call :has_model "qwen2.5:3b"
        if errorlevel 1 (echo [CHECK] model qwen2.5:3b: ABSENT) else (echo [CHECK] model qwen2.5:3b: PRESENT)
        call :has_model "nomic-embed-text"
        if errorlevel 1 (echo [CHECK] model nomic-embed-text: ABSENT) else (echo [CHECK] model nomic-embed-text: PRESENT)
    )
    exit /b 0
)

if not defined OLLAMA (
    >>"%LOG%" echo [INFO] Ollama executable not found; starting installer download.
    echo [INFO] Downloading Ollama installer (official source, then China fallback)...
    set "SETUP=%TEMP%\FuXuanOllamaSetup.exe"
    set "FU_OLLAMA_SETUP=%SETUP%"
    powershell -NoProfile -ExecutionPolicy Bypass -File "%APP%\extras\components\download-ollama.ps1" -OutputPath "%SETUP%" -LogPath "%LOG%" -TimeoutSec %DOWNLOAD_TIMEOUT_SEC% >>"%LOG%" 2>&1
    if errorlevel 1 (
        >>"%LOG%" echo [ERROR] Ollama installer download failed.
        echo [ERROR] Ollama installer download failed. See "%LOG%".
        exit /b 30
    )
    if not exist "%SETUP%" (
        >>"%LOG%" echo [ERROR] Download command returned without an installer file.
        echo [ERROR] Ollama installer download failed. See "%LOG%".
        exit /b 30
    )
    set "FU_OLLAMA_SETUP=%SETUP%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=Get-AuthenticodeSignature -LiteralPath $env:FU_OLLAMA_SETUP; if($s.Status -ne 'Valid'){ exit 1 }" >>"%LOG%" 2>&1
    if errorlevel 1 (
        >>"%LOG%" echo [ERROR] Ollama installer Authenticode signature is invalid.
        echo [ERROR] Ollama installer signature is invalid; refusing to execute. See "%LOG%".
        del /q "%SETUP%" >nul 2>&1
        exit /b 30
    )
    echo [INFO] Installing Ollama (timeout: %DOWNLOAD_TIMEOUT_SEC%s)...
    >>"%LOG%" echo [INFO] launching Ollama installer with /VERYSILENT.
    set "FU_OLLAMA_SETUP=%SETUP%"
    rem Ollama's Windows installer uses Inno Setup silent-install switches; /S is not reliable.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=Start-Process -FilePath $env:FU_OLLAMA_SETUP -ArgumentList @('/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES') -PassThru; if(-not $p.WaitForExit(600000)){ try { & taskkill /PID $p.Id /T /F | Out-Null } catch {}; exit 124 }; exit $p.ExitCode" >>"%LOG%" 2>&1
    if errorlevel 124 (
        >>"%LOG%" echo [ERROR] Ollama installer timed out.
        echo [ERROR] Ollama installer timed out after %DOWNLOAD_TIMEOUT_SEC% seconds. See "%LOG%".
        exit /b 31
    )
    if errorlevel 1 (
        >>"%LOG%" echo [ERROR] Ollama installer returned a non-zero code.
        echo [ERROR] Ollama installer returned a non-zero code. See "%LOG%".
        exit /b 31
    )
    set "OLLAMA="
    for /l %%I in (1,1,30) do (
        call :find_ollama
        if defined OLLAMA goto :ollama_found
        ping -n 2 127.0.0.1 >nul
    )
    >>"%LOG%" echo [ERROR] Ollama was not found after installation.
    echo [ERROR] Ollama was not found after installation. See "%LOG%".
    exit /b 31
)

:ollama_found
echo [OK] Ollama executable: %OLLAMA%
>>"%LOG%" echo [OK] Ollama executable: %OLLAMA%
call :api_ready
if errorlevel 1 (
    echo [INFO] Starting Ollama local API...
    >>"%LOG%" echo [INFO] API was not ready; starting ollama serve.
    set "FU_OLLAMA_EXE=%OLLAMA%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=Start-Process -FilePath $env:FU_OLLAMA_EXE -ArgumentList 'serve' -PassThru; $p.Id" >>"%LOG%" 2>&1
)

echo [INFO] Waiting for Ollama API on 127.0.0.1:11434...
for /l %%I in (1,1,45) do (
    call :api_ready
    if not errorlevel 1 goto :api_found
    ping -n 2 127.0.0.1 >nul
)
>>"%LOG%" echo [ERROR] Ollama API did not become ready within 90 seconds.
echo [ERROR] Ollama API did not become ready within 90 seconds. See "%LOG%".
exit /b 32

:api_found
echo [INFO] Pulling qwen2.5:3b (resumable, about 2GB; timeout: %PULL_TIMEOUT_SEC%s)...
call :pull_model "qwen2.5:3b" "%TEMP%\fuxuan-ollama-qwen2.5-3b.log"
if errorlevel 1 exit /b 33
echo [INFO] Pulling nomic-embed-text (required by memory search; timeout: %PULL_TIMEOUT_SEC%s)...
call :pull_model "nomic-embed-text" "%TEMP%\fuxuan-ollama-nomic-embed-text.log"
if errorlevel 1 exit /b 34
call :has_model "qwen2.5:3b"
if errorlevel 1 exit /b 35
call :has_model "nomic-embed-text"
if errorlevel 1 exit /b 36
echo [OK] Ollama and both required models are ready.
>>"%LOG%" echo [OK] Ollama and both required models are ready.
exit /b 0

:pull_model
set "FU_OLLAMA_EXE=%OLLAMA%"
set "FU_OLLAMA_MODEL=%~1"
set "FU_OLLAMA_PULL_LOG=%~2"
set "FU_OLLAMA_STDERR=%~2.err"
del /q "%FU_OLLAMA_PULL_LOG%" "%FU_OLLAMA_STDERR%" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=Start-Process -FilePath $env:FU_OLLAMA_EXE -ArgumentList @('pull',$env:FU_OLLAMA_MODEL) -PassThru -NoNewWindow -RedirectStandardOutput $env:FU_OLLAMA_PULL_LOG -RedirectStandardError $env:FU_OLLAMA_STDERR; if(-not $p.WaitForExit(1200000)){ try { & taskkill /PID $p.Id /T /F | Out-Null } catch {}; exit 124 }; exit $p.ExitCode" >>"%LOG%" 2>&1
set "PULL_RC=%ERRORLEVEL%"
if exist "%FU_OLLAMA_PULL_LOG%" type "%FU_OLLAMA_PULL_LOG%" >>"%LOG%"
if exist "%FU_OLLAMA_STDERR%" type "%FU_OLLAMA_STDERR%" >>"%LOG%"
if not "%PULL_RC%"=="0" (
    if "%PULL_RC%"=="124" (
        echo [ERROR] %~1 download timed out after %PULL_TIMEOUT_SEC% seconds.
    ) else (
        echo [ERROR] %~1 download failed with exit code %PULL_RC%.
    )
    echo [ERROR] Rerun this component to resume. Detailed log: "%FU_OLLAMA_PULL_LOG%"
    exit /b 1
)
exit /b 0

:find_ollama
if defined OLLAMA exit /b 0
if exist "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" set "OLLAMA=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
if defined OLLAMA exit /b 0
if exist "%ProgramFiles%\Ollama\ollama.exe" set "OLLAMA=%ProgramFiles%\Ollama\ollama.exe"
if defined OLLAMA exit /b 0
if exist "%ProgramFiles(x86)%\Ollama\ollama.exe" set "OLLAMA=%ProgramFiles(x86)%\Ollama\ollama.exe"
if defined OLLAMA exit /b 0
for /f "delims=" %%O in ('where.exe ollama 2^>nul') do if not defined OLLAMA set "OLLAMA=%%O"
exit /b 0

:api_ready
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 | Out-Null; exit 0 } catch { exit 1 }" >nul 2>&1
exit /b %errorlevel%

:has_model
if not defined OLLAMA exit /b 1
"%OLLAMA%" list 2>nul | findstr /I /C:"%~1" >nul
exit /b %errorlevel%
