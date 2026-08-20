# DeepSeek 峰谷计价小组件 安装脚本（由自解压安装器调用，双击运行）
# 自动完成：结束旧进程 -> 信任证书 -> 安装/升级 MSIX -> 启动小组件
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$msix = Join-Path $root 'DeepSeekPeakWidget.msix'
$cer = Join-Path $root 'pve-widget-cert.cer'
$log = Join-Path $env:TEMP 'DeepSeekPeakWidget-install.log'

function Log($msg) {
    try { Add-Content -LiteralPath $log -Value ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $msg) } catch { }
    Write-Host $msg
}

Set-Content -LiteralPath $log -Value ("=== DeepSeekPeakWidget install $(Get-Date) ===")
Log '开始安装 DeepSeek 峰谷计价小组件...'

# 1) 结束正在运行的小组件
Get-Process -Name 'DeepSeekPeakWidget' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 2) 信任签名证书（当前用户，无需管理员）
if (Test-Path -LiteralPath $cer) {
    try {
        Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\CurrentUser\Root' -ErrorAction SilentlyContinue | Out-Null
        Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher' -ErrorAction SilentlyContinue | Out-Null
        Log '证书已信任。'
    } catch {
        Log "证书信任失败（继续尝试安装）：$($_.Exception.Message)"
    }
}

# 3) 安装 / 升级 MSIX
if (-not (Test-Path -LiteralPath $msix)) {
    Log "错误：未找到安装包 $msix"
    Start-Sleep -Seconds 5
    exit 1
}

$targetVer = [Version]'__VERSION__'
$installed = Get-AppxPackage -Name 'DeepSeekPeak.Widget' -ErrorAction SilentlyContinue
$needInstall = $true
if ($installed) {
    try { $needInstall = ([Version]$installed.Version.ToString()) -lt $targetVer } catch { $needInstall = $true }
}

if (-not $needInstall) {
    Log "已安装 $($installed.Version)，无需重新安装。"
} else {
    try {
        Add-AppxPackage -Path $msix -ErrorAction Stop
        Log '安装/升级成功。'
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match '0x80073CF9|AllowAllTrustedApps|sideloading|旁加载') {
            Log '检测到系统未开启旁加载，尝试自动开启（需要管理员权限）...'
            try {
                New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Force | Out-Null
                Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name 'AllowAllTrustedApps' -Value 1 -Type DWord
                Add-AppxPackage -Path $msix -ErrorAction Stop
                Log '安装成功。'
            } catch {
                Log "安装失败：$msg"
                Log '请右键本安装程序选择“以管理员身份运行”后重试。'
                Start-Sleep -Seconds 8
                exit 1
            }
        } else {
            Log "安装失败：$msg"
            Start-Sleep -Seconds 8
            exit 1
        }
    }
}

# 4) 启动小组件
Start-Process explorer.exe -ArgumentList 'shell:AppsFolder\DeepSeekPeak.Widget_1vyzvtm0mxts6!App' | Out-Null
Log '安装完成，小组件已启动。'
Start-Sleep -Seconds 4
