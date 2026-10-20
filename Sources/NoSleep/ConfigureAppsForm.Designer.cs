namespace NoSleep
{
    partial class ConfigureAppsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.dataGridViewApps = new System.Windows.Forms.DataGridView();
            this.colName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colExePath = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.buttonAdd = new System.Windows.Forms.Button();
            this.buttonRemove = new System.Windows.Forms.Button();
            this.buttonEnable = new System.Windows.Forms.Button();
            this.buttonClose = new System.Windows.Forms.Button();
            this.labelWatchState = new System.Windows.Forms.Label();
            this.leftButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.rightButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.tableLayout = new System.Windows.Forms.TableLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewApps)).BeginInit();
            this.tableLayout.SuspendLayout();
            this.leftButtons.SuspendLayout();
            this.rightButtons.SuspendLayout();
            this.SuspendLayout();
            //
            // dataGridViewApps
            //
            this.dataGridViewApps.AllowUserToAddRows = false;
            this.dataGridViewApps.AllowUserToDeleteRows = false;
            this.dataGridViewApps.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewApps.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colName,
            this.colExePath});
            this.dataGridViewApps.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridViewApps.MultiSelect = false;
            this.dataGridViewApps.Name = "dataGridViewApps";
            this.dataGridViewApps.RowHeadersVisible = false;
            this.dataGridViewApps.RowHeadersWidth = 62;
            this.dataGridViewApps.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dataGridViewApps.TabIndex = 0;
            this.dataGridViewApps.SelectionChanged += new System.EventHandler(this.DataGridViewApps_SelectionChanged);
            //
            // colName
            //
            this.colName.DataPropertyName = "Name";
            this.colName.HeaderText = Properties.Strings.Col_Name;
            this.colName.MinimumWidth = 8;
            this.colName.Name = "colName";
            this.colName.Width = 200;
            //
            // colExePath
            //
            this.colExePath.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colExePath.DataPropertyName = "ExePath";
            this.colExePath.HeaderText = Properties.Strings.Col_ExePath;
            this.colExePath.MinimumWidth = 8;
            this.colExePath.Name = "colExePath";
            //
            // buttonAdd
            //
            this.buttonAdd.AutoSize = true;
            this.buttonAdd.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonAdd.Location = new System.Drawing.Point(0, 0);
            this.buttonAdd.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.buttonAdd.MinimumSize = new System.Drawing.Size(96, 0);
            this.buttonAdd.Name = "buttonAdd";
            this.buttonAdd.TabIndex = 1;
            this.buttonAdd.Text = Properties.Strings.Btn_Add;
            this.buttonAdd.UseVisualStyleBackColor = true;
            this.buttonAdd.Click += new System.EventHandler(this.ButtonAdd_Click);
            //
            // buttonRemove
            //
            this.buttonRemove.AutoSize = true;
            this.buttonRemove.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonRemove.Location = new System.Drawing.Point(0, 0);
            this.buttonRemove.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.buttonRemove.MinimumSize = new System.Drawing.Size(96, 0);
            this.buttonRemove.Name = "buttonRemove";
            this.buttonRemove.TabIndex = 2;
            this.buttonRemove.Text = Properties.Strings.Btn_Remove;
            this.buttonRemove.UseVisualStyleBackColor = true;
            this.buttonRemove.Click += new System.EventHandler(this.ButtonRemove_Click);
            //
            // buttonEnable
            //
            this.buttonEnable.AutoSize = true;
            this.buttonEnable.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonEnable.Location = new System.Drawing.Point(0, 0);
            this.buttonEnable.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.buttonEnable.MinimumSize = new System.Drawing.Size(96, 0);
            this.buttonEnable.Name = "buttonEnable";
            this.buttonEnable.TabIndex = 3;
            this.buttonEnable.UseVisualStyleBackColor = true;
            this.buttonEnable.Click += new System.EventHandler(this.ButtonEnable_Click);
            //
            // buttonClose
            //
            this.buttonClose.AutoSize = true;
            this.buttonClose.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonClose.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonClose.Location = new System.Drawing.Point(0, 0);
            this.buttonClose.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.buttonClose.MinimumSize = new System.Drawing.Size(96, 0);
            this.buttonClose.Name = "buttonClose";
            this.buttonClose.TabIndex = 4;
            this.buttonClose.Text = Properties.Strings.Btn_Close;
            this.buttonClose.UseVisualStyleBackColor = true;
            //
            // labelWatchState
            //
            this.labelWatchState.AutoSize = true;
            this.labelWatchState.Location = new System.Drawing.Point(0, 0);
            this.labelWatchState.Margin = new System.Windows.Forms.Padding(4, 14, 4, 6);
            this.labelWatchState.Name = "labelWatchState";
            this.labelWatchState.TabIndex = 5;
            //
            // leftButtons
            //
            this.leftButtons.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            this.leftButtons.AutoSize = true;
            this.leftButtons.Controls.Add(this.buttonAdd);
            this.leftButtons.Controls.Add(this.buttonRemove);
            this.leftButtons.Location = new System.Drawing.Point(0, 0);
            this.leftButtons.Margin = new System.Windows.Forms.Padding(0, 10, 0, 0);
            this.leftButtons.Name = "leftButtons";
            this.leftButtons.WrapContents = false;
            //
            // rightButtons
            //
            this.rightButtons.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.rightButtons.AutoSize = true;
            this.rightButtons.Controls.Add(this.labelWatchState);
            this.rightButtons.Controls.Add(this.buttonEnable);
            this.rightButtons.Controls.Add(this.buttonClose);
            this.rightButtons.Location = new System.Drawing.Point(0, 0);
            this.rightButtons.Margin = new System.Windows.Forms.Padding(0, 10, 0, 0);
            this.rightButtons.Name = "rightButtons";
            this.rightButtons.WrapContents = false;
            //
            // tableLayout
            //
            this.tableLayout.ColumnCount = 2;
            this.tableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayout.Controls.Add(this.dataGridViewApps, 0, 0);
            this.tableLayout.Controls.Add(this.leftButtons, 0, 1);
            this.tableLayout.Controls.Add(this.rightButtons, 1, 1);
            this.tableLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayout.Location = new System.Drawing.Point(0, 0);
            this.tableLayout.Name = "tableLayout";
            this.tableLayout.RowCount = 2;
            this.tableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tableLayout.SetColumnSpan(this.dataGridViewApps, 2);
            this.tableLayout.Size = new System.Drawing.Size(876, 555);
            //
            // ConfigureAppsForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(876, 555);
            this.Controls.Add(this.tableLayout);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ConfigureAppsForm";
            this.Padding = new System.Windows.Forms.Padding(12);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = Properties.Strings.Form_Title;
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewApps)).EndInit();
            this.tableLayout.ResumeLayout(false);
            this.tableLayout.PerformLayout();
            this.leftButtons.ResumeLayout(false);
            this.leftButtons.PerformLayout();
            this.rightButtons.ResumeLayout(false);
            this.rightButtons.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView dataGridViewApps;
        private System.Windows.Forms.Button buttonAdd;
        private System.Windows.Forms.Button buttonRemove;
        private System.Windows.Forms.DataGridViewTextBoxColumn colName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colExePath;
        private System.Windows.Forms.Button buttonEnable;
        private System.Windows.Forms.Button buttonClose;
        private System.Windows.Forms.Label labelWatchState;
        private System.Windows.Forms.FlowLayoutPanel leftButtons;
        private System.Windows.Forms.FlowLayoutPanel rightButtons;
        private System.Windows.Forms.TableLayoutPanel tableLayout;
    }
}
