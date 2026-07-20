# NoSleep

NoSleep is a lightweight tool that prevents Windows from automatically activating the screen saver, sleep mode, or lock screen. It's designed for situations where you can't change these settings yourself—for example, due to corporate-enforced policies. Inspired by Linux Caffeine.

> **Note:** Windows PowerToys includes a tool called Awake that does the same thing, but PowerToys may require administrator privileges and is quite large. NoSleep aims to be as minimal as possible and runs without any extra permissions.

## Installation

NoSleep can be installed via the [Scoop](https://scoop.sh/) package manager:

```sh
scoop bucket add extras
scoop install extras/nosleep
```

Alternatively, you can download the latest release manually from [the releases page](https://github.com/CHerSun/NoSleep/releases/latest).

## Usage

NoSleep is designed to be "set and forget". Once running, it sits in the Windows system tray and prevents your PC from sleeping.

- **Left‑click** the tray icon to toggle the enabled state on or off. The icon changes to reflect the current state.
- **Right‑click** the tray icon to open a menu with additional options:

  - **Autostart at login** – Start NoSleep automatically when you log in.
  - **Keep screen on** – Prevent the display from turning off while NoSleep is enabled.
  - **Remember enabled state** – Save the enabled state between application restarts.
  - **Configure apps to monitor** – Define a list of applications. If none of them are running, NoSleep automatically disables itself; if any monitored app is running, NoSleep enables itself and prevents sleep. This dynamic behavior only works when NoSleep is enabled by the user. (Available since v1.4.0)

To completely stop NoSleep, right‑click the tray icon and select **Close**.

### Behavior Matrix

| Enabled | Keep Screen On | System Behavior                          | Display Behavior |
| ------- | -------------- | ---------------------------------------- | ---------------- |
| ✅ On   | ✅ On          | Sleep is prevented                       | Always on        |
| ✅ On   | ⬜ Off         | Sleep is prevented                       | Can turn off     |
| ⬜ Off  | Any            | Normal system behavior (sleep may occur) | Can turn off     |

## Requirements

- .NET Framework 4.8 or later (4.x branch). This is typically preinstalled on Windows 10 and later. If needed, you can download it from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet-framework).

## How It Works

NoSleep calls the `SetThreadExecutionState` function every 10 seconds, resetting Windows' display and idle timers. This process uses negligible CPU and only a small amount of RAM. The compiled binary includes icons (about 180 KB) and the core code.

## Icons Attribution

| Usage          | Icon & Source                                                                                  | License                  | Artist                                                                      |
| -------------- | ---------------------------------------------------------------------------------------------- | ------------------------ | --------------------------------------------------------------------------- |
| Enabled state  | [Coffee icon](https://www.iconarchive.com/show/food-icons-by-martin-berube/coffee-icon.html)   | Freeware                 | [Martin Berube](https://www.iconarchive.com/artist/martin-berube.html)      |
| Disabled state | [Sleep icon](https://www.iconarchive.com/show/material-icons-by-pictogrammers/sleep-icon.html) | Apache 2.0 (Open Source) | [Pictogrammers Team](https://www.iconarchive.com/artist/pictogrammers.html) |

Many thanks to the artists for these icons!
