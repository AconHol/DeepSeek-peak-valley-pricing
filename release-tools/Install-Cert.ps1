<#
.SYNOPSIS
    DeepSeek 峰谷计价小组件 · 证书一键安装脚本

.DESCRIPTION
    双击同目录的「安装证书.cmd」（或 Install-Cert.cmd）即可把 pve-widget-cert.cer
    装进当前用户的受信任存储，让系统信任本小组件的自签名发布者（CN=PVEWidget Dev）。

    默认安装位置（当前用户，无需管理员）：
      Cert:\CurrentUser\Root            受信任的根证书颁发机构
      Cert:\CurrentUser\TrustedPublisher 受信任的发布者
      Cert:\CurrentUser\TrustedPeople    受信任人
    以管理员身份运行或加 -MachineWide 时，额外安装到本机（所有用户）：
      Cert:\LocalMachine\Root
      Cert:\LocalMachine\TrustedPeople

.PARAMETER Cert
    指定 .cer 证书路径；默认自动查找脚本所在目录下的 *.cer。

.PARAMETER Remove
    卸载证书（从上述存储中删除），用于彻底清理。

.PARAMETER MachineWide
    同时安装到本机存储（所有用户可用），需要管理员权限；脚本会自动请求提权。

.PARAMETER Quiet
    减少输出（仍会打印最终结果）。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Cert.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Cert.ps1 -Remove
#>
#Requires -Version 5.1
param(
    [string]$Cert,
    [switch]$Remove,
    [switch]$MachineWide,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$script:Root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Say {
    param([string]$Text, [string]$Level = 'INFO')
    if ($Quiet -and $Level -eq 'INFO') { return }
    switch ($Level) {
        'OK'    { Write-Host ("  " + $Text) -ForegroundColor Green }
        'WARN'  { Write-Host ("  " + $Text) -ForegroundColor Yellow }
        'ERROR' { Write-Host ("  " + $Text) -ForegroundColor Red }
        default { Write-Host ("  " + $Text) }
    }
}

function Test-Admin {
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { return $false }
}

function Invoke-Elevated {
    Say '需要管理员权限才能装进本机存储，正在请求提权（UAC 选“是”）…' 'WARN'
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath), '-MachineWide')
    if ($Cert) { $args += @('-Cert', ('"{0}"' -f $Cert)) }
    if ($Remove) { $args += '-Remove' }
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $args | Out-Null
        exit 0
    } catch {
        Say '提权失败，请右键“以管理员身份运行”后重试。' 'ERROR'
        exit 1
    }
}

function Find-CertFile {
    if ($Cert) {
        if (-not (Test-Path -LiteralPath $Cert)) { throw "指定的证书不存在：$Cert" }
        return (Resolve-Path -LiteralPath $Cert).Path
    }
    $candidates = @()
    foreach ($dir in @($script:Root, (Get-Location).Path)) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $candidates += Get-ChildItem -LiteralPath $dir -Filter *.cer -File -ErrorAction SilentlyContinue
    }
    $hit = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $hit) { throw "未找到 .cer 证书。请把证书与本脚本放在同一文件夹，或用 -Cert 指定路径。" }
    return $hit.FullName
}

function Get-StoreObject {
    param([string]$Name, [string]$Location)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($Name, $Location)
    $store.Open('ReadWrite')
    return $store
}

function Test-InStore {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
          [string]$Name, [string]$Location)
    try {
        $store = Get-StoreObject -Name $Name -Location $Location
        try {
            return $null -ne ($store.Certificates | Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint })
        } finally { $store.Close() }
    } catch {
        return $false
    }
}

function Add-ToStore {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
          [string]$Name, [string]$Location)
    $store = Get-StoreObject -Name $Name -Location $Location
    try {
        if ($store.Certificates | Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint }) {
            Say ("已存在：{0}\{1}" -f $Location, $Name) 'OK'
            return
        }
        $store.Add($Certificate)
        Say ("已安装：{0}\{1}" -f $Location, $Name) 'OK'
    } finally { $store.Close() }
}

