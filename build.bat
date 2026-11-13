@echo off
rem Builds Hauppauge Live Capture Studio (self-contained single-file EXE, win-x64).
rem Optional switches are passed through, e.g.  build.bat -SkipFfmpeg   or   build.bat -Clean
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
    echo.
    echo *** Build FAILED. Read the messages above. ***
    pause
    exit /b 1
)

echo.
echo Done. Open the folder:  dist\Hauppauge Live Capture Studio
pause
