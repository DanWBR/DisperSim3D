using System;
using System.Windows.Media.Media3D;
using DisperSim3D.Core;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a 3D gas release source with position, release parameters, and exit conditions.
    /// </summary>
    public class ReleaseSource3D
    {
        /// <summary>
        /// Gets or sets the unique identifier for this release source.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the display name of this release source.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the 3D position of the release source in scene coordinates.
        /// </summary>
        public Point3D Position { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the flowsheet unit operation this source is attached to, if any.
        /// </summary>
        public string AttachedUnitId { get; set; }

        /// <summary>
        /// Gets or sets the gas properties for the released substance.
        /// </summary>
        public GasProperties Gas { get; set; }

        /// <summary>
        /// Gets or sets the mass release rate in kilograms per second.
        /// </summary>
        public double ReleaseRateKgPerS { get; set; }

        /// <summary>
        /// Gets or sets the total release duration in seconds.
        /// </summary>
        public double ReleaseDurationS { get; set; }

        /// <summary>
        /// Gets or sets the time interval between consecutive puff emissions in seconds.
        /// </summary>
        public double PuffIntervalS { get; set; }

        /// <summary>
        /// Gets or sets the vertical height offset from the source position in meters.
        /// </summary>
        public double ReleaseHeightOffset { get; set; }

        /// <summary>
        /// Gets or sets the high-pressure leak parameters, or <c>null</c> if not applicable.
        /// </summary>
        public HighPressureLeakParams HighPressureLeak { get; set; }

        /// <summary>
        /// Gets or sets the gas exit temperature in Kelvin.
        /// </summary>
        public double ExitTemperatureK { get; set; }

        /// <summary>
        /// Gets or sets the manually specified gas exit velocity in meters per second.
        /// A value of zero indicates the velocity should be computed automatically.
        /// </summary>
        public double ExitVelocityMPerS { get; set; }

        /// <summary>
        /// Gets or sets the stack or orifice diameter in meters.
        /// </summary>
        public double StackDiameterM { get; set; }

        /// <summary>
        /// Gets or sets the release azimuth angle in degrees (0 = North, clockwise).
        /// </summary>
        public double ReleaseAzimuthDeg { get; set; }

        /// <summary>
        /// Gets or sets the release elevation angle in degrees (0 = horizontal, positive = upward).
        /// </summary>
        public double ReleaseElevationDeg { get; set; }

        /// <summary>
        /// Gets the unit direction vector of the release computed from
        /// <see cref="ReleaseAzimuthDeg"/> and <see cref="ReleaseElevationDeg"/>.
        /// </summary>
        public Vector3D ReleaseDirection
        {
            get
            {
                double azRad = ReleaseAzimuthDeg * Math.PI / 180.0;
                double elRad = ReleaseElevationDeg * Math.PI / 180.0;
                double cosEl = Math.Cos(elRad);
                return new Vector3D(
                    cosEl * Math.Sin(azRad),
                    cosEl * Math.Cos(azRad),
                    Math.Sin(elRad));
            }
        }

        /// <summary>
        /// Gets the effective release position, offsetting <see cref="Position"/> vertically by <see cref="ReleaseHeightOffset"/>.
        /// </summary>
        public Point3D EffectivePosition
        {
            get
            {
                return new Point3D(Position.X, Position.Y, Position.Z + ReleaseHeightOffset);
            }
        }

        /// <summary>
        /// Gets the mass of gas released per puff in kilograms, computed as
        /// <see cref="ReleaseRateKgPerS"/> multiplied by <see cref="PuffIntervalS"/>.
        /// </summary>
        public double MassPerPuff
        {
            get
            {
                return ReleaseRateKgPerS * PuffIntervalS;
            }
        }

        /// <summary>
        /// Gets the effective orifice or stack diameter in meters.
        /// Returns <see cref="StackDiameterM"/> if set, otherwise falls back to the high-pressure leak orifice diameter.
        /// </summary>
        public double EffectiveDiameterM
        {
            get
            {
                if (StackDiameterM > 0) return StackDiameterM;
                if (HighPressureLeak != null && HighPressureLeak.OrificeDiameterM > 0)
                    return HighPressureLeak.OrificeDiameterM;
                return 0;
            }
        }

        /// <summary>
        /// Gets the computed gas exit velocity in meters per second.
        /// Returns <see cref="ExitVelocityMPerS"/> if explicitly set; otherwise computes from
        /// high-pressure leak parameters or from the release rate, diameter, and gas properties.
        /// </summary>
        public double ComputedExitVelocity
        {
            get
            {
                if (ExitVelocityMPerS > 0) return ExitVelocityMPerS;

                if (HighPressureLeak != null && HighPressureLeak.OrificeDiameterM > 0)
                {
                    double gamma = HighPressureLeak.GasGamma;
                    double M = HighPressureLeak.GasMolarMassKgMol;
                    double T = HighPressureLeak.VesselTemperatureK;
                    if (HighPressureLeakModel.IsChoked(HighPressureLeak))
                        return Math.Sqrt(gamma * 8.314 * T / M * (2.0 / (gamma + 1)));
                    double mdot = HighPressureLeakModel.MassFlowRate(HighPressureLeak);
                    double area = Math.PI * 0.25 * HighPressureLeak.OrificeDiameterM * HighPressureLeak.OrificeDiameterM;
                    double rho = 101325.0 * M / (8.314 * T);
                    return area > 0 && rho > 0 ? mdot / (rho * area) : 0;
                }

                double diam = EffectiveDiameterM;
                if (diam <= 0 || ReleaseRateKgPerS <= 0) return 0;

                double areaSimple = Math.PI * 0.25 * diam * diam;
                double molarMass = Gas != null && Gas.MolarMass > 0 ? Gas.MolarMass : 0.029;
                double rhoSimple = 101325.0 * molarMass / (8.314 * ExitTemperatureK);
                return ReleaseRateKgPerS / (rhoSimple * areaSimple);
            }
        }

        /// <summary>
        /// Gets the exit velocity as a 3D vector, combining <see cref="ComputedExitVelocity"/>
        /// with <see cref="ReleaseDirection"/>.
        /// </summary>
        public Vector3D ExitVelocityVector
        {
            get
            {
                double v = ComputedExitVelocity;
                if (v <= 0) return new Vector3D(0, 0, 0);
                var dir = ReleaseDirection;
                return new Vector3D(dir.X * v, dir.Y * v, dir.Z * v);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReleaseSource3D"/> class with default values.
        /// </summary>
        public ReleaseSource3D()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Release Source";
            Gas = new GasProperties();
            ReleaseRateKgPerS = 1.0;
            ReleaseDurationS = 60.0;
            PuffIntervalS = 1.0;
            ReleaseHeightOffset = 2.0;
            ExitTemperatureK = 293.15;
            ExitVelocityMPerS = 0;
            StackDiameterM = 0;
            ReleaseAzimuthDeg = 0;
            ReleaseElevationDeg = 0;
        }
    }
}
