# NoSleep sleep-mode investigation — findings & test log (issue #8)

Generated: 2026-10-21. Everything below is **empirical** — measured on the hardware
listed, with the scripts in this folder (see §3). Numbered findings (T01…) are
referenced by the rework plan (`S0-REWORK-PLAN.md`).

## 1. Test hardware

| Machine | Role | OS / build | Sleep support |
|---|---|---|---|
| Desktop (MSI MS-7821) | control, dev | Windows 10 Enterprise 19045 x64 | S3 only, no lid, no battery |
| Laptop (Lenovo 21HESEDE00) | target | Windows 11 Pro **26200** | **S0 Modern Standby only** (network connected), lid, battery; corporate environment; Russian locale |

Raw reports: `S0-Report-user-AC.txt`, `S0-Report-user-DC.txt`, `S0-Report-admin-DC.txt`
(pass 1), `S0-Report-user-v2.txt` (pass 2, battery), `S0-Report-user-v4.txt` (3d on AC),
`S0-Report-user-v5.txt` (3d on battery), `S0-Report-user-v6.txt`, `lid-notify-test-output.txt`,
`cliff\*` + `s0-cliff-report-v2.txt`.

## 2. Documented baseline (Microsoft, used as hypotheses — all confirmed where tested)

- [PowerSetRequest remarks](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-powersetrequest):
  `ES_AWAYMODE_REQUIRED`/`PowerRequestAwayModeRequired` is **only applicable on Traditional Sleep (S3)** systems.
