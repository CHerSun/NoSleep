using System;

namespace NoSleep
{
    /// <summary> What the controller asks the backend to enforce while armed. </summary>
    internal sealed class SleepOptions
    {
        /// <summary> Keep the display on as well (existing "Keep screen on" setting). </summary>
        public bool KeepDisplayOn { get; set; }

        /// <summary> Override the lid-close action to "Do nothing" (Modern Standby only). </summary>
        public bool BlockLidSleep { get; set; }

        /// <summary> Activity simulation mode: 0 = off, 1 = prevent sleep attempts (event-driven),
        /// 2 = simulate user presence (continuous). Modern Standby only. </summary>
        public int SimulateActivity { get; set; }

        public bool SameAs(SleepOptions other) =>
            other != null &&
            KeepDisplayOn == other.KeepDisplayOn &&
            BlockLidSleep == other.BlockLidSleep &&
            SimulateActivity == other.SimulateActivity;
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
    }
}
