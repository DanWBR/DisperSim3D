using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class FireSourceDialog : Form
    {
        private TextBox txtName;
        private NumericUpDown nudMassFlow;
        private NumericUpDown nudOrifice;
        private NumericUpDown nudHeatCombustion;
        private NumericUpDown nudRadFraction;
        private CheckBox chkPoolFire;
        private NumericUpDown nudPoolDiameter;
        private NumericUpDown nudBurnRate;

        public FireSource Result { get; private set; }

        public FireSourceDialog()
        {
            Result = new FireSource();
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "Add Fire Source";
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
                ColumnCount = 1, RowCount = 2
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var table = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 9, Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(200 * dpi)));

            int row = 0;
            txtName = new TextBox { Text = "JetFire1", Dock = DockStyle.Fill };
            AddRow(table, row++, "Name:", txtName);

            nudMassFlow = MakeNud(0.001m, 1000m, 1.0m, 3);
            AddRow(table, row++, "Mass Flow (kg/s):", nudMassFlow);

            nudOrifice = MakeNud(0.001m, 1.0m, 0.02m, 3);
            AddRow(table, row++, "Orifice Dia (m):", nudOrifice);

            nudHeatCombustion = MakeNud(1e6m, 100e6m, 50e6m, 0);
            AddRow(table, row++, "Heat Combustion (J/kg):", nudHeatCombustion);

            nudRadFraction = MakeNud(0.05m, 0.5m, 0.2m, 2);
            AddRow(table, row++, "Radiative Fraction:", nudRadFraction);

            chkPoolFire = new CheckBox { Text = "Pool Fire", AutoSize = true };
            chkPoolFire.CheckedChanged += (s, e) =>
            {
                nudPoolDiameter.Enabled = chkPoolFire.Checked;
                nudBurnRate.Enabled = chkPoolFire.Checked;
            };
            AddRow(table, row++, "", chkPoolFire);

            nudPoolDiameter = MakeNud(0.5m, 100m, 5.0m, 1);
            nudPoolDiameter.Enabled = false;
            AddRow(table, row++, "Pool Diameter (m):", nudPoolDiameter);

            nudBurnRate = MakeNud(0.001m, 1.0m, 0.05m, 3);
            nudBurnRate.Enabled = false;
            AddRow(table, row++, "Burn Rate (kg/m²/s):", nudBurnRate);

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
                Result = new FireSource
                {
                    Name = txtName.Text,
                    MassFlowRateKgS = (double)nudMassFlow.Value,
                    OrificeDiameterM = (double)nudOrifice.Value,
                    HeatOfCombustionJKg = (double)nudHeatCombustion.Value,
                    RadiativeFraction = (double)nudRadFraction.Value,
                    IsPoolFire = chkPoolFire.Checked,
                    PoolDiameterM = (double)nudPoolDiameter.Value,
                    PoolBurnRateKgM2S = (double)nudBurnRate.Value,
                    Direction = new System.Windows.Media.Media3D.Vector3D(0, 0, 1)
                };
            };
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
