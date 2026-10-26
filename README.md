# DeepSeek 峰谷计价小组件（WinUI 3 版）

使用WINUI3技术栈编写的 DeepSeek API 峰谷计价提醒小组件。

[![Built with Codex](https://img.shields.io/badge/Built%20with-Codex-000000?style=flat-square&logo=openai&logoColor=white)](https://openai.com/codex)

## 安装

- **绿色版（免安装，推荐给不想安装的人）**：解压 `DeepSeekPeakWidget-portable-<版本>-win-x64.zip`，双击 `DeepSeekPeakWidget.exe` 即可运行。免安装、免证书、免管理员，也无需另装 .NET / Windows App SDK 运行库；开机自启用当前用户注册表 `Run` 项实现。
- 一键安装：使用 release 中的 `DeepSeek峰谷小组件-WinUI-安装程序.exe`，双击即可自动完成证书信任 + 安装/升级 + 启动（无需管理员，首次需系统允许旁加载）。
- 一键脚本（装证书 + 装/升级 MSIX + 启动，推荐发给别人）：把 `DeepSeekWidget-Setup.cmd`（或中文名 `双击安装.cmd`）与 `.msix`、`.cer` 放在同一文件夹，双击即可；
  - 目录里没有安装包时（例如只把脚本发给别人），脚本会**自动从 GitHub 最新 Release 下载** MSIX 与证书再安装——「只发一个脚本」也能装；
  - `-Offline` 关闭联网下载；`-Uninstall` 卸载；`-Reinstall` 同版本修复重装；`-NoLaunch` 装完不自动启动；
  - `-MachineWide` 把证书装进本机存储（需要管理员，所有用户可用）；遇到证书/旁加载错误时脚本会自动提权重试。
- 手动安装：解压 `DeepSeek峰谷小组件-WinUI-<版本>-安装包(发给其他人).zip`，双击 `双击安装.cmd`（或右键 `Add-AppDevPackage.ps1` → 使用 PowerShell 运行），自动信任证书并安装。
- 发给别人：在 GitHub Releases 页下载 `DeepSeekPeakWidget-portable-<版本>-win-x64.zip`（免安装，最省事）或 `DeepSeek峰谷小组件-WinUI-安装程序.exe`；对方电脑需 Windows 10/11 x64。

## 峰谷规则（官方公告，2026-10-26 定价页核对）

- 峰时（全价）：北京时间周一至周五（**不含中国法定节假日**）`09:00-12:00`、`14:00-18:00`
- 谷时（半价）：其余时段（含周末与法定节假日全天），价格为高峰时段的一半
- **周六、周日及法定节假日全天按谷价计费**；调休上班的周六/周日仍按周末处理

> 小组件内置“每周规则”：可在设置中分别勾选周一～周日是否全天按谷时，默认周六/周日全天谷；法定节假日清单会**自动联网更新**（timor.tech 优先、holiday-cn 兜底，断网时用内置清单），也可在设置中手动编辑、立即更新或关闭自动更新。

## 功能

- 当前时段大字状态（峰时 · 全价 / 谷时 · 半价）+ 距下次切换倒计时 + 当前段进度条
- 24 小时峰谷时段图（当前小时高亮），悬停可看节假日数据来源与更新时间
- 接下来 3 次切换节点预告
- V4.1 Flash / V4 Pro 峰谷实时价格表（元 / 百万 tokens，含官方核对日期与搜索/抓取 API 计费提示）
- DeepSeek 账户余额卡片（设置中填写 API Key 后，以卡片形式显示余额、可用状态与充值/赠送明细；刷新间隔可按秒配置，0=关闭，右键可立即刷新）
- 每周规则（周一～周日可分别设置“全天按谷时”，默认周六/周日谷）＋法定节假日全天谷时（自动联网更新，可编辑）
- 时段切换系统通知 + 提前 N 分钟提醒（默认提前 10 分钟）
- 简略版 / 完整版、置顶、图钉锁定、深色/浅色/跟随系统、亚克力背景、5 套主题色
- 窗口位置记忆、开机自启（安装版用 StartupTask，绿色版用注册表 Run 项）

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

## 打包（绿色版，免安装）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer\build-portable.ps1
```

产物：`release\DeepSeekPeakWidget-portable-<版本>-win-x64.zip`，解压双击 `DeepSeekPeakWidget.exe` 即用。
注意绿色版必须取 **MSIX 构建输出目录**（`bin\x64\Release\...\win-x64`，含 `resources.pri`）；
`dotnet publish -p:WindowsPackageType=None` 的产物缺少 `resources.pri`，启动会立即退出。

## 配置文件

配置写入真实 `%LOCALAPPDATA%\DeepSeekPeakWidget\config.json`（打包应用 exe 目录只读，绿色版共用同一份）。
主要字段：窗口行为、`apiKey`（可选，用于查询 DeepSeek 账户余额）、时区偏移、两个峰时段、`weekValleyDays` 每周规则（周一～周日是否全天谷时）、`holidays` 法定节假日（yyyy-MM-dd，全天谷时）、`holidayAutoUpdate` 自动联网更新开关、`holidayLastSync` / `holidaySource` 上次更新时间与来源、提醒、V4.1 Flash / V4 Pro 峰谷单价、`priceRevision`（官方调价/规则变化后自动更新内置单价的版本标记）。

## 注意

- 亚克力模糊需要系统“设置 → 个性化 → 颜色 → 透明效果”处于开启状态。
- 首次安装需信任证书 `pve-widget-cert.cer`（或直接使用 release 中的“安装包(发给其他人).zip”的 `Add-AppDevPackage.ps1` 自动信任+安装）。
