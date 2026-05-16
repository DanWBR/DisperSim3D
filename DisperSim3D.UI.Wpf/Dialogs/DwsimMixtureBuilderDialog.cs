using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Builds a <see cref="GasMixture"/> by pulling compound data from DWSIM's
    /// database via the FluentAPI (reflection-loaded) and running a property-package
    /// flash (PR78 by default) to compute aggregate mixture properties (M, ρ, μ, Cp, γ)
    /// at a chosen T/P. The resulting mixture is wrapped in a <see cref="GasLibraryItem"/>.
    /// Layout uses TableLayoutPanel exclusively — no SplitContainer (project convention).
    /// </summary>
    public class DwsimMixtureBuilderDialog : Form
    {
        private TextBox _txtSearch;
        private ListBox _lstAvailable;
        private ListView _lvSelected;
        private NumericUpDown _nudFraction;
        private NumericUpDown _nudT;
        private NumericUpDown _nudP;
        private Label _lblStatus;
        private Label _lblResults;
        private Button _btnAdd, _btnRemove, _btnCompute;

        public GasLibraryItem Result { get; private set; }

        public DwsimMixtureBuilderDialog()
        {
            Text = "Gas Mixture Builder (DWSIM)";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(900, 560);
            Size = new Size(960, 620);
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
            TryAutoInit();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;

            // Outer layout: 2 rows — main grid (expands), bottom buttons.
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding((int)(8 * dpi)),
                ColumnCount = 1,
                RowCount = 2
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Main 2-column layout — left: available list, right: selected + thermo.
            // Column widths split 40 / 60 so the right side has room for the
            // composition table + results.
            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            main.Controls.Add(BuildLeftPanel(dpi), 0, 0);
            main.Controls.Add(BuildRightPanel(dpi), 1, 0);

            outer.Controls.Add(main, 0, 0);
            outer.Controls.Add(BuildBottomPanel(dpi), 0, 1);
            Controls.Add(outer);
        }

        // Left: search + compound list + Add row.
        private TableLayoutPanel BuildLeftPanel(float dpi)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0, 0, (int)(4 * dpi), 0)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtSearch = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Search compounds..." };
            _txtSearch.TextChanged += (s, e) => FilterCompoundList();

            _lstAvailable = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };

            // Add row: mole-fraction NUD + Add button.
            var addRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0, (int)(4 * dpi), 0, 0)
            };
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addRow.Controls.Add(new Label
            {
                Text = "Mole fraction:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, (int)(6 * dpi), (int)(4 * dpi), 0)
            }, 0, 0);
            _nudFraction = new NumericUpDown
            {
                Minimum = 0.0001m,
                Maximum = 1m,
                Value = 1.0m,
                DecimalPlaces = 4,
                Increment = 0.01m,
                Width = (int)(90 * dpi)
            };
            addRow.Controls.Add(_nudFraction, 1, 0);
            _btnAdd = new Button { Text = "Add →", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Anchor = AnchorStyles.Right };
            _btnAdd.Click += (s, e) => AddSelected();
            addRow.Controls.Add(_btnAdd, 2, 0);

            t.Controls.Add(_txtSearch, 0, 0);
            t.Controls.Add(_lstAvailable, 0, 1);
            t.Controls.Add(addRow, 0, 2);
            return t;
        }

        // Right: selected composition list + Remove + T/P + Run flash + results.
        private TableLayoutPanel BuildRightPanel(float dpi)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding((int)(4 * dpi), 0, 0, 0)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            t.Controls.Add(new Label
            {
                Text = "Mixture composition (mole fractions):",
                AutoSize = true
            }, 0, 0);

            _lvSelected = new ListView
            {
                Dock = DockStyle.Fill,
                View = System.Windows.Forms.View.Details,
                FullRowSelect = true,
                GridLines = true,
                LabelEdit = true
            };
            _lvSelected.Columns.Add("Compound", (int)(220 * dpi));
            _lvSelected.Columns.Add("Mole Fraction", (int)(120 * dpi));
            _lvSelected.AfterLabelEdit += (s, e) =>
            {
                if (e.Label == null) return;
                if (!double.TryParse(e.Label, out _)) e.CancelEdit = true;
            };
            t.Controls.Add(_lvSelected, 0, 1);

            // Remove button row (left-aligned).
            var removeRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(0, (int)(4 * dpi), 0, 0)
            };
            removeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            removeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _btnRemove = new Button { Text = "Remove", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
            _btnRemove.Click += (s, e) => RemoveSelected();
            removeRow.Controls.Add(_btnRemove, 0, 0);
            removeRow.Controls.Add(new Label(), 1, 0);
            t.Controls.Add(removeRow, 0, 2);

            // Thermo inputs row: T, P, Run flash button.
            var thermoRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 6,
                Padding = new Padding(0, (int)(6 * dpi), 0, 0)
            };
            thermoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            thermoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            thermoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            thermoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            thermoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            thermoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            thermoRow.Controls.Add(new Label
            {
                Text = "T (K):",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, (int)(6 * dpi), (int)(4 * dpi), 0)
            }, 0, 0);
            _nudT = new NumericUpDown
            {
                Minimum = 100,
                Maximum = 2000,
                Value = 293,
                DecimalPlaces = 2,
                Increment = 1,
                Width = (int)(90 * dpi)
            };
            thermoRow.Controls.Add(_nudT, 1, 0);
            thermoRow.Controls.Add(new Label
            {
                Text = "  P (Pa):",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, (int)(6 * dpi), (int)(4 * dpi), 0)
            }, 2, 0);
            _nudP = new NumericUpDown
            {
                Minimum = 100,
                Maximum = 50_000_000,
                Value = 101325,
                DecimalPlaces = 0,
                Increment = 1000,
                Width = (int)(110 * dpi)
            };
            thermoRow.Controls.Add(_nudP, 3, 0);
            thermoRow.Controls.Add(new Label(), 4, 0);
            _btnCompute = new Button { Text = "Run flash", AutoSize = true, Padding = new Padding(10, 2, 10, 2), Anchor = AnchorStyles.Right };
            _btnCompute.Click += (s, e) => ComputeProperties();
            thermoRow.Controls.Add(_btnCompute, 5, 0);
            t.Controls.Add(thermoRow, 0, 3);

            _lblResults = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ForeColor = Color.DarkSlateGray,
                Text = "(No properties computed yet)",
                Padding = new Padding(0, (int)(4 * dpi), 0, 0)
            };
            t.Controls.Add(_lblResults, 0, 4);
            return t;
        }

        // Bottom: status label + Cancel + OK.
        private TableLayoutPanel BuildBottomPanel(float dpi)
        {
            // Project convention (memory: feedback_winforms_button_order): Cancel
            // BEFORE OK in column order so Cancel sits to the LEFT of OK.
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(0, (int)(8 * dpi), 0, 0)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _lblStatus = new Label
            {
                AutoSize = true,
                ForeColor = Color.DarkSlateGray,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, (int)(6 * dpi), 0, 0)
            };
            var btnCancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(12, 2, 12, 2), DialogResult = DialogResult.Cancel };
            var btnOK = new Button { Text = "OK", AutoSize = true, Padding = new Padding(16, 2, 16, 2) };
            btnOK.Click += (s, e) => CommitResult();
            t.Controls.Add(_lblStatus, 0, 0);
            t.Controls.Add(btnCancel, 1, 0);
            t.Controls.Add(btnOK, 2, 0);
            AcceptButton = btnOK;
            CancelButton = btnCancel;
            return t;
        }

        private void TryAutoInit()
        {
            // DWSIMCore is now bundled directly into the engine (lib/DWSIMCore/),
            // so initialisation no longer requires an external install path. Just
            // load the calculator and populate the compound list.
            DwsimThermo.SetPropertyPackage(AppSettings.Instance.DwsimPropertyPackage);
            if (DwsimThermo.Initialize())
            {
                LoadCompoundList();
            }
            else
            {
                _lblStatus.Text = "DWSIMCore init failed: " + DwsimThermo.LastError;
            }
        }

        private List<string> _allCompounds = new List<string>();

        private void LoadCompoundList()
        {
            Cursor = Cursors.WaitCursor;
            _lblStatus.Text = "Loading compound database...";
            Application.DoEvents();
            try
            {
                _allCompounds = DwsimThermo.AvailableCompounds().ToList();
                FilterCompoundList();
                _lblStatus.Text = string.Format("{0} compounds loaded.", _allCompounds.Count);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void FilterCompoundList()
        {
            string q = _txtSearch.Text?.Trim() ?? "";
            _lstAvailable.BeginUpdate();
            _lstAvailable.Items.Clear();
            foreach (var c in _allCompounds)
                if (q.Length == 0 || c.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    _lstAvailable.Items.Add(c);
            _lstAvailable.EndUpdate();
        }

        private void AddSelected()
        {
            if (_lstAvailable.SelectedItem == null) return;
            string name = _lstAvailable.SelectedItem.ToString();
            // Skip duplicates.
            foreach (ListViewItem it in _lvSelected.Items)
                if (string.Equals(it.Text, name, StringComparison.OrdinalIgnoreCase)) return;
            var lvi = new ListViewItem(name);
            lvi.SubItems.Add(((double)_nudFraction.Value).ToString("G4"));
            _lvSelected.Items.Add(lvi);
        }

        private void RemoveSelected()
        {
            foreach (ListViewItem it in _lvSelected.SelectedItems) _lvSelected.Items.Remove(it);
        }

        private IDictionary<string, double> ReadComposition()
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem it in _lvSelected.Items)
            {
                double f = 0;
                double.TryParse(it.SubItems[1].Text, out f);
                if (f > 0) dict[it.Text] = f;
            }
            return dict;
        }

        private DwsimThermo.MixtureProperties _lastProps;

        private void ComputeProperties()
        {
            var comp = ReadComposition();
            if (comp.Count == 0) { _lblStatus.Text = "Add at least one compound."; return; }
            Cursor = Cursors.WaitCursor;
            _lblStatus.Text = "Running flash (" + AppSettings.Instance.DwsimPropertyPackage + ")...";
            Application.DoEvents();
            try
            {
                _lastProps = DwsimThermo.ComputeMixtureProperties(
                    comp, (double)_nudT.Value, (double)_nudP.Value);
                if (!string.IsNullOrEmpty(_lastProps.Error))
                {
                    _lblResults.Text = "Flash failed: " + _lastProps.Error;
                    _lblStatus.Text = "Computation failed.";
                    return;
                }
                _lblResults.Text = string.Format(
                    "M = {0:F4} kg/mol    ρ = {1:F3} kg/m³    μ = {2:E3} Pa·s    Cp = {3:F1} J/kg/K    γ = {4:F3}",
                    _lastProps.MolarMassKgMol, _lastProps.DensityKgM3, _lastProps.ViscosityPaS,
                    _lastProps.CpJPerKgK, _lastProps.GammaCpCv);
                _lblStatus.Text = "Properties ready.";
            }
            finally { Cursor = Cursors.Default; }
        }

        private void CommitResult()
        {
            var comp = ReadComposition();
            if (comp.Count == 0) { _lblStatus.Text = "Add at least one compound."; DialogResult = DialogResult.None; return; }
            // Normalise.
            double sum = comp.Values.Sum();
            if (sum <= 0) { _lblStatus.Text = "Composition sums to zero."; DialogResult = DialogResult.None; return; }
            var mix = new GasMixture();
            foreach (var kv in comp)
            {
                // Pull the real per-component constant properties (M, LFL) from
                // DWSIM's database via the wrapper. Falls back to 0.029 kg/mol
                // (≈ air) only if the compound isn't in DWSIM's catalog.
                var info = DwsimThermo.GetCompoundInfo(kv.Key);
                double mw = (info != null && info.MolarMassKgMol > 0) ? info.MolarMassKgMol : 0.029;
                // Pull LFL/IDLH from DisperSim3D's local hazard table — DWSIM stores
                // only thermodynamic constants.
                var haz = HazardDatabase.Lookup(kv.Key);
                mix.Components.Add(new GasComponent
                {
                    Name = kv.Key,
                    MoleFraction = kv.Value / sum,
                    MolarMass = mw,
                    LFL = haz?.LflKgM3 ?? 0,
                    UFL = haz?.UflKgM3 ?? 0,
                    IDLH = haz?.IdlhKgM3 ?? 0
                });
            }
            // Wrap with a sensible mixture name (top 3 components).
            string mixName = string.Join("+", comp.Keys.Take(3));
            if (comp.Count > 3) mixName += " (+" + (comp.Count - 3) + ")";
            Result = GasLibraryItem.FromMixture(mixName, mix);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
