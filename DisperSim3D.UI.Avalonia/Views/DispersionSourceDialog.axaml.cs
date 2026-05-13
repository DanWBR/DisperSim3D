#nullable enable
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>DispersionSourceDialog</c>.
    /// Modal "Add Release Source" form with 10 fields (name + gas preset +
    /// 3 gas properties + 5 release parameters) and Cancel/OK buttons.
    ///
    /// Usage:
    ///   var dlg = new DispersionSourceDialog(windDirDeg);
    ///   if (await dlg.ShowDialog&lt;bool&gt;(this))
    ///       _scene.TopLevelSources.Add(dlg.BuildSource());
    ///
    /// First Avalonia dialog port. The pattern (Result property, BuildXxx()
    /// factory, async ShowDialog&lt;bool&gt; return) is what the remaining ~32
    /// dialogs will follow.
    /// </summary>
    public partial class DispersionSourceDialog : Window
    {
        // ── Output properties (only valid after OK click) ───────────────────
        public string SourceName { get; private set; } = "Source1";
        public GasProperties Gas { get; private set; } = GasProperties.CreateMethane();
        public double ReleaseRateKgPerS { get; private set; } = 0.5;
        public double PuffIntervalS { get; private set; } = 1.0;
        public double HeightOffset { get; private set; } = 2.0;
        public double AzimuthDeg { get; private set; }
        public double ElevationDeg { get; private set; }

        public DispersionSourceDialog() : this(0, 0, 0) { }

        /// <summary>Open with the dialog's Azimuth pre-populated from the
        /// project's wind direction (so a freshly-created source points
        /// "downwind" by default, matching the WinForms behaviour).</summary>
        public DispersionSourceDialog(double windDirectionDeg)
            : this(windDirectionDeg, windDirectionDeg, 0) { }

        /// <summary>Surface-normal placement constructor: caller pre-supplies
        /// the azimuth/elevation derived from the clicked surface. The OK
        /// handler keeps whatever the user has in the NUDs at the time, so
        /// surface-normal values survive unless explicitly changed.</summary>
        public DispersionSourceDialog(double windDirectionDeg,
            double initialAzimuthDeg, double initialElevationDeg)
        {
            InitializeComponent();

            // Pre-seed the azimuth / elevation NUDs. The NUDs clamp out-of-
            // range values; we replicate the WinForms clamping so 360°
            // doesn't crash the parser.
            double az = ((initialAzimuthDeg % 360) + 360) % 360;
            NudAzimuth.Value = (decimal)Math.Clamp(az, 0, 359);
            NudElevation.Value = (decimal)Math.Clamp(initialElevationDeg, -90, 90);

            // Methane is the default preset; lock the 3 gas properties until
            // the user picks "Custom".
            CmbGasPreset.SelectedIndex = 0;
            UpdateGasFieldEnabled();
        }

        // ── Gas preset wiring ───────────────────────────────────────────────
        private void CmbGasPreset_Changed(object? sender, SelectionChangedEventArgs e)
        {
            UpdateGasFieldEnabled();
            switch (CmbGasPreset.SelectedIndex)
            {
                case 0: NudMolarMass.Value = 0.016m; NudLFL.Value = 0.033m; NudIDLH.Value = 0.033m; break;
                case 1: NudMolarMass.Value = 0.034m; NudLFL.Value = 0.028m; NudIDLH.Value = 0.070m; break;
                case 2: NudMolarMass.Value = 0.017m; NudLFL.Value = 0.110m; NudIDLH.Value = 0.018m; break;
                // case 3 = Custom — keep current values, just unlock fields.
            }
        }

        private void UpdateGasFieldEnabled()
        {
            bool custom = CmbGasPreset.SelectedIndex == 3;
            NudMolarMass.IsEnabled = custom;
            NudLFL.IsEnabled = custom;
            NudIDLH.IsEnabled = custom;
        }

        // ── Button handlers ─────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            if (!Validate(out string error))
            {
                ErrorText.Text = error;
                ErrorBanner.IsVisible = true;
                return;
            }

            SourceName = string.IsNullOrWhiteSpace(TxtName.Text) ? "Source1" : TxtName.Text.Trim();
            ReleaseRateKgPerS = (double)(NudReleaseRate.Value ?? 0.5m);
            PuffIntervalS = (double)(NudPuffInterval.Value ?? 1m);
            HeightOffset = (double)(NudHeightOffset.Value ?? 2m);
            AzimuthDeg = (double)(NudAzimuth.Value ?? 0m);
            ElevationDeg = (double)(NudElevation.Value ?? 0m);

            Gas = CmbGasPreset.SelectedIndex switch
            {
                0 => GasProperties.CreateMethane(),
                1 => GasProperties.CreateH2S(),
                2 => GasProperties.CreateAmmonia(),
                _ => new GasProperties
                {
                    Name = "Custom",
                    MolarMass = (double)(NudMolarMass.Value ?? 0.016m),
                    LFL = (double)(NudLFL.Value ?? 0.033m),
                    IDLH = (double)(NudIDLH.Value ?? 0.033m)
                }
            };

            Close(true);
        }

        private bool Validate(out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                error = "Name cannot be empty.";
                return false;
            }
            decimal rate = NudReleaseRate.Value ?? 0m;
            if (rate <= 0)
            {
                error = "Release Rate must be > 0 kg/s.";
                return false;
            }
            return true;
        }

        // ── Result factory ──────────────────────────────────────────────────
        /// <summary>Builds a <see cref="ReleaseSource3D"/> from the dialog's
        /// fields. Call after <c>ShowDialog&lt;bool&gt;</c> returns true. The
        /// source's <c>Position</c> is left at (0,0,0) — the caller is
        /// expected to place it (e.g. from a ground-plane click).</summary>
        public ReleaseSource3D BuildSource()
        {
            return new ReleaseSource3D
            {
                Name = SourceName,
                Gas = Gas,
                ReleaseRateKgPerS = ReleaseRateKgPerS,
                PuffIntervalS = PuffIntervalS,
                ReleaseHeightOffset = HeightOffset,
                ReleaseAzimuthDeg = AzimuthDeg,
                ReleaseElevationDeg = ElevationDeg
            };
        }
    }
}
