using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// In-memory representation of a <c>.fbench</c> file — the fire counterpart of
    /// <see cref="BenchmarkSpec"/>. A fire benchmark describes a published fire test and
    /// what the literature reports about it, in two independent halves:
    ///
    /// <list type="number">
    ///   <item><b>Flame geometry and emissive power</b> — flame length, flame diameter,
    ///     surface emissive power. These are widely reproduced summary numbers, and they
    ///     exercise the front half of the solid-flame model: correlations and SEP.</item>
    ///   <item><b>Incident flux at radiometers</b> — measured kW/m² at known positions.
    ///     These exercise the back half: view factor and atmospheric transmissivity.</item>
    /// </list>
    ///
    /// The halves are separate on purpose. A benchmark that only pins the geometry is
    /// still worth running, and a model can pass the first and fail the second — which
    /// is exactly the diagnosis you want when it happens.
    ///
    /// Same rule as the dispersion benches: only published-summary numerics from the
    /// cited source, never raw experimental data files.
    /// </summary>
    public class FireBenchmarkSpec
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = "fbench/v1";

        public string Name { get; set; }

        /// <summary>Full citation of the source the numbers came from.</summary>
        public string Citation { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// How much the numbers in this file can be trusted. <c>High</c> means they were
        /// read from a table in the cited source; <c>Medium</c> means they were read off
        /// a figure or a secondary citation; <c>Unverified</c> means they have not been
        /// checked against the source at all.
        ///
        /// A run of an <c>Unverified</c> bench is reported but never counted as a pass:
        /// a green tick against a number nobody confirmed is worse than no test.
        /// </summary>
        public string DataConfidence { get; set; } = "Unverified";

        public FireBenchmarkFire Fire { get; set; }
        public FireBenchmarkAmbient Ambient { get; set; }

        /// <summary>What the source reports about the flame itself. Any field left null
        /// is simply not checked.</summary>
        public FireBenchmarkFlame ExpectedFlame { get; set; }

        /// <summary>Radiometer positions and their measured incident flux.</summary>
        public List<FireBenchmarkRadiometer> Radiometers { get; set; }
            = new List<FireBenchmarkRadiometer>();

        public FireBenchmarkAcceptance Acceptance { get; set; } = new FireBenchmarkAcceptance();
    }

    /// <summary>The fire as the source describes it.</summary>
    public class FireBenchmarkFire
    {
        /// <summary>"pool" or "jet".</summary>
        public string Kind { get; set; } = "pool";

        /// <summary>Fuel name, for the report only.</summary>
        public string Fuel { get; set; }

        /// <summary>Pool diameter (m). Pool fires only.</summary>
        public double PoolDiameterM { get; set; }

        /// <summary>Surface regression rate (kg/m²/s). Pool fires only.</summary>
        public double BurnRateKgM2S { get; set; }

        /// <summary>Fuel mass flow (kg/s). Jet fires only; derived from the pool
        /// diameter and burn rate when left at zero for a pool.</summary>
        public double MassFlowRateKgS { get; set; }

        /// <summary>Orifice diameter (m). Jet fires only.</summary>
        public double OrificeDiameterM { get; set; }

        /// <summary>Heat of combustion (J/kg).</summary>
        public double HeatOfCombustionJKg { get; set; } = 50e6;

        /// <summary>Fraction of the heat release radiated away.</summary>
        public double RadiativeFraction { get; set; } = 0.2;

        /// <summary>Fuel molar mass (kg/mol), for the exit velocity behind the wind tilt.</summary>
        public double FuelMolarMassKgMol { get; set; } = 0.016;

        /// <summary>Whether the fuel burns sooty. False for LNG, LPG and hydrogen.</summary>
        public bool IsSootyFuel { get; set; } = true;

        /// <summary>Base of the flame in scene coordinates. Radiometer positions are in
        /// the same frame.</summary>
        public double[] Position { get; set; } = { 0, 0, 0 };

        /// <summary>Jet axis. Ignored for a pool fire, whose flame is vertical.</summary>
        public double[] Direction { get; set; } = { 1, 0, 0 };
    }

    public class FireBenchmarkAmbient
    {
        public double WindSpeed { get; set; }
        public double WindDirectionDeg { get; set; } = 270;
        public double AmbientTemperatureK { get; set; } = 293.15;
        public double AmbientPressurePa { get; set; } = 101325;

        /// <summary>Relative humidity as a fraction (0–1). Drives the transmissivity, so
        /// a bench that reports flux without reporting humidity is checking two things at
        /// once — say so in the description when that is the case.</summary>
        public double RelativeHumidity { get; set; } = 0.5;
    }

    /// <summary>What the source reports about the flame. Null fields are not checked.</summary>
    public class FireBenchmarkFlame
    {
        public double? LengthM { get; set; }
        public double? DiameterM { get; set; }
        public double? SurfaceEmissivePowerKwM2 { get; set; }

        /// <summary>Free-text note on how these were reported (mean over the flame, base
        /// value, range, and so on).</summary>
        public string Note { get; set; }
    }

    public class FireBenchmarkRadiometer
    {
        public string Name { get; set; }

        /// <summary>Position in the same frame as the fire.</summary>
        public double[] Position { get; set; } = { 0, 0, 0 };

        /// <summary>Measured incident flux (kW/m²).</summary>
        public double MeasuredKwM2 { get; set; }

        /// <summary>Receiver orientation the instrument had: "MaxOriented" (the default,
        /// for a radiometer aimed at the flame), "Horizontal" or "Vertical".</summary>
        public string ReceiverMode { get; set; } = "MaxOriented";
    }

    /// <summary>
    /// Acceptance is a ratio band, predicted over observed. The defaults are the FAC2
    /// convention the dispersion side already uses — within a factor of two — which is
    /// the usual bar for a screening radiation model against field data.
    /// </summary>
    public class FireBenchmarkAcceptance
    {
        public double FlameLengthRatioMin { get; set; } = 0.5;
        public double FlameLengthRatioMax { get; set; } = 2.0;

        public double FlameDiameterRatioMin { get; set; } = 0.5;
        public double FlameDiameterRatioMax { get; set; } = 2.0;

        public double SepRatioMin { get; set; } = 0.5;
        public double SepRatioMax { get; set; } = 2.0;

        public double FluxRatioMin { get; set; } = 0.5;
        public double FluxRatioMax { get; set; } = 2.0;

        /// <summary>Fraction of radiometers that must land inside the flux band for the
        /// bench to pass. 1.0 demands every one of them.</summary>
        public double MinFluxFac2Fraction { get; set; } = 0.8;
    }
}
