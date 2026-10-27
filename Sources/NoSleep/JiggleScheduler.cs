using System;
using System.Windows.Forms;

namespace NoSleep
{
    /// <summary>
    /// Continuous activity simulation (the "Simulate presence" / "Move the mouse" modes):
    /// fires zero-delta or paired mouse moves at a randomized cadence. The cadence is
    /// recomputed per tick within +/- jitter around the base interval so the events do not
    /// form a perfectly periodic pattern (periodicity is trackable); the settings are
    /// user-tunable and read once at startup by the owner. Shared by both sleep backends:
    /// the mechanics are pure SendInput and do not depend on the sleep mode.
    /// </summary>
    internal sealed class JiggleScheduler : IDisposable
    {
        private const int RealMoveDistance = 4;      // visible-mode jiggle step, px
        private const int MinJiggleIntervalMs = 100; // safety clamp for hand-edited settings

        private readonly Timer _timer;
        private readonly int _intervalMs;
        private readonly int _jitterPercent;
        private readonly Random _rnd = new Random();
        private ActivityMode _mode; // only SimulatePresence / MoveMouse while the timer runs

        public JiggleScheduler(int intervalMs, int jitterPercent)
        {
            _intervalMs = Math.Max(MinJiggleIntervalMs, intervalMs);
            _jitterPercent = Math.Min(90, Math.Max(0, jitterPercent));
            _timer = new Timer();
            _timer.Tick += Tick;
        }

        /// <summary> Reconcile to the given mode: the continuous modes (re)start the timer,
        /// everything else (Off, and the S0-only event-driven mode) stops it. Idempotent. </summary>
        public void SetActivity(ActivityMode mode)
        {
            if (mode != ActivityMode.SimulatePresence && mode != ActivityMode.MoveMouse)
            {
                _timer.Stop();
                return;
            }
            _mode = mode;
            _timer.Interval = NextInterval();
            _timer.Start();
        }

        /// <summary> Uniformly randomized interval within +/- jitter around the base. </summary>
        private int NextInterval()
        {
            if (_jitterPercent <= 0)
                return _intervalMs;
            int spread = Math.Max(1, (int)((long)_intervalMs * _jitterPercent / 100));
            return Math.Max(MinJiggleIntervalMs, _intervalMs - spread + _rnd.Next(2 * spread + 1));
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_mode == ActivityMode.SimulatePresence)
                InputSimulator.ZeroDeltaMove(); // invisible activity, pointer never moves
            else
                InputSimulator.MoveAndReturn(RealMoveDistance); // visible jiggle, returns home in the same batch
            _timer.Interval = NextInterval();
        }

        public void Dispose() => _timer.Dispose();
    }
}
