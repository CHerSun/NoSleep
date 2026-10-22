# AGENTS.md — Guidance for AI Agents Working on NoSleep

NoSleep is a lightweight, open-source (The Unlicense) C#/WinForms tray utility for Windows that prevents sleep/screensaver/lock by calling `SetThreadExecutionState` on a timer. Think "Caffeine for Windows, but minimal": no admin rights, no installer, no runtime download for the primary target.

## Local environment notes

Machine-specific quirks of the current dev machine (shell behavior, absolute tool paths, testing gotchas) live in **`ENVIRONMENT.md`** at the repo root — a gitignored, per-machine file. Read it if it exists; never commit it.

## Non-negotiable constraints

These rules come from the maintainer. Violating any of them requires explicit human approval **before** the change is made.

1. **Framework targets**
   - **Primary:** .NET Framework 4.8 (`net48`) — the runtime bundled with Windows by default. This target must always keep working.
   - **Secondary:** the two latest .NET LTS versions — as of Oct 2026 that is .NET 10 (LTS into late 2028) and .NET 8 (LTS only until **Nov 10, 2026**; candidate for removal from the matrix after 1.5.x). The csproj builds `net48;net8.0-windows;net10.0-windows`. Retargeting is a maintainer decision, never a side effect.
2. **Lightweight, target package size < 1 MB.** This is a background utility: minimize allocations, CPU, exception throwing, and avoid spawning external processes.
3. **No new dependencies** (NuGet packages, COM references, P/Invoke beyond what exists) without explicit human approval. The project currently has **zero** NuGet/package references: `Microsoft.CSharp` is a GAC assembly reference on net48 only, and on modern targets both `Microsoft.CSharp` and `System.Configuration.ConfigurationManager` are part of the WindowsDesktop shared framework. Keep it that way.
4. **Single executable** must remain the build output for both targets (framework-dependent single-file publish for the modern .NET target; plain single-exe build for net48). Only the maintainer can change this for the project as a whole.
5. **Cross-framework compatibility:** every change must compile and work on both targets. Prefer shared code; use `#if NETFRAMEWORK` / `#if NET` only when unavoidable.
6. **Tooling:** prefer MCP tools over shelling out to direct commands where possible (a workspace `codebase-memory-mcp` server is configured in `.zcode/config.json`).

## Building

Windows-only build. Solution: `Sources/NoSleep.sln` (single project `Sources/NoSleep/NoSleep.csproj`, `TargetFrameworks=net48;net8.0-windows`).

```sh
# Modern .NET targets (dotnet CLI)
cd Sources
dotnet build -f net8.0-windows -c Debug
dotnet build -f net8.0-windows -c Release   # triggers automatic single-file publish afterwards
dotnet build -f net10.0-windows -c Release  # same for .NET 10
dotnet publish -f net8.0-windows -c Release # explicit publish -> bin/Release/net8.0-windows/win-x64/publish

# .NET Framework 4.8 target (requires MSBuild from VS/Build Tools; dotnet build can't do single-exe here)
cd Sources
msbuild NoSleep/NoSleep.csproj /p:Configuration=Release /p:TargetFramework=net48 /restore
```

Output locations: `Sources/NoSleep/bin/<Config>/net48/` and `Sources/NoSleep/bin/<Config>/net8.0-windows/`. Full details, prerequisites and VS Code tasks: see [BUILD.md](BUILD.md).

There is no test suite; verify manually on both targets (Debug + Release). After release builds, sanity-check the published exe size stays under 1 MB.

## Codebase map

All code lives in `Sources/NoSleep/` (~650 LoC, no subprojects):

| File | Role |
|------|------|
| `Program.cs` | Entry point: `--lang` UI-language override, single-instance mutex, settings upgrade, runs `TrayIcon` as `ApplicationContext` |
| `TrayIcon.cs` | UI shell: tray icon, context menu (incl. the sleep-mode indicator and the S0-only items), theme watcher; forwards user intent into settings + `SleepController`, renders from `StateChanged` |
| `SleepController.cs` | State machine / single source of truth: user intent + app-watch result → `EffectivePreventSleep` → routed backend |
| `ISleepBackend.cs` | Backend contract (`Arm(SleepOptions)`/`Disarm()`, idempotent reconcile) + `SleepOptions` |
| `ExecutionStateBackend.cs` | Classic S1–S3 prevention: `SetThreadExecutionState` incl. away mode, timer re-arm (1.5.x behavior) |
| `ModernStandbyBackend.cs` | S0 prevention: ES base (no away mode) + lid override + activity simulation orchestration |
| `LidOverride.cs` | `LIDACTION` → "Do nothing" while armed; snapshot/restore with crash self-heal (`LidOverrideArmed`/`LidOverrideSnapshot` settings) |
| `InputSimulator.cs` | 1 px relative mouse moves via `SendInput` (burst + single moves) |
| `PowerNotifications.cs` | powrprof callback notifications (lid switch + suspend/resume); **delegate must stay statically rooted, registrations are never unregistered** (crash lessons) |
| `PowerPlatform.cs` | `GetPwrCapabilities`-based sleep-mode detection (registry `PlatformAoAc` is NOT usable — absent on real S0 hardware) |
| `Properties/Strings.cs` | Culture-aware string facade: languages discovered from embedded resources, English per-string fallback |
| `WinU.cs` | Win32 interop: `EXECUTION_STATE`/`SetThreadExecutionState`, `GetPwrCapabilities`, power-scheme read/write APIs, `QueryFullProcessImageName`-based process path lookup (exception-free) |
| `Tools.cs` | Autostart via Startup-folder shortcut, created through `WScript.Shell` COM late binding (works on both targets, no IWshRuntimeLibrary reference) |
| `AppsConfig.cs` | Watched-apps list load/save backed by `Properties.Settings` |
| `ConfigureAppsForm.*` | WinForms dialog to edit the watched-apps list |

