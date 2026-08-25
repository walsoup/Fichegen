@echo off
rem PROFstudio MSIX sideload installer (self-elevates via the PowerShell script).
setlocal
set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Install-MSIX.ps1" %*
if errorlevel 1 pause
endlocal
