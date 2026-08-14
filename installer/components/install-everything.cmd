@echo off
rem ============================================================
rem  FuXuan installer component - Everything (es.exe) portable
rem  Optional: provides millisecond file search (search_files tool).
rem  If es.exe is bundled under extras, ensure it is reachable.
rem  Usage: install-everything.cmd [/CHECK]
rem ============================================================
setlocal
set "APP=%~dp0..\..\"
set "CHECK=0"
if /I "%~1"=="/CHECK" set "CHECK=1"

set "SRC=%APP%extras\es.exe"
set "DEST=%APP%extras\es.exe"

if not exist "%SRC%" (
    if %CHECK%==1 echo [CHECK] Everything es.exe: NOT BUNDLED (search falls back to slower methods)
    exit /b 0
)
if %CHECK%==1 (
    echo [CHECK] Everything es.exe: BUNDLED at %DEST%
    exit /b 0
)
echo [OK] Everything es.exe present at %DEST% (add %APP%extras to PATH if needed)
exit /b 0
