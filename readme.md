# NoSleep windows utility
NoSleep is a tool that prevents Windows OS from automatically going into ScreenSaver / Sleep / Lock modes. It's meant for those cases where user cannot change settings (due to corporate-enforced policy, for example). Inspired by Linux Caffeine.

> NOTE: Windows PowerToys now has PowerToys.Awake tool which does the same thing. It could require admin permissions though, also PowerToys are quite large. NoSleep aims to be as minimal as possible with no extra permissions required.

## Usage
Download **NoSleep.exe** from [the latest release](https://github.com/CHerSun/NoSleep/releases/latest). Save it anywhere you like. Then just run it and forget. While active NoSleep will sit in Windows tray and prevent Windows from blocking.

You can also enable Autostart from context menu or manually add shortcut to NoSleep to your StartUp items for NoSleep to start on your login. 

You can click tray icon (or toggle Enabled context menu item) to toggle NoSleep state.

If you wish to completely stop it - close the program - right-click tray icon and select "Close".

## Requirements
.NET 4.8

> Previously NoSleep was targeting .NET 4.0 (client profile). If you encounter problems related to this requirements change - please create an [issue](https://github.com/CHerSun/NoSleep/issues)

## How it works
NoSleep works through calling to SetThreadExecutionState resetting display/idle Windows timers every 10 seconds. Should use no CPU and around 6-7 MBs of RAM. Icons are taking up ~180 KB in compiled binary, the rest is the code (~10 KB).

## Icons

* Coffee icon (Active) was taken from https://www.iconarchive.com/show/food-icons-by-martin-berube/coffee-icon.html. Icon is disributed as freeware. Designer: [Martin Berube](https://www.iconarchive.com/artist/martin-berube.html)
* Sleep icon (Inactive) was taken from https://www.iconarchive.com/show/material-icons-by-pictogrammers/sleep-icon.html. License: Apache 2.0 (Open Source). Designer: [Pictogrammers Team](https://www.iconarchive.com/artist/pictogrammers.html)

Thanks for icons!

## License
The Unlicense
