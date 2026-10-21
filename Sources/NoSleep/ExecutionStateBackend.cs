using System;
using System.Windows.Forms;
using NoSleep.Properties;

namespace NoSleep
{
    /// <summary>
    /// Classic S1–S3 prevention: SetThreadExecutionState with away mode (away mode is what
    /// keeps the system running through user-initiated sleep on S3 machines), re-armed on a
    /// timer because some systems drop long-lived requests. Behavior identical to NoSleep 1.5.x.
    /// </summary>
    internal sealed class ExecutionStateBackend : ISleepBackend
    {
        private readonly Timer _refreshTimer;
        private EXECUTION_STATE _mode = EXECUTION_STATE.None;

        public ExecutionStateBackend(int rearmIntervalMs)
        {
            _refreshTimer = new Timer { Interval = rearmIntervalMs };
            _refreshTimer.Tick += (s, e) => Refresh();
        }

        public void Arm(bool keepDisplayOn)
        {
            _mode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    EXECUTION_STATE.ES_AWAYMODE_REQUIRED;
            if (keepDisplayOn)
                _mode |= EXECUTION_STATE.ES_DISPLAY_REQUIRED;
            Refresh();
            _refreshTimer.Start();
        }

        public void Disarm()
        {
            _refreshTimer.Stop();
            // A continuous state is released by calling with a lone ES_CONTINUOUS (see SetThreadExecutionState docs).
            if (_mode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS))
                WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
            _mode = EXECUTION_STATE.None;
        }

        private void Refresh()
        {
            if (_mode != EXECUTION_STATE.None)
                WinU.SetThreadExecutionState(_mode);
        }

        public void Dispose() => _refreshTimer.Dispose();
    }
}
