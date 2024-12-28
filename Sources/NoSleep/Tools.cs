using System;
using System.IO;
using System.Windows.Forms;

namespace NoSleep
{
    public static class Tools
    {
        /// <summary> Create a shortcut at given path with given link.</summary>
        /// <param name="targetPath"> Where to target the shortcut, i.e. what to run on shortcut usage.</param>
        /// <param name="shortcutPath"> Where to create the shortcut and how to name it.</param>
        /// <exception cref="Exception"> On IO related issues, including permissions.</exception>
        public static void CreateShortcut(string targetPath, string shortcutPath)
        {
            var shell = new IWshRuntimeLibrary.WshShell();
            var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.Save();
        }
        /// <summary> Path to the autostart shortcut. </summary>
        private static readonly string autostartPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{Properties.Settings.Default.AppStartupName}.lnk");

        /// <summary> Check application startup state </summary>
        /// <returns><see langword="true"/> if application startup is enabled.</returns>
        internal static bool AutostartCheck()
            => File.Exists(autostartPath);

        /// <summary> Disable application startup on user login. </summary>
        /// <returns><see langword="true"/> if shortcut is no longer present.</returns>
        internal static bool AutostartDisable()
        {
            // If autostart shortcut exists - try to remove it
            if (File.Exists(autostartPath))
            {
                try
                {
                    File.Delete(autostartPath);
                }
                catch (Exception e)
                {
                    MessageBox.Show($"Wasn't able to remove autostart shortcut from '{autostartPath}'. Error: {e.Message}",
                                    caption: Properties.Settings.Default.AppName, buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Error);
                    return false;
                }
            }
            return true;
        }

        /// <summary> Enable application startup on user login </summary>
        /// <returns><see langword="true"/> if shortcut was created.</returns>
        internal static bool AutostartEnable()
        {
            try
            {
                CreateShortcut(Application.ExecutablePath, autostartPath);
            }
            catch (Exception e)
            {
                MessageBox.Show($"Wasn't able to create autostart shortcut at '{autostartPath}'. Error: {e.Message}",
                                caption: Properties.Settings.Default.AppName, buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Error);
                return false;
            }
            return true;
        }
    }
}
