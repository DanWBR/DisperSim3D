using System;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents the meteorological conditions used in a gas dispersion simulation,
    /// including wind profile, atmospheric stability, and ambient state.
    /// </summary>
    public class MeteorologicalConditions
    {
        /// <summary>
        /// Gets or sets the reference wind speed in meters per second.
        /// </summary>
        public double WindSpeed { get; set; }

        /// <summary>
        /// Gets or sets the wind direction in degrees (meteorological convention: 0 = from North, 270 = from West).
        /// </summary>
        public double WindDirectionDeg { get; set; }

        /// <summary>
        /// Gets or sets the Pasquill-Gifford atmospheric stability class.
        /// </summary>
        public PasquillStabilityClass StabilityClass { get; set; }

        /// <summary>
        /// Gets or sets the ambient temperature in Kelvin.
        /// </summary>
        public double AmbientTemperature { get; set; }

        /// <summary>
        /// Gets or sets the ambient atmospheric pressure in Pascals.
        /// </summary>
        public double AmbientPressure { get; set; }

        /// <summary>
        /// Gets or sets the atmospheric mixing height in meters.
        /// </summary>
        public double MixingHeightM { get; set; }

        /// <summary>
        /// Gets or sets the height at which wind speed was measured, in meters.
        /// </summary>
        public double WindMeasurementHeightM { get; set; }

        /// <summary>
        /// Gets or sets the wind shear power-law exponent.
        /// A negative value indicates the default exponent should be used based on stability class and terrain.
        /// </summary>
        public double WindShearExponent { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the terrain is urban.
        /// Affects the default wind shear exponent selection.
        /// </summary>
        public bool IsUrbanTerrain { get; set; }

        /// <summary>
        /// Gets the horizontal wind velocity vector at the reference measurement height,
        /// computed from <see cref="WindSpeed"/> and <see cref="WindDirectionDeg"/>.
        /// </summary>
        public Vector3D WindVector
        {
            get
            {
                var radians = WindDirectionDeg * Math.PI / 180.0;
                return new Vector3D(
                    WindSpeed * Math.Sin(radians),
                    WindSpeed * Math.Cos(radians),
                    0);
            }
        }

        /// <summary>
        /// Computes the wind speed at a given height using the power-law wind profile.
        /// </summary>
        /// <param name="z">The height above ground in meters (clamped to a minimum of 0.5 m).</param>
        /// <returns>The wind speed at the specified height in meters per second.</returns>
        public double WindSpeedAtHeight(double z)
        {
            if (z < 0.5) z = 0.5;
            double p = WindShearExponent >= 0 ? WindShearExponent : GetDefaultShearExponent();
            double zRef = WindMeasurementHeightM > 0 ? WindMeasurementHeightM : 10.0;
            return WindSpeed * Math.Pow(z / zRef, p);
        }

        /// <summary>
        /// Computes the horizontal wind velocity vector at a given height.
        /// </summary>
        /// <param name="z">The height above ground in meters.</param>
        /// <returns>The wind velocity vector at the specified height.</returns>
        public Vector3D WindVectorAtHeight(double z)
        {
            double ratio = WindSpeedAtHeight(z) / Math.Max(WindSpeed, 0.01);
            var wv = WindVector;
            return new Vector3D(wv.X * ratio, wv.Y * ratio, 0);
        }

        private double GetDefaultShearExponent()
        {
            if (IsUrbanTerrain)
            {
                switch (StabilityClass)
                {
                    case PasquillStabilityClass.A: return 0.15;
                    case PasquillStabilityClass.B: return 0.15;
                    case PasquillStabilityClass.C: return 0.20;
                    case PasquillStabilityClass.D: return 0.25;
                    case PasquillStabilityClass.E: return 0.30;
                    case PasquillStabilityClass.F: return 0.30;
                    default: return 0.25;
                }
            }
            switch (StabilityClass)
            {
                case PasquillStabilityClass.A: return 0.07;
                case PasquillStabilityClass.B: return 0.07;
                case PasquillStabilityClass.C: return 0.10;
                case PasquillStabilityClass.D: return 0.15;
                case PasquillStabilityClass.E: return 0.35;
                case PasquillStabilityClass.F: return 0.55;
                default: return 0.15;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeteorologicalConditions"/> class with default values.
        /// </summary>
        public MeteorologicalConditions()
        {
            WindSpeed = 5.0;
            WindDirectionDeg = 270.0;
            StabilityClass = PasquillStabilityClass.D;
            AmbientTemperature = 293.15;
            AmbientPressure = 101325.0;
            MixingHeightM = 1000.0;
            WindMeasurementHeightM = 10.0;
            WindShearExponent = -1;
            IsUrbanTerrain = false;
        }
    }
}
