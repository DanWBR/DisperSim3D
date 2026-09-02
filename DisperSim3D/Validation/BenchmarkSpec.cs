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

        /// <summary>
        /// Where the observed values in this file came from, which decides what a pass
        /// is worth. Mirrors the contract the fire suite has used since it was written.
        ///
        /// <list type="bullet">
        ///   <item><c>High</c> — read from a table in the cited source.</item>
        ///   <item><c>Medium</c> — read off a figure, or from a secondary citation.</item>
        ///   <item><c>RegressionBaseline</c> — captured from this engine's own
        ///     last-known-good output, because the primary data is restricted or not
        ///     yet digitised. Guards against silent drift; says nothing about whether
        ///     the model matches the world.</item>
        ///   <item><c>SelfConsistency</c> — the engine against its own analytical
        ///     solution. Catches numerical regression; not evidence about physics.</item>
        ///   <item><c>Unverified</c> — not checked against any source.</item>
        /// </list>
        ///
        /// <para>An <c>Unverified</c> bench is evaluated and printed but never counted
        /// as a pass. A green tick against numbers nobody confirmed is worse than
        /// having no test. The other levels all count, but they do not mean the same
        /// thing, and a headline that adds them together says less than it appears
        /// to — which is why the runner reports the split.</para>
        /// </summary>
        public string DataConfidence { get; set; } = "Unverified";

        public BenchmarkSource Source { get; set; }
        public BenchmarkMeteo Meteo { get; set; }
        public BenchmarkDomain Domain { get; set; }

        /// <summary>Optional obstacle-array specification. When set, the runner
        /// adds the corresponding boxes to <c>Scene3D.Decorations</c> before
        /// running the solver, so both the wind-field LBM and the tracer engine
        /// see the obstacles.</summary>
        public BenchmarkObstacleArray ObstacleArray { get; set; }

        /// <summary>Optional reference SPMs from a published validated model
        /// (typically FLACS or PHAST) on the SAME experiment, as cited in a
        /// peer-reviewed paper. When set, the validation harness compares
        /// DisperSim 3D's SPMs against these reference numbers: the bench
        /// PASSES when DisperSim is no worse than the reference within the
        /// declared <see cref="BenchmarkAcceptance.ReferenceMatchTolerance"/>.
        /// This is the right way to validate a dispersion engine: we are not
        /// asking the engine to match the field measurements perfectly (which
        /// is rarely possible for a Gaussian or simple CFD model), but to
        /// reach the same level of agreement that commercial models reach
        /// against the same data.</summary>
        public BenchmarkReferenceModelSpms ReferenceModelSpms { get; set; }

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
            // Only rhoReactingBuoyantFoam survives among the OpenFOAM
            // solvers and writes the CH4 species mass fraction. FluidX3D
            // and analytical Gaussian engines use T as the concentration
            // proxy.
            return ResolveSolverType() == CfdSolverType.RhoReactingBuoyantFoam
                ? "CH4"
                : "T";
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

    /// <summary>Hanna SPMs that a published reference model (FLACS, PHAST,
    /// or similar) achieved on the same experiment, as cited in a validation
    /// paper. Used by the validation harness to define what "good enough"
    /// means for this bench: the engine PASSES when its SPMs are no worse
    /// than these reference numbers within the tolerance declared in
    /// <see cref="BenchmarkAcceptance.ReferenceMatchTolerance"/>.</summary>
    public class BenchmarkReferenceModelSpms
    {
        /// <summary>Name of the reference model and its version, e.g.
        /// "FLACS v9.1 r2", "PHAST v8.1", "ANSYS-CFX".</summary>
        public string Model { get; set; }

        /// <summary>Full citation of the paper that reports these SPMs.</summary>
        public string Citation { get; set; }

        public double? MRB { get; set; }
        public double? RMSE { get; set; }
        public double? FAC2 { get; set; }
        public double? MG { get; set; }
        public double? VG { get; set; }

        /// <summary>Optional notes (e.g. "aggregate over 43 Prairie Grass trials"
        /// when the paper only reports cohort-level statistics).</summary>
        public string Notes { get; set; }
    }

    /// <summary>Parametric obstacle-array specification for built-environment
    /// benchmarks (such as MUST). When <see cref="Type"/> is "must" the runner
    /// generates the 120-container array via <see cref="Core.MustGeometryBuilder"/>.
    /// Other types may be added later (regular grid, single block, custom CAD path).</summary>
    public class BenchmarkObstacleArray
    {
        /// <summary>Array type. Currently supported: "must".</summary>
        public string Type { get; set; }

        public int Rows { get; set; } = 12;
        public int Columns { get; set; } = 10;
        public double SpacingAlongWindM { get; set; } = 12.9;
        public double SpacingCrosswindM { get; set; } = 12.9;

        public double ContainerLengthM { get; set; } = 12.2;
        public double ContainerWidthM { get; set; } = 2.42;
        public double ContainerHeightM { get; set; } = 2.54;

        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double GroundZ { get; set; }
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
