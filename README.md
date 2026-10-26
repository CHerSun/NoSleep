> 🌐 Languages: [English](README.md) | [Русский](README.ru.md) | [简体中文](README.zh-CN.md)

[![GitHub Release](https://img.shields.io/github/v/release/CHerSun/NoSleep)](https://github.com/CHerSun/NoSleep/releases/latest)

# NoSleep

NoSleep is a lightweight tool that prevents Windows from automatically activating the screen saver, sleep mode, or lock screen. It's designed for situations where you can't change these settings yourself—for example, due to corporate-enforced policies. Inspired by Linux Caffeine.

NoSleep is sleep-mode aware: it detects whether your machine uses classic (S1–S3) or Modern Standby (S0) sleep and adapts — see [Sleep modes](#sleep-modes-modern-standby-vs-classic).

> **Note:** Windows PowerToys includes a tool called Awake that does the same thing, but PowerToys may require administrator privileges and is quite large. NoSleep aims to be as minimal as possible and runs without any extra permissions.

## Installation

NoSleep can be installed via the [Scoop](https://scoop.sh/) package manager:

```sh
scoop bucket add extras
scoop install extras/nosleep
```

Alternatively, you can download the latest release manually from [the releases page](https://github.com/CHerSun/NoSleep/releases/latest). Each release carries three executables — pick just one:

- **`NoSleep.exe`** — the default build for everyone (runs on .NET Framework 4.8, preinstalled on Windows).
- **`NoSleep-net8.exe`** / **`NoSleep-net10.exe`** — single-file builds for users who already have the .NET 8 or .NET 10 runtime.

## Usage

NoSleep is designed to be "set and forget". Once running, it sits in the Windows system tray and prevents your PC from sleeping.

- **Left‑click** the tray icon to toggle the enabled state on or off. The icon changes to reflect the current state; when disabled, the "zzz" icon follows your Windows color mode (white on dark taskbars) so it stays visible.
- **Right‑click** the tray icon to open a menu with additional options:

  - **Autostart at login** – Start NoSleep automatically when you log in.
  - **Keep screen on** – Prevent the display from turning off while NoSleep is enabled.
  - **Block sleep on lid close** *(Modern Standby laptops only; off by default)* – While NoSleep is enabled, closing the lid does not put the machine to sleep; the original lid action is restored when NoSleep is disabled or exits. (Available since v1.6.0)
  - **Simulate activity** *(Modern Standby machines only; off by default)* – See [Sleep modes](#sleep-modes-modern-standby-vs-classic). (Available since v1.6.0)
  - **Remember enabled state** – Save the enabled state between application restarts.
  - **Configure apps to monitor** – Define a list of applications. If none of them are running, NoSleep automatically disables itself; if any monitored app is running, NoSleep enables itself and prevents sleep. This dynamic behavior only works when NoSleep is enabled by the user. (Available since v1.4.0)

To completely stop NoSleep, right‑click the tray icon and select **Close**.

### Language

The interface automatically follows your Windows display language among **English, Русский and 简体中文**. To pick a language explicitly, launch with the `--lang` flag:

```sh
NoSleep.exe --lang ru        # Russian for this run
NoSleep.exe --lang zh-CN     # Simplified Chinese
NoSleep.exe --lang list      # show the available languages
```

To make the choice permanent, add the flag to the target of your NoSleep shortcut.

## Requirements

- **`NoSleep.exe`** (default): .NET Framework 4.8 or later — typically preinstalled on Windows 10 and later; if needed, downloadable from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet-framework).
- **`NoSleep-net8.exe`** / **`NoSleep-net10.exe`**: the respective .NET runtime installed.

## How It Works

NoSleep keeps the system awake by re-asserting a Windows power request every 10 seconds (`SetThreadExecutionState`) — negligible CPU and a small amount of RAM; the compiled binary includes the icons (about 180 KB) and the core code. On Modern Standby machines, NoSleep additionally applies the S0-specific protections described in [Sleep modes](#sleep-modes-modern-standby-vs-classic).

## Sleep modes (Modern Standby vs classic)

Windows machines sleep in one of two ways, and NoSleep shows which one applies in the first line of the tray menu. You can check yours with `powercfg /a` in a terminal.

**What NoSleep can prevent while it is enabled:**

| Event | Classic (S3) | Modern Standby (S0) | S0 on battery |
| --- | --- | --- | --- |
| Automatic sleep | ✅ Prevented | ✅ Prevented | ✅ Prevented — until sleep timeout + 5 min<br>✔ Indefinite (Simulate activity) |
| Screen turning off | ✅ Prevented (Keep screen on) | ✅ Prevented (Keep screen on) | ✅ Prevented (Keep screen on) |
| Sleep on lid close | ✅ Prevented | ✔ Can prevent (Block sleep on lid close) | ✔ Can prevent (Block sleep on lid close) |
| Sleep via power menu / button | ✅ Prevented | — | — |

*✅ = prevented by default · ✔ = can be prevented with an option · — = not targeted. The table shows the effective (enabled) state — when NoSleep is disabled, or app monitoring is active and no watched app is running, Windows behaves normally. The lid is the only manual-sleep trigger NoSleep overrides — covering the sleep button too is intentionally out of scope.*

**Classic (S3)** — the traditional sleep. While NoSleep is enabled, sleep is prevented entirely, including manually triggered sleep — the machine only *appears* to sleep, while it keeps running. This is the behavior NoSleep has always had.

**Modern Standby (S0)** — the newer model used by most recent laptops. Here Windows deliberately terminates what apps ask for when *you* explicitly request sleep, so NoSleep cannot simply veto it. Two additional options (both off by default) cover the gaps:

- **Block sleep on lid close** — while NoSleep is enabled, it temporarily sets the lid action to "Do nothing" and restores your original setting when it is disabled or exits (also automatically restored on the next launch if NoSleep was killed). Closing the lid then leaves the machine running with the screen off — exactly like an external monitor setup.
- **Simulate activity** — how NoSleep fights sleep triggers that power requests cannot cover on Modern Standby:
  - *Prevent sleep attempts* — best-effort: when the system tries to sleep (sleep button, power menu), NoSleep reacts with a short burst of simulated input that aims to wake it right back up. You may briefly appear "active" in chats for a moment. For lid close, prefer the deterministic **Block sleep on lid close** option above.
  - *Simulate user presence* — NoSleep continuously simulates invisible user activity (input events that move nothing — the cursor never moves), preventing **automatic** sleep. **Side effects:** you appear permanently active (chats never idle), and the screen and your session never lock on their own.
  - *Move the mouse (visible)* — same purpose as presence simulation, but with real, visible mouse movement (the cursor returns to its place each time) — for systems where invisible activity is not enough.
  - The continuous modes run at a randomized cadence (so the events are not a perfectly periodic pattern); advanced users can tune it via the `JiggleIntervalMs` / `JiggleJitterPercent` values in the standard settings file (defaults: 10 seconds, ±20%).

> ⚠️ **Laptop safety — overheating risk.** Everything above (and NoSleep itself) exists to keep a machine awake. A laptop that stays awake inside a closed bag or sleeve cannot cool itself: it can overheat, drain its battery, and in the worst case damage itself or nearby items. This risk is inherent to *any* keep-awake tool — including NoSleep's classic S3 behavior, where closing the lid has never meant the machine actually sleeps. For that reason **both Modern Standby options are off by default**: nothing changes on your system until you explicitly opt in. NoSleep also restores every system setting it changed the moment it is disabled or exits, and repairs a leftover lid override on the next launch. Make it a habit: before a laptop goes into a bag, glance at the tray — the coffee icon means it will stay awake — and disable NoSleep.

**Honest limits on Modern Standby:**

- **On battery**, Windows terminates power requests 5 minutes after the sleep timeout — this is OS behavior no keep-awake request can override. Continuous presence simulation (*Simulate user presence* or *Move the mouse*) is the only way to prevent automatic sleep indefinitely on battery.
- Explicitly choosing **Sleep** from the power menu (or the power button) is always honored by the OS on Modern Standby — only activity simulation defeats it. If you want the machine to sleep, disabling NoSleep is the intended path.

If you never need NoSleep's dynamic behavior, setting the lid action to "Do nothing" yourself (Power Options → Choose what closing the lid does) achieves the same without any tool — NoSleep's advantage is that it is tied to one click and reverts automatically.

The full investigation behind these behaviors (with measurements on real S3 and S0 hardware) lives in [`tools/sleep-mode/`](tools/sleep-mode/) in the repository.

## Icons Attribution

| Usage          | Icon & Source                                                                                  | License                  | Artist                                                                      |
| -------------- | ---------------------------------------------------------------------------------------------- | ------------------------ | --------------------------------------------------------------------------- |
| Enabled state  | [Coffee icon](https://www.iconarchive.com/show/food-icons-by-martin-berube/coffee-icon.html)   | Freeware                 | [Martin Berube](https://www.iconarchive.com/artist-martin-berube.html)      |
| Disabled state | [Sleep icon](https://www.iconarchive.com/show/material-icons-by-pictogrammers/sleep-icon.html) (black; white recolor for dark taskbars) | Apache 2.0 (Open Source) | [Pictogrammers Team](https://www.iconarchive.com/artist/pictogrammers.html) |

Many thanks to the artists for these icons!
