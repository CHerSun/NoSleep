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
        /// startup, before arming anything. Keeps the snapshot armed (retried on the next
        /// launch) if the restore cannot be completed.
        /// </summary>
        public static void Heal()
        {
            if (!Settings.Default.LidOverrideArmed)
                return;
            if (TryRestoreSnapshot(Settings.Default.LidOverrideSnapshot))
            {
                Settings.Default.LidOverrideArmed = false;
                Settings.Default.LidOverrideSnapshot = "";
                Settings.Default.Save();
            }
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

        /// <summary> Restore the original lid action. On a failed restore the override stays
        /// armed (snapshot kept) so a later Disarm or the next launch's Heal retries it -
        /// never report ourselves disarmed while the override may still be in place. </summary>
        public void Disarm()
        {
            if (!IsArmed)
                return;
            if (!TryRestoreSnapshot(_snapshot))
                return;
            IsArmed = false;
            _snapshot = null;
            Settings.Default.LidOverrideArmed = false;
            Settings.Default.LidOverrideSnapshot = "";
            Settings.Default.Save();
        }

        private static string FormatSnapshot(string scheme, uint ac, uint dc) => $"{scheme}|{ac}|{dc}";

        /// <summary> Returns true when the originals are back in place (or were never ours anymore);
        /// false when the restore could not be completed and should be retried later. </summary>
        private static bool TryRestoreSnapshot(string snapshot)
        {
            try
            {
                var parts = (snapshot ?? "").Split('|');
                if (parts.Length != 3)
                    return true; // malformed/empty snapshot: nothing of ours to restore
                string scheme = parts[0];
                uint ac = uint.Parse(parts[1]);
                uint dc = uint.Parse(parts[2]);

                // Skip the restore only when BOTH reads succeeded AND both sides hold non-zero
                // values - the user took the lid action back into their own hands. A FAILED
                // read (null) means UNKNOWN, not "user's own value": skipping on a failed read
                // would silently leave our override in place while we report ourselves
                // disarmed (observed in the wild as: "zzz" icon, lid still overridden).
                uint? nowAc = WinU.ReadSchemeValue(scheme, SubButtonsGroup, LidActionSetting, true);
                uint? nowDc = WinU.ReadSchemeValue(scheme, SubButtonsGroup, LidActionSetting, false);
                if (nowAc == null || nowDc == null)
                    return false; // cannot verify - keep the snapshot armed and retry later
                if (nowAc.Value != LidDoNothing && nowDc.Value != LidDoNothing)
                    return true;  // user's own values are in place - consider it restored

                if (!WinU.WriteSchemeValue(scheme, SubButtonsGroup, LidActionSetting, ac, true) ||
                    !WinU.WriteSchemeValue(scheme, SubButtonsGroup, LidActionSetting, dc, false))
                    return false;

                // Only re-apply when the snapshot scheme is the active one: re-activating a
                // scheme the user switched away from would silently change their power plan.
                if (string.Equals(WinU.GetActivePowerScheme(), scheme, StringComparison.OrdinalIgnoreCase))
                    WinU.ApplyPowerScheme(scheme);
                return true;
            }
            catch
            {
                // Best effort by design: a failed restore must never take the app down -
                // report failure so the armed snapshot survives and Heal retries.
                return false;
            }
        }
    }
}
