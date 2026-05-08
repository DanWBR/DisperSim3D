using System;
using System.Windows.Forms;
using DisperSim3D.Core;

namespace DisperSim3D.Dialogs
{
    public class HighPressureSourceDialog : Form
    {
        private NumericUpDown nudPressure;
        private NumericUpDown nudTemperature;
        private NumericUpDown nudOrifice;
        private NumericUpDown nudVolume;
        private NumericUpDown nudGamma;
        private NumericUpDown nudMolarMass;
        private Label lblFlowRate;
        private Label lblChoked;

        public HighPressureLeakParams Result { get; private set; }

        public HighPressureSourceDialog()
        {
            Result = new HighPressureLeakParams();
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "High Pressure Leak Source";
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
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 3
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var table = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 8, Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(200 * dpi)));

            int row = 0;
            nudPressure = MakeNud(100000m, 100000000m, 1000000m, 0);
            nudPressure.ValueChanged += (s, e) => UpdateCalc();
            AddRow(table, row++, "Vessel Pressure (Pa):", nudPressure);

            nudTemperature = MakeNud(100m, 1000m, 293.15m, 2);
            nudTemperature.ValueChanged += (s, e) => UpdateCalc();
            AddRow(table, row++, "Vessel Temperature (K):", nudTemperature);

            nudOrifice = MakeNud(0.001m, 0.5m, 0.025m, 3);
            nudOrifice.ValueChanged += (s, e) => UpdateCalc();
            AddRow(table, row++, "Orifice Diameter (m):", nudOrifice);

            nudVolume = MakeNud(0.01m, 10000m, 10m, 2);
            AddRow(table, row++, "Vessel Volume (m³):", nudVolume);

            nudGamma = MakeNud(1.0m, 1.7m, 1.4m, 2);
            nudGamma.ValueChanged += (s, e) => UpdateCalc();
            AddRow(table, row++, "Gamma (Cp/Cv):", nudGamma);

            nudMolarMass = MakeNud(0.002m, 0.2m, 0.016m, 3);
            nudMolarMass.ValueChanged += (s, e) => UpdateCalc();
            AddRow(table, row++, "Molar Mass (kg/mol):", nudMolarMass);

            lblChoked = new Label { AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold) };
            AddRow(table, row++, "Flow regime:", lblChoked);

            lblFlowRate = new Label { AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold) };
            AddRow(table, row++, "Initial mass flow:", lblFlowRate);

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
                Result = new HighPressureLeakParams
                {
                    VesselPressurePa = (double)nudPressure.Value,
                    VesselTemperatureK = (double)nudTemperature.Value,
                    OrificeDiameterM = (double)nudOrifice.Value,
                    VesselVolumeM3 = (double)nudVolume.Value,
                    GasGamma = (double)nudGamma.Value,
                    GasMolarMassKgMol = (double)nudMolarMass.Value
                };
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

            UpdateCalc();
        }

        private void UpdateCalc()
        {
            try
            {
                var p = new HighPressureLeakParams
                {
                    VesselPressurePa = (double)nudPressure.Value,
                    VesselTemperatureK = (double)nudTemperature.Value,
                    OrificeDiameterM = (double)nudOrifice.Value,
                    GasGamma = (double)nudGamma.Value,
                    GasMolarMassKgMol = (double)nudMolarMass.Value
                };
                bool choked = HighPressureLeakModel.IsChoked(p);
                double mdot = HighPressureLeakModel.MassFlowRate(p);
                lblChoked.Text = choked ? "CHOKED" : "Unchoked";
                lblFlowRate.Text = mdot.ToString("F4") + " kg/s";
            }
            catch { }
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
