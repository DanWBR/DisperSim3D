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
