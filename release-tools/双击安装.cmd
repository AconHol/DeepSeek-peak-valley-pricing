@echo off
rem One-click installer for the manual package (no PowerShell knowledge needed).
cd /d "%~dp0"
echo Installing DeepSeek Peak Valley Widget...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "Add-AppDevPackage.ps1"
if errorlevel 1 (
  echo.
  echo Install failed. Try: right-click this file -^> "Run as administrator",
  echo or enable Developer Mode in Windows Settings and run again.
) else (
  echo.
  echo Done. The widget should start shortly.
)
pause
