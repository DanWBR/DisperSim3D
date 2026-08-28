using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Scores a <see cref="FireStudy"/>: one row per scenario with frequency,
    /// consequence and their product, ranked so the dominant contributor is obvious.
    ///
    /// <para><b>Consequence is an exposed footprint.</b> For a jet or pool fire it is the
    /// volume where the study's harm quantity crosses its threshold — 1% lethality by
    /// default. For a flash fire it is the burnt envelope, because consequence practice
    /// treats everyone inside a flash fire as a fatality. Both are volumes in m³, which
    /// makes them comparable; neither is a body count, because the project carries no
    /// population model and inventing one would be worse than saying so.</para>
    ///
    /// <para><b>Frequency comes from where the data is.</b> A flash fire inherits the
    /// leak frequency of the release that made the cloud — IOGP by equipment and hole
    /// size, through <see cref="RiskWeightHelper.AutoFrequency"/> — multiplied by the
    /// study's ignition probability. A jet or pool fire placed by hand has no such
    /// chain, so it reports no auto frequency and asks for a manual one; that gap is
    /// stated in the row rather than papered over with a default.</para>
    /// </summary>
    public static class FireStudyEngine
    {
        /// <summary>What kind of fire a row scores.</summary>
        public enum ScenarioKind { JetFire, PoolFire, FlashFire }

        public sealed class FireStudyRow
        {
            public string ScenarioId;
            public string Name;
            public ScenarioKind Kind;

            /// <summary>Events per year. Zero when it could not be derived and no
            /// manual value was set — see <see cref="Note"/>.</summary>
            public double FrequencyPerYear;
            public bool FrequencyIsAuto;

            /// <summary>Harm footprint (m³).</summary>
            public double ConsequenceM3;
            public bool ConsequenceIsAuto;

            /// <summary>frequency × consequence, in m³ per year.</summary>
            public double RiskM3PerYear;

            /// <summary>Share of the study's total risk, 0–1.</summary>
            public double RiskShare;

            /// <summary>Why a value is missing or approximate, when it is.</summary>
            public string Note = "";
        }

        public sealed class FireStudyResult
        {
            public List<FireStudyRow> Rows = new List<FireStudyRow>();
            public double TotalRiskM3PerYear;

            /// <summary>Rows that could not be scored at all (missing simulation,
            /// unreadable result, ignition outside the cloud).</summary>
            public List<string> Warnings = new List<string>();

            /// <summary>Fixed-width report, for the CLI and for pasting into a note.</summary>
            public string Format()
            {
                var inv = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine($"{"Scenario",-28} {"Kind",-10} {"Freq (1/y)",12} {"Cons (m³)",12} {"Risk (m³/y)",13} {"Share",7}");
                sb.AppendLine(new string('-', 86));
                foreach (var r in Rows)
                {
                    sb.AppendLine(string.Format(inv,
                        "{0,-28} {1,-10} {2,12:E3} {3,12:F0} {4,13:E3} {5,6:P1}",
                        Truncate(r.Name, 28), r.Kind, r.FrequencyPerYear,
                        r.ConsequenceM3, r.RiskM3PerYear, r.RiskShare));
                    if (!string.IsNullOrEmpty(r.Note))
                        sb.AppendLine($"{"",-28} └─ {r.Note}");
                }
                sb.AppendLine(new string('-', 86));
                sb.AppendLine(string.Format(inv, "{0,-52} {1,13:E3}", "Total", TotalRiskM3PerYear));
                foreach (var w in Warnings) sb.AppendLine("! " + w);
                return sb.ToString();
            }

            private static string Truncate(string s, int n)
                => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");
        }

        /// <summary>Scores every scenario in the study against the current scene.</summary>
        public static FireStudyResult Evaluate(Scene3D scene, FireStudy study)
        {
            var result = new FireStudyResult();
            if (scene == null || study == null) return result;

            int n = Math.Max(4, study.GridResolution);
            int nz = Math.Max(2, n / 2);
            double half = study.DomainHalfM > 0 ? study.DomainHalfM : 100.0;
            // Same mapping BuildRadiationField uses, so the footprint integrates over
            // the cells the user sees in a View.
            double cellVolume = (2.0 * half / n) * (2.0 * half / n) * (2.0 * half / nz);

            foreach (var id in study.FireSourceIds ?? new List<string>())
            {
                var source = FindFireSource(scene, id);
                if (source == null)
                {
                    result.Warnings.Add($"fire source {id} is in the study but not in the scene");
                    continue;
                }
                result.Rows.Add(ScoreFireSource(scene, study, source, n, nz, half, cellVolume));
            }

            foreach (var id in study.IgnitionIds ?? new List<string>())
            {
                var ignition = FindIgnition(scene, id);
                if (ignition == null)
                {
                    result.Warnings.Add($"ignition {id} is in the study but not in the scene");
                    continue;
                }
                result.Rows.Add(ScoreIgnition(scene, study, ignition, result));
            }

            foreach (var row in result.Rows)
            {
                row.RiskM3PerYear = row.FrequencyPerYear * row.ConsequenceM3;
                result.TotalRiskM3PerYear += row.RiskM3PerYear;
            }
            if (result.TotalRiskM3PerYear > 0)
                foreach (var row in result.Rows)
                    row.RiskShare = row.RiskM3PerYear / result.TotalRiskM3PerYear;

            result.Rows.Sort((a, b) => b.RiskM3PerYear.CompareTo(a.RiskM3PerYear));
            return result;
        }

        // ── Scenario scoring ────────────────────────────────────────────────

        private static FireStudyRow ScoreFireSource(Scene3D scene, FireStudy study,
            FireSource source, int n, int nz, double half, double cellVolume)
        {
            var row = new FireStudyRow
            {
                ScenarioId = source.Id,
                Name = string.IsNullOrEmpty(source.Name) ? "(fire)" : source.Name,
                Kind = source.IsPoolFire ? ScenarioKind.PoolFire : ScenarioKind.JetFire
            };

            var risk = study.EnsureRiskFor(source.Id);

            // Consequence: the harm field of this source alone, so the row measures the
            // scenario and not the whole plant burning at once.
            if (risk.ConsMode == RiskValueMode.Manual)
            {
                row.ConsequenceM3 = risk.Consequence;
                row.ConsequenceIsAuto = false;
            }
            else
            {
                var probe = SingleSourceScene(scene, source);
                var field = FieldTransform.BuildAnalyticField(probe, study.HarmQuantity, n, n, nz, half);
                row.ConsequenceM3 = ThermalDose.FootprintVolumeM3(field, cellVolume, study.HarmThreshold);
                row.ConsequenceIsAuto = true;
            }

            // Frequency: nothing to derive it from. A FireSource is placed by hand and
            // carries no equipment inventory, so there is no IOGP chain behind it.
            if (risk.FreqMode == RiskValueMode.Manual)
            {
                row.FrequencyPerYear = risk.FreqPerYear;
                row.FrequencyIsAuto = false;
            }
            else
            {
                row.FrequencyPerYear = 0.0;
                row.FrequencyIsAuto = true;
                row.Note = "no leak frequency behind a hand-placed fire — set one manually "
                         + "or model the release and ignite it instead";
            }

            return row;
        }

        private static FireStudyRow ScoreIgnition(Scene3D scene, FireStudy study,
            IgnitionEvent ignition, FireStudyResult result)
        {
            var row = new FireStudyRow
            {
                ScenarioId = ignition.Id,
                Name = string.IsNullOrEmpty(ignition.Name) ? "(ignition)" : ignition.Name,
                Kind = ScenarioKind.FlashFire
            };

            var risk = study.EnsureRiskFor(ignition.Id);
            var sim = FindSimulation(scene, ignition.SimulationId);

            // Consequence: the envelope the ignition actually burns.
            if (risk.ConsMode == RiskValueMode.Manual)
            {
                row.ConsequenceM3 = risk.Consequence;
                row.ConsequenceIsAuto = false;
            }
            else if (sim == null)
            {
                row.ConsequenceIsAuto = true;
                row.Note = "the ignition's simulation is missing from the project";
                result.Warnings.Add($"{row.Name}: simulation {ignition.SimulationId} not found");
            }
            else
            {
                var flash = BurnIgnition(scene, sim, ignition);
                if (flash == null)
                {
                    row.ConsequenceIsAuto = true;
                    row.Note = "no readable result for the simulation — run it first";
                    result.Warnings.Add($"{row.Name}: no result to burn at {sim.CasePath}");
                }
                else if (!flash.Ignited)
                {
                    row.ConsequenceIsAuto = true;
                    row.Note = "the ignition point is not inside flammable gas at that instant";
                }
                else
                {
                    row.ConsequenceM3 = flash.EnvelopeVolumeM3;
                    row.ConsequenceIsAuto = true;
                }
            }

            // Frequency: leak frequency of the release behind the cloud × P(ignition).
            if (risk.FreqMode == RiskValueMode.Manual)
            {
                row.FrequencyPerYear = risk.FreqPerYear;
                row.FrequencyIsAuto = false;
            }
            else
            {
                double leak = 0.0;
                if (sim?.SnapshotSource != null)
                {
                    WindFieldScenario wf = null;
                    if (scene.WindFieldScenarios != null)
                        foreach (var w in scene.WindFieldScenarios)
                            if (w != null && w.Id == sim.WindFieldId) { wf = w; break; }
                    leak = RiskWeightHelper.AutoFrequency(sim.SnapshotSource, wf, scene.WindRose);
                }
                double pIgnition = study.IgnitionProbability > 0 ? study.IgnitionProbability : 0.1;
                row.FrequencyPerYear = leak * pIgnition;
                row.FrequencyIsAuto = true;
                if (!(leak > 0) && string.IsNullOrEmpty(row.Note))
                    row.Note = "the release has no leak frequency — fill in the IOGP inventory "
                             + "on the source, or set the frequency manually";
            }

            return row;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// A scene carrying exactly one fire source, so the radiation field measures that
        /// scenario alone. Everything the field depends on — meteorology, receiver mode,
        /// exposure time, obstacle geometry — is shared by reference with the real scene,
        /// because none of it is written during evaluation.
        /// </summary>
        private static Scene3D SingleSourceScene(Scene3D scene, FireSource source)
        {
            var probe = new Scene3D
            {
                GeneralSettings = scene.GeneralSettings,
                WindFieldScenarios = scene.WindFieldScenarios,
                Decorations = scene.Decorations
            };
            probe.FireScenario.ReceiverMode = scene.FireScenario?.ReceiverMode ?? ReceiverMode.MaxOriented;
            probe.FireScenario.ExposureTimeS = scene.FireScenario?.ExposureTimeS ?? 20.0;
            probe.FireScenario.Sources.Add(source);
            return probe;
        }

        /// <summary>Loads the simulation's concentration snapshot at the ignition instant
        /// and burns it. Null when no result can be read.</summary>
        private static FlashFireEngine.FlashFireResult BurnIgnition(
            Scene3D scene, Simulation sim, IgnitionEvent ignition)
        {
            if (string.IsNullOrEmpty(sim.CasePath)) return null;

            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

            var gas = ResolveGas(sim, scene);
            string species = OpenFoamCaseGenerator.ResolveOpenFoamSpecies(sim.SnapshotSource);

            var read = OpenFoamResultReader.ReadResults(sim.CasePath, nx, ny, nz, half,
                scalarFieldName: species);
            if (read == null || !read.IsLoaded || read.TimeSteps.Count == 0)
                read = OpenFoamResultReader.TryLoadFlatBinCase(sim.CasePath, ref nx, ref ny, ref nz, half);
            if (read == null || !read.IsLoaded || read.TimeSteps.Count == 0) return null;

            // The snapshot closest to the ignition instant is the cloud that burns.
            double chosen = read.TimeSteps[0];
            double best = Math.Abs(chosen - ignition.TimeS);
            foreach (double t in read.TimeSteps)
            {
                double d = Math.Abs(t - ignition.TimeS);
                if (d < best) { best = d; chosen = t; }
            }

            var raw = read.GetField(chosen);
            if (raw == null) return null;

            var concentration = FieldTransform.FromMassFraction(
                raw, ViewFieldProperty.ConcentrationKgM3, gas);

            double lfl = gas != null && gas.LFL > 0 ? gas.LFL : 0.033;
            double ufl = gas != null && gas.UFL > 0 ? gas.UFL : 0;
            double cell = 2.0 * half / concentration.GetLength(0);

            return FlashFireEngine.Compute(concentration, lfl, ufl, ignition,
                -half, -half, 0, cell, cell, cell, SceneObstacles.Collect(scene));
        }

        /// <summary>Gas attached to a simulation's snapshotted source — the library entry
        /// when it is linked, the inline gas otherwise.</summary>
        public static GasProperties ResolveGas(Simulation sim, Scene3D scene)
        {
            if (sim?.SnapshotSource == null) return null;
            var src = sim.SnapshotSource;
            if (!string.IsNullOrEmpty(src.GasRefId) && scene?.GasLibrary != null)
            {
                var lib = scene.GasLibrary.Find(g => g.Id == src.GasRefId);
                if (lib != null) return lib.AsGasProperties();
            }
            return src.Gas;
        }

        private static FireSource FindFireSource(Scene3D scene, string id)
        {
            if (scene?.FireScenario?.Sources == null) return null;
            foreach (var s in scene.FireScenario.Sources)
                if (s != null && s.Id == id) return s;
            return null;
        }

        private static IgnitionEvent FindIgnition(Scene3D scene, string id)
        {
            if (scene?.Ignitions == null) return null;
            foreach (var g in scene.Ignitions)
                if (g != null && g.Id == id) return g;
            return null;
        }

        private static Simulation FindSimulation(Scene3D scene, string id)
        {
            if (scene?.Simulations == null || string.IsNullOrEmpty(id)) return null;
            foreach (var s in scene.Simulations)
                if (s != null && s.Id == id) return s;
            return null;
        }
    }
}
