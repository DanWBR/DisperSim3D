#nullable enable
using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>GasLibraryItemDialog</c>.
    /// Edits a <see cref="GasLibraryItem"/> — either a pure substance
    /// (6 numeric properties) or a mixture (DataGrid of <see cref="GasComponent"/>
    /// rows). The Pure / Mixture radio buttons swap visibility of the two
    /// panels.
    ///
    /// The DataGrid is bound to an ObservableCollection so add/remove
    /// buttons reflect in the UI instantly — the WinForms DataGridView used
    /// CanUserAddRows="True"; we keep that behaviour but route add/remove
    /// through explicit buttons for cross-platform reliability (DataGrid's
    /// new-row footer is finicky on Avalonia 12).
    /// </summary>
    public partial class GasLibraryItemDialog : Window
    {
        public GasLibraryItem Result { get; private set; }

        private readonly ObservableCollection<GasComponent> _components = new();

        public GasLibraryItemDialog() : this(new GasLibraryItem()) { }

        public GasLibraryItemDialog(GasLibraryItem existing)
        {
            InitializeComponent();
            Result = existing ?? new GasLibraryItem();

            // Wire the DataGrid up before LoadFromItem so the bindings see
            // the populated collection.
            GridMix.ItemsSource = _components;
            GridMix.Columns.Add(new DataGridTextColumn
            {
                Header = "Component", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(GasComponent.Name)) { Mode = BindingMode.TwoWay }
            });
            GridMix.Columns.Add(new DataGridTextColumn
            {
                Header = "Molar Mass (kg/mol)", Width = new DataGridLength(140),
                Binding = new Binding(nameof(GasComponent.MolarMass)) { Mode = BindingMode.TwoWay }
            });
            GridMix.Columns.Add(new DataGridTextColumn
            {
                Header = "Mole Fraction", Width = new DataGridLength(110),
                Binding = new Binding(nameof(GasComponent.MoleFraction)) { Mode = BindingMode.TwoWay }
            });
            GridMix.Columns.Add(new DataGridTextColumn
            {
                Header = "LFL (kg/m³)", Width = new DataGridLength(110),
                Binding = new Binding(nameof(GasComponent.LFL)) { Mode = BindingMode.TwoWay }
            });
            GridMix.Columns.Add(new DataGridTextColumn
            {
                Header = "IDLH (kg/m³)", Width = new DataGridLength(110),
                Binding = new Binding(nameof(GasComponent.IDLH)) { Mode = BindingMode.TwoWay }
            });

            LoadFromItem(Result);
        }

        // ── Mode switching ──────────────────────────────────────────────────
        private void GasKind_Changed(object? sender, RoutedEventArgs e)
        {
            // The IsCheckedChanged event fires on BOTH radios when the
            // selection toggles. Guard against missing references on the
            // first call from XAML initialisation.
            if (PnlPure is null || PnlMixture is null) return;
            PnlPure.IsVisible = RbPure?.IsChecked == true;
            PnlMixture.IsVisible = RbMixture?.IsChecked == true;
        }

        // ── Load / save ─────────────────────────────────────────────────────
        private void LoadFromItem(GasLibraryItem item)
        {
            TxtName.Text = item.Name ?? "";

            if (item.Kind == GasLibraryItemKind.Mixture)
            {
                RbMixture.IsChecked = true;
                _components.Clear();
                if (item.Mixture != null)
                    foreach (var c in item.Mixture.Components)
                        _components.Add(Clone(c));
            }
            else
            {
                RbPure.IsChecked = true;
                var g = item.PureGas ?? new GasProperties();
                NudMolarMass.Value = (decimal)Clamp(g.MolarMass, 0.001, 1.0);
                NudLFL.Value = (decimal)Clamp(g.LFL, 0, 10);
                NudIDLH.Value = (decimal)Clamp(g.IDLH, 0, 10);
                NudERPG1.Value = (decimal)Clamp(g.ERPG1, 0, 10);
                NudERPG2.Value = (decimal)Clamp(g.ERPG2, 0, 10);
                NudERPG3.Value = (decimal)Clamp(g.ERPG3, 0, 10);
            }
            GasKind_Changed(null, new RoutedEventArgs());
        }

        private void BtnAddComponent_Click(object? sender, RoutedEventArgs e)
        {
            _components.Add(new GasComponent
            {
                Name = "Component " + (_components.Count + 1),
                MolarMass = 0.016, MoleFraction = 0.0, LFL = 0, IDLH = 0
            });
        }

        private void BtnRemoveComponent_Click(object? sender, RoutedEventArgs e)
        {
            if (GridMix.SelectedItem is GasComponent c)
                _components.Remove(c);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            Result.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Unnamed" : TxtName.Text.Trim();

            if (RbMixture.IsChecked == true)
            {
                Result.Kind = GasLibraryItemKind.Mixture;
                Result.Mixture = new GasMixture();
                foreach (var c in _components)
                    Result.Mixture.Components.Add(Clone(c));
            }
            else
            {
                Result.Kind = GasLibraryItemKind.Pure;
                Result.PureGas = new GasProperties
                {
                    Name = Result.Name,
                    MolarMass = (double)(NudMolarMass.Value ?? 0.016m),
                    LFL = (double)(NudLFL.Value ?? 0m),
                    IDLH = (double)(NudIDLH.Value ?? 0m),
                    ERPG1 = (double)(NudERPG1.Value ?? 0m),
                    ERPG2 = (double)(NudERPG2.Value ?? 0m),
                    ERPG3 = (double)(NudERPG3.Value ?? 0m)
                };
            }
            Close(true);
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static double Clamp(double v, double min, double max)
            => v < min ? min : v > max ? max : v;

        private static GasComponent Clone(GasComponent c) => new GasComponent
        {
            Name = c.Name, MolarMass = c.MolarMass,
            MoleFraction = c.MoleFraction, LFL = c.LFL, IDLH = c.IDLH
        };
    }
}
