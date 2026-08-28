using System;
using System.Collections.Generic;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Checks for <see cref="ThermalDose"/>: the error function against tabulated
    /// values, the probits against their published anchors, and the fields against the
    /// scalar functions they vectorise.
    ///
    /// The anchors are the point of this test. A probit is a fit, and a fit with the
    /// wrong unit or a transcribed constant still returns plausible-looking numbers —
    /// the only way to catch that is to check it where the literature pins it: 20 s at
    /// roughly 18 kW/m² is 1% lethality, and at roughly 36 kW/m² it is 50%.
    ///
    /// Run via <c>DisperSim3D.CLI --thermal-dose-selftest</c>.
    /// </summary>
    public static class ThermalDoseSelfTest
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

        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();

            // ── Error function ──────────────────────────────────────────────
            {
                // Compared against the true erf, not a rounded table: the A&S 7.1.26
                // approximation is specified to |error| <= 1.5e-7, and at x = 2 it sits
                // at 1.25e-7 — close enough to the bound that a 7-digit literal on the
                // expected side would fail on its own rounding.
                var cases = new (double X, double Expected)[]
                {
                    (0.0, 0.000000000),
                    (0.5, 0.520499878),
                    (1.0, 0.842700793),
                    (2.0, 0.995322265),
                    (3.0, 0.999977910)
                };
                foreach (var (x, expected) in cases)
                {
                    double actual = ThermalDose.Erf(x);
                    results.Add(new Result($"erf({x:F1})",
                        Math.Abs(actual - expected) < 1.5e-7,
                        $"expected={expected:F9} actual={actual:F9} err={Math.Abs(actual - expected):E2}"));
                }
                results.Add(new Result("erf is odd",
                    Math.Abs(ThermalDose.Erf(-1.3) + ThermalDose.Erf(1.3)) < 1e-12));
            }

            // ── Probit → probability ────────────────────────────────────────
            {
                double p50 = ThermalDose.ProbitToProbability(ThermalDose.Probit50Percent);
                results.Add(new Result("probit 5.0 is 50%",
                    Math.Abs(p50 - 0.5) < 1e-9, $"actual={p50:P3}"));

                double p1 = ThermalDose.ProbitToProbability(ThermalDose.Probit1Percent);
                results.Add(new Result("probit 2.67 is 1%",
                    Math.Abs(p1 - 0.01) < 5e-4, $"actual={p1:P3}"));

                results.Add(new Result("probability is clamped to [0, 1]",
                    ThermalDose.ProbitToProbability(-50) >= 0
                    && ThermalDose.ProbitToProbability(50) <= 1));

                results.Add(new Result("zero dose is zero probability",
                    ThermalDose.ProbitToProbability(ThermalDose.FatalityProbit(0)) == 0));
            }

            // ── Published anchors ───────────────────────────────────────────
            {
                const double exposure = 20.0;

                double flux1 = ThermalDose.FluxForFatalityProbit(ThermalDose.Probit1Percent, exposure);
                results.Add(new Result("1% lethality at 20 s is ~18 kW/m²",
                    flux1 > 15.0 && flux1 < 21.0, $"actual={flux1:F1} kW/m²"));

                double flux50 = ThermalDose.FluxForFatalityProbit(ThermalDose.Probit50Percent, exposure);
                results.Add(new Result("50% lethality at 20 s is ~36 kW/m²",
                    flux50 > 32.0 && flux50 < 40.0, $"actual={flux50:F1} kW/m²"));

                // The inverse has to land back on the probability it was asked for.
                double back1 = ThermalDose.FatalityProbability(flux1, exposure);
                double back50 = ThermalDose.FatalityProbability(flux50, exposure);
                results.Add(new Result("FluxForFatalityProbit inverts FatalityProbability",
                    Math.Abs(back1 - 0.01) < 1e-3 && Math.Abs(back50 - 0.5) < 1e-9,
                    $"1%→{back1:P2}, 50%→{back50:P2}"));

                // The three design levels the FireScenario ships with, at 20 s.
                double atPain = ThermalDose.FatalityProbability(4.0, exposure);
                double atWood = ThermalDose.FatalityProbability(12.5, exposure);
                double atSteel = ThermalDose.FatalityProbability(37.5, exposure);
                results.Add(new Result("4 kW/m² for 20 s is survivable",
                    atPain < 1e-4, $"P={atPain:E2}"));
                results.Add(new Result("12.5 kW/m² for 20 s is low but non-zero lethality",
                    atWood > 1e-4 && atWood < 0.05, $"P={atWood:P2}"));
                results.Add(new Result("37.5 kW/m² for 20 s is around half",
                    atSteel > 0.4 && atSteel < 0.7, $"P={atSteel:P1}"));

                // Second-degree burns must set in well below the fatality threshold.
                double burnProbit = ThermalDose.SecondDegreeBurnProbit(ThermalDose.Dose(12.5, exposure));
                double fatalProbit = ThermalDose.FatalityProbit(ThermalDose.Dose(12.5, exposure));
                results.Add(new Result("burns come before death at the same dose",
                    burnProbit > fatalProbit,
                    $"2nd-degree Y={burnProbit:F2} vs fatality Y={fatalProbit:F2}"));
            }

            // ── Dose behaviour ──────────────────────────────────────────────
            {
                results.Add(new Result("dose is zero without flux or without time",
                    ThermalDose.Dose(0, 20) == 0 && ThermalDose.Dose(12.5, 0) == 0));

                // V = t·I^(4/3): doubling the time doubles the dose, doubling the flux
                // multiplies it by 2^(4/3).
                double baseline = ThermalDose.Dose(10, 10);
                results.Add(new Result("dose is linear in time",
                    Math.Abs(ThermalDose.Dose(10, 20) / baseline - 2.0) < 1e-12,
                    $"ratio={ThermalDose.Dose(10, 20) / baseline:F6}"));
                results.Add(new Result("dose goes as flux^(4/3)",
                    Math.Abs(ThermalDose.Dose(20, 10) / baseline - Math.Pow(2, 4.0 / 3.0)) < 1e-9,
                    $"ratio={ThermalDose.Dose(20, 10) / baseline:F6} expected={Math.Pow(2, 4.0 / 3.0):F6}"));
            }

            // ── Fields ──────────────────────────────────────────────────────
            {
                var flux = new double[4, 3, 2];
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 3; j++)
                        for (int k = 0; k < 2; k++)
                            flux[i, j, k] = 5.0 * (i + 1);

                var dose = ThermalDose.BuildDoseField(flux, 30.0);
                var fatality = ThermalDose.BuildFatalityField(dose);

                bool matchesScalar = true;
                bool monotonic = true;
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 3; j++)
                        for (int k = 0; k < 2; k++)
                        {
                            if (Math.Abs(dose[i, j, k] - ThermalDose.Dose(flux[i, j, k], 30.0)) > 1e-6)
                                matchesScalar = false;
                            if (i > 0 && dose[i, j, k] <= dose[i - 1, j, k]) monotonic = false;
                        }
                results.Add(new Result("the dose field matches the scalar function", matchesScalar));
                results.Add(new Result("the dose field is monotonic in the flux field", monotonic));

                results.Add(new Result("the fatality field matches the probit",
                    Math.Abs(fatality[3, 0, 0]
                             - ThermalDose.ProbitToProbability(ThermalDose.FatalityProbit(dose[3, 0, 0]))) < 1e-12));

                // Per-cell exposure: cells the flame never reaches take no dose.
                var exposure = new double[4, 3, 2];
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 3; j++)
                        for (int k = 0; k < 2; k++)
                            exposure[i, j, k] = i < 2 ? 30.0 : FlashFireEngine.UnreachedArrivalS;

                var perCell = ThermalDose.BuildDoseField(flux, exposure);
                results.Add(new Result("unreached cells take no dose",
                    perCell[0, 0, 0] > 0 && perCell[3, 0, 0] == 0,
                    $"reached={perCell[0, 0, 0]:E2} unreached={perCell[3, 0, 0]:E2}"));

                double footprint = ThermalDose.FootprintVolumeM3(fatality, 1.0, 0.01);
                results.Add(new Result("the footprint counts only cells above the threshold",
                    footprint > 0 && footprint < 4 * 3 * 2,
                    $"volume={footprint:F0} m³ of {4 * 3 * 2}"));
            }

            // ── End to end through the scene ────────────────────────────────
            {
                var scene = new Scene3D();
                scene.FireScenario.ExposureTimeS = 30.0;
                scene.FireScenario.Sources.Add(new FireSource
                {
                    Name = "jet",
                    Position = new Point3D(0, 0, 2),
                    Direction = new Vector3D(1, 0, 0),
                    MassFlowRateKgS = 2.0,
                    OrificeDiameterM = 0.05,
                    HeatOfCombustionJKg = 50_000_000,
                    RadiativeFraction = 0.2,
                    RadiationModel = RadiationModel.SolidFlame
                });

                var radiation = FieldTransform.BuildAnalyticField(scene,
                    ViewFieldProperty.ThermalRadiationKwM2, 20, 20, 10, 60);
                var doseField = FieldTransform.BuildAnalyticField(scene,
                    ViewFieldProperty.ThermalDose, 20, 20, 10, 60);
                var fatalityField = FieldTransform.BuildAnalyticField(scene,
                    ViewFieldProperty.FatalityProbability, 20, 20, 10, 60);

                bool consistent = true;
                double peakFatality = 0;
                for (int i = 0; i < 20; i++)
                    for (int j = 0; j < 20; j++)
                        for (int k = 0; k < 10; k++)
                        {
                            double expectedDose = ThermalDose.Dose(radiation[i, j, k], 30.0);
                            if (Math.Abs(doseField[i, j, k] - expectedDose) > 1e-6 * Math.Max(1, expectedDose))
                                consistent = false;
                            if (fatalityField[i, j, k] > peakFatality) peakFatality = fatalityField[i, j, k];
                        }

                results.Add(new Result("BuildAnalyticField chains radiation → dose → fatality",
                    consistent));
                results.Add(new Result("the fatality field is non-trivial near a 100 MW jet",
                    peakFatality > 0.5, $"peak={peakFatality:P1}"));

                double atPoint = FieldTransform.AnalyticAtPoint(scene,
                    ViewFieldProperty.FatalityProbability, 0, 25, 2);
                double direct = ThermalDose.FatalityProbability(
                    FieldTransform.RadiationAtPoint(scene, 0, 25, 2), 30.0);
                results.Add(new Result("AnalyticAtPoint agrees with the scalar chain",
                    Math.Abs(atPoint - direct) < 1e-12,
                    $"point={atPoint:P3} direct={direct:P3}"));

                double consequence = RiskWeightHelper.AutoConsequenceFire(scene, 20, 20, 10, 60);
                results.Add(new Result("the fire consequence footprint is non-zero",
                    consequence > 0, $"volume={consequence:F0} m³"));
            }

            return results;
        }

        /// <summary>Prints results and returns <c>true</c> when every case passes.</summary>
        public static bool RunAndPrint(TextWriter writer)
        {
            var results = Run();
            int passed = 0;
            foreach (var r in results)
            {
                writer.WriteLine(r.ToString());
                if (r.Passed) passed++;
            }
            writer.WriteLine();
            writer.WriteLine(passed == results.Count
                ? $"OK — {passed}/{results.Count} thermal-dose checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }
    }
}
