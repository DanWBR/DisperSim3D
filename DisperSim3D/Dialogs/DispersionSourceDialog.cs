using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class DispersionSourceDialog : Form
    {
        private TextBox txtName;
        private ComboBox cmbGasPreset;
        private NumericUpDown nudMolarMass;
        private NumericUpDown nudLFL;
        private NumericUpDown nudIDLH;
        private NumericUpDown nudReleaseRate;
        private NumericUpDown nudDuration;
        private NumericUpDown nudPuffInterval;
        private NumericUpDown nudHeightOffset;
        private NumericUpDown nudAzimuth;
        private NumericUpDown nudElevation;

        public string SourceName { get; private set; } = "Source1";
        public GasProperties Gas { get; private set; }
        public double ReleaseRateKgPerS { get; private set; } = 0.5;
        public double ReleaseDurationS { get; private set; } = 60;
        public double PuffIntervalS { get; private set; } = 1.0;
        public double HeightOffset { get; private set; } = 2.0;
        public double AzimuthDeg { get; private set; } = 0;
        public double ElevationDeg { get; private set; } = 0;

        private double _defaultAzimuth;

        public DispersionSourceDialog() : this(0) { }

        public DispersionSourceDialog(double windDirectionDeg)
        {
            _defaultAzimuth = windDirectionDeg;
            Gas = GasProperties.CreateMethane();
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "Add Release Source";
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
                RowCount = 11,
                Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(180 * dpi)));

            int row = 0;
            txtName = new TextBox { Text = "Source1", Dock = DockStyle.Fill };
            AddRow(table, row++, "Name:", txtName);

            cmbGasPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbGasPreset.Items.AddRange(new object[] { "Methane", "H2S", "Ammonia", "Custom" });
            cmbGasPreset.SelectedIndex = 0;
            cmbGasPreset.SelectedIndexChanged += CmbGasPreset_Changed;
            AddRow(table, row++, "Gas Preset:", cmbGasPreset);

            nudMolarMass = MakeNud(0.001m, 1.0m, 0.016m, 3);
            nudMolarMass.Enabled = false;
            AddRow(table, row++, "Molar Mass (kg/mol):", nudMolarMass);

            nudLFL = MakeNud(0.0m, 10.0m, 0.033m, 4);
            nudLFL.Enabled = false;
            AddRow(table, row++, "LFL (kg/m³):", nudLFL);

            nudIDLH = MakeNud(0.0m, 10.0m, 0.033m, 4);
            nudIDLH.Enabled = false;
            AddRow(table, row++, "IDLH (kg/m³):", nudIDLH);

            nudReleaseRate = MakeNud(0.001m, 1000.0m, 0.5m, 3);
            AddRow(table, row++, "Release Rate (kg/s):", nudReleaseRate);

            nudDuration = MakeNud(1m, 100000m, 60m, 0);
            AddRow(table, row++, "Duration (s):", nudDuration);

            nudPuffInterval = MakeNud(0.1m, 60m, 1.0m, 1);
            AddRow(table, row++, "Puff Interval (s):", nudPuffInterval);

            nudHeightOffset = MakeNud(0m, 100m, 2.0m, 1);
            AddRow(table, row++, "Height Offset (m):", nudHeightOffset);

            nudAzimuth = MakeNud(0m, 359m, (decimal)_defaultAzimuth, 0);
            AddRow(table, row++, "Release Azimuth (°):", nudAzimuth);

            nudElevation = MakeNud(-90m, 90m, 0m, 0);
            AddRow(table, row++, "Release Elevation (°):", nudElevation);

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
            btnOK.Click += BtnOK_Click;
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(btnOK, 1, 0);
            buttons.Controls.Add(btnCancel, 2, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            outerLayout.Controls.Add(table, 0, 0);
            outerLayout.Controls.Add(buttons, 0, 1);
            this.Controls.Add(outerLayout);
            this.ApplyDpiScaling();
        }

        private void CmbGasPreset_Changed(object sender, EventArgs e)
        {
            bool custom = cmbGasPreset.SelectedIndex == 3;
            nudMolarMass.Enabled = custom;
            nudLFL.Enabled = custom;
            nudIDLH.Enabled = custom;

            switch (cmbGasPreset.SelectedIndex)
            {
                case 0: nudMolarMass.Value = 0.016m; nudLFL.Value = 0.033m; nudIDLH.Value = 0.033m; break;
                case 1: nudMolarMass.Value = 0.034m; nudLFL.Value = 0.028m; nudIDLH.Value = 0.070m; break;
                case 2: nudMolarMass.Value = 0.017m; nudLFL.Value = 0.110m; nudIDLH.Value = 0.018m; break;
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SourceName = txtName.Text;
            ReleaseRateKgPerS = (double)nudReleaseRate.Value;
            ReleaseDurationS = (double)nudDuration.Value;
            PuffIntervalS = (double)nudPuffInterval.Value;
            HeightOffset = (double)nudHeightOffset.Value;
            AzimuthDeg = (double)nudAzimuth.Value;
            ElevationDeg = (double)nudElevation.Value;

            switch (cmbGasPreset.SelectedIndex)
            {
                case 0: Gas = GasProperties.CreateMethane(); break;
                case 1: Gas = GasProperties.CreateH2S(); break;
                case 2: Gas = GasProperties.CreateAmmonia(); break;
                default:
                    Gas = new GasProperties
                    {
                        Name = "Custom",
                        MolarMass = (double)nudMolarMass.Value,
                        LFL = (double)nudLFL.Value,
                        IDLH = (double)nudIDLH.Value
                    };
                    break;
            }
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
