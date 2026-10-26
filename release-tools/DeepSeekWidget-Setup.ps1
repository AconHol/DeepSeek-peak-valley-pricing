<#
.SYNOPSIS
    DeepSeek 峰谷计价小组件（WinUI 3）一键安装脚本：安装签名证书 + 安装/升级 MSIX + 启动。

.DESCRIPTION
    把本脚本与 .msix、.cer 放在同一个文件夹，双击同目录的 DeepSeekWidget-Setup.cmd
    （或「双击安装.cmd」）即可。脚本会依次：

      1. 检查系统版本与 CPU 架构；
      2. 把自签名证书装进「当前用户」的受信任根 / 受信任发布者 / 受信任人
         （以管理员运行或加 -MachineWide 时，额外装进「本机」存储，所有用户可用）；
      3. 结束旧进程，安装或升级 MSIX；遇到“未允许旁加载”“证书不受信任”等错误时
         自动提权并修复后重试；
      4. 校验安装版本并启动小组件，最后给出配置路径与后续操作提示。

.PARAMETER Msix
    指定 .msix 路径；默认自动查找脚本所在目录下的 *.msix。

.PARAMETER Cert
    指定 .cer 路径；默认自动查找脚本所在目录下的 *.cer。

.PARAMETER Online
    保留参数（兼容旧用法）。默认行为已经是“本地没有安装包时自动从 GitHub 下载”。

.PARAMETER Offline
    禁止联网下载：本地没有 MSIX 时直接报错，适合离线环境。

.PARAMETER MachineWide
    把证书同时安装到本机证书存储（所有用户可用）。需要管理员权限；
    脚本在遇到证书类错误时也会自动以管理员身份重跑并带上此参数。

.PARAMETER Reinstall
    已安装相同版本时，先卸载再重新安装（用于修复）。

.PARAMETER Uninstall
    卸载小组件后退出，不安装。

.PARAMETER NoLaunch
    安装完成后不自动启动小组件。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\DeepSeekWidget-Setup.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\DeepSeekWidget-Setup.ps1 -Uninstall
#>
#Requires -Version 5.1
param(
    [string]$Msix,
    [string]$Cert,
    [switch]$Online,
    [switch]$Offline,
    [switch]$MachineWide,
    [switch]$Reinstall,
    [switch]$Uninstall,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue } catch { }
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$PkgName = 'DeepSeekPeak.Widget'
$ProcessName = 'DeepSeekPeakWidget'
$RepoApi = 'https://api.github.com/repos/AconHol/DeepSeek-peak-valley-pricing/releases/latest'
$LogFile = Join-Path $env:TEMP 'DeepSeekPeakWidget-setup.log'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('INFO', 'OK', 'WARN', 'ERROR')][string]$Level = 'INFO'
    )
    $line = '[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}' -f (Get-Date), $Level, $Message
    try { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 } catch { }
    switch ($Level) {
        'OK'    { Write-Host ("  " + $Message) -ForegroundColor Green }
        'WARN'  { Write-Host ("  " + $Message) -ForegroundColor Yellow }
        'ERROR' { Write-Host ("  " + $Message) -ForegroundColor Red }
        default { Write-Host ("  " + $Message) }
    }
}

function Write-Title {
    param([string]$Text)
    Write-Host ''
    Write-Host ("== " + $Text + " ==") -ForegroundColor Cyan
}

function Test-Admin {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch {
        return $false
    }
}

function Invoke-Elevated {
    param([string]$Reason)
    Write-Log $Reason WARN
    Write-Log '正在以管理员身份重新运行（会弹出 UAC 提示，请选择“是”）…'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath), '-MachineWide')
    if ($Online) { $argList += '-Online' }
    if ($Offline) { $argList += '-Offline' }
    if ($NoLaunch) { $argList += '-NoLaunch' }
    if ($Reinstall) { $argList += '-Reinstall' }
    if ($Msix) { $argList += @('-Msix', ('"{0}"' -f $Msix)) }
    if ($Cert) { $argList += @('-Cert', ('"{0}"' -f $Cert)) }
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList | Out-Null
        exit 0
    } catch {
        Write-Log '无法自动提权。请右键脚本（或 cmd）选择“以管理员身份运行”后重试。' ERROR
        exit 1
    }
}

function Install-CertToStore {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [string]$StoreName,
        [ValidateSet('CurrentUser', 'LocalMachine')][string]$Location
    )
    try {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, $Location)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try {
            $existing = $store.Certificates | Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint }
            if ($existing) {
                Write-Log ("证书已存在：{0}\{1}" -f $Location, $StoreName)
            } else {
                $store.Add($Certificate)
                Write-Log ("证书已写入：{0}\{1}" -f $Location, $StoreName) 'OK'
            }
        } finally {
            $store.Close()
        }
    } catch {
        Write-Log ("写入证书失败（{0}\{1}）：{2}" -f $Location, $StoreName, $_.Exception.Message) 'WARN'
    }
}

