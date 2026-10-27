using System;

namespace NoSleep
{
    /// <summary> Activity simulation modes (the "Simulate activity" submenu). Numeric values
    /// are persisted in the SimulateActivity user setting - do not renumber. </summary>
    internal enum ActivityMode
    {
        Off = 0,
        /// <summary> Event-driven: burst + sustain when a sleep attempt is reported (best effort;
        /// the deterministic lid protection is the lid-close override). </summary>
        PreventSleepAttempts = 1,
        /// <summary> Continuous invisible activity (zero-delta input events): presence simulation
        /// against automatic sleep. </summary>
        SimulatePresence = 2,
        /// <summary> Continuous visible mouse movement that returns home: presence simulation
        /// for systems where invisible activity is not enough. </summary>
        MoveMouse = 3,
    }

    /// <summary> What the controller asks the backend to enforce while armed. </summary>
    internal sealed class SleepOptions
    {
        /// <summary> Keep the display on as well (existing "Keep screen on" setting). </summary>
        public bool KeepDisplayOn { get; set; }

        /// <summary> Override the lid-close action to "Do nothing". The scheme write is
        /// sleep-mode-agnostic; the UI gates the setting on a lid being present. </summary>
        public bool BlockLidSleep { get; set; }

        /// <summary> Activity simulation mode. The continuous presence modes are honored by
        /// both backends; the event-driven PreventSleepAttempts mode is Modern Standby only
        /// (classic sleep has no trigger left to fight - away mode covers them all). </summary>
        public ActivityMode Activity { get; set; }
    }

    /// <summary>
    /// How sleep prevention is actually performed. Implementations are routed by the
    /// platform's supported sleep mode (classic S1–S3 vs Modern Standby S0).
    /// Arm/Disarm are idempotent reconcile operations; the backend owns its re-arm timer
    /// and releases everything it has asserted on Disarm.
    /// </summary>
    internal interface ISleepBackend : IDisposable
    {
        /// <summary> Assert the prevention state, immediately and via periodic re-arm. </summary>
        void Arm(SleepOptions options);

        /// <summary> Release everything this backend has asserted. </summary>
        void Disarm();

        /// <summary> True only when the lid override is wanted but the power APIs refused it
        /// while prevention was active. </summary>
        bool LidOverrideFailed { get; }
    }
}
