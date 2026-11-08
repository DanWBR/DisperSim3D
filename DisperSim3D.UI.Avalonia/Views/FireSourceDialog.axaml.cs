#nullable enable
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>FireSourceDialog</c>.
    /// Edits a <see cref="FireSource"/>: jet fire, pool fire or fireball, with
    /// flame length drivers (mass flow, orifice, heat of combustion, radiative
    /// fraction), the pool-only fields (diameter, burn rate) and the fireball
    /// mass, each enabled by the Kind box. The second group picks the
    /// radiation model and its optional overrides — flame diameter and surface
    /// emissive power, both 0 meaning "derive from the correlations".
    /// </summary>
    public partial class FireSourceDialog : Window
    {
        public FireSource Result { get; private set; } = new FireSource();

        public FireSourceDialog() : this(null) { }

        public FireSourceDialog(FireSource? existing)
        {
            InitializeComponent();

            if (existing != null)
            {
                TxtName.Text             = existing.Name ?? "JetFire1";
                NudMassFlow.Value        = (decimal)existing.MassFlowRateKgS;
                NudOrifice.Value         = (decimal)existing.OrificeDiameterM;
                NudHeatCombustion.Value  = (decimal)existing.HeatOfCombustionJKg;
                NudRadFraction.Value     = (decimal)existing.RadiativeFraction;
                CmbKind.SelectedIndex    = (int)existing.Kind;
                NudFireballMass.Value    = (decimal)existing.FireballMassKg;
                NudPoolDiameter.Value    = (decimal)existing.PoolDiameterM;
                NudBurnRate.Value        = (decimal)existing.PoolBurnRateKgM2S;
                CmbRadiationModel.SelectedIndex =
                    existing.RadiationModel == RadiationModel.PointSource ? 1 : 0;
                NudFlameDiameter.Value   = (decimal)existing.FlameDiameterM;
                NudSep.Value             = (decimal)existing.SepKwM2;
                NudFuelMolar.Value       = (decimal)existing.FuelMolarMassKgMol;
                UpdatePoolFieldsEnabled();
            }
        }

        private void CmbKind_Changed(object? sender, SelectionChangedEventArgs e)
            => UpdatePoolFieldsEnabled();

        private FireKind SelectedKind => CmbKind?.SelectedIndex switch
        {
            1 => FireKind.Pool,
            2 => FireKind.Fireball,
            _ => FireKind.Jet
        };

        private void UpdatePoolFieldsEnabled()
        {
            var kind = SelectedKind;
            // The fields below the box may not be wired up yet during the
            // initial XAML load; guard for safety.
            if (NudPoolDiameter != null) NudPoolDiameter.IsEnabled = kind == FireKind.Pool;
            if (NudBurnRate != null) NudBurnRate.IsEnabled = kind == FireKind.Pool;
            if (NudFireballMass != null) NudFireballMass.IsEnabled = kind == FireKind.Fireball;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            Result = new FireSource
            {
                Name                 = string.IsNullOrWhiteSpace(TxtName.Text) ? "JetFire1" : TxtName.Text.Trim(),
                MassFlowRateKgS      = (double)(NudMassFlow.Value ?? 1m),
                OrificeDiameterM     = (double)(NudOrifice.Value ?? 0.025m),
                HeatOfCombustionJKg  = (double)(NudHeatCombustion.Value ?? 50_000_000m),
                RadiativeFraction    = (double)(NudRadFraction.Value ?? 0.2m),
                Kind                 = SelectedKind,
                FireballMassKg       = (double)(NudFireballMass.Value ?? 1000m),
                PoolDiameterM        = (double)(NudPoolDiameter.Value ?? 5m),
                PoolBurnRateKgM2S    = (double)(NudBurnRate.Value ?? 0.05m),
                RadiationModel       = CmbRadiationModel.SelectedIndex == 1
                                       ? RadiationModel.PointSource
                                       : RadiationModel.SolidFlame,
                FlameDiameterM       = (double)(NudFlameDiameter.Value ?? 0m),
                SepKwM2              = (double)(NudSep.Value ?? 0m),
                FuelMolarMassKgMol   = (double)(NudFuelMolar.Value ?? 0.016m),
                Direction            = new Vector3D(0, 0, 1)
            };
            Close(true);
        }
    }
}
