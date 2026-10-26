@echo off
rem ==========================================================================
rem  DeepSeek 峰谷计价小组件 - 单文件一键安装脚本（完整功能版）
rem  双击本文件即可：装证书 -> 安装/升级 MSIX -> 启动
rem  证书已内嵌；安装包优先用同目录的 *.msix，没有则自动从 GitHub 下载。
rem ==========================================================================
setlocal
title DeepSeek 峰谷计价小组件 一键安装
set "DSH_SETUP_SELF=%~f0"
echo 正在启动安装程序，请稍候...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$f=$env:DSH_SETUP_SELF; $c=[IO.File]::ReadAllText($f,[Text.Encoding]::GetEncoding(936)); $m='#' + '__PAYLOAD__'; $i=$c.IndexOf($m); if ($i -lt 0) { Write-Host 'PAYLOAD NOT FOUND' -ForegroundColor Red; exit 9 }; iex $c.Substring($i + $m.Length)"
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
  echo [WARN] 安装未成功（退出码 %RC%）。可右键“以管理员身份运行”再试。
)
pause
exit /b %RC%
#__PAYLOAD__
$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
$Pkg = 'DeepSeekPeak.Widget'; $Proc = 'DeepSeekPeakWidget'
$Root = Split-Path -Parent $env:DSH_SETUP_SELF
$tmp = Join-Path $env:TEMP 'DeepSeekWidgetOneClick'
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$certB64 = (
  'MIIDADCCAeigAwIBAgIQXlzDbT10SbRE3pI6nBD6UzANBgkqhkiG9w0BAQsFADAYMRYwFAYDVQQDDA1QVkVXaWRnZX' +
  'QgRGV2MB4XDTI2MDgwODA5MTAzNVoXDTMxMDgwODA5MjAzNVowGDEWMBQGA1UEAwwNUFZFV2lkZ2V0IERldjCCASIw' +
  'DQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBANFQTIjSF637H9ZRMTeRdAupa4vJuTZsEj7fnM9ZJGUyD19IsciitU' +
  't25QTPXZ8ctmOJuinARjNqkiMOyI56eLxP9ezNwnaaxVXqMNJX+EEUmfgut4/Gul25utEspcodCpHAPtNjHi1R7Aik' +
  'Kji9qIAdlieN0UbyVZM+qvVcKFN/qV82+GeqsGI5IemPPtpuREi7/+UHELIuEC6X1q/Y6yKwKEnAMUSWmJFrBegWy+' +
  '3MKwG/lpnPKJkcke7cGotJLNLK2S2Q/Fyu7Y5Dwa4uMKujYQWYPlkZwX/gFScd9kyazOAmVdZiy7mEG4+lVniKodo2' +
  '4Hs4cmbvKMz43BjXv5kCAwEAAaNGMEQwDgYDVR0PAQH/BAQDAgeAMBMGA1UdJQQMMAoGCCsGAQUFBwMDMB0GA1UdDg' +
  'QWBBTIxZxe//rGeq9ih+xipnnzcgd1vzANBgkqhkiG9w0BAQsFAAOCAQEAYNEhTli4/1mVytGf+TYxUYPJLyxallSi' +
  'p2jdGnrb3eZHXyKX/R0cBxqyiv/8LYUTYAOwVGQLcyZ6x9mGQ3t2SG3Ravp98tj6qw0lv26sT4rtgrLuw6ZKnphawZ' +
  '+5JRR4iMjPw4xmkGWUwYRxPM0q4zycxWGCKzknMibqozCmt94UQp/q4qOGc04MjZNt5ctcDJF6YPje5Jy67UN2pV+L' +
  '5vx3Vur3Dd5VjVTGJCqXkuGI7bhIftjEgesGF49Pl0NGZhdyObDD63LrCo/+ZdvMVUJbGWvOG7uRJv7ifVhTjmvUyi' +
  'afe/52psKVUNhN1qN2iQxYU2Vl/3JxyCbJtD2Xbw=='
)
$cerPath = Join-Path $tmp 'pve-widget-cert.cer'
[IO.File]::WriteAllBytes($cerPath, [Convert]::FromBase64String($certB64))
function Step([string]$t) { Write-Host ''; Write-Host ('== ' + $t + ' ==') -ForegroundColor Cyan }
function Info([string]$m) { Write-Host ('  ' + $m) }
function Ok([string]$m) { Write-Host ('  ' + $m) -ForegroundColor Green }
function Warn([string]$m) { Write-Host ('  ' + $m) -ForegroundColor Yellow }
Write-Host 'DeepSeek 峰谷计价小组件 · 一键安装（完整功能版）' -ForegroundColor White
Step '定位安装包'
$msix = $null
foreach ($dir in @($Root, (Get-Location).Path)) {
  $hit = Get-ChildItem -LiteralPath $dir -Filter *.msix -File -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($hit) { $msix = $hit.FullName; break }
}
if (-not $msix) {
  Info '本地没有 MSIX，正在从 GitHub 最新 Release 下载…'
  $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/AconHol/DeepSeek-peak-valley-pricing/releases/latest' -UseBasicParsing -Headers @{ 'User-Agent' = 'DeepSeekWidgetOneClick' } -TimeoutSec 120
  $asset = $rel.assets | Where-Object { $_.name -like '*.msix' } | Select-Object -First 1
  if (-not $asset) { throw '最新 Release 中找不到 MSIX 安装包' }
  $msix = Join-Path $tmp $asset.name
  if (-not (Test-Path $msix) -or (Get-Item $msix).Length -ne $asset.size) {
    Info ('下载 ' + $asset.name + '（约 ' + [math]::Round($asset.size/1MB,1) + ' MB）…')
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $msix -UseBasicParsing -TimeoutSec 3600
  }
  Ok '安装包下载完成。'
}
Ok ('MSIX：' + $msix)
Step '安装签名证书（CN=PVEWidget Dev）'
$x509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
foreach ($storeName in @('Root','TrustedPublisher','TrustedPeople')) {
  try {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
    $store.Open('ReadWrite')
    if (-not ($store.Certificates | Where-Object { $_.Thumbprint -eq $x509.Thumbprint })) { $store.Add($x509); Ok ('已安装：CurrentUser\' + $storeName) } else { Info ('已存在：CurrentUser\' + $storeName) }
    $store.Close()
  } catch { Warn ('写入 ' + $storeName + ' 失败：' + $_.Exception.Message) }
}
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
  foreach ($storeName in @('Root','TrustedPeople')) {
    try {
      $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine')
      $store.Open('ReadWrite')
      if (-not ($store.Certificates | Where-Object { $_.Thumbprint -eq $x509.Thumbprint })) { $store.Add($x509); Ok ('已安装：LocalMachine\' + $storeName) }
      $store.Close()
    } catch { }
  }
}
Step '安装小组件'
Get-Process -Name $Proc -ErrorAction SilentlyContinue | Stop-Process -Force
try {
  Add-AppxPackage -Path $msix -ForceApplicationShutdown -ErrorAction Stop
  Ok '安装成功。'
} catch {
  $msg = $_.Exception.Message
  if ($msg -match '0x80073CFB') { Ok '已安装同版本，跳过安装。' }
  elseif ($msg -match '0x80073CF9') { throw ('系统未允许旁加载：' + $msg + '  请右键脚本选择“以管理员身份运行”后重试。') }
  elseif ($msg -match '0x800B0109') { throw ('证书未被信任：' + $msg + '  请右键脚本选择“以管理员身份运行”后重试。') }
  else { throw }
}
$installed = Get-AppxPackage -Name $Pkg
if (-not $installed) { throw '安装后未检测到已安装的小组件' }
Ok ('已安装版本：' + $installed.Version)
Step '启动'
Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\' + $installed.PackageFamilyName + '!App') | Out-Null
Start-Sleep -Seconds 5
if (Get-Process -Name $Proc -ErrorAction SilentlyContinue) { Ok '小组件已启动。' } else { Warn '已请求启动，请从开始菜单手动打开。' }
Step '完成'
Info ('配置文件：' + (Join-Path $env:LOCALAPPDATA 'DeepSeekPeakWidget\config.json'))
Info '右键浮窗 → 个性化设置 可填 DeepSeek API Key、调主题、开开机自启。'
Ok '安装完成。'
