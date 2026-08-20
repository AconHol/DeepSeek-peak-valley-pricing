# DeepSeek 峰谷计价小组件（WinUI 3 版）

使用WINUI3技术栈编写的 DeepSeek API 峰谷计价提醒小组件。

## 峰谷规则（官方公告，2026-08-17 起生效）

- 峰时（全价）：北京时间每天 `09:00-12:00`、`14:00-18:00`
- 谷时（半价）：其余 17 个小时
- 谷时价格为高峰时段价格的一半

> 部分媒体称“周末及节假日为空闲时段”，官方口径未单独豁免周末；默认按每天固定峰谷计算，可在设置中勾选“周末及节假日全天按谷时计算”。

## 功能

- 当前时段大字状态（峰时 · 全价 / 谷时 · 半价）+ 距下次切换倒计时 + 当前段进度条
- 24 小时峰谷时段图（当前小时高亮）
- 接下来 3 次切换节点预告
- V4 Flash / V4 Pro 峰谷实时价格表（元 / 百万 tokens）
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
主要字段：窗口行为、时区偏移、两个峰时段、周末全天谷时、提醒、V4 Flash / V4 Pro 峰谷单价。

## 注意

- 亚克力模糊需要系统“设置 → 个性化 → 颜色 → 透明效果”处于开启状态。
- 首次安装需信任证书 `pve-widget-cert.cer`（或直接使用 release 中的“安装包(发给其他人).zip”的 `Add-AppDevPackage.ps1` 自动信任+安装）。
