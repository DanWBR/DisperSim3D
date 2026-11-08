using System;
using System.Globalization;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Calm and near-calm wind. A project may set 0 m/s — in:Flux runs calm cases —
    /// but every model here needs some wind: the Gaussian models divide by it (and
    /// so does Briggs plume rise), the OpenFOAM atmospheric inlet derives u*, k and ε
    /// from it, the FluidX3D tracer sizes its subgrid diffusivity and jet length with
    /// it. A slower wind runs at the lowest speed its model accepts, in its own
    /// direction, never at a made-up default.
    /// </summary>
    public static class CalmWind
    {
        /// <summary>Gaussian plume and puff: concentration and plume rise go as 1/u.</summary>
        public const double GaussianMinimumMPerS = 0.5;

        /// <summary>Unified integral model: the friction velocity of the wind profile.</summary>
        public const double UnifiedMinimumMPerS = 0.3;

        /// <summary>OpenFOAM: the atmospheric inlet's Uref, k and ε, and the turbulent diffusivity.</summary>
        public const double OpenFoamMinimumMPerS = 0.5;

        /// <summary>FluidX3D: the wind field's inlet, and the dispersion tracer's subgrid diffusivity and jet momentum length.</summary>
        public const double FluidX3DMinimumMPerS = 0.5;

        /// <summary>The wind speed a model runs at: <paramref name="windSpeed"/>, or its minimum.</summary>
        public static double AtLeast(double windSpeed, double minimumMPerS) =>
            Math.Max(windSpeed, minimumMPerS);

        /// <summary>
        /// <paramref name="meteo"/> itself when its wind reaches the minimum; otherwise a
        /// copy with the wind raised to it, same direction. The project's own
        /// conditions are never changed.
        /// </summary>
        public static MeteorologicalConditions AtLeast(MeteorologicalConditions meteo, double minimumMPerS)
        {
            if (meteo == null || meteo.WindSpeed >= minimumMPerS) return meteo;
            var raised = meteo.Clone();
            raised.WindSpeed = minimumMPerS;
            return raised;
        }

        /// <summary>A line for the run log when the wind was raised, or null when it was not.</summary>
        public static string Note(MeteorologicalConditions meteo, double minimumMPerS, string model)
        {
            if (meteo == null || meteo.WindSpeed >= minimumMPerS) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "{0}: wind {1:0.##} m/s is below the {2:0.##} m/s this model needs; running at {2:0.##} m/s",
                model, meteo.WindSpeed, minimumMPerS);
        }
    }
}
