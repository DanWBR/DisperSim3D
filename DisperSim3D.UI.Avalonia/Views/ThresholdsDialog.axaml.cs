#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Models;
using DisperSim3D.Geometry;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>ThresholdsDialog</c>.
    /// Edits a list of <see cref="DispersionThreshold"/> rows that drive
    /// the isosurface rendering. Each row carries: name, concentration
    /// in kg/m³, an ARGB hex color string ("64FF0000"), an opacity in
    /// [0..1], and a Visible flag.
    ///
    /// We bind the DataGrid to an <see cref="ObservableCollection{T}"/> of
    /// <see cref="ThresholdRow"/> view-models so add/remove buttons reflect
    /// instantly. On OK we map the rows back to <see cref="DispersionThreshold"/>
    /// (engine type) via <see cref="ThresholdRow.ToModel"/>.
    /// </summary>
    public partial class ThresholdsDialog : Window
    {
        public List<DispersionThreshold>? Result { get; private set; }

        private readonly ObservableCollection<ThresholdRow> _rows = new();

        public ThresholdsDialog() : this(null) { }

        public ThresholdsDialog(List<DispersionThreshold>? existing)
        {
            InitializeComponent();

            // Configure DataGrid columns. AutoGenerateColumns is off so we
            // can pick the right column type per field.
            GridThresholds.ItemsSource = _rows;
            GridThresholds.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(ThresholdRow.Name)) { Mode = BindingMode.TwoWay }
            });
            GridThresholds.Columns.Add(new DataGridTextColumn
            {
                Header = "Concentration (kg/m³)",
                Width = new DataGridLength(150),
                Binding = new Binding(nameof(ThresholdRow.Concentration)) { Mode = BindingMode.TwoWay }
            });
            GridThresholds.Columns.Add(new DataGridTextColumn
            {
                Header = "Color (ARGB hex)",
                Width = new DataGridLength(130),
                Binding = new Binding(nameof(ThresholdRow.ColorHex)) { Mode = BindingMode.TwoWay }
            });
            GridThresholds.Columns.Add(new DataGridTextColumn
            {
                Header = "Opacity",
                Width = new DataGridLength(80),
                Binding = new Binding(nameof(ThresholdRow.Opacity)) { Mode = BindingMode.TwoWay }
            });
            GridThresholds.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Visible",
                Width = new DataGridLength(60),
                Binding = new Binding(nameof(ThresholdRow.Visible)) { Mode = BindingMode.TwoWay }
            });

            // Seed from existing list or apply the WinForms defaults (LFL + IDLH).
            if (existing != null && existing.Count > 0)
            {
                foreach (var t in existing) _rows.Add(ThresholdRow.From(t));
            }
            else
            {
                _rows.Add(new ThresholdRow
                {
                    Name = "LFL", Concentration = 0.033,
                    ColorHex = "64FF0000", Opacity = 0.3, Visible = true
                });
                _rows.Add(new ThresholdRow
                {
                    Name = "IDLH", Concentration = 0.033,
                    ColorHex = "64FFA500", Opacity = 0.25, Visible = true
                });
            }
        }

        private void BtnAdd_Click(object? sender, RoutedEventArgs e)
        {
            _rows.Add(new ThresholdRow
            {
                Name = "Custom", Concentration = 0.01,
                ColorHex = "6400C800", Opacity = 0.2, Visible = true
            });
        }

        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (GridThresholds.SelectedItem is ThresholdRow row)
                _rows.Remove(row);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            Result = new List<DispersionThreshold>();
            foreach (var r in _rows)
                Result.Add(r.ToModel());
            Close(true);
        }
    }

    /// <summary>Editable view-model for a threshold row. Strings for color
    /// hex / numerics-as-text so the DataGrid TextBox columns can edit them
    /// without a custom value converter — we parse on commit.</summary>
    public sealed class ThresholdRow
    {
        public string Name { get; set; } = "Threshold";
        public double Concentration { get; set; } = 0.01;
        public string ColorHex { get; set; } = "64FF0000";
        public double Opacity { get; set; } = 0.3;
        public bool Visible { get; set; } = true;

        public static ThresholdRow From(DispersionThreshold t)
        {
            var c = t.Color;
            string hex = string.Format(CultureInfo.InvariantCulture,
                "{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
            return new ThresholdRow
            {
                Name = t.Name ?? "Threshold",
                Concentration = t.ConcentrationValue,
                ColorHex = hex,
                Opacity = t.Opacity,
                Visible = t.Visible
            };
        }

        public DispersionThreshold ToModel()
        {
            // Parse the hex string into a portable engine Color. Bad input
            // falls back to translucent red so an empty cell doesn't crash
            // the OK click.
            var color = new Color(100, 255, 0, 0);
            if (!string.IsNullOrWhiteSpace(ColorHex) && ColorHex.Length == 8)
            {
                try
                {
                    byte a = Convert.ToByte(ColorHex.Substring(0, 2), 16);
                    byte r = Convert.ToByte(ColorHex.Substring(2, 2), 16);
                    byte g = Convert.ToByte(ColorHex.Substring(4, 2), 16);
                    byte b = Convert.ToByte(ColorHex.Substring(6, 2), 16);
                    color = new Color(a, r, g, b);
                }
                catch { /* fall back to default */ }
            }
            return new DispersionThreshold
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "Threshold" : Name,
                Type = DispersionThresholdType.Custom,
                ConcentrationValue = Concentration,
                Color = color,
                Opacity = Opacity,
                Visible = Visible
            };
        }
    }
}
