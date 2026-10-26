@echo off
rem 一键安装「完整功能版」：安装证书 + 安装/升级 MSIX + 启动。
rem 实际逻辑在同目录的 DeepSeekWidget-Setup.cmd / .ps1；本文件只是中文名入口。
rem 支持参数：-Uninstall 卸载  -Reinstall 修复重装  -NoLaunch 装完不启动
cd /d "%~dp0"
if exist "DeepSeekWidget-Setup.cmd" (
  call "DeepSeekWidget-Setup.cmd" %*
  exit /b %ERRORLEVEL%
)
echo 未找到 DeepSeekWidget-Setup.cmd，请把本文件与它放在同一文件夹后重试。
pause
exit /b 1
