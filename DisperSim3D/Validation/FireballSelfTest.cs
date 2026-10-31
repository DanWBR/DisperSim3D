using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// The analytic fireball (<see cref="FireballModel"/>) against the CCPS correlations
    /// worked by hand, and its place in the radiation field, the dose (exposure cut to
    /// the burn duration), the fire study and the fire bench.
    ///
    /// Run via <c>DisperSim3D.CLI --fireball-selftest</c>.
    /// </summary>
    public static class FireballSelfTest
    {
        public static bool RunAndPrint(TextWriter w)
        {
            int passed = 0, total = 0;
            var inv = CultureInfo.InvariantCulture;
            void Check(string name, bool ok, string detail = "")
            {
                total++; if (ok) passed++;
                w.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
            }
            bool Near(double a, double b, double rel = 1e-9) => Math.Abs(a - b) <= rel * Math.Max(1e-12, Math.Abs(b));

            try
            {
                // ── Correlations ──
                Check("diameter 5.8·M^(1/3): 1000 kg → 58 m", Near(FireballModel.DiameterM(1000), 58.0));
                Check("duration 0.45·M^(1/3) below 30 t: 1000 kg → 4.5 s", Near(FireballModel.DurationS(1000), 4.5));
                Check("duration 2.6·M^(1/6) above 30 t: 100 t → 17.71 s",
                    Near(FireballModel.DurationS(100000), 2.6 * Math.Pow(100000, 1.0 / 6.0)) && Math.Abs(FireballModel.DurationS(100000) - 17.71) < 0.01,
                    FireballModel.DurationS(100000).ToString("F3", inv) + " s");
                Check("centre height 0.75·D: 1000 kg → 43.5 m", Near(FireballModel.CentreHeightM(1000), 43.5));

                var ball = new FireSource
                {
                    Name = "ball", Kind = FireKind.Fireball, FireballMassKg = 1000, HeatOfCombustionJKg = 46e6,
                    RadiativeFraction = 0.3, Position = new Point3D(0, 0, 0)
                };
                double sep = 0.3 * 1000 * 46e6 / (Math.PI * 58.0 * 58.0 * 4.5) / 1000.0;
                Check("emissive power F·M·ΔHc/(π·D²·t)", Near(FireballModel.SepKwM2(ball), sep),
                    FireballModel.SepKwM2(ball).ToString("F1", inv) + " kW/m²");
                var pressurised = new FireSource { Kind = FireKind.Fireball, StagnationPressurePa = 1.5e6, RadiativeFraction = 0.2 };
                Check("Roberts' radiative fraction 0.27·P^0.32 from the vessel pressure",
                    Near(FireballModel.RadiativeFraction(pressurised), 0.27 * Math.Pow(1.5, 0.32)));
                Check("a set emissive power wins", FireballModel.SepKwM2(new FireSource { Kind = FireKind.Fireball, SepKwM2 = 200 }) == 200);

                // ── Flux, dry air so τ = 1 ──
                var receiver = new Point3D(100, 0, 0);
                double l = Math.Sqrt(100 * 100 + 43.5 * 43.5);
                double faceOn = sep * 29.0 * 29.0 / (l * l);
                double qMax = FireballModel.FluxKwM2(ball, receiver, ReceiverMode.MaxOriented, 293.15, 0);
                Check("face-on flux E·(D/2)²/L² at 100 m", Near(qMax, faceOn), string.Format(inv, "{0:F2} kW/m²", qMax));
                Check("a vertical receiver gets cos θ of it",
                    Near(FireballModel.FluxKwM2(ball, receiver, ReceiverMode.Vertical, 293.15, 0), faceOn * 100 / l));
                Check("a horizontal receiver gets cos θ of it",
                    Near(FireballModel.FluxKwM2(ball, receiver, ReceiverMode.Horizontal, 293.15, 0), faceOn * 43.5 / l));
                Check("humid air attenuates",
                    FireballModel.FluxKwM2(ball, receiver, ReceiverMode.MaxOriented, 293.15, 0.7) < qMax);
                Check("inside the fireball the flux is its emissive power",
                    Near(FireballModel.FluxKwM2(ball, new Point3D(0, 0, 40), ReceiverMode.MaxOriented, 293.15, 0), sep));

                // ── In the scene ──
                var scene = new Scene3D();
                scene.WindFieldScenarios.Add(new WindFieldScenario
                {
                    Name = "dry", Meteo = new MeteorologicalConditions { WindSpeed = 2, AmbientTemperature = 293.15, RelativeHumidity = 0 }
                });
                scene.FireScenario.ExposureTimeS = 20;
                scene.FireScenario.Sources.Add(ball);
                Check("the radiation at a point comes from the fireball",
                    Near(FieldTransform.RadiationAtPoint(scene, 100, 0, 0), qMax));
                double doseBall = ThermalDose.Dose(qMax, 4.5);
                Check("its dose takes the burn duration, not the 20 s escape time",
                    Near(FieldTransform.AnalyticAtPoint(scene, ViewFieldProperty.ThermalDose, 100, 0, 0), doseBall));

                // One cell of a 20-cell grid over ±200 m: centre (110, 10, 30) with dz = 2·half/nz.
                var field = FieldTransform.BuildRadiationField(scene, 20, 20, 10, 200);
                var cell = new Point3D(-200 + 15.5 * 20, -200 + 10.5 * 20, 0.5 * 40);
                double qCell = FireballModel.FluxKwM2(ball, cell, ReceiverMode.MaxOriented, 293.15, 0);
                Check("the radiation field holds the fireball", Near(field[15, 10, 0], qCell),
                    string.Format(inv, "{0:F3} kW/m²", field[15, 10, 0]));
                var fatality = FieldTransform.BuildAnalyticField(scene, ViewFieldProperty.FatalityProbability, 20, 20, 10, 200);
                Check("the fatality field uses the fireball's duration",
                    Near(fatality[15, 10, 0], ThermalDose.ProbitToProbability(ThermalDose.FatalityProbit(ThermalDose.Dose(qCell, 4.5)))));

                var jet = new FireSource { Name = "jet", Kind = FireKind.Jet, MassFlowRateKgS = 2, Position = new Point3D(-50, 0, 1), RadiationModel = RadiationModel.PointSource };
                scene.FireScenario.Sources.Add(jet);
                double qJet = FieldTransform.RadiationAtPoint(new Scene3D
                {
                    WindFieldScenarios = scene.WindFieldScenarios,
                    FireScenario = new FireScenario { Sources = new List<FireSource> { jet } }
                }, 100, 0, 0);
                Check("with a jet beside it, each fire's dose takes its own exposure",
                    Near(FieldTransform.AnalyticAtPoint(scene, ViewFieldProperty.ThermalDose, 100, 0, 0),
                         ThermalDose.Dose(qJet, 20) + doseBall));
                scene.FireScenario.Sources.Remove(jet);

                // ── Fire study ──
                var study = new FireStudy { HarmQuantity = ViewFieldProperty.FatalityProbability, HarmThreshold = 0.01, DomainHalfM = 150, GridResolution = 30 };
                study.FireSourceIds.Add(ball.Id);
                var row = FireStudyEngine.Evaluate(scene, study).Rows.Single();
                Check("the fire study scores a fireball", row.Kind == FireStudyEngine.ScenarioKind.Fireball && row.ConsequenceM3 > 0,
                    string.Format(inv, "{0:F0} m³", row.ConsequenceM3));

                // ── Fire bench ──
                string bench = Path.Combine(Path.GetTempPath(), "dsim_fireball_" + Guid.NewGuid().ToString("N") + ".fbench");
                File.WriteAllText(bench, BenchJson);
                try
                {
                    var r = FireBenchmarkRunner.Run(FireBenchmarkRunner.Load(bench));
                    var byName = r.Checks.ToDictionary(c => c.Name);
                    Check("a fireball bench checks diameter, emissive power, duration and flux on the model",
                        byName.TryGetValue("fireball diameter", out var d) && Near(d.Predicted, 58)
                        && byName.TryGetValue("surface emissive power", out var s) && Near(s.Predicted, sep)
                        && byName.TryGetValue("fireball duration", out var t) && Near(t.Predicted, 4.5)
                        && byName.TryGetValue("flux @ r1", out var q) && Near(q.Predicted, qMax)
                        && !byName.ContainsKey("flame length"),
                        string.Join(", ", r.Checks.Select(c => c.Name)));
                }
                finally { try { File.Delete(bench); } catch { } }
            }
            catch (Exception ex)
            {
                Check("no exception", false, ex.GetType().Name + ": " + ex.Message);
            }

            w.WriteLine();
            w.WriteLine(passed == total
                ? $"OK — {passed}/{total} fireball checks passed."
                : $"FAIL — {passed}/{total} passed.");
            return passed == total;
        }

        private const string BenchJson = @"{
  ""$schema"": ""fbench/v1"",
  ""name"": ""fireball-selftest"",
  ""dataConfidence"": ""Unverified"",
  ""fire"": { ""kind"": ""fireball"", ""fuel"": ""propane"", ""fireballMassKg"": 1000,
            ""heatOfCombustionJKg"": 46000000, ""radiativeFraction"": 0.3, ""position"": [0, 0, 0] },
  ""ambient"": { ""windSpeed"": 2, ""relativeHumidity"": 0 },
  ""expectedFlame"": { ""lengthM"": 50, ""diameterM"": 58, ""surfaceEmissivePowerKwM2"": 290, ""durationS"": 4.5 },
  ""radiometers"": [ { ""name"": ""r1"", ""position"": [100, 0, 0], ""measuredKwM2"": 10 } ]
}";
    }
}
