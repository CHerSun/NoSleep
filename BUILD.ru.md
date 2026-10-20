> 🌐 Языки: [English](BUILD.md) | [Русский](BUILD.ru.md) | [简体中文](BUILD.zh-CN.md)

# Как собрать из исходного кода

Этот документ описывает сборку NoSleep из исходного кода. Инструкции предназначены для разработчиков, которые хотят внести вклад или поэкспериментировать с кодовой базой.

Решение использует WinForms и собирается под три фреймворка:

- net48 – .NET Framework 4.8 (устаревший, основной)
- net8.0-windows – .NET 8.0 (специфичный для Windows)
- net10.0-windows – .NET 10.0 (специфичный для Windows, актуальный LTS)

Цель — собирать один автономный исполняемый файл небольшого размера (то есть публиковать единый файл; исполняемый файл не содержит среду .NET) для простой дистрибуции.

NoSleep изначально собиралась на .NET 4.x. Поскольку .NET 4.x поставляется вместе с Windows, от конечного пользователя обычно не требуется никаких дополнительных действий.

.NET 8.0 был добавлен в ходе модернизации проекта для упрощения будущих обновлений, а .NET 10.0 присоединился позже как актуальный LTS — современные цели являются частью постепенного перехода с устаревшего .NET Framework.

## Предварительные требования

- Windows (обязательно, приложение только для Windows)
- Для современных сборок .NET (net8.0 / net10.0):
  - .NET SDK 8.0 или новее
- Для .NET Framework 4.8 (устаревший):
  - .NET SDK (любая свежая версия)
  - SDK .NET Framework 4.8
  - MSBuild

Компоненты для .NET Framework 4.8 можно получить, установив либо:

- Visual Studio Build Tools — выберите рабочую нагрузку «.NET desktop build tools» (она включает все необходимые компоненты), либо
- Полную Visual Studio (Community/Professional/Enterprise) с рабочей нагрузкой «.NET desktop development».

## Получение исходного кода

Клонируйте репозиторий:

```sh
git clone https://github.com/CHerSun/NoSleep.git
cd NoSleep
```

## Расположение результатов сборки

- .NET Framework 4.8 — `Sources/NoSleep/bin/Debug/net48/` или `Sources/NoSleep/bin/Release/net48/`
- .NET 8.0 — `Sources/NoSleep/bin/Debug/net8.0-windows/` или `Sources/NoSleep/bin/Release/net8.0-windows/`
- .NET 10.0 — `Sources/NoSleep/bin/Debug/net10.0-windows/` или `Sources/NoSleep/bin/Release/net10.0-windows/`
- Опубликованные (единый исполняемый файл) современные .NET — `Sources/NoSleep/bin/Release/<фреймворк>/win-x64/publish`

## Сборка в Visual Studio

Это самый простой способ. Откройте `Sources/NoSleep.sln` и соберите обычным образом (`F6` или `Ctrl+Shift+B`).

## Сборка из командной строки

### Сборка .NET 8.0 / .NET 10.0

Используйте команду `dotnet build`. Отладочная сборка:

```sh
cd Sources
dotnet build -f net8.0-windows -c Debug
```

Релизная сборка:

```sh
cd Sources
dotnet build -f net8.0-windows -c Release
dotnet build -f net10.0-windows -c Release
```

Примечание: шаг публикации (создание единого исполняемого файла) запускается автоматически после релизной сборки современного .NET-таргета.

### Сборка .NET Framework 4.8

Однофайловый exe через `dotnet build` получить не удалось, поэтому для этого таргета используется `msbuild`. Отладочная сборка:

```sh
cd Sources
msbuild NoSleep/NoSleep.csproj /p:Configuration=Debug /p:TargetFramework=net48 /restore
```

Релизная сборка:

```sh
cd Sources
msbuild NoSleep/NoSleep.csproj /p:Configuration=Release /p:TargetFramework=net48 /restore
```

Убедитесь, что `msbuild` есть в PATH (обычно доступен в Developer Command Prompt для Visual Studio) либо укажите полный путь к `msbuild.exe`.

## Сборка в Visual Studio Code

Лично я предпочитаю VS Code. Задачи сборки можно описать в файле `.vscode/tasks.json`. Пример `tasks.json`:

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
            "dependsOn": ["build-net48", "publish-net8.0-release"],
            "group": {
                "kind": "build",
                "isDefault": true
            }
        },
    ]
}
```

Вам может понадобиться скорректировать путь к `MSBuild` или переопределить задачи под ваше окружение.

## Отладка

Visual Studio предоставляет штатную поддержку отладки.

Для VS Code потребуется конфигурация запуска. Пример `.vscode/launch.json`:

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

Обратите внимание: значения `preLaunchTask` должны в точности совпадать с именами задач из `tasks.json`.
