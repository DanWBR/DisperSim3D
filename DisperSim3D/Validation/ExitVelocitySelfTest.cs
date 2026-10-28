using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// A source with no exit velocity takes rate / (ρ·area). Across the leak range
    /// (0.1–32 kg/s, 1–50 mm) that is often above the speed of sound — 0.1 kg/s of
    /// methane through 1 mm gives ~190 km/s — so it is capped at the sonic throat
    /// velocity. Checks the cap, that subsonic and explicit velocities are left
    /// alone, and the two places the old value broke: puffs thrown hundreds of
    /// metres, and an OpenFOAM time step sized for the impossible velocity.
    ///
    /// Run via <c>DisperSim3D.CLI --exit-velocity-selftest</c>.
    /// </summary>
    public static class ExitVelocitySelfTest
    {
        public sealed class Result
        {
            public string Name { get; }
            public bool Passed { get; }
            public string Detail { get; }
            public Result(string name, bool passed, string detail = "")
            {
                Name = name; Passed = passed; Detail = detail;
            }
            public override string ToString()
                => (Passed ? "PASS  " : "FAIL  ") + Name +
                   (string.IsNullOrEmpty(Detail) ? "" : "   " + Detail);
        }

        private const double AmbientT = 288.15;

        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();

            // Methane: cp 2200 J/kg/K, M 0.01604 → γ ≈ 1.31, throat velocity ≈ 411 m/s at 288 K.
            double sonic = Sonic(2200, 0.01604, AmbientT);

            foreach (var (rate, mm) in new[] { (0.1, 1.0), (0.1, 5.0), (32.0, 50.0) })
            {
                var src = Source(rate, mm / 1000, 0);
                double raw = rate / (101325.0 * 0.01604 / (8.314 * AmbientT) * Math.PI * 0.25 * Math.Pow(mm / 1000, 2));
                results.Add(new Result(Fmt("{0} kg/s through {1} mm is capped at the sonic velocity", rate, mm),
                    raw > sonic && Math.Abs(src.ComputedExitVelocity - sonic) < 1e-6,
                    Fmt("rate/(ρA) {0:F0} m/s, used {1:F1} m/s", raw, src.ComputedExitVelocity)));
            }

            var subsonic = Source(0.001, 0.050, 0);
            double expected = 0.001 / (101325.0 * 0.01604 / (8.314 * AmbientT) * Math.PI * 0.25 * 0.05 * 0.05);
            results.Add(new Result("a subsonic release keeps rate/(ρA)",
                Math.Abs(subsonic.ComputedExitVelocity - expected) < 1e-9,
                Fmt("{0:F3} m/s", subsonic.ComputedExitVelocity)));

            var explicitVelocity = Source(32, 0.050, 120);
            results.Add(new Result("an exit velocity that is set is used as it is",
                explicitVelocity.ComputedExitVelocity == 120));

            CheckPuff(results);
            CheckOpenFoamTimeStep(results, sonic);
            return results;
        }

        public static bool RunAndPrint(TextWriter writer)
        {
            IReadOnlyList<Result> results = Run();
            int passed = 0;
            foreach (Result r in results)
            {
                writer.WriteLine(r.ToString());
                if (r.Passed) passed++;
            }
            writer.WriteLine();
            writer.WriteLine(passed == results.Count
                ? $"OK — {passed}/{results.Count} exit velocity checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        /// <summary>The puffs of a 1 mm leak stay near the source instead of being thrown away.</summary>
        private static void CheckPuff(List<Result> results)
        {
            var scenario = Scenario(Source(0.1, 0.001, 0));
            var puff = new GaussianPuffEngine();
            puff.Initialize(scenario);
            puff.StepTo(60.0);
            double peak = 0;
            foreach (double z in new[] { 0.5, 1.5, 3.0 })
                for (double x = -150; x <= 150; x += 5)
                    for (double y = -150; y <= 150; y += 5)
                        peak = Math.Max(peak, puff.EvaluateConcentration(x, y, z));
            results.Add(new Result("Gaussian puff: a 0.1 kg/s, 1 mm leak shows up around the source",
                peak > 0 && !double.IsInfinity(peak), Fmt("peak {0:E3} kg/m3 within 150 m", peak)));
        }

        /// <summary>The OpenFOAM time step is sized for the sonic velocity, not 190 km/s.</summary>
        private static void CheckOpenFoamTimeStep(List<Result> results, double sonic)
        {
            string work = Path.Combine(Path.GetTempPath(), "dispersim-exitvel-" + Guid.NewGuid().ToString("N"));
            try
            {
                var scenario = Scenario(Source(0.1, 0.001, 0));
                string caseDir = OpenFoamCaseGenerator.GenerateRhoReactingBuoyantFoam(
                    scenario, new CfdConfiguration { WorkingDirectory = work });
                string control = File.ReadAllText(Path.Combine(caseDir, "system", "controlDict"));
                Match m = Regex.Match(control, @"deltaT\s+([0-9.eE+-]+);");
                double dt = m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : double.NaN;
                double cell = 2.0 * scenario.DomainSizeM / scenario.GridResolution;
                double expected = 0.3 * cell / sonic;
                results.Add(new Result("OpenFOAM: the first time step follows the sonic velocity",
                    m.Success && Math.Abs(dt - expected) <= 1e-3 * expected, // as written in controlDict
                    Fmt("deltaT {0} s (0.3·{1} m / {2:F0} m/s = {3:G6})", m.Success ? m.Groups[1].Value : "?", cell, sonic, expected)));
            }
            catch (Exception ex)
            {
                results.Add(new Result("OpenFOAM: case written", false, ex.GetType().Name + ": " + ex.Message));
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { /* best effort */ }
            }
        }

        private static double Sonic(double cp, double molarMass, double t)
        {
            double r = 8.314 / molarMass;
            double gamma = cp / (cp - r);
            return Math.Sqrt(gamma * r * t * 2.0 / (gamma + 1));
        }

        private static ReleaseSource3D Source(double rate, double diameter, double velocity) => new ReleaseSource3D
        {
            Name = "leak",
            Gas = GasProperties.CreateMethane(),
            Position = new Point3D(0, 0, 0),
            ReleaseHeightOffset = 1.5,
            ReleaseRateKgPerS = rate,
            StackDiameterM = diameter,
            ExitVelocityMPerS = velocity,
            ExitTemperatureK = AmbientT,
            ReleaseDurationS = 600
        };

        private static DispersionScenario Scenario(ReleaseSource3D source)
        {
            var scenario = new DispersionScenario
            {
                Id = "exitvel-selftest",
                Name = "exit velocity self-test",
                Meteo = new MeteorologicalConditions
                {
                    WindSpeed = 3,
                    WindDirectionDeg = 45,
                    StabilityClass = PasquillStabilityClass.D,
                    AmbientTemperature = AmbientT,
                    AmbientPressure = 101325,
                    WindMeasurementHeightM = 10,
                    RoughnessLengthM = 0.03,
                    WindShearExponent = -1
                },
                DomainSizeM = 150,
                GridResolution = 20,
                SimulationDurationS = 60,
                TimeStepS = 1
            };
            scenario.Sources.Add(source);
            return scenario;
        }

        private static string Fmt(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
