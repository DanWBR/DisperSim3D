using System;
using System.Windows.Forms;
using DisperSim3D.Models;

namespace TestApp.Dialogs
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

        public string SourceName { get; private set; } = "Source1";
        public GasProperties Gas { get; private set; }
        public double ReleaseRateKgPerS { get; private set; } = 0.5;
        public double PuffIntervalS { get; private set; } = 1.0;
        public double HeightOffset { get; private set; } = 2.0;

        public DispersionSourceDialog()
        {
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
            this.Padding = new Padding(10);

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
                RowCount = 8,
                Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));

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

            nudPuffInterval = MakeNud(0.1m, 60m, 1.0m, 1);
            AddRow(table, row++, "Puff Interval (s):", nudPuffInterval);

            nudHeightOffset = MakeNud(0m, 100m, 2.0m, 1);
            AddRow(table, row++, "Height Offset (m):", nudHeightOffset);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill
            };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += BtnOK_Click;
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOK);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            outerLayout.Controls.Add(table, 0, 0);
            outerLayout.Controls.Add(buttons, 0, 1);
            this.Controls.Add(outerLayout);
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
