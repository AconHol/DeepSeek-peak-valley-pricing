# 构建「绿色版」（免安装、免证书、免下载运行库）并打包 zip
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File installer\build-portable.ps1
#
# 说明：绿色版直接使用 MSIX 构建输出目录（bin\x64\Release\...\win-x64），
#       该目录自带 resources.pri 与自包含的 Windows App SDK 运行库，
#       脱离 MSIX 安装即可直接运行；不要用 dotnet publish（会丢失 resources.pri）。
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReleaseDir  = 'C:\Users\75366\CodexOutput\DeepSeek峰谷小组件-WinUI\release'
)
$ErrorActionPreference = 'Stop'

$manifest = Join-Path $ProjectRoot 'src\DeepSeekPeakWidget\AppxManifest.xml'
$xml = [xml](Get-Content -Raw -LiteralPath $manifest)
$ver = $xml.Package.Identity.Version
Write-Host "绿色版版本：$ver"

$proj = Join-Path $ProjectRoot 'src\DeepSeekPeakWidget\DeepSeekPeakWidget.csproj'
dotnet build $proj -c Release -p:Platform=x64 -v:q --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败（退出码 $LASTEXITCODE）" }

$bin = Join-Path $ProjectRoot 'src\DeepSeekPeakWidget\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64'
if (-not (Test-Path (Join-Path $bin 'DeepSeekPeakWidget.exe'))) { throw "未找到构建输出：$bin" }
if (-not (Test-Path (Join-Path $bin 'resources.pri'))) { throw "构建输出缺少 resources.pri，绿色版无法启动：$bin" }

$stage = Join-Path $env:TEMP 'DeepSeekPeakWidget-portable-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $bin '*') $stage -Recurse -Force

# 去掉调试符号与不需要的文件，减小体积
Get-ChildItem $stage -Filter *.pdb -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $stage -Filter *.appxsym -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $stage -Filter *.winmd -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

$readme = @"
DeepSeek 峰谷计价小组件 · 绿色版 v$ver（免安装）
================================================

用法：双击 DeepSeekPeakWidget.exe 即可运行，无需安装、无需管理员、无需安装证书，
      也无需另外安装 .NET / Windows App SDK 运行库（已自带）。

说明：
  * 配置保存在 %LOCALAPPDATA%\DeepSeekPeakWidget\config.json（与安装版共用同一份配置）。
  * 开机自启：右键小组件 → 开机自启（绿色版通过当前用户注册表 Run 项实现）。
  * 卸载：直接删除本文件夹，并在右键菜单里关闭开机自启即可。
  * 节假日：默认自动联网更新；断网时使用内置清单。
  * 余额：右键 → 个性化设置 → 填自己的 DeepSeek API Key。

注意：绿色版无法使用安装版的商店式开机自启（StartupTask），已改为注册表方式，效果一致。
"@
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText((Join-Path $stage '使用说明.txt'), $readme, $utf8Bom)

if (-not (Test-Path $ReleaseDir)) { New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null }
$zip = Join-Path $ReleaseDir "DeepSeekPeakWidget-portable-$ver-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("绿色版已打包：{0}（{1:N1} MB）" -f $zip, ((Get-Item $zip).Length / 1MB))
Remove-Item $stage -Recurse -Force
