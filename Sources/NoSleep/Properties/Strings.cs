using System.Globalization;
using System.Resources;
using System.Threading;

namespace NoSleep.Properties
{
    /// <summary>
    /// Serves user-facing strings for the current UI culture (ru, zh-CN, English fallback).
    /// All language sets are embedded into the main assembly on purpose: the published
    /// artifact is the bare executable, so satellite assemblies would never ship with it.
    /// </summary>
    internal static class Strings
    {
        private static readonly ResourceManager English =
            new ResourceManager("NoSleep.Properties.Resources", typeof(Strings).Assembly);

        private static readonly ResourceManager Selected = Select();

        private static ResourceManager Select()
        {
            for (CultureInfo culture = Thread.CurrentThread.CurrentUICulture;
                 !string.IsNullOrEmpty(culture.Name);
                 culture = culture.Parent)
            {
                switch (culture.Name)
                {
                    case "ru":
                        return new ResourceManager("NoSleep.Properties.Resources.ru", typeof(Strings).Assembly);
                    case "zh":
                    case "zh-CN":
                        return new ResourceManager("NoSleep.Properties.Resources.zh-CN", typeof(Strings).Assembly);
                }
            }

            return English;
        }

        private static string Get(string name) =>
            Selected.GetString(name, CultureInfo.InvariantCulture)
            ?? English.GetString(name, CultureInfo.InvariantCulture);

        internal static string TrayMenu_Close => Get("TrayMenu_Close");
        internal static string TrayMenu_Autostart => Get("TrayMenu_Autostart");
        internal static string TrayMenu_RememberState => Get("TrayMenu_RememberState");
        internal static string TrayMenu_ConfigureApps => Get("TrayMenu_ConfigureApps");
        internal static string TrayMenu_Enabled => Get("TrayMenu_Enabled");
        internal static string TrayMenu_KeepScreenOn => Get("TrayMenu_KeepScreenOn");
        internal static string Tooltip_Autostart => Get("Tooltip_Autostart");
        internal static string Tooltip_RememberState => Get("Tooltip_RememberState");
        internal static string Tooltip_ConfigureApps => Get("Tooltip_ConfigureApps");
        internal static string Tooltip_Enabled => Get("Tooltip_Enabled");
        internal static string Tooltip_KeepScreenOn => Get("Tooltip_KeepScreenOn");
        internal static string Msg_InstanceRunning => Get("Msg_InstanceRunning");
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
