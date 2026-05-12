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
    /// Create / edit a <see cref="DetectorAllocation"/>: pick the target
    /// <see cref="DispersionStudy"/>, set objective / radius / Z-range /
    /// candidate-grid resolution, optionally fold existing detectors into
    /// the coverage seed, run the greedy allocator, and inspect the result.
    /// "Apply" materialises the allocated positions as <see cref="GasDetector3D"/>
    /// entries in the scene (caller handles).
    /// </summary>
    public class DetectorAllocationDialog : Form
    {
        private readonly Scene3D _scene;
        private readonly DetectorAllocation _editing;

        private TextBox _txtName;
        private ComboBox _cmbStudy;
        private RadioButton _radioStrategyCov, _radioStrategyRisk;
        private RadioButton _radioAll, _radioPercent;
        private Label _lblTargetPct;
        private NumericUpDown _nudTargetPct, _nudMaxDet, _nudRadius, _nudMinZ, _nudMaxZ;
        private NumericUpDown _nudNx, _nudNy, _nudNz;
        private CheckBox _chkUseExisting;
        private GroupBox _grpRisk;
        private DataGridView _gridRisk;
        private CheckBox _chkDistanceWeight;
        private NumericUpDown _nudWmin, _nudWmax, _nudPod;
        private Label _lblTotalRisk, _lblResidualRisk, _lblRrf;
        private ListView _lvRiskCurve;
        private Button _btnRun;
        private Label _lblStatus, _lblCoverage;
        private ListView _lvPositions, _lvPerCloud;

        public DetectorAllocation Result { get; private set; }

        public DetectorAllocationDialog(Scene3D scene, DetectorAllocation editing = null)
        {
            _scene = scene;
            _editing = editing;
            Text = editing == null ? "New Detector Allocation" : "Edit Detector Allocation";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(880, 620);
            Size = new Size(960, 720);
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
            PopulateFromEditing();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding((int)(10 * dpi)),
                ColumnCount = 1,
                RowCount = 6
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // identity
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // settings
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // risk
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // run row
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // results
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons

            outer.Controls.Add(BuildIdentityBox(dpi), 0, 0);
            outer.Controls.Add(BuildSettingsBox(dpi), 0, 1);
            outer.Controls.Add(BuildRiskBox(dpi), 0, 2);
            outer.Controls.Add(BuildRunRow(dpi), 0, 3);
            outer.Controls.Add(BuildResultsBox(dpi), 0, 4);
            outer.Controls.Add(BuildButtonRow(dpi), 0, 5);
            Controls.Add(outer);

            // Initial visibility for risk panel + label swap.
            UpdateStrategyDependentUI();
        }

        private GroupBox BuildIdentityBox(float dpi)
        {
            var box = new GroupBox { Text = "Identity", Dock = DockStyle.Fill,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(8 * dpi)) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 1 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.Controls.Add(L("Name:", dpi), 0, 0);
            _txtName = new TextBox { Dock = DockStyle.Fill,
                Text = "Allocation " + (_scene.DetectorAllocations.Count + 1) };
            t.Controls.Add(_txtName, 1, 0);
            t.Controls.Add(L("Study:", dpi), 2, 0);
            _cmbStudy = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var st in _scene.DispersionStudies)
                _cmbStudy.Items.Add(new ComboItem { Id = st.Id, Display = st.Name + "  (" + st.SimulationIds.Count + " sims)" });
            if (_cmbStudy.Items.Count > 0) _cmbStudy.SelectedIndex = 0;
            _cmbStudy.SelectedIndexChanged += (s, e) =>
            {
                if (_radioStrategyRisk != null && _radioStrategyRisk.Checked)
                    PopulateRiskGrid();
            };
            t.Controls.Add(_cmbStudy, 3, 0);
            box.Controls.Add(t);
            return box;
        }

        private GroupBox BuildSettingsBox(float dpi)
        {
            var box = new GroupBox { Text = "Allocation settings", Dock = DockStyle.Fill,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(8 * dpi)) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            // Strategy row
            t.Controls.Add(L("Strategy:", dpi), 0, row);
            var stratRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _radioStrategyCov = new RadioButton
            {
                Text = "Greedy max coverage",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 4, 16, 0)
            };
            _radioStrategyRisk = new RadioButton
            {
                Text = "Min residual risk (IOGP × wind rose × consequence)",
                AutoSize = true,
                Margin = new Padding(0, 4, 4, 0)
            };
            _radioStrategyCov.CheckedChanged += (s, e) => UpdateStrategyDependentUI();
            _radioStrategyRisk.CheckedChanged += (s, e) => UpdateStrategyDependentUI();
            stratRow.Controls.AddRange(new Control[] { _radioStrategyCov, _radioStrategyRisk });
            t.SetColumnSpan(stratRow, 3);
            t.Controls.Add(stratRow, 1, row);
            row++;

            // Objective row
            t.Controls.Add(L("Objective:", dpi), 0, row);
            var objRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _radioAll = new RadioButton { Text = "Cover everything", AutoSize = true, Checked = true };
            _radioPercent = new RadioButton { Text = "Stop when", AutoSize = true, Margin = new Padding(20, 4, 4, 0) };
            _nudTargetPct = new NumericUpDown { Minimum = 1m, Maximum = 100m, Value = 95m,
                DecimalPlaces = 0, Increment = 5m, Width = (int)(60 * dpi) };
            _lblTargetPct = new Label { Text = " % of clouds covered", AutoSize = true, Padding = new Padding(2, 6, 0, 0) };
            objRow.Controls.AddRange(new Control[] { _radioAll, _radioPercent, _nudTargetPct, _lblTargetPct });
            t.SetColumnSpan(objRow, 3);
            t.Controls.Add(objRow, 1, row);
            row++;

            // Max detectors
            t.Controls.Add(L("Max detectors:", dpi), 0, row);
            _nudMaxDet = new NumericUpDown { Minimum = 0m, Maximum = 10000m, Value = 0m,
                DecimalPlaces = 0, Increment = 1m, Width = (int)(80 * dpi) };
            t.Controls.Add(_nudMaxDet, 1, row);
            t.Controls.Add(L("(0 = unlimited)", dpi), 2, row);
            row++;

            // Detection radius
            t.Controls.Add(L("Detection radius (m):", dpi), 0, row);
            _nudRadius = new NumericUpDown { Minimum = 0.1m, Maximum = 500m, Value = 5m,
                DecimalPlaces = 2, Increment = 0.5m, Width = (int)(80 * dpi) };
            t.Controls.Add(_nudRadius, 1, row);
            row++;

            // Breathing zone Z range
            t.Controls.Add(L("Breathing zone Z (m):", dpi), 0, row);
            var zRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _nudMinZ = new NumericUpDown { Minimum = 0m, Maximum = 1000m, Value = 1.5m,
                DecimalPlaces = 2, Increment = 0.5m, Width = (int)(80 * dpi) };
            _nudMaxZ = new NumericUpDown { Minimum = 0m, Maximum = 1000m, Value = 3.0m,
                DecimalPlaces = 2, Increment = 0.5m, Width = (int)(80 * dpi) };
            zRow.Controls.Add(_nudMinZ);
            zRow.Controls.Add(new Label { Text = "  to  ", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            zRow.Controls.Add(_nudMaxZ);
            t.SetColumnSpan(zRow, 3);
            t.Controls.Add(zRow, 1, row);
            row++;

            // Candidate grid Nx, Ny, Nz
            t.Controls.Add(L("Candidate grid (Nx × Ny × Nz):", dpi), 0, row);
            var nRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _nudNx = new NumericUpDown { Minimum = 2m, Maximum = 500m, Value = 60m, DecimalPlaces = 0, Width = (int)(60 * dpi) };
            _nudNy = new NumericUpDown { Minimum = 2m, Maximum = 500m, Value = 60m, DecimalPlaces = 0, Width = (int)(60 * dpi) };
            _nudNz = new NumericUpDown { Minimum = 1m, Maximum = 50m,  Value = 3m,  DecimalPlaces = 0, Width = (int)(60 * dpi) };
            nRow.Controls.Add(_nudNx);
            nRow.Controls.Add(new Label { Text = " × ", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            nRow.Controls.Add(_nudNy);
            nRow.Controls.Add(new Label { Text = " × ", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            nRow.Controls.Add(_nudNz);
            t.SetColumnSpan(nRow, 3);
            t.Controls.Add(nRow, 1, row);
            row++;

            // Use existing detectors
            t.Controls.Add(new Label(), 0, row);
            _chkUseExisting = new CheckBox { Text = "Use existing detectors as seed (only add new ones to fill gaps)",
                AutoSize = true };
            t.SetColumnSpan(_chkUseExisting, 3);
            t.Controls.Add(_chkUseExisting, 1, row);

            box.Controls.Add(t);
            return box;
        }

        private GroupBox BuildRiskBox(float dpi)
        {
            _grpRisk = new GroupBox
            {
                Text = "Risk weights (IOGP 434-01 × wind rose × consequence)",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(8 * dpi))
            };

            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // knobs
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // grid (height set via MinimumSize)
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // help

            // Knobs row
            var knobs = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            knobs.Controls.Add(new Label { Text = "Detector POD:", AutoSize = true,
                Padding = new Padding(0, 6, 4, 0) });
            _nudPod = new NumericUpDown
            {
                Minimum = 0m, Maximum = 1m, Value = 1m,
                DecimalPlaces = 3, Increment = 0.05m, Width = (int)(70 * dpi),
                Margin = new Padding(0, 2, 16, 2)
            };
            knobs.Controls.Add(_nudPod);

            _chkDistanceWeight = new CheckBox
            {
                Text = "Distance weight (Rad 2016)",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            knobs.Controls.Add(_chkDistanceWeight);
            knobs.Controls.Add(new Label { Text = "Wmin:", AutoSize = true,
                Padding = new Padding(0, 6, 4, 0) });
            _nudWmin = new NumericUpDown
            {
                Minimum = 0m, Maximum = 1m, Value = 0.5m,
                DecimalPlaces = 2, Increment = 0.05m, Width = (int)(60 * dpi),
                Margin = new Padding(0, 2, 8, 2)
            };
            knobs.Controls.Add(_nudWmin);
            knobs.Controls.Add(new Label { Text = "Wmax:", AutoSize = true,
                Padding = new Padding(0, 6, 4, 0) });
            _nudWmax = new NumericUpDown
            {
                Minimum = 0m, Maximum = 1m, Value = 1m,
                DecimalPlaces = 2, Increment = 0.05m, Width = (int)(60 * dpi),
                Margin = new Padding(0, 2, 2, 2)
            };
            knobs.Controls.Add(_nudWmax);
            t.Controls.Add(knobs, 0, 0);

            // Per-simulation risk weights grid
            _gridRisk = new DataGridView
            {
                Dock = DockStyle.Fill,
                MinimumSize = new Size(0, (int)(140 * dpi)),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };
            _gridRisk.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Sim", HeaderText = "Simulation", ReadOnly = true, FillWeight = 35 });
            _gridRisk.Columns.Add(new DataGridViewCheckBoxColumn
            { Name = "FreqAuto", HeaderText = "Freq Auto", FillWeight = 8 });
            _gridRisk.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Freq", HeaderText = "Freq/yr", FillWeight = 16 });
            _gridRisk.Columns.Add(new DataGridViewCheckBoxColumn
            { Name = "ConsAuto", HeaderText = "Cons Auto", FillWeight = 8 });
            _gridRisk.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Cons", HeaderText = "Consequence", FillWeight = 16 });
            _gridRisk.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Risk", HeaderText = "Risk R_s", ReadOnly = true, FillWeight = 17 });
            _gridRisk.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_gridRisk.IsCurrentCellDirty)
                    _gridRisk.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _gridRisk.CellValueChanged += (s, e) => RefreshRiskGridFromUI();
            t.Controls.Add(_gridRisk, 0, 1);

            t.Controls.Add(new Label
            {
                Text = "Auto frequency = source.LeakFreq × P_wind(direction). "
                    + "Auto consequence = cloud volume × max(1, peak/IDLH) for toxic gases, or "
                    + "× LFL flag for flammables. Uncheck Auto to type a value.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 4, 0, 0),
                MaximumSize = new Size((int)(900 * dpi), 0)
            }, 0, 2);

            _grpRisk.Controls.Add(t);
            return _grpRisk;
        }

        private void UpdateStrategyDependentUI()
        {
            bool risk = _radioStrategyRisk != null && _radioStrategyRisk.Checked;
            if (_grpRisk != null) _grpRisk.Visible = risk;
            if (_lblTargetPct != null)
                _lblTargetPct.Text = risk ? " % Risk Reduction" : " % of clouds covered";
            if (risk) PopulateRiskGrid();
        }

        private TableLayoutPanel BuildRunRow(float dpi)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, Padding = new Padding(0, (int)(4 * dpi), 0, (int)(4 * dpi)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _btnRun = new Button { Text = "Run allocation", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
            _btnRun.Click += (s, e) => RunAllocation();
            t.Controls.Add(_btnRun, 0, 0);
            _lblStatus = new Label { AutoSize = true, ForeColor = SystemColors.GrayText,
                Padding = new Padding(8, (int)(8 * dpi), 0, 0) };
            t.Controls.Add(_lblStatus, 1, 0);
            _lblCoverage = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold) };
            t.Controls.Add(_lblCoverage, 2, 0);
            return t;
        }

        private GroupBox BuildResultsBox(float dpi)
        {
            var box = new GroupBox { Text = "Results", Dock = DockStyle.Fill,
                Padding = new Padding((int)(8 * dpi)) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 2, RowCount = 2 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Risk summary header — populated by MinResidualRisk runs.
            var riskSummary = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            _lblTotalRisk = new Label
            { AutoSize = true, Margin = new Padding(0, 2, 16, 2),
              Text = "Total risk: —" };
            _lblResidualRisk = new Label
            { AutoSize = true, Margin = new Padding(0, 2, 16, 2),
              Text = "Residual: —" };
            _lblRrf = new Label
            { AutoSize = true, Margin = new Padding(0, 2, 16, 2),
              Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold),
              Text = "RRF: —" };
            _lvRiskCurve = new ListView
            {
                View = System.Windows.Forms.View.Details, GridLines = true, FullRowSelect = true,
                Width = (int)(180 * dpi), Height = (int)(60 * dpi),
                Margin = new Padding(0, 0, 0, 0)
            };
            _lvRiskCurve.Columns.Add("K", (int)(40 * dpi));
            _lvRiskCurve.Columns.Add("RRF", (int)(120 * dpi));
            riskSummary.Controls.AddRange(new Control[] { _lblTotalRisk, _lblResidualRisk, _lblRrf, _lvRiskCurve });
            t.SetColumnSpan(riskSummary, 2);
            t.Controls.Add(riskSummary, 0, 0);

            _lvPositions = new ListView { Dock = DockStyle.Fill,
                View = System.Windows.Forms.View.Details, GridLines = true, FullRowSelect = true };
            _lvPositions.Columns.Add("#", (int)(40 * dpi));
            _lvPositions.Columns.Add("X (m)", (int)(80 * dpi));
            _lvPositions.Columns.Add("Y (m)", (int)(80 * dpi));
            _lvPositions.Columns.Add("Z (m)", (int)(80 * dpi));

            _lvPerCloud = new ListView { Dock = DockStyle.Fill,
                View = System.Windows.Forms.View.Details, GridLines = true, FullRowSelect = true };
            _lvPerCloud.Columns.Add("Simulation", (int)(180 * dpi));
            _lvPerCloud.Columns.Add("Covered", (int)(70 * dpi));
            _lvPerCloud.Columns.Add("Residual R_s", (int)(110 * dpi));

            var leftBox = new GroupBox { Text = "Allocated positions", Dock = DockStyle.Fill };
            leftBox.Controls.Add(_lvPositions);
            var rightBox = new GroupBox { Text = "Per-cloud coverage", Dock = DockStyle.Fill };
            rightBox.Controls.Add(_lvPerCloud);
            t.Controls.Add(leftBox, 0, 1);
            t.Controls.Add(rightBox, 1, 1);
            box.Controls.Add(t);
            return box;
        }

        /// <summary>Populates the risk-weights DataGridView from the currently-
        /// selected study and the persisted <see cref="DispersionStudy.RiskWeights"/>.
        /// Computes auto frequency / consequence on demand via
        /// <see cref="RiskWeightHelper"/> so the user sees a live preview.</summary>
        private void PopulateRiskGrid()
        {
            if (_gridRisk == null) return;
            _gridRisk.Rows.Clear();
            var study = ResolveSelectedStudy();
            if (study == null) return;
            double pod = (double)_nudPod.Value;
            foreach (var simId in study.SimulationIds)
            {
                var sim = _scene.Simulations.FirstOrDefault(s => s.Id == simId);
                if (sim == null) continue;
                var risk = study.EnsureRiskFor(simId);
                int idx = _gridRisk.Rows.Add();
                var row = _gridRisk.Rows[idx];
                row.Tag = simId;
                row.Cells["Sim"].Value = sim.Name ?? simId;
                row.Cells["FreqAuto"].Value = risk.FreqMode == RiskValueMode.Auto;
                row.Cells["ConsAuto"].Value = risk.ConsMode == RiskValueMode.Auto;
                row.Cells["Freq"].Value = risk.FreqPerYear.ToString("E3",
                    System.Globalization.CultureInfo.InvariantCulture);
                row.Cells["Cons"].Value = risk.Consequence.ToString("E3",
                    System.Globalization.CultureInfo.InvariantCulture);
                row.Cells["Risk"].Value = "—";
            }
            RefreshRiskGridFromUI();
        }

        /// <summary>Recomputes the Risk column (R_s) for every row using the
        /// auto/manual choices and the auto-derivation helpers. Called every time
        /// the user toggles a checkbox or edits a numeric cell.</summary>
        private void RefreshRiskGridFromUI()
        {
            if (_gridRisk == null) return;
            var study = ResolveSelectedStudy();
            if (study == null) return;
            double pod = (double)_nudPod.Value;
            if (!(pod > 0)) pod = 1.0;

            // Need clouds to compute Auto consequence — load them lazily on first call.
            List<CloudSnapshot> clouds = null;
            try { clouds = DispersionStudyEngine.LoadClouds(study, _scene); }
            catch { clouds = new List<CloudSnapshot>(); }

            foreach (DataGridViewRow row in _gridRisk.Rows)
            {
                if (row.Tag is not string simId) continue;
                var sim = _scene.Simulations.FirstOrDefault(s => s.Id == simId);
                if (sim == null) continue;
                var risk = study.EnsureRiskFor(simId);

                // Commit auto/manual + values from UI back to the model.
                bool freqAuto = ToBool(row.Cells["FreqAuto"].Value, risk.FreqMode == RiskValueMode.Auto);
                bool consAuto = ToBool(row.Cells["ConsAuto"].Value, risk.ConsMode == RiskValueMode.Auto);
                risk.FreqMode = freqAuto ? RiskValueMode.Auto : RiskValueMode.Manual;
                risk.ConsMode = consAuto ? RiskValueMode.Auto : RiskValueMode.Manual;
                double manFreq = ParseDouble(row.Cells["Freq"].Value, risk.FreqPerYear);
                double manCons = ParseDouble(row.Cells["Cons"].Value, risk.Consequence);
                if (!freqAuto) risk.FreqPerYear = manFreq;
                if (!consAuto) risk.Consequence = manCons;

                // Resolve cloud for auto-cons.
                var snap = clouds?.FirstOrDefault(c => c.SimulationId == simId);
                var (freq, cons, rs) = RiskWeightHelper.ResolveScenarioRisk(study, snap, sim, _scene, pod);

                // Reflect auto-derived values back into the read-only-when-auto cells.
                if (freqAuto)
                    row.Cells["Freq"].Value = freq.ToString("E3", System.Globalization.CultureInfo.InvariantCulture);
                if (consAuto)
                    row.Cells["Cons"].Value = cons.ToString("E3", System.Globalization.CultureInfo.InvariantCulture);
                row.Cells["Risk"].Value = rs.ToString("E3", System.Globalization.CultureInfo.InvariantCulture);

                // Disable text cells when Auto is on (read-only); enable when Manual.
                row.Cells["Freq"].ReadOnly = freqAuto;
                row.Cells["Cons"].ReadOnly = consAuto;
                row.Cells["Freq"].Style.ForeColor = freqAuto ? SystemColors.GrayText : SystemColors.ControlText;
                row.Cells["Cons"].Style.ForeColor = consAuto ? SystemColors.GrayText : SystemColors.ControlText;
            }
        }

        private DispersionStudy ResolveSelectedStudy()
        {
            string id = (_cmbStudy?.SelectedItem as ComboItem)?.Id;
            if (string.IsNullOrEmpty(id)) return null;
            return _scene.DispersionStudies.FirstOrDefault(s => s.Id == id);
        }

        private static bool ToBool(object v, bool fallback)
        {
            if (v is bool b) return b;
            if (v == null) return fallback;
            if (bool.TryParse(v.ToString(), out bool parsed)) return parsed;
            return fallback;
        }

        private static double ParseDouble(object v, double fallback)
        {
            if (v == null) return fallback;
            string s = v.ToString();
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                return parsed;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out parsed))
                return parsed;
            return fallback;
        }

        private TableLayoutPanel BuildButtonRow(float dpi)
        {
            // Cancel left of OK (project convention).
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, Padding = new Padding(0, (int)(8 * dpi), 0, 0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", AutoSize = true,
                Padding = new Padding(12, 2, 12, 2), DialogResult = DialogResult.Cancel };
            var btnOK = new Button { Text = "OK", AutoSize = true,
                Padding = new Padding(16, 2, 16, 2) };
            btnOK.Click += (s, e) => CommitAndClose();
            t.Controls.Add(new Label(), 0, 0);
            t.Controls.Add(btnCancel, 1, 0);
            t.Controls.Add(btnOK, 2, 0);
            AcceptButton = btnOK;
            CancelButton = btnCancel;
            return t;
        }

        private static Label L(string text, float dpi) => new Label
        {
            Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
            Padding = new Padding(0, (int)(6 * dpi), (int)(6 * dpi), 0)
        };

        private void PopulateFromEditing()
        {
            if (_editing == null)
            {
                Result = new DetectorAllocation();
                UpdateStrategyDependentUI();
                return;
            }
            _txtName.Text = _editing.Name ?? "";
            int idx = -1;
            for (int i = 0; i < _cmbStudy.Items.Count; i++)
                if (((ComboItem)_cmbStudy.Items[i]).Id == _editing.DispersionStudyId) { idx = i; break; }
            if (idx >= 0) _cmbStudy.SelectedIndex = idx;
            _radioStrategyCov.Checked = _editing.Strategy == AllocationStrategy.GreedyMaxCoverage;
            _radioStrategyRisk.Checked = _editing.Strategy == AllocationStrategy.MinResidualRisk;
            _radioAll.Checked = _editing.Objective == AllocationObjective.CoverAll;
            _radioPercent.Checked = _editing.Objective == AllocationObjective.CoverPercentage;
            _nudTargetPct.Value = (decimal)Math.Max(1, Math.Min(100, _editing.TargetCoveragePercent));
            _nudMaxDet.Value = Math.Max(0, _editing.MaxDetectors);
            _nudRadius.Value = (decimal)Math.Max(0.1, Math.Min(500, _editing.DetectionRadiusM));
            _nudMinZ.Value = (decimal)Math.Max(0, Math.Min(1000, _editing.MinZ));
            _nudMaxZ.Value = (decimal)Math.Max(0, Math.Min(1000, _editing.MaxZ));
            _nudNx.Value = Math.Max(2, Math.Min(500, _editing.CandidateNx));
            _nudNy.Value = Math.Max(2, Math.Min(500, _editing.CandidateNy));
            _nudNz.Value = Math.Max(1, Math.Min(50, _editing.CandidateNz));
            _chkUseExisting.Checked = _editing.UseExistingDetectors;
            _nudPod.Value = (decimal)Math.Max(0, Math.Min(1, _editing.DetectionProbability));
            _chkDistanceWeight.Checked = _editing.UseDistanceWeighting;
            _nudWmin.Value = (decimal)Math.Max(0, Math.Min(1, _editing.DistanceWeightMin));
            _nudWmax.Value = (decimal)Math.Max(0, Math.Min(1, _editing.DistanceWeightMax));
            UpdateStrategyDependentUI();
            PopulateResultsList(_editing);
        }

        private void PopulateResultsList(DetectorAllocation a)
        {
            _lvPositions.Items.Clear();
            int n = 1;
            foreach (var p in a.AllocatedPositions ?? new List<System.Windows.Media.Media3D.Point3D>())
            {
                var lvi = new ListViewItem((n++).ToString());
                lvi.SubItems.Add(p.X.ToString("F2"));
                lvi.SubItems.Add(p.Y.ToString("F2"));
                lvi.SubItems.Add(p.Z.ToString("F2"));
                _lvPositions.Items.Add(lvi);
            }
            _lvPerCloud.Items.Clear();
            var residuals = a.PerCloudResidualRisk ?? new Dictionary<string, double>();
            foreach (var kv in a.PerCloudCovered ?? new Dictionary<string, bool>())
            {
                var sim = _scene.Simulations.FirstOrDefault(s => s.Id == kv.Key);
                var lvi = new ListViewItem(sim?.Name ?? kv.Key);
                lvi.SubItems.Add(kv.Value ? "Yes" : "No");
                residuals.TryGetValue(kv.Key, out double r);
                lvi.SubItems.Add(a.Strategy == AllocationStrategy.MinResidualRisk
                    ? r.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                    : "");
                lvi.ForeColor = kv.Value ? Color.DarkGreen : Color.Firebrick;
                _lvPerCloud.Items.Add(lvi);
            }
            if (a.Status == AllocationStatus.Completed)
                _lblCoverage.Text = a.AchievedCoveragePercent.ToString("F1") + "% coverage";

            // Risk summary
            if (a.Strategy == AllocationStrategy.MinResidualRisk
                && a.Status == AllocationStatus.Completed)
            {
                _lblTotalRisk.Text = "Total risk: "
                    + a.TotalRisk.ToString("E3", System.Globalization.CultureInfo.InvariantCulture);
                _lblResidualRisk.Text = "Residual: "
                    + a.ResidualRisk.ToString("E3", System.Globalization.CultureInfo.InvariantCulture);
                _lblRrf.Text = "RRF: " + (a.RiskReductionFraction * 100.0).ToString("F1") + " %";
                _lvRiskCurve.Items.Clear();
                if (a.RiskCurveK != null && a.RiskCurveRRF != null)
                {
                    int count = Math.Min(a.RiskCurveK.Count, a.RiskCurveRRF.Count);
                    for (int i = 0; i < count; i++)
                    {
                        var lvi = new ListViewItem(a.RiskCurveK[i].ToString());
                        lvi.SubItems.Add((a.RiskCurveRRF[i] * 100.0).ToString("F1") + " %");
                        _lvRiskCurve.Items.Add(lvi);
                    }
                }
            }
            else
            {
                _lblTotalRisk.Text = "Total risk: —";
                _lblResidualRisk.Text = "Residual: —";
                _lblRrf.Text = "RRF: —";
                _lvRiskCurve.Items.Clear();
            }
        }

        private DetectorAllocation BuildAllocationFromUI()
        {
            var a = _editing ?? new DetectorAllocation();
            a.Name = string.IsNullOrWhiteSpace(_txtName.Text) ? "Allocation" : _txtName.Text.Trim();
            a.DispersionStudyId = (_cmbStudy.SelectedItem as ComboItem)?.Id ?? "";
            a.Strategy = _radioStrategyRisk.Checked
                ? AllocationStrategy.MinResidualRisk
                : AllocationStrategy.GreedyMaxCoverage;
            a.Objective = _radioPercent.Checked
                ? AllocationObjective.CoverPercentage
                : AllocationObjective.CoverAll;
            a.TargetCoveragePercent = (double)_nudTargetPct.Value;
            a.MaxDetectors = (int)_nudMaxDet.Value;
            a.DetectionRadiusM = (double)_nudRadius.Value;
            a.MinZ = (double)_nudMinZ.Value;
            a.MaxZ = (double)_nudMaxZ.Value;
            a.CandidateNx = (int)_nudNx.Value;
            a.CandidateNy = (int)_nudNy.Value;
            a.CandidateNz = (int)_nudNz.Value;
            a.UseExistingDetectors = _chkUseExisting.Checked;
            a.DetectionProbability = (double)_nudPod.Value;
            a.UseDistanceWeighting = _chkDistanceWeight.Checked;
            a.DistanceWeightMin = (double)_nudWmin.Value;
            a.DistanceWeightMax = (double)_nudWmax.Value;
            // Risk-weight grid edits already mutated study.RiskWeights via RefreshRiskGridFromUI.
            return a;
        }

        private void RunAllocation()
        {
            if (_cmbStudy.SelectedItem == null)
            { _lblStatus.Text = "Pick a Dispersion Study first."; return; }
            var a = BuildAllocationFromUI();
            var study = _scene.DispersionStudies.FirstOrDefault(s => s.Id == a.DispersionStudyId);
            if (study == null) { _lblStatus.Text = "Study not found."; return; }

            Cursor = Cursors.WaitCursor;
            _lblStatus.Text = "Loading clouds...";
            Application.DoEvents();
            try
            {
                var clouds = DispersionStudyEngine.LoadClouds(study, _scene);
                _lblStatus.Text = string.Format("Loaded {0} clouds ({1} valid). Running {2}...",
                    clouds.Count, clouds.Count(c => c.IsValid),
                    a.Strategy == AllocationStrategy.MinResidualRisk
                        ? "risk-reduction allocator"
                        : "greedy allocator");
                Application.DoEvents();
                // Obstacle bboxes from current decorations.
                var obstacles = new List<BoundingBox>();
                foreach (var d in _scene.Decorations)
                    if (d.BoundingBox != null) obstacles.Add(d.BoundingBox);

                double domainHalf = clouds.Where(c => c.IsValid).Select(c => c.DomainHalfM)
                    .DefaultIfEmpty(200.0).Max();

                var r = DetectorAllocator.Run(a, study, _scene,
                    clouds, obstacles, _scene.GasDetectors, domainHalf);

                a.AllocatedPositions = r.Positions;
                a.AchievedCoveragePercent = r.CoveragePercent;
                a.PerCloudCovered = r.PerCloudCovered;
                a.TotalRisk = r.TotalRisk;
                a.ResidualRisk = r.ResidualRisk;
                a.RiskReductionFraction = r.RiskReductionFraction;
                a.RiskCurveK = r.RiskCurveK;
                a.RiskCurveRRF = r.RiskCurveRRF;
                a.PerCloudResidualRisk = r.PerCloudResidualRisk;
                a.Status = AllocationStatus.Completed;
                a.StatusMessage = r.Message;
                a.RunAt = DateTime.Now;
                PopulateResultsList(a);
                _lblStatus.Text = r.Message + "  " + r.CandidateCount + " candidates evaluated.";
                Result = a;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Run failed: " + ex.Message;
                a.Status = AllocationStatus.Failed;
                a.StatusMessage = ex.Message;
            }
            finally { Cursor = Cursors.Default; }
        }

        private void CommitAndClose()
        {
            // If user didn't click Run, still build the configuration so the
            // allocation is created/saved (without results).
            Result = BuildAllocationFromUI();
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class ComboItem
        {
            public string Id;
            public string Display;
            public override string ToString() => Display;
        }
    }
}
