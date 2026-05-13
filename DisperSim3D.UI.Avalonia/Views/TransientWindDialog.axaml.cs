#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>TransientWindDialog</c>.
    /// Edits a <see cref="TransientWindProfile"/>: an Enabled flag, an ESD
    /// (emergency shutdown) timestamp, and a list of <see cref="WindProfileEntry"/>
    /// samples (Time, Speed, Direction, Stability). The DataGrid binds
    /// directly to the engine entries since they expose plain properties the
    /// text/combo columns can edit in-place.
    /// </summary>
    public partial class TransientWindDialog : Window
    {
        public TransientWindProfile Result { get; private set; }

        private readonly ObservableCollection<WindProfileEntry> _rows = new();

        public TransientWindDialog() : this(null) { }

        public TransientWindDialog(TransientWindProfile? existing)
        {
            Result = existing ?? new TransientWindProfile();
            InitializeComponent();

            ChkEnabled.IsChecked = Result.Enabled;
            // Clamp the seed value into NUD range; -1 means disabled.
            NudESD.Value = (decimal)Math.Max(-1.0, Math.Min(100000.0, Result.ESDTimeS));

            GridEntries.ItemsSource = _rows;
            GridEntries.Columns.Add(new DataGridTextColumn
            {
                Header = "Time (s)",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(WindProfileEntry.TimeS)) { Mode = BindingMode.TwoWay }
            });
            GridEntries.Columns.Add(new DataGridTextColumn
            {
                Header = "Wind Speed (m/s)",
                Width = new DataGridLength(140),
                Binding = new Binding(nameof(WindProfileEntry.WindSpeed)) { Mode = BindingMode.TwoWay }
            });
            GridEntries.Columns.Add(new DataGridTextColumn
            {
                Header = "Direction (°)",
                Width = new DataGridLength(120),
                Binding = new Binding(nameof(WindProfileEntry.WindDirectionDeg)) { Mode = BindingMode.TwoWay }
            });

            // Stability is an enum; the DataGridComboBoxColumn lets the user
            // pick from the canonical Pasquill classes (A–F).
            var stabCol = new DataGridTemplateColumn
            {
                Header = "Stability",
                Width = new DataGridLength(110),
                CellTemplate = BuildStabilityCellTemplate(false),
                CellEditingTemplate = BuildStabilityCellTemplate(true)
            };
            GridEntries.Columns.Add(stabCol);

            foreach (var e in Result.Entries)
                _rows.Add(e);
        }

        /// <summary>Builds a one-cell DataTemplate that renders the stability
        /// class as a ComboBox bound to <see cref="WindProfileEntry.StabilityClass"/>.
        /// The same template is reused for view + edit modes so the cell
        /// always shows the dropdown (matches WPF behaviour where the column
        /// is a DataGridComboBoxColumn).</summary>
        private static global::Avalonia.Markup.Xaml.Templates.DataTemplate BuildStabilityCellTemplate(bool _)
        {
            var tmpl = new global::Avalonia.Markup.Xaml.Templates.DataTemplate
            {
                DataType = typeof(WindProfileEntry),
                Content = new global::Avalonia.Controls.Templates.FuncDataTemplate<WindProfileEntry>(
                    (item, _) =>
                    {
                        var cb = new ComboBox { Margin = new global::Avalonia.Thickness(2, 0) };
                        foreach (var v in Enum.GetValues<PasquillStabilityClass>())
                            cb.Items.Add(v);
                        cb.SelectedItem = item?.StabilityClass ?? PasquillStabilityClass.D;
                        cb.SelectionChanged += (_, _) =>
                        {
                            if (item != null && cb.SelectedItem is PasquillStabilityClass sc)
                                item.StabilityClass = sc;
                        };
                        return cb;
                    })
            };
            return tmpl;
        }

        private void BtnAdd_Click(object? sender, RoutedEventArgs e)
        {
            // Seed new rows just after the last existing one so the table
            // stays sorted by Time without user intervention.
            double nextT = _rows.Count == 0 ? 0 : _rows.Max(r => r.TimeS) + 10.0;
            _rows.Add(new WindProfileEntry
            {
                TimeS = nextT,
                WindSpeed = 5,
                WindDirectionDeg = 270,
                StabilityClass = PasquillStabilityClass.D
            });
        }

        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (GridEntries.SelectedItem is WindProfileEntry row)
                _rows.Remove(row);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            Result = new TransientWindProfile
            {
                Enabled = ChkEnabled.IsChecked == true,
                ESDTimeS = (double)(NudESD.Value ?? -1m)
            };
            foreach (var r in _rows.OrderBy(r => r.TimeS))
                Result.Entries.Add(r);

            Close(true);
        }
    }
}
