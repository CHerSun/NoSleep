using System;
using System.Windows.Forms;
using NoSleep.Properties;

namespace NoSleep
{
    /// <summary>
    /// Classic S1–S3 prevention: SetThreadExecutionState with away mode (away mode is what
    /// keeps the system running through user-initiated sleep on S3 machines), re-armed on a
    /// timer because some systems drop long-lived requests. Behavior identical to NoSleep 1.5.x,
    /// plus the lid-close override (the power-scheme write is sleep-mode-agnostic; on classic
    /// machines it is a deterministic alternative to away mode's "appears asleep" state - some
    /// firmware enforces away mode poorly) and the continuous presence-simulation modes. The
    /// event-driven "prevent sleep attempts" mode is S0-only and inert here: away mode already
    /// covers every classic sleep trigger (S0-FINDINGS.md behavior matrix).
    /// </summary>
    internal sealed class ExecutionStateBackend : ISleepBackend
    {
        private readonly Timer _refreshTimer;
        private readonly LidOverride _lidOverride = new LidOverride();
        private readonly JiggleScheduler _jiggle;
        private EXECUTION_STATE _mode = EXECUTION_STATE.None;
        private bool _lidOverrideFailed;

        /// <summary> True only when the lid override is wanted AND the power APIs refused it
        /// while prevention was active; inactive prevention is not a failure (the setting
        /// applies on the next enable) - same contract as the Modern Standby backend. </summary>
        public bool LidOverrideFailed => _lidOverrideFailed;

        public ExecutionStateBackend(int rearmIntervalMs, int jiggleIntervalMs, int jiggleJitterPercent)
        {
            _refreshTimer = new Timer { Interval = rearmIntervalMs };
            _refreshTimer.Tick += (s, e) => Refresh();
            _jiggle = new JiggleScheduler(jiggleIntervalMs, jiggleJitterPercent);
        }

        public void Arm(SleepOptions options)
        {
            // NOTE: no "nothing changed" guard on purpose - the controller passes its live
            // options instance (see the same note in ModernStandbyBackend.Arm); everything
            // below is idempotent and Arm only runs on state transitions.
            _mode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    EXECUTION_STATE.ES_AWAYMODE_REQUIRED;
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
            _jiggle.SetActivity(options.Activity); // continuous modes only; other modes stop it
        }

        public void Disarm()
        {
            _refreshTimer.Stop();
            // A continuous state is released by calling with a lone ES_CONTINUOUS (see SetThreadExecutionState docs).
            if (_mode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS))
                WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            _mode = EXECUTION_STATE.None;
            _lidOverride.Disarm();
            _lidOverrideFailed = false; // inactive prevention is not a failure; retried on next Arm
            _jiggle.SetActivity(ActivityMode.Off);
        }

        private void Refresh()
        {
            if (_mode != EXECUTION_STATE.None)
                WinU.SetThreadExecutionState(_mode);
        }

        public void Dispose()
        {
            _refreshTimer.Dispose();
            _jiggle.Dispose();
        }
    }
}
