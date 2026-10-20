using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace NoSleep
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Optional UI language override: --lang <culture> (e.g. ru, en-US, zh-CN); --lang [list] lists the available languages.
            // Must run before any UI string is used; unknown tags fall back to English via the Strings culture picker.
            if (args.Length > 0 && string.Equals(args[0], "--lang", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2 || string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(Properties.Strings.AvailableLanguagesList, Properties.Settings.Default.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(args[1]);
                }
                catch (CultureNotFoundException)
                {
                    MessageBox.Show(string.Format(Properties.Strings.Msg_UnknownLanguage, args[1]) + Environment.NewLine + Environment.NewLine + Properties.Strings.AvailableLanguagesList,
                                    caption: Properties.Settings.Default.AppName, buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Exclamation);
                    return;
                }
            }

            using (Mutex mutex = new Mutex(false, Properties.Settings.Default.AppMutexGuid))
            {
                if (!mutex.WaitOne(0, false))
                {
                    MessageBox.Show(string.Format(Properties.Strings.Msg_InstanceRunning, Properties.Settings.Default.AppName), Properties.Settings.Default.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                InitUpgradeSettings();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayIcon());
            }
        }

        /// <summary>
        /// If no settings were found - it's possible that the program version has changed and there are settings from a previous version.
        /// This function will upgrade the settings to the current version - this preserves user settings.
        /// </summary>
        static void InitUpgradeSettings()
        {
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }
        }
    }
}
