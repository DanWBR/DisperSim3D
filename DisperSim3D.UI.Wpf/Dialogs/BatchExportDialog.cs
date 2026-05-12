using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class BatchExportDialog : Form
    {
        private CheckedListBox lstPresets;
        private NumericUpDown nudWidth;
        private NumericUpDown nudHeight;
        private TextBox txtFolder;

        public List<CameraPreset> SelectedPresets { get; private set; } = new List<CameraPreset>();
        public int ImageWidth { get; private set; } = 1920;
        public int ImageHeight { get; private set; } = 1080;
        public string OutputFolder { get; private set; }

        public BatchExportDialog(List<CameraPreset> presets)
        {
            BuildUI(presets);
        }

        private void BuildUI(List<CameraPreset> presets)
        {
            this.Text = "Batch Export Images";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            var dpi = DeviceDpi / 96f;
            this.Size = new System.Drawing.Size((int)(400 * dpi), (int)(360 * dpi));
            this.Padding = new Padding((int)(10 * dpi));

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 5
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            lstPresets = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            foreach (var p in presets)
                lstPresets.Items.Add(p.Name, true);

            var lblPresets = new Label
            {
                Text = "Camera presets:\nSelect which saved camera angles to render. One PNG is generated per checked preset.",
                AutoSize = true,
                MaximumSize = new System.Drawing.Size((int)(360 * dpi), 0)
            };
            table.Controls.Add(lblPresets, 0, 0);
            table.SetColumnSpan(lstPresets, 2);
            table.Controls.Add(lstPresets, 0, 1);

            nudWidth = new NumericUpDown { Minimum = 320, Maximum = 7680, Value = 1920, Dock = DockStyle.Fill };
            nudHeight = new NumericUpDown { Minimum = 240, Maximum = 4320, Value = 1080, Dock = DockStyle.Fill };

            table.Controls.Add(new Label { Text = "Width:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            table.Controls.Add(nudWidth, 1, 2);
            table.Controls.Add(new Label { Text = "Height:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            table.Controls.Add(nudHeight, 1, 3);

            var folderPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 2, RowCount = 1
            };
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtFolder = new TextBox { Dock = DockStyle.Fill, Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
            var btnBrowse = new Button { Text = "...", AutoSize = true };
            btnBrowse.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                        txtFolder.Text = fbd.SelectedPath;
                }
            };
            folderPanel.Controls.Add(txtFolder, 0, 0);
            folderPanel.Controls.Add(btnBrowse, 1, 0);
            table.Controls.Add(new Label { Text = "Output:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            table.Controls.Add(folderPanel, 1, 4);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "Export", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += (s, e) =>
            {
                ImageWidth = (int)nudWidth.Value;
                ImageHeight = (int)nudHeight.Value;
                OutputFolder = txtFolder.Text;
                SelectedPresets.Clear();
                for (int i = 0; i < lstPresets.Items.Count; i++)
                {
                    if (lstPresets.GetItemChecked(i))
                        SelectedPresets.Add(presets[i]);
                }
            };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(btnCancel, 1, 0);
            buttons.Controls.Add(btnOK, 2, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            this.Controls.Add(table);
            this.Controls.Add(buttons);
            this.ApplyDpiScaling();
        }
    }
}
