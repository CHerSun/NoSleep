using System;
using System.Windows.Forms;

namespace NoSleep
{
    /// <summary>
    /// Modern Standby (S0) prevention. Away mode is dropped (inapplicable on S0 - the OS
    /// terminates power requests on user-initiated sleep regardless, S0-FINDINGS.md), the
    /// base is ES_CONTINUOUS|ES_SYSTEM_REQUIRED re-armed on a timer, plus the optional
    /// lid-close action override (T05: with the override, lid close produces no standby
    /// events at all). Known platform limit, documented in the README: on battery the OS
    /// terminates power requests 5 minutes after the sleep timeout - no request strategy
    /// survives that (T12/T13); continuous activity simulation is the only indefinite
    /// battery mechanism and lives in commit 4.
    /// </summary>
    internal sealed class ModernStandbyBackend : ISleepBackend
    {
        private readonly Timer _refreshTimer;
        private readonly LidOverride _lidOverride = new LidOverride();
        private SleepOptions _armed;
        private EXECUTION_STATE _mode = EXECUTION_STATE.None;

        /// <summary> True while the lid override is actually in effect (arm requested AND succeeded). </summary>
        public bool LidOverrideEffective => _lidOverride.IsArmed;

        public ModernStandbyBackend(int rearmIntervalMs)
        {
            _refreshTimer = new Timer { Interval = rearmIntervalMs };
            _refreshTimer.Tick += (s, e) => Refresh();
        }

        public void Arm(SleepOptions options)
        {
            if (_armed != null && _armed.SameAs(options))
                return; // reconcile: nothing changed

            _mode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED;
            if (options.KeepDisplayOn)
                _mode |= EXECUTION_STATE.ES_DISPLAY_REQUIRED;
            Refresh();
            _refreshTimer.Start();

            if (options.BlockLidSleep)
                _lidOverride.Arm(); // idempotent; failure leaves LidOverrideEffective = false (UI shows it)
            else
                _lidOverride.Disarm();

            _armed = options;
        }

        public void Disarm()
        {
            _refreshTimer.Stop();
            if (_mode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS))
                WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            _mode = EXECUTION_STATE.None;
            _lidOverride.Disarm();
            _armed = null;
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
        }
    }
}
