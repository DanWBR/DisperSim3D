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

        public SimulationEditorDialog(Scene3D scene, string preselectedSourceId = null)
        {
            _scene = scene;
            BuildUI();
            PopulateLists(preselectedSourceId);
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
                "CFD Transient (scalarTransportFoam)",
                "CFD Steady (scalarTransportFoam)",
                "CFD Steady (simpleFoam + scalar)",
                "CFD Transient (pimpleFoam)",
                "CFD Transient (buoyantPimpleFoam)",
                "CFD Transient (reactingFoam)",
                "CFD Steady (rhoSimpleFoam)",
                "CFD Transient (rhoReactingBuoyantFoam) — universal",
                "FluidX3D Wind (GPU LBM)",
                "FluidX3D Dispersion (GPU LBM)"
            });
            cmbSolver.SelectedIndex = 0;
            DialogHelpers.AddRowWithHelp(table, ref row, "Solver:", cmbSolver,
                "Dispersion solver. Gaussian models are fast; CFD solvers respect obstacles and require more time.");

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
            switch (cmbSolver.SelectedIndex)
            {
                case 0: solverType = CfdSolverType.GaussianPuff; break;
                case 1: solverType = CfdSolverType.GaussianPlume; break;
                case 2: solverType = CfdSolverType.ScalarTransportFoam; break;
                case 3: solverType = CfdSolverType.ScalarTransportFoamSteady; break;
                case 4: solverType = CfdSolverType.ScalarSimpleFoam; break;
                case 5: solverType = CfdSolverType.PimpleFoam; break;
                case 6: solverType = CfdSolverType.BuoyantPimpleFoam; break;
                case 7: solverType = CfdSolverType.ReactingFoam; break;
                case 8: solverType = CfdSolverType.RhoSimpleFoam; break;
                case 9: solverType = CfdSolverType.RhoReactingBuoyantFoam; break;
                case 10: solverType = CfdSolverType.FluidX3DWind; break;
                case 11: solverType = CfdSolverType.FluidX3DDispersion; break;
            }

            Result = new Simulation
            {
                Name = string.IsNullOrEmpty(txtName.Text) ? "Simulation" : txtName.Text,
                SourceId = _scene.TopLevelSources[srcIdx].Id,
                WindFieldId = _scene.WindFieldScenarios[wfIdx].Id,
                SolverType = solverType,
                Status = SimulationStatus.Configured,
                SnapshotDurationS = (double)nudDuration.Value,
                SnapshotTimeStepS = (double)nudTimeStep.Value,
                SnapshotDomainSizeM = (double)nudDomain.Value,
                SnapshotGridResolution = (int)nudGrid.Value,
                SnapshotCount = (int)nudSnapCount.Value
            };
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
