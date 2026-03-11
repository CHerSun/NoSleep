# How to Build from Source Code

This document describes how to build NoSleep from source code. These instructions are intended for developers who want to contribute or experiment with the codebase.

The solution uses WinForms and targets two frameworks:

- net48 – .NET Framework 4.8 (legacy)
- net8.0-windows – .NET 8.0 (Windows-specific)

The goal is to build a single standalone executable of small size (i.e., we can publish a single file; the executable does not contain the .NET runtime) for simple distribution.

NoSleep was originally built using the .NET 4.x framework. Since .NET 4.x comes bundled with Windows, normally no extra steps are required from the end user.

.NET 8.0 was added during project modernization to make future updates easier.

## Prerequisites

- Windows (required because the application is Windows‑only)
- For .NET 8.0 build:
  - .NET SDK 8.0 or later
- For .NET Framework 4.8 (legacy):
  - .NET SDK (any recent version)
  - .NET Framework 4.8 SDK
  - MSBuild

You can obtain the required components for .NET Framework 4.8 by installing either:

- Visual Studio Build Tools – select the ".NET desktop build tools" workload (this will include all necessary components), or
- Full Visual Studio (Community/Professional/Enterprise) with the ".NET desktop development" workload.

## Getting the Source

Clone the repository:

```sh
git clone https://github.com/CHerSun/NoSleep.git
cd NoSleep
```

## Build results locations

- .NET Framework 4.8 - `Sources/NoSleep/bin/Debug/net48/` or `Sources/NoSleep/bin/Release/net48/`
- .NET 8.0 - `Sources/NoSleep/bin/Debug/net8.0-windows/` or `Sources/NoSleep/bin/Release/net8.0-windows/`
- Published (single executable) .NET 8.0 - `Sources/NoSleep/bin/Release/net8.0-windows/win-x64/publish`

## Building with Visual Studio

This is the easiest way to build the solution. Simply open `Sources/NoSleep.sln` and build normally (`F6` or `Ctrl+Shift+B`).

## Building from the Command Line

### Build .NET 8.0 Version

Use the `dotnet build` command. To build the debug version:

```sh
cd Sources
dotnet build -f net8.0-windows -c Debug
```

For the release version:

```sh
cd Sources
dotnet build -f net8.0-windows -c Release
```

Note: The publish step (to create a single executable) is triggered automatically after the release build of `net8.0-windows` is complete.

### Build .NET Framework 4.8 Version

I wasn’t able to produce a single executable with dotnet build, so we use `msbuild` for this target. For a debug build:

```sh
cd Sources
msbuild NoSleep/NoSleep.csproj /p:Configuration=Debug /p:TargetFramework=net48 /restore
```

For the release build:

```sh
cd Sources
msbuild NoSleep/NoSleep.csproj /p:Configuration=Release /p:TargetFramework=net48 /restore
```

Make sure `msbuild` is in your PATH (it is usually available from a Visual Studio Developer Command Prompt) or provide the full path to `msbuild.exe`.

## Building with Visual Studio Code

Personally I prefer VS Code. You can define build tasks in a `.vscode/tasks.json` file. Here is a sample `tasks.json`:

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
                "/p:GenerateResourceUsePreserializedResources=false",  // optional, but keep if needed
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
                "/p:GenerateResourceUsePreserializedResources=false",  // optional, but keep if needed
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

You might need to adjust the `MSBuild` path or redefine the tasks to suit your environment.

## Debugging

Visual Studio provides immediate debugging support.

For VS Code, you will need a debugging configuration. Here is a sample `.vscode/launch.json`:

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

Note that the `preLaunchTask` values must match the task labels exactly as defined in `tasks.json`.
