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
            using (Mutex mutex = new Mutex(false, TrayIcon.AppGuid))
            {
                if (!mutex.WaitOne(0, false))
                {
                    MessageBox.Show($"{TrayIcon.AppName} instance is already running.", TrayIcon.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayIcon());
            }
        }
    }
}
