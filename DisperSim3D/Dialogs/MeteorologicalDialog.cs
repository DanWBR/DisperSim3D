using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class MeteorologicalDialog : Form
    {
        private NumericUpDown nudWindSpeed;
        private NumericUpDown nudWindDirection;
        private ComboBox cmbStability;
        private NumericUpDown nudTemperature;
        private NumericUpDown nudPressure;

        public MeteorologicalConditions Result { get; private set; }

        public MeteorologicalDialog(MeteorologicalConditions existing = null)
        {
            BuildUI();
            if (existing != null)
            {
                nudWindSpeed.Value = (decimal)existing.WindSpeed;
                nudWindDirection.Value = (decimal)existing.WindDirectionDeg;
                cmbStability.SelectedIndex = (int)existing.StabilityClass;
                nudTemperature.Value = (decimal)existing.AmbientTemperature;
                nudPressure.Value = (decimal)existing.AmbientPressure;
            }
        }

        private void BuildUI()
        {
            this.Text = "Meteorological Conditions";
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
                RowCount = 5,
                Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(180 * dpi)));

            int row = 0;
            nudWindSpeed = MakeNud(0.1m, 50m, 5m, 1);
            DialogHelpers.AddRowWithHelp(table, ref row, "Wind Speed (m/s):", nudWindSpeed,
                "Mean wind magnitude at the 10 m reference height.");

            nudWindDirection = MakeNud(0m, 360m, 270m, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Wind Direction (deg, 0=N):", nudWindDirection,
                "Meteorological convention: direction the wind blows FROM (0°=N, 90°=E, 180°=S, 270°=W).");

            cmbStability = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbStability.Items.AddRange(new object[] {
                "A - Very Unstable", "B - Unstable", "C - Slightly Unstable",
                "D - Neutral", "E - Slightly Stable", "F - Stable"
            });
            cmbStability.SelectedIndex = 3;
            DialogHelpers.AddRowWithHelp(table, ref row, "Stability Class:", cmbStability,
                "Pasquill-Gifford atmospheric stability. Controls turbulent dispersion: A spreads fastest, F traps the plume.");

            nudTemperature = MakeNud(200m, 350m, 293.15m, 2);
            DialogHelpers.AddRowWithHelp(table, ref row, "Temperature (K):", nudTemperature,
                "Ambient air temperature in Kelvin (293.15 K = 20 °C).");

            nudPressure = MakeNud(80000m, 120000m, 101325m, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Pressure (Pa):", nudPressure,
                "Ambient atmospheric pressure (101325 Pa = sea level standard).");

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

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Result = new MeteorologicalConditions
            {
                WindSpeed = (double)nudWindSpeed.Value,
                WindDirectionDeg = (double)nudWindDirection.Value,
                StabilityClass = (PasquillStabilityClass)cmbStability.SelectedIndex,
                AmbientTemperature = (double)nudTemperature.Value,
                AmbientPressure = (double)nudPressure.Value
            };
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
