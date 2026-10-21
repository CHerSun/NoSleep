using NoSleep.Properties;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace NoSleep
{
    /// <summary>
    /// UI shell: tray icon + context menu. Holds no prevention logic - user actions are
    /// written into the settings and pushed into the <see cref="SleepController"/>, and all
    /// visuals are re-rendered from the controller's StateChanged event (one-way data flow).
    /// </summary>
    internal class TrayIcon : ApplicationContext
    {
        // --- UI elements ---
        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _menuItem_ConfigureApps;
        private ToolStripMenuItem _menuItem_Enabled;
        private ToolStripMenuItem _menuItem_DisplayRequired;
        private ToolStripMenuItem _menuItem_AutoStart;
        private ToolStripMenuItem _menuItem_RememberEnabledState;
        // --- Timers ---
        /// <summary> While the inactive icon is shown: watches the Windows color mode to keep the "zzz" visible on dark taskbars. </summary>
        private readonly Timer _themeWatchTimer;
        /// <summary> Cadence of <see cref="_themeWatchTimer"/>; matches the app's other timers. </summary>
        private const int ThemePollIntervalMs = 10000;
        // --- Runtime state ---
        /// <summary> Last taskbar color mode answer; the poll tick compares against it to skip redundant updates. </summary>
        private bool? _taskbarLight;

        // --- domains ---
        private readonly SleepController _controller;
        private readonly AppMonitor _appMonitor;

        /// <summary> User-controlled watched apps list. </summary>
        public BindingList<AppEntry> WatchedApps { get; }

        #region USER SETTINGS (persisted) - thin wrappers: persist + feed the controller
        /// <summary> Whether the app is enabled at all. </summary>
        public bool UserEnabled
        {
            get => Settings.Default.EnabledState;
            set
            {
                if (Settings.Default.EnabledState == value)
                    return;
                Settings.Default.EnabledState = value;
                Settings.Default.Save();
                _controller.SetUserEnabled(value);
            }
        }
        /// <summary> Whether the user wants to persist the enabled state between reruns. </summary>
        public bool UserPersistEnabled
        {
            get => Settings.Default.SaveEnabledState;
            set
            {
                if (Settings.Default.SaveEnabledState == value)
                    return;
                Settings.Default.SaveEnabledState = value;
                Settings.Default.Save();
            }
        }
        /// <summary> Whether apps watching is enabled at all. </summary>
        public bool UserWatchingEnabled
        {
            get => Settings.Default.WatchedAppsEnabled;
            set
            {
                if (Settings.Default.WatchedAppsEnabled == value)
                    return;
                Settings.Default.WatchedAppsEnabled = value;
                Settings.Default.Save();
                SyncWatching();
            }
        }
        /// <summary> Whether the display is kept on while prevention is active. </summary>
        public bool UserKeepScreenOn
        {
            get => Settings.Default.DisplayRequired;
            set
            {
                if (Settings.Default.DisplayRequired == value)
                    return;
                Settings.Default.DisplayRequired = value;
                Settings.Default.Save();
                _controller.SetKeepScreenOn(value);
            }
        }
        public bool UserAutoStart
        {
            get => Tools.AutostartCheck();
            set
            {
                if (value)
                    Tools.AutostartEnable();
                else
                    Tools.AutostartDisable();
            }
        }
        #endregion

        #region EFFECTIVE STATES (read-only for UI)
        /// <summary> If we effectively prevent sleep right now? </summary>
        public bool EffectivePreventSleep => _controller.EffectivePreventSleep;
        /// <summary> If Apps watching feature is active right now? </summary>
        public bool IsWatchingFeatureActive =>
            Settings.Default.WatchedAppsEnabled && WatchedApps.Count > 0;
        #endregion

        // CONSTRUCTOR

        public TrayIcon()
        {
            // FIX: Initialize apps list, if it is not initialized (C# has it at null, if not initialized)
            if (Settings.Default.WatchedApps == null)
                Settings.Default.WatchedApps = new System.Collections.Specialized.StringCollection();

            // Load watched apps from settings; persist on every change
            WatchedApps = new BindingList<AppEntry>(AppsConfig.Load());
            WatchedApps.ListChanged += (s, e) =>
            {
                AppsConfig.Save(WatchedApps.ToList());
                SyncWatching(); // list edits may toggle the watching feature on/off
            };

            // Initialize the taskbar color mode watcher (runs only while the inactive icon is shown)
            _themeWatchTimer = new Timer { Interval = ThemePollIntervalMs };
            _themeWatchTimer.Tick += ThemeWatchTimer_Tick;

            // Initialize application
            Application.ApplicationExit += OnApplicationExit;
            InitializeComponent();
            _trayIcon.Visible = true;

            // Domains. Wired after the UI exists: state transitions below refresh the menu and icon.
            _appMonitor = new AppMonitor(WatchedApps, Settings.Default.WatchAppsIntervalMs);
            _controller = new SleepController(new ExecutionStateBackend(Settings.Default.RefreshIntervalMs), _appMonitor);
            _controller.StateChanged += (s, e) => RefreshFromState();

            // Apply initial state.
            _appMonitor.Initialize(); // real watch answer before the first Apply, not an assumption
            _controller.SetKeepScreenOn(Settings.Default.DisplayRequired);
            // BEHAVIOR: initial Enabled state depends on the user preference for persistence between restarts.
            _controller.SetUserEnabled(!Settings.Default.SaveEnabledState || Settings.Default.EnabledState);
            SyncWatching();
            _controller.Apply();
        }

        private void InitializeComponent()
        {
            // Initialize Tray icon
            _trayIcon = new NotifyIcon
            {
                Text = Settings.Default.AppName,
                Icon = Resources.TrayIcon
            };
            _trayIcon.Click += Click_TrayIcon;

            // Create tray menu items
            var _MenuItem_Close = new ToolStripMenuItem(Strings.TrayMenu_Close);
            _MenuItem_Close.Click += (e, s) => Application.Exit();

            _menuItem_AutoStart = new ToolStripMenuItem(Strings.TrayMenu_Autostart)
            {
                Checked = UserAutoStart,
                ToolTipText = Strings.Tooltip_Autostart
            };
            _menuItem_AutoStart.Click += Click_AutoStart;

            _menuItem_RememberEnabledState = new ToolStripMenuItem(Strings.TrayMenu_RememberState)
            {
                Checked = UserPersistEnabled,
                ToolTipText = Strings.Tooltip_RememberState
            };
            _menuItem_RememberEnabledState.Click += Click_SaveEnabledState;

            _menuItem_ConfigureApps = new ToolStripMenuItem(Strings.TrayMenu_ConfigureApps)
            {
                Checked = IsWatchingFeatureActive,
                ToolTipText = Strings.Tooltip_ConfigureApps
            };
            _menuItem_ConfigureApps.Click += Click_ConfigureApps;

            _menuItem_Enabled = new ToolStripMenuItem(Strings.TrayMenu_Enabled)
            {
                Checked = !Settings.Default.SaveEnabledState || Settings.Default.EnabledState,
                ToolTipText = Strings.Tooltip_Enabled
            };
            _menuItem_Enabled.Click += Click_Enabled;

            _menuItem_DisplayRequired = new ToolStripMenuItem(Strings.TrayMenu_KeepScreenOn)
            {
                Checked = Settings.Default.DisplayRequired,
                ToolTipText = Strings.Tooltip_KeepScreenOn
            };
            _menuItem_DisplayRequired.Click += Click_DisplayRequired;

            // Sleep-mode indicator: non-interactive info line at the very top (issue #8).
            // The mode cannot change without a reboot, so the text is fixed for the run.
            var modeText = PowerPlatform.SleepMode == SleepMode.ModernStandby ? Strings.TrayMenu_SleepModeModern
                        : PowerPlatform.SleepMode == SleepMode.Classic ? Strings.TrayMenu_SleepModeClassic
                        : Strings.TrayMenu_SleepModeUnknown;
            var modeIndicator = new ToolStripControlHost(new Label
            {
                Text = modeText,
                AutoSize = true,
                Enabled = false, // grayed on purpose: informational, not interactive
                Margin = new Padding(8, 4, 8, 4)
            });

            // Initialize context menu with created items
            _trayIcon.ContextMenuStrip = new ContextMenuStrip();
            _trayIcon.ContextMenuStrip.Items.Add(modeIndicator);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_AutoStart);
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_DisplayRequired);
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_RememberEnabledState);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_ConfigureApps);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_Enabled);
            _trayIcon.ContextMenuStrip.Items.Add(_MenuItem_Close);
        }

        /// <summary> Feed the watching-feature state (enabled setting + non-empty list) into the controller. </summary>
        private void SyncWatching()
        {
            _controller.SetWatchingActive(IsWatchingFeatureActive);
            RefreshFromState(); // the checked state of "Configure apps" depends on the list too
        }

        /// <summary> The single UI render: menu checked states, icon and the theme watcher - all from the current state. </summary>
        private void RefreshFromState()
        {
            _menuItem_Enabled.Checked = _controller.UserEnabled;
            _menuItem_DisplayRequired.Checked = _controller.KeepScreenOn;
            _menuItem_ConfigureApps.Checked = IsWatchingFeatureActive;

            // The color mode watcher is only needed while the inactive icon is displayed.
            _themeWatchTimer.Enabled = !EffectivePreventSleep;
            if (EffectivePreventSleep)
                _trayIcon.Icon = Resources.TrayIcon;
            else
                UpdateInactiveTrayIcon();
        }

        // PRIVATE EVENT HANDLERS
        /// <summary> Tray icon click (the icon ITSELF) - enable/disable global state.</summary>
        private void Click_TrayIcon(object sender, EventArgs e)
        {
            var e2 = e as MouseEventArgs;
            if (e2 != null && e2.Button == MouseButtons.Left)
                UserEnabled = !UserEnabled;
        }

        /// <summary> Click on "Enabled" menu item - toggle the enabled state.</summary>
        private void Click_Enabled(object sender, EventArgs e)
        {
            UserEnabled = !UserEnabled;
        }

        /// <summary> Click on "Keep screen on" menu item - toggle the display required flag.</summary>
        private void Click_DisplayRequired(object sender, EventArgs e)
        {
            UserKeepScreenOn = !UserKeepScreenOn;
        }

        /// <summary> Click on "Configure apps" menu item - show the form to adjust the apps watch list. </summary>
        private void Click_ConfigureApps(object sender, EventArgs e)
        {
            using (var configForm = new ConfigureAppsForm(this))
                configForm.ShowDialog();
        }

        /// <summary> Click on "Autostart at login" menu item - toggle the autostart state and update the menu item accordingly.</summary>
        private void Click_AutoStart(object sender, EventArgs e)
        {
            UserAutoStart = !_menuItem_AutoStart.Checked;
            // Refresh menu item checked state from the actual AutoStart state (if the program was able to change it).
            _menuItem_AutoStart.Checked = UserAutoStart;
        }

        /// <summary> Click on "Remember enabled state" menu item - toggle the persistence of the enabled state.</summary>
        private void Click_SaveEnabledState(object sender, EventArgs e)
        {
            UserPersistEnabled = !UserPersistEnabled;
            _menuItem_RememberEnabledState.Checked = UserPersistEnabled;
        }

        /// <summary> On application exit - hide tray icon and release everything the domains asserted.</summary>
        private void OnApplicationExit(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _controller.Dispose();
            _themeWatchTimer.Dispose();
        }

        /// <summary> Taskbar color mode tick - refresh the inactive icon if the user switched light/dark theme.</summary>
        private void ThemeWatchTimer_Tick(object sender, EventArgs e)
        {
            // Skip the icon update while the theme answer is unchanged (the icon is re-applied on every state change anyway).
            if (!EffectivePreventSleep && _taskbarLight != WinU.IsTaskbarLightTheme())
                UpdateInactiveTrayIcon();
        }

        /// <summary> Sets the inactive ("zzz") tray icon, white on dark taskbars so it stays visible (issue #16).
        /// Always applies: the enabled state overwrites the icon, so it must be re-set on every transition into the inactive state. </summary>
        private void UpdateInactiveTrayIcon()
        {
            _taskbarLight = WinU.IsTaskbarLightTheme();
            _trayIcon.Icon = _taskbarLight.Value ? Resources.TrayIconInactive : Resources.TrayIconInactiveWhite;
        }
    }
}
