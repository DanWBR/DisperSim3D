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
        private NumericUpDown nudPuffInterval;
        private NumericUpDown nudHeightOffset;
        private NumericUpDown nudAzimuth;
        private NumericUpDown nudElevation;

        public string SourceName { get; private set; } = "Source1";
        public GasProperties Gas { get; private set; }
        public double ReleaseRateKgPerS { get; private set; } = 0.5;
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
                RowCount = 10,
                Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(180 * dpi)));

            int row = 0;
            txtName = new TextBox { Text = "Source1", Dock = DockStyle.Fill };
            DialogHelpers.AddRowWithHelp(table, ref row, "Name:", txtName,
                "Identifier shown in the scene tree and result reports.");

            cmbGasPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbGasPreset.Items.AddRange(new object[] { "Methane", "H2S", "Ammonia", "Custom" });
            cmbGasPreset.SelectedIndex = 0;
            cmbGasPreset.SelectedIndexChanged += CmbGasPreset_Changed;
            DialogHelpers.AddRowWithHelp(table, ref row, "Gas Preset:", cmbGasPreset,
                "Pre-loaded gas properties. Choose 'Custom' to enter your own molar mass, LFL and IDLH.");

            nudMolarMass = MakeNud(0.001m, 1.0m, 0.016m, 3);
            nudMolarMass.Enabled = false;
            DialogHelpers.AddRowWithHelp(table, ref row, "Molar Mass (kg/mol):", nudMolarMass,
                "Molecular weight, used to compute density relative to air.");

            nudLFL = MakeNud(0.0m, 10.0m, 0.033m, 4);
            nudLFL.Enabled = false;
            DialogHelpers.AddRowWithHelp(table, ref row, "LFL (kg/m³):", nudLFL,
                "Lower Flammability Limit. Concentration above this in air can ignite.");

            nudIDLH = MakeNud(0.0m, 10.0m, 0.033m, 4);
            nudIDLH.Enabled = false;
            DialogHelpers.AddRowWithHelp(table, ref row, "IDLH (kg/m³):", nudIDLH,
                "Immediately Dangerous to Life and Health threshold (toxicity reference).");

            nudReleaseRate = MakeNud(0.001m, 1000.0m, 0.5m, 3);
            DialogHelpers.AddRowWithHelp(table, ref row, "Release Rate (kg/s):", nudReleaseRate,
                "Mass emission rate at the source.");

            nudPuffInterval = MakeNud(0.1m, 60m, 1.0m, 1);
            DialogHelpers.AddRowWithHelp(table, ref row, "Puff Interval (s):", nudPuffInterval,
                "Time between successive puff emissions in the Gaussian Puff model.");

            nudHeightOffset = MakeNud(0m, 100m, 2.0m, 1);
            DialogHelpers.AddRowWithHelp(table, ref row, "Height Offset (m):", nudHeightOffset,
                "Vertical offset from the source position (e.g. stack height above the unit).");

            nudAzimuth = MakeNud(0m, 359m, (decimal)_defaultAzimuth, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Release Azimuth (°):", nudAzimuth,
                "Horizontal direction of the initial jet (0°=N, 90°=E, 180°=S, 270°=W).");

            nudElevation = MakeNud(-90m, 90m, 0m, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Release Elevation (°):", nudElevation,
                "Vertical jet angle: 0° = horizontal, +90° = straight up, -90° = straight down.");

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
            buttons.Controls.Add(btnCancel, 1, 0);
            buttons.Controls.Add(btnOK, 2, 0);

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