function Install-TrustedCert {
    param([string]$CertPath)
    if (-not $CertPath -or -not (Test-Path -LiteralPath $CertPath)) {
        Write-Log '未找到证书文件，跳过证书安装（安装可能因“发布者不受信任”失败）。' 'WARN'
        return
    }
    Write-Log ("使用证书：{0}" -f (Split-Path -Leaf $CertPath))
    $x509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path -LiteralPath $CertPath).Path)
    Write-Log ("  主题：{0}" -f $x509.Subject)
    Write-Log ("  指纹：{0}" -f $x509.Thumbprint)

    foreach ($storeName in @('Root', 'TrustedPublisher', 'TrustedPeople')) {
        Install-CertToStore -Certificate $x509 -StoreName $storeName -Location 'CurrentUser'
    }

    if ((Test-Admin) -or $MachineWide) {
        if (-not (Test-Admin)) {
            Invoke-Elevated '需要管理员权限才能把证书装进本机存储。'
        }
        foreach ($storeName in @('Root', 'TrustedPeople')) {
            Install-CertToStore -Certificate $x509 -StoreName $storeName -Location 'LocalMachine'
        }
    } else {
        Write-Log '未以管理员运行：证书只对当前用户生效（通常已足够）。' 'INFO'
    }
}

function Get-MsixVersion {
    param([string]$Path)
    $result = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $entry = $zip.Entries | Where-Object { $_.FullName -ieq 'AppxManifest.xml' } | Select-Object -First 1
            if ($entry) {
                $reader = New-Object System.IO.StreamReader($entry.Open())
                try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
                [xml]$manifest = $text
                $version = $manifest.Package.Identity.Version
                if ($version) { $result = [Version]$version }
            }
        } finally {
            $zip.Dispose()
        }
    } catch {
        Write-Log ("读取 MSIX 版本失败：{0}" -f $_.Exception.Message) 'WARN'
    }
    return $result
}

function Get-PackageFromGitHub {
    $downloadDir = Join-Path $env:TEMP 'DeepSeekPeakWidget-setup-download'
    if (-not (Test-Path -LiteralPath $downloadDir)) {
        New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
    }

    Write-Log '正在查询 GitHub 最新版本…'
    $headers = @{ 'User-Agent' = 'DeepSeekPeakWidget-Setup' }
    $release = Invoke-RestMethod -Uri $RepoApi -UseBasicParsing -Headers $headers -TimeoutSec 60
    Write-Log ("最新版本：{0}" -f $release.tag_name)

    $msixAsset = $release.assets | Where-Object { $_.name -like '*.msix' } | Select-Object -First 1
    if (-not $msixAsset) { throw '最新 Release 里没有找到 .msix 安装包' }
    $certAsset = $release.assets | Where-Object { $_.name -like '*.cer' } | Select-Object -First 1

    $msixPath = Join-Path $downloadDir $msixAsset.name
    $needDownload = $true
    if (Test-Path -LiteralPath $msixPath) {
        if ((Get-Item -LiteralPath $msixPath).Length -eq $msixAsset.size) { $needDownload = $false }
    }
    if ($needDownload) {
        Write-Log ("下载 MSIX（约 {0:N1} MB），请稍候…" -f ($msixAsset.size / 1MB))
        Invoke-WebRequest -Uri $msixAsset.browser_download_url -OutFile $msixPath -UseBasicParsing -TimeoutSec 1800
        Write-Log 'MSIX 下载完成。' 'OK'
    } else {
        Write-Log '已存在完整下载的 MSIX，跳过下载。'
    }

    $certPath = $null
    if ($certAsset) {
        $certPath = Join-Path $downloadDir $certAsset.name
        if (-not (Test-Path -LiteralPath $certPath)) {
            Write-Log '下载签名证书…'
            Invoke-WebRequest -Uri $certAsset.browser_download_url -OutFile $certPath -UseBasicParsing -TimeoutSec 300
        }
    }

    return [pscustomobject]@{ Msix = $msixPath; Cert = $certPath; Version = $release.tag_name }
}

