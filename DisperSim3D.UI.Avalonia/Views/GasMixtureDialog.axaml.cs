#nullable enable
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>GasMixtureDialog</c>.
    /// Edits a <see cref="GasMixture"/>: the list of <see cref="GasComponent"/>
    /// rows that make up the mixture. Each row carries name, molar mass,
    /// mole fraction, LFL and IDLH. The DataGrid is bound to an
    /// <see cref="ObservableCollection{T}"/> of the actual engine
    /// <c>GasComponent</c> instances — no view-model wrapping — because the
    /// types already expose plain double properties that the grid's text
    /// columns can edit directly.
    /// </summary>
    public partial class GasMixtureDialog : Window
    {
        public GasMixture Result { get; private set; }

        private readonly ObservableCollection<GasComponent> _rows = new();

        public GasMixtureDialog() : this(null) { }

        public GasMixtureDialog(GasMixture? existing)
        {
            Result = existing ?? new GasMixture();
            InitializeComponent();

            GridComponents.ItemsSource = _rows;
            GridComponents.Columns.Add(new DataGridTextColumn
            {
                Header = "Component",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(GasComponent.Name)) { Mode = BindingMode.TwoWay }
            });
            GridComponents.Columns.Add(new DataGridTextColumn
            {
                Header = "Molar Mass (kg/mol)",
                Width = new DataGridLength(140),
                Binding = new Binding(nameof(GasComponent.MolarMass)) { Mode = BindingMode.TwoWay }
            });
            GridComponents.Columns.Add(new DataGridTextColumn
            {
                Header = "Mole Fraction",
                Width = new DataGridLength(110),
                Binding = new Binding(nameof(GasComponent.MoleFraction)) { Mode = BindingMode.TwoWay }
            });
            GridComponents.Columns.Add(new DataGridTextColumn
            {
                Header = "LFL (kg/m³)",
                Width = new DataGridLength(100),
                Binding = new Binding(nameof(GasComponent.LFL)) { Mode = BindingMode.TwoWay }
            });
            GridComponents.Columns.Add(new DataGridTextColumn
            {
                Header = "IDLH (kg/m³)",
                Width = new DataGridLength(100),
                Binding = new Binding(nameof(GasComponent.IDLH)) { Mode = BindingMode.TwoWay }
            });

            foreach (var c in Result.Components)
                _rows.Add(c);

            // If the caller passed an empty mixture, seed with a single
            // default row so the grid isn't visually empty / confusing.
            if (_rows.Count == 0)
                _rows.Add(new GasComponent { Name = "Methane", MolarMass = 0.016, MoleFraction = 1.0 });
        }

        private void BtnAdd_Click(object? sender, RoutedEventArgs e)
            => _rows.Add(new GasComponent { Name = "Component", MolarMass = 0.016, MoleFraction = 0 });

        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (GridComponents.SelectedItem is GasComponent row)
                _rows.Remove(row);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            // We bound to a fresh ObservableCollection but kept the engine
            // GasComponent instances; rebuild Result.Components in the same
            // order the user sees in the grid. Drop empty/invalid rows.
            Result = new GasMixture();
            foreach (var c in _rows)
            {
                if (string.IsNullOrWhiteSpace(c.Name)) continue;
                Result.Components.Add(c);
            }
            Close(true);
        }
    }
}
