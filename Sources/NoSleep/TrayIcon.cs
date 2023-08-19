using NoSleep.Properties;
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
        private EXECUTION_STATE ExecutionMode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_AWAYMODE_REQUIRED;

        // PRIVATE VARIABLES
        private NotifyIcon _TrayIcon;
        private ToolStripMenuItem _EnabledItem;
        private ToolStripMenuItem _DisplayRequired;
        private readonly Timer _RefreshTimer;

        // CONSTRUCTOR
        public TrayIcon()
        {
            // Set timer to tick to refresh idle timers
            _RefreshTimer = new Timer() { Interval = RefreshInterval };
            _RefreshTimer.Tick += RefreshTimer_Tick;
            ArmExecutionState();

            // Initialize application
            Application.ApplicationExit += this.OnApplicationExit;
            InitializeComponent();
            _TrayIcon.Visible = true;
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
            // Initialize MonitorRequired as field, so we can reference it freely. Set it to opposite value and trigger a click once.
            _DisplayRequired = new ToolStripMenuItem("Keep screen on") { Checked = !Settings.Default.DisplayRequired, ToolTipText="If display should be kept always on in addition to keeping the system on." };
            _DisplayRequired.Click += MonitorRequired_Click;
            MonitorRequired_Click(null, null);

            // Initialize context menu
            _TrayIcon.ContextMenuStrip = new ContextMenuStrip();
            _TrayIcon.ContextMenuStrip.Items.Add(_AutoStartItem);
            _TrayIcon.ContextMenuStrip.Items.Add(_DisplayRequired);
            _TrayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _TrayIcon.ContextMenuStrip.Items.Add(_EnabledItem);
            _TrayIcon.ContextMenuStrip.Items.Add(_CloseMenuItem);
        }

        private void MonitorRequired_Click(object sender, EventArgs e)
        {
            if (_DisplayRequired.Checked)
            {
                _DisplayRequired.Checked = false;
                // Properly disarm current state
                DisarmExecutionState();
                // Update ExecutionMode
                ExecutionMode &= ~EXECUTION_STATE.ES_DISPLAY_REQUIRED;
                // Update settings
                Settings.Default.DisplayRequired = false;
                Settings.Default.Save();
                // Rearm
                ArmExecutionState();
            }
            else
            {
                _DisplayRequired.Checked = true;
                // Properly disarm current state
                DisarmExecutionState();
                // Update ExecutionMode
                ExecutionMode |= EXECUTION_STATE.ES_DISPLAY_REQUIRED;
                // Update settings
                Settings.Default.DisplayRequired = true;
                Settings.Default.Save();
                // Rearm
                ArmExecutionState();
            }
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
                item.Checked = false;
                _TrayIcon.Icon = Resources.TrayIconInactive;
                DisarmExecutionState();
            }
            else
            {
                item.Checked = true;
                _TrayIcon.Icon = Resources.TrayIcon;
                ArmExecutionState();
            }
        }

        private void AutoStartItem_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem item))
                return;

            item.Checked = item.Checked ? !RemoveFromStartup() : AddToStartup();
        }

        private void ArmExecutionState()
        {
            _RefreshTimer.Start();
        }

        private void DisarmExecutionState()
        {
            _RefreshTimer.Enabled = false;
            // Clean up continuous state, if ES_CONTINUOUS was used
            if (ExecutionMode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS)) WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            _TrayIcon.Visible = false;
            DisarmExecutionState();
            _RefreshTimer.Dispose();
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
            {
                try
                {
                    File.Delete(appShortcutPath);
                }
                catch (Exception e)
                {
                    MessageBox.Show($"Wasn't able to remove autostart shortcut from '{appShortcutPath}'. Error: {e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
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