using System;
using System.Collections.Generic;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Checks for <see cref="FireStudyEngine"/> and the <see cref="FireStudy"/> round
    /// trip.
    ///
    /// The scoring cases build fire sources whose relative severity is known by
    /// construction — a bigger release radiates further, so it must own a bigger
    /// footprint and, at equal frequency, more of the risk. That ordering is the
    /// property a study exists to produce, and it is what breaks if the footprint
    /// integration or the ranking regresses.
    ///
    /// Run via <c>DisperSim3D.CLI --fire-study-selftest</c>.
    /// </summary>
    public static class FireStudySelfTest
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

            // ── Scoring two jet fires of different size ─────────────────────
            {
                var scene = new Scene3D();
                var small = MakeJet("Small jet", 0.5, new Point3D(-40, 0, 2));
                var large = MakeJet("Large jet", 8.0, new Point3D(40, 0, 2));
                scene.FireScenario.Sources.Add(small);
                scene.FireScenario.Sources.Add(large);

                var study = new FireStudy
                {
                    Name = "Test study",
                    DomainHalfM = 100,
                    GridResolution = 32,
                    HarmQuantity = ViewFieldProperty.FatalityProbability,
                    HarmThreshold = 0.01
                };
                study.FireSourceIds.Add(small.Id);
                study.FireSourceIds.Add(large.Id);

                // Same frequency on both, so the ranking is decided by consequence alone.
                foreach (var id in new[] { small.Id, large.Id })
                {
                    var risk = study.EnsureRiskFor(id);
                    risk.FreqMode = RiskValueMode.Manual;
                    risk.FreqPerYear = 1e-4;
                }

                var evaluated = FireStudyEngine.Evaluate(scene, study);

                results.Add(new Result("both scenarios are scored",
                    evaluated.Rows.Count == 2, $"rows={evaluated.Rows.Count}"));

                var largeRow = evaluated.Rows.Find(r => r.ScenarioId == large.Id);
                var smallRow = evaluated.Rows.Find(r => r.ScenarioId == small.Id);

                results.Add(new Result("the larger release has the larger footprint",
                    largeRow != null && smallRow != null
                    && largeRow.ConsequenceM3 > smallRow.ConsequenceM3,
                    $"large={largeRow?.ConsequenceM3:F0} m³ small={smallRow?.ConsequenceM3:F0} m³"));

                results.Add(new Result("risk is frequency times consequence",
                    largeRow != null
                    && Math.Abs(largeRow.RiskM3PerYear
                                - largeRow.FrequencyPerYear * largeRow.ConsequenceM3) < 1e-12));

                results.Add(new Result("rows come back ranked by risk",
                    evaluated.Rows.Count == 2
                    && evaluated.Rows[0].RiskM3PerYear >= evaluated.Rows[1].RiskM3PerYear
                    && evaluated.Rows[0].ScenarioId == large.Id,
                    $"first={evaluated.Rows[0].Name}"));

                double shareSum = 0;
                foreach (var r in evaluated.Rows) shareSum += r.RiskShare;
                results.Add(new Result("risk shares add up to one",
                    Math.Abs(shareSum - 1.0) < 1e-9, $"sum={shareSum:F6}"));

                results.Add(new Result("the total is the sum of the rows",
                    Math.Abs(evaluated.TotalRiskM3PerYear
                             - (largeRow.RiskM3PerYear + smallRow.RiskM3PerYear)) < 1e-12));

                results.Add(new Result("the jet is reported as a jet",
                    largeRow.Kind == FireStudyEngine.ScenarioKind.JetFire,
                    $"kind={largeRow.Kind}"));
            }

            // ── A hand-placed fire has no frequency to derive ───────────────
            {
                var scene = new Scene3D();
                var jet = MakeJet("Unfrequenced", 2.0, new Point3D(0, 0, 2));
                scene.FireScenario.Sources.Add(jet);

                var study = new FireStudy { DomainHalfM = 80, GridResolution = 24 };
                study.FireSourceIds.Add(jet.Id);

                var evaluated = FireStudyEngine.Evaluate(scene, study);
                var row = evaluated.Rows[0];

                results.Add(new Result("a hand-placed fire reports no auto frequency",
                    row.FrequencyPerYear == 0 && row.FrequencyIsAuto));
                results.Add(new Result("and says why, instead of inventing a default",
                    !string.IsNullOrEmpty(row.Note), $"note='{row.Note}'"));
                results.Add(new Result("its consequence is still measured",
                    row.ConsequenceM3 > 0, $"{row.ConsequenceM3:F0} m³"));
            }

            // ── A manual consequence overrides the field integration ────────
            {
                var scene = new Scene3D();
                var jet = MakeJet("Manual", 2.0, new Point3D(0, 0, 2));
                scene.FireScenario.Sources.Add(jet);

                var study = new FireStudy { DomainHalfM = 80, GridResolution = 16 };
                study.FireSourceIds.Add(jet.Id);
                var risk = study.EnsureRiskFor(jet.Id);
                risk.ConsMode = RiskValueMode.Manual;
                risk.Consequence = 4242.0;
                risk.FreqMode = RiskValueMode.Manual;
                risk.FreqPerYear = 2e-3;

                var row = FireStudyEngine.Evaluate(scene, study).Rows[0];
                results.Add(new Result("a manual consequence is used as given",
                    Math.Abs(row.ConsequenceM3 - 4242.0) < 1e-9 && !row.ConsequenceIsAuto,
                    $"{row.ConsequenceM3:F0} m³"));
                results.Add(new Result("a manual frequency is used as given",
                    Math.Abs(row.FrequencyPerYear - 2e-3) < 1e-12 && !row.FrequencyIsAuto));
            }

            // ── Missing members are reported, not skipped silently ──────────
            {
                var scene = new Scene3D();
                var study = new FireStudy();
                study.FireSourceIds.Add("does-not-exist");
                study.IgnitionIds.Add("also-missing");

                var evaluated = FireStudyEngine.Evaluate(scene, study);
                results.Add(new Result("members missing from the scene raise warnings",
                    evaluated.Rows.Count == 0 && evaluated.Warnings.Count == 2,
                    $"rows={evaluated.Rows.Count} warnings={evaluated.Warnings.Count}"));

                results.Add(new Result("an empty study formats without throwing",
                    !string.IsNullOrEmpty(evaluated.Format())));
            }

            // ── An ignition with no runnable simulation ─────────────────────
            {
                var scene = new Scene3D();
                var ignition = new IgnitionEvent
                {
                    Name = "Orphan ignition",
                    SimulationId = "no-such-sim",
                    Position = new Point3D(0, 0, 2)
                };
                scene.Ignitions.Add(ignition);

                var study = new FireStudy();
                study.IgnitionIds.Add(ignition.Id);

                var evaluated = FireStudyEngine.Evaluate(scene, study);
                var row = evaluated.Rows[0];
                results.Add(new Result("a flash fire is reported as a flash fire",
                    row.Kind == FireStudyEngine.ScenarioKind.FlashFire));
                results.Add(new Result("an ignition without its simulation is flagged",
                    row.ConsequenceM3 == 0 && !string.IsNullOrEmpty(row.Note)
                    && evaluated.Warnings.Count == 1,
                    $"note='{row.Note}'"));
            }

            // ── Round trip ──────────────────────────────────────────────────
            results.AddRange(RoundTrip());

            return results;
        }

        /// <summary>Saves a scene with a fully populated study, reloads it and compares.
        /// The saver and loader are a matched pair that has drifted before, so every new
        /// persisted field gets pinned here.</summary>
        private static IReadOnlyList<Result> RoundTrip()
        {
            var results = new List<Result>();
            string dir = Path.Combine(Path.GetTempPath(),
                "dispersim-firestudy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                var scene = new Scene3D();
                var jet = MakeJet("Jet", 3.0, new Point3D(5, 5, 2));
                scene.FireScenario.Sources.Add(jet);
                var ignition = new IgnitionEvent { Name = "Ign", SimulationId = "sim-1" };
                scene.Ignitions.Add(ignition);

                var study = new FireStudy
                {
                    Name = "Estudo de incêndio",
                    Description = "unidade 12",
                    HarmQuantity = ViewFieldProperty.ThermalRadiationKwM2,
                    HarmThreshold = 12.5,
                    DomainHalfM = 175.0,
                    GridResolution = 48,
                    IgnitionProbability = 0.03,
                    IsVisible = false
                };
                study.FireSourceIds.Add(jet.Id);
                study.IgnitionIds.Add(ignition.Id);
                var risk = study.EnsureRiskFor(jet.Id);
                risk.FreqMode = RiskValueMode.Manual;
                risk.FreqPerYear = 7.5e-5;
                risk.ConsMode = RiskValueMode.Manual;
                risk.Consequence = 1234.5;
                scene.FireStudies.Add(study);

                string path = Path.Combine(dir, "firestudy.xml");
                SceneFileSaver.Save(scene, path);
                var reloaded = SceneFileLoader.Load(path);

                results.Add(new Result("round trip: one study comes back",
                    reloaded.FireStudies.Count == 1,
                    $"count={reloaded.FireStudies.Count}"));
                if (reloaded.FireStudies.Count != 1) return results;

                var back = reloaded.FireStudies[0];
                Compare(results, "Id", study.Id, back.Id);
                Compare(results, "Name", study.Name, back.Name);
                Compare(results, "Description", study.Description, back.Description);
                Compare(results, "HarmQuantity", study.HarmQuantity.ToString(), back.HarmQuantity.ToString());
                CompareNum(results, "HarmThreshold", study.HarmThreshold, back.HarmThreshold);
                CompareNum(results, "DomainHalfM", study.DomainHalfM, back.DomainHalfM);
                CompareNum(results, "GridResolution", study.GridResolution, back.GridResolution);
                CompareNum(results, "IgnitionProbability", study.IgnitionProbability, back.IgnitionProbability);
                Compare(results, "IsVisible", study.IsVisible.ToString(), back.IsVisible.ToString());
                Compare(results, "FireSourceIds", string.Join(",", study.FireSourceIds),
                    string.Join(",", back.FireSourceIds));
                Compare(results, "IgnitionIds", string.Join(",", study.IgnitionIds),
                    string.Join(",", back.IgnitionIds));

                var backRisk = back.EnsureRiskFor(jet.Id);
                Compare(results, "risk FreqMode", risk.FreqMode.ToString(), backRisk.FreqMode.ToString());
                CompareNum(results, "risk FreqPerYear", risk.FreqPerYear, backRisk.FreqPerYear);
                Compare(results, "risk ConsMode", risk.ConsMode.ToString(), backRisk.ConsMode.ToString());
                CompareNum(results, "risk Consequence", risk.Consequence, backRisk.Consequence);
            }
            catch (Exception ex)
            {
                results.Add(new Result("round trip completed without throwing", false, ex.Message));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* temp dir, best effort */ }
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
                ? $"OK — {passed}/{results.Count} fire-study checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        private static void Compare(List<Result> results, string field, string expected, string actual)
            => results.Add(new Result("round trip: " + field, expected == actual,
                $"expected='{expected}' actual='{actual}'"));

        private static void CompareNum(List<Result> results, string field, double expected, double actual)
            => results.Add(new Result("round trip: " + field,
                Math.Abs(expected - actual) <= 1e-9 * Math.Max(1.0, Math.Abs(expected)),
                $"expected={expected} actual={actual}"));

        private static FireSource MakeJet(string name, double massFlowKgS, Point3D position)
            => new FireSource
            {
                Name = name,
                Position = position,
                Direction = new Vector3D(1, 0, 0),
                MassFlowRateKgS = massFlowKgS,
                OrificeDiameterM = 0.05,
                HeatOfCombustionJKg = 50_000_000,
                RadiativeFraction = 0.2,
                RadiationModel = RadiationModel.SolidFlame
            };
    }
}
