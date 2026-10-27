using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace NoSleep.Properties
{
    /// <summary>
    /// Serves user-facing strings for the current UI culture (English fallback).
    /// All language sets are embedded into the main assembly on purpose: the published
    /// artifact is the bare executable, so satellite assemblies would never ship with it.
    /// Available cultures are discovered from the embedded resource names, so adding a
    /// language is just a Resources_xx.resx file plus its csproj EmbeddedResource entry.
    /// </summary>
    internal static class Strings
    {
        private const string BaseName = "NoSleep.Properties.Resources";

        private static readonly ResourceManager English =
            new ResourceManager(BaseName, typeof(Strings).Assembly);

        private static readonly string[] Cultures = DiscoverCultures();

        private static readonly ResourceManager Selected = Select();

        private static string[] DiscoverCultures()
        {
            const string Suffix = ".resources";
            var cultures = new List<string>();
            foreach (string name in typeof(Strings).Assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(Suffix, StringComparison.Ordinal))
                    continue;

                string stem = name.Substring(0, name.Length - Suffix.Length);
                if (!stem.StartsWith(BaseName + ".", StringComparison.Ordinal))
                    continue;

                string culture = stem.Substring(BaseName.Length + 1);
                if (culture.Length > 0)
                    cultures.Add(culture);
            }

            cultures.Sort(StringComparer.OrdinalIgnoreCase);
            return cultures.ToArray();
        }

        private static ResourceManager Select()
        {
            for (CultureInfo culture = Thread.CurrentThread.CurrentUICulture;
                 !string.IsNullOrEmpty(culture.Name);
                 culture = culture.Parent)
            {
                string exact = Array.Find(Cultures, c => string.Equals(c, culture.Name, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return CreateManager(exact);
            }

            // zh-Hans, zh-SG and friends have no set of their own: fall into the set of the same two-letter language
            for (CultureInfo culture = Thread.CurrentThread.CurrentUICulture;
                 !string.IsNullOrEmpty(culture.Name);
                 culture = culture.Parent)
            {
                string sibling = Array.Find(Cultures, c => c.StartsWith(culture.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase));
                if (sibling != null)
                    return CreateManager(sibling);
            }

            return English;
        }

        private static ResourceManager CreateManager(string culture) =>
            new ResourceManager(BaseName + "." + culture, typeof(Strings).Assembly);

        private static string Get(string name) =>
            Selected.GetString(name, CultureInfo.InvariantCulture)
            ?? English.GetString(name, CultureInfo.InvariantCulture);

        internal static string AvailableLanguagesList
        {
            get
            {
                var entries = new List<string> { "en — " + NativeName("en") };
                foreach (string culture in Cultures)
                    entries.Add(culture + " — " + NativeName(culture));

                return string.Format(Get("Msg_AvailableLanguages"), string.Join(", ", entries));
            }
        }

        private static string NativeName(string culture)
        {
            try
            {
                string name = CultureInfo.GetCultureInfo(culture).NativeName;
                return char.ToUpper(name[0]) + name.Substring(1);
            }
            catch (CultureNotFoundException)
            {
                return culture;
            }
        }

        internal static string TrayMenu_Close => Get("TrayMenu_Close");
        internal static string TrayMenu_Autostart => Get("TrayMenu_Autostart");
        internal static string TrayMenu_RememberState => Get("TrayMenu_RememberState");
        internal static string TrayMenu_ConfigureApps => Get("TrayMenu_ConfigureApps");
        internal static string TrayMenu_Enabled => Get("TrayMenu_Enabled");
        internal static string TrayMenu_KeepScreenOn => Get("TrayMenu_KeepScreenOn");
        internal static string TrayMenu_SleepModeClassic => Get("TrayMenu_SleepModeClassic");
        internal static string TrayMenu_SleepModeModern => Get("TrayMenu_SleepModeModern");
        internal static string TrayMenu_SleepModeUnknown => Get("TrayMenu_SleepModeUnknown");
        internal static string TrayMenu_BlockLidSleep => Get("TrayMenu_BlockLidSleep");
        internal static string TrayMenu_SimulateActivity => Get("TrayMenu_SimulateActivity");
        internal static string TrayMenu_SimulateActivityOff => Get("TrayMenu_SimulateActivityOff");
        internal static string TrayMenu_SimulateActivityPrevent => Get("TrayMenu_SimulateActivityPrevent");
        internal static string TrayMenu_SimulateActivityPresence => Get("TrayMenu_SimulateActivityPresence");
        internal static string TrayMenu_SimulateActivityReal => Get("TrayMenu_SimulateActivityReal");
        internal static string Tooltip_Autostart => Get("Tooltip_Autostart");
        internal static string Tooltip_RememberState => Get("Tooltip_RememberState");
        internal static string Tooltip_ConfigureApps => Get("Tooltip_ConfigureApps");
        internal static string Tooltip_Enabled => Get("Tooltip_Enabled");
        internal static string Tooltip_KeepScreenOn => Get("Tooltip_KeepScreenOn");
        internal static string Tooltip_BlockLidSleep => Get("Tooltip_BlockLidSleep");
        internal static string TrayMenu_NoLidDetected => Get("TrayMenu_NoLidDetected");
        internal static string Tooltip_NoLidDetected => Get("Tooltip_NoLidDetected");
        internal static string Tooltip_SimulateActivity => Get("Tooltip_SimulateActivity");
        internal static string Tooltip_SimulateActivityOff => Get("Tooltip_SimulateActivityOff");
        internal static string Tooltip_SimulateActivityPrevent => Get("Tooltip_SimulateActivityPrevent");
        internal static string Tooltip_SimulateActivityPresence => Get("Tooltip_SimulateActivityPresence");
        internal static string Tooltip_SimulateActivityReal => Get("Tooltip_SimulateActivityReal");
        internal static string Msg_InstanceRunning => Get("Msg_InstanceRunning");
        internal static string Msg_UnknownLanguage => Get("Msg_UnknownLanguage");
        internal static string Msg_AutostartRemoveFailed => Get("Msg_AutostartRemoveFailed");
        internal static string Msg_AutostartCreateFailed => Get("Msg_AutostartCreateFailed");
        internal static string Btn_AppsWatchingEnabled => Get("Btn_AppsWatchingEnabled");
        internal static string Btn_AppsWatchingDisabled => Get("Btn_AppsWatchingDisabled");
        internal static string FileDlg_FilterExecutable => Get("FileDlg_FilterExecutable");
        internal static string FileDlg_FilterAllFiles => Get("FileDlg_FilterAllFiles");
        internal static string FileDlg_Title => Get("FileDlg_Title");
        internal static string Col_Name => Get("Col_Name");
        internal static string Col_ExePath => Get("Col_ExePath");
        internal static string Btn_Add => Get("Btn_Add");
        internal static string Btn_Remove => Get("Btn_Remove");
        internal static string Btn_Enable => Get("Btn_Enable");
        internal static string Btn_Disable => Get("Btn_Disable");
        internal static string Btn_Close => Get("Btn_Close");
        internal static string Form_Title => Get("Form_Title");
    }
}
