using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Edits a single <see cref="GasLibraryItem"/> — pure substance properties or mixture components.
    /// </summary>
    public class GasLibraryItemDialog : Form
    {
        private TextBox txtName;
        private RadioButton rbPure;
        private RadioButton rbMixture;
        private Panel pnlPure;
        private Panel pnlMixture;
        private NumericUpDown nudMolarMass;
        private NumericUpDown nudLFL;
        private NumericUpDown nudIDLH;
        private NumericUpDown nudERPG1, nudERPG2, nudERPG3;
        private DataGridView gridMix;

        public GasLibraryItem Result { get; private set; }

        public GasLibraryItemDialog(GasLibraryItem existing)
        {
            Result = existing ?? new GasLibraryItem();
            BuildUI();
            LoadFromItem(Result);
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "Edit Gas";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new System.Drawing.Size((int)(560 * dpi), (int)(480 * dpi));
            this.Padding = new Padding((int)(10 * dpi));

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
            txtName = new TextBox { Dock = DockStyle.Fill };
            header.Controls.Add(txtName);
            rbPure = new RadioButton { Text = "Pure", AutoSize = true, Checked = true };
            rbMixture = new RadioButton { Text = "Mixture", AutoSize = true };
            rbPure.CheckedChanged += (s, e) => UpdateModeVisibility();
            rbMixture.CheckedChanged += (s, e) => UpdateModeVisibility();
            header.Controls.Add(rbPure);
            header.Controls.Add(rbMixture);
            outer.Controls.Add(header, 0, 0);

            // Pure panel
            pnlPure = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            var pureTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, Padding = new Padding(4)
            };
            pureTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pureTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;
            nudMolarMass = MakeNud(0.001m, 1.0m, 0.016m, 4);
            DialogHelpers.AddRowWithHelp(pureTable, ref row, "Molar Mass (kg/mol):", nudMolarMass,
                "Molecular weight of the substance.");
            nudLFL = MakeNud(0m, 10m, 0.033m, 4);
            DialogHelpers.AddRowWithHelp(pureTable, ref row, "LFL (kg/m³):", nudLFL,
                "Lower Flammability Limit.");
            nudIDLH = MakeNud(0m, 10m, 0m, 4);
            DialogHelpers.AddRowWithHelp(pureTable, ref row, "IDLH (kg/m³):", nudIDLH,
                "Immediately Dangerous to Life and Health.");
            nudERPG1 = MakeNud(0m, 10m, 0m, 4);
            DialogHelpers.AddRowWithHelp(pureTable, ref row, "ERPG-1 (kg/m³):", nudERPG1,
                "Threshold for transient mild effects.");
            nudERPG2 = MakeNud(0m, 10m, 0m, 4);
            DialogHelpers.AddRowWithHelp(pureTable, ref row, "ERPG-2 (kg/m³):", nudERPG2,
                "Threshold for irreversible effects.");
            nudERPG3 = MakeNud(0m, 10m, 0m, 4);
            DialogHelpers.AddRowWithHelp(pureTable, ref row, "ERPG-3 (kg/m³):", nudERPG3,
                "Life-threatening effect threshold.");
            pnlPure.Controls.Add(pureTable);

            // Mixture panel
            pnlMixture = new Panel { Dock = DockStyle.Fill };
            gridMix = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = System.Drawing.SystemColors.Window
            };
            gridMix.Columns.Add("Name", "Component");
            gridMix.Columns.Add("MolarMass", "Molar Mass (kg/mol)");
            gridMix.Columns.Add("MoleFrac", "Mole Fraction");
            gridMix.Columns.Add("LFL", "LFL (kg/m³)");
            gridMix.Columns.Add("IDLH", "IDLH (kg/m³)");
            pnlMixture.Controls.Add(gridMix);

            outer.Controls.Add(pnlPure, 0, 1);
            outer.Controls.Add(pnlMixture, 0, 2);

            var btnPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += BtnOK_Click;
            btnPanel.Controls.Add(new Label(), 0, 0);
            btnPanel.Controls.Add(btnCancel, 1, 0);
            btnPanel.Controls.Add(btnOK, 2, 0);
            outer.Controls.Add(btnPanel, 0, 3);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
            this.Controls.Add(outer);
            this.ApplyDpiScaling();
        }

        private void UpdateModeVisibility()
        {
            pnlPure.Visible = rbPure.Checked;
            pnlMixture.Visible = rbMixture.Checked;
        }

        private void LoadFromItem(GasLibraryItem item)
        {
            txtName.Text = item.Name ?? "";
            if (item.Kind == GasLibraryItemKind.Mixture)
            {
                rbMixture.Checked = true;
                gridMix.Rows.Clear();
                if (item.Mixture != null)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    foreach (var c in item.Mixture.Components)
                        gridMix.Rows.Add(c.Name, c.MolarMass.ToString("F4", inv),
                            c.MoleFraction.ToString("F4", inv),
                            c.LFL.ToString("E2", inv), c.IDLH.ToString("E2", inv));
                }
            }
            else
            {
                rbPure.Checked = true;
                var g = item.PureGas ?? new GasProperties();
                nudMolarMass.Value = (decimal)Math.Max((double)nudMolarMass.Minimum, Math.Min((double)nudMolarMass.Maximum, g.MolarMass));
                nudLFL.Value = (decimal)Math.Max((double)nudLFL.Minimum, Math.Min((double)nudLFL.Maximum, g.LFL));
                nudIDLH.Value = (decimal)Math.Max((double)nudIDLH.Minimum, Math.Min((double)nudIDLH.Maximum, g.IDLH));
                nudERPG1.Value = (decimal)Math.Max((double)nudERPG1.Minimum, Math.Min((double)nudERPG1.Maximum, g.ERPG1));
                nudERPG2.Value = (decimal)Math.Max((double)nudERPG2.Minimum, Math.Min((double)nudERPG2.Maximum, g.ERPG2));
                nudERPG3.Value = (decimal)Math.Max((double)nudERPG3.Minimum, Math.Min((double)nudERPG3.Maximum, g.ERPG3));
            }
            UpdateModeVisibility();
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Result.Name = string.IsNullOrEmpty(txtName.Text) ? "Unnamed" : txtName.Text;
            if (rbMixture.Checked)
            {
                Result.Kind = GasLibraryItemKind.Mixture;
                Result.Mixture = new GasMixture();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                foreach (DataGridViewRow row in gridMix.Rows)
                {
                    if (row.IsNewRow) continue;
                    try
                    {
                        Result.Mixture.Components.Add(new GasComponent
                        {
                            Name = row.Cells["Name"].Value?.ToString() ?? "Component",
                            MolarMass = double.Parse(row.Cells["MolarMass"].Value?.ToString() ?? "0.016", inv),
                            MoleFraction = double.Parse(row.Cells["MoleFrac"].Value?.ToString() ?? "1", inv),
                            LFL = double.Parse(row.Cells["LFL"].Value?.ToString() ?? "0", inv),
                            IDLH = double.Parse(row.Cells["IDLH"].Value?.ToString() ?? "0", inv)
                        });
                    }
                    catch { }
                }
            }
            else
            {
                Result.Kind = GasLibraryItemKind.Pure;
                Result.PureGas = new GasProperties
                {
                    Name = Result.Name,
                    MolarMass = (double)nudMolarMass.Value,
                    LFL = (double)nudLFL.Value,
                    IDLH = (double)nudIDLH.Value,
                    ERPG1 = (double)nudERPG1.Value,
                    ERPG2 = (double)nudERPG2.Value,
                    ERPG3 = (double)nudERPG3.Value
                };
            }
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
