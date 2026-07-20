@echo off
subst Z: /d >nul 2>&1
subst Z: D:\Unity\projects\Desktop_per_pro
Z:
cd Z:\
"D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe" -batchmode -quit -projectPath Z:\ -logFile build_log13.txt -executeMethod BuildScript.BuildDesktopPet