- Same page: power requests are **terminated on user-initiated sleep** (power button, lid close, Start → Sleep) — the only exception being away mode on S3.
- Same page: **on Modern Standby + battery (DC), system/execution power requests are terminated 5 minutes after the system sleep timeout expires** (confirmed: T08/T10/T13).
- [System power states](https://learn.microsoft.com/en-us/windows/win32/power/system-power-states); [power setting GUIDs](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids).

## 3. Tools (this folder)

| File | Purpose |
|---|---|
| `s0-diag.ps1` | big diagnostic suite (phases 1–6); PS 5.1 + pwsh 7; C# via `Add-Type` (C#5-compatible) |
| `lid-notify-test.ps1` | standalone probe: do lid/display/suspend notifications arrive (window path vs powrprof callbacks) |
| `s0-battery-cliff-test.ps1` | standalone probe: one-shot pings vs cycled claims vs the battery cliff |

Each standalone script prints `size + sha256` as its first line — verify before running
(two transfer corruptions happened during the campaign: §8).

## 4. Test log

**T01 — Registry detection (`PlatformAoAc`) — UNSUITABLE.**
Conditions: both machines. Result: value absent on the desktop *and* on the true-S0
laptop. Conclusion: registry-based detection misclassifies real S0 hardware as classic.

**T02 — `GetPwrCapabilities` detection — VALIDATED.**
Byte offsets (WinNT.h `SYSTEM_POWER_CAPABILITIES` head): LidPresent=2, SystemS1..S5=3..7,
HiberFile=8, FastSystemS4=17, Hiberboot=18, WakeAlert=19, **AoAc=20**, HiberFileType=22,
AoAcConnectivity=23, SystemBatteries=24. Results: desktop → S3=yes/AoAc=no/Lid=no;
laptop → S3=no/**AoAc=yes**/Lid=yes — matches `powercfg -a` on both. Quirk: offset 24
(SystemBatteriesPresent) reads "no" on the battery laptop — never rely on it.

**T03 — Lid-action write privileges — NO ADMIN NEEDED.**
`PowerRead/WriteAC/DCValueIndex` + `PowerSetActiveScheme` on `LIDACTION` succeed as a
normal user on Win10 19045 and Win11 26200 (status 0 everywhere, verified restore).
Note: Win11 hides the setting from `powercfg -q` output — the API still reaches it.

**T04 — Lid close, baseline (action = Sleep).** 4 runs (AC/DC × user/admin):
Kernel-Power **506 (enter Modern Standby) ~5–7 s after close**, 507 (exit) on reopen;
DC runs add Id=172 pairs; 566 accompanies screen-off transitions. Apps get suspended
(music stops) — the issue #8 bug.

**T05 — Lid close with `LIDACTION=0` override — ZERO standby events.** Same 4 runs:
no 42/506/507/566 at all while the lid is closed; system keeps running; restore verified.
This is the primary S0 feature mechanism.

**T06 — Continuous mouse jiggle (1 px @ 1 Hz, SendInput) — defeats any sleep entry.**
2 runs: 506+507 **in the same second** (standby entry instantly undone by wake-on-input);
jiggle log never pauses → the jiggling process is never meaningfully suspended. Also
covers Start→Sleep/power button. Side effect: user appears present (chats, screen/lock
idle timers).

**T07 — Idle sleep on AC, screen turning off, persistent `ES_SYSTEM_REQUIRED`.**
10 min, both timeouts 2 min, parent + lock-free child: **entire system keeps running**
(child included) with the screen off; 506 logged at 2 min is a phase marker, not
suspension. No cliff on AC.

**T08 — Idle sleep on battery, persistent claim (held once).** Screen off at 2 min →
system-wide running until **timeout+5 min**, then the documented DC termination fires
(507+506 transition), the machine truly sleeps; self-maintenance (172 ticks) woke it
~3 min later. A one-shot claim buys exactly ~5 minutes.

**T09 — Control (no request, screen off).** Machine sleeps at ~the sleep timeout
(153 s heartbeat gap, 506) — validates the method; earlier "stayed awake" results
obtained with the screen left on were artifacts (see §8 pitfalls).

**T10 — Per-app question + re-arm on battery.** Parent (claim + re-arm every 5 s) +
lock-free child: minutes 2–7 **system-wide running (not per-app)** — the request exempts
everyone during its lifetime. At timeout+5 min the sweep suspends **both**; the re-arm
never happens because DAM freezes the arming process itself. (The kit's first printed
verdict for this run was wrong — tail-gap analysis bug, fixed; see §8.)

**T11 — `PowerRequestExecutionRequired` (buho-style).** Behaves identically to
`ES_SYSTEM_REQUIRED` in every tested scenario, including the same battery sweep →
redundant; not used by the app (smaller interop surface).

**T12 — Cliff variant A: one-shot pings (`ES_SYSTEM_REQUIRED` without `ES_CONTINUOUS`,
every 5 s).** Battery, both timeouts 2 min: standby **right at screen-off (~2:15), no
grace at all**. Idle-timer resets do not gate Modern Standby entry; the 5-minute window
in T08 was purely the persistent request working until its termination.

**T13 — Cliff variant B: cycled persistent claims (release + fresh re-claim every 5 s).**
Battery: identical to T08 — everything suspends at **exactly timeout+5 min**. The
termination is an **age-independent sweep** (a genuinely new request does not survive);
repeated identical claims are not merely no-ops. **Conclusion: on battery S0 no
`SetThreadExecutionState` strategy survives past sleep-timeout+5 min. The cliff is final.**

**T14 — Notifications.** Both mechanisms work on the laptop (the earlier "Win11 26200
ignores window notifications" hypothesis was a stale-script artifact, §8):
- window path: `RegisterPowerSettingNotification` on a hidden window;
- **callback path (chosen for the app)**: `PowerSettingRegisterNotification` /
  `PowerRegisterSuspendResumeNotification` (powrprof), `DEVICE_NOTIFY_CALLBACK` = flags **2**.
Measured on lid close: `[cb] LID_CLOSED` first, window +17 ms, `SESSION_DISPLAY_OFF`
both, `APMSUSPEND` at **+107 ms**; on open: `LID_OPEN`, `APMRESUME(18)` (=RESUMEAUTOMATIC)
and `APMRESUME(7)`, display on. Initial current-state notifications arrive immediately
on registration. The 107 ms pre-suspend window is why the app's event-driven jiggle must
be **burst + sustained ~2 Hz for ~15–20 s** (input during the entry transition produces
the same-second wake, per T06), not a single short burst.

**T15 — `SC_MONITORPOWER` broadcast — BANNED on Modern Standby.** A manual screen-off
broadcast itself triggered standby entry on the laptop (506/507 storm every 1–4 s for
~90 s). Do not use it for the "screen off, keep running" goal.

**T16 — Sleep/power button actions.** `SBUTTONACTION`/`PBUTTONACTION` read status 0x2
(setting not materialized) on **both** machines → overriding them is moot on this
hardware; the app's override table defaults to LIDACTION only.

**T17 — powercfg privileges/output.** `-requests` requires admin (both OSes); `-a`
works unelevated; output is localized (ru) — never parse it; use the APIs.

**T18 — Validated GUIDs** (several first-draft values were wrong; these are verified):
`LIDACTION` 5ca83367-6e45-459f-a27b-476b1d01c936 · `SBUTTONACTION` 961cc777-2547-4f9d-8174-7d86181b8a7a ·
`PBUTTONACTION` 7648efa3-dd33-4e3e-b566-50f929386280 · `SUB_BUTTONS` 4f971e89-eebd-4455-a8de-9e59040e7347 ·
`SUB_SLEEP`/`STANDBYIDLE` 238c9fa8-…/29f6c1db-… · `SUB_VIDEO`/`VIDEOIDLE` 7516b95f-…/3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e ·
`GUID_LIDSWITCH_STATE_CHANGE` BA3E0F4D-B817-4094-A2D1-D56379E6A0F3 (data 0=closed/1=open) ·
`GUID_SESSION_DISPLAY_STATUS` 2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5 (docs-recommended for user apps) ·
`GUID_CONSOLE_DISPLAY_STATE` 6FE69556-704A-47A0-8F24-C28D936FDA47 · `GUID_MONITOR_POWER_ON` 02731015-… ·
`GUID_ACDC_POWER_SOURCE` 5D3E9A59-….

## 5. Event vocabulary (as observed, Kernel-Power provider)

- **506** — enter Modern Standby (logged even when apps keep running — phase marker).
- **507** — exit Modern Standby.
- **566** — screen-off/standby phase companion event.
- **172** — DC-only, MS maintenance/phase ticks (bracketed the T08 standby period at 3-min spacing).
- 42/107 — classic sleep/wake (seen on the S3 desktop).

## 6. Consolidated behavior matrix (the spec the app implements)

| Scenario | S3 (S1–S3) | S0 on AC | S0 on battery |
|---|---|---|---|
| Idle sleep (screen on) | blocked by requests | blocked | blocked (lit screen gates MS entry) |
| Idle sleep (screen off) | blocked by requests | blocked, system-wide, unlimited (T07) | blocked for **sleep-timeout + 5 min**, then everything sleeps (T08/T10/T12/T13) — unfixable by any ES strategy; continuous jiggle covers it (T06) |
| Lid close, override ON | n/a (S3 keeps running via away mode) | nothing happens; music plays (T05) | same for 5 min past timeout, then sleeps |
| Lid close / Start→Sleep, no override | away mode: appears asleep, keeps running | standby (T04); only jiggle defeats it (T06) | same |
| Detection | `GetPwrCapabilities` once per boot (T02) | | |

## 7. Design decisions derived

1. Detection: `GetPwrCapabilities` only; failure → classic fallback + "unknown" indicator (T01/T02).
2. S3 backend: today's behavior unchanged (away mode included).
3. S0 backend: `ES_CONTINUOUS|ES_SYSTEM_REQUIRED` (+`ES_DISPLAY_REQUIRED` per existing
   keep-screen-on), re-armed by the existing timer; **away mode dropped on S0** (T11 context; inapplicable).
4. Lid override: explicit setting; in-code table of `SUB_BUTTONS` entries, **default =
   lid only** (T16); unelevated (T03); snapshot+restore+crash self-heal (T05 mechanics).
5. Jiggle: explicit setting, two modes — "prevent" (event-driven: callback lid+suspend
   notifications, burst + ~2 Hz sustain 15–20 s; brief presence flicker only) and
   "simulate presence" (continuous 1 Hz; full side effects, documented).
6. Mode indicator: non-interactive top tray-menu item ("Classic (S3)" / "Modern Standby (S0)").
7. `ExecutionRequired`, `SC_MONITORPOWER`, registry detection, sleep/power-button
   overrides: all excluded (T11, T15, T01, T16).
8. Docs duty: manual "lid = Do nothing" alternative; jiggle side effects; battery cliff;
   bag-overheat honesty (inherent to the product since S3 away-mode days).

## 8. Engineering pitfalls discovered during the campaign (do not repeat)

- **Lit-screen artifact:** with the display never timing out, Modern Standby never even
  attempts entry — every idle test MUST shorten the display-off timeout too.
- **Tail-gap blind spot:** a process suspended during the last loop iteration leaves no
  gap in its tick log unless it resumes — count the time from the last tick to cleanup.
- **Suspension-without-resume is invisible** in a child's log (T13 child looked alive).
- **PS 5.1 hex literals ≥ 0x80000000 parse as negative Int32** → UInt32 conversion fails;
  use `[uint32]'2147483649'`-style constants.
- **GC'd delegate crash (0x80131623):** OS-held callback delegates (power notifications)
  MUST be statically rooted for the process lifetime; unregistering while a callback is
  in flight also crashed. Run each probe in a fresh PowerShell window.
- **Stale/hand-assembled transfers** produced two rounds of false "notifications dead"
  conclusions — self-verifying banners (size+hash) on every standalone script.
- `powercfg -requests` needs admin; `powercfg` output is localized — use the APIs.
