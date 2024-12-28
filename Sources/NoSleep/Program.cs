using System;
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
        static void Main()
        {
            using (Mutex mutex = new Mutex(false, Properties.Settings.Default.AppMutexGuid))
            {
                if (!mutex.WaitOne(0, false))
                {
                    MessageBox.Show($"{Properties.Settings.Default.AppName} instance is already running.", Properties.Settings.Default.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
