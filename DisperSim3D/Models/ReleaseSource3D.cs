using System;
using System.Collections.Generic;
using System.ComponentModel;
using DisperSim3D.Geometry;
using DisperSim3D.Core;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a 3D gas release source with position, release parameters, and exit conditions.
    /// </summary>
    public class ReleaseSource3D
    {
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; }

        [Category("Identity")]
        [Description("Display name shown in the project tree.")]
        public string Name { get; set; }

        [Category("Position")]
        [Description("3D position of the release in scene coordinates (m).")]
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        [Editor("DisperSim3D.Controls.Point3DPropertyEditor, DisperSim3D.UI.Wpf",
            "HandyControl.Controls.PropertyEditorBase, HandyControl")]
        public Point3D Position { get; set; }

        [Category("Identity")]
        [Description("Identifier of the unit operation this source is attached to, if any.")]
        public string AttachedUnitId { get; set; }

        [Category("Gas")]
        [Description("Inline gas properties (legacy). New code should prefer GasRefId.")]
        public GasProperties Gas { get; set; }

        [Category("Gas")]
        [Description("Reference to a GasLibraryItem in the project's gas library. Takes precedence over the inline Gas property.")]
        public string GasRefId { get; set; }

        [Category("Visualization")]
        [Description("Whether to draw the source marker in the 3D viewport.")]
        public bool IsVisible { get; set; } = true;

        [Category("Release")]
        [Description("Mass release rate (kg/s).")]
        public double ReleaseRateKgPerS { get; set; }

        [Category("Release")]
        [Description("Duration of the continuous release (s). 0 = active for the whole simulation duration. Used by the CFD case writer for time-limited fvOptions sources.")]
        public double ReleaseDurationS { get; set; }

        [Category("Release")]
        [Description("Time interval between consecutive puff emissions (s) — Gaussian Puff only.")]
        public double PuffIntervalS { get; set; }

        [Category("Position")]
        [Description("Vertical height offset added to the position Z (m). Default 2 m.")]
        public double ReleaseHeightOffset { get; set; }

        [Category("High-Pressure Leak")]
        [Description("Optional high-pressure leak parameters (vessel pressure, orifice, etc.). Null = no HP leak; release rate comes from ReleaseRateKgPerS.")]
        public HighPressureLeakParams HighPressureLeak { get; set; }

        [Category("Jet")]
        [Description("Gas exit temperature (K).")]
        public double ExitTemperatureK { get; set; }

        [Category("Jet")]
        [Description("Manually specified gas exit velocity (m/s). 0 = computed automatically from rate / orifice area.")]
        public double ExitVelocityMPerS { get; set; }

        [Category("Jet")]
        [Description("Stack or orifice diameter (m). Used for jet momentum and Birch & Schefer expanded source.")]
        public double StackDiameterM { get; set; }

        [Category("Release")]
        [Description("Release azimuth angle in degrees (0 = North, clockwise).")]
        public double ReleaseAzimuthDeg { get; set; }

        [Category("Release")]
        [Description("Release elevation angle in degrees (0 = horizontal, +ve = upward).")]
        public double ReleaseElevationDeg { get; set; }

        [Category("Risk / IOGP")]
        [Description("Equipment inventory contributing to this release scenario. The leak frequency is computed as the sum of IOGP 434-01 per-item (or per-metre) frequencies over all items at the chosen HoleSizeBand.")]
        public List<EquipmentInventoryItem> EquipmentInventory { get; set; }
            = new List<EquipmentInventoryItem>();

        [Category("Risk / IOGP")]
        [Description("Representative hole-size band for the modelled release. Geometric-mean diameters: Tiny=1.7mm, Small=5.5mm, Medium=22.4mm, Large=86.6mm, Rupture=full bore.")]
        public IogpHoleSizeBand HoleSizeBand { get; set; } = IogpHoleSizeBand.Medium;

        [Category("Risk / IOGP")]
        [Description("When true, LeakFrequencyPerYear is recomputed from EquipmentInventory + HoleSizeBand on every read of EffectiveLeakFrequencyPerYear. When false, the property holds a user-entered override.")]
        public bool AutoComputeLeakFrequency { get; set; } = true;

        [Category("Risk / IOGP")]
        [Description("Manual override for source leak frequency (events/year). Only consulted when AutoComputeLeakFrequency is false. Default 1e-4 = a generic order-of-magnitude leak for a small process unit.")]
        public double LeakFrequencyPerYear { get; set; } = 1e-4;

        /// <summary>
        /// Total leak frequency for this source in events per year. When
        /// <see cref="AutoComputeLeakFrequency"/> is true, this is recomputed on
        /// every access by summing the IOGP 434-01 frequencies over the equipment
        /// inventory at the configured <see cref="HoleSizeBand"/>. When false, it
        /// returns the user-supplied <see cref="LeakFrequencyPerYear"/> override.
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public double EffectiveLeakFrequencyPerYear
        {
            get
            {
                if (AutoComputeLeakFrequency)
                    return IogpFrequencyTable.TotalSourceFrequency(EquipmentInventory, HoleSizeBand);
                return LeakFrequencyPerYear;
            }
        }

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
        /// Gets the effective mass release rate in kg/s.
        /// Returns the HP leak computed rate when HP leak is enabled, otherwise the manual rate.
        /// </summary>
        public double EffectiveReleaseRateKgPerS
        {
            get
            {
                if (HighPressureLeak != null)
                {
                    if (HighPressureLeak.SpecifyMassFlow)
                        return HighPressureLeak.SpecifiedMassFlowKgPerS;
                    if (HighPressureLeak.OrificeDiameterM > 0)
                    {
                        double mdot = Core.HighPressureLeakModel.MassFlowRate(HighPressureLeak);
                        if (mdot > 0) return mdot;
                    }
                }
                return ReleaseRateKgPerS;
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
                return EffectiveReleaseRateKgPerS * PuffIntervalS;
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
                if (HighPressureLeak != null)
                {
                    if (HighPressureLeak.SpecifyMassFlow)
                        return Core.HighPressureLeakModel.OrificeDiameterFromMassFlow(
                            HighPressureLeak, HighPressureLeak.SpecifiedMassFlowKgPerS);
                    if (HighPressureLeak.OrificeDiameterM > 0)
                        return HighPressureLeak.OrificeDiameterM;
                }
                if (StackDiameterM > 0) return StackDiameterM;
                return 0;
            }
        }

        /// <summary>
        /// Birch &amp; Schefer expanded diameter for CFD meshing of sonic releases.
        /// Returns the physical orifice diameter when there's no HP leak or the flow is subsonic,
        /// and the larger pseudo-source diameter otherwise (subsonic at atmospheric, ~100 m/s).
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public double ExpandedDiameterForCfdM
        {
            get
            {
                if (HighPressureLeak == null || !Core.HighPressureLeakModel.IsChoked(HighPressureLeak))
                    return EffectiveDiameterM;
                var (d, _, _) = Core.HighPressureLeakModel.ComputeExpandedSource(HighPressureLeak);
                return d > 0 ? d : EffectiveDiameterM;
            }
        }

        /// <summary>
        /// Velocity at the Birch expanded pseudo-source (subsonic, suitable for CFD).
        /// Returns <see cref="ComputedExitVelocity"/> for non-choked cases.
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public double ExpandedVelocityForCfdMS
        {
            get
            {
                if (HighPressureLeak == null || !Core.HighPressureLeakModel.IsChoked(HighPressureLeak))
                    return ComputedExitVelocity;
                var (_, v, _) = Core.HighPressureLeakModel.ComputeExpandedSource(HighPressureLeak);
                return v > 0 ? v : ComputedExitVelocity;
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
                double effRate = EffectiveReleaseRateKgPerS;
                if (diam <= 0 || effRate <= 0) return 0;

                double areaSimple = Math.PI * 0.25 * diam * diam;
                double molarMass = Gas != null && Gas.MolarMass > 0 ? Gas.MolarMass : 0.029;
                double rhoSimple = 101325.0 * molarMass / (8.314 * ExitTemperatureK);
                return effRate / (rhoSimple * areaSimple);
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
