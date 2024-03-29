# NoSleep windows utility
NoSleep is a tool that prevents Windows OS from automatically going into ScreenSaver / Sleep / Lock modes. It's meant for those cases where user cannot change settings (due to corporate-enforced policy, for example). Inspired by Linux Caffeine.

> NOTE: Windows PowerToys now has PowerToys.Awake tool which does the same thing. It could require admin permissions though, also PowerToys are quite large. NoSleep aims to be as minimal as possible with no extra permissions required.

## Usage

Download **NoSleep.exe** from [the latest release](https://github.com/CHerSun/NoSleep/releases/latest). Save it anywhere you like. Then just run it and forget. While active NoSleep will sit in Windows tray and prevent Windows from blocking.

> NoSleep is now available via [scoop](https://scoop.sh/) too: `scoop install extras/nosleep`
>
> if extras bucket is not yet in use - you need to add the bucket using: `scoop bucket add extras`

You can also enable Autostart from context menu or manually add shortcut to NoSleep to your StartUp items for NoSleep to start on your login. 

You can click tray icon (or toggle Enabled context menu item) to toggle NoSleep state.

If you wish to completely stop it - close the program - right-click tray icon and select "Close".

Options explanation:

| `Enabled`  | `Keep screen On` | System behavior | Display behavior |
|----------|----------------|--------|---------|
| ✅ `On`  | ✅ `On`             | Sleep is prevented | Always on |
| ✅ `On`  | ⬜ `Off`            | Sleep is prevented | Can go off |
| ⬜ `Off` | any | Usual behavior, can sleep, if configured |  Can go off |

## Requirements

.NET 4.8 or later (4.x.x). Can be downloaded from [here](https://dotnet.microsoft.com/en-us/download/dotnet-framework).

> Previously NoSleep was targeting .NET 4.0 (client profile). If you encounter problems related to this requirements change - please create an [issue](https://github.com/CHerSun/NoSleep/issues)

## How it works

NoSleep works through calling to SetThreadExecutionState resetting display/idle Windows timers every 10 seconds. Should use no CPU and around 6-7 MBs of RAM. Icons are taking up ~180 KB in compiled binary, the rest is the code (~10 KB).

## Icons attribution

| Usage | Icon and source | License | Artist |
|-------|-----------------|---------|--------|
| Enabled state | [Coffee icon](https://www.iconarchive.com/show/food-icons-by-martin-berube/coffee-icon.html) | Freeware | [Martin Berube](https://www.iconarchive.com/artist/martin-berube.html) |
| Disabled state | [Sleep icon](https://www.iconarchive.com/show/material-icons-by-pictogrammers/sleep-icon.html) | Apache 2.0 (Open Source) | [Pictogrammers Team](https://www.iconarchive.com/artist/pictogrammers.html) |

Thanks for icons!

## License

The Unlicense
