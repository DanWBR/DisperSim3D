using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Wizard for creating a new <see cref="Simulation"/>: pick a Source, a WindField, and the solver.
    /// On OK, the dialog returns a Simulation with Status=Configured (snapshot is taken at Run time).
    /// </summary>
    public class SimulationEditorDialog : Form
    {
        private readonly Scene3D _scene;
        private TextBox txtName;
        private ComboBox cmbSource;
        private ComboBox cmbWindField;
        private ComboBox cmbSolver;
        private NumericUpDown nudDuration;
        private NumericUpDown nudTimeStep;
        private NumericUpDown nudDomain;
        private NumericUpDown nudGrid;
        private NumericUpDown nudSnapCount;

        public Simulation Result { get; private set; }

        private readonly Simulation _editingSim;

        public SimulationEditorDialog(Scene3D scene, string preselectedSourceId = null)
        {
            _scene = scene;
            BuildUI();
            PopulateLists(preselectedSourceId);
        }

        /// <summary>Edits an existing Simulation in place: dialog is pre-populated from
        /// <paramref name="sim"/> and on OK the same instance is updated (Id preserved).
        /// Used by Configure &amp; Run on an existing simulation node.</summary>
        public SimulationEditorDialog(Scene3D scene, Simulation sim)
        {
            _scene = scene;
            _editingSim = sim;
            BuildUI();
            this.Text = "Configure Simulation";
            PopulateLists(sim?.SourceId);
            // Pre-fill UI from the existing simulation.
            if (sim != null)
            {
                txtName.Text = sim.Name ?? "";
                if (!string.IsNullOrEmpty(sim.WindFieldId))
                {
                    int wi = _scene.WindFieldScenarios.FindIndex(w => w.Id == sim.WindFieldId);
                    if (wi >= 0) cmbWindField.SelectedIndex = wi + 1;
                }
                int sx = (int)sim.SolverType;
                if (sx >= 0 && sx < cmbSolver.Items.Count) cmbSolver.SelectedIndex = sx;
                if (sim.SnapshotDurationS > 0) nudDuration.Value = (decimal)Math.Max(1, Math.Min(100000, sim.SnapshotDurationS));
                if (sim.SnapshotTimeStepS > 0) nudTimeStep.Value = (decimal)Math.Max(0.01, Math.Min(60, sim.SnapshotTimeStepS));
                if (sim.SnapshotDomainSizeM > 0) nudDomain.Value = (decimal)Math.Max(10, Math.Min(100000, sim.SnapshotDomainSizeM));
                if (sim.SnapshotGridResolution > 0) nudGrid.Value = (decimal)Math.Max(10, Math.Min(500, sim.SnapshotGridResolution));
                if (sim.SnapshotCount > 0) nudSnapCount.Value = (decimal)Math.Max(2, Math.Min(1000, sim.SnapshotCount));
            }
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "New Simulation";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = new Padding((int)(10 * dpi));

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 2
            };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var table = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(280 * dpi)));

            int row = 0;
            txtName = new TextBox { Dock = DockStyle.Fill, Text = "Simulation " + (_scene.Simulations.Count + 1) };
            DialogHelpers.AddRowWithHelp(table, ref row, "Name:", txtName,
                "Display name shown in the project tree and result tables.");

            cmbSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            DialogHelpers.AddRowWithHelp(table, ref row, "Source:", cmbSource,
                "Release source whose configuration will be snapshotted at Run time.");

            cmbWindField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            DialogHelpers.AddRowWithHelp(table, ref row, "Wind Field:", cmbWindField,
                "Pre-computed wind field that this simulation will advect through. Must be Ready before Run.");

            cmbSolver = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbSolver.Items.AddRange(new object[] {
                "Gaussian Puff (Transient)",
                "Gaussian Plume (Steady-State)",
                "CFD Transient (rhoReactingBuoyantFoam) — universal",
                "FluidX3D Wind (GPU LBM)",
                "FluidX3D Dispersion (GPU LBM)",
                "FluidX3D Fire (Hot Buoyant Plume)",
                "FluidX3D Dispersion (Steady State)"
            });
            cmbSolver.SelectedIndex = 0;
            // Owner-draw: prepend a gold star to every FluidX3D row so the
            // recommended GPU solvers stand out in both the dropdown and the
            // closed combo. Non-FluidX3D rows render as plain text.
            cmbSolver.DrawMode = DrawMode.OwnerDrawFixed;
            cmbSolver.DrawItem += SolverCombo_DrawItem;
            DialogHelpers.AddRowWithHelp(table, ref row, "Solver:", cmbSolver,
                "Dispersion solver. Gaussian models are fast; CFD solvers respect obstacles and require more time. " +
                "★ marks the GPU-accelerated FluidX3D solvers — recommended for fast iteration.");

            double defaultDur = _scene.GeneralSettings?.DefaultMeteo != null ? 300 : 300;
            double defaultDomain = _scene.GeneralSettings?.DefaultDomainSizeM ?? 200;
            int defaultGrid = _scene.GeneralSettings?.DefaultGridResolution ?? 40;

            nudDuration = MakeNud(1m, 100000m, (decimal)defaultDur, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Duration (s):", nudDuration,
                "Total simulation time.");

            nudTimeStep = MakeNud(0.01m, 60m, 0.5m, 2);
            DialogHelpers.AddRowWithHelp(table, ref row, "Time Step (s):", nudTimeStep,
                "Output write interval.");

            nudDomain = MakeNud(10m, 100000m, (decimal)defaultDomain, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Domain Half-Size (m):", nudDomain,
                "Half-extent of the simulation box.");

            nudSnapCount = MakeNud(2m, 1000m, 20m, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Snapshot Count:", nudSnapCount,
                "Number of concentration snapshots written between t=0 and t=duration. Higher = smoother playback, more disk. Default 20.");

            nudGrid = MakeNud(10m, 500m, (decimal)defaultGrid, 0);
            DialogHelpers.AddRowWithHelp(table, ref row, "Grid Resolution:", nudGrid,
                "Cells per axis.");

            outer.Controls.Add(table, 0, 0);

            var btns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += BtnOK_Click;
            btns.Controls.Add(new Label(), 0, 0);
            btns.Controls.Add(btnCancel, 1, 0);
            btns.Controls.Add(btnOK, 2, 0);
            outer.Controls.Add(btns, 0, 1);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
            this.Controls.Add(outer);
            this.ApplyDpiScaling();
        }

        private void PopulateLists(string preselectedSourceId)
        {
            cmbSource.Items.Clear();
            cmbSource.Items.Add("(none)");
            int presel = 0;
            for (int i = 0; i < _scene.TopLevelSources.Count; i++)
            {
                var s = _scene.TopLevelSources[i];
                cmbSource.Items.Add(s.Name + "  [" + (s.Id ?? "").Substring(0, Math.Min(8, (s.Id ?? "").Length)) + "]");
                if (s.Id == preselectedSourceId) presel = i + 1;
            }
            cmbSource.SelectedIndex = Math.Min(presel, cmbSource.Items.Count - 1);

            cmbWindField.Items.Clear();
            cmbWindField.Items.Add("(none)");
            for (int i = 0; i < _scene.WindFieldScenarios.Count; i++)
            {
                var wf = _scene.WindFieldScenarios[i];
                cmbWindField.Items.Add(wf.Name + " [" + wf.Status + "]");
            }
            if (cmbWindField.Items.Count > 1) cmbWindField.SelectedIndex = 1;
            else cmbWindField.SelectedIndex = 0;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            int srcIdx = cmbSource.SelectedIndex - 1;
            int wfIdx = cmbWindField.SelectedIndex - 1;
            if (srcIdx < 0 || srcIdx >= _scene.TopLevelSources.Count)
            {
                MessageBox.Show(this, "Pick a source.", "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }
            if (wfIdx < 0 || wfIdx >= _scene.WindFieldScenarios.Count)
            {
                MessageBox.Show(this, "Pick a wind field.", "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            CfdSolverType solverType = CfdSolverType.GaussianPuff;
            // Combo trimmed down after the 7 redundant OpenFOAM variants were
            // retired. Order must match cmbSolver.Items.AddRange below.
            switch (cmbSolver.SelectedIndex)
            {
                case 0: solverType = CfdSolverType.GaussianPuff; break;
                case 1: solverType = CfdSolverType.GaussianPlume; break;
                case 2: solverType = CfdSolverType.RhoReactingBuoyantFoam; break;
                case 3: solverType = CfdSolverType.FluidX3DWind; break;
                case 4: solverType = CfdSolverType.FluidX3DDispersion; break;
                case 5: solverType = CfdSolverType.FluidX3DFire; break;
                case 6: solverType = CfdSolverType.FluidX3DDispersionSteady; break;
            }

            // Edit existing sim in place (preserves Id) when launched via the editing
            // constructor; otherwise create a new instance.
            var target = _editingSim ?? new Simulation();
            target.Name = string.IsNullOrEmpty(txtName.Text) ? "Simulation" : txtName.Text;
            target.SourceId = _scene.TopLevelSources[srcIdx].Id;
            target.WindFieldId = _scene.WindFieldScenarios[wfIdx].Id;
            target.SolverType = solverType;
            target.Status = SimulationStatus.Configured;
            target.SnapshotDurationS = (double)nudDuration.Value;
            target.SnapshotTimeStepS = (double)nudTimeStep.Value;
            target.SnapshotDomainSizeM = (double)nudDomain.Value;
            target.SnapshotGridResolution = (int)nudGrid.Value;
            target.SnapshotCount = (int)nudSnapCount.Value;
            Result = target;
        }

        /// <summary>Owner-draw handler for the solver combo: prefixes every FluidX3D entry
        /// with a gold-coloured star to flag the GPU-accelerated solvers as the recommended
        /// fast-iteration path. Non-FluidX3D rows render exactly like the default
        /// <see cref="ComboBoxStyle.DropDownList"/> would.</summary>
        private void SolverCombo_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= cmbSolver.Items.Count) return;
            e.DrawBackground();

            string text = cmbSolver.Items[e.Index].ToString() ?? string.Empty;
            bool isFluidX3D = text.StartsWith("FluidX3D", StringComparison.Ordinal);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            System.Drawing.Color fg = selected
                ? System.Drawing.SystemColors.HighlightText
                : cmbSolver.ForeColor;

            int x = e.Bounds.Left + 2;
            int y = e.Bounds.Top + (e.Bounds.Height - e.Font.Height) / 2;

            if (isFluidX3D)
            {
                // Star colour stays the same gold whether the row is selected (blue
                // background) or not — the contrast is fine against both.
                const string star = "★"; // ★
                TextRenderer.DrawText(e.Graphics, star, e.Font,
                    new System.Drawing.Point(x, y), System.Drawing.Color.Gold,
                    TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(e.Graphics, star + " ", e.Font,
                    new System.Drawing.Size(int.MaxValue, e.Bounds.Height),
                    TextFormatFlags.NoPadding).Width;
            }

            TextRenderer.DrawText(e.Graphics, text, e.Font,
                new System.Drawing.Point(x, y), fg, TextFormatFlags.NoPadding);

            e.DrawFocusRectangle();
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
