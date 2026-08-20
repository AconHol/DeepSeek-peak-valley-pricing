# 安装 DeepSeek 峰谷小组件（自动信任证书并安装 MSIX）
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$msix = Get-ChildItem -LiteralPath $scriptDir -Filter *.msix | Select-Object -First 1
$cer  = Get-ChildItem -LiteralPath $scriptDir -Filter *.cer  | Select-Object -First 1

if (-not $msix) { throw '未找到 .msix 安装包' }

if ($cer) {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer.FullName)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', 'CurrentUser')
    $store.Open('ReadWrite')
    try {
        if (-not ($store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) {
            $store.Add($cert)
            Write-Host "已信任开发者证书: $($cert.Subject)"
        } else {
            Write-Host '开发者证书已信任'
        }
    } finally {
        $store.Close()
    }
}

Add-AppxPackage -Path $msix.FullName
Write-Host '安装完成。'