function Remove-FromStore {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
          [string]$Name, [string]$Location)
    try {
        $store = Get-StoreObject -Name $Name -Location $Location
        try {
            $hits = @($store.Certificates | Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint })
            if ($hits.Count -eq 0) {
                Say ("未安装：{0}\{1}" -f $Location, $Name)
                return $true
            }
            foreach ($h in $hits) { $store.Remove($h) }
            Say ("已删除：{0}\{1}" -f $Location, $Name) 'OK'
            return $true
        } finally { $store.Close() }
    } catch {
        Say ("删除失败（{0}\{1}）：{2}" -f $Location, $Name, $_.Exception.Message) 'WARN'
        return $false
    }
}

try {
    Write-Host ''
    Write-Host '  DeepSeek 峰谷计价小组件 · 证书一键安装' -ForegroundColor White
    Write-Host ''

    $certPath = Find-CertFile
    $x509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath)
    Say ("证书文件：{0}" -f (Split-Path -Leaf $certPath))
    Say ("  主题：{0}" -f $x509.Subject)
    Say ("  指纹：{0}" -f $x509.Thumbprint)
    Say ("  有效期至：{0:yyyy-MM-dd}" -f $x509.NotAfter)

    if ($Remove) {
        $ok = $true
        foreach ($name in @('Root', 'TrustedPublisher', 'TrustedPeople')) {
            $ok = (Remove-FromStore -Certificate $x509 -Name $name -Location 'CurrentUser') -and $ok
        }
        if (Test-Admin) {
            foreach ($name in @('Root', 'TrustedPeople')) {
                $ok = (Remove-FromStore -Certificate $x509 -Name $name -Location 'LocalMachine') -and $ok
            }
        }
        $left = Test-InStore -Certificate $x509 -Name 'Root' -Location 'CurrentUser'
        if ($left) {
            Say '仍有残留（受信任根需要交互确认才能删除）。可打开 certmgr.msc → 受信任的根证书颁发机构，手动删除 CN=PVEWidget Dev。' 'WARN'
        }
        Write-Host ''
        Say ($(if ($ok) { '证书已清理。' } else { '清理完成，但有个别存储删除失败（见上）。' })) ($(if ($ok) { 'OK' } else { 'WARN' }))
        exit $(if ($ok) { 0 } else { 2 })
    }

    if (-not (Test-Admin) -and -not $MachineWide) {
        Say '当前为普通用户：只装进当前用户存储（安装小组件已足够）。' 'OK'
    }

    foreach ($name in @('Root', 'TrustedPublisher', 'TrustedPeople')) {
        Add-ToStore -Certificate $x509 -Name $name -Location 'CurrentUser'
    }

    if ((Test-Admin) -or $MachineWide) {
        if (-not (Test-Admin)) { Invoke-Elevated }
        foreach ($name in @('Root', 'TrustedPeople')) {
            Add-ToStore -Certificate $x509 -Name $name -Location 'LocalMachine'
        }
    }

    Write-Host ''
    Say '校验安装结果：'
    $allOk = $true
    foreach ($name in @('Root', 'TrustedPublisher', 'TrustedPeople')) {
        $hit = Test-InStore -Certificate $x509 -Name $name -Location 'CurrentUser'
        if (-not $hit) { $allOk = $false }
        Say ("  CurrentUser\{0}：{1}" -f $name, $(if ($hit) { '已信任' } else { '缺失' })) ($(if ($hit) { 'OK' } else { 'ERROR' }))
    }
    if (Test-Admin) {
        foreach ($name in @('Root', 'TrustedPeople')) {
            $hit = Test-InStore -Certificate $x509 -Name $name -Location 'LocalMachine'
            Say ("  LocalMachine\{0}：{1}" -f $name, $(if ($hit) { '已信任' } else { '缺失' })) ($(if ($hit) { 'OK' } else { 'WARN' }))
        }
    }

    Write-Host ''
    if ($allOk) {
        Say '证书安装完成，现在可以安装/启动小组件了（双击安装程序，或运行 DeepSeekWidget-Setup.cmd）。' 'OK'
        exit 0
    }
    Say '部分存储安装失败，请右键“以管理员身份运行”本脚本后重试。' 'ERROR'
    exit 1
} catch {
    Say ("证书安装失败：{0}" -f $_.Exception.Message) 'ERROR'
    exit 1
}
