using System;
using System.Threading;
using System.Windows.Forms;

namespace NoSleep
{
    /// <summary>
    /// Modern Standby (S0) prevention. Away mode is dropped (inapplicable on S0 - the OS
    /// terminates power requests on user-initiated sleep regardless, S0-FINDINGS.md), the
    /// base is ES_CONTINUOUS|ES_SYSTEM_REQUIRED re-armed on a timer, plus:
    /// - the optional lid-close action override (T05: with the override, lid close produces
    ///   no standby events at all), and
    /// - optional activity simulation (T06: input resets idle timers and instantly undoes
    ///   standby entry): "prevent" mode reacts to lid-close/suspend notifications with a
    ///   burst plus a ~2 Hz sustain for ~20 s (a bare burst can finish inside the ~107 ms
    ///   pre-suspend window and get lost, T14); "presence" mode jiggles continuously at 1 Hz
    ///   - the only mechanism that also survives the battery cliff (T12/T13: on battery the
    ///   OS terminates power requests 5 minutes after the sleep timeout, no request strategy
    ///   survives; documented in the README).
    /// </summary>
    internal sealed class ModernStandbyBackend : ISleepBackend
    {
        private const int SustainIntervalMs = 500;   // post-event sustain cadence (~2 Hz)
        private const int SustainTicks = 40;         // ~20 s of sustain
        private const int RealMoveDistance = 4;      // "real movement" jiggle size, px (MouseJiggler default)
        private const int MinJiggleIntervalMs = 100; // safety clamp for hand-edited settings

        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly System.Windows.Forms.Timer _activityTimer;
        private readonly LidOverride _lidOverride = new LidOverride();
        private readonly SynchronizationContext _uiContext; // captured on the UI thread (ctor runs there)
        private readonly int _jiggleIntervalMs;
        private readonly int _jiggleJitterPercent;
        private readonly Random _rnd = new Random();
        private PowerNotifications _notifications; // created on first need, kept for process lifetime
        private EXECUTION_STATE _mode = EXECUTION_STATE.None;
        private bool _lidOverrideFailed; // last arm attempt was refused by the power APIs (while wanted)
        private ActivityMode _activityMode; // only non-Off while ARMED
        private int _sustainRemaining;
        private int _flick = 1;

        /// <summary> True only when the override is wanted AND the power APIs refused it while
        /// prevention was active. NOT armed because prevention is inactive (user disabled) is a
        /// normal state and must never count as a failure - the setting simply applies on the
        /// next enable. </summary>
        public bool LidOverrideFailed => _lidOverrideFailed;

        public ModernStandbyBackend(int rearmIntervalMs, int jiggleIntervalMs, int jiggleJitterPercent)
        {
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
            // Continuous-mode cadence: randomized per tick so the events are not a perfectly
            // periodic pattern (periodicity is trackable; settings are user-tunable, read once
            // here on startup like every other setting).
            _jiggleIntervalMs = Math.Max(MinJiggleIntervalMs, jiggleIntervalMs);
            _jiggleJitterPercent = Math.Min(90, Math.Max(0, jiggleJitterPercent));
            _refreshTimer = new System.Windows.Forms.Timer { Interval = rearmIntervalMs };
            _refreshTimer.Tick += (s, e) => Refresh();
            _activityTimer = new System.Windows.Forms.Timer();
            _activityTimer.Tick += ActivityTimer_Tick;
        }

        public void Arm(SleepOptions options)
        {
            // NOTE: deliberately no "nothing changed" guard. The controller passes its live
            // options instance, so comparing against the previously armed reference compares
            // the object with itself and always says "unchanged" - the first version of this
            // method had exactly that aliasing bug: option toggles while armed (lid override,
            // activity mode, display flag) silently did nothing until a disable/enable cycle.
            // Arm is only called on state transitions (the periodic re-arm goes through
            // Refresh), and everything below is idempotent.
            _mode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED;
            if (options.KeepDisplayOn)
                _mode |= EXECUTION_STATE.ES_DISPLAY_REQUIRED;
            Refresh();
            _refreshTimer.Start();

            if (options.BlockLidSleep)
                _lidOverrideFailed = !_lidOverride.Arm(); // idempotent; false only on a real refusal
            else
            {
                _lidOverride.Disarm();
                _lidOverrideFailed = false; // not wanted - nothing to report
            }

            UpdateActivity(options.Activity);
        }

