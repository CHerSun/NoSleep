# NoSleep windows utility
*13 July 2016*


NoSleep utility prevents Windows OS from going into ScreenSaver / Sleep / ScreenLock modes. It's meant for those cases where user cannot change settings due to corporate-enforced policy.

## Usage
Download **NoSleep.exe** from **"Compiled binary"** folder. Then just run and forget. It will sit in tray and work. If you wish to close the program - right-click tray icon and select "Close".

## Requirements
.NET 4.0 client profile

## How it works
NoSleep works through calling to SetThreadExecutionState resetting display/idle Windows timers every 10 seconds. Should use no CPU and around 6-7 MBs of RAM. Icons are taking up 166 KB in compiled binary, the rest is the code (~10 KB).