@echo off
rem Friendly entry (double-click). Real logic lives in DeepSeekWidget-Setup.ps1.
cd /d "%~dp0"
if exist "%~dp0DeepSeekWidget-Setup.ps1" (
  call "%~dp0DeepSeekWidget-Setup.cmd" %*
  exit /b %ERRORLEVEL%
)
echo Fallback: legacy installer script not found, trying Add-AppDevPackage.ps1 ...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Add-AppDevPackage.ps1"
if "%~1"=="" pause
