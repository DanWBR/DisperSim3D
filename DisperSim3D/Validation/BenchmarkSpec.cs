using System.Collections.Generic;
using System.Text.Json.Serialization;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// In-memory representation of a .dsbench file. A benchmark describes:
    /// - the recipe to run (source, gas, meteo, domain, solver)
    /// - the sensor positions and their published-numerical observed concentrations
    /// - acceptance ranges per Hanna SPM metric
    ///
    /// Only published-summary numerics from the cited paper are stored — never raw experimental
    /// data files. This keeps redistribution within fair-use bounds.
    /// </summary>
    public class BenchmarkSpec
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = "dsbench/v1";

        public string Name { get; set; }
        public string Citation { get; set; }
        public string Description { get; set; }

        public BenchmarkSource Source { get; set; }
        public BenchmarkMeteo Meteo { get; set; }
        public BenchmarkDomain Domain { get; set; }

        /// <summary>
        /// String matching <see cref="CfdSolverType"/> values (case-insensitive).
        /// E.g. "GaussianPlume", "GaussianPuff", "RhoReactingBuoyantFoam".
        /// </summary>
        public string Solver { get; set; }

        public List<BenchmarkSensor> Sensors { get; set; } = new List<BenchmarkSensor>();

        /// <summary>
        /// "PeakOverTime" (transient: keep cell max over all timesteps) or
        /// "FinalSnapshot" (steady: use last timestep only).
        /// </summary>
        public string ConcentrationKind { get; set; } = "PeakOverTime";

        /// <summary>
        /// Unit in which both <see cref="BenchmarkSensor.MeasuredKgM3"/> and the engine's
        /// returned value are interpreted by SPM. "KgPerM3" (default), "MoleFraction", or
        /// "MassFraction" — see <see cref="SensorUnit"/>.
        /// </summary>
        public string Unit { get; set; } = "KgPerM3";

        /// <summary>
        /// OpenFOAM field name to sample at sensor positions (e.g. "CH4" for Burro LNG,
        /// "SF6" for DAT632, "s" for passive-scalar solvers, "T" for thermal benches).
        /// Defaults are inferred per solver when null.
        /// </summary>
        public string ConcentrationField { get; set; }

        /// <summary>
        /// Expected flammable cloud volume (m³) from the cited paper or regression baseline.
        /// When set, the runner computes flammable volume (LFL ≤ c ≤ UFL) and checks it
        /// against <see cref="BenchmarkAcceptance.CloudVolumeRatio"/>.
        /// </summary>
        public double? ExpectedCloudVolumeM3 { get; set; }

        public BenchmarkAcceptance Acceptance { get; set; }

        /// <summary>Default field name for the chosen solver when <see cref="ConcentrationField"/> is null.</summary>
        public string ResolveConcentrationField()
        {
            if (!string.IsNullOrEmpty(ConcentrationField)) return ConcentrationField;
            switch (ResolveSolverType())
            {
                case CfdSolverType.RhoReactingBuoyantFoam:
                case CfdSolverType.ReactingFoam:
                    return "CH4";
                case CfdSolverType.ScalarTransportFoam:
                case CfdSolverType.ScalarTransportFoamSteady:
                case CfdSolverType.ScalarSimpleFoam:
                case CfdSolverType.PimpleFoam:
                case CfdSolverType.BuoyantPimpleFoam:
                    return "s";
                default:
                    return "T";
            }
        }

        public CfdSolverType ResolveSolverType()
        {
            CfdSolverType t;
            if (System.Enum.TryParse(Solver ?? "GaussianPlume", ignoreCase: true, out t)) return t;
            return CfdSolverType.GaussianPlume;
        }
    }

    public class BenchmarkSource
    {
        public string Name { get; set; }
        public BenchmarkGas Gas { get; set; }
        /// <summary>Position [x, y, z] in metres.</summary>
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };
        public double ReleaseRateKgPerS { get; set; }
        public double ReleaseDurationS { get; set; }
        public double StackDiameterM { get; set; }
        public double ExitTemperatureK { get; set; }
        public double ExitVelocityMPerS { get; set; }

        /// <summary>Optional two-phase pressurized-release specification. When present,
        /// <see cref="TwoPhase"/>.<see cref="BenchmarkTwoPhase.Enabled"/> may be true to
        /// have <see cref="ValidationRunner"/> pre-process the source via
        /// <see cref="Core.TwoPhaseSourceCalculator"/>: the dispersion engine then sees
        /// only the vapor mass flow (with rainout subtracted) at the Birch pseudo-source
        /// geometry, instead of the raw total mass flow.</summary>
        public BenchmarkTwoPhase TwoPhase { get; set; }
    }

    /// <summary>Pressurized two-phase release recipe (Cl2/NH3/CO2 liquid storage etc.).
    /// Vessel state + orifice → mass flow + vapor fraction (Clapeyron) → vapor source.</summary>
    public class BenchmarkTwoPhase
    {
        /// <summary>When true the runner replaces <see cref="BenchmarkSource.ReleaseRateKgPerS"/>
        /// and friends with the flash-corrected vapor source before invoking the engine.</summary>
        public bool Enabled { get; set; }

        /// <summary>Compound name as recognised by the built-in flash table
        /// (e.g. "Carbon dioxide", "Ammonia", "Chlorine", "Methane", "Propane").
        /// Defaults to <see cref="BenchmarkGas.Name"/> when null.</summary>
        public string CompoundName { get; set; }

        public double VesselPressurePa { get; set; } = 1e6;
        public double VesselTemperatureK { get; set; } = 293.15;
        public double OrificeDiameterM { get; set; } = 0.025;
        public double DischargeCoefficient { get; set; } = 0.65;

        /// <summary>Birch &amp; Schefer pseudo-source target velocity (default 100 m/s).</summary>
        public double TargetExpandedVelocityMS { get; set; } = 100.0;
    }

    public class BenchmarkGas
    {
        public string Name { get; set; }
        public double MolarMass { get; set; }
        public double Lfl { get; set; }
        public double Ufl { get; set; }
        public bool IsCryogenic { get; set; }
    }

    public class BenchmarkMeteo
    {
        public double WindSpeed { get; set; }
        public double WindDirectionDeg { get; set; }
        public string Stability { get; set; } = "D";
        public double AmbientTemperature { get; set; } = 293.15;
        public double AmbientPressure { get; set; } = 101325;
        public double WindMeasurementHeightM { get; set; } = 10;
        public double RoughnessLengthM { get; set; } = 0.03;
    }

    public class BenchmarkDomain
    {
        public double SizeM { get; set; } = 1000;
        public int GridResolution { get; set; } = 60;
        public double DurationS { get; set; } = 200;
        public double TimeStepS { get; set; } = 1.0;
    }
}
