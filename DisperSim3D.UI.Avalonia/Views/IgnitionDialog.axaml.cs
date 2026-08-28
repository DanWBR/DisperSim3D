#nullable enable
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Edits an <see cref="IgnitionEvent"/>: which dispersion run is ignited, where,
    /// when, and the two burn parameters — the envelope fraction of the LFL and the
    /// flame speed. The simulation list is limited to completed runs, since an
    /// ignition needs a concentration field to burn.
    /// </summary>
    public partial class IgnitionDialog : Window
    {
        private readonly List<Simulation> _simulations = new();

        public IgnitionEvent Result { get; private set; } = new IgnitionEvent();

        public IgnitionDialog() : this(null, null) { }

        public IgnitionDialog(Scene3D? scene, IgnitionEvent? existing)
        {
            InitializeComponent();

            if (scene?.Simulations != null)
            {
                foreach (var sim in scene.Simulations)
                {
                    if (sim == null || sim.Status != SimulationStatus.Completed) continue;
                    _simulations.Add(sim);
                    CmbSimulation.Items.Add(new ComboBoxItem { Content = sim.Name });
                }
            }
            if (_simulations.Count == 0)
                CmbSimulation.Items.Add(new ComboBoxItem { Content = "(no completed simulations)" });
            CmbSimulation.SelectedIndex = 0;

            if (existing != null)
            {
                TxtName.Text          = existing.Name ?? "Ignition";
                NudX.Value            = (decimal)existing.Position.X;
                NudY.Value            = (decimal)existing.Position.Y;
                NudZ.Value            = (decimal)existing.Position.Z;
                NudTime.Value         = (decimal)existing.TimeS;
                NudEnvelope.Value     = (decimal)existing.EnvelopeFraction;
                NudFlameSpeed.Value   = (decimal)existing.FlameSpeedMS;

                int index = _simulations.FindIndex(s => s.Id == existing.SimulationId);
                if (index >= 0) CmbSimulation.SelectedIndex = index;
            }
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            int index = CmbSimulation.SelectedIndex;
            string simulationId = index >= 0 && index < _simulations.Count
                ? _simulations[index].Id
                : "";

            Result = new IgnitionEvent
            {
                Name             = string.IsNullOrWhiteSpace(TxtName.Text) ? "Ignition" : TxtName.Text.Trim(),
                SimulationId     = simulationId,
                Position         = new Point3D(
                                       (double)(NudX.Value ?? 0m),
                                       (double)(NudY.Value ?? 0m),
                                       (double)(NudZ.Value ?? 0m)),
                TimeS            = (double)(NudTime.Value ?? 0m),
                EnvelopeFraction = (double)(NudEnvelope.Value ?? 0.5m),
                FlameSpeedMS     = (double)(NudFlameSpeed.Value ?? 10m)
            };
            Close(true);
        }
    }
}
