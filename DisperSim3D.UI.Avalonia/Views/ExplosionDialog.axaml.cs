#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Edits an <see cref="ExplosionEvent"/>: the dispersion run whose cloud or gas burns,
    /// the dispersed cloud or an equivalent box, the ignition point, the cell size and the
    /// congestion regions. The simulation list holds the completed dispersion runs only:
    /// an explosion needs the cloud, or at least the gas, of a finished run.
    /// </summary>
    public partial class ExplosionDialog : Window
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly List<Simulation> _simulations = new();
        private readonly List<CongestionRegion> _regions = new();
        private bool _loading;

        public ExplosionEvent Result { get; private set; } = new ExplosionEvent();

        public ExplosionDialog() : this(null, null) { }

        public ExplosionDialog(Scene3D? scene, ExplosionEvent? existing)
        {
            InitializeComponent();

            if (scene?.Simulations != null)
            {
                foreach (var sim in scene.Simulations)
                {
                    if (sim == null || sim.Status != SimulationStatus.Completed || sim.IsFireRun || sim.IsExplosionRun) continue;
                    _simulations.Add(sim);
                    CmbSimulation.Items.Add(new ComboBoxItem { Content = sim.Name });
                }
            }
            if (_simulations.Count == 0)
                CmbSimulation.Items.Add(new ComboBoxItem { Content = "(no completed dispersion simulations)" });
            CmbSimulation.SelectedIndex = 0;
            CmbCloud.SelectedIndex = 0;

            if (existing != null)
            {
                TxtName.Text = existing.Name ?? "Explosion";
                CmbCloud.SelectedIndex = existing.Cloud == ExplosionCloud.EquivalentBox ? 1 : 0;
                Set(NudX, existing.Position.X); Set(NudY, existing.Position.Y); Set(NudZ, existing.Position.Z);
                Set(NudTime, existing.TimeS);
                Set(NudBoxMinX, existing.BoxMin.X); Set(NudBoxMinY, existing.BoxMin.Y); Set(NudBoxMinZ, existing.BoxMin.Z);
                Set(NudBoxMaxX, existing.BoxMax.X); Set(NudBoxMaxY, existing.BoxMax.Y); Set(NudBoxMaxZ, existing.BoxMax.Z);
                Set(NudCell, existing.CellSizeM);
                foreach (var r in existing.Congestion ?? new List<CongestionRegion>())
                    _regions.Add(Copy(r));
                int index = _simulations.FindIndex(s => s.Id == existing.SimulationId);
                if (index >= 0) CmbSimulation.SelectedIndex = index;
            }
            RefreshRegionList(_regions.Count > 0 ? 0 : -1);
            UpdateBoxEnabled();
        }

        private static void Set(NumericUpDown n, double v) => n.Value = (decimal)v;
        private static double Get(NumericUpDown n, double fallback = 0) => n.Value is decimal d ? (double)d : fallback;

        private static CongestionRegion Copy(CongestionRegion r) => new CongestionRegion
        {
            Name = r.Name, Min = r.Min, Max = r.Max, PipeDiameterM = r.PipeDiameterM, AreaPerVolume = r.AreaPerVolume,
        };

        private static string Label(CongestionRegion r)
            => string.Format(Inv, "{0} ({1:G3} m, {2:G3}/m)", string.IsNullOrWhiteSpace(r.Name) ? "Congestion" : r.Name, r.PipeDiameterM, r.AreaPerVolume);

        private void UpdateBoxEnabled()
        {
            bool box = CmbCloud.SelectedIndex == 1;
            foreach (var n in new[] { NudBoxMinX, NudBoxMinY, NudBoxMinZ, NudBoxMaxX, NudBoxMaxY, NudBoxMaxZ }) n.IsEnabled = box;
            NudTime.IsEnabled = !box;
        }

        private void CmbCloud_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (NudBoxMinX != null) UpdateBoxEnabled();
        }

        private void RefreshRegionList(int select)
        {
            _loading = true;
            LstRegions.Items.Clear();
            foreach (var r in _regions) LstRegions.Items.Add(new ListBoxItem { Content = Label(r) });
            LstRegions.SelectedIndex = select;
            _loading = false;
            LoadRegion();
        }

        private CongestionRegion? Selected
            => LstRegions.SelectedIndex >= 0 && LstRegions.SelectedIndex < _regions.Count ? _regions[LstRegions.SelectedIndex] : null;

        private void LoadRegion()
        {
            var r = Selected;
            RegionEditor.IsEnabled = r != null;
            BtnRemoveRegion.IsEnabled = r != null;
            if (r == null) return;
            _loading = true;
            TxtRegionName.Text = r.Name;
            Set(NudRMinX, r.Min.X); Set(NudRMinY, r.Min.Y); Set(NudRMinZ, r.Min.Z);
            Set(NudRMaxX, r.Max.X); Set(NudRMaxY, r.Max.Y); Set(NudRMaxZ, r.Max.Z);
            Set(NudRDiameter, r.PipeDiameterM); Set(NudRArea, r.AreaPerVolume);
            _loading = false;
        }

        private void StoreRegion()
        {
            var r = Selected;
            if (_loading || r == null) return;
            r.Name = string.IsNullOrWhiteSpace(TxtRegionName.Text) ? "Congestion" : TxtRegionName.Text.Trim();
            r.Min = new Point3D(Get(NudRMinX), Get(NudRMinY), Get(NudRMinZ));
            r.Max = new Point3D(Get(NudRMaxX), Get(NudRMaxY), Get(NudRMaxZ));
            r.PipeDiameterM = Get(NudRDiameter, 0.15);
            r.AreaPerVolume = Get(NudRArea, 1.0);
            if (LstRegions.SelectedItem is ListBoxItem item) item.Content = Label(r);
        }

        private void LstRegions_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_loading) LoadRegion();
        }

        private void Region_Changed(object? sender, RoutedEventArgs e) => StoreRegion();

        private void RegionNumber_Changed(object? sender, NumericUpDownValueChangedEventArgs e) => StoreRegion();

        private void BtnAddRegion_Click(object? sender, RoutedEventArgs e)
        {
            // A new region fills the equivalent box, or a 10 m cube about the ignition point.
            bool box = CmbCloud.SelectedIndex == 1;
            double x = Get(NudX), y = Get(NudY);
            var r = new CongestionRegion
            {
                Name = "Congestion " + (_regions.Count + 1),
                Min = box ? new Point3D(Get(NudBoxMinX), Get(NudBoxMinY), Get(NudBoxMinZ)) : new Point3D(x - 5, y - 5, 0),
                Max = box ? new Point3D(Get(NudBoxMaxX), Get(NudBoxMaxY), Get(NudBoxMaxZ)) : new Point3D(x + 5, y + 5, 5),
            };
            _regions.Add(r);
            RefreshRegionList(_regions.Count - 1);
        }

        private void BtnRemoveRegion_Click(object? sender, RoutedEventArgs e)
        {
            int i = LstRegions.SelectedIndex;
            if (i < 0 || i >= _regions.Count) return;
            _regions.RemoveAt(i);
            RefreshRegionList(_regions.Count == 0 ? -1 : System.Math.Min(i, _regions.Count - 1));
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            StoreRegion();
            int index = CmbSimulation.SelectedIndex;
            string simulationId = index >= 0 && index < _simulations.Count ? _simulations[index].Id : "";
            Result = new ExplosionEvent
            {
                Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Explosion" : TxtName.Text.Trim(),
                SimulationId = simulationId,
                Cloud = CmbCloud.SelectedIndex == 1 ? ExplosionCloud.EquivalentBox : ExplosionCloud.Dispersed,
                Position = new Point3D(Get(NudX), Get(NudY), Get(NudZ)),
                TimeS = Get(NudTime),
                BoxMin = new Point3D(Get(NudBoxMinX), Get(NudBoxMinY), Get(NudBoxMinZ)),
                BoxMax = new Point3D(Get(NudBoxMaxX), Get(NudBoxMaxY), Get(NudBoxMaxZ)),
                CellSizeM = Get(NudCell),
                Congestion = new List<CongestionRegion>(_regions),
            };
            Close(true);
        }
    }
}
