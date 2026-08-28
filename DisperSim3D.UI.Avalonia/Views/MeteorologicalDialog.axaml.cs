#nullable enable
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>MeteorologicalDialog</c>.
    /// Edits a <see cref="MeteorologicalConditions"/> in-place: wind speed,
    /// direction, Pasquill stability class, ambient temperature, ambient
    /// pressure. Cancel/OK like every other dialog.
    /// </summary>
    public partial class MeteorologicalDialog : Window
    {
        public MeteorologicalConditions? Result { get; private set; }

        public MeteorologicalDialog() : this(null) { }

        public MeteorologicalDialog(MeteorologicalConditions? existing)
        {
            InitializeComponent();

            // Seed from existing, otherwise sensible defaults (already in XAML).
            if (existing != null)
            {
                NudWindSpeed.Value = (decimal)existing.WindSpeed;
                NudWindDir.Value = (decimal)existing.WindDirectionDeg;
                CmbStability.SelectedIndex = (int)existing.StabilityClass;
                NudTemperature.Value = (decimal)existing.AmbientTemperature;
                NudPressure.Value = (decimal)existing.AmbientPressure;
                NudHumidity.Value = (decimal)(existing.RelativeHumidity * 100.0);
            }
            else
            {
                CmbStability.SelectedIndex = 3; // D — Neutral
            }
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            if (CmbStability.SelectedIndex < 0)
            {
                ErrorText.Text = "Please pick a stability class.";
                ErrorBanner.IsVisible = true;
                return;
            }

            Result = new MeteorologicalConditions
            {
                WindSpeed = (double)(NudWindSpeed.Value ?? 5m),
                WindDirectionDeg = (double)(NudWindDir.Value ?? 270m),
                StabilityClass = (PasquillStabilityClass)CmbStability.SelectedIndex,
                AmbientTemperature = (double)(NudTemperature.Value ?? 293.15m),
                AmbientPressure = (double)(NudPressure.Value ?? 101325m),
                RelativeHumidity = (double)(NudHumidity.Value ?? 50m) / 100.0
            };
            Close(true);
        }
    }
}
