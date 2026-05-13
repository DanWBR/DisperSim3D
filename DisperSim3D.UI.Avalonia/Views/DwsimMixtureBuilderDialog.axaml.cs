#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>DwsimMixtureBuilderDialog</c>.
    /// Builds a <see cref="GasMixture"/> by pulling compound data from DWSIM
    /// via the FluentAPI and running a property-package flash at the chosen
    /// T/P. The resulting mixture is wrapped in a <see cref="GasLibraryItem"/>
    /// and surfaced via <see cref="Result"/>. The left pane has a searchable
    /// list of available compounds; the right pane shows the editable
    /// composition table + computed bulk properties.
    /// </summary>
    public partial class DwsimMixtureBuilderDialog : Window
    {
        private List<string> _allCompounds = new();
        private readonly ObservableCollection<CompositionRow> _composition = new();
        private DwsimThermo.MixtureProperties? _lastProps;

        public GasLibraryItem? Result { get; private set; }

        public DwsimMixtureBuilderDialog()
        {
            InitializeComponent();

            // Composition grid: compound name (read-only) + mole fraction
            // (text — parsed on commit so users can paste fractions in any
            // standard format).
            GridComposition.ItemsSource = _composition;
            GridComposition.Columns.Add(new DataGridTextColumn
            {
                Header = "Compound",
                Width = new DataGridLength(2, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                Binding = new Binding(nameof(CompositionRow.Name))
            });
            GridComposition.Columns.Add(new DataGridTextColumn
            {
                Header = "Mole Fraction",
                Width = new DataGridLength(120),
                Binding = new Binding(nameof(CompositionRow.MoleFractionText))
                    { Mode = BindingMode.TwoWay }
            });

            TryAutoInit();
        }

        // ── DWSIM bootstrap ──────────────────────────────────────────────────
        private async void TryAutoInit()
        {
            string path = AppSettings.Instance.DwsimInstallPath ?? "";
            if (string.IsNullOrEmpty(path))
            {
                LblStatus.Text = "DWSIM install path not configured — open Tools → DWSIM Settings...";
                return;
            }

            LblStatus.Text = "Loading compound database...";
            // Initialize + AvailableCompounds touch the FluentAPI; both run
            // off the UI thread so the dialog doesn't freeze on a cold load.
            bool ok = await Task.Run(() => DwsimThermo.Initialize(path));
            if (!ok)
            {
                LblStatus.Text = "DWSIM init failed: " + (DwsimThermo.LastError ?? "(unknown)")
                    + "  (configure via Tools → DWSIM Settings...)";
                return;
            }

            _allCompounds = await Task.Run(() => DwsimThermo.AvailableCompounds().ToList());
            FilterCompoundList();
            LblStatus.Text = _allCompounds.Count + " compounds loaded.";
        }

        // ── Compound list filter ─────────────────────────────────────────────
        private void TxtSearch_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
            => FilterCompoundList();

        private void FilterCompoundList()
        {
            string q = (TxtSearch.Text ?? "").Trim();
            LstAvailable.Items.Clear();
            foreach (var c in _allCompounds)
                if (q.Length == 0 || c.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    LstAvailable.Items.Add(c);
        }

        // ── Add / Remove rows ────────────────────────────────────────────────
        private void BtnAdd_Click(object? sender, RoutedEventArgs e)
        {
            if (LstAvailable.SelectedItem is not string name) return;
            // Skip duplicates — comparing case-insensitively so "Methane"
            // and "methane" don't end up in the grid twice.
            foreach (var r in _composition)
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                    return;
            _composition.Add(new CompositionRow
            {
                Name = name,
                MoleFractionText = ((double)(NudFraction.Value ?? 1m))
                    .ToString("G4", CultureInfo.InvariantCulture)
            });
        }

        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (GridComposition.SelectedItem is CompositionRow row)
                _composition.Remove(row);
        }

        // ── Read composition from grid ───────────────────────────────────────
        private Dictionary<string, double> ReadComposition()
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in _composition)
            {
                if (double.TryParse(r.MoleFractionText, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double v) && v > 0)
                    dict[r.Name] = v;
            }
            return dict;
        }

        // ── Flash ────────────────────────────────────────────────────────────
        private async void BtnCompute_Click(object? sender, RoutedEventArgs e)
        {
            var comp = ReadComposition();
            if (comp.Count == 0)
            {
                LblStatus.Text = "Add at least one compound.";
                return;
            }

            LblStatus.Text = "Running flash (" + AppSettings.Instance.DwsimPropertyPackage + ")...";
            BtnCompute.IsEnabled = false;
            try
            {
                double t = (double)(NudT.Value ?? 293m);
                double p = (double)(NudP.Value ?? 101325m);
                _lastProps = await Task.Run(() =>
                    DwsimThermo.ComputeMixtureProperties(comp, t, p));
                if (!string.IsNullOrEmpty(_lastProps.Error))
                {
                    LblResults.Text = "Flash failed: " + _lastProps.Error;
                    LblStatus.Text = "Computation failed.";
                    return;
                }
                LblResults.Text = string.Format(CultureInfo.InvariantCulture,
                    "M = {0:F4} kg/mol    ρ = {1:F3} kg/m³    μ = {2:E3} Pa·s    Cp = {3:F1} J/kg/K    γ = {4:F3}",
                    _lastProps.MolarMassKgMol, _lastProps.DensityKgM3,
                    _lastProps.ViscosityPaS, _lastProps.CpJPerKgK, _lastProps.GammaCpCv);
                LblStatus.Text = "Properties ready.";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Flash error: " + ex.Message;
            }
            finally
            {
                BtnCompute.IsEnabled = true;
            }
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            var comp = ReadComposition();
            if (comp.Count == 0)
            {
                LblStatus.Text = "Add at least one compound.";
                return;
            }

            // Normalise so the engine doesn't have to handle fractions that
            // don't sum to 1 (the WinForms version does the same).
            double sum = comp.Values.Sum();
            if (sum <= 0)
            {
                LblStatus.Text = "Composition sums to zero.";
                return;
            }

            var mix = new GasMixture();
            foreach (var kv in comp)
            {
                // Pull per-component thermo constants from DWSIM's database;
                // hazard constants (LFL / IDLH) come from DisperSim3D's
                // local table — DWSIM doesn't carry those.
                var info = DwsimThermo.GetCompoundInfo(kv.Key);
                double mw = (info != null && info.MolarMassKgMol > 0) ? info.MolarMassKgMol : 0.029;
                var haz = HazardDatabase.Lookup(kv.Key);
                mix.Components.Add(new GasComponent
                {
                    Name = kv.Key,
                    MoleFraction = kv.Value / sum,
                    MolarMass = mw,
                    LFL = haz?.LflKgM3 ?? 0,
                    UFL = haz?.UflKgM3 ?? 0,
                    IDLH = haz?.IdlhKgM3 ?? 0
                });
            }

            // Mixture-name heuristic: top 3 compounds + "(+N)" overflow.
            string mixName = string.Join("+", comp.Keys.Take(3));
            if (comp.Count > 3) mixName += " (+" + (comp.Count - 3) + ")";
            Result = GasLibraryItem.FromMixture(mixName, mix);
            Close(true);
        }

        /// <summary>Editable row in the composition grid. Stores the mole
        /// fraction as a string so users can type partial values without the
        /// grid eagerly parsing them as zero.</summary>
        public sealed class CompositionRow
        {
            public string Name { get; set; } = "";
            public string MoleFractionText { get; set; } = "1.0";
        }
    }
}
