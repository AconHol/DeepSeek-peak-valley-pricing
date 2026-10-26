<#
    抓取 DeepSeek 官方定价页，解析模型与峰谷单价并保存结构化结果。

    用途：官方调价（如 2026-09-10 12:00 起 flash 系列降价）后自动取得准确价格，
          供小组件更新价格表使用。

    示例：
      # 立即抓取一次（用于自检）
      powershell -ExecutionPolicy Bypass -File tools\capture-deepseek-pricing.ps1 -NoWait

      # 等到今天 12:00 开始，每 60 秒抓一次，直到页面价格相对首次抓取发生变化
      powershell -ExecutionPolicy Bypass -File tools\capture-deepseek-pricing.ps1 -WaitUntil 12:00 -Deadline 12:45

    输出（默认写入 CodexOutput\DeepSeek峰谷小组件-WinUI\pricing-captures）：
      latest.json         最近一次解析结果
      latest-page.txt     最近一次页面纯文本（便于人工核对）
      page-<时间戳>.html  原始页面存档
      changed-<时间戳>.json  检测到价格变化时的结果
      capture.log         运行日志
#>
param(
    [string]$OutDir = 'C:\Users\75366\CodexOutput\DeepSeek峰谷小组件-WinUI\pricing-captures',
    [string]$WaitUntil = '',
    [string]$Deadline = '',
    [string]$BaselineFile = '',
    [int]$PollSeconds = 60,
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$url = 'https://api-docs.deepseek.com/zh-cn/quick_start/pricing'
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$logPath = Join-Path $OutDir 'capture.log'

function Write-Log([string]$message) {
    $line = '[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $message
    Write-Host $line
    try { Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8 } catch { }
}

function Get-PageContent {
    $resp = Invoke-WebRequest -Uri ('{0}?ts={1}' -f $url, (Get-Random)) -TimeoutSec 30 -UseBasicParsing -Headers @{
        'Cache-Control' = 'no-cache'
        'Pragma'        = 'no-cache'
        'User-Agent'    = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'
    }
    # 响应头未声明 charset 时 PowerShell 5.1 会按 Latin-1 解码导致中文乱码，
    # 这里显式用 UTF-8 解码原始字节流。
    $html = ''
    try {
        $stream = $resp.RawContentStream
        if ($stream) {
            $stream.Position = 0
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            $html = $reader.ReadToEnd()
            $reader.Dispose()
        }
    } catch { }
    if ([string]::IsNullOrWhiteSpace($html)) { $html = $resp.Content }
    $text = $html -replace '(?s)<script.*?</script>', ' ' `
                  -replace '(?s)<style.*?</style>', ' ' `
                  -replace '<[^>]+>', ' ' `
                  -replace '&nbsp;', ' ' `
                  -replace '&#x27;', "'" `
                  -replace '\s+', ' '
    return [pscustomobject]@{ Html = $html; Text = $text }
}

function Get-Segment([string]$text, [string]$from, [string]$to) {
    $i = $text.IndexOf($from)
    if ($i -lt 0) { return '' }
    $j = $text.IndexOf($to, $i + $from.Length)
    if ($j -lt 0) { $j = [Math]::Min($text.Length, $i + 500) }
    return $text.Substring($i, $j - $i)
}

function Get-Models([string]$text) {
    # 表头形如：模型 deepseek-flash (1) deepseek-v4-pro BASE URL ...
    # 模型数量随官方表格变化（2026-10 为 flash / v4-pro 两列），这里按出现顺序全量提取。
    $seg = Get-Segment $text '模型 ' 'BASE URL'
    $names = @([regex]::Matches($seg, 'deepseek[\w\.\-]+') | ForEach-Object { $_.Value })
    return $names
}

function Get-ModelVersions([string]$text) {
    # 形如：模型版本 DeepSeek-V4.1-Flash-1017 DeepSeek-V4-Pro-0813
    $seg = Get-Segment $text '模型版本' '思考模式'
    $versions = @([regex]::Matches($seg, 'DeepSeek[\w\.\-]+') | ForEach-Object { $_.Value })
    return $versions
}

function Get-PriceRow([string]$text, [string]$label) {
    # 形如：<label> 空闲时段 0.02元 0.15元 高峰时段 0.04元 0.30元
    # 每档报价的数量=模型数量（2 个或 3 个），因此按“数字+元”的连续片段整体提取。
    $pattern = [regex]::Escape($label) +
               '\s*空闲时段\s*((?:[\d.]+\s*元\s*)+)' +
               '\s*高峰时段\s*((?:[\d.]+\s*元\s*)+)'
    $m = [regex]::Match($text, $pattern)
    if (-not $m.Success) { return $null }
    $valley = @([regex]::Matches($m.Groups[1].Value, '[\d.]+') | ForEach-Object { [double]$_.Value })
    $peak = @([regex]::Matches($m.Groups[2].Value, '[\d.]+') | ForEach-Object { [double]$_.Value })
    if ($valley.Count -eq 0 -or $valley.Count -ne $peak.Count) { return $null }
    return [pscustomobject]@{ Valley = $valley; Peak = $peak }
}

function Format-PriceKey($models, $prices) {
    if (-not $prices -or -not $prices.Hit -or -not $prices.Miss -or -not $prices.Out) { return '<解析失败>' }
    $n = $prices.Hit.Valley.Count
    $items = @()
    for ($i = 0; $i -lt $n; $i++) {
        $name = if ($i -lt $models.Count) { $models[$i] } else { "模型$($i + 1)" }
        $items += ('{0} 谷 {1}/{2}/{3} 峰 {4}/{5}/{6}' -f `
            $name, `
            $prices.Hit.Valley[$i], $prices.Miss.Valley[$i], $prices.Out.Valley[$i], `
            $prices.Hit.Peak[$i], $prices.Miss.Peak[$i], $prices.Out.Peak[$i])
    }
    return ($items -join '；')
}

