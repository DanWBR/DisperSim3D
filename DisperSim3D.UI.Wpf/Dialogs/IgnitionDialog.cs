using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Edits an <see cref="IgnitionEvent"/>: which dispersion run is ignited, where,
    /// when, and the two burn parameters — the envelope fraction of the LFL and the
    /// flame speed. Only completed simulations are offered, since an ignition needs a
    /// concentration field to burn.
    /// </summary>
    public class IgnitionDialog : Form
    {
        private TextBox txtName;
        private ComboBox cmbSimulation;
        private NumericUpDown nudX, nudY, nudZ;
        private NumericUpDown nudTime;
        private NumericUpDown nudEnvelope;
        private NumericUpDown nudFlameSpeed;

        private readonly List<Simulation> _simulations = new List<Simulation>();

        public IgnitionEvent Result { get; private set; }

        public IgnitionDialog(Scene3D scene, IgnitionEvent existing = null)
        {
            Result = existing ?? new IgnitionEvent();
            BuildUI(scene);

            if (existing != null)
            {
                txtName.Text = existing.Name;
                nudX.Value = Clamp(nudX, (decimal)existing.Position.X);
                nudY.Value = Clamp(nudY, (decimal)existing.Position.Y);
                nudZ.Value = Clamp(nudZ, (decimal)existing.Position.Z);
                nudTime.Value = Clamp(nudTime, (decimal)existing.TimeS);
                nudEnvelope.Value = Clamp(nudEnvelope, (decimal)existing.EnvelopeFraction);
                nudFlameSpeed.Value = Clamp(nudFlameSpeed, (decimal)existing.FlameSpeedMS);

                int index = _simulations.FindIndex(s => s.Id == existing.SimulationId);
                if (index >= 0) cmbSimulation.SelectedIndex = index;
            }
        }

        private void BuildUI(Scene3D scene)
        {
            this.Text = "Ignite Cloud";
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
                ColumnCount = 2, RowCount = 8, Margin = new Padding(0, 0, 0, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(240 * dpi)));

            int row = 0;

            txtName = new TextBox { Text = "Ignition", Dock = DockStyle.Fill };
            DialogHelpers.AddRowWithHelp(table, ref row, "Name:", txtName,
                "Identifier shown in the scene tree.");

            cmbSimulation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            if (scene?.Simulations != null)
            {
                foreach (var sim in scene.Simulations)
                {
                    if (sim == null || sim.Status != SimulationStatus.Completed) continue;
                    _simulations.Add(sim);
                    cmbSimulation.Items.Add(sim.Name);
                }
            }
            if (_simulations.Count == 0) cmbSimulation.Items.Add("(no completed simulations)");
            cmbSimulation.SelectedIndex = 0;
            DialogHelpers.AddRowWithHelp(table, ref row, "Simulation:", cmbSimulation,
                "The dispersion run whose cloud is ignited. Only the gas connected to the "
                + "ignition point burns.");

            var positionPanel = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3, RowCount = 1, Margin = new Padding(0)
            };
            nudX = MakeNud(-100000m, 100000m, 0m, 1);
            nudY = MakeNud(-100000m, 100000m, 0m, 1);
            nudZ = MakeNud(-100000m, 100000m, 0m, 1);
            positionPanel.Controls.Add(nudX, 0, 0);
            positionPanel.Controls.Add(nudY, 1, 0);
            positionPanel.Controls.Add(nudZ, 2, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Position X, Y, Z (m):", positionPanel,
                "Where the cloud is lit. Must be inside gas at or above the LFL.");

            nudTime = MakeNud(0m, 100000m, 0m, 1);
            DialogHelpers.AddRowWithHelp(table, ref row, "Ignition Time (s):", nudTime,
                "The written snapshot closest to this time is the one that burns. "
                + "A cloud ignited at 30 s is not the cloud at 300 s.");

            nudEnvelope = MakeNud(0.1m, 1.0m, 0.5m, 2);
            DialogHelpers.AddRowWithHelp(table, ref row, "Envelope (x LFL):", nudEnvelope,
                "Dispersion gives time-averaged concentrations, and turbulent fluctuation puts "
                + "the momentary flammable cloud outside the averaged LFL contour. Consequence "
                + "practice draws the flash-fire envelope at half the LFL.");

            nudFlameSpeed = MakeNud(0.5m, 100m, 10m, 1);
            DialogHelpers.AddRowWithHelp(table, ref row, "Flame Speed (m/s):", nudFlameSpeed,
                "Burn-back speed through the cloud. Turns distance into arrival time, and the "
                + "longest arrival is the exposure duration. 5-15 m/s for an unconfined methane "
                + "deflagration.");

            var hint = new Label
            {
                Text = "To see the result, add a View on the same simulation and set its Field\r\n"
                     + "to FlashFireEnvelope (isosurface at 0.5) or FlashFireArrivalS.",
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0)
            };
            table.Controls.Add(hint, 0, row);
            table.SetColumnSpan(hint, 2);
            row++;

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
                int index = cmbSimulation.SelectedIndex;
                Result.Name = string.IsNullOrWhiteSpace(txtName.Text) ? "Ignition" : txtName.Text.Trim();
                Result.SimulationId = index >= 0 && index < _simulations.Count
                    ? _simulations[index].Id
                    : "";
                Result.Position = new DisperSim3D.Geometry.Point3D(
                    (double)nudX.Value, (double)nudY.Value, (double)nudZ.Value);
                Result.TimeS = (double)nudTime.Value;
                Result.EnvelopeFraction = (double)nudEnvelope.Value;
                Result.FlameSpeedMS = (double)nudFlameSpeed.Value;
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

        private static decimal Clamp(NumericUpDown nud, decimal value)
            => value < nud.Minimum ? nud.Minimum : (value > nud.Maximum ? nud.Maximum : value);

        private static NumericUpDown MakeNud(decimal min, decimal max, decimal value, int decimals)
        {
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals,
                Dock = DockStyle.Fill, Width = 75
            };
            nud.Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1;
            return nud;
        }
    }
}
