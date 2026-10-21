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
    /// </summary>
    internal sealed class AppMonitor : IDisposable
    {
        private readonly BindingList<AppEntry> _apps;
        private readonly Timer _timer;
        private HashSet<string> _pathSet;
        private HashSet<string> _nameSet;
        private bool _anyRunning;

        /// <summary> Raised on the UI thread whenever <see cref="AnyRunning"/> changes. </summary>
        public event EventHandler AnyRunningChanged;

        /// <summary> Whether any watched app is running. An empty watch list counts as "running",
        /// i.e. nothing gates the sleep prevention. </summary>
        public bool AnyRunning => _anyRunning;

        public AppMonitor(BindingList<AppEntry> apps, int scanIntervalMs)
        {
            _apps = apps;
            // List edits invalidate the caches and re-evaluate the state immediately.
            _apps.ListChanged += (s, e) => { _pathSet = null; _nameSet = null; Scan(); };
            _timer = new Timer { Interval = scanIntervalMs };
            _timer.Tick += (s, e) => Scan();
        }

        /// <summary> One synchronous scan at startup, so the initial state is real instead of assumed. </summary>
        public void Initialize() => Scan();

        /// <summary> The scan timer runs only while the watching feature is active (enabled + non-empty list). </summary>
        public void SetActive(bool active) => _timer.Enabled = active;

        private void Scan()
        {
            bool any = AreAnyRunning();
            if (any == _anyRunning)
                return;
            _anyRunning = any;
            AnyRunningChanged?.Invoke(this, EventArgs.Empty);
        }

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

        public void Dispose() => _timer.Dispose();
    }
}
