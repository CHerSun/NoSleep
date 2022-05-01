# NoSleep windows utility
NoSleep is a tool that prevents Windows OS from automatically going into ScreenSaver / Sleep / Lock modes. It's meant for those cases where user cannot change settings (due to corporate-enforced policy, for example). Inspired by Linux Caffeine.

> NOTE: Windows PowerToys now has PowerToys.Awake tool which does the same thing + has a context menu with options. Could require admin permissions though.

## Usage
Download **NoSleep.exe** from **"Compiled binary"** folder. Then just run and forget. You can also add shortcut to NoSleep to your StartUp items for NoSleep to start on user login. While active NoSleep will sit in Windows tray and prevent Windows from blocking.

If you wish to stop it - close the program - right-click tray icon and select "Close".

## Requirements
.NET 4.0 (client profile) or later (any 4.x.x should work)

## How it works
NoSleep works through calling to SetThreadExecutionState resetting display/idle Windows timers every 10 seconds. Should use no CPU and around 6-7 MBs of RAM. Icons are taking up 166 KB in compiled binary, the rest is the code (~10 KB).

## Icon
Icon was taken from http://www.iconarchive.com/show/food-icons-by-martin-berube/coffee-icon.html. Icon is disributed as freeware. 

Artist: Martin Berube (thanks, Martin!)

## License
The Unlicense
