using System;

namespace NoSleep
{
    /// <summary>
    /// How sleep prevention is actually performed. Implementations are routed by the
    /// platform's supported sleep mode (classic S1–S3 vs Modern Standby S0).
    /// Arm/Disarm are idempotent reconcile operations; the backend owns its re-arm timer
    /// and releases everything it has asserted on Disarm.
    /// </summary>
    internal interface ISleepBackend : IDisposable
    {
        /// <summary> Assert the prevention state, immediately and via periodic re-arm. </summary>
        void Arm(bool keepDisplayOn);

        /// <summary> Release everything this backend has asserted. </summary>
        void Disarm();
    }
}
