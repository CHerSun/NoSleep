using System;
using System.IO;
using System.Windows.Forms;

namespace NoSleep
{
    class TrayIcon : ApplicationContext
    {
        internal const string AppName = "NoSleep";
        internal const string AppGuid = "8b2caf22-dc35-4e70-88df-35933ab63f69";
        /// <summary>
        /// Interval between timer ticks (in ms) to refresh Windows idle timers. Shouldn't be too small to avoid resources consumption. Must be less then Windows screensaver/sleep timer.
        /// Default = 10 000 ms (10 seconds).
        /// </summary>
        const int RefreshInterval = 10000;
        /// <summary>
        /// ExecutionMode defines how blocking is made. See details at https://msdn.microsoft.com/en-us/library/aa373208.aspx?f=255&MSPPError=-2147217396
        /// </summary>
        const EXECUTION_STATE ExecutionMode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_AWAYMODE_REQUIRED;

        // PRIVATE VARIABLES
        private NotifyIcon _TrayIcon;
        private ToolStripMenuItem _EnabledItem;
        private readonly Timer _RefreshTimer;

        // CONSTRUCTOR
        public TrayIcon()
        {
            // Initialize application
            Application.ApplicationExit += this.OnApplicationExit;
            InitializeComponent();
            _TrayIcon.Visible = true;

            // Set timer to tick to refresh idle timers
            _RefreshTimer = new Timer() { Interval = RefreshInterval, Enabled = true };
            _RefreshTimer.Tick += RefreshTimer_Tick;
        }

        private void InitializeComponent()
        {
            // Initialize Tray icon
            _TrayIcon = new NotifyIcon
            {
                Text = AppName,
                Icon = Properties.Resources.TrayIcon
            };
            _TrayIcon.Click += TrayIcon_Click;

            // Initialize Close menu item for context menu
            var _CloseMenuItem = new ToolStripMenuItem("Close");
            _CloseMenuItem.Click += CloseMenuItem_Click;
            // Initialize Autostart menu item for context menu
            var _AutoStartItem = new ToolStripMenuItem("Autostart at login") { Checked = LoadAutoStartPreference() };
            _AutoStartItem.Click += AutoStartItem_Click;
            // Initialize EnabledItem as field, so we can reference it freely
            _EnabledItem = new ToolStripMenuItem("Enabled") { Checked = true };
            _EnabledItem.Click += EnabledItem_Click;

            // Initialize context menu
            _TrayIcon.ContextMenuStrip = new ContextMenuStrip();
            _TrayIcon.ContextMenuStrip.Items.Add(_AutoStartItem);
            _TrayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _TrayIcon.ContextMenuStrip.Items.Add(_EnabledItem);
            _TrayIcon.ContextMenuStrip.Items.Add(_CloseMenuItem);
        }

        private void TrayIcon_Click(object sender, EventArgs e)
        {
            var e2 = e as MouseEventArgs;
            if (e2.Button == MouseButtons.Left)
            {
                EnabledItem_Click(sender, e);
            }
        }

        private void EnabledItem_Click(object sender, EventArgs e)
        {
            var item = _EnabledItem;
            if (item.Checked)
            {
                _RefreshTimer.Stop();
                item.Checked = false;
                _TrayIcon.Icon = Properties.Resources.TrayIconInactive;
            }
            else
            {
                _RefreshTimer.Start();
                item.Checked = true;
                _TrayIcon.Icon = Properties.Resources.TrayIcon;
            }
        }

        private void AutoStartItem_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem item))
                return;

            item.Checked = item.Checked ? !RemoveFromStartup() : AddToStartup();
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            // Clean up things on exit
            _TrayIcon.Visible = false;
            _RefreshTimer.Enabled = false;
            _RefreshTimer.Dispose();
            // Clean up continuous state, if required
            if(ExecutionMode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS)) WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        }

        /// <summary> Close context menu item click - exit the application. </summary>
        private void CloseMenuItem_Click(object sender, EventArgs e) 
        { 
            Application.Exit(); 
        }

        /// <summary> Timer tick to refresh PC-required lock. </summary>
        private void RefreshTimer_Tick(object sender, EventArgs e) 
        { 
            WinU.SetThreadExecutionState(ExecutionMode); 
        }


        /// <summary>
        /// Create Autostart shortcut.
        /// </summary>
        /// <returns><see langword="true"/> if shortcut was created.</returns>
        private bool AddToStartup()
        {
            // Get the path to the user's Startup folder
            string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);

            // Copy the application executable to the Startup folder
            string appExecutablePath = Application.ExecutablePath;
            string appShortcutPath = Path.Combine(startupFolderPath, $"{AppName}.lnk");
            try { CreateShortcut(appExecutablePath, appShortcutPath); }
            catch (Exception e) 
            {
                MessageBox.Show($"Wasn't able to create autostart shortcut at '{appShortcutPath}'. Error: {e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Remove Autostart shortcut.
        /// </summary>
        /// <returns><see langword="true"/> if shortcut is no longer present.</returns>
        private bool RemoveFromStartup()
        {
            string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string appShortcutPath = Path.Combine(startupFolderPath, $"{AppName}.lnk");
            if (File.Exists(appShortcutPath))
                try { File.Delete(appShortcutPath); }
                catch (Exception e)
                {
                    MessageBox.Show($"Wasn't able to remove autostart shortcut from '{appShortcutPath}'. Error: {e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            return true;
        }

        /// <summary>
        /// Create a shortcut. Could raise <see cref="Exception"/> on IO related issues, including permissions.
        /// </summary>
        /// <param name="targetPath">Where to target the shortcut, i.e. what to run on shortcut usage.</param>
        /// <param name="shortcutPath">Where to create the shortcut and how to name it.</param>
        /// <exception cref="Exception"/>
        private void CreateShortcut(string targetPath, string shortcutPath)
        {
            // Create a shortcut to the application executable
            var shell = new IWshRuntimeLibrary.WshShell();
            var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.Save();
        }

        /// <summary>
        /// Load AutoStart state by checking if shortcut exitst.
        /// </summary>
        /// <returns><see langword="true"/> if autostart is enabled.</returns>
        private bool LoadAutoStartPreference()
        {
            string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string appShortcutPath = Path.Combine(startupFolderPath, $"{AppName}.lnk");
            return File.Exists(appShortcutPath);
        }
    }
}