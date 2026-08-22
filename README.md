# DeepSeek 峰谷计价小组件（WinUI 3 版）

使用WINUI3技术栈编写的 DeepSeek API 峰谷计价提醒小组件。

[![Built with Codex](https://img.shields.io/badge/Built%20with-Codex-000000?style=flat-square&logo=openai&logoColor=white)](https://openai.com/codex)

## 安装

- 一键安装：使用 release 中的 `DeepSeek峰谷小组件-WinUI-安装程序.exe`，双击即可自动完成证书信任 + 安装/升级 + 启动（无需管理员，首次需系统允许旁加载）。
- 手动安装：解压 `DeepSeek峰谷小组件-WinUI-<版本>-安装包(发给其他人).zip`，运行 `Add-AppDevPackage.ps1` 自动信任证书并安装。

## 峰谷规则（官方公告，2026-08-23 起生效）

- 峰时（全价）：北京时间每天 `09:00-12:00`、`14:00-18:00`
- 谷时（半价）：其余 17 个小时
- 谷时价格为高峰时段价格的一半
- **周六、周日全天按谷价计费**（DeepSeek 官方 2026-08-23 起不再区分周末峰谷）

> 小组件内置“每周规则”：可在设置中分别勾选周一～周日是否全天按谷时，默认周六/周日全天谷（对应官方新规），可随时改回任意自定义组合。

## 功能

- 当前时段大字状态（峰时 · 全价 / 谷时 · 半价）+ 距下次切换倒计时 + 当前段进度条
- 24 小时峰谷时段图（当前小时高亮）
- 接下来 3 次切换节点预告
- V4 Flash / V4 Pro 峰谷实时价格表（元 / 百万 tokens）
- DeepSeek 账户余额卡片（设置中填写 API Key 后，以卡片形式显示余额、可用状态与充值/赠送明细；刷新间隔可按秒配置，0=关闭，右键可立即刷新）
- 每周规则（周一～周日可分别设置“全天按谷时”，默认周六/周日谷）
- 时段切换系统通知 + 提前 N 分钟提醒（默认提前 10 分钟）
- 简略版 / 完整版、置顶、图钉锁定、深色/浅色/跟随系统、亚克力背景、5 套主题色
- 窗口位置记忆、开机自启（StartupTask）

## 构建

```powershell
dotnet build src/DeepSeekPeakWidget/DeepSeekPeakWidget.csproj -c Release
```

产物为自包含（Self-Contained）模式，无需预装 Windows App SDK 运行时：
`src/DeepSeekPeakWidget/bin/Release/net9.0-windows10.0.19041.0/win-x64/DeepSeekPeakWidget.exe`

## 打包（MSIX + 签名）

使用自签名开发者证书 `CN=PVEWidget Dev`（与 PVE 小组件同一张证书）：

```powershell
msbuild src/DeepSeekPeakWidget/DeepSeekPeakWidget.csproj -t:Restore;Rebuild -p:Configuration=Release -p:Platform=x64 -p:AppxPackageSigningEnabled=true -p:PackageCertificateKeyFile="C:\Users\75366\CodexOutput\PVE监视小组件-WinUI\signing\pve-widget-build.pfx" -p:PackageCertificatePassword=pvebuild123 -p:AppxPackageDir=release\
```

## 配置文件

配置写入真实 `%LOCALAPPDATA%\DeepSeekPeakWidget\config.json`（打包应用 exe 目录只读）。
主要字段：窗口行为、`apiKey`（可选，用于查询 DeepSeek 账户余额）、时区偏移、两个峰时段、`weekValleyDays` 每周规则（周一～周日是否全天谷时）、提醒、V4 Flash / V4 Pro 峰谷单价。

## 注意

- 亚克力模糊需要系统“设置 → 个性化 → 颜色 → 透明效果”处于开启状态。
- 首次安装需信任证书 `pve-widget-cert.cer`（或直接使用 release 中的“安装包(发给其他人).zip”的 `Add-AppDevPackage.ps1` 自动信任+安装）。
