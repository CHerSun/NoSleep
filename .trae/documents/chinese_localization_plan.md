# NoSleep 中文本地化(汉化)方案

## 概述

采用 .NET 标准 i18n 机制(.resx 卫星程序集)为 NoSleep 添加简体中文(`zh-CN`)本地化支持。英文作为默认/中性语言保留,运行时根据系统 `CurrentUICulture` 自动选择语言;同时翻译全部项目文档。

## 当前状态分析

### 国际化现状
- **无任何 i18n 支持**:所有用户可见字符串均为英文硬编码在源码中
- [Properties/Resources.resx](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/Properties/Resources.resx) 仅含 2 个图标(`TrayIcon`、`TrayIconInactive`),无字符串资源
- [ConfigureAppsForm.resx](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/ConfigureAppsForm.resx) 为空(仅有 schema)
- [Properties/Resources.Designer.cs](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/Properties/Resources.Designer.cs) 已包含完整的 `ResourceManager` 基础设施,可直接扩展字符串属性

### 项目约束(来自 [CONTRIBUTING.md](file:///Users/shen/Studio/Code/NoSleep/CONTRIBUTING.md))
- **避免外部依赖**:本方案不引入任何 NuGet 包,仅用 .NET 内置资源机制
- **单一可执行文件**:卫星程序集在 .NET 8.0 单文件发布中会被打入 bundle,符合约束
- **跨框架兼容**:卫星程序集机制同时支持 net48 和 net8.0-windows
- **保持精简**:资源查找开销可忽略不计

### 不应本地化的项
| 项 | 原因 |
|---|---|
| `Settings.AppName`("NoSleep") | 用于 Mutex 名称、品牌名,保持不变 |
| `Settings.AppStartupName`("NoSleep") | 用于自启动快捷方式文件名 `.lnk`,本地化会导致切换语言后遗留孤立快捷方式 |
| `Settings.AppMutexGuid` | 技术标识符 |
| 托盘图标 `Text`(= AppName) | 品牌名,显示为 "NoSleep" |
| MessageBox 标题(使用 AppName) | 品牌名 |
| 代码注释 | 非用户可见,保持英文(遵循项目惯例) |

## 提议变更

### 1. 资源文件

#### 1.1 [Properties/Resources.resx](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/Properties/Resources.resx) — 添加英文字符串(中性/默认)
在现有图标条目后,新增所有用户可见字符串的 `<data>` 条目,值为英文原文。这些作为 fallback 默认值。

#### 1.2 新建 `Properties/Resources.zh-CN.resx` — 简体中文翻译
与 `Resources.resx` 结构相同(schema + resheader),包含相同的 `<data>` 条目但值为中文翻译。构建时自动生成 `zh-CN/NoSleep.resources.dll` 卫星程序集。

#### 1.3 [Properties/Resources.Designer.cs](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/Properties/Resources.Designer.cs) — 添加字符串属性
在现有 `TrayIcon`/`TrayIconInactive` 图标属性之后,为每个字符串键添加 `internal static string` 属性,模式如下:
```csharp
internal static string TrayMenu_Close {
    get { return ResourceManager.GetString("TrayMenu_Close", resourceCulture); }
}
```

### 2. 项目文件 [NoSleep.csproj](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/NoSleep.csproj)
在 `<EmbeddedResource Include="Properties\Resources.resx">` 条目之后,添加:
```xml
<EmbeddedResource Include="Properties\Resources.zh-CN.resx">
  <DependentUpon>Resources.resx</DependentUpon>
</EmbeddedResource>
```
SDK 风格项目会自动识别文件名中的 `zh-CN` 文化后缀并生成对应的卫星程序集。

### 3. 代码改动(用 `Properties.Resources.XXX` 替换硬编码字符串)

#### 3.1 [TrayIcon.cs](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/TrayIcon.cs#L146-L183) — `InitializeComponent` 方法
| 行 | 原文 | 替换为 |
|---|---|---|
| 146 | `"Close"` | `Resources.TrayMenu_Close` |
| 149 | `"Autostart at login"` | `Resources.TrayMenu_Autostart` |
| 152 | `"Should we start when you log in?"` | `Resources.Tooltip_Autostart` |
| 156 | `"Remember enabled state"` | `Resources.TrayMenu_RememberState` |
| 159 | `"Should we remember the enabled state between restarts?"` | `Resources.Tooltip_RememberState` |
| 163 | `"Configure apps to monitor"` | `Resources.TrayMenu_ConfigureApps` |
| 166 | `"Configure apps to keep the screen on when they are running."` | `Resources.Tooltip_ConfigureApps` |
| 170 | `"Enabled"` | `Resources.TrayMenu_Enabled` |
| 173 | `"Are we enabled right now?"` | `Resources.Tooltip_Enabled` |
| 177 | `"Keep screen on"` | `Resources.TrayMenu_KeepScreenOn` |
| 180 | `"If display should be kept always on in addition to keeping the system on."` | `Resources.Tooltip_KeepScreenOn` |

