#nullable enable
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>SimulationEditorDialog</c>.
    /// Wizard for creating a new <see cref="Simulation"/>: pick a Source, a
    /// WindField, the solver, and timing/resolution parameters. On OK the
    /// dialog returns a Simulation with <see cref="SimulationStatus.Configured"/>
    /// (snapshot is taken at Run time). The second constructor edits an
    /// existing Simulation in place — same instance, Id preserved.
    /// </summary>
    public partial class SimulationEditorDialog : Window
    {
        private readonly Scene3D _scene;
        private readonly Simulation? _editingSim;

        public Simulation? Result { get; private set; }

        public SimulationEditorDialog() : this(new Scene3D(), (string?)null) { }

        public SimulationEditorDialog(Scene3D scene, string? preselectedSourceId = null)
        {
            _scene = scene;
            InitializeComponent();

            TxtName.Text = "Simulation " + (_scene.Simulations.Count + 1);
            CmbSolver.SelectedIndex = 0;

            // Pick up engine defaults so a fresh sim matches what the WPF dialog produces.
            double defaultDomain = _scene.GeneralSettings?.DefaultDomainSizeM ?? 200;
            int defaultGrid      = _scene.GeneralSettings?.DefaultGridResolution ?? 40;
            NudDomain.Value = (decimal)defaultDomain;
            NudGrid.Value   = defaultGrid;

            PopulateLists(preselectedSourceId);
        }

        /// <summary>Edits an existing Simulation in place: dialog is pre-populated
        /// from <paramref name="sim"/> and on OK the same instance is updated
        /// (Id preserved). Used by Configure &amp; Run on an existing simulation node.</summary>
        public SimulationEditorDialog(Scene3D scene, Simulation sim) : this(scene, sim?.SourceId)
        {
            _editingSim = sim;
            Title = "Configure Simulation";

            if (sim != null)
            {
                TxtName.Text = sim.Name ?? "";
                if (!string.IsNullOrEmpty(sim.WindFieldId))
                {
                    int wi = _scene.WindFieldScenarios.FindIndex(w => w.Id == sim.WindFieldId);
                    if (wi >= 0) CmbWindField.SelectedIndex = wi + 1;
                }
                int sx = (int)sim.SolverType;
                if (sx >= 0 && sx < CmbSolver.Items.Count) CmbSolver.SelectedIndex = sx;
                if (sim.SnapshotDurationS > 0)
                    NudDuration.Value = (decimal)Math.Max(1, Math.Min(100000, sim.SnapshotDurationS));
                if (sim.SnapshotTimeStepS > 0)
                    NudTimeStep.Value = (decimal)Math.Max(0.01, Math.Min(60, sim.SnapshotTimeStepS));
                if (sim.SnapshotDomainSizeM > 0)
                    NudDomain.Value = (decimal)Math.Max(10, Math.Min(100000, sim.SnapshotDomainSizeM));
                if (sim.SnapshotGridResolution > 0)
                    NudGrid.Value = (decimal)Math.Max(10, Math.Min(500, sim.SnapshotGridResolution));
                if (sim.SnapshotCount > 0)
                    NudSnapCount.Value = (decimal)Math.Max(2, Math.Min(1000, sim.SnapshotCount));
            }
        }

        private void PopulateLists(string? preselectedSourceId)
        {
            CmbSource.Items.Clear();
            CmbSource.Items.Add(new ComboBoxItem { Content = "(none)" });
            int presel = 0;
            for (int i = 0; i < _scene.TopLevelSources.Count; i++)
            {
                var s = _scene.TopLevelSources[i];
                string idShort = string.IsNullOrEmpty(s.Id)
                    ? ""
                    : s.Id.Substring(0, Math.Min(8, s.Id.Length));
                CmbSource.Items.Add(new ComboBoxItem
                {
                    Content = (s.Name ?? "(unnamed)") + "  [" + idShort + "]"
                });
                if (s.Id == preselectedSourceId) presel = i + 1;
            }
            CmbSource.SelectedIndex = Math.Min(presel, CmbSource.Items.Count - 1);

            CmbWindField.Items.Clear();
            CmbWindField.Items.Add(new ComboBoxItem { Content = "(none)" });
            foreach (var wf in _scene.WindFieldScenarios)
                CmbWindField.Items.Add(new ComboBoxItem
                {
                    Content = (wf.Name ?? "(unnamed)") + " [" + wf.Status + "]"
                });
            CmbWindField.SelectedIndex = CmbWindField.Items.Count > 1 ? 1 : 0;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            int srcIdx = CmbSource.SelectedIndex - 1;
            int wfIdx  = CmbWindField.SelectedIndex - 1;

            // The "(none)" entry sits at index 0, so a value < 0 means "nothing
            // valid picked". Surface the error inline rather than via MessageBox —
            // there's no native one in Avalonia and the banner is plenty.
            if (srcIdx < 0 || srcIdx >= _scene.TopLevelSources.Count)
            {
                ErrorText.Text = "Pick a source.";
                ErrorBanner.IsVisible = true;
                return;
            }
            if (wfIdx < 0 || wfIdx >= _scene.WindFieldScenarios.Count)
            {
                ErrorText.Text = "Pick a wind field.";
                ErrorBanner.IsVisible = true;
                return;
            }

            // Map combo index → CfdSolverType. Matches the WPF dialog row-for-row.
            CfdSolverType solverType = CmbSolver.SelectedIndex switch
            {
                0 => CfdSolverType.GaussianPuff,
                1 => CfdSolverType.GaussianPlume,
                2 => CfdSolverType.ScalarTransportFoam,
                3 => CfdSolverType.ScalarTransportFoamSteady,
                4 => CfdSolverType.ScalarSimpleFoam,
                5 => CfdSolverType.PimpleFoam,
                6 => CfdSolverType.BuoyantPimpleFoam,
                7 => CfdSolverType.ReactingFoam,
                8 => CfdSolverType.RhoSimpleFoam,
                9 => CfdSolverType.RhoReactingBuoyantFoam,
                10 => CfdSolverType.FluidX3DWind,
                11 => CfdSolverType.FluidX3DDispersion,
                12 => CfdSolverType.FluidX3DFire,
                13 => CfdSolverType.FluidX3DDispersionSteady,
                _ => CfdSolverType.GaussianPuff
            };

            // Edit existing sim in place (preserves Id) when launched via the
            // editing constructor; otherwise create a new instance.
            var target = _editingSim ?? new Simulation();
            target.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Simulation" : TxtName.Text.Trim();
            target.SourceId               = _scene.TopLevelSources[srcIdx].Id;
            target.WindFieldId            = _scene.WindFieldScenarios[wfIdx].Id;
            target.SolverType             = solverType;
            target.Status                 = SimulationStatus.Configured;
            target.SnapshotDurationS      = (double)(NudDuration.Value ?? 300m);
            target.SnapshotTimeStepS      = (double)(NudTimeStep.Value ?? 0.5m);
            target.SnapshotDomainSizeM    = (double)(NudDomain.Value ?? 200m);
            target.SnapshotGridResolution = (int)(NudGrid.Value ?? 40m);
            target.SnapshotCount          = (int)(NudSnapCount.Value ?? 20m);
            Result = target;

            Close(true);
        }
    }
}
