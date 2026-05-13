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
using DisperSim3D.UI.Avalonia.ViewModels;
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
