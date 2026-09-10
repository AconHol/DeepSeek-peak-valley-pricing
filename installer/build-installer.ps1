# 构建“一键安装”自解压安装器（iexpress）
# 用法：powershell -File installer\build-installer.ps1
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReleaseDir  = 'C:\Users\75366\CodexOutput\DeepSeek峰谷小组件-WinUI\release',
    [string]$OutName     = 'DeepSeek峰谷小组件-WinUI-安装程序'
)
$ErrorActionPreference = 'Stop'

# 读取 AppxManifest 版本
$manifest = Join-Path $ProjectRoot 'src\DeepSeekPeakWidget\AppxManifest.xml'
$xml = [xml](Get-Content -Raw -LiteralPath $manifest)
$ver = $xml.Package.Identity.Version

# 构建目录：必须使用纯英文路径。
# iexpress 解析 SED 时按当前系统 ANSI 代码页处理，SED 中含非 ASCII 字符
# （如中文项目路径）会导致打包静默失败（退出码 1），故源目录与打包输出
# 都放在英文临时目录，最后再复制到 release 下的目标文件名。
$build = Join-Path ([System.IO.Path]::GetTempPath()) 'DeepSeekPeakWidgetInstaller'
if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force }
New-Item -ItemType Directory -Path $build | Out-Null

# 拷贝 MSIX 与证书（安装器内使用 ASCII 文件名，避免编码问题）
$msixSrc = Join-Path $ProjectRoot "appxout\DeepSeekPeakWidget_${ver}_x64_Test\DeepSeekPeakWidget_${ver}_x64.msix"
if (-not (Test-Path -LiteralPath $msixSrc)) { throw "未找到 MSIX：$msixSrc" }
Copy-Item -LiteralPath $msixSrc -Destination (Join-Path $build 'DeepSeekPeakWidget.msix')

$cerSrc = Join-Path $ReleaseDir 'pve-widget-cert.cer'
if (-not (Test-Path -LiteralPath $cerSrc)) {
    $cerSrc = Join-Path $ProjectRoot 'signing\pve-widget-cert.cer'
}
if (-not (Test-Path -LiteralPath $cerSrc)) { throw "未找到证书：$cerSrc" }
Copy-Item -LiteralPath $cerSrc -Destination (Join-Path $build 'pve-widget-cert.cer')

# 生成 install.ps1（注入版本号，UTF-8 BOM 以便 PowerShell 5.1 正确读取中文）
$installTemplate = Get-Content -Raw -LiteralPath (Join-Path $ProjectRoot 'installer\install.ps1')
$installContent = $installTemplate.Replace('__VERSION__', $ver)
$enc = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText((Join-Path $build 'install.ps1'), $installContent, $enc)
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'installer\run-install.cmd') -Destination (Join-Path $build 'run-install.cmd')

# 输出文件
$targetName = Join-Path $ReleaseDir ($OutName + '.exe')
$packedExe = Join-Path $build 'installer-out.exe'
$sedPath = Join-Path $build 'installer.sed'

# 生成 iexpress SED（最小化配置，避免可选键导致打包失败；路径含中文需 ANSI 编码）
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimationHandler=1
UseCustomAnimationHandler=0
CompressionType=MSZIP
LongFileNames=1
TargetName=$packedExe
FriendlyName=DeepSeek Peak Valley Pricing Widget v$ver Installer
AppLaunched=cmd.exe /c run-install.cmd
SourceFiles=SourceFiles
[SourceFiles]
SourceFiles0=$build
[SourceFiles0]
run-install.cmd=
install.ps1=
DeepSeekPeakWidget.msix=
pve-widget-cert.cer=
"@
[System.IO.File]::WriteAllText($sedPath, $sed, [System.Text.Encoding]::Default)

# 运行 iexpress 打包
$p = Start-Process -FilePath 'C:\Windows\System32\iexpress.exe' -ArgumentList @('/N', '/Q', $sedPath) -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "iexpress 打包失败，退出码：$($p.ExitCode)" }
if (-not (Test-Path -LiteralPath $packedExe)) { throw "未生成安装器：$packedExe" }

# 复制到 release 目标文件名（中文名，避开 iexpress 的路径编码限制）
Copy-Item -LiteralPath $packedExe -Destination $targetName -Force

Remove-Item -LiteralPath $build -Recurse -Force
Write-Host "安装器已生成：$targetName"
