using System;
using System.Windows.Forms;

namespace NoSleep
{
    class TrayIcon : ApplicationContext
    {
        /// <summary>
        /// ExecutionMode defines how blocking is made. Mutable at runtime.
        /// </summary>
        private EXECUTION_STATE ExecutionMode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED |
                                                EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_AWAYMODE_REQUIRED;

        // PRIVATE VARIABLES
        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _menuItem_Enabled;
        private ToolStripMenuItem _menuItem_DisplayRequired;
        private ToolStripMenuItem _menuItem_AutoStart;
        private ToolStripMenuItem _menuItem_RememberEnabledState;
        private readonly Timer _refreshTimer;

        // CONSTRUCTOR
        public TrayIcon()
        {
            // Set timer to tick to refresh idle timers
            _refreshTimer = new Timer() { Interval = Properties.Settings.Default.RefreshIntervalMs };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // Initialize application
            Application.ApplicationExit += OnApplicationExit;
            InitializeComponent();
            _trayIcon.Visible = true;

            UpdateAppEnabledState(_menuItem_Enabled.Checked);
        }

        private void InitializeComponent()
        {
            // Initialize Tray icon
            _trayIcon = new NotifyIcon
            {
                Text = Properties.Settings.Default.AppName,
                Icon = Properties.Resources.TrayIcon
            };
            _trayIcon.Click += Click_TrayIcon;

            // Create tray menu items
            var _MenuItem_Close = new ToolStripMenuItem("Close");
            _MenuItem_Close.Click += Click_Close;

            _menuItem_AutoStart = new ToolStripMenuItem("Autostart at login")
            {
                Checked = Tools.AutostartCheck(),
                ToolTipText="Should we start when you log in?"
            };
            _menuItem_AutoStart.Click += Click_AutoStart;

            _menuItem_RememberEnabledState = new ToolStripMenuItem("Remember enabled state")
            {
                Checked = Properties.Settings.Default.SaveEnabledState,
                ToolTipText = "Should we remember the enabled state between restarts?"
            };
            _menuItem_RememberEnabledState.Click += Click_SaveEnabledState;

            _menuItem_Enabled = new ToolStripMenuItem("Enabled")
            {
                Checked = !Properties.Settings.Default.SaveEnabledState || Properties.Settings.Default.EnabledState,
                ToolTipText="Are we enabled right now?"
            };
            _menuItem_Enabled.Click += Click_Enabled;

            _menuItem_DisplayRequired = new ToolStripMenuItem("Keep screen on")
            {
                Checked = !Properties.Settings.Default.DisplayRequired,
                ToolTipText="If display should be kept always on in addition to keeping the system on."
            };
            _menuItem_DisplayRequired.Click += Click_DisplayRequired;
            Click_DisplayRequired(null, null);

            // Initialize context menu with created items
            _trayIcon.ContextMenuStrip = new ContextMenuStrip();
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_AutoStart);
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_DisplayRequired);
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_RememberEnabledState);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_Enabled);
            _trayIcon.ContextMenuStrip.Items.Add(_MenuItem_Close);
        }

        // EVENT HANDLERS
        /// <summary> Tray icon click (the icon ITSELF) - enable/disable.</summary>
        private void Click_TrayIcon(object sender, EventArgs e)
        {
            var e2 = e as MouseEventArgs;
            if (e2.Button == MouseButtons.Left)
                Click_Enabled(sender, e);
        }

        /// <summary> Click on "Keep screen on" menu item - Toggle display required flag.</summary>
        private void Click_DisplayRequired(object sender, EventArgs e)
        {
            var OriginalState = _menuItem_DisplayRequired.Checked;

            DisarmExecutionState();

            _menuItem_DisplayRequired.Checked = !OriginalState;
            Properties.Settings.Default.DisplayRequired = !OriginalState;
            ExecutionMode = OriginalState ? ExecutionMode.DisableFlag(EXECUTION_STATE.ES_DISPLAY_REQUIRED)
                                          : ExecutionMode.EnableFlag(EXECUTION_STATE.ES_DISPLAY_REQUIRED);

            ArmExecutionState();

            Properties.Settings.Default.Save();
        }

        /// <summary> Click on "Enabled" menu item - Toggle the enabled state and update the menu item accordingly.</summary>
        private void Click_Enabled(object sender, EventArgs e)
        {
            _menuItem_Enabled.Checked = !_menuItem_Enabled.Checked;
            UpdateAppEnabledState(_menuItem_Enabled.Checked);
            SaveEnabledState(_menuItem_Enabled.Checked);
        }

        /// <summary> Click on "Autostart at login" menu item - Toggle the autostart state and update the menu item accordingly.</summary>
        private void Click_AutoStart(object sender, EventArgs e)
        {
            // Toggle the autostart state (depending on original state) and update the menu item accordingly
            // Note the reversed result for Disable. This is because function returns the opposite of what we want to set.
            _menuItem_AutoStart.Checked = _menuItem_AutoStart.Checked ? !Tools.AutostartDisable()
                                                                      :  Tools.AutostartEnable();
        }

        /// <summary> Click on "Remember enabled state" menu item - Toggle the state and save it and the current state to settings.</summary>
        private void Click_SaveEnabledState(object sender, EventArgs e)
        {
            _menuItem_RememberEnabledState.Checked = !_menuItem_RememberEnabledState.Checked;
            Properties.Settings.Default.SaveEnabledState = _menuItem_RememberEnabledState.Checked;
            Properties.Settings.Default.EnabledState = _menuItem_Enabled.Checked;
            Properties.Settings.Default.Save();
        }

        /// <summary> Close context menu item click - exit the application.</summary>
        private void Click_Close(object sender, EventArgs e)
        {
            Application.Exit();
        }

        /// <summary> On application exit - hide tray icon, disarm execution state and dispose the timer.</summary>
        private void OnApplicationExit(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            DisarmExecutionState();
            _refreshTimer.Dispose();
        }

        /// <summary> Timer tick to refresh PC-required lock. </summary>
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            WinU.SetThreadExecutionState(ExecutionMode);
        }

        // PRIVATE METHODS
        /// <summary> Arm the execution state with the __current__ ExecutionMode.</summary>
        private void ArmExecutionState()
        {
            RefreshTimer_Tick(null, null);
            _refreshTimer.Start();
        }

        /// <summary> Disarm the execution state. If ES_CONTINUOUS was used, it will be released.</summary>
        private void DisarmExecutionState()
        {
            _refreshTimer.Stop();
            // If we had ES_CONTINUOUS enabled - we have to release it by calling SetThreadExecutionState with it alone (i.e. with no other flags).
            if (ExecutionMode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS))
                WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        }

        /// <summary> Save current enabled state to settings. Actually saves only if the Save flag is enabled.</summary>
        private static void SaveEnabledState(bool state)
        {
            if (Properties.Settings.Default.SaveEnabledState)
            {
                Properties.Settings.Default.EnabledState = state;
                Properties.Settings.Default.Save();
            }
        }

        /// <summary> Update the application state to reflect enabled state - tray icon and execution state timer.</summary>
        private void UpdateAppEnabledState(bool state)
        {
            if (state)
            {
                _trayIcon.Icon = Properties.Resources.TrayIcon;
                ArmExecutionState();
            }
            else
            {
                _trayIcon.Icon = Properties.Resources.TrayIconInactive;
                DisarmExecutionState();
            }
        }
    }
}