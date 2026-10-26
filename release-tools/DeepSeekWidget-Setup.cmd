@echo off
rem =====================================================================
rem  DeepSeek Widget - one-click installer for the FULL (MSIX) version.
rem  It will: trust the signing certificate -> install/upgrade the MSIX
rem           -> launch the widget. Needs no local files: if the .ps1 or
rem           the package is missing, both are downloaded automatically.
rem
rem  Double-click this file. Optional switches (pass-through):
rem    -Uninstall  -Reinstall  -NoLaunch  -Offline  -MachineWide
rem =====================================================================
setlocal
cd /d "%~dp0"
title DeepSeek Peak Valley Widget - one-click setup

set "PS1=%~dp0DeepSeekWidget-Setup.ps1"
if exist "%PS1%" goto :run

echo Setup script not found next to this file. Downloading from GitHub...
set "PS1=%TEMP%\DeepSeekWidget-Setup.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/AconHol/DeepSeek-peak-valley-pricing/main/release-tools/DeepSeekWidget-Setup.ps1' -OutFile '%TEMP%\DeepSeekWidget-Setup.ps1' -UseBasicParsing } catch { Write-Host 'Download failed: ' -NoNewline; Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }"
if not exist "%PS1%" goto :fail

:run
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
  echo [WARN] Setup finished with code %RC%. See the messages above.
)
if "%~1"=="" pause
exit /b %RC%

:fail
echo.
echo [ERROR] Could not download the setup script. Check your network
echo         ^(GitHub may be blocked^), or put DeepSeekWidget-Setup.ps1
echo         next to this file and run again.
if "%~1"=="" pause
exit /b 1
