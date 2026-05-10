using System;
using System.ComponentModel;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents the meteorological conditions used in a gas dispersion simulation,
    /// including wind profile, atmospheric stability, and ambient state.
    /// </summary>
    [System.ComponentModel.TypeConverter(typeof(System.ComponentModel.ExpandableObjectConverter))]
    public class MeteorologicalConditions
    {
        [Category("Wind")]
        [Description("Reference wind speed at the measurement height (m/s).")]
        public double WindSpeed { get; set; }

        [Category("Wind")]
        [Description("Direction the wind blows FROM (meteorological convention): 0=N, 90=E, 180=S, 270=W.")]
        public double WindDirectionDeg { get; set; }

        [Category("Stability")]
        [Description("Pasquill-Gifford atmospheric stability class. A=very unstable, D=neutral, F=very stable.")]
        public PasquillStabilityClass StabilityClass { get; set; }

        [Category("Ambient")]
        [Description("Ambient air temperature (K). 293.15 K = 20 °C.")]
        public double AmbientTemperature { get; set; }

        [Category("Ambient")]
        [Description("Ambient atmospheric pressure (Pa). 101325 Pa = sea level standard.")]
        public double AmbientPressure { get; set; }

        [Category("Wind")]
        [Description("Atmospheric mixing height — vertical extent above which dispersion is bounded (m).")]
        public double MixingHeightM { get; set; }

        [Category("Wind")]
        [Description("Reference height at which the wind speed was measured (m). Default = 10 m.")]
        public double WindMeasurementHeightM { get; set; }

        [Category("Wind")]
        [Description("Power-law exponent for wind profile u(z) = uref·(z/zref)^p. Negative = use default for stability + terrain.")]
        public double WindShearExponent { get; set; }

        [Category("Wind")]
        [Description("If true, uses the urban shear-exponent table; otherwise rural.")]
        public bool IsUrbanTerrain { get; set; }

        [Category("Wind")]
        [Description("Aerodynamic roughness length z0 (m). Drives log-law inlet and rough-wall functions in CFD. Typical: 2e-4 (water), 0.03 (open grass), 0.10 (cropland), 0.30 (suburban), 1.0 (urban/forest).")]
        public double RoughnessLengthM { get; set; }

        /// <summary>
        /// Gets the horizontal wind transport velocity vector at the reference measurement height.
        /// WindDirectionDeg follows meteorological convention (direction wind comes FROM),
        /// so the transport vector points in the opposite direction (where wind blows TO).
        /// </summary>
        public Vector3D WindVector
        {
            get
            {
                var radians = WindDirectionDeg * Math.PI / 180.0;
                return new Vector3D(
                    -WindSpeed * Math.Sin(radians),
                    -WindSpeed * Math.Cos(radians),
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
            RoughnessLengthM = 0.03; // open grass / rural default
        }
    }
}
