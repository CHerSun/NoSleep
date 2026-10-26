# NoSleep 1.6.0 rework plan — sleep-mode routing + domain separation

Branch: `feat/s0-modern-standby` off `master`. **No merge until the maintainer explicitly
confirms.** Evidence for every behavioral claim: `S0-FINDINGS.md` (T-numbers).

## 1. Goals

1. **Issue #8**: prevent sleep on Modern Standby (S0) machines as reliably as the
   platform allows, and communicate the limits honestly (mode indicator + docs).
2. **Architecture**: separate the app into domains (UI / state / prevention backends /
   app monitoring / platform detection), route sleep prevention by detected sleep mode,
   single source of truth for state — removing the known coupling smells.
3. Stay within hard constraints: no new NuGet/COM dependencies, single exe, < 1 MB,
   net48 primary + .NET 8/10, no admin requirement (everything below is proven
   unelevated, T03).

Approved interop additions (maintainer, 2026-10-20): powrprof (`GetPwrCapabilities`,
`PowerRead/WriteAC/DCValueIndex`, `PowerSetActiveScheme`, `PowerGetActiveScheme`,
`PowerSettingRegisterNotification`, `PowerRegisterSuspendResumeNotification`) and
`user32!SendInput`. Excluded after testing: `PowerCreateRequest`/`ExecutionRequired`
(T11), `SC_MONITORPOWER` (T15), registry detection (T01).

## 2. Target architecture

```
Program.cs            entry point (unchanged responsibilities)
PowerPlatform.cs      sleep-mode detection, once per boot, cached (T02)
SleepController.cs    the state machine - single source of truth
ISleepBackend.cs      interface: Arm(options)/Disarm()/Capabilities + StateChanged
ExecutionStateBackend.cs   classic S1–S3: current flags incl. away mode (unchanged behavior)
ModernStandbyBackend.cs     S0: ES base + lid override + jiggle orchestration
LidOverride.cs        SUB_BUTTONS snapshot/restore + crash self-heal (used by MS backend)
InputSimulator.cs     SendInput jiggle: burst / sustained / continuous
PowerNotifications.cs powrprof callbacks (lid switch + suspend/resume), static-rooted
AppMonitor.cs         watched-apps scan timer + caches (extracted from TrayIcon)
AppsConfig.cs         persistence (existing + the SetEquals/names fix)
TrayIcon.cs           UI shell only: menu construction (THE one platform-projection
                      point), icons, theme watcher, click→command forwarding
ConfigureAppsForm.*   dialog (minor: stop mutating state from the render helper)
```

### SleepController (state machine)

- Inputs: `UserEnabled`, `WatchingActive` (= watching enabled AND list non-empty),
  `AnyWatchedAppRunning`, `KeepScreenOn`, backend settings.
- Output: `EffectivePreventSleep` (formula unchanged: enabled && (!watching || anyRunning)).
- One `Apply()` transition point; `Apply` arms/disarms the routed backend; every state
  change flows through it; UI refreshes from a single `StateChanged` event. This deletes
  today's scattered recomputation, the duplicated disarm logic, the fake
  `Click_DisplayRequired(null,null)` initialization, the UI-as-source-of-truth pattern,
  and the `_anyWatchedAppRunning = true` startup lie (replaced by one synchronous scan
  at startup — cheap, once).

### Settings (superset — all keys exist on every machine; unused ones are inert)

Existing keys unchanged. New:

| Key | Type | Meaning |
|---|---|---|
| `BlockLidSleep` | bool | S0 lid-close override setting (shown only when S0 + lid present) |
| `JiggleMode` | int | 0 = off, 1 = prevent (event-driven, default when used), 2 = simulate presence |
| `LidOverrideArmed` | bool | crash self-heal flag: override was applied and not restored |
| `LidOverrideSnapshot` | string | `schemeGuid|lidAc|lidDc` originals for self-heal |

Settings `Upgrade()` migrates automatically (existing `UpgradeRequired` mechanism).

### Backends

**ExecutionStateBackend (S1–S3 / unknown-fallback)** — exactly today's behavior:
`ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED` (+`ES_DISPLAY_REQUIRED`
per keep-screen-on), re-armed by the existing refresh timer, released with a lone
`ES_CONTINUOUS`. Byte-for-byte the 1.5.x contract.

**ModernStandbyBackend (S0)**

- ES base: `ES_CONTINUOUS | ES_SYSTEM_REQUIRED` (+display flag per keep-screen-on),
  re-armed by the existing timer; **no away mode** (T11/inapplicable).
- Lid override (`BlockLidSleep`, default off): on arm — read `LIDACTION` AC/DC, store
  originals (memory + settings snapshot + `LidOverrideArmed=true`), write 0/0, apply
  scheme (T03/T05). On disarm/exit — restore originals, apply, clear the flag. On
  startup — if `LidOverrideArmed`, restore the snapshot first (crash self-heal), then
  re-arm if still appropriate. If the active scheme GUID no longer matches the snapshot,
  clear the snapshot (log via debug trace) and re-arm fresh.
- Trigger table (in-code, one line to extend): default = `LIDACTION` only. `SBUTTONACTION`
  / `PBUTTONACTION` entries present but disabled — unmaterialized on all test hardware (T16).
