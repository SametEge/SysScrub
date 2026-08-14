<div align="center">

<img src="docs/assets/banner.png" alt="SysScrub" width="100%">

**Windows 维护、驱动更新与硬盘健康 — 一个应用全包。**

[![发布](https://img.shields.io/github/v/release/SametEge/SysScrub?include_prereleases&color=FF6B2C)](https://github.com/SametEge/SysScrub/releases/latest)
[![许可证](https://img.shields.io/badge/license-MIT-FF6B2C)](LICENSE)
[![构建](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml/badge.svg)](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml)

### [⬇ 下载最新版本](https://github.com/SametEge/SysScrub/releases/latest)

[English](README.md) · [Türkçe](README.tr.md) · [Deutsch](README.de.md) · [日本語](README.ja.md) · [한국어](README.ko.md)

</div>

---

<img src="docs/assets/screens/dashboard.png" alt="SysScrub 仪表板" width="100%">

## 它能做什么

把三个独立程序的活儿，装进一个真正设计过的界面。

| | |
|---|---|
| 🧹 **清理** | 按规则扫描 Windows、浏览器和应用留下的残余。删除的一切都进入隔离区，一键即可找回。 |
| 🗂 **注册表** | 十二个扫描器，专找目标已经消失的项。在删除任何东西之前，先做 `.reg` 备份并创建还原点。 |
| ⚙️ **驱动程序** | 识别你的硬件并找出过时驱动。更新来自 Windows 更新 — 经过 WHQL 签名，且由 Microsoft 针对该硬件放行。 |
| 📥 **更新** | 通过 winget 查找已安装程序的新版本。每个包都从其发行方自己的源下载。 |
| 🚀 **启动项** | 开机时运行的一切都在一个列表里。禁用使用 Windows 自己的机制，因此始终与任务管理器保持一致。 |
| 📦 **程序** | 批量卸载。结果以注册表项是否真的消失为准 — 退出码并不可靠。 |
| 💽 **硬盘健康** | 读取 S.M.A.R.T. 与 NVMe 健康数据：温度、通电时间、总写入量、剩余寿命。在原始数值旁边，还告诉你它意味着什么。 |
| 📊 **空间分析** | 是什么在吃你的空间？矩形树图、最大的文件，以及三阶段重复文件查找。 |
| 🕓 **时间线** | 应用对系统做过的每一次改动，汇成一条按时间排列的记录。可从任意节点撤销。 |

## 为什么还要再做一个清理工具

现有的每一个都会做些让人不快的事：注水的“已节省空间”数字、无法撤销的删除、臃肿的后台服务、
付费墙、遥测。

SysScrub 的立场是：

- **扫描永远不删除。** 每个模块先读取，把找到了什么、为什么这么判断摆给你看，然后等待。删什么由
  你决定。
- **没有不可逆的操作。** 清理、注册表、驱动、启动项 — 每一次改动都留在同一条时间线上，可以从那里
  回退。
- **数字是真的。** 收回的空间是在操作前后从硬盘上实测的，不是估算。没有编造的“系统快了 40%”。
- **不知道的时候就说不知道。** 读不到 S.M.A.R.T. 的硬盘不会从列表里消失，也不会拿到绿色徽章 —
  它会说明为什么读不到。
- **无账号、无广告、无遥测、无付费版。**

---

## 各个模块

### 清理

<img src="docs/assets/screens/cleaner.png" alt="清理" width="100%">

覆盖 Windows、浏览器、应用、游戏平台、开发工具和隐私痕迹的 48 条规则。每一条都写明它删除什么、
后果是什么 — 包括那些不太好听的部分（“删除 Windows.old 之后，就无法再回退到旧版 Windows”）。

规则是**数据，不是代码**：它们在 [`data/rules/*.json`](data/rules) 里。增加一个清理目标是添加一条
JSON，而不是改动程序。

在删除任何一个文件之前，它都要通过一次安全检查：受保护的 Windows 目录、你的文档、重解析点（联接和
符号链接从不跟随）以及云占位文件都会被拒绝。除纯临时文件夹之外的一切，都先进入隔离区。

### 注册表

<img src="docs/assets/screens/registry.png" alt="注册表清理" width="100%">

十二个扫描器：共享 DLL 计数、文件关联、ProgID 与 CLSID 项、COM 服务器、类型库、外壳扩展、卸载项、
应用程序路径、启动项、MUICache、安装程序文件夹与声音事件。

每条结果都显示完整的键路径**以及为什么判定它已失效**。删除前会为每个受影响的键写出 `.reg` 导出，
外加一个系统还原点。备份失败时，什么都不会被删除。

Windows 运行所需的键 — 服务、DriverStore、WinSxS、组件服务、.NET、Defender — 都在一份写死在代码里
的“绝不触碰”清单上。

### 驱动程序

<img src="docs/assets/screens/drivers.png" alt="驱动更新" width="100%">

通过 SetupAPI 建立硬件清单，再以 Windows 更新作为来源。列表分成两个诚实的类别：Windows 更新确实
提供了新版本的驱动，以及超过两年、但没有任何来源提供更新的驱动。后者标为“可能过时”—— 而不是
“过时”，因为我们并不知道。

在安装任何东西之前，所有第三方驱动都可以一键导出到备份文件夹。

### 硬盘健康

<img src="docs/assets/screens/disk-health.png" alt="硬盘健康" width="100%">

直接从硬盘读取 NVMe 健康日志（页 0x02）和 ATA S.M.A.R.T.。温度、通电时间、通电次数、总写入量、
剩余寿命、备用块、异常断电、无法纠正的错误 —— 每一项旁边都配有一句大白话的解读。

厂商专用属性的含义放在 [`data/smart-attributes.json`](data/smart-attributes.json) 里，所以支持一家
新厂商是加一行表格，而不是改代码。

### 空间分析

<img src="docs/assets/screens/disk-analysis.png" alt="空间分析" width="100%">

整块硬盘的 squarified 矩形树图、最大的文件，以及按文件类型的分布。只读：不删除文件，甚至不打开它们。
云文件不会被下载 —— 既然它们在硬盘上不占空间，也就不计入统计。读不到的文件夹不会被悄悄跳过，而是
计数并报告出来。

重复文件查找分三个阶段比较 —— 大小，然后是首尾各 4 KB，最后是完整的 SHA-256 —— 因此只对必须处理的
部分计算哈希。

### 启动项与程序

<img src="docs/assets/screens/startup.png" alt="启动项管理" width="100%">

Run 与 RunOnce 键（两个注册表视图）、启动文件夹、由登录触发的计划任务，以及非 Microsoft 的自动启动
服务。禁用会写入任务管理器所用的同一个 `StartupApproved` 存储，因此两者永不矛盾。开机延迟不是猜的，
而是从 Windows 的 Diagnostics-Performance 事件日志中读出的实测值。

卸载会运行每个程序自带的卸载程序，然后以注册表项是否真的消失来核实结果。

### 时间线

<img src="docs/assets/screens/timeline.png" alt="时间线" width="100%">

每次运行都会被记录：删了什么、多少字节、依据哪条规则、能否撤销。进入隔离区的清理一键即可还原。

---

## 安装

**安装程序：** 从 [Releases](https://github.com/SametEge/SysScrub/releases/latest) 取得
`SysScrub-Setup-*.exe` 并运行。

**便携版：** 解压 `SysScrub-*-portable-x64.zip` 直接运行 —— 无需安装。在可执行文件旁放一个空的
`portable.flag` 文件，应用就会把所有设置和日志保存在自己的文件夹里，不向系统写入任何东西（从 U 盘
使用时很方便）。

> **SmartScreen 警告：** 应用没有用代码签名证书签名，因此 Windows 会显示“未知发布者”的警告。选择
> *更多信息 → 仍要运行*。如果你更愿意自己构建，源码全部在这里。

**运行要求：** Windows 10 1809 或更新版本（64 位）。若缺少 .NET 8 桌面运行时，安装程序会提示获取；
self-contained 的便携版没有任何前置要求。

应用以管理员身份运行 —— 否则无法处理 Windows 更新缓存、停止服务和读取 S.M.A.R.T.。

**更新**每天对照本仓库的发布检查一次，可在设置界面安装。下载的安装包会与随发布一同公开的
`SHA256SUMS.txt` 校验；哈希不符时文件会被删除，什么都不会运行。这项检查只读取一个版本号，不发送
任何内容 —— 也可以关闭。

## 语言

界面提供**土耳其语、英语、德语、日语、韩语和简体中文**，包含全部 48 条清理规则的说明。首次运行时会
按你的 Windows 设置选择语言；可随时更改，无需重启即刻生效。

语言包是 [`data/i18n/`](data/i18n) 下的纯 JSON —— 为某种语言做贡献就是发送一个文件。德语、日语、
韩语和中文的翻译仍在等待母语者审校。

## 状态

正在积极开发中，当前为 `0.14.0-alpha`。九个模块已能工作并读取真实的系统数据。还缺什么，都诚实地列
在 [docs/ROADMAP.md](docs/ROADMAP.md) 里：

| 已完成 | 尚未完成 |
|---|---|
| 清理 · 注册表 · 驱动程序 · 软件更新 | 后台模式与通知区域 |
| 启动项 · 程序 · 硬盘健康 · 空间分析 | 命令面板（Ctrl+K） |
| 时间线 · 六种语言 · 自动更新 | Windows 更新以外的驱动来源 |

## 从源码构建

```powershell
git clone https://github.com/SametEge/SysScrub.git
cd SysScrub
dotnet build
dotnet run --project src/SysScrub.App
```

生成 `dist/` 和安装程序：

```powershell
./build/publish.ps1 -SelfContained
```

安装程序这一步需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)
（`winget install JRSoftware.InnoSetup`）。没有它则跳过该步骤，便携版产物照常生成。

## 目录结构

```
src/SysScrub.Core    引擎 —— 扫描、安全检查、驱动与磁盘层，零 UI 依赖
src/SysScrub.App     WPF 界面、设计系统、本地化
src/SysScrub.Cli     计划任务/静默清理与技术员报告
tests/               496 项测试：安全检查、规则引擎、S.M.A.R.T. 解析、语言包
data/rules           清理规则（JSON）
data/i18n            界面翻译（JSON）
build/               发布脚本与版本号
installer/           Inno Setup 脚本与向导图片
```

## 参与贡献

欢迎通过 [issue](https://github.com/SametEge/SysScrub/issues) 反馈问题和提出建议。

有两件事完全不需要写 C#：

- **一条清理规则** —— 在 [`data/rules/`](data/rules) 添加一个 JSON 条目
- **一份翻译** —— 编辑 [`data/i18n/`](data/i18n) 中的一个文件

## 许可证

[MIT](LICENSE) · 第三方声明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
