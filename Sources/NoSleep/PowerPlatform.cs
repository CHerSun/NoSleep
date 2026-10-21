namespace NoSleep
{
    /// <summary>
    /// Sleep modes the app distinguishes. Routing only separates Modern Standby from
    /// classic sleep; on classic machines the specific S-state makes no difference
    /// (SetThreadExecutionState incl. away mode covers S1–S3 the same way).
    /// </summary>
    internal enum SleepMode
    {
        /// <summary> Traditional S1–S3 sleep (the label shows "S3", the common representative). </summary>
        Classic,
        /// <summary> Modern Standby (S0 low power idle): away mode is inapplicable and manual sleep
        /// terminates power requests - prevention is routed to the Modern Standby backend. </summary>
        ModernStandby,
        /// <summary> Detection failed - classic behavior is the safe fallback, indicator says so. </summary>
        Unknown
    }

    /// <summary>
    /// Platform sleep-capability detection via GetPwrCapabilities (the power manager's own
    /// answer). The sleep mode cannot change without a reboot, so everything is computed once.
    /// Registry markers (PlatformAoAc) are deliberately not used: they are absent on real
    /// Modern Standby hardware (see tools/sleep-mode/S0-FINDINGS.md, T01/T02).
    /// </summary>
    internal static class PowerPlatform
    {
        // SYSTEM_POWER_CAPABILITIES head layout (winnt.h, Win10+), one byte per BOOLEAN/UCHAR:
        // 0 PowerButtonPresent, 1 SleepButtonPresent, 2 LidPresent, 3 SystemS1, 4 SystemS2,
        // 5 SystemS3, 6 SystemS4, 7 SystemS5, 8 HiberFile, 17 FastSystemS4, 18 Hiberboot,
        // 19 WakeAlertPresent, 20 AoAc (Modern Standby), 23 AoAcConnectivity, 24 SystemBatteries.
        // Offsets validated against real S3 and S0 hardware (S0-FINDINGS.md, T02).
        private const int OffsetLidPresent = 2;
        private const int OffsetSystemS1 = 3;
        private const int OffsetSystemS3 = 5;
        private const int OffsetAoAc = 20;

        private static byte[] _caps;
        private static byte[] Caps => _caps ?? (_caps = WinU.GetPowerCapabilities());

        public static SleepMode SleepMode { get; } = Detect();

        public static bool LidPresent
        {
            get
            {
                var caps = Caps;
                return caps != null && caps[OffsetLidPresent] != 0;
            }
        }

        private static SleepMode Detect()
        {
            var caps = Caps;
            if (caps == null)
                return SleepMode.Unknown;
            if (caps[OffsetAoAc] != 0)
                return SleepMode.ModernStandby;
            // Classic covers S3 (common), S1/S2-only firmware and S4-only boxes alike:
            // requests-based prevention behaves identically for all of them.
            return SleepMode.Classic;
        }
    }
}