- Jiggle (`JiggleMode`):
  - *prevent (1)*: `PowerNotifications` registers lid-switch + suspend/resume callbacks
    (flags=2, T14). On lid-closed/suspend: 10×1 px burst (10 ms apart) then ~2 Hz
    sustain for 20 s (the 107 ms pre-suspend window makes a bare burst unsafe, T14).
    After a wake, the ordinary ES re-arm re-establishes requests.
  - *presence (2)*: 1 px @ 1 Hz, always while armed (T06) — the only indefinite battery
    mechanism (T13).
  - Both fire from the backend's arm state; mode changes take effect on next Apply.

**PowerNotifications** — powrprof callbacks only (no window). Delegates **statically
rooted** for process lifetime; events marshaled to the UI thread via
`SynchronizationContext`/`Control.BeginInvoke` (GC-delegate crash lesson, §8 of findings).

## 3. Tray menu (the single platform-projection point)

```
┌─────────────────────────────────────┐
│ Sleep mode: Classic (S3)            │   non-interactive label (ToolStripControlHost)
├─────────────────────────────────────┤
│ Enabled                        ☐/☑  │
│ Keep screen on                 ☐/☑  │
│ Block sleep on lid close   ☐/☑      │   S0 + lid only
│ Simulate activity: off/on sleep attempts/continuous   ← S0 only, 3-state
├─────────────────────────────────────┤
│ Configure apps …                    │
│ Remember enabled state         ☐/☑  │
│ Autostart                      ☐/☑  │
├─────────────────────────────────────┤
│ Close                               │
└─────────────────────────────────────┘
```

Platform is boot-constant → the menu is built once; the S0-only items exist only when
`PowerPlatform` says S0 (+lid for the override). Indicator tooltip carries the honest
caveat on S0 battery (the cliff, T13) and what is/isn't preventable. New strings ×
en/ru/zh-CN via the existing `Strings`/resx mechanism; RU text polished by the maintainer
per the usual process. Tray icon set unchanged.

## 4. Bundled simplifications (behavior-preserving, from the code review)

1. `AppsConfig.Save`: replace the dead `==`-on-HashSets check with `SetEquals` on
   **paths and names** (names must be included or renames stop persisting).
2. Remove UI-as-state: handlers write settings; one `RefreshMenuFromState()` renders.
3. Merge the duplicated disarm logic (`ApplySleepPrevention` vs `DisarmExecutionState`).
4. Compose ES flags at `Arm()` from `(KeepScreenOn, platform)`; delete the
   `EnableFlag/DisableFlag` mutation paths.
5. One synchronous app scan at startup (removes the `_anyWatchedAppRunning=true` lie).
6. `ConfigureAppsForm.UpdateEnabledState` renders only; the toggle writes state.

## 5. Commit sequence (each reviewable on its own)

1. `refactor: split TrayIcon into domains (no behavior change)` — controller, backends
   (S3 only at this point), AppMonitor, UI shell, simplifications §4. S3 smoke = 1.5.1 parity.
2. `feat: sleep-mode detection and tray indicator` — `PowerPlatform` + menu label + strings ×3.
3. `feat: Modern Standby backend - ES base + lid-close override` — backend routing +
   `LidOverride` + self-heal + `BlockLidSleep` setting + menu item.
4. `feat: activity simulation (prevent + presence modes)` — `InputSimulator` +
   `PowerNotifications` + `JiggleMode` + menu item.
5. `docs: sleep modes, settings and honest limits` — README en/ru/zh-CN sections +
   AGENTS.md codebase-map refresh + move `s0-test-kit` findings into the repo (see §7).
6. `chore: bump version to 1.6.0` — single bump for the PR.

## 6. Verification

- Builds: `msbuild … /p:TargetFramework=net48` + `dotnet build -c Release` for
  net8.0-windows and net10.0-windows; all published exes < 1 MB.
- S3 desktop checklist: indicator shows "Classic (S3)"; no menu changes vs 1.5.1;
  enable/disable/watching/keep-screen-on cycles; away-mode behavior on forced sleep
  unchanged (event-log check).
- Laptop checklist (real build, replaces further PS probes): indicator "Modern Standby
  (S0)"; lid override on → close lid → no 506/507, music keeps playing (AC), battery =
  5-min window then sleep (expected, documented); override off → 506 within ~7 s;
  prevent-mode jiggle → lid close → same-second 506/507; presence mode → no standby at
  all; crash self-heal (kill process with override armed → relaunch → LIDACTION restored);
  disable/exit → LIDACTION restored immediately; `powercfg -q` (API-side) sanity.

## 7. Open decisions for the maintainer

1. Include `s0-test-kit/` (scripts + `S0-FINDINGS.md` + this plan) in the PR as
   `tools/sleep-mode/`? (Recommended: yes — it is the reproduction kit for issue #8.)
2. Settings superset keys as listed in §2 — OK?
3. Indicator wording: "Sleep mode: Classic (S3)" / "Sleep mode: Modern Standby (S0)"?

## 8. Risks / notes

- Corporate machines may differ from the test laptop; all mechanisms degrade to "classic
  behavior + honest indicator" (detection failure → fallback, override failure → setting
  ignored with a disabled menu item rather than a broken promise).
- `powercfg -requests` (admin-only, T17) stays out of the app.
- Single-instance mutex, `--lang`, settings migration, scoop/release contract untouched.
- Estimated diff: ~650 LoC reworked + ~350–450 new LoC across the new files; binary
  growth only from new strings (icons unchanged), well within the 1 MB budget.
