#nullable enable
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>FireSourceDialog</c>.
    /// Edits a <see cref="FireSource"/>: jet/pool fire with flame length
    /// drivers (mass flow, orifice, heat of combustion, radiative fraction)
    /// and optional pool-fire-only fields (diameter, burn rate). The pool
    /// fields are gated by the "Pool fire" checkbox.
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
                ChkPoolFire.IsChecked    = existing.IsPoolFire;
                NudPoolDiameter.Value    = (decimal)existing.PoolDiameterM;
                NudBurnRate.Value        = (decimal)existing.PoolBurnRateKgM2S;
                UpdatePoolFieldsEnabled();
            }
        }

        private void ChkPoolFire_Changed(object? sender, RoutedEventArgs e)
            => UpdatePoolFieldsEnabled();

        private void UpdatePoolFieldsEnabled()
        {
            bool pool = ChkPoolFire.IsChecked == true;
            // NudPoolDiameter / NudBurnRate may not be wired up yet during
            // the initial XAML load; guard for safety.
            if (NudPoolDiameter != null) NudPoolDiameter.IsEnabled = pool;
            if (NudBurnRate != null) NudBurnRate.IsEnabled = pool;
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
                IsPoolFire           = ChkPoolFire.IsChecked == true,
                PoolDiameterM        = (double)(NudPoolDiameter.Value ?? 5m),
                PoolBurnRateKgM2S    = (double)(NudBurnRate.Value ?? 0.05m),
                Direction            = new Vector3D(0, 0, 1)
            };
            Close(true);
        }
    }
}