function Test-ModelMatch($prices, [int]$index, $baseline) {
    if (-not $baseline -or -not $prices -or $prices.Hit.Valley.Count -le $index) { return $null }
    if ($null -eq $baseline.hitValley) { return $null }
    return (
        [double]$baseline.hitValley -eq $prices.Hit.Valley[$index] -and
        [double]$baseline.hitPeak -eq $prices.Hit.Peak[$index] -and
        [double]$baseline.inputValley -eq $prices.Miss.Valley[$index] -and
        [double]$baseline.inputPeak -eq $prices.Miss.Peak[$index] -and
        [double]$baseline.outputValley -eq $prices.Out.Valley[$index] -and
        [double]$baseline.outputPeak -eq $prices.Out.Peak[$index]
    )
}

# ---- 等待到指定时刻 ----
if (-not $NoWait -and $WaitUntil) {
    $t = [datetime]::ParseExact($WaitUntil, 'HH:mm', $null)
    $start = (Get-Date).Date.AddHours($t.Hour).AddMinutes($t.Minute)
    if ($start -gt (Get-Date)) {
        Write-Log ('等待至 {0:yyyy-MM-dd HH:mm} 开始抓取（还需 {1:N1} 分钟）' -f $start, ($start - (Get-Date)).TotalMinutes)
        Start-Sleep -Seconds ([int]($start - (Get-Date)).TotalSeconds)
    }
}

$deadlineTime = $null
if ($Deadline) {
    $d = [datetime]::ParseExact($Deadline, 'HH:mm', $null)
    $deadlineTime = (Get-Date).Date.AddHours($d.Hour).AddMinutes($d.Minute)
    Write-Log ('截止时间：{0:HH:mm}' -f $deadlineTime)
}

$baselineFlash = $null
$baselinePro = $null
if ($BaselineFile -and (Test-Path -LiteralPath $BaselineFile)) {
    try {
        $cfg = Get-Content -Raw -LiteralPath $BaselineFile | ConvertFrom-Json
        $baselineFlash = $cfg.flash
        $baselinePro = $cfg.pro
        Write-Log ('程序当前 flash 价格（基线）：谷 {0}/{1}/{2}，峰 {3}/{4}/{5}' -f `
            $baselineFlash.hitValley, $baselineFlash.inputValley, $baselineFlash.outputValley, `
            $baselineFlash.hitPeak, $baselineFlash.inputPeak, $baselineFlash.outputPeak)
        if ($baselinePro) {
            Write-Log ('程序当前 pro 价格（基线）：谷 {0}/{1}/{2}，峰 {3}/{4}/{5}' -f `
                $baselinePro.hitValley, $baselinePro.inputValley, $baselinePro.outputValley, `
                $baselinePro.hitPeak, $baselinePro.inputPeak, $baselinePro.outputPeak)
        }
    } catch { Write-Log ('读取基线失败：{0}' -f $_.Exception.Message) }
}

$prevKey = $null
$changed = $false
$attempt = 0

