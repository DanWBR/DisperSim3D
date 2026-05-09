using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class GasMixtureDialog : Form
    {
        private DataGridView _grid;

        public GasMixture Result { get; private set; }

        public GasMixtureDialog(GasMixture existing)
        {
            Result = existing ?? new GasMixture();
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "Gas Mixture Components";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            var dpi = DeviceDpi / 96f;
            this.Size = new System.Drawing.Size((int)(500 * dpi), (int)(350 * dpi));
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = System.Drawing.SystemColors.Window
            };
            _grid.Columns.Add("Name", "Component");
            _grid.Columns.Add("MolarMass", "Molar Mass (kg/mol)");
            _grid.Columns.Add("MoleFrac", "Mole Fraction");
            _grid.Columns.Add("LFL", "LFL (kg/m³)");
            _grid.Columns.Add("IDLH", "IDLH (kg/m³)");

            foreach (var c in Result.Components)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                _grid.Rows.Add(c.Name, c.MolarMass.ToString("F4", inv),
                    c.MoleFraction.ToString("F4", inv),
                    c.LFL.ToString("E2", inv), c.IDLH.ToString("E2", inv));
            }

            var btnPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += (s, e) => BuildResult();
            btnPanel.Controls.Add(new Label(), 0, 0);
            btnPanel.Controls.Add(btnCancel, 1, 0);
            btnPanel.Controls.Add(btnOK, 2, 0);
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            var summary = new Label
            {
                Dock = DockStyle.Top, AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 9f),
                Padding = new Padding(8),
                Text = "Define gas mixture components. Mole fractions should sum to 1.0.\n" +
                       "Individual component concentrations = total concentration × mole fraction.\n\n" +
                       "Component:  display name (e.g. Methane, H2S).\n" +
                       "Molar Mass: kg per mole (CH₄ = 0.016, H₂S = 0.034).\n" +
                       "Mole Fraction: 0–1, fraction of moles in the mixture.\n" +
                       "LFL: lower flammability limit in kg/m³ (combustion threshold).\n" +
                       "IDLH: immediately dangerous to life and health threshold (toxic exposure)."
            };

            this.Controls.Add(_grid);
            this.Controls.Add(summary);
            this.Controls.Add(btnPanel);
            this.ApplyDpiScaling();
        }

        private void BuildResult()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Result = new GasMixture();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                try
                {
                    var comp = new GasComponent
                    {
                        Name = row.Cells["Name"].Value?.ToString() ?? "Component",
                        MolarMass = double.Parse(row.Cells["MolarMass"].Value?.ToString() ?? "0.016", inv),
                        MoleFraction = double.Parse(row.Cells["MoleFrac"].Value?.ToString() ?? "1", inv),
                        LFL = double.Parse(row.Cells["LFL"].Value?.ToString() ?? "0", inv),
                        IDLH = double.Parse(row.Cells["IDLH"].Value?.ToString() ?? "0", inv)
                    };
                    Result.Components.Add(comp);
                }
                catch { }
            }
        }
    }
}
