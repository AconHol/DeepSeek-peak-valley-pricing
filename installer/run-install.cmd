@echo off
rem 由自解压安装器调用：切换到解压目录并执行安装脚本
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "install.ps1"