Key mechanics to preserve:

- Sleep prevention = backend armed via the controller, re-arming `SetThreadExecutionState` every `RefreshIntervalMs` (default 10 s); `ES_CONTINUOUS` release is done with a lone `ES_CONTINUOUS` call.
- Sleep-mode routing: `PowerPlatform.SleepMode` (boot-constant) picks `ExecutionStateBackend` (classic/unknown) or `ModernStandbyBackend` (S0). S0-only menu items appear only on S0 machines; settings are a superset (all keys exist everywhere; unused ones are inert).
- Away mode is S3-only (documented OS behavior): the classic backend keeps it, the S0 backend must not use it.
- Lid override: explicit user setting, lid-only by default (trigger table in `LidOverride`); restore-on-disarm plus next-launch self-heal; skips restore if the user took the lid action back.
- Activity simulation: 0/1/2/3 (off / prevent / presence-zen / real movement). "Prevent" = burst + ~2 Hz sustain ~20 s on lid-close/suspend notifications (a bare burst can finish inside the ~107 ms pre-suspend window and get lost); "presence" = 1 Hz zero-delta input events (invisible — MouseJiggler's "zen" mechanic); "real movement" = 1 Hz paired opposite moves in one SendInput batch (visible jiggle that returns home). Both continuous modes are the only battery-surviving mechanisms (on battery S0 the OS terminates power requests 5 min after the sleep timeout; final, measured).
- Apps watching: `EffectivePreventSleep` is gated on the monitor's `AnyRunning` when watching is active (feature enabled + non-empty list). The scanner runs **only while the whole chain is on** (user enabled AND watching active) — a user-disabled NoSleep does not touch the process list. `AnyRunning` is `true` whenever the scanner is off (unknown = the optimistic/safe side; only a real scan may set it `false`): enabling therefore always shows the optimistic state first, then a ~200 ms one-shot reconcile scan corrects it ("heard you, checking…" feedback). Process matching tries full path first, falls back to process name (case-insensitive). Measured worst-case scan cost (full pass, no match, 287 processes, net48): ~9.5 ms median, ~33 ms max.
- Settings persist via `Properties.Settings` (`Settings.settings`); `UpgradeRequired` flag drives settings migration between versions.
- Icons are embedded resources and make up most of the binary size (~180 KB).
- Localization: UI strings live in `Properties/Resources*.resx` (English neutral, `ru`, `zh-CN`), embedded into the **main assembly** under explicit `LogicalName`s. Use underscore file names (`Resources_ru.resx`) — dot-culture names (`Resources.ru.resx`) make MSBuild build satellite DLLs that the single-exe artifact would never ship. Access strings via `NoSleep.Properties.Strings`; language auto-detects from `CurrentUICulture`, overridable per run with `--lang`. Adding a language = a new `Resources_xx.resx` file plus its csproj `EmbeddedResource` entry; picker, fallback and `--lang list` adapt automatically.

`tools/sleep-mode/` holds the sleep-mode investigation kit: measured findings (`S0-FINDINGS.md`, referenced as T01–T18 across the code), the rework plan, and standalone probe scripts. Raw test reports are intentionally NOT committed (they contained private environment details).

## Git workflow

- **Commits:** Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, …). The commit type is the input for judging the version bump (`feat` → minor, `fix` → patch, breaking change → major).
- **Versioning:** SemVer, defined in `NoSleep.csproj` (`<Version>` + `<FileVersion>`). Exactly **one** version bump lands on `master` per pull request, or per direct commit — intermediate commits within a PR don't each bump.
- **Large or external changes go through a PR to `master`:** spec-level changes, anything touching 3+ files, and contributions from other people.
- **Small changes may be committed directly to `master`:** 1–2 files with a small number of lines, and hotfixes.

## Releases & distribution contract

Releases are consumed externally by the Scoop `extras` bucket manifest (`nosleep.json`), which pins a strict publishing contract:

- Release tags are **plain SemVer without a `v` prefix** (e.g. `1.4.1`): scoop's `checkver: github` reads the latest release tag name as the version.
- Tags are **annotated**, and their message shapes the GitHub release automatically: the first line — `{version} — {short title}`, e.g. `1.5.0 — Localization & modern .NET builds` — becomes the release **name**, the message body becomes the release **notes** (curated changes + contributor credits), and GitHub's generated changelog is appended. The version must match the csproj.
- Each release carries the **primary asset named exactly `NoSleep.exe`** — the bare net48 build produced by the msbuild command in [BUILD.md](BUILD.md) — matching the manifest's autoupdate URL `releases/download/$version/NoSleep.exe`. Secondary assets for modern .NET (`NoSleep-net8.exe`, `NoSleep-net10.exe`) are added alongside and don't affect the manifest.
- Do not change the tag format, primary asset name, or packaging (zips, suffixes on the primary asset, extra primary assets) without coordinating a manifest update in the scoop extras bucket — otherwise autoupdate breaks for every scoop user.
- Release workflows must build **all** targets (net48 via VS MSBuild, modern .NET via `dotnet build -c Release`, which also runs the single-file publish) even though only the net48 exe is the scoop-facing asset, and should fail if any published exe exceeds the 1 MB size budget.

## Conventions

- Contributor-facing rules (dependencies, single-exe, lean code) are formalized in [CONTRIBUTING.md](CONTRIBUTING.md) — keep it in sync when constraints change.
- Main branch is `master`.
- User-facing strings live in `.resx` resources (English neutral, Russian, Simplified Chinese — see the localization note under Key mechanics); UI is WinForms designer-supported.
