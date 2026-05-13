#nullable enable
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Core;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>HighPressureSourceDialog</c>.
    /// Edits a <see cref="HighPressureLeakParams"/> with live feedback: when
    /// the orifice diameter is the input, we show the resulting mass flow;
    /// when the user checks "Specify Mass Flow Rate", the orifice diameter
    /// is back-calculated from the chosen rate. The flow-regime label
    /// (CHOKED / Unchoked) updates on every parameter edit.
    /// </summary>
    public partial class HighPressureSourceDialog : Window
    {
        public HighPressureLeakParams Result { get; private set; } = new HighPressureLeakParams();

        // Guard the live-recalc against re-entry — a NUD value change inside
        // ComputeOrificeFromMassFlow() would otherwise re-fire ValueChanged.
        private bool _updating;

        public HighPressureSourceDialog() : this(null) { }

        public HighPressureSourceDialog(HighPressureLeakParams? existing)
        {
            InitializeComponent();

            if (existing != null)
            {
                NudPressure.Value      = (decimal)existing.VesselPressurePa;
                NudTemperature.Value   = (decimal)existing.VesselTemperatureK;
                NudGamma.Value         = (decimal)existing.GasGamma;
                NudMolarMass.Value     = (decimal)existing.GasMolarMassKgMol;
                NudDischargeCoeff.Value = (decimal)existing.DischargeCoefficient;
                NudVolume.Value        = (decimal)existing.VesselVolumeM3;
                NudOrifice.Value       = (decimal)existing.OrificeDiameterM;
            }

            // Wire ValueChanged for live calc. Done after seeding so the
            // calculator only fires from user edits.
            NudPressure.ValueChanged       += (_, _) => UpdateCalc();
            NudTemperature.ValueChanged    += (_, _) => UpdateCalc();
            NudGamma.ValueChanged          += (_, _) => UpdateCalc();
            NudMolarMass.ValueChanged      += (_, _) => UpdateCalc();
            NudDischargeCoeff.ValueChanged += (_, _) => UpdateCalc();
            NudOrifice.ValueChanged        += (_, _) => UpdateCalc();
            NudMassFlowRate.ValueChanged   += (_, _) => UpdateCalc();

            OnInputModeChanged();
            UpdateCalc();
        }

        private void ChkSpecifyMassFlow_Changed(object? sender, RoutedEventArgs e)
            => OnInputModeChanged();

        private void OnInputModeChanged()
        {
            bool specifyFlow = ChkSpecifyMassFlow.IsChecked == true;
            NudOrifice.IsEnabled = !specifyFlow;
            NudMassFlowRate.IsEnabled = specifyFlow;
            UpdateCalc();
        }

        private double ComputeOrificeFromMassFlow()
        {
            var p = MakeParamsFromUI(0.01);
            return HighPressureLeakModel.OrificeDiameterFromMassFlow(
                p, (double)(NudMassFlowRate.Value ?? 1m));
        }

        private HighPressureLeakParams MakeParamsFromUI(double orificeDiameter)
            => new HighPressureLeakParams
            {
                VesselPressurePa       = (double)(NudPressure.Value ?? 1000000m),
                VesselTemperatureK     = (double)(NudTemperature.Value ?? 293.15m),
                OrificeDiameterM       = orificeDiameter,
                GasGamma               = (double)(NudGamma.Value ?? 1.4m),
                GasMolarMassKgMol      = (double)(NudMolarMass.Value ?? 0.016m),
                DischargeCoefficient   = (double)(NudDischargeCoeff.Value ?? 0.65m)
            };

        private void UpdateCalc()
        {
            if (_updating) return;
            _updating = true;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                if (ChkSpecifyMassFlow.IsChecked == true)
                {
                    double diam = ComputeOrificeFromMassFlow();
                    var p = MakeParamsFromUI(diam);
                    bool choked = HighPressureLeakModel.IsChoked(p);
                    LblChoked.Text = choked ? "CHOKED" : "Unchoked";
                    LblFlowRate.Text =
                        "Orifice: " + (diam * 1000).ToString("F2", inv) + " mm "
                      + "(" + diam.ToString("F4", inv) + " m)";
                }
                else
                {
                    var p = MakeParamsFromUI((double)(NudOrifice.Value ?? 0.025m));
                    bool choked = HighPressureLeakModel.IsChoked(p);
                    double mdot = HighPressureLeakModel.MassFlowRate(p);
                    LblChoked.Text = choked ? "CHOKED" : "Unchoked";
                    LblFlowRate.Text = mdot.ToString("F4", inv) + " kg/s";
                }
            }
            catch
            {
                // A transient inconsistent state during typing (e.g. orifice
                // briefly out of range) shouldn't blow up the recalc; just
                // skip this update and let the next one fix the display.
            }
            _updating = false;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            double orifice = ChkSpecifyMassFlow.IsChecked == true
                ? ComputeOrificeFromMassFlow()
                : (double)(NudOrifice.Value ?? 0.025m);

            Result = new HighPressureLeakParams
            {
                VesselPressurePa       = (double)(NudPressure.Value ?? 1000000m),
                VesselTemperatureK     = (double)(NudTemperature.Value ?? 293.15m),
                OrificeDiameterM       = orifice,
                VesselVolumeM3         = (double)(NudVolume.Value ?? 10m),
                GasGamma               = (double)(NudGamma.Value ?? 1.4m),
                GasMolarMassKgMol      = (double)(NudMolarMass.Value ?? 0.016m),
                DischargeCoefficient   = (double)(NudDischargeCoeff.Value ?? 0.65m)
            };
            Close(true);
        }
    }
}
