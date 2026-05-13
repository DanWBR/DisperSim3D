#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>DetectorAllocationDialog</c>.
    /// Creates / edits a <see cref="DetectorAllocation"/>: pick a target study,
    /// configure strategy/objective/radius/candidate grid, optionally fold
    /// existing detectors into the seed, and run the greedy or risk-reduction
    /// allocator. The risk-weight grid is wired to
    /// <see cref="RiskWeightHelper"/> for auto-derived freq/consequence.
    /// On OK the dialog returns the configured (and optionally evaluated)
    /// allocation via <see cref="Result"/>.
    /// </summary>
    public partial class DetectorAllocationDialog : Window
    {
        private readonly Scene3D _scene;
        private readonly DetectorAllocation? _editing;
        private readonly ObservableCollection<PositionRow> _positions = new();
        private readonly ObservableCollection<PerCloudRow> _perCloud = new();
        private readonly ObservableCollection<RiskRow> _riskRows = new();

        public DetectorAllocation? Result { get; private set; }

        public DetectorAllocationDialog() : this(new Scene3D()) { }

        public DetectorAllocationDialog(Scene3D scene, DetectorAllocation? editing = null)
        {
            _scene = scene;
            _editing = editing;
            InitializeComponent();

            Title = editing == null ? "New Detector Allocation" : "Edit Detector Allocation";

            // ── Identity: study picker ───────────────────────────────────────
            foreach (var st in _scene.DispersionStudies)
                CmbStudy.Items.Add(new ComboItem
                {
                    Id = st.Id,
                    Display = (st.Name ?? "(study)") + "  (" + st.SimulationIds.Count + " sims)"
                });
            if (CmbStudy.Items.Count > 0) CmbStudy.SelectedIndex = 0;

            // ── Allocated-positions grid ─────────────────────────────────────
            GridPositions.ItemsSource = _positions;
            GridPositions.Columns.Add(new DataGridTextColumn
            {
                Header = "#", Width = new DataGridLength(40),
                Binding = new Binding(nameof(PositionRow.Index))
            });
            GridPositions.Columns.Add(new DataGridTextColumn
            {
                Header = "X (m)", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(PositionRow.X))
            });
            GridPositions.Columns.Add(new DataGridTextColumn
            {
                Header = "Y (m)", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(PositionRow.Y))
            });
            GridPositions.Columns.Add(new DataGridTextColumn
            {
                Header = "Z (m)", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(PositionRow.Z))
            });

            // ── Per-cloud coverage grid ──────────────────────────────────────
            GridPerCloud.ItemsSource = _perCloud;
            GridPerCloud.Columns.Add(new DataGridTextColumn
            {
                Header = "Simulation", Width = new DataGridLength(2, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(PerCloudRow.Simulation))
            });
            GridPerCloud.Columns.Add(new DataGridTextColumn
            {
                Header = "Covered", Width = new DataGridLength(80),
                Binding = new Binding(nameof(PerCloudRow.Covered))
            });
            GridPerCloud.Columns.Add(new DataGridTextColumn
            {
                Header = "Residual R_s", Width = new DataGridLength(120),
                Binding = new Binding(nameof(PerCloudRow.Residual))
            });

            // ── Risk-weights grid (only used when Strategy=MinResidualRisk) ──
            GridRisk.ItemsSource = _riskRows;
            GridRisk.Columns.Add(new DataGridTextColumn
            {
                Header = "Simulation", Width = new DataGridLength(2, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                Binding = new Binding(nameof(RiskRow.Simulation))
            });
            GridRisk.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Freq Auto", Width = new DataGridLength(80),
                Binding = new Binding(nameof(RiskRow.FreqAuto)) { Mode = BindingMode.TwoWay }
            });
            GridRisk.Columns.Add(new DataGridTextColumn
            {
                Header = "Freq/yr", Width = new DataGridLength(120),
                Binding = new Binding(nameof(RiskRow.Freq)) { Mode = BindingMode.TwoWay }
            });
            GridRisk.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Cons Auto", Width = new DataGridLength(80),
                Binding = new Binding(nameof(RiskRow.ConsAuto)) { Mode = BindingMode.TwoWay }
            });
            GridRisk.Columns.Add(new DataGridTextColumn
            {
                Header = "Consequence", Width = new DataGridLength(120),
                Binding = new Binding(nameof(RiskRow.Consequence)) { Mode = BindingMode.TwoWay }
            });
            GridRisk.Columns.Add(new DataGridTextColumn
            {
                Header = "Risk R_s", Width = new DataGridLength(120),
                IsReadOnly = true,
                Binding = new Binding(nameof(RiskRow.Risk))
            });
            // Recompute after any cell-edit commits.
            GridRisk.CellEditEnded += (_, _) => RefreshRiskGridFromUI();

            PopulateFromEditing();
            UpdateStrategyDependentUI();
        }

        // ── Strategy toggle ──────────────────────────────────────────────────
        private void Strategy_Changed(object? sender, RoutedEventArgs e)
            => UpdateStrategyDependentUI();

        private void UpdateStrategyDependentUI()
        {
            bool risk = RadioStrategyRisk?.IsChecked == true;
            if (GrpRisk != null) GrpRisk.IsVisible = risk;
            if (LblTargetPct != null)
                LblTargetPct.Text = risk ? "% Risk Reduction" : "% of clouds covered";
            if (risk) PopulateRiskGrid();
        }

        private void CmbStudy_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (RadioStrategyRisk?.IsChecked == true) PopulateRiskGrid();
        }

        // ── Risk-weights grid ────────────────────────────────────────────────
        private void PopulateRiskGrid()
        {
            _riskRows.Clear();
            var study = ResolveSelectedStudy();
            if (study == null) return;
            var inv = CultureInfo.InvariantCulture;
            foreach (var simId in study.SimulationIds)
            {
                var sim = _scene.Simulations.FirstOrDefault(s => s.Id == simId);
                if (sim == null) continue;
                var risk = study.EnsureRiskFor(simId);
                _riskRows.Add(new RiskRow
                {
                    SimulationId = simId,
                    Simulation   = sim.Name ?? simId,
                    FreqAuto     = risk.FreqMode == RiskValueMode.Auto,
                    ConsAuto     = risk.ConsMode == RiskValueMode.Auto,
                    Freq         = risk.FreqPerYear.ToString("E3", inv),
                    Consequence  = risk.Consequence.ToString("E3", inv),
                    Risk         = "—"
                });
            }
            RefreshRiskGridFromUI();
        }

        private void RefreshRiskGridFromUI()
        {
            var study = ResolveSelectedStudy();
            if (study == null) return;
            double pod = (double)(NudPod.Value ?? 1m);
            if (!(pod > 0)) pod = 1.0;

            // Loading clouds is expensive — do it once per refresh.
            List<CloudSnapshot> clouds;
            try { clouds = DispersionStudyEngine.LoadClouds(study, _scene); }
            catch { clouds = new List<CloudSnapshot>(); }

            var inv = CultureInfo.InvariantCulture;
            foreach (var row in _riskRows)
            {
                var sim = _scene.Simulations.FirstOrDefault(s => s.Id == row.SimulationId);
                if (sim == null) continue;
                var risk = study.EnsureRiskFor(row.SimulationId);

                risk.FreqMode = row.FreqAuto ? RiskValueMode.Auto : RiskValueMode.Manual;
                risk.ConsMode = row.ConsAuto ? RiskValueMode.Auto : RiskValueMode.Manual;
                double manFreq = ParseDouble(row.Freq, risk.FreqPerYear);
                double manCons = ParseDouble(row.Consequence, risk.Consequence);
                if (!row.FreqAuto) risk.FreqPerYear = manFreq;
                if (!row.ConsAuto) risk.Consequence = manCons;

                var snap = clouds?.FirstOrDefault(c => c.SimulationId == row.SimulationId);
                var (freq, cons, rs) = RiskWeightHelper.ResolveScenarioRisk(
                    study, snap, sim, _scene, pod);

                if (row.FreqAuto) row.Freq = freq.ToString("E3", inv);
                if (row.ConsAuto) row.Consequence = cons.ToString("E3", inv);
                row.Risk = rs.ToString("E3", inv);
            }
            // Tell the grid to refresh cell values that were mutated outside
            // the binding pipeline (Risk + Auto-derived Freq/Cons).
            GridRisk.InvalidateVisual();
        }

        private DispersionStudy? ResolveSelectedStudy()
        {
            string? id = (CmbStudy?.SelectedItem as ComboItem)?.Id;
            if (string.IsNullOrEmpty(id)) return null;
            return _scene.DispersionStudies.FirstOrDefault(s => s.Id == id);
        }

        private static double ParseDouble(string? s, double fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                return v;
            return fallback;
        }

        // ── Build / restore the allocation from / to the UI ──────────────────
        private void PopulateFromEditing()
        {
            if (_editing == null)
            {
                TxtName.Text = "Allocation " + (_scene.DetectorAllocations.Count + 1);
                Result = new DetectorAllocation();
                return;
            }
            TxtName.Text = _editing.Name ?? "";
            for (int i = 0; i < CmbStudy.Items.Count; i++)
                if (CmbStudy.Items[i] is ComboItem ci && ci.Id == _editing.DispersionStudyId)
                {
                    CmbStudy.SelectedIndex = i;
                    break;
                }
            RadioStrategyCov.IsChecked  = _editing.Strategy == AllocationStrategy.GreedyMaxCoverage;
            RadioStrategyRisk.IsChecked = _editing.Strategy == AllocationStrategy.MinResidualRisk;
            RadioAll.IsChecked          = _editing.Objective == AllocationObjective.CoverAll;
            RadioPercent.IsChecked      = _editing.Objective == AllocationObjective.CoverPercentage;
            NudTargetPct.Value          = ClampDecimal(_editing.TargetCoveragePercent, 1m, 100m);
            NudMaxDet.Value             = Math.Max(0, _editing.MaxDetectors);
            NudRadius.Value             = ClampDecimal(_editing.DetectionRadiusM, 0.1m, 500m);
            NudMinZ.Value               = ClampDecimal(_editing.MinZ, 0m, 1000m);
            NudMaxZ.Value               = ClampDecimal(_editing.MaxZ, 0m, 1000m);
            NudNx.Value                 = Math.Max(2, Math.Min(500, _editing.CandidateNx));
            NudNy.Value                 = Math.Max(2, Math.Min(500, _editing.CandidateNy));
            NudNz.Value                 = Math.Max(1, Math.Min(50,  _editing.CandidateNz));
            ChkUseExisting.IsChecked    = _editing.UseExistingDetectors;
            NudPod.Value                = ClampDecimal(_editing.DetectionProbability, 0m, 1m);
            ChkDistanceWeight.IsChecked = _editing.UseDistanceWeighting;
            NudWmin.Value               = ClampDecimal(_editing.DistanceWeightMin, 0m, 1m);
            NudWmax.Value               = ClampDecimal(_editing.DistanceWeightMax, 0m, 1m);
            PopulateResultsList(_editing);
        }

        private static decimal ClampDecimal(double v, decimal min, decimal max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return min;
            decimal d;
            try { d = (decimal)v; } catch { return min; }
            return d < min ? min : (d > max ? max : d);
        }

        private void PopulateResultsList(DetectorAllocation a)
        {
            var inv = CultureInfo.InvariantCulture;
            _positions.Clear();
            int n = 1;
            foreach (var p in a.AllocatedPositions ?? new List<DisperSim3D.Geometry.Point3D>())
                _positions.Add(new PositionRow
                {
                    Index = (n++).ToString(inv),
                    X = p.X.ToString("F2", inv),
                    Y = p.Y.ToString("F2", inv),
                    Z = p.Z.ToString("F2", inv)
                });

            _perCloud.Clear();
            var residuals = a.PerCloudResidualRisk ?? new Dictionary<string, double>();
            foreach (var kv in a.PerCloudCovered ?? new Dictionary<string, bool>())
            {
                var sim = _scene.Simulations.FirstOrDefault(s => s.Id == kv.Key);
                residuals.TryGetValue(kv.Key, out double r);
                _perCloud.Add(new PerCloudRow
                {
                    Simulation = sim?.Name ?? kv.Key,
                    Covered    = kv.Value ? "Yes" : "No",
                    Residual   = a.Strategy == AllocationStrategy.MinResidualRisk
                                   ? r.ToString("E3", inv) : ""
                });
            }

            if (a.Status == AllocationStatus.Completed)
                LblCoverage.Text = a.AchievedCoveragePercent.ToString("F1", inv) + "% coverage";

            if (a.Strategy == AllocationStrategy.MinResidualRisk
                && a.Status == AllocationStatus.Completed)
            {
                LblTotalRisk.Text    = "Total risk: " + a.TotalRisk.ToString("E3", inv);
                LblResidualRisk.Text = "Residual: "   + a.ResidualRisk.ToString("E3", inv);
                LblRrf.Text          = "RRF: " + (a.RiskReductionFraction * 100.0).ToString("F1", inv) + " %";
            }
            else
            {
                LblTotalRisk.Text = "Total risk: —";
                LblResidualRisk.Text = "Residual: —";
                LblRrf.Text = "RRF: —";
            }
        }

        private DetectorAllocation BuildAllocationFromUI()
        {
            var a = _editing ?? new DetectorAllocation();
            a.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Allocation" : TxtName.Text.Trim();
            a.DispersionStudyId = (CmbStudy.SelectedItem as ComboItem)?.Id ?? "";
            a.Strategy = RadioStrategyRisk.IsChecked == true
                ? AllocationStrategy.MinResidualRisk
                : AllocationStrategy.GreedyMaxCoverage;
            a.Objective = RadioPercent.IsChecked == true
                ? AllocationObjective.CoverPercentage
                : AllocationObjective.CoverAll;
            a.TargetCoveragePercent  = (double)(NudTargetPct.Value ?? 95m);
            a.MaxDetectors           = (int)(NudMaxDet.Value ?? 0m);
            a.DetectionRadiusM       = (double)(NudRadius.Value ?? 5m);
            a.MinZ                   = (double)(NudMinZ.Value ?? 1.5m);
            a.MaxZ                   = (double)(NudMaxZ.Value ?? 3m);
            a.CandidateNx            = (int)(NudNx.Value ?? 60m);
            a.CandidateNy            = (int)(NudNy.Value ?? 60m);
            a.CandidateNz            = (int)(NudNz.Value ?? 3m);
            a.UseExistingDetectors   = ChkUseExisting.IsChecked == true;
            a.DetectionProbability   = (double)(NudPod.Value ?? 1m);
            a.UseDistanceWeighting   = ChkDistanceWeight.IsChecked == true;
            a.DistanceWeightMin      = (double)(NudWmin.Value ?? 0.5m);
            a.DistanceWeightMax      = (double)(NudWmax.Value ?? 1m);
            return a;
        }

        // ── Run allocation (async, on the thread pool) ───────────────────────
        private async void BtnRun_Click(object? sender, RoutedEventArgs e)
        {
            if (CmbStudy.SelectedItem is null)
            {
                LblStatus.Text = "Pick a Dispersion Study first.";
                LblStatus.Foreground = Brushes.Firebrick;
                return;
            }
            var a = BuildAllocationFromUI();
            var study = _scene.DispersionStudies.FirstOrDefault(s => s.Id == a.DispersionStudyId);
            if (study is null)
            {
                LblStatus.Text = "Study not found.";
                LblStatus.Foreground = Brushes.Firebrick;
                return;
            }

            LblStatus.Text = "Loading clouds...";
            LblStatus.Foreground = Brushes.Gray;
            BtnRun.IsEnabled = false;
            try
            {
                // LoadClouds + DetectorAllocator.Run are both CPU-heavy —
                // push them off the UI thread.
                var (positions, coverage, perCovered, perResidual,
                     totalRisk, residualRisk, rrf, msg, candCount) =
                    await Task.Run(() =>
                    {
                        var clouds = DispersionStudyEngine.LoadClouds(study, _scene);
                        var obstacles = new List<BoundingBox>();
                        foreach (var d in _scene.Decorations)
                            if (d.BoundingBox != null) obstacles.Add(d.BoundingBox);

                        double domainHalf = clouds
                            .Where(c => c.IsValid)
                            .Select(c => c.DomainHalfM)
                            .DefaultIfEmpty(200.0).Max();

                        var r = DetectorAllocator.Run(a, study, _scene,
                            clouds, obstacles, _scene.GasDetectors, domainHalf);
                        return (r.Positions, r.CoveragePercent, r.PerCloudCovered,
                            r.PerCloudResidualRisk, r.TotalRisk, r.ResidualRisk,
                            r.RiskReductionFraction, r.Message, r.CandidateCount);
                    });

                a.AllocatedPositions      = positions;
                a.AchievedCoveragePercent = coverage;
                a.PerCloudCovered         = perCovered;
                a.PerCloudResidualRisk    = perResidual;
                a.TotalRisk               = totalRisk;
                a.ResidualRisk            = residualRisk;
                a.RiskReductionFraction   = rrf;
                a.Status                  = AllocationStatus.Completed;
                a.StatusMessage           = msg;
                a.RunAt                   = DateTime.Now;

                PopulateResultsList(a);
                LblStatus.Text = msg + "  " + candCount + " candidates evaluated.";
                LblStatus.Foreground = Brushes.DarkGreen;
                Result = a;
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Run failed: " + ex.Message;
                LblStatus.Foreground = Brushes.Firebrick;
                a.Status = AllocationStatus.Failed;
                a.StatusMessage = ex.Message;
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            // If the user never ran the allocator we still save the
            // configured allocation (no results) — same as WinForms.
            Result = BuildAllocationFromUI();
            Close(true);
        }

        // ── Row backing types ────────────────────────────────────────────────
        public sealed class PositionRow
        {
            public string Index { get; set; } = "";
            public string X { get; set; } = "";
            public string Y { get; set; } = "";
            public string Z { get; set; } = "";
        }

        public sealed class PerCloudRow
        {
            public string Simulation { get; set; } = "";
            public string Covered { get; set; } = "";
            public string Residual { get; set; } = "";
        }

        public sealed class RiskRow
        {
            public string SimulationId { get; set; } = "";
            public string Simulation { get; set; } = "";
            public bool FreqAuto { get; set; }
            public bool ConsAuto { get; set; }
            public string Freq { get; set; } = "";
            public string Consequence { get; set; } = "";
            public string Risk { get; set; } = "";
        }

        private sealed class ComboItem
        {
            public string Id { get; set; } = "";
            public string Display { get; set; } = "";
            public override string ToString() => Display;
        }
    }
}
