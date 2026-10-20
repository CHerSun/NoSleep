> 🌐 语言:[English](BUILD.md) | [简体中文](BUILD.zh-CN.md)

# 如何从源代码构建

本文档描述了如何从源代码构建 NoSleep。这些说明面向希望为项目贡献代码或对代码库进行试验的开发者。

该解决方案使用 WinForms,并以两个框架为目标:

- net48 – .NET Framework 4.8(旧版)
- net8.0-windows – .NET 8.0(Windows 专用)

目标是构建一个体积较小的独立可执行文件(即我们可以发布单文件;可执行文件本身不包含 .NET 运行时),以便于简单分发。

NoSleep 最初使用 .NET 4.x 框架构建。由于 .NET 4.x 随 Windows 一起捆绑,通常终端用户无需进行额外操作。

在项目现代化过程中添加了 .NET 8.0,以便更轻松地进行未来的更新。

## 先决条件

- Windows(必需,因为该应用程序仅支持 Windows)
- 对于 .NET 8.0 构建:
  - .NET SDK 8.0 或更高版本
- 对于 .NET Framework 4.8(旧版):
  - .NET SDK(任意较新版本)
  - .NET Framework 4.8 SDK
  - MSBuild

您可以通过安装以下任意一项来获取 .NET Framework 4.8 所需的组件:

- Visual Studio Build Tools – 选择 ".NET desktop build tools" 工作负载(这将包含所有必要的组件),或
- 完整版 Visual Studio(Community/Professional/Enterprise)并选择 ".NET desktop development" 工作负载。

## 获取源代码

克隆仓库:

```sh
git clone https://github.com/CHerSun/NoSleep.git
cd NoSleep
```

## 构建结果位置

- .NET Framework 4.8 - `Sources/NoSleep/bin/Debug/net48/` 或 `Sources/NoSleep/bin/Release/net48/`
- .NET 8.0 - `Sources/NoSleep/bin/Debug/net8.0-windows/` 或 `Sources/NoSleep/bin/Release/net8.0-windows/`
- 已发布(单可执行文件)的 .NET 8.0 - `Sources/NoSleep/bin/Release/net8.0-windows/win-x64/publish`

## 使用 Visual Studio 构建

这是构建解决方案最简单的方式。只需打开 `Sources/NoSleep.sln` 并正常构建(`F6` 或 `Ctrl+Shift+B`)。

## 从命令行构建

### 构建 .NET 8.0 版本

使用 `dotnet build` 命令。要构建调试版本:

```sh
cd Sources
dotnet build -f net8.0-windows -c Debug
```

构建发布版本:

```sh
cd Sources
dotnet build -f net8.0-windows -c Release
```

注意:在 `net8.0-windows` 的发布构建完成后,会自动触发发布步骤(以创建单可执行文件)。

### 构建 .NET Framework 4.8 版本

我无法使用 dotnet build 生成单可执行文件,因此我们对该目标使用 `msbuild`。要构建调试版本:

```sh
cd Sources
msbuild NoSleep/NoSleep.csproj /p:Configuration=Debug /p:TargetFramework=net48 /restore
```

构建发布版本:

```sh
cd Sources
msbuild NoSleep/NoSleep.csproj /p:Configuration=Release /p:TargetFramework=net48 /restore
```

请确保 `msbuild` 位于您的 PATH 中(通常可从 Visual Studio Developer Command Prompt 中获取),或者提供 `msbuild.exe` 的完整路径。

## 使用 Visual Studio Code 构建

我个人更喜欢 VS Code。您可以在 `.vscode/tasks.json` 文件中定义构建任务。以下是一个示例 `tasks.json`:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build-net48",
            "command": "C:\\Program Files (x86)\\Microsoft Visual Studio\\18\\BuildTools\\MSBuild\\Current\\Bin\\MSBuild.exe",
            "type": "process",
            "group": "build",
            "problemMatcher": "$msCompile",
            "options": {
                "cwd": "${workspaceFolder}/Sources/"
            },
            "args": [
                "${workspaceFolder}/Sources/NoSleep/NoSleep.csproj",
                "/p:Configuration=Debug",
                "/p:TargetFramework=net48",
                "/p:GenerateResourceUsePreserializedResources=false",  // 可选,如有需要请保留
                "/restore"
            ]
        },
        {
            "label": "build-net48-release",
            "command": "C:\\Program Files (x86)\\Microsoft Visual Studio\\18\\BuildTools\\MSBuild\\Current\\Bin\\MSBuild.exe",
            "type": "process",
            "group": "build",
            "problemMatcher": "$msCompile",
            "options": {
                "cwd": "${workspaceFolder}/Sources/"
            },
            "args": [
                "${workspaceFolder}/Sources/NoSleep/NoSleep.csproj",
                "/p:Configuration=Release",
                "/p:TargetFramework=net48",
                "/p:GenerateResourceUsePreserializedResources=false",  // 可选,如有需要请保留
                "/restore"
            ]
        },
        {
            "label": "build-net8.0",
            "command": "dotnet",
            "type": "process",
            "group": "build",
            "problemMatcher": "$msCompile",
            "options": {
                "cwd": "${workspaceFolder}/Sources/"
            },
            "args": [
                "build",
                "${workspaceFolder}/Sources/NoSleep/NoSleep.csproj",
                "-f",
                "net8.0-windows",
                "-c",
                "Debug"
            ]
        },
        {
            "label": "publish-net8.0-release",
            "command": "dotnet",
            "type": "process",
            "group": "build",
            "problemMatcher": "$msCompile",
            "options": {
                "cwd": "${workspaceFolder}/Sources/"
            },
            "args": [
                "publish",
                "${workspaceFolder}/Sources/NoSleep/NoSleep.csproj",
                "-f",
                "net8.0-windows",
                "-c",
                "Release",
            ]
        },
        {
            "label": "build-all",
            "dependsOn": ["build-net48", "build-net8.0"],
            "group": {
                "kind": "build"
            }
        },
        {
            "label": "build-all-release",
            "dependsOn": ["build-net48-release", "publish-net8.0-release"],
            "group": {
                "kind": "build",
                "isDefault": true
            }
        },
    ]
}
```

您可能需要根据自身环境调整 `MSBuild` 路径或重新定义任务。

## 调试

Visual Studio 提供即时调试支持。

对于 VS Code,您需要一个调试配置。以下是一个示例 `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug .NET 8.0 (Windows)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build-net8.0",
            "program": "${workspaceFolder}/Sources/NoSleep/bin/Debug/net8.0-windows/NoSleep.dll",
            "args": [],
            "cwd": "${workspaceFolder}",
            "stopAtEntry": false,
            "console": "internalConsole"
        },
        {
            "name": "Debug .NET Framework 4.8",
            "type": "clr",
            "request": "launch",
            "preLaunchTask": "build-net48",
            "program": "${workspaceFolder}/Sources/NoSleep/bin/Debug/net48/NoSleep.exe",
            "args": [],
            "cwd": "${workspaceFolder}",
            "console": "internalConsole"
        }
    ]
}
```

请注意,`preLaunchTask` 的值必须与 `tasks.json` 中定义的任务标签完全一致。