#### 3.2 [Program.cs](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/Program.cs#L19)
```csharp
// 原:
$"{Properties.Settings.Default.AppName} instance is already running."
// 改为:
string.Format(Resources.Msg_InstanceRunning, Properties.Settings.Default.AppName)
```
说明:使用 `string.Format` 以支持 `{0}` 占位符,保持 `AppName` 作为品牌名注入。

#### 3.3 [Tools.cs](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/Tools.cs#L49)
```csharp
// 原 (AutostartDisable):
$"Wasn't able to remove autostart shortcut from '{autostartPath}'. Error: {e.Message}"
// 改为:
string.Format(Resources.Msg_AutostartRemoveFailed, autostartPath, e.Message)

// 原 (AutostartEnable):
$"Wasn't able to create autostart shortcut at '{autostartPath}'. Error: {e.Message}"
// 改为:
string.Format(Resources.Msg_AutostartCreateFailed, autostartPath, e.Message)
```

#### 3.4 [ConfigureAppsForm.cs](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/ConfigureAppsForm.cs)
| 行 | 原文 | 替换为 |
|---|---|---|
| 31 | `"Executable\|*.exe\|All files\|*.*"` | `Resources.FileDlg_FilterExecutable + "\|*.exe\|" + Resources.FileDlg_FilterAllFiles + "\|*.*"` |
| 32 | `"Select application executable"` | `Resources.FileDlg_Title` |
| 53 | `"Apps watching is enabled"` | `Resources.Btn_AppsWatchingEnabled` |
| 53 | `"Apps watching is DISABLED ❌"` | `Resources.Btn_AppsWatchingDisabled` |

需要在文件顶部添加 `using NoSleep.Properties;`(若尚未引用)。

#### 3.5 [ConfigureAppsForm.Designer.cs](file:///Users/shen/Studio/Code/NoSleep/Sources/NoSleep/ConfigureAppsForm.Designer.cs)
将 `InitializeComponent` 中的硬编码字符串替换为资源引用:
| 行 | 原文 | 替换为 |
|---|---|---|
| 66 | `"Name"` | `Properties.Resources.Col_Name` |
| 75 | `"Exe Path"` | `Properties.Resources.Col_ExePath` |
| 87 | `"➕ Add"` | `Properties.Resources.Btn_Add` |
| 99 | `"➖ Remove"` | `Properties.Resources.Btn_Remove` |
| 112 | `"Enable"` | `Properties.Resources.Btn_Enable` |
| 125 | `"Close"` | `Properties.Resources.Btn_Close` |
| 144 | `"Configure Applications to monitor"` | `Properties.Resources.Form_Title` |

注意:Designer.cs 中引用需使用完整命名空间 `Properties.Resources.XXX` 或在文件顶部添加 `using NoSleep.Properties;`。

### 4. 文档翻译

为三个文档创建中文版本,并在英文原文顶部添加语言切换链接:

