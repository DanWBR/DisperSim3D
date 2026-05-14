#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using DisperSim3D.UI.Avalonia.ViewModels;
using PortableColor = DisperSim3D.Geometry.Color;
using PortablePoint3D = DisperSim3D.Geometry.Point3D;
using PortableVector3D = DisperSim3D.Geometry.Vector3D;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia property-grid replacement for HandyControl's
    /// <c>PropertyGrid</c> + the WPF property adapters in the WinForms UI.
    /// Reflection-based: feed any CLR object via <see cref="SetTarget"/> and
    /// it builds one row per public instance property, grouped by
    /// <c>[Category]</c>, with the editor control picked from
    /// <see cref="PropertyRow.For"/>'s decision tree.
    ///
    /// Iteration 3 of the WPF→Avalonia port. Scope:
    ///   - Editors: TextBox, NumericUpDown, CheckBox, ComboBox, Point3D/Vector3D
    ///     triples.
    ///   - Read-only fallback for collections / complex types (shows
    ///     "[N items]" or <c>ToString()</c>).
    ///   - Two-way binding: every edit fires <see cref="ValueChanged"/> so the
    ///     host window can mark the project dirty.
    /// Not yet supported:
    ///   - Color picker (TextBox stand-in for now)
    ///   - Custom UI-side editors (Point3D-on-ground-plane snap, etc.)
    ///   - Validation attributes
    ///   - <c>[Editor(typeof(...))]</c> overrides from the engine models
    /// </summary>
    public partial class PropertyEditor : UserControl
    {
        public event EventHandler? ValueChanged;

        private object? _target;

        public PropertyEditor()
        {
            InitializeComponent();
        }

        /// <summary>Replaces the inspected object. Pass <c>null</c> to clear.</summary>
        public void SetTarget(object? target, string? hint = null)
        {
            _target = target;
            Rebuild(hint);
        }

        private void Rebuild(string? hint)
        {
            Rows.Items.Clear();
            // Clear the description footer between selections so a stale
            // "EffectiveLeakFrequency: ..." doesn't linger after the user
            // clicks a different tree node.
            DescPropName.Text = "(no property selected)";
            DescPropDesc.Text = "Hover or click a row to see its description.";

            if (_target is null)
            {
                TypeText.Text = "(no selection)";
                HintText.Text = hint ?? "Select a tree node to inspect.";
                return;
            }

            TypeText.Text = _target.GetType().Name;
            HintText.Text = hint ?? "";

            var props = _target.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => PropertyRow.For(_target, p))
                .Where(r => r != null)
                .Cast<PropertyRow>()
                .ToList();

            // Group by category, alphabetical category order with "General" first.
            var grouped = props
                .GroupBy(r => string.IsNullOrEmpty(r.Category) ? "General" : r.Category)
                .OrderBy(g => g.Key == "General" ? "" : g.Key);

            foreach (var group in grouped)
            {
                Rows.Items.Add(new Border
                {
                    Classes = { "categoryHeader" },
                    Child = new TextBlock
                    {
                        Text = group.Key,
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 11
                    }
                });
                foreach (var row in group.OrderBy(r => r.Name))
                {
                    Rows.Items.Add(BuildRow(row));
                }
            }
        }

        // ── Row factory ─────────────────────────────────────────────────────
        private Control BuildRow(PropertyRow row)
        {
            var grid = new Grid
            {
                // Wider label column — Avalonia's default font is taller than
                // WPF's, and engine model property names tend to be verbose
                // ("EffectiveLeakFrequency", "ExpandedDiameterForCfd"). 180px
                // fits most without truncating; longer names get ellipsis
                // and reveal the full name via tooltip.
                ColumnDefinitions = new ColumnDefinitions("180,*"),
                Margin = new Thickness(6, 2)
            };

            // Hovering or clicking anywhere on the row updates the bottom
            // description pane — even on read-only rows that can't take
            // keyboard focus.
            grid.PointerEntered += (_, _) => ShowDescription(row);
            grid.PointerPressed += (_, _) => ShowDescription(row);

            var label = new TextBlock
            {
                Text = row.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#444")),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            // Show the full property name on hover (and a description if the
            // engine model carries a [Description] attribute).
            string tip = string.IsNullOrEmpty(row.Description)
                ? row.Name
                : row.Name + "\n\n" + row.Description;
            ToolTip.SetTip(label, tip);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            Control editor = BuildEditor(row);
            editor.IsEnabled = !row.IsReadOnly;
            // Keyboard focus into the editor also pins the description so
            // tab-walking through the inspector updates the footer.
            editor.GotFocus += (_, _) => ShowDescription(row);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);

            return grid;
        }

        /// <summary>Updates the bottom description pane to reflect the row
        /// the user is currently interacting with. Falls back to the
        /// property name when the engine model has no <c>[Description]</c>
        /// attribute on that property.</summary>
        private void ShowDescription(PropertyRow row)
        {
            DescPropName.Text = row.Name;
            DescPropDesc.Text = string.IsNullOrEmpty(row.Description)
                ? "(No description attribute on this property.)"
                : row.Description;
        }

        private Control BuildEditor(PropertyRow row)
        {
            switch (row.Kind)
            {
                case PropertyEditorKind.Text: return TextEditor(row);
                case PropertyEditorKind.Boolean: return BoolEditor(row);
                case PropertyEditorKind.Integer: return NumericEditor(row, isInt: true);
                case PropertyEditorKind.Number: return NumericEditor(row, isInt: false);
                case PropertyEditorKind.EnumChoice: return EnumEditor(row);
                case PropertyEditorKind.Point3D: return Vec3Editor(row, isPoint: true);
                case PropertyEditorKind.Vector3D: return Vec3Editor(row, isPoint: false);
                case PropertyEditorKind.Color: return ColorEditor(row);
                case PropertyEditorKind.FilePath: return FilePathEditor(row);
                default: return ReadOnlyEditor(row);
            }
        }

        // ── Editor implementations ──────────────────────────────────────────
        private Control TextEditor(PropertyRow row)
        {
            var tb = new TextBox
            {
                Text = row.Getter()?.ToString() ?? "",
                MinHeight = 24
            };
            tb.LostFocus += (_, _) =>
            {
                TryAssign(row, tb.Text ?? "");
            };
            return tb;
        }

        private Control BoolEditor(PropertyRow row)
        {
            var cb = new CheckBox
            {
                IsChecked = row.Getter() is bool b && b,
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                TryAssign(row, cb.IsChecked == true);
            };
            return cb;
        }

        private Control NumericEditor(PropertyRow row, bool isInt)
        {
            var nud = new NumericUpDown
            {
                MinHeight = 24,
                ShowButtonSpinner = true,
                FormatString = isInt ? "0" : "0.######",
                Increment = isInt ? 1m : 0.1m
            };
            object? raw = row.Getter();
            if (raw != null)
            {
                try { nud.Value = Convert.ToDecimal(raw, CultureInfo.InvariantCulture); }
                catch { /* ignore */ }
            }
            nud.ValueChanged += (_, _) =>
            {
                if (nud.Value is decimal v) TryAssign(row, v);
            };
            return nud;
        }

        private Control EnumEditor(PropertyRow row)
        {
            var cbx = new ComboBox
            {
                ItemsSource = row.EnumOptions,
                SelectedItem = row.Getter()?.ToString(),
                MinHeight = 24
            };
            cbx.SelectionChanged += (_, _) =>
            {
                if (cbx.SelectedItem is string s) TryAssign(row, s);
            };
            return cbx;
        }

        private Control Vec3Editor(PropertyRow row, bool isPoint)
        {
            double x = 0, y = 0, z = 0;
            object? raw = row.Getter();
            if (raw is PortablePoint3D p) { x = p.X; y = p.Y; z = p.Z; }
            else if (raw is PortableVector3D v) { x = v.X; y = v.Y; z = v.Z; }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,*,4,*") };
            var nx = MakeAxisNud(x);
            var ny = MakeAxisNud(y);
            var nz = MakeAxisNud(z);
            Grid.SetColumn(nx, 0); grid.Children.Add(nx);
            Grid.SetColumn(ny, 2); grid.Children.Add(ny);
            Grid.SetColumn(nz, 4); grid.Children.Add(nz);

            void OnAnyChange(object? s, NumericUpDownValueChangedEventArgs e)
            {
                double dx = (double)(nx.Value ?? 0);
                double dy = (double)(ny.Value ?? 0);
                double dz = (double)(nz.Value ?? 0);
                if (isPoint) TryAssign(row, new PortablePoint3D(dx, dy, dz));
                else TryAssign(row, new PortableVector3D(dx, dy, dz));
            }
            nx.ValueChanged += OnAnyChange;
            ny.ValueChanged += OnAnyChange;
            nz.ValueChanged += OnAnyChange;
            return grid;
        }

        private static NumericUpDown MakeAxisNud(double value)
            => new NumericUpDown
            {
                Value = (decimal)value,
                FormatString = "0.######",
                Increment = 0.1m,
                ShowButtonSpinner = false,
                MinHeight = 24
            };

        private Control ColorEditor(PropertyRow row)
        {
            PortableColor current = row.Getter() is PortableColor c
                ? c : PortableColor.FromRgb(128, 128, 128);

            var swatch = new Border
            {
                Width = 22, Height = 18,
                CornerRadius = new CornerRadius(2),
                BorderBrush = new SolidColorBrush(Color.Parse("#888")),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(
                    Color.FromArgb(current.A, current.R, current.G, current.B)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new global::Avalonia.Input.Cursor(
                    global::Avalonia.Input.StandardCursorType.Hand)
            };

            var hexBox = new TextBox
            {
                Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}",
                MinHeight = 24,
                MinWidth = 70,
                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                FontSize = 11
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { swatch, hexBox }
            };

            var nr = new NumericUpDown
            {
                Value = current.R, Minimum = 0, Maximum = 255,
                Increment = 1, FormatString = "0", MinHeight = 24,
                ShowButtonSpinner = true
            };
            var ng = new NumericUpDown
            {
                Value = current.G, Minimum = 0, Maximum = 255,
                Increment = 1, FormatString = "0", MinHeight = 24,
                ShowButtonSpinner = true
            };
            var nb = new NumericUpDown
            {
                Value = current.B, Minimum = 0, Maximum = 255,
                Increment = 1, FormatString = "0", MinHeight = 24,
                ShowButtonSpinner = true
            };
            var na = new NumericUpDown
            {
                Value = current.A, Minimum = 0, Maximum = 255,
                Increment = 1, FormatString = "0", MinHeight = 24,
                ShowButtonSpinner = true
            };

            var preview = new Border
            {
                Width = 40, Height = 24,
                CornerRadius = new CornerRadius(3),
                BorderBrush = new SolidColorBrush(Color.Parse("#888")),
                BorderThickness = new Thickness(1),
                Background = swatch.Background,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var flyoutContent = new StackPanel
            {
                Spacing = 4, Width = 180,
                Children =
                {
                    MakeColorChannel("R", nr),
                    MakeColorChannel("G", ng),
                    MakeColorChannel("B", nb),
                    MakeColorChannel("A", na),
                    preview
                }
            };

            var flyout = new Flyout { Content = flyoutContent };

            void ApplyFromRgb()
            {
                byte r = (byte)(nr.Value ?? 0);
                byte g = (byte)(ng.Value ?? 0);
                byte b = (byte)(nb.Value ?? 0);
                byte a = (byte)(na.Value ?? 255);
                var nc = PortableColor.FromArgb(a, r, g, b);
                var avColor = Color.FromArgb(a, r, g, b);
                swatch.Background = new SolidColorBrush(avColor);
                preview.Background = new SolidColorBrush(avColor);
                hexBox.Text = $"#{r:X2}{g:X2}{b:X2}";
                TryAssign(row, nc);
            }

            nr.ValueChanged += (_, _) => ApplyFromRgb();
            ng.ValueChanged += (_, _) => ApplyFromRgb();
            nb.ValueChanged += (_, _) => ApplyFromRgb();
            na.ValueChanged += (_, _) => ApplyFromRgb();

            hexBox.LostFocus += (_, _) =>
            {
                try
                {
                    var parsed = PortableColor.Parse(hexBox.Text ?? "#808080");
                    nr.Value = parsed.R;
                    ng.Value = parsed.G;
                    nb.Value = parsed.B;
                    na.Value = parsed.A;
                    var avColor = Color.FromArgb(parsed.A, parsed.R, parsed.G, parsed.B);
                    swatch.Background = new SolidColorBrush(avColor);
                    preview.Background = new SolidColorBrush(avColor);
                    TryAssign(row, parsed);
                }
                catch { /* ignore bad input */ }
            };

            swatch.PointerPressed += (_, _) => flyout.ShowAt(swatch);

            return panel;
        }

        private static Grid MakeColorChannel(string label, NumericUpDown nud)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
            var lbl = new TextBlock
            {
                Text = label, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.SemiBold, FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#666"))
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(nud, 1);
            g.Children.Add(lbl);
            g.Children.Add(nud);
            return g;
        }

        private Control FilePathEditor(PropertyRow row)
        {
            var presets = row.FilePresets ?? Array.Empty<string>();
            var presetLabels = row.FilePresetLabels ?? Array.Empty<string>();

            var items = new List<string>(presetLabels);
            items.Add("Browse…");

            string currentValue = row.Getter()?.ToString() ?? "";

            var fileLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#555")),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120,
                Text = GetFileDisplayName(currentValue, presets, presetLabels)
            };

            var cbx = new ComboBox
            {
                ItemsSource = items,
                MinHeight = 24,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            int presetIndex = -1;
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i] == currentValue) { presetIndex = i; break; }
            }
            if (presetIndex >= 0)
                cbx.SelectedIndex = presetIndex;

            cbx.SelectionChanged += (_, _) =>
            {
                int idx = cbx.SelectedIndex;
                if (idx < 0) return;

                if (idx < presets.Count)
                {
                    TryAssign(row, presets[idx]);
                    fileLabel.Text = GetFileDisplayName(presets[idx], presets, presetLabels);
                }
                else
                {
                    BrowseForFile(row, fileLabel, cbx, presets, presetLabels);
                }
            };

            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };
            Grid.SetColumn(cbx, 0);
            Grid.SetColumn(fileLabel, 1);
            fileLabel.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(cbx);
            panel.Children.Add(fileLabel);

            return panel;
        }

        private static string GetFileDisplayName(string value,
            IReadOnlyList<string> presets, IReadOnlyList<string> presetLabels)
        {
            if (string.IsNullOrEmpty(value)) return "";
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i] == value)
                    return presetLabels.Count > i ? presetLabels[i] : value;
            }
            try { return System.IO.Path.GetFileName(value); }
            catch { return value; }
        }

        private async void BrowseForFile(PropertyRow row, TextBlock fileLabel,
            ComboBox cbx, IReadOnlyList<string> presets, IReadOnlyList<string> presetLabels)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var patterns = new List<string>();
                string filterName = "All files";
                if (!string.IsNullOrEmpty(row.FileFilter))
                {
                    var parts = row.FileFilter!.Split('|');
                    if (parts.Length >= 2)
                    {
                        filterName = parts[0];
                        foreach (var ext in parts[1].Split(';'))
                            patterns.Add(ext.Trim());
                    }
                }
                if (patterns.Count == 0)
                    patterns.Add("*.*");

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Select file",
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new FilePickerFileType(filterName) { Patterns = patterns }
                        }
                    });

                if (files.Count > 0)
                {
                    string path = files[0].Path.LocalPath;
                    TryAssign(row, path);
                    fileLabel.Text = System.IO.Path.GetFileName(path);
                    cbx.SelectedIndex = -1;
                }
                else
                {
                    string current = row.Getter()?.ToString() ?? "";
                    int idx = -1;
                    for (int i = 0; i < presets.Count; i++)
                    {
                        if (presets[i] == current) { idx = i; break; }
                    }
                    cbx.SelectedIndex = idx;
                }
            }
            catch { /* dialog cancelled or failed */ }
        }

        private Control ReadOnlyEditor(PropertyRow row)
        {
            object? v = row.Getter();
            string display = v switch
            {
                null => "(null)",
                string s => s,
                System.Collections.IEnumerable seq when v is not string =>
                    "[" + CountEnumerable(seq) + " items]",
                _ => v.ToString() ?? "(null)"
            };
            return new TextBox
            {
                Text = display,
                IsReadOnly = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.Parse("#666"))
            };
        }

        private static int CountEnumerable(System.Collections.IEnumerable seq)
        {
            int n = 0; foreach (var _ in seq) n++; return n;
        }

        private void TryAssign(PropertyRow row, object? value)
        {
            try
            {
                row.Setter(value);
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Surface failures via the type banner so the user sees them.
                HintText.Text = "Set failed (" + row.Name + "): " + ex.Message;
            }
        }
    }
}
