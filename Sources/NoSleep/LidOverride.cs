using System;
using NoSleep.Properties;

namespace NoSleep
{
    /// <summary>
    /// Temporarily sets the power scheme's lid-close action to "Do nothing" while sleep
    /// prevention is active, and restores the original values afterwards. The snapshot is
    /// mirrored into the settings (LidOverrideSnapshot/LidOverrideArmed) so a crash can be
    /// self-healed on the next launch (Heal). All operations are unelevated-safe
    /// (S0-FINDINGS.md, T03/T05).
    /// </summary>
    internal sealed class LidOverride
    {
        public const string SubButtonsGroup = "4f971e89-eebd-4455-a8de-9e59040e7347";
        public const string LidActionSetting = "5ca83367-6e45-459f-a27b-476b1d01c936";
        // Trigger table: to cover more manual-sleep triggers, add their settings here and
        // loop over them in Arm/Disarm. Sleep/power buttons are deliberately NOT covered:
        // those settings were not even materialized on any test machine (S0-FINDINGS.md, T16).
        //   sleep button: 961cc777-2547-4f9d-8174-7d86181b8a7a
        //   power button: 7648efa3-dd33-4e3e-b566-50f929386280

        private const uint LidDoNothing = 0;

        /// <summary> Snapshot of the original values: "schemeGuid|acValue|dcValue". </summary>
        private string _snapshot;

        /// <summary> True while the override is applied by THIS process instance. </summary>
        public bool IsArmed { get; private set; }

        /// <summary>
        /// Restore a leftover override from a previous crashed run, if any. Call once at
        /// startup, before arming anything.
        /// </summary>
        public static void Heal()
        {
            if (!Settings.Default.LidOverrideArmed)
                return;
            // A successful Disarm clears the flag; if it is still set, the last run died armed.
            TryRestoreSnapshot(Settings.Default.LidOverrideSnapshot);
            Settings.Default.LidOverrideArmed = false;
            Settings.Default.LidOverrideSnapshot = "";
            Settings.Default.Save();
        }

        /// <summary> Apply the override. Returns false (and stays disarmed) if the power APIs refuse. </summary>
        public bool Arm()
        {
            if (IsArmed)
                return true;

            string scheme = WinU.GetActivePowerScheme();
            if (scheme == null)
                return false;
            uint? ac = WinU.ReadSchemeValue(scheme, SubButtonsGroup, LidActionSetting, true);
            uint? dc = WinU.ReadSchemeValue(scheme, SubButtonsGroup, LidActionSetting, false);
            if (ac == null || dc == null)
                return false;

            if (!WinU.WriteSchemeValue(scheme, SubButtonsGroup, LidActionSetting, LidDoNothing, true) ||
                !WinU.WriteSchemeValue(scheme, SubButtonsGroup, LidActionSetting, LidDoNothing, false))
                return false;
            if (!WinU.ApplyPowerScheme(scheme))
                return false;

            _snapshot = FormatSnapshot(scheme, ac.Value, dc.Value);
            Settings.Default.LidOverrideSnapshot = _snapshot;
            Settings.Default.LidOverrideArmed = true;
            Settings.Default.Save();
            IsArmed = true;
            return true;
        }

        /// <summary> Restore the original lid action. Skips the write if the current action is not ours
        /// anymore (the user took control back via powercfg/Settings while we were armed). </summary>
        public void Disarm()
        {
            if (!IsArmed)
                return;
            IsArmed = false;
            TryRestoreSnapshot(_snapshot);
            _snapshot = null;
            Settings.Default.LidOverrideArmed = false;
            Settings.Default.LidOverrideSnapshot = "";
            Settings.Default.Save();
        }

        private static string FormatSnapshot(string scheme, uint ac, uint dc) => $"{scheme}|{ac}|{dc}";

        private static void TryRestoreSnapshot(string snapshot)
        {
            try
            {
                var parts = (snapshot ?? "").Split('|');
                if (parts.Length != 3)
                    return;
                string scheme = parts[0];
                uint ac = uint.Parse(parts[1]);
                uint dc = uint.Parse(parts[2]);

                // Skip the restore if the user has taken the lid action back into their own hands.
                uint? nowAc = WinU.ReadSchemeValue(scheme, SubButtonsGroup, LidActionSetting, true);
                uint? nowDc = WinU.ReadSchemeValue(scheme, SubButtonsGroup, LidActionSetting, false);
                if (nowAc != LidDoNothing && nowDc != LidDoNothing)
                    return;

                WinU.WriteSchemeValue(scheme, SubButtonsGroup, LidActionSetting, ac, true);
                WinU.WriteSchemeValue(scheme, SubButtonsGroup, LidActionSetting, dc, false);
                // Only re-apply when the snapshot scheme is the active one: re-activating a
                // scheme the user switched away from would silently change their power plan.
                if (string.Equals(WinU.GetActivePowerScheme(), scheme, StringComparison.OrdinalIgnoreCase))
                    WinU.ApplyPowerScheme(scheme);
            }
            catch
            {
                // Best effort by design: a failed restore of a cosmetic-adjacent setting
                // must never take the app down.
            }
        }
    }
}
