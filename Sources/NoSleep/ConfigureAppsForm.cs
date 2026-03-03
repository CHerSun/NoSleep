using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace NoSleep
{
    public partial class ConfigureAppsForm : Form
    {
        private BindingList<AppEntry> appsBinding;

        public ConfigureAppsForm()
        {
            InitializeComponent();

            var loaded = AppsConfig.Load();
            appsBinding = new BindingList<AppEntry>(loaded);
            appsBinding.ListChanged += (s, e) => SaveApps();

            dataGridViewApps.AutoGenerateColumns = false;
            dataGridViewApps.DataSource = appsBinding;
            UpdateRemoveState();
        }

        private void SaveApps()
        {
            try
            {
                AppsConfig.Save(appsBinding.ToList());
            }
            catch
            {
                // ignore save errors
            }
        }

        private void buttonAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
              dlg.Filter = "Executable|*.exe|All files|*.*";
              dlg.Title = "Select application executable";
              if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

              var path = dlg.FileName;
              var name = System.IO.Path.GetFileNameWithoutExtension(path);
              appsBinding.Add(new AppEntry { Name = name, ExePath = path });
            }
        }

        private void buttonRemove_Click(object sender, EventArgs e)
        {
            if (dataGridViewApps.SelectedRows.Count == 0)
                return;
            
            var row = dataGridViewApps.SelectedRows[0];
            if (row.DataBoundItem is AppEntry item)
            {
                appsBinding.Remove(item);
            }
        }

        private void dataGridViewApps_SelectionChanged(object sender, EventArgs e)
        {
            UpdateRemoveState();
        }

        private void UpdateRemoveState()
        {
            buttonRemove.Enabled = dataGridViewApps.SelectedRows.Count > 0;
        }
    }
}
