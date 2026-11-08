#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DisperSim3D.Extensibility;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// The options a solver extender offers (<see cref="ISolverExtenderOptions"/>), one row each:
    /// a check box, a text box for a number or a list of choices, with the option's description
    /// and default under it. Shows nothing for a solver without options.
    /// </summary>
    public sealed class SolverOptionsPanel : StackPanel
    {
        private readonly List<(SolverOption option, Control control)> _rows = new();

        /// <summary>The options shown.</summary>
        public int Count => _rows.Count;

        public SolverOptionsPanel()
        {
            Spacing = 2;
        }

        /// <summary>Shows the options of <paramref name="solver"/> with the values in <paramref name="values"/>
        /// (the defaults for those it leaves out).</summary>
        public void Show(ISolverExtender? solver, IEnumerable<SolverOptionValue>? values)
        {
            Children.Clear();
            _rows.Clear();
            var given = (values ?? Enumerable.Empty<SolverOptionValue>()).Where(v => v?.Key != null)
                .GroupBy(v => v.Key).ToDictionary(g => g.Key, g => g.Last().Value);
            foreach (var option in SolverOptionValues.Of(solver))
            {
                string value = given.TryGetValue(option.Key, out var v) && option.Normalise(v) is string n ? n : option.DefaultValue;
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), Margin = new global::Avalonia.Thickness(0, 4, 0, 0) };
                var label = new TextBlock
                {
                    Text = option.Label + ":", VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.Parse("#444")), Margin = new global::Avalonia.Thickness(0, 0, 8, 0),
                    TextWrapping = TextWrapping.Wrap,
                };
                grid.Children.Add(label);
                Control control;
                switch (option.Kind)
                {
                    case SolverOptionKind.Boolean:
                        control = new CheckBox { IsChecked = value == "true", Name = "Opt_" + option.Key };
                        break;
                    case SolverOptionKind.Choice:
                        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 160, Name = "Opt_" + option.Key };
                        foreach (var c in option.Choices ?? Array.Empty<string>()) combo.Items.Add(new ComboBoxItem { Content = c });
                        combo.SelectedIndex = Math.Max(0, (option.Choices ?? Array.Empty<string>()).ToList().FindIndex(c => c == value));
                        control = combo;
                        break;
                    default:
                        control = new TextBox { Text = value, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Name = "Opt_" + option.Key };
                        break;
                }
                var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                right.Children.Add(control);
                if (!string.IsNullOrEmpty(option.Unit))
                    right.Children.Add(new TextBlock { Text = option.Unit, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.Parse("#666")) });
                Grid.SetColumn(right, 1);
                grid.Children.Add(right);
                Children.Add(grid);
                string hint = (option.Description ?? "").Trim();
                hint = (hint.Length > 0 ? hint + " " : "") + "Default: " + Shown(option, option.DefaultValue) + ".";
                var hintBlock = new TextBlock
                {
                    Text = hint, Foreground = new SolidColorBrush(Color.Parse("#888")), FontSize = 10,
                    TextWrapping = TextWrapping.Wrap, Margin = new global::Avalonia.Thickness(170, 2, 0, 4),
                };
                Children.Add(hintBlock);
                _rows.Add((option, control));
            }
        }

        private static string Shown(SolverOption o, string v) => o.Kind == SolverOptionKind.Boolean ? (v == "true" ? "on" : "off") : v;

        private static string? Raw(SolverOption option, Control control) => control switch
        {
            CheckBox cb => cb.IsChecked == true ? "true" : "false",
            ComboBox cmb => (cmb.SelectedItem as ComboBoxItem)?.Content?.ToString(),
            TextBox tb => tb.Text,
            _ => null
        };

        /// <summary>The first value that is not one of its option's, as a message; null when all are.</summary>
        public string? Validate()
        {
            foreach (var (option, control) in _rows)
            {
                string? raw = Raw(option, control);
                if (option.Normalise(raw) == null)
                    return option.Label + ": not a valid value (" + (raw ?? "") + ")" + (option.Minimum.HasValue || option.Maximum.HasValue
                        ? "; from " + (option.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "−∞") + " to " + (option.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "∞")
                        : "") + ".";
            }
            return null;
        }

        /// <summary>The values that differ from their option's default; an option at its default is left
        /// out, so the run follows the solver's default.</summary>
        public List<SolverOptionValue> Values()
        {
            var list = new List<SolverOptionValue>();
            foreach (var (option, control) in _rows)
            {
                string? n = option.Normalise(Raw(option, control));
                if (n != null && n != option.Normalise(option.DefaultValue)) list.Add(new SolverOptionValue(option.Key, n));
            }
            return list;
        }
    }
}