function Find-LocalFile {
    param([string]$Folder, [string[]]$Patterns)
    foreach ($pattern in $Patterns) {
        $hit = Get-ChildItem -LiteralPath $Folder -Filter $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Resolve-Packages {
    $msixPath = $Msix
    $certPath = $Cert

    if ($msixPath -and -not (Test-Path -LiteralPath $msixPath)) {
        throw "指定的 MSIX 不存在：$msixPath"
    }
    if ($certPath -and -not (Test-Path -LiteralPath $certPath)) {
        throw "指定的证书不存在：$certPath"
    }

    if (-not $msixPath) { $msixPath = Find-LocalFile -Folder $Root -Patterns @('*.msix') }
    if (-not $certPath) { $certPath = Find-LocalFile -Folder $Root -Patterns @('*.cer') }

    if (-not $msixPath) {
        # 双击脚本（不带参数）时也能装：本地没有安装包就自动从 GitHub 最新 Release 下载。
        # 想强制只离线安装，加 -Offline 即可。
        if ($Offline) {
            throw ('未找到 MSIX 安装包（-Offline 已禁用联网下载）。' +
                   '请把 .msix 与本脚本放在同一文件夹后重试。')
        }
        Write-Log '本地未找到 MSIX 安装包，改为从 GitHub 最新 Release 下载。'
        try {
            $remote = Get-PackageFromGitHub
        } catch {
            throw ('本地没有安装包，且从 GitHub 下载失败：{0}。' +
                   '请检查网络，或改用完整安装包 zip（内含 msix 与本脚本）。' -f $_.Exception.Message)
        }
        $msixPath = $remote.Msix
        if (-not $certPath) { $certPath = $remote.Cert }
    }

    if (-not $certPath) {
        Write-Log '未找到 .cer 证书，将跳过证书安装步骤。' 'WARN'
    }

    return [pscustomobject]@{ Msix = $msixPath; Cert = $certPath }
}

function Stop-RunningWidget {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Stop-Process -Id $_.Id -Force -ErrorAction Stop
            Write-Log ("已结束正在运行的旧进程（PID {0}）" -f $_.Id)
        } catch { }
    }
    Start-Sleep -Milliseconds 500
}

function Install-Widget {
    param([string]$MsixPath, [string]$CertPath)

    $installed = Get-AppxPackage -Name $PkgName -ErrorAction SilentlyContinue
    $targetVersion = Get-MsixVersion -Path $MsixPath

    if (-not $targetVersion) {
        Write-Log '无法读取 MSIX 版本，将直接尝试安装。' 'WARN'
    } else {
        Write-Log ("安装包版本：{0}" -f $targetVersion)
    }

    $needInstall = $true
    if ($installed -and $targetVersion) {
        $installedVersion = [Version]$installed.Version.ToString()
        if ($installedVersion -eq $targetVersion) {
            if ($Reinstall) {
                Write-Log ("已安装同版本 {0}，按要求卸载后重装。" -f $installedVersion) 'INFO'
                Stop-RunningWidget
                Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 800
            } else {
                Write-Log ("已安装同版本 {0}，跳过安装（如需修复请加 -Reinstall）。" -f $installedVersion) 'OK'
                $needInstall = $false
            }
        } elseif ($installedVersion -gt $targetVersion) {
            Write-Log ("已安装的版本 {0} 高于安装包 {1}，跳过安装。" -f $installedVersion, $targetVersion) 'OK'
            $needInstall = $false
        } else {
            Write-Log ("将升级：{0} -> {1}" -f $installedVersion, $targetVersion)
        }
    }

    if (-not $needInstall) { return }

    Stop-RunningWidget
    Write-Log '正在安装 MSIX…'
    try {
        Add-AppxPackage -Path $MsixPath -ForceApplicationShutdown -ErrorAction Stop
        Write-Log '安装成功。' 'OK'
        return
    } catch {
        $message = $_.Exception.Message
        Write-Log ("安装失败：{0}" -f $message) 'ERROR'

        if ($message -match '0x80073CF9|0x80073CFF|0x80073CFB|旁加载|[Ss]ideload|AllowAllTrustedApps') {
            if (-not (Test-Admin)) {
                Invoke-Elevated '系统未允许旁加载，需要管理员权限开启后重试。'
            }
            Write-Log '尝试开启旁加载策略（AllowAllTrustedApps）…'
            New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Force | Out-Null
            Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
                -Name 'AllowAllTrustedApps' -Value 1 -Type DWord
            Add-AppxPackage -Path $MsixPath -ForceApplicationShutdown -ErrorAction Stop
            Write-Log '安装成功（已开启旁加载）。' 'OK'
            return
        }

        if ($message -match '0x800B0109|0x800B0100|0x80073CF0|0x80073CF3|证书|发布者|[Cc]ertificate|publisher|trust') {
            if (-not (Test-Admin)) {
                Invoke-Elevated '证书不受信任，需要管理员权限把证书装进本机存储后重试。'
            }
            Write-Log '重新安装证书到本机存储后重试…'
            Install-TrustedCert -CertPath $CertPath
            Add-AppxPackage -Path $MsixPath -ForceApplicationShutdown -ErrorAction Stop
            Write-Log '安装成功（证书已补装）。' 'OK'
            return
        }

        throw
    }
}

