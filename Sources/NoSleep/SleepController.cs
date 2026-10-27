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
        private readonly SleepOptions _options = new SleepOptions();

        public SleepController(ISleepBackend backend, AppMonitor monitor)
        {
            _backend = backend;
            _monitor = monitor;
            _monitor.AnyRunningChanged += (s, e) => Apply();
        }

        public bool UserEnabled { get; private set; }

        public bool KeepScreenOn => _options.KeepDisplayOn;

        public bool BlockLidSleep => _options.BlockLidSleep;

        public ActivityMode Activity => _options.Activity;

        /// <summary> Whether the apps-watching feature currently gates prevention (watching enabled AND list non-empty). </summary>
        public bool WatchingActive { get; private set; }

        /// <summary> Same formula as 1.5.x: enabled, and (not watching, or a watched app is running). </summary>
        public bool EffectivePreventSleep => UserEnabled && (!WatchingActive || _monitor.AnyRunning);

        /// <summary> True when the lid override is wanted but the power APIs refused it while
        /// prevention was active. </summary>
        public bool LidOverrideFailed => _backend.LidOverrideFailed;

        /// <summary> Raised on the UI thread after every state transition. </summary>
        public event EventHandler StateChanged;

        public void SetUserEnabled(bool value)
        {
            if (UserEnabled == value)
                return;
            UserEnabled = value;
            UpdateMonitorGate();
            Apply();
        }

        public void SetKeepScreenOn(bool value)
        {
            if (_options.KeepDisplayOn == value)
                return;
            _options.KeepDisplayOn = value;
            Apply();
        }

        public void SetBlockLidSleep(bool value)
        {
            if (_options.BlockLidSleep == value)
                return;
            _options.BlockLidSleep = value;
            Apply();
        }

        public void SetActivity(ActivityMode mode)
        {
            if (_options.Activity == mode)
                return;
            _options.Activity = mode;
            Apply();
        }

        /// <summary> Whether the watching feature should gate prevention; drives the monitor timer. </summary>
        public void SetWatchingActive(bool value)
        {
            if (WatchingActive == value)
                return;
            WatchingActive = value;
            UpdateMonitorGate();
            Apply();
        }

        /// <summary> The app scanner runs only while the whole chain is on:
        /// user enabled AND watching active. Enabling therefore always evaluates optimistically
        /// (the monitor reports "running/unknown" while off) and the monitor's reconcile scan
        /// corrects the state shortly after. </summary>
        private void UpdateMonitorGate() => _monitor.SetActive(UserEnabled && WatchingActive);

        /// <summary> Re-evaluate the effective state and reconcile the backend. Idempotent. </summary>
        public void Apply()
        {
            if (EffectivePreventSleep)
                _backend.Arm(_options);
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
