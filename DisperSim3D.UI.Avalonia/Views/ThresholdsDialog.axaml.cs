#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DisperSim3D.Models;
using EngineColor = DisperSim3D.Geometry.Color;
using MediaColor = Avalonia.Media.Color;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>ThresholdsDialog</c>.
    /// Edits a list of <see cref="DispersionThreshold"/> rows that drive
    /// the isosurface rendering. Each row carries: name, concentration
    /// in kg/m³, an ARGB hex color string ("64FF0000"), an opacity in
    /// [0..1], and a Visible flag, plus a live colour swatch column and
    /// a "Pick color..." button that opens an Avalonia ColorView dialog
    /// so the user doesn't have to type hex.
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

        /// <summary>
        /// Open the dialog. When <paramref name="existing"/> is null or empty
        /// we seed with the same 3-layer LFL set the renderer falls back to
        /// (100 % / 60 % / 20 % LFL with red / orange / gold). Pass the
        /// active gas's LFL so the values match the viewport legend.
        /// </summary>
        public ThresholdsDialog(List<DispersionThreshold>? existing, double defaultLfl = 0.033)
        {
            InitializeComponent();

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
            // Live colour preview — chequer-backed Border whose Background is
            // a SolidColorBrush built from the row's current ColorHex string.
            // Bound via a value converter so it updates as the hex column is
            // typed.
            GridThresholds.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Preview",
                Width = new DataGridLength(80),
                CanUserSort = false,
                CellTemplate = new FuncDataTemplate<ThresholdRow>((row, _) =>
                {
                    var swatch = new Border
                    {
                        Margin = new Thickness(4, 2),
                        CornerRadius = new CornerRadius(2),
                        BorderBrush = Brushes.DarkGray,
                        BorderThickness = new Thickness(1),
                        Background = MakeSwatchBrush(row?.ColorHex ?? "64FF0000"),
                        Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                    };
                    ToolTip.SetTip(swatch, "Double-click to pick a colour");
                    // Repaint when the bound row's ColorHex changes.
                    if (row != null)
                    {
                        row.ColorHexChanged += (_, _) =>
                            swatch.Background = MakeSwatchBrush(row.ColorHex);
                    }
                    swatch.DoubleTapped += async (_, _) =>
                    {
                        if (row != null) await PickColorForRowAsync(row);
                    };
                    return swatch;
                })
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

            if (existing != null && existing.Count > 0)
            {
                foreach (var t in existing) _rows.Add(ThresholdRow.From(t));
            }
            else
            {
                foreach (var t in DispersionThreshold.BuildDefaultLflLayers(defaultLfl))
                    _rows.Add(ThresholdRow.From(t));
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

        private void BtnResetDefaults_Click(object? sender, RoutedEventArgs e)
        {
            _rows.Clear();
            foreach (var t in DispersionThreshold.BuildDefaultLflLayers(0.033))
                _rows.Add(ThresholdRow.From(t));
        }

        private async void BtnPickColor_Click(object? sender, RoutedEventArgs e)
        {
            if (GridThresholds.SelectedItem is ThresholdRow row)
                await PickColorForRowAsync(row);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            Result = new List<DispersionThreshold>();
            foreach (var r in _rows)
                Result.Add(r.ToModel());
            Close(true);
        }

        /// <summary>
        /// Opens a small modal dialog with an Avalonia <see cref="ColorView"/>
        /// seeded from the row's current colour. The picker doesn't surface
        /// the alpha channel separately — we preserve the row's existing
        /// alpha and only update R/G/B from the picker, matching what the
        /// Opacity field controls separately.
        /// </summary>
        private async System.Threading.Tasks.Task PickColorForRowAsync(ThresholdRow row)
        {
            (byte a, byte r, byte g, byte b) = ParseArgbHex(row.ColorHex);
            var picker = new global::Avalonia.Controls.ColorView
            {
                Color = MediaColor.FromArgb(a, r, g, b),
                ColorModel = global::Avalonia.Controls.ColorModel.Rgba,
                IsAlphaEnabled = false,
                Margin = new Thickness(8),
            };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0,0,8,0) };
            var cancel = new Button { Content = "Cancel", MinWidth = 80 };
            var btnRow = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(8),
                Children = { ok, cancel }
            };
            var content = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(btnRow, global::Avalonia.Controls.Dock.Bottom);
            content.Children.Add(btnRow);
            content.Children.Add(picker);
            var win = new Window
            {
                Title = "Pick colour — " + (row.Name ?? "Threshold"),
                Width = 380, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = content,
            };
            bool accepted = false;
            ok.Click += (_, _) => { accepted = true; win.Close(); };
            cancel.Click += (_, _) => win.Close();
            await win.ShowDialog(this);
            if (!accepted) return;
            var c = picker.Color;
            row.ColorHex = string.Format(CultureInfo.InvariantCulture,
                "{0:X2}{1:X2}{2:X2}{3:X2}", a, c.R, c.G, c.B);
        }

        private static (byte a, byte r, byte g, byte b) ParseArgbHex(string hex)
        {
            byte a = 100, r = 255, g = 0, b = 0;
            if (!string.IsNullOrWhiteSpace(hex) && hex.Length == 8)
            {
                try
                {
                    a = Convert.ToByte(hex.Substring(0, 2), 16);
                    r = Convert.ToByte(hex.Substring(2, 2), 16);
                    g = Convert.ToByte(hex.Substring(4, 2), 16);
                    b = Convert.ToByte(hex.Substring(6, 2), 16);
                } catch { }
            }
            return (a, r, g, b);
        }

        /// <summary>
        /// Build a tinted swatch brush for the preview column. The colour's
        /// own alpha channel blends with a flat light-grey backdrop so the
        /// user sees transparency at a glance, similar to what the renderer
        /// will composite against the 3-D scene.
        /// </summary>
        private static IBrush MakeSwatchBrush(string hex)
        {
            (byte a, byte r, byte g, byte b) = ParseArgbHex(hex);
            // Composite the ARGB over a flat 240-grey backdrop so partly
            // transparent colours read correctly.
            float af = a / 255f;
            byte rr = (byte)(r * af + 240 * (1 - af));
            byte gg = (byte)(g * af + 240 * (1 - af));
            byte bb = (byte)(b * af + 240 * (1 - af));
            return new SolidColorBrush(MediaColor.FromRgb(rr, gg, bb));
        }
    }

    /// <summary>Editable view-model for a threshold row. Strings for color
    /// hex / numerics-as-text so the DataGrid TextBox columns can edit them
    /// without a custom value converter — we parse on commit. Raises an
    /// event when ColorHex changes so the swatch column can repaint.</summary>
    public sealed class ThresholdRow
    {
        public string Name { get; set; } = "Threshold";
        public double Concentration { get; set; } = 0.01;

        private string _colorHex = "64FF0000";
        public string ColorHex
        {
            get => _colorHex;
            set { if (_colorHex == value) return; _colorHex = value; ColorHexChanged?.Invoke(this, EventArgs.Empty); }
        }
        public event EventHandler? ColorHexChanged;

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
            var color = new EngineColor(100, 255, 0, 0);
            if (!string.IsNullOrWhiteSpace(ColorHex) && ColorHex.Length == 8)
            {
                try
                {
                    byte a = Convert.ToByte(ColorHex.Substring(0, 2), 16);
                    byte r = Convert.ToByte(ColorHex.Substring(2, 2), 16);
                    byte g = Convert.ToByte(ColorHex.Substring(4, 2), 16);
                    byte b = Convert.ToByte(ColorHex.Substring(6, 2), 16);
                    color = new EngineColor(a, r, g, b);
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
