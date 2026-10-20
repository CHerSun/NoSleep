using System;
using System.Drawing;
using System.Windows.Forms;
using NoSleep.Properties;

namespace NoSleep
{
    internal partial class ConfigureAppsForm : Form
    {
        private readonly TrayIcon mainForm;

        public ConfigureAppsForm(TrayIcon main)
        {
            // Back-reference for immediate apps watchlist toggling
            mainForm = main;

            // Initialize the form
            InitializeComponent();
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            // Initialize the state
            dataGridViewApps.AutoGenerateColumns = false;
            dataGridViewApps.DataSource = mainForm.WatchedApps;
            UpdateRemoveState();
            UpdateEnabledState(mainForm.UserWatchingEnabled);
        }

        private void ButtonAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = Strings.FileDlg_FilterExecutable + "|*.exe|" + Strings.FileDlg_FilterAllFiles + "|*.*";
                dlg.Title = Strings.FileDlg_Title;
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                var app = AppEntry.FromExePath(dlg.FileName);
                mainForm.WatchedApps.Add(app);
            }
        }

        private void ButtonRemove_Click(object sender, EventArgs e)
        {
            if (dataGridViewApps.SelectedRows.Count == 0)
                return;

            var row = dataGridViewApps.SelectedRows[0];
            if (row.DataBoundItem is AppEntry item)
                mainForm.WatchedApps.Remove(item);
        }

        private void UpdateEnabledState(bool enabled)
        {
            labelWatchState.Text = enabled ? Strings.Btn_AppsWatchingEnabled : Strings.Btn_AppsWatchingDisabled;
            labelWatchState.ForeColor = enabled ? Color.DarkGreen : Color.Firebrick;
            buttonEnable.Text = enabled ? Strings.Btn_Disable : Strings.Btn_Enable;
            mainForm.UserWatchingEnabled = enabled;
        }
        private void ButtonEnable_Click(object sender, EventArgs e)
        {
            UpdateEnabledState(!mainForm.UserWatchingEnabled);
        }

        private void DataGridViewApps_SelectionChanged(object sender, EventArgs e)
        {
            UpdateRemoveState();
        }

        private void UpdateRemoveState()
        {
            buttonRemove.Enabled = dataGridViewApps.SelectedRows.Count > 0;
        }
    }
}