while ($true) {
    $attempt++
    Write-Log ('第 {0} 次抓取 {1}' -f $attempt, $url)

    $page = $null
    try {
        $page = Get-PageContent
    } catch {
        Write-Log ('抓取失败：{0}' -f $_.Exception.Message)
    }

    if ($page) {
        $prices = [pscustomobject]@{
            Hit  = Get-PriceRow $page.Text '（缓存命中）'
            Miss = Get-PriceRow $page.Text '（缓存未命中）'
            Out  = Get-PriceRow $page.Text '百万tokens输出'
        }
        $models = @(Get-Models $page.Text)
        $versions = @(Get-ModelVersions $page.Text)
        $key = Format-PriceKey $models $prices
        Write-Log ('解析状态：模型 {0} 个；命中输入 {1}；未命中输入 {2}；输出 {3}；页面文本 {4} 字符' -f `
            $models.Count, ($null -ne $prices.Hit), ($null -ne $prices.Miss), ($null -ne $prices.Out), $page.Text.Length)

        # 官方表格的列顺序可能变化，按模型名定位 flash / pro 在列中的下标
        $flashIndex = 0
        $proIndex = -1
        for ($i = 0; $i -lt $models.Count; $i++) {
            if ($models[$i] -match 'pro') { $proIndex = $i }
        }
        if ($proIndex -eq 0) { $flashIndex = 1 }

        $targetMatch = $null
        $proMatch = $null
        if ($prices.Hit -and $prices.Miss -and $prices.Out) {
            $targetMatch = Test-ModelMatch $prices $flashIndex $baselineFlash
            if ($proIndex -ge 0) { $proMatch = Test-ModelMatch $prices $proIndex $baselinePro }
        }

        $result = [pscustomobject]@{
            capturedAt  = (Get-Date).ToString('s')
            sourceUrl   = $url
            models      = $models
            modelVersions = $versions
            hitInput    = $prices.Hit
            missInput   = $prices.Miss
            output      = $prices.Out
            summary     = $key
            matchesProgramPrice = $targetMatch
            matchesProPrice = $proMatch
            textLength  = $page.Text.Length
        }

        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'latest.json') -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $OutDir 'latest-page.txt') -Value $page.Text -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $OutDir ('page-{0}.html' -f $stamp)) -Value $page.Html -Encoding UTF8

        if (-not $prices.Hit -or -not $prices.Miss -or -not $prices.Out) {
            Write-Log '解析失败：未匹配到价格行（页面结构可能已变化），原始页面已存档'
            $i = $page.Text.IndexOf('缓存命中')
            if ($i -ge 0) {
                $from = [math]::Max(0, $i - 40)
                Write-Log ('页面片段：' + $page.Text.Substring($from, [math]::Min(260, $page.Text.Length - $from)))
            } else {
                Write-Log '页面文本中未找到“缓存命中”，可能页面结构已大幅变化'
            }
        } else {
            Write-Log ('模型：{0}' -f ($models -join ', '))
            if ($versions.Count -gt 0) { Write-Log ('模型版本：{0}' -f ($versions -join ', ')) }
            Write-Log ('价格（谷价/峰价）：{0}' -f $key)
            if ($null -ne $targetMatch) { Write-Log ('flash 是否与程序当前价格一致：{0}' -f $targetMatch) }
            if ($null -ne $proMatch) { Write-Log ('pro 是否与程序当前价格一致：{0}' -f $proMatch) }

            if ($null -eq $prevKey) {
                $prevKey = $key
                Write-Log '已记录基线价格，开始等待变化…'
                if ($NoWait) { break }
            } elseif ($key -ne $prevKey) {
                $changedFile = Join-Path $OutDir ('changed-{0}.json' -f $stamp)
                $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $changedFile -Encoding UTF8
                Write-Log ('检测到价格变化：{0} → {1}，结果存于 {2}' -f $prevKey, $key, $changedFile)
                $changed = $true
                break
            } else {
                Write-Log '价格未变化，继续等待…'
            }
        }

        # 单次抓取（-NoWait）无论解析成功与否都只跑一轮，避免无截止时间时无限轮询
        if ($NoWait) { break }
    }

    if ($deadlineTime -and (Get-Date) -gt $deadlineTime) {
        Write-Log '已到截止时间，停止抓取'
        break
    }
    Start-Sleep -Seconds $PollSeconds
}

Write-Log ('抓取结束，是否检测到价格变化：{0}' -f $changed)
if ($changed) { exit 0 } else { exit 2 }
