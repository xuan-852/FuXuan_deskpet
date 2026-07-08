@echo off
REM ============================================================
REM  符玄桌宠 — CMD 构建脚本
REM  用法: build.cmd
REM  注意: Unity 项目在 code\desktop_unity 子目录
REM  Tuanjie 有路径拼接 Bug，所以必须 CD 到项目目录用 relative path
REM ============================================================

set PROJECT_DIR=D:\Unity\projects\Desktop_per_pro\code\desktop_unity
set LOG_FILE=D:\Unity\projects\Desktop_per_pro\build_log.txt
set UNITY_EXE=D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe

echo [Build] Project: %PROJECT_DIR%
echo [Build] Starting Tuanjie build...

cd /d "%PROJECT_DIR%"
"%UNITY_EXE%" -batchmode -quit -projectPath . -logFile "%LOG_FILE%" -executeMethod BuildScript.BuildDesktopPet

set EXIT_CODE=%ERRORLEVEL%
echo [Build] Exit code: %EXIT_CODE%

if %EXIT_CODE% NEQ 0 (
    echo [Build] FAILED - check %LOG_FILE% for details
    findstr /C:"Error" /C:"error" /C:"Couldn't" /C:"Aborting" "%LOG_FILE%"
) else (
    echo [Build] SUCCESS
)

exit /b %EXIT_CODE%