        public void Disarm()
        {
            _refreshTimer.Stop();
            if (_mode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS))
                WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            _mode = EXECUTION_STATE.None;
            _lidOverride.Disarm();
            _lidOverrideFailed = false; // inactive prevention is not a failure; retried on next Arm
            _activityTimer.Stop();
            _activityMode = ActivityMode.Off;
        }

        private void UpdateActivity(ActivityMode mode)
        {
            _activityMode = mode;
            _sustainRemaining = 0;
            if (mode == ActivityMode.SimulatePresence || mode == ActivityMode.MoveMouse)
            {
                // Continuous presence simulation at the (jittered) jiggle cadence.
                EnsureNotifications();
                _activityTimer.Interval = NextJiggleInterval();
                _activityTimer.Start();
            }
            else
            {
                // Prevent-mode idles between events; Off does nothing at all.
                _activityTimer.Stop();
                if (mode == ActivityMode.PreventSleepAttempts)
                    EnsureNotifications();
            }
        }

        /// <summary> Uniformly randomized interval within +/- jitter around the base -
        /// recomputed for every tick so no repeating pattern emerges. </summary>
        private int NextJiggleInterval()
        {
            if (_jiggleJitterPercent <= 0)
                return _jiggleIntervalMs;
            int spread = Math.Max(1, (int)((long)_jiggleIntervalMs * _jiggleJitterPercent / 100));
            return Math.Max(MinJiggleIntervalMs, _jiggleIntervalMs - spread + _rnd.Next(2 * spread + 1));
        }

        private void EnsureNotifications()
        {
            if (_notifications != null)
                return;
            _notifications = new PowerNotifications(); // created on the UI thread (Arm runs there)
            // Immediate hooks run on the callback thread: the burst is time-critical.
            _notifications.LidClosedImmediate += (s, e) => ReactToSleepAttempt();
            _notifications.SuspendInitiatedImmediate += (s, e) => ReactToSleepAttempt();
        }

        private void ReactToSleepAttempt()
        {
            if (_activityMode != ActivityMode.PreventSleepAttempts)
                return;
            InputSimulator.Burst(); // now, on the callback thread, inside/near the pre-suspend window
            // Sustain from the UI thread (the callback thread must not own timer state).
            _uiContext.Post(s => StartSustain(), null);
        }

        private void StartSustain()
        {
            if (_activityMode != ActivityMode.PreventSleepAttempts)
                return;
            _sustainRemaining = SustainTicks;
            _activityTimer.Interval = SustainIntervalMs;
            _activityTimer.Start();
        }

        private void ActivityTimer_Tick(object sender, EventArgs e)
        {
            switch (_activityMode)
            {
                case ActivityMode.PreventSleepAttempts: // sustain phase: single alternating moves, budgeted
                    InputSimulator.Move(_flick, 0);
                    _flick = -_flick;
                    if (--_sustainRemaining <= 0)
                        _activityTimer.Stop();
                    break;

                case ActivityMode.SimulatePresence: // invisible activity, pointer never moves
                    InputSimulator.ZenMove();
                    _activityTimer.Interval = NextJiggleInterval();
                    break;

                case ActivityMode.MoveMouse: // visible jiggle that returns home in the same batch
                    InputSimulator.MoveAndReturn(RealMoveDistance);
                    _activityTimer.Interval = NextJiggleInterval();
                    break;
            }
        }

        private void Refresh()
        {
            if (_mode != EXECUTION_STATE.None)
                WinU.SetThreadExecutionState(_mode);
        }

        public void Dispose()
        {
            Disarm();
            _refreshTimer.Dispose();
            _activityTimer.Dispose();
        }
    }
}
