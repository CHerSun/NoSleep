using NoSleep.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NoSleep
{
    internal class TrayIcon : ApplicationContext
    {
        /// <summary> ExecutionMode defines how exactly sleep prevention is made. Mutable at runtime. </summary>
        private EXECUTION_STATE ExecutionMode = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED |
                                                EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_AWAYMODE_REQUIRED;

        // PRIVATE VARIABLES
        // --- UI elements ---
        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _menuItem_ConfigureApps;
        private ToolStripMenuItem _menuItem_Enabled;
        private ToolStripMenuItem _menuItem_DisplayRequired;
        private ToolStripMenuItem _menuItem_AutoStart;
        private ToolStripMenuItem _menuItem_RememberEnabledState;
        // --- Timers ---
        /// <summary> Keeps the system awake (sole purpose). </summary>
        private readonly Timer _refreshTimer;
        /// <summary> Checks running apps and controlls effective state. </summary>
        private readonly Timer _appsWatchTimer;
        // --- Runtime state ---
        /// <summary> Private flag to show if we found app on the watch list </summary>
        private bool _anyWatchedAppRunning;

        // PROPERTIES
        #region USER SETTINGS (persisted)
        /// <summary> Get or Set user-controlled setting -- whether the app is enabled at all </summary>
        public bool UserEnabled
        {
            get => Settings.Default.EnabledState;
            set
            {
                if (Settings.Default.EnabledState == value)
                    return;
                Settings.Default.EnabledState = value;
                Settings.Default.Save();
                OnUserSettingsChanged(); // Trigger point - user action, we need to update state.
            }
        }
        /// <summary> Get or Set user-controlled setting -- whether the user wants to persist enabled state between reruns </summary>
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
        /// <summary> Get or Set user-controlled setting -- whether Apps Watching is enabled at all </summary>
        public bool UserWatchingEnabled
        {
            get => Settings.Default.WatchedAppsEnabled;
            set
            {
                if (Settings.Default.WatchedAppsEnabled == value)
                    return;
                Settings.Default.WatchedAppsEnabled = value;
                Settings.Default.Save();
                OnUserSettingsChanged(); // Trigger point - user action, we need to update state.
            }
        }
        /// <summary> User-controlled watched apps list. </summary>
        public BindingList<AppEntry> WatchedApps { get; } = new BindingList<AppEntry>();
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


        #region EFFECTIVE STATES (read‑only for UI)
        /// <summary> If we effectively prevent sleep right now? </summary>
        public bool EffectivePreventSleep =>
            UserEnabled && ((!(UserWatchingEnabled && (WatchedApps.Count>0))) || _anyWatchedAppRunning);
        /// <summary> If Apps watching feature is active right now? </summary>
        public bool IsWatchingFeatureActive =>
            UserWatchingEnabled && WatchedApps.Any();
        #endregion

        // CONSTRUCTOR

        public TrayIcon()
        {
            // FIX: Initialize apps list, if it is not initialized (C# has it at null, if not initialized)
            if (Settings.Default.WatchedApps == null)
                Settings.Default.WatchedApps = new System.Collections.Specialized.StringCollection();

            // Start up as if we've detected some app on watchlist (so that we don't block sleep prevention initially)
            _anyWatchedAppRunning = true;

            // Load watched apps from settings into our BindingList and add auto modification action
            WatchedApps = new BindingList<AppEntry>(AppsConfig.Load());
            WatchedApps.ListChanged += (s, e) => OnWatchedAppsChanged();

            // Initialize the sleep prevention timer with sole action of re-arming the state
            _refreshTimer = new Timer { Interval = Settings.Default.RefreshIntervalMs };
            _refreshTimer.Tick += (s, e) => WinU.SetThreadExecutionState(ExecutionMode);

            // Initialize the apps watching timer
            _appsWatchTimer = new Timer { Interval = Settings.Default.WatchAppsIntervalMs };
            _appsWatchTimer.Tick += AppsWatchTimer_Tick;

            // BEHAVIOR: Set initial Enabled state depending on user preference for persistence between restarts
            UserEnabled = !Settings.Default.SaveEnabledState || Settings.Default.EnabledState;

            // Initialize application
            Application.ApplicationExit += OnApplicationExit;
            InitializeComponent();
            _trayIcon.Visible = true;

            // Apply initial state
            OnUserSettingsChanged();
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
            var _MenuItem_Close = new ToolStripMenuItem("Close");
            _MenuItem_Close.Click += (e, s) => Application.Exit();

            _menuItem_AutoStart = new ToolStripMenuItem("Autostart at login")
            {
                Checked = UserAutoStart,
                ToolTipText = "Should we start when you log in?"
            };
            _menuItem_AutoStart.Click += Click_AutoStart;

            _menuItem_RememberEnabledState = new ToolStripMenuItem("Remember enabled state")
            {
                Checked = UserPersistEnabled,
                ToolTipText = "Should we remember the enabled state between restarts?"
            };
            _menuItem_RememberEnabledState.Click += Click_SaveEnabledState;

            _menuItem_ConfigureApps = new ToolStripMenuItem("Configure apps to monitor")
            {
                Checked = IsWatchingFeatureActive,
                ToolTipText = "Configure apps to keep the screen on when they are running."
            };
            _menuItem_ConfigureApps.Click += Click_ConfigureApps;

            _menuItem_Enabled = new ToolStripMenuItem("Enabled")
            {
                Checked = UserEnabled,
                ToolTipText = "Are we enabled right now?"
            };
            _menuItem_Enabled.Click += Click_Enabled;

            _menuItem_DisplayRequired = new ToolStripMenuItem("Keep screen on")
            {
                Checked = !Settings.Default.DisplayRequired,
                ToolTipText = "If display should be kept always on in addition to keeping the system on."
            };
            _menuItem_DisplayRequired.Click += Click_DisplayRequired;
            Click_DisplayRequired(null, null);

            // Initialize context menu with created items
            _trayIcon.ContextMenuStrip = new ContextMenuStrip();
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_AutoStart);
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_DisplayRequired);
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_RememberEnabledState);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_ConfigureApps);
            _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Add(_menuItem_Enabled);
            _trayIcon.ContextMenuStrip.Items.Add(_MenuItem_Close);
        }


        // PRIVATE EVENT HANDLERS
        /// <summary> Tray icon click (the icon ITSELF) - enable/disable global state.</summary>
        private void Click_TrayIcon(object sender, EventArgs e)
        {
            var e2 = e as MouseEventArgs;
            if (e2.Button == MouseButtons.Left)
                Click_Enabled(sender, e);
        }

        /// <summary> Click on "Keep screen on" menu item - Toggle display required flag.</summary>
        private void Click_DisplayRequired(object sender, EventArgs e)
        {
            // Toggle the setting
            bool newState = !_menuItem_DisplayRequired.Checked; // NOTE: Yes, MenuItem state here. Used for initial toggling from the opposite.
            if (Settings.Default.DisplayRequired != newState)
            {
                Settings.Default.DisplayRequired = newState;
                Settings.Default.Save();
            }
            _menuItem_DisplayRequired.Checked = newState;

            // Update the execution mode flags
            ExecutionMode = newState
                ? ExecutionMode.EnableFlag(EXECUTION_STATE.ES_DISPLAY_REQUIRED)
                : ExecutionMode.DisableFlag(EXECUTION_STATE.ES_DISPLAY_REQUIRED);

            // If sleep prevention is currently active, apply the new flags immediately
            if (_refreshTimer.Enabled)
                WinU.SetThreadExecutionState(ExecutionMode);
        }

        /// <summary> Click on "Enabled" menu item - Toggle the enabled state and update the menu item accordingly.</summary>
        private void Click_Enabled(object sender, EventArgs e)
        {
            _menuItem_Enabled.Checked = !_menuItem_Enabled.Checked;
            UserEnabled = _menuItem_Enabled.Checked;
        }

        /// <summary> Click on "Configure apps" menu item - show the form to adjust apps watch list. </summary>
        private void Click_ConfigureApps(object sender, EventArgs e)
        {
            using (var configForm = new ConfigureAppsForm(this))
                configForm.ShowDialog();
        }

        /// <summary> Click on "Autostart at login" menu item - Toggle the autostart state and update the menu item accordingly.</summary>
        private void Click_AutoStart(object sender, EventArgs e)
        {
            // Toggle the autostart state
            UserAutoStart = !_menuItem_AutoStart.Checked;
            // Refresh menu item checked state from actual AutoStart state (if program was able to change the state).
            _menuItem_AutoStart.Checked = UserAutoStart;
        }

        /// <summary> Click on "Remember enabled state" menu item - Toggle the state and save it and the current state to settings.</summary>
        private void Click_SaveEnabledState(object sender, EventArgs e)
        {
            _menuItem_RememberEnabledState.Checked = !_menuItem_RememberEnabledState.Checked;
            UserPersistEnabled = _menuItem_RememberEnabledState.Checked;
        }

        /// <summary> On application exit - hide tray icon, disarm execution state and dispose the timer.</summary>
        private void OnApplicationExit(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            DisarmExecutionState();
            _refreshTimer.Dispose();
            _appsWatchTimer.Dispose();
        }
        /// <summary> Event handler for the change of apps list </summary>
        private void OnWatchedAppsChanged()
        {
            // Save list to settings
            AppsConfig.Save(WatchedApps.ToList());
            // Clear cached HashSets
            _nameSet = _pathSet = null;
            // Re-evaluate watching timer and effective state
            OnUserSettingsChanged();
        }

        /// <summary> App watching timer tick - checks running apps</summary>
        private void AppsWatchTimer_Tick(object sender, EventArgs e)
        {
            var apps = WatchedApps.ToList();
            bool anyRunning = AreAnyConfiguredAppsRunning(apps);
            if (_anyWatchedAppRunning != anyRunning)
            {
                _anyWatchedAppRunning = anyRunning;
                // This change may affect EffectivePreventSleep
                ApplySleepPrevention();
            }
        }

        // PRIVATE METHODS
        /// <summary> Disarm the execution state. If ES_CONTINUOUS was used, it will be released.</summary>
        private void DisarmExecutionState()
        {
            _refreshTimer.Stop();
            // If we had ES_CONTINUOUS enabled - we have to release it by calling SetThreadExecutionState with it alone (i.e. with no other flags).
            if (ExecutionMode.HasFlag(EXECUTION_STATE.ES_CONTINUOUS))
                WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        }

        /// <summary> Caching watched apps paths </summary>
        private HashSet<string> _pathSet;
        /// <summary> Caching watched apps names </summary>
        private HashSet<string> _nameSet;
        /// <summary> Checks if any of Watched apps are currently running. </summary>
        private bool AreAnyConfiguredAppsRunning(List<AppEntry> apps)
        {
            // If there are no apps - nothing to watch. Explicit true, if we reached here somewhy.
            if (apps.Count == 0)
                return true;

            if (_pathSet is null)
            {
                // We have apps, but no cached paths? Prepare HashSets with paths and names
                _pathSet = apps.Select(app => app.ExePath)
                              .Where(path => !string.IsNullOrWhiteSpace(path))
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _nameSet = apps.Select(app => app.Name)
                              .Where(name => !string.IsNullOrWhiteSpace(name))
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            // Test running processes to see if we have any of the wanted apps running
            return Process.GetProcesses().Any(p =>
            {
                // Exception free Process path extraction.
                // Standard Process.MainModule throws exceptions, which consume quite a lot of cpu.
                string path = p.TryGetExecutablePath();
                if ((path != null) && _pathSet.Contains(path, StringComparer.OrdinalIgnoreCase))
                    return true;

                // Fallback to name-only detection, if we can't get process path.
                return _nameSet.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
            });
        }
        /// <summary> A single point to reflect user settings change into effective app state </summary>
        private void OnUserSettingsChanged()
        {
            // APPS WATCHING: timer & menu item checked state
            _appsWatchTimer.Enabled = IsWatchingFeatureActive;
            this._menuItem_ConfigureApps.Checked = IsWatchingFeatureActive;

            // SLEEP PREVENTION: recompute effective sleep prevention
            ApplySleepPrevention();
        }


        /// <summary> SLEEP PREVENTION: updates timer and app effective state </summary>
        private void ApplySleepPrevention()
        {
            if (EffectivePreventSleep)
            {
                // Arm refresh timer if not already
                if (!_refreshTimer.Enabled)
                {
                    WinU.SetThreadExecutionState(ExecutionMode); // immediate first call
                    _refreshTimer.Start();
                }
                _trayIcon.Icon = Resources.TrayIcon;
            }
            else
            {
                if (_refreshTimer.Enabled)
                {
                    _refreshTimer.Stop();
                    WinU.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS); // release
                }
                _trayIcon.Icon = Resources.TrayIconInactive;
            }
        }

    }
}
