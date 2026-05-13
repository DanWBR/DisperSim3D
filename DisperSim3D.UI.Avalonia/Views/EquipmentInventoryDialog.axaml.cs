#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>EquipmentInventoryDialog</c>.
    /// Edits a release source's IOGP 434-01 equipment inventory: the band
    /// selector on top, an editable grid with type / diameter / count / note,
    /// and a live "effective leak frequency" computation. The source's
    /// <see cref="ReleaseSource3D.EquipmentInventory"/>,
    /// <see cref="ReleaseSource3D.HoleSizeBand"/>,
    /// <see cref="ReleaseSource3D.AutoComputeLeakFrequency"/> and
    /// <see cref="ReleaseSource3D.LeakFrequencyPerYear"/> are mutated in-place
    /// on OK. On Cancel the original values are restored from the snapshot
    /// taken when the dialog opened.
    /// </summary>
    public partial class EquipmentInventoryDialog : Window
    {
        private readonly ReleaseSource3D _source;

        // Snapshot for Cancel rollback. We deep-copy the inventory list so
        // edits to grid rows can't leak back if the user bails out.
        private readonly List<EquipmentInventoryItem> _originalSnapshot;
        private readonly IogpHoleSizeBand _originalBand;
        private readonly bool _originalAuto;
        private readonly double _originalLeakFreq;

        private readonly ObservableCollection<InventoryRow> _rows = new();

        // Type-label cache so we don't recompute the strings every keystroke.
        private static readonly Dictionary<IogpEquipmentType, string> _typeLabels =
            BuildTypeLabels();
        private static readonly List<string> _typeLabelsList = new(_typeLabels.Values);

        public EquipmentInventoryDialog() : this(new ReleaseSource3D()) { }

        public EquipmentInventoryDialog(ReleaseSource3D source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _source.EquipmentInventory ??= new List<EquipmentInventoryItem>();

            _originalSnapshot = new List<EquipmentInventoryItem>(_source.EquipmentInventory.Count);
            foreach (var it in _source.EquipmentInventory)
                _originalSnapshot.Add(new EquipmentInventoryItem
                {
                    Type              = it.Type,
                    NominalDiameterMm = it.NominalDiameterMm,
                    Count             = it.Count,
                    Note              = it.Note
                });
            _originalBand     = _source.HoleSizeBand;
            _originalAuto     = _source.AutoComputeLeakFrequency;
            _originalLeakFreq = _source.LeakFrequencyPerYear;

            InitializeComponent();

            Title = "Equipment Inventory — " + (_source.Name ?? "Source");

            // Populate the hole-size band combo from the enum using the
            // engine's own DescribeBand for human labels.
            foreach (IogpHoleSizeBand b in Enum.GetValues(typeof(IogpHoleSizeBand)))
                CmbBand.Items.Add(new ComboBoxItem { Content = IogpFrequencyTable.DescribeBand(b) });
            CmbBand.SelectedIndex = (int)_source.HoleSizeBand;

            // Build the data grid columns. The Type column is a combo bound
            // to a per-row TypeLabel string; the others are plain text.
            GridInventory.ItemsSource = _rows;
            var typeCol = new DataGridTemplateColumn
            {
                Header = "Equipment type (IOGP 434-01)",
                Width = new DataGridLength(2.4, DataGridLengthUnitType.Star),
                CellTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<InventoryRow>(
                    (row, _) =>
                    {
                        var cb = new ComboBox
                        {
                            Margin = new global::Avalonia.Thickness(2, 0),
                            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch
                        };
                        foreach (var label in _typeLabelsList) cb.Items.Add(label);
                        cb.SelectedItem = row?.TypeLabel ?? _typeLabelsList[0];
                        cb.SelectionChanged += (_, _) =>
                        {
                            if (row != null && cb.SelectedItem is string s)
                            {
                                row.TypeLabel = s;
                                CommitGridToInventory();
                                RecomputeFrequency();
                            }
                        };
                        return cb;
                    })
            };
            GridInventory.Columns.Add(typeCol);
            GridInventory.Columns.Add(new DataGridTextColumn
            {
                Header = "Diameter (mm)",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(InventoryRow.Diameter)) { Mode = BindingMode.TwoWay }
            });
            GridInventory.Columns.Add(new DataGridTextColumn
            {
                Header = "Count / Length (m)",
                Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(InventoryRow.Count)) { Mode = BindingMode.TwoWay }
            });
            GridInventory.Columns.Add(new DataGridTextColumn
            {
                Header = "Note",
                Width = new DataGridLength(2.2, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(InventoryRow.Note)) { Mode = BindingMode.TwoWay }
            });

            // Re-evaluate the frequency label after any text-cell commit.
            // The combo column above also triggers it via SelectionChanged.
            GridInventory.CellEditEnded += (_, _) =>
            {
                CommitGridToInventory();
                RecomputeFrequency();
            };

            ChkAuto.IsChecked = _source.AutoComputeLeakFrequency;
            NudManualFreq.Value = ClampDecimal(_source.LeakFrequencyPerYear, 0m, 1m);
            NudManualFreq.IsEnabled = !_source.AutoComputeLeakFrequency;

            PopulateGrid();
            RecomputeFrequency();
        }

        private void PopulateGrid()
        {
            _rows.Clear();
            var inv = CultureInfo.InvariantCulture;
            foreach (var it in _source.EquipmentInventory)
                _rows.Add(new InventoryRow
                {
                    TypeLabel = _typeLabels.TryGetValue(it.Type, out var lbl) ? lbl : _typeLabelsList[0],
                    Diameter  = it.NominalDiameterMm.ToString("0.##", inv),
                    Count     = it.Count.ToString("0.###", inv),
                    Note      = it.Note ?? ""
                });
        }

        private void CommitGridToInventory()
        {
            // Resize the inventory list to match the row count. The row order
            // preserves the user's edits exactly as displayed.
            while (_source.EquipmentInventory.Count > _rows.Count)
                _source.EquipmentInventory.RemoveAt(_source.EquipmentInventory.Count - 1);
            while (_source.EquipmentInventory.Count < _rows.Count)
                _source.EquipmentInventory.Add(new EquipmentInventoryItem());

            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                var it = _source.EquipmentInventory[i];
                it.Type              = ParseType(r.TypeLabel);
                it.NominalDiameterMm = ParseDouble(r.Diameter, it.NominalDiameterMm);
                it.Count             = ParseDouble(r.Count, it.Count);
                it.Note              = r.Note ?? "";
            }
        }

        private void RecomputeFrequency()
        {
            _source.HoleSizeBand             = (IogpHoleSizeBand)Math.Max(0, CmbBand.SelectedIndex);
            _source.AutoComputeLeakFrequency = ChkAuto.IsChecked == true;
            _source.LeakFrequencyPerYear     = (double)(NudManualFreq.Value ?? 0m);

            double effective = _source.EffectiveLeakFrequencyPerYear;
            LblComputedFreq.Text = "Effective: " +
                effective.ToString("E3", CultureInfo.InvariantCulture) + " events/yr";
        }

        // ── Event handlers ───────────────────────────────────────────────────
        private void CmbBand_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => RecomputeFrequency();

        private void ChkAuto_Changed(object? sender, RoutedEventArgs e)
        {
            if (NudManualFreq != null)
                NudManualFreq.IsEnabled = ChkAuto.IsChecked != true;
            RecomputeFrequency();
        }

        private void NudManualFreq_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
            => RecomputeFrequency();

        private void BtnAdd_Click(object? sender, RoutedEventArgs e)
        {
            _source.EquipmentInventory.Add(new EquipmentInventoryItem());
            PopulateGrid();
            RecomputeFrequency();
        }

        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            int idx = GridInventory.SelectedIndex;
            if (idx < 0 || idx >= _source.EquipmentInventory.Count) return;
            _source.EquipmentInventory.RemoveAt(idx);
            PopulateGrid();
            RecomputeFrequency();
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            // Restore the original inventory + frequency settings from the
            // snapshot. Any in-place edits during the session are dropped.
            _source.EquipmentInventory.Clear();
            foreach (var it in _originalSnapshot) _source.EquipmentInventory.Add(it);
            _source.HoleSizeBand             = _originalBand;
            _source.AutoComputeLeakFrequency = _originalAuto;
            _source.LeakFrequencyPerYear     = _originalLeakFreq;
            Close(false);
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            CommitGridToInventory();
            RecomputeFrequency();   // also writes the source fields
            Close(true);
        }

        // ── Type label helpers (matches the WPF labels 1:1) ──────────────────
        private static Dictionary<IogpEquipmentType, string> BuildTypeLabels()
        {
            var d = new Dictionary<IogpEquipmentType, string>();
            foreach (IogpEquipmentType t in Enum.GetValues(typeof(IogpEquipmentType)))
                d[t] = DescribeType(t);
            return d;
        }

        private static string DescribeType(IogpEquipmentType t)
        {
            int n = (int)t;
            return t switch
            {
                IogpEquipmentType.SteelProcessPipe        => n + ". Steel process pipe (per metre)",
                IogpEquipmentType.FlangedJoint            => n + ". Flanged joint",
                IogpEquipmentType.ManualValve             => n + ". Manual valve",
                IogpEquipmentType.ActuatedValve           => n + ". Actuated valve",
                IogpEquipmentType.InstrumentConnection    => n + ". Instrument connection",
                IogpEquipmentType.PressureVessel          => n + ". Pressure vessel",
                IogpEquipmentType.PumpCentrifugal         => n + ". Pump, centrifugal",
                IogpEquipmentType.PumpReciprocating       => n + ". Pump, reciprocating",
                IogpEquipmentType.CompressorCentrifugal   => n + ". Compressor, centrifugal",
                IogpEquipmentType.CompressorReciprocating => n + ". Compressor, reciprocating",
                IogpEquipmentType.HxShellTubeShellSide    => n + ". HX shell+tube, HC shell side",
                IogpEquipmentType.HxShellTubeTubeSide     => n + ". HX shell+tube, HC tube side",
                IogpEquipmentType.HxPlate                 => n + ". HX plate",
                IogpEquipmentType.HxAirCooled             => n + ". HX air-cooled",
                IogpEquipmentType.Filter                  => n + ". Filter",
                IogpEquipmentType.PigTrap                 => n + ". Pig trap",
                IogpEquipmentType.FlexiblePipe            => n + ". Flexible pipe (per metre)",
                IogpEquipmentType.PressureVesselOther     => n + ". Pressure vessel (Other)",
                IogpEquipmentType.Degasser                => n + ". Degasser",
                IogpEquipmentType.Expander                => n + ". Expander",
                IogpEquipmentType.XmasTree                => n + ". Xmas tree",
                IogpEquipmentType.Turbine                 => n + ". Turbine",
                IogpEquipmentType.PipelineEsdv            => n + ". Pipeline ESDV",
                IogpEquipmentType.SsivAssembly            => n + ". SSIV assembly",
                _                                          => n + ". " + t
            };
        }

        private static IogpEquipmentType ParseType(string? label)
        {
            if (string.IsNullOrEmpty(label)) return IogpEquipmentType.SteelProcessPipe;
            int dot = label.IndexOf('.');
            if (dot <= 0) return IogpEquipmentType.SteelProcessPipe;
            if (int.TryParse(label.Substring(0, dot), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 24)
                return (IogpEquipmentType)n;
            return IogpEquipmentType.SteelProcessPipe;
        }

        private static double ParseDouble(string? value, double fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            // Accept invariant first, then current culture — keeps EU / US
            // decimal separators interchangeable.
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                return v;
            return fallback;
        }

        private static decimal ClampDecimal(double v, decimal min, decimal max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return min;
            decimal d;
            try { d = (decimal)v; } catch { return min; }
            if (d < min) return min;
            if (d > max) return max;
            return d;
        }

        /// <summary>Row backing for the inventory grid. Keeps the editable
        /// values as strings so the user's typing isn't reformatted by
        /// premature double conversion.</summary>
        public sealed class InventoryRow
        {
            public string TypeLabel { get; set; } = "";
            public string Diameter { get; set; } = "0";
            public string Count { get; set; } = "0";
            public string Note { get; set; } = "";
        }
    }
}
