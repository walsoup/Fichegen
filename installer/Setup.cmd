@echo off
rem PROFstudio standalone installer launcher.
rem Double-click entry point: runs the per-user PowerShell installer.
setlocal
set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Install-PROFstudio.ps1" %*
if errorlevel 1 (
    echo.
    echo L'installation a echoue. Consultez les messages ci-dessus.
    pause
)
endlocal
