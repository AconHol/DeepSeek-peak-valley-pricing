@echo off
rem One-click setup: install signing certificate + MSIX + launch widget.
rem Options are passed through to DeepSeekWidget-Setup.ps1, e.g.:
rem   DeepSeekWidget-Setup.cmd -Online
rem   DeepSeekWidget-Setup.cmd -Uninstall
setlocal
cd /d "%~dp0"
title DeepSeek Peak Valley Widget Setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DeepSeekWidget-Setup.ps1" %*
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo [ERROR] Setup failed, exit code %RC%.
  echo Log: %TEMP%\DeepSeekPeakWidget-setup.log
)
if "%~1"=="" pause
exit /b %RC%
