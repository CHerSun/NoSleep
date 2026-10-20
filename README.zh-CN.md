> 🌐 语言:[English](README.md) | [Русский](README.ru.md) | [简体中文](README.zh-CN.md)

# NoSleep

NoSleep 是一款轻量级工具,用于防止 Windows 自动激活屏幕保护程序、睡眠模式或锁屏。它专为那些你无法自行更改这些设置的场景而设计——例如,因公司强制策略限制而无法修改时。灵感来自 Linux 的 Caffeine。

> **注意:** Windows PowerToys 包含一款名为 Awake 的工具,功能相同,但 PowerToys 可能需要管理员权限,且体积较大。NoSleep 致力于尽可能精简,运行时无需任何额外权限。

## 安装

NoSleep 可通过 [Scoop](https://scoop.sh/) 包管理器安装:

```sh
scoop bucket add extras
scoop install extras/nosleep
```

你也可以从[发布页面](https://github.com/CHerSun/NoSleep/releases/latest)手动下载最新版本。发布包含三个可执行文件:

- **`NoSleep.exe`** — 面向所有用户的默认版本(基于 .NET Framework 4.8 运行,Windows 已预装)。
- **`NoSleep-net8.exe`** / **`NoSleep-net10.exe`** — 面向已安装 .NET 8 或 .NET 10 运行时的单文件版本。

## 使用方法

NoSleep 采用"设置后即忘"的设计。一旦运行,它会驻留在 Windows 系统托盘中,防止你的电脑进入睡眠状态。

- **左键单击**托盘图标可切换启用或禁用状态。图标会随之变化以反映当前状态。
- **右键单击**托盘图标可打开菜单,包含以下附加选项:

  - **登录时自动启动** – 在你登录时自动启动 NoSleep。
  - **保持屏幕常亮** – 在 NoSleep 启用时,防止显示器关闭。
  - **记住启用状态** – 在应用程序重启后保存启用状态。
  - **配置要监视的应用程序** – 定义一个应用程序列表。如果这些应用都没有运行,NoSleep 会自动禁用自身;如果任意一个被监视的应用正在运行,NoSleep 会启用自身并阻止睡眠。此动态行为仅在用户启用 NoSleep 时生效。(自 v1.4.0 起可用)

要完全停止 NoSleep,请右键单击托盘图标并选择 **关闭**。

### 语言

界面会自动跟随 Windows 显示语言,在 **English、Русский 和 简体中文** 之间选择。如需显式指定语言,可使用 `--lang` 参数启动:

```sh
NoSleep.exe --lang zh-CN     # 本次运行使用简体中文
NoSleep.exe --lang ru        # 俄语
NoSleep.exe --lang list      # 显示可用语言
```

要固定语言选择,请将参数添加到 NoSleep 快捷方式的目标中。

### 行为矩阵

| 启用 | 保持屏幕常亮 | 系统行为                          | 显示器行为       |
| ------- | -------------- | ---------------------------------------- | ---------------- |
| ✅ 开   | ✅ 开          | 阻止睡眠                       | 始终常亮        |
| ✅ 开   | ⬜ 关         | 阻止睡眠                       | 可关闭     |
| ⬜ 关  | 任意            | 正常系统行为(可能进入睡眠) | 可关闭     |

## 系统要求

- **`NoSleep.exe`**(默认版本):.NET Framework 4.8 或更高版本——通常在 Windows 10 及更高版本中已预装。如需安装,可从 [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet-framework) 下载。
- **`NoSleep-net8.exe`** / **`NoSleep-net10.exe`**:需要安装对应的 .NET 运行时。

## 工作原理

NoSleep 每 10 秒调用一次 `SetThreadExecutionState` 函数,重置 Windows 的显示器和空闲计时器。此过程 CPU 占用极低,仅消耗少量内存。编译后的二进制文件包含图标(约 180 KB)和核心代码。

## 图标归属

| 用途          | 图标及来源                                                                                      | 许可证                  | 作者                                                                      |
| -------------- | ---------------------------------------------------------------------------------------------- | ------------------------ | --------------------------------------------------------------------------- |
| 启用状态  | [Coffee icon](https://www.iconarchive.com/show/food-icons-by-martin-berube/coffee-icon.html)   | 免费软件                 | [Martin Berube](https://www.iconarchive.com/artist/martin-berube.html)      |
| 禁用状态 | [Sleep icon](https://www.iconarchive.com/show/material-icons-by-pictogrammers/sleep-icon.html) | Apache 2.0(开源) | [Pictogrammers Team](https://www.iconarchive.com/artist/pictogrammers.html) |

衷心感谢这些图标的作者!
