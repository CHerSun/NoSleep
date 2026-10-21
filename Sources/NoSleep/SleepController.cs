using System;

namespace NoSleep
{
    /// <summary>
    /// The app's state machine and the single source of truth: combines user intent with the
    /// app-watch result into the effective prevent-sleep state, and drives the routed
    /// prevention backend. All inputs flow through the Set* methods; every change re-applies
    /// and raises <see cref="StateChanged"/> for the UI to re-render from.
    /// </summary>
    internal sealed class SleepController : IDisposable
    {
        private readonly ISleepBackend _backend;
        private readonly AppMonitor _monitor;

        public SleepController(ISleepBackend backend, AppMonitor monitor)
        {
            _backend = backend;
            _monitor = monitor;
            _monitor.AnyRunningChanged += (s, e) => Apply();
        }

        public bool UserEnabled { get; private set; }

        public bool KeepScreenOn { get; private set; }

        /// <summary> Whether the apps-watching feature currently gates prevention (watching enabled AND list non-empty). </summary>
        public bool WatchingActive { get; private set; }

        /// <summary> Same formula as 1.5.x: enabled, and (not watching, or a watched app is running). </summary>
        public bool EffectivePreventSleep => UserEnabled && (!WatchingActive || _monitor.AnyRunning);

        /// <summary> Raised on the UI thread after every state transition. </summary>
        public event EventHandler StateChanged;

        public void SetUserEnabled(bool value)
        {
            if (UserEnabled == value)
                return;
            UserEnabled = value;
            Apply();
        }

        public void SetKeepScreenOn(bool value)
        {
            if (KeepScreenOn == value)
                return;
            KeepScreenOn = value;
            Apply();
        }

        public void SetWatchingActive(bool value)
        {
            if (WatchingActive == value)
                return;
            WatchingActive = value;
            _monitor.SetActive(value);
            Apply();
        }

        /// <summary> Re-evaluate the effective state and reconcile the backend. Idempotent. </summary>
        public void Apply()
        {
            if (EffectivePreventSleep)
                _backend.Arm(KeepScreenOn);
            else
                _backend.Disarm();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _backend.Disarm();
            _backend.Dispose();
            _monitor.Dispose();
        }
    }
}
