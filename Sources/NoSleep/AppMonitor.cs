using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace NoSleep
{
    /// <summary>
    /// Watches the configured applications list and reports whether any of them is running.
    /// Owns the scan timer and the path/name caches; everything runs on the UI thread.
    /// Scanning is gated from outside (see <see cref="SetActive"/>): the scanner runs only
    /// while the whole chain is on - the app is user-enabled AND watching is active.
    ///
    /// State semantics: <see cref="AnyRunning"/> is TRUE whenever the answer is unknown
    /// (scanner off) - an unknown answer must never veto an enabling action, and only a
    /// real scan may set it to false.
    /// </summary>
    internal sealed class AppMonitor : IDisposable
    {
        /// <summary> Perceptible "heard you" window after the scanner activates: the state stays
        /// optimistic (AnyRunning = true) for this long, then the first scan reconciles it -
        /// possibly back to inactive if no watched app is running. Measured full-scan cost is
        /// ~10 ms median / ~33 ms worst on a 287-process machine (net48), so this delay exists
        /// purely for user feedback, not to hide the scan. </summary>
        private const int ReconcileDelayMs = 200;

        private readonly BindingList<AppEntry> _apps;
        private readonly Timer _timer;
        private readonly Timer _reconcileTimer;
        private HashSet<string> _pathSet;
        private HashSet<string> _nameSet;
        private bool _anyRunning = true; // unknown == true (the optimistic/safe side)

        /// <summary> Raised on the UI thread whenever <see cref="AnyRunning"/> changes. </summary>
        public event EventHandler AnyRunningChanged;

        /// <summary> Whether any watched app is running - or TRUE while unknown (scanner off). </summary>
        public bool AnyRunning => _anyRunning;

        public AppMonitor(BindingList<AppEntry> apps, int scanIntervalMs)
        {
            _apps = apps;
            // List edits invalidate the caches; while the scanner is on they also re-scan at once.
            _apps.ListChanged += (s, e) =>
            {
                _pathSet = null;
                _nameSet = null;
                if (_timer.Enabled)
                    Scan();
            };
            _timer = new Timer { Interval = scanIntervalMs };
            _timer.Tick += (s, e) => Scan();
            _reconcileTimer = new Timer { Interval = ReconcileDelayMs };
            _reconcileTimer.Tick += (s, e) => { _reconcileTimer.Stop(); Scan(); };
        }

        /// <summary> The scanner gate. On activation the state stays optimistic for a short
        /// perceptible window, then the first scan reconciles. On deactivation the pending
        /// reconcile is cancelled and the answer returns to unknown (true). </summary>
        public void SetActive(bool active)
        {
            if (active == _timer.Enabled)
                return;
            if (active)
            {
                _timer.Start();
                _reconcileTimer.Start();
            }
            else
            {
                _timer.Stop();
                _reconcileTimer.Stop();
                SetAnyRunning(true);
            }
        }

        private void SetAnyRunning(bool value)
        {
            if (_anyRunning == value)
                return;
            _anyRunning = value;
            AnyRunningChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Scan() => SetAnyRunning(AreAnyRunning());

        private bool AreAnyRunning()
        {
            var apps = _apps.ToList();
            // If there are no apps - nothing to watch, nothing gates the prevention.
            if (apps.Count == 0)
                return true;

            if (_pathSet is null)
            {
                _pathSet = apps.Select(app => app.ExePath)
                               .Where(path => !string.IsNullOrWhiteSpace(path))
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _nameSet = apps.Select(app => app.Name)
                               .Where(name => !string.IsNullOrWhiteSpace(name))
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return Process.GetProcesses().Any(p =>
            {
                // Exception free Process path extraction.
                // Standard Process.MainModule throws exceptions, which consume quite a lot of cpu.
                string path = p.TryGetExecutablePath();
                if ((path != null) && _pathSet.Contains(path))
                    return true;

                // Fallback to name-only detection, if we can't get process path.
                return _nameSet.Contains(p.ProcessName);
            });
        }

        public void Dispose()
        {
            _timer.Dispose();
            _reconcileTimer.Dispose();
        }
    }
}
