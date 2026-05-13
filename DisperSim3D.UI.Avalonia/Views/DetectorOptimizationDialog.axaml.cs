#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>DetectorOptimizationDialog</c>.
    /// Configuration + result dialog for gas-detector placement optimisation
    /// (Vianna 2019 Set Covering Problem). The user picks which Completed
    /// simulations to consider and defines the protected region. On OK the
    /// dialog exposes <see cref="ResultDetectorPositions"/> for the caller to
    /// turn into <see cref="GasDetector3D"/> entries.
    /// </summary>
    public partial class DetectorOptimizationDialog : Window
    {
        private readonly Scene3D _scene;
        private readonly ObservableCollection<SimRow> _simRows = new();
        private readonly ObservableCollection<DetectorRow> _resultRows = new();

        public List<Point3D> ResultDetectorPositions { get; private set; } = new();

        public DetectorOptimizationDialog() : this(new Scene3D()) { }

        public DetectorOptimizationDialog(Scene3D scene)
        {
            _scene = scene;
            InitializeComponent();
            CmbNeighborhood.SelectedIndex = 0;

            // Simulation chooser — checkbox per Completed simulation.
            LstSimulations.ItemsSource = _simRows;
            LstSimulations.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<SimRow>(
                (row, _) =>
                {
                    var cb = new CheckBox
                    {
                        Content = row.DisplayName,
                        IsChecked = row.IsChecked,
                        Margin = new global::Avalonia.Thickness(4, 1)
                    };
                    cb.IsCheckedChanged += (_, _) => row.IsChecked = cb.IsChecked == true;
                    return cb;
                });

            GridResults.ItemsSource = _resultRows;
            GridResults.Columns.Add(new DataGridTextColumn
            {
                Header = "#",
                Width = new DataGridLength(40),
                Binding = new Binding(nameof(DetectorRow.Index))
            });
            GridResults.Columns.Add(new DataGridTextColumn
            {
                Header = "X (m)",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(DetectorRow.X))
            });
            GridResults.Columns.Add(new DataGridTextColumn
            {
                Header = "Y (m)",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(DetectorRow.Y))
            });
            GridResults.Columns.Add(new DataGridTextColumn
            {
                Header = "Z (m)",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(DetectorRow.Z))
            });

            PopulateSimulations();
            PrepopulateRegionFromScene();
        }

        private void PopulateSimulations()
        {
            _simRows.Clear();
            if (_scene.Simulations == null) return;
            foreach (var sim in _scene.Simulations)
            {
                if (sim.Status != SimulationStatus.Completed) continue;
                _simRows.Add(new SimRow
                {
                    Simulation = sim,
                    DisplayName = (sim.Name ?? "(unnamed)")
                                  + " [" + sim.SolverType + ", " + sim.Status + "]",
                    IsChecked = true
                });
            }
        }

        private void PrepopulateRegionFromScene()
        {
            // Default to the largest decoration's bounding box. Falls back to
            // the dialog's hard-coded ±50 m defaults when no decoration carries
            // a usable box.
            if (_scene.Decorations == null || _scene.Decorations.Count == 0) return;
            BoundingBox? biggest = null;
            double biggestVol = 0;
            foreach (var d in _scene.Decorations)
            {
                if (d.BoundingBox == null) continue;
                double v = (d.BoundingBox.Max.X - d.BoundingBox.Min.X)
                         * (d.BoundingBox.Max.Y - d.BoundingBox.Min.Y)
                         * (d.BoundingBox.Max.Z - d.BoundingBox.Min.Z);
                if (v > biggestVol) { biggestVol = v; biggest = d.BoundingBox; }
            }
            if (biggest == null) return;
            NudXMin.Value = (decimal)biggest.Min.X;
            NudYMin.Value = (decimal)biggest.Min.Y;
            NudZMin.Value = (decimal)Math.Max(0, biggest.Min.Z);
            NudXMax.Value = (decimal)biggest.Max.X;
            NudYMax.Value = (decimal)biggest.Max.Y;
            NudZMax.Value = (decimal)biggest.Max.Z;
        }

        // ── Run ──────────────────────────────────────────────────────────────
        private async void BtnOptimize_Click(object? sender, RoutedEventArgs e)
        {
            var sims = new List<Simulation>();
            foreach (var row in _simRows)
                if (row.IsChecked) sims.Add(row.Simulation);

            if (sims.Count == 0)
            {
                LblStatus.Text = "Select at least one Completed simulation.";
                return;
            }

            // The protected region uses the portable Point3D (engine
            // BoundingBox is engine-typed, no WPF leak).
            var input = new DetectorOptimizer.Input
            {
                Simulations = sims,
                Scene = _scene,
                ProtectedRegion = new BoundingBox(
                    new Point3D((double)(NudXMin.Value ?? 0m),
                                (double)(NudYMin.Value ?? 0m),
                                (double)(NudZMin.Value ?? 0m)),
                    new Point3D((double)(NudXMax.Value ?? 0m),
                                (double)(NudYMax.Value ?? 0m),
                                (double)(NudZMax.Value ?? 0m))),
                ConcentrationThresholdKgM3 = (double)(NudThreshold.Value ?? 0m),
                MeshSizeMOverride = (double)(NudMeshOverride.Value ?? 0m),
                DominanceRadiusCells = (int)(NudRadius.Value ?? 1m),
                Neighborhood = CmbNeighborhood.SelectedIndex == 1
                    ? DetectorOptimizer.NeighborhoodKind.Moore
                    : DetectorOptimizer.NeighborhoodKind.Cardinal,
                UseExactSolver = ChkExactSolver.IsChecked == true
            };

            LblStatus.Text = "Running optimisation...";
            BtnOptimize.IsEnabled = false;
            try
            {
                // Run off the UI thread so the progress callbacks have a
                // chance to be dispatched. Each progress message just
                // overwrites the status label — there's no log pane here.
                var r = await Task.Run(() => DetectorOptimizer.Run(input,
                    msg => Dispatcher.UIThread.Post(() => LblStatus.Text = msg)));

                ResultDetectorPositions = r.DetectorPositions;
                FillGrid(r);

                LblStatus.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} detectors • L = {1:F2} m • {2}/{3} cells covered{4}",
                    r.DetectorPositions.Count, r.MeshSizeM,
                    r.RequiredCoverageCells, r.TotalCells,
                    string.IsNullOrEmpty(r.Notes) ? "" : " — " + r.Notes);
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Error: " + ex.Message;
            }
            finally
            {
                BtnOptimize.IsEnabled = true;
            }
        }

        private void FillGrid(DetectorOptimizer.OptimizationResult r)
        {
            _resultRows.Clear();
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < r.DetectorPositions.Count; i++)
            {
                var p = r.DetectorPositions[i];
                _resultRows.Add(new DetectorRow
                {
                    Index = (i + 1).ToString(inv),
                    X = p.X.ToString("F2", inv),
                    Y = p.Y.ToString("F2", inv),
                    Z = p.Z.ToString("F2", inv)
                });
            }
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnAdd_Click(object? sender, RoutedEventArgs e)
        {
            // Result is the positions list already populated by the last
            // Optimize click. The caller decides whether to convert them
            // into GasDetector3D entries.
            Close(true);
        }

        /// <summary>One row in the simulations chooser. Wraps the engine
        /// Simulation so we can carry the IsChecked toggle without mutating
        /// the saved project.</summary>
        private sealed class SimRow
        {
            public Simulation Simulation { get; set; } = new Simulation();
            public string DisplayName { get; set; } = "";
            public bool IsChecked { get; set; }
        }

        /// <summary>Row backing for the optimal-detectors grid. Strings rather
        /// than doubles so the engine's invariant-culture formatting carries
        /// through unaltered.</summary>
        public sealed class DetectorRow
        {
            public string Index { get; set; } = "";
            public string X { get; set; } = "";
            public string Y { get; set; } = "";
            public string Z { get; set; } = "";
        }
    }
}
