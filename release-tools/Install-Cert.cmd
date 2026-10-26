@echo off
rem One-click certificate installer: trusts CN=PVEWidget Dev so the MSIX can be installed.
rem Options are passed through, e.g.:  Install-Cert.cmd -Remove
setlocal
cd /d "%~dp0"
title DeepSeek widget certificate setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Cert.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
  echo [WARN] Script finished with code %RC%. Check the messages above.
  echo        If it says access denied, right-click this file -^> "Run as administrator".
)
if "%~1"=="" pause
exit /b %RC%