function Start-Widget {
    $package = Get-AppxPackage -Name $PkgName -ErrorAction SilentlyContinue
    if (-not $package) {
        Write-Log '未检测到已安装的小组件，无法启动。' 'WARN'
        return
    }
    $running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($running) {
        Write-Log ("小组件已在运行（PID {0}），无需重复启动。" -f ($running | Select-Object -First 1).Id) 'OK'
        return
    }
    $aumid = '{0}!App' -f $package.PackageFamilyName
    try {
        Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\' + $aumid) | Out-Null
    } catch {
        Start-Process ('shell:AppsFolder\' + $aumid) | Out-Null
    }
    Start-Sleep -Seconds 5
    $proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Log ("小组件已启动（PID {0}）。" -f ($proc | Select-Object -First 1).Id) 'OK'
    } else {
        Write-Log '已请求启动，但暂未检测到进程；可从开始菜单手动打开。' 'WARN'
    }
}

# ============================ 主流程 ============================

try {
    Write-Host ''
    Write-Host '  DeepSeek 峰谷计价小组件 · 一键安装' -ForegroundColor White
    Write-Host ('  日志：{0}' -f $LogFile) -ForegroundColor DarkGray
    Write-Host ''
    Write-Log ('开始执行：{0}' -f $PSCommandPath)

    Write-Title '检查系统环境'
    $osVersion = [Environment]::OSVersion.Version
    Write-Log ("Windows 版本：{0}（Build {1}）" -f $osVersion, $osVersion.Build)
    if ($osVersion.Build -lt 17763) {
        throw ('系统版本过低（需要 Windows 10 1809 / Build 17763 及以上，当前 Build {0}）。' -f $osVersion.Build)
    }
    $arch = $env:PROCESSOR_ARCHITECTURE
    if ($env:PROCESSOR_ARCHITEW6432) { $arch = $env:PROCESSOR_ARCHITEW6432 }
    Write-Log ("CPU 架构：{0}" -f $arch)
    if ($arch -ne 'AMD64') {
        Write-Log '本安装包为 x64 专用，当前架构可能不受支持。' 'WARN'
    }
    if (Test-Admin) {
        Write-Log '当前以管理员身份运行。' 'OK'
    } else {
        Write-Log '当前为普通用户权限（证书装入当前用户存储，通常已足够）。'
    }

    if ($Uninstall) {
        Write-Title '卸载小组件'
        $existing = Get-AppxPackage -Name $PkgName -ErrorAction SilentlyContinue
        if (-not $existing) {
            Write-Log '未检测到已安装的小组件，无需卸载。' 'OK'
            exit 0
        }
        Stop-RunningWidget
        Remove-AppxPackage -Package $existing.PackageFullName -ErrorAction Stop
        Write-Log ("已卸载 {0} {1}" -f $existing.Name, $existing.Version) 'OK'
        exit 0
    }

    Write-Title '定位安装包'
    $resolved = Resolve-Packages
    Write-Log ("MSIX：{0}" -f $resolved.Msix)
    if ($resolved.Cert) {
        Write-Log ("证书：{0}" -f $resolved.Cert)
    }

    Write-Title '安装签名证书'
    Install-TrustedCert -CertPath $resolved.Cert

    Write-Title '安装小组件'
    Install-Widget -MsixPath $resolved.Msix -CertPath $resolved.Cert

    $installedFinal = Get-AppxPackage -Name $PkgName -ErrorAction SilentlyContinue
    if (-not $installedFinal) {
        throw '安装流程结束，但未检测到已安装的包，请查看日志。'
    }
    Write-Log ("已安装版本：{0}" -f $installedFinal.Version) 'OK'

    if (-not $NoLaunch) {
        Write-Title '启动小组件'
        Start-Widget
    }

    Write-Title '完成'
    $configPath = Join-Path $env:LOCALAPPDATA 'DeepSeekPeakWidget\config.json'
    Write-Log ("配置文件：{0}" -f $configPath)
    Write-Log '提示：余额卡片需要在小程序内右键 →「个性化设置」填写自己的 DeepSeek API Key。'
    Write-Log '提示：开机自启可在右键菜单中勾选；卸载可再次运行本脚本并加 -Uninstall。'
    Write-Log '全部完成。' 'OK'
    exit 0
} catch {
    Write-Log ("安装失败：{0}" -f $_.Exception.Message) 'ERROR'
    Write-Log '排查建议：' 'WARN'
    Write-Log '  1) 右键本脚本 / cmd，选择“以管理员身份运行”后重试；'
    Write-Log '  2) Windows 设置 → 系统 → 开发者选项 → 打开“旁加载应用”；'
    Write-Log '  3) 若公司电脑有应用安装策略限制，需联系 IT 放行。'
    Write-Log ("完整日志：{0}" -f $LogFile) 'WARN'
    exit 1
}
