using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Calm wind (0 m/s): every model runs at the lowest wind it accepts
    /// (<see cref="CalmWind"/>) and gives the same answer as a run at that speed —
    /// finite and non-zero — across the leak range DisperSim 3D is used for
    /// (0.1–32 kg/s, 1–50 mm). The OpenFOAM case is written with the minimum in the
    /// velocity inlet and the k/ε profiles alike. The project's own conditions stay
    /// at 0 m/s.
    ///
    /// Run via <c>DisperSim3D.CLI --calm-wind-selftest</c>.
    /// </summary>
    public static class CalmWindSelfTest
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

        // (rate kg/s, orifice m, exit velocity m/s, jet transition). The exit velocity is
        // always given: without one a source takes rate / (ρ·area), with no sonic cap.
        private static readonly (double Rate, double Orifice, double Velocity, bool Jet)[] Leaks =
        {
            (0.1, 0.001, 10, false),
            (0.1, 0.005, 300, true),
            (32.0, 0.050, 20, false),
            (32.0, 0.050, 350, true)
        };

        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();

            CheckCopy(results);
            foreach (var leak in Leaks)
            {
                string tag = string.Format(CultureInfo.InvariantCulture, "{0} kg/s, {1} mm{2}",
                    leak.Rate, leak.Orifice * 1000, leak.Jet ? ", jet" : "");
                CheckPlume(results, tag, leak);
                CheckPuff(results, tag, leak);
                CheckUnified(results, tag, leak);
            }
            CheckOpenFoam(results, atmosphericBl: false);
            CheckOpenFoam(results, atmosphericBl: true);
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
                ? $"OK — {passed}/{results.Count} calm wind checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        // ── checks ─────────────────────────────────────────────────────

        private static void CheckCopy(List<Result> results)
        {
            var calm = Meteo(0.0);
            var raised = CalmWind.AtLeast(calm, CalmWind.GaussianMinimumMPerS);
            var dir = raised.WindVector;
            results.Add(new Result("calm conditions are raised on a copy, in their own direction",
                calm.WindSpeed == 0 && !ReferenceEquals(calm, raised) &&
                Math.Abs(raised.WindSpeed - CalmWind.GaussianMinimumMPerS) < 1e-12 &&
                Math.Abs(dir.X - 0.5 * -Math.Sin(Math.PI / 4)) < 1e-9 && Math.Abs(dir.Y - 0.5 * -Math.Cos(Math.PI / 4)) < 1e-9,
                Fmt("project {0} m/s, model {1} m/s", calm.WindSpeed, raised.WindSpeed)));

            var windy = Meteo(3.0);
            results.Add(new Result("a wind above the minimum is used as it is",
                ReferenceEquals(windy, CalmWind.AtLeast(windy, CalmWind.GaussianMinimumMPerS))));
        }

        private static void CheckPlume(List<Result> results, string tag, (double Rate, double Orifice, double Velocity, bool Jet) leak)
        {
            (double, double) Peak(double wind)
            {
                var plume = new GaussianPlumeEngine();
                plume.Initialize(Scenario(wind, leak));
                return PeakOf(plume.EvaluateConcentration);
            }
            Compare(results, "Gaussian plume, " + tag, Peak(0.0), Peak(CalmWind.GaussianMinimumMPerS));
        }

        private static void CheckPuff(List<Result> results, string tag, (double Rate, double Orifice, double Velocity, bool Jet) leak)
        {
            (double, double) Peak(double wind)
            {
                var puff = new GaussianPuffEngine();
                puff.Initialize(Scenario(wind, leak));
                puff.StepTo(120.0);
                return PeakOf(puff.EvaluateConcentration);
            }
            Compare(results, "Gaussian puff, " + tag, Peak(0.0), Peak(CalmWind.GaussianMinimumMPerS));
        }

        private static void CheckUnified(List<Result> results, string tag, (double Rate, double Orifice, double Velocity, bool Jet) leak)
        {
            (double, double) Peak(double wind)
            {
                var udm = new UnifiedDispersionEngine();
                udm.Initialize(Scenario(wind, leak));
                return PeakOf(udm.EvaluateConcentration);
            }
            Compare(results, "unified model, " + tag, Peak(0.0), Peak(CalmWind.UnifiedMinimumMPerS));
        }

        private static void CheckOpenFoam(List<Result> results, bool atmosphericBl)
        {
            string label = "OpenFOAM case" + (atmosphericBl ? " (atmospheric BL)" : "");
            string work = Path.Combine(Path.GetTempPath(), "dispersim-calm-" + Guid.NewGuid().ToString("N"));
            try
            {
                var scenario = Scenario(0.0, (1.0, 0.025, 200, false));
                var config = new CfdConfiguration { WorkingDirectory = work, UseAtmosphericBL = atmosphericBl };
                string caseDir = OpenFoamCaseGenerator.GenerateRhoReactingBuoyantFoam(scenario, config);

                string u = File.ReadAllText(Path.Combine(caseDir, "0", "U"));
                string k = File.ReadAllText(Path.Combine(caseDir, "0", "k"));
                string eps = File.ReadAllText(Path.Combine(caseDir, "0", "epsilon"));
                string min = CalmWind.OpenFoamMinimumMPerS.ToString(CultureInfo.InvariantCulture);

                results.Add(new Result(label + ": the velocity field carries the minimum wind",
                    !u.Contains("internalField   uniform (0 0 0)") && !u.Contains("value           uniform (0 0 0)"),
                    FirstLine(u, "internalField")));
                if (atmosphericBl)
                {
                    bool consistent = u.Contains("Uref            " + min + ";") &&
                                      k.Contains("Uref            " + min + ";") &&
                                      eps.Contains("Uref            " + min + ";");
                    results.Add(new Result(label + ": U, k and ε inlets share Uref = " + min + " m/s",
                        consistent && !k.Contains("Uref            5;") && !eps.Contains("Uref            5;"),
                        FirstLine(u, "Uref") + " | " + FirstLine(k, "Uref") + " | " + FirstLine(eps, "Uref")));
                }
                results.Add(new Result(label + ": the project's wind stays at 0 m/s", scenario.Meteo.WindSpeed == 0.0));

                string windCase = OpenFoamCaseGenerator.GenerateWindCase(Scenario(0.0, (1.0, 0.025, 0, false)), config, null);
                string wu = File.ReadAllText(Path.Combine(windCase, "0", "U"));
                results.Add(new Result(label + ": the wind case carries the minimum wind",
                    !wu.Contains("internalField   uniform (0 0 0)"), FirstLine(wu, "internalField")));
            }
            catch (Exception ex)
            {
                results.Add(new Result(label + ": case written", false, ex.GetType().Name + ": " + ex.Message));
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { /* best effort */ }
            }
        }

        // ── helpers ────────────────────────────────────────────────────

        private static void Compare(List<Result> results, string name,
            (double Peak, double Sum) calm, (double Peak, double Sum) atMinimum)
        {
            static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0;
            static bool Same(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Abs(b));
            results.Add(new Result(name + ": calm runs as the minimum wind",
                Finite(calm.Peak) && Finite(calm.Sum) && Same(calm.Peak, atMinimum.Peak) && Same(calm.Sum, atMinimum.Sum),
                Fmt("0 m/s: peak {0:E4}, sum {1:E4}; minimum: peak {2:E4}, sum {3:E4}",
                    calm.Peak, calm.Sum, atMinimum.Peak, atMinimum.Sum)));
        }

        /// <summary>Highest value and sum on a 5 m grid around the source, up to 20 m high.</summary>
        private static (double Peak, double Sum) PeakOf(Func<double, double, double, double> concentration)
        {
            double peak = 0, sum = 0;
            foreach (double z in new[] { 0.5, 1.5, 3.0, 6.0, 12.0, 20.0 })
                for (double x = -150; x <= 150; x += 5)
                    for (double y = -150; y <= 150; y += 5)
                    {
                        double c = concentration(x, y, z);
                        if (double.IsNaN(c) || double.IsInfinity(c)) return (c, c);
                        if (c > peak) peak = c;
                        sum += c;
                    }
            return (peak, sum);
        }

        private static MeteorologicalConditions Meteo(double wind) => new MeteorologicalConditions
        {
            WindSpeed = wind,
            WindDirectionDeg = 45,
            StabilityClass = PasquillStabilityClass.F,
            AmbientTemperature = 288.15,
            AmbientPressure = 101325,
            WindMeasurementHeightM = 10,
            RoughnessLengthM = 0.03,
            WindShearExponent = -1
        };

        private static DispersionScenario Scenario(double wind, (double Rate, double Orifice, double Velocity, bool Jet) leak)
        {
            var scenario = new DispersionScenario
            {
                Id = "calm-selftest",
                Name = "calm wind self-test",
                Meteo = Meteo(wind),
                DomainSizeM = 150,
                GridResolution = 20,
                SimulationDurationS = 120,
                TimeStepS = 1
            };
            scenario.Sources.Add(new ReleaseSource3D
            {
                Name = "leak",
                Gas = GasProperties.CreateMethane(),
                Position = new Point3D(0, 0, 0),
                ReleaseHeightOffset = 1.5,
                ReleaseRateKgPerS = leak.Rate,
                StackDiameterM = leak.Orifice,
                ExitVelocityMPerS = leak.Velocity,
                UseJetTransition = leak.Jet,
                ReleaseDurationS = 600,
                // At ambient temperature: a warmer source rises out of the sampled layer.
                ExitTemperatureK = 288.15
            });
            return scenario;
        }

        private static string FirstLine(string text, string key)
        {
            foreach (string line in text.Split('\n'))
                if (line.Contains(key)) return line.Trim();
            return "(no " + key + ")";
        }

        private static string Fmt(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