| 英文文档(保留) | 中文文档(新建) |
|---|---|
| [readme.md](file:///Users/shen/Studio/Code/NoSleep/readme.md) | `readme.zh-CN.md` |
| [BUILD.md](file:///Users/shen/Studio/Code/NoSleep/BUILD.md) | `BUILD.zh-CN.md` |
| [CONTRIBUTING.md](file:///Users/shen/Studio/Code/NoSleep/CONTRIBUTING.md) | `CONTRIBUTING.zh-CN.md` |

每个英文文档顶部添加:
```markdown
> 🌐 Languages: [English](readme.md) | [简体中文](readme.zh-CN.md)
```
每个中文文档顶部添加:
```markdown
> 🌐 语言:[English](readme.md) | [简体中文](readme.zh-CN.md)
```

## 字符串清单(完整)

| 键 | 英文原文 | 中文翻译 |
|---|---|---|
| `TrayMenu_Close` | Close | 关闭 |
| `TrayMenu_Autostart` | Autostart at login | 登录时自动启动 |
| `TrayMenu_RememberState` | Remember enabled state | 记住启用状态 |
| `TrayMenu_ConfigureApps` | Configure apps to monitor | 配置监视的应用程序 |
| `TrayMenu_Enabled` | Enabled | 已启用 |
| `TrayMenu_KeepScreenOn` | Keep screen on | 保持屏幕常亮 |
| `Tooltip_Autostart` | Should we start when you log in? | 是否在登录时自动启动? |
| `Tooltip_RememberState` | Should we remember the enabled state between restarts? | 是否在重启之间记住启用状态? |
| `Tooltip_ConfigureApps` | Configure apps to keep the screen on when they are running. | 配置应用程序,当其运行时保持屏幕常亮。 |
| `Tooltip_Enabled` | Are we enabled right now? | 当前是否已启用? |
| `Tooltip_KeepScreenOn` | If display should be kept always on in addition to keeping the system on. | 在保持系统运行的基础上,是否同时保持显示器常亮。 |
| `Msg_InstanceRunning` | {0} instance is already running. | {0} 实例已在运行。 |
| `Msg_AutostartRemoveFailed` | Wasn't able to remove autostart shortcut from '{0}'. Error: {1} | 无法从 '{0}' 移除自启动快捷方式。错误:{1} |
| `Msg_AutostartCreateFailed` | Wasn't able to create autostart shortcut at '{0}'. Error: {1} | 无法在 '{0}' 创建自启动快捷方式。错误:{1} |
| `Btn_AppsWatchingEnabled` | Apps watching is enabled | 应用监视已启用 |
| `Btn_AppsWatchingDisabled` | Apps watching is DISABLED ❌ | 应用监视已禁用 ❌ |
| `FileDlg_FilterExecutable` | Executable | 可执行文件 |
| `FileDlg_FilterAllFiles` | All files | 所有文件 |
| `FileDlg_Title` | Select application executable | 选择应用程序可执行文件 |
| `Col_Name` | Name | 名称 |
| `Col_ExePath` | Exe Path | 可执行文件路径 |
| `Btn_Add` | ➕ Add | ➕ 添加 |
| `Btn_Remove` | ➖ Remove | ➖ 移除 |
| `Btn_Enable` | Enable | 启用 |
| `Btn_Close` | Close | 关闭 |
| `Form_Title` | Configure Applications to monitor | 配置监视的应用程序 |

共 **26** 个字符串条目。

## 假设与决策

1. **语言选择策略**:依赖 .NET 默认的 `CurrentUICulture` 自动检测。中文系统 → 中文,其他系统 → 英文(默认)。不在代码中强制设置 culture,保持标准行为。
2. **品牌名不本地化**:`NoSleep` 作为品牌名在所有语言中保持不变(Mutex、快捷方式文件名、托盘 tooltip、MessageBox 标题)。
3. **文化代码**:使用 `zh-CN`(简体中文,中国大陆),覆盖最广。如需支持繁体中文可后续添加 `zh-TW`。
4. **Designer.cs 手动编辑**:虽然 Designer.cs 是自动生成的,但项目已手动管理编译项(`EnableDefaultCompileItems=false`),且当前环境无 VS 设计器运行,手动编辑安全。在 VS 中重新生成时会保留字符串属性(只要 .resx 中有对应条目)。
5. **单文件发布兼容性**:.NET 6+ 的 `PublishSingleFile` 默认将卫星程序集打入单文件 bundle,无需额外配置。net48 无单文件发布需求,卫星程序集正常输出到 `zh-CN\` 子目录。
6. **文档策略**:保留英文原文 + 新增中文版本 + 顶部语言链接,符合 GitHub 国际化惯例。

## 验证步骤

1. **构建验证**(两个框架):
   ```sh
   cd Sources
   dotnet build -f net8.0-windows -c Debug
   # net48 需在 Windows 上用 msbuild
   ```
2. **卫星程序集生成验证**:确认 `bin/Debug/net8.0-windows/zh-CN/NoSleep.resources.dll` 存在。
3. **单文件发布验证**:
   ```sh
   dotnet publish -f net8.0-windows -c Release
   ```
   确认发布产物为单文件,且中文资源已打入(运行时验证)。
4. **运行时验证**:
   - **中文环境**:设置系统 locale 为简体中文,或临时在 `Program.cs` 中添加 `Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");` 测试,确认所有 UI 文本显示中文。
   - **英文环境**:保持默认 locale,确认所有 UI 文本显示英文(回退正常)。
5. **UI 完整性检查**:逐项核对托盘菜单(6 项 + 6 tooltip)、单实例提示、自启错误提示、配置窗口(标题 + 表头 + 4 按钮 + 文件对话框)。
6. **功能回归**:确认左键切换、右键菜单各功能、应用监视、自启动等行为不受影响。
