using System;
using System.Windows.Forms;
using DisperSim3D.Core;

namespace DisperSim3D.Dialogs
{
    public class MonitorPointDialog : Form
    {
        private TextBox txtName;
        private NumericUpDown nudX;
        private NumericUpDown nudY;
        private NumericUpDown nudZ;

        public string MonitorName { get; private set; } = "Monitor1";
        public double PosX { get; private set; }
        public double PosY { get; private set; }
        public double PosZ { get; private set; }

        public MonitorPointDialog()
        {
            BuildUI();
        }

        public MonitorPointDialog(string name, double x, double y, double z) : this()
        {
            txtName.Text = name;
            nudX.Value = (decimal)x;
            nudY.Value = (decimal)y;
            nudZ.Value = (decimal)z;
        }

        private void BuildUI()
        {
            this.Text = "Add Monitor Point";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var dpi = DeviceDpi / 96f;
            this.Padding = new Padding((int)(10 * dpi));

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 4,
                Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(180 * dpi)));

            int row = 0;
            txtName = new TextBox { Text = "Monitor1", Dock = DockStyle.Fill };
            AddRow(table, row++, "Name:", txtName);

            nudX = MakeNud(-10000m, 10000m, 0m, 2);
            AddRow(table, row++, "X (m):", nudX);

            nudY = MakeNud(-10000m, 10000m, 0m, 2);
            AddRow(table, row++, "Y (m):", nudY);

            nudZ = MakeNud(-10000m, 10000m, 2m, 2);
            AddRow(table, row++, "Z (m):", nudZ);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += (s, e) =>
            {
                MonitorName = txtName.Text;
                PosX = (double)nudX.Value;
                PosY = (double)nudY.Value;
                PosZ = (double)nudZ.Value;
            };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(btnCancel, 1, 0);
            buttons.Controls.Add(btnOK, 2, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            outerLayout.Controls.Add(table, 0, 0);
            outerLayout.Controls.Add(buttons, 0, 1);
            this.Controls.Add(outerLayout);
            this.ApplyDpiScaling();
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
            table.Controls.Add(lbl, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            table.Controls.Add(control, 1, row);
        }

        private static NumericUpDown MakeNud(decimal min, decimal max, decimal value, int decimals)
        {
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals,
                Dock = DockStyle.Fill
            };
            nud.Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1;
            return nud;
        }
    }
}
