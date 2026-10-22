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
        private const int PresenceIntervalMs = 1000; // "simulate presence" cadence (T06: 1 Hz)
        private const int SustainIntervalMs = 500;   // post-event sustain cadence (~2 Hz)
        private const int SustainTicks = 40;         // ~20 s of sustain

        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly System.Windows.Forms.Timer _activityTimer;
        private readonly LidOverride _lidOverride = new LidOverride();
        private readonly SynchronizationContext _uiContext; // captured on the UI thread (ctor runs there)
        private PowerNotifications _notifications; // created on first need, kept for process lifetime
        private EXECUTION_STATE _mode = EXECUTION_STATE.None;
        private int _activityMode;          // 0 off / 1 prevent / 2 presence - while ARMED
        private int _sustainRemaining;
        private int _flick = 1;

        /// <summary> True while the lid override is actually in effect (arm requested AND succeeded). </summary>
        public bool LidOverrideEffective => _lidOverride.IsArmed;

        public ModernStandbyBackend(int rearmIntervalMs)
        {
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
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
                _lidOverride.Arm(); // idempotent; failure leaves LidOverrideEffective = false (UI shows it)
            else
                _lidOverride.Disarm();

            UpdateActivity(options.SimulateActivity);
        }

        public void Disarm()
        {
            _refreshTimer.Stop();
            if (_mode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS))
                WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            _mode = EXECUTION_STATE.None;
            _lidOverride.Disarm();
            _activityTimer.Stop();
            _activityMode = 0;
        }

        private void UpdateActivity(int mode)
        {
            _activityMode = mode;
            _sustainRemaining = 0;
            if (mode == 2)
            {
                EnsureNotifications();
                _activityTimer.Interval = PresenceIntervalMs;
                _activityTimer.Start();
            }
            else
            {
                // Mode 1 idles between events; mode 0 does nothing at all.
                _activityTimer.Stop();
                if (mode == 1)
                    EnsureNotifications();
            }
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
            if (_activityMode != 1)
                return;
            InputSimulator.Burst(); // now, on the callback thread, inside/near the pre-suspend window
            // Sustain from the UI thread (the callback thread must not own timer state).
            _uiContext.Post(s => StartSustain(), null);
        }

        private void StartSustain()
        {
            if (_activityMode != 1)
                return;
            _sustainRemaining = SustainTicks;
            _activityTimer.Interval = SustainIntervalMs;
            _activityTimer.Start();
        }

        private void ActivityTimer_Tick(object sender, EventArgs e)
        {
            InputSimulator.Move(_flick, 0);
            _flick = -_flick;
            if (_activityMode == 1)
            {
                // Sustain phase of "prevent" mode: stop after the budget is spent.
                if (--_sustainRemaining <= 0)
                    _activityTimer.Stop();
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
