# 构建「一键安装」exe（.NET 单文件自解压启动器，实验性）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File installer\build-installer.ps1
#
# 背景：原 IExpress/wextract 自解压在部分系统上会报「不支持此系统的簇大小」/
#       「创建进程时出错：参数错误」，因此改用 .NET 单文件启动器：
#       内嵌 MSIX + 证书 + 安装脚本，运行时释放到临时目录后调用
#       DeepSeekWidget-Setup.ps1 完成「装证书 → 安装/升级 MSIX → 启动」。
# 注意：该 exe 尚未在受限环境（如带写保护策略的机器）验证通过，
#       当前发布以「安装程序.zip + 双击安装.cmd」为主；本脚本产物仅作实验/备用。
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReleaseDir  = 'C:\Users\75366\CodexOutput\DeepSeek峰谷小组件-WinUI\release',
    [string]$OutName     = 'DeepSeekWidgetSetup-launcher-实验版'
)
$ErrorActionPreference = 'Stop'

$manifest = Join-Path $ProjectRoot 'src\DeepSeekPeakWidget\AppxManifest.xml'
$xml = [xml](Get-Content -Raw -LiteralPath $manifest)
$ver = $xml.Package.Identity.Version
Write-Host "安装器版本：$ver"

$launcher = Join-Path $ProjectRoot 'installer\SetupLauncher'
$launcherProj = Join-Path $launcher 'SetupLauncher.csproj'
$payload = Join-Path $launcher 'payload'

# 1) 准备内嵌文件
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload -Force | Out-Null

$msix = Join-Path $ProjectRoot "appxout\DeepSeekPeakWidget_${ver}_x64_Test\DeepSeekPeakWidget_${ver}_x64.msix"
if (-not (Test-Path $msix)) { throw "未找到 MSIX：$msix" }
Copy-Item $msix (Join-Path $payload 'DeepSeekPeakWidget.msix')

$cer = Join-Path $ReleaseDir 'pve-widget-cert.cer'
if (-not (Test-Path $cer)) { $cer = Join-Path $ProjectRoot 'signing\pve-widget-cert.cer' }
if (-not (Test-Path $cer)) { throw "未找到证书：$cer" }
Copy-Item $cer (Join-Path $payload 'pve-widget-cert.cer')

Copy-Item (Join-Path $ProjectRoot 'release-tools\DeepSeekWidget-Setup.ps1') (Join-Path $payload 'DeepSeekWidget-Setup.ps1')

# 2) 发布单文件启动器
$publishDir = Join-Path $env:TEMP 'DeepSeekPeakWidgetSetupPublish'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $launcherProj -c Release -o $publishDir -v:q --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }

$exe = Join-Path $publishDir 'DeepSeekWidgetSetup.exe'
if (-not (Test-Path $exe)) { throw "未生成启动器：$exe" }

if (-not (Test-Path $ReleaseDir)) { New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null }
$target = Join-Path $ReleaseDir ($OutName + '.exe')
Copy-Item $exe $target -Force

# 3) 可选：用同一张自签名证书签名（减少“未知发布者”提示；找不到 signtool 则跳过）
try {
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    $pfx = 'C:\Users\75366\CodexOutput\PVE监视小组件-WinUI\signing\pve-widget-build.pfx'
    if ($signtool -and (Test-Path $pfx)) {
        & $signtool.FullName sign /f $pfx /p 'pvebuild123' /fd sha256 $target | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Host '已签名（CN=PVEWidget Dev）' }
    }
} catch { Write-Host '签名跳过（不影响安装）' }

Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ("安装器已生成：{0}（{1:N1} MB）" -f $target, ((Get-Item $target).Length / 1MB))
