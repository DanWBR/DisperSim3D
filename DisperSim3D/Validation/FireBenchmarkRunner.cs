using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DisperSim3D.Core;
using DisperSim3D.Extensibility;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Runs a <c>.fbench</c> fire benchmark: builds the fire the source describes, then
    /// compares the model's flame geometry, emissive power and incident flux against
    /// what the literature reports.
    ///
    /// <para>The two halves are scored separately. Flame length, diameter and SEP test
    /// the correlations; the radiometer fluxes test the view factor and the atmospheric
    /// transmissivity on top of them. A bench with no radiometers still runs and still
    /// means something — it just says so in the report.</para>
    ///
    /// <para>A bench whose <see cref="FireBenchmarkSpec.DataConfidence"/> is
    /// <c>Unverified</c> is evaluated and printed but never counted as a pass. A green
    /// tick against numbers nobody checked against the source would be worse than having
    /// no test at all.</para>
    /// </summary>
    public static class FireBenchmarkRunner
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public sealed class Check
        {
            public string Name;
            public double Predicted;
            public double Observed;
            public double Ratio;
            public bool Passed;
            public string Unit;

            public override string ToString()
                => string.Format(CultureInfo.InvariantCulture,
                    "  {0} {1,-26} predicted={2,10:G5} observed={3,10:G5} {4,-9} ratio={5:F2}",
                    Passed ? "PASS" : "FAIL", Name, Predicted, Observed, Unit, Ratio);
        }

        public sealed class FireBenchmarkResult
        {
            public string Name;
            public string Citation;
            public string DataConfidence;
            public List<Check> Checks = new List<Check>();
            public List<string> Notes = new List<string>();

            /// <summary>True when every check passed.</summary>
            public bool ChecksPassed;

            /// <summary>True when the bench counts as a pass — every check passed AND the
            /// data was confirmed against the source.</summary>
            public bool Counted => ChecksPassed && !IsUnverified;

            public bool IsUnverified =>
                string.Equals(DataConfidence, "Unverified", StringComparison.OrdinalIgnoreCase);

            public string Format()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"── {Name}");
                sb.AppendLine($"   {Citation}");
                sb.AppendLine($"   data confidence: {DataConfidence}");
                foreach (var c in Checks) sb.AppendLine(c.ToString());
                foreach (var n in Notes) sb.AppendLine("   ! " + n);
                sb.AppendLine(Counted
                    ? "   => PASS"
                    : (IsUnverified
                        ? "   => NOT COUNTED (data unverified against the source)"
                        : "   => FAIL"));
                return sb.ToString();
            }
        }

        public static FireBenchmarkSpec Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Fire benchmark file not found", path);
            return JsonSerializer.Deserialize<FireBenchmarkSpec>(File.ReadAllText(path), JsonOptions);
        }

        public static FireBenchmarkResult Run(FireBenchmarkSpec spec) => Run(spec, null);

        /// <summary>
        /// Runs the bench on the analytic models, or on the fire solver extender that
        /// <paramref name="solverOverride"/> or the bench's <see cref="FireBenchmarkSpec.Solver"/> names.
        /// </summary>
        public static FireBenchmarkResult Run(FireBenchmarkSpec spec, string solverOverride)
        {
            string solverName = !string.IsNullOrWhiteSpace(solverOverride) ? solverOverride : spec?.Solver;
            if (!string.IsNullOrWhiteSpace(solverName) && spec?.Fire != null)
                return RunOnExtender(spec, solverName);

            var result = new FireBenchmarkResult
            {
                Name = spec?.Name ?? "(unnamed)",
                Citation = spec?.Citation ?? "(no citation)",
                DataConfidence = spec?.DataConfidence ?? "Unverified"
            };
            if (spec?.Fire == null)
            {
                result.Notes.Add("the bench has no fire block");
                return result;
            }

            var accept = spec.Acceptance ?? new FireBenchmarkAcceptance();
            var ambient = spec.Ambient ?? new FireBenchmarkAmbient();
            var source = BuildFireSource(spec.Fire);
            var wind = new MeteorologicalConditions
            {
                WindSpeed = ambient.WindSpeed,
                WindDirectionDeg = ambient.WindDirectionDeg,
                AmbientTemperature = ambient.AmbientTemperatureK,
                AmbientPressure = ambient.AmbientPressurePa,
                RelativeHumidity = ambient.RelativeHumidity
            }.WindVector;

            bool fireball = source.Kind == FireKind.Fireball;
            var emitter = fireball ? null : SolidFlameModel.Prepare(source, wind);

            // ── Flame geometry and emissive power ───────────────────────────
            var expected = spec.ExpectedFlame;
            if (expected?.LengthM is double length && length > 0 && !fireball)
                result.Checks.Add(Ratio("flame length", emitter.FlameLengthM, length, "m",
                    accept.FlameLengthRatioMin, accept.FlameLengthRatioMax));

            if (expected?.DiameterM is double diameter && diameter > 0)
                result.Checks.Add(Ratio(fireball ? "fireball diameter" : "flame diameter",
                    fireball ? FireballModel.DiameterM(source.FireballMassKg) : emitter.FlameDiameterM, diameter, "m",
                    accept.FlameDiameterRatioMin, accept.FlameDiameterRatioMax));

            if (expected?.SurfaceEmissivePowerKwM2 is double sep && sep > 0)
                result.Checks.Add(Ratio("surface emissive power",
                    fireball ? FireballModel.SepKwM2(source) : emitter.SepKwM2, sep, "kW/m²",
                    accept.SepRatioMin, accept.SepRatioMax));

            // The length band is the closest one a duration has.
            if (fireball && expected?.DurationS is double duration && duration > 0)
                result.Checks.Add(Ratio("fireball duration", FireballModel.DurationS(source.FireballMassKg), duration, "s",
                    accept.FlameLengthRatioMin, accept.FlameLengthRatioMax));

            if (!string.IsNullOrEmpty(expected?.Note)) result.Notes.Add(expected.Note);

            // ── Incident flux at the radiometers ────────────────────────────
            int fluxChecks = 0, fluxPassed = 0;
            if (spec.Radiometers != null)
            {
                foreach (var r in spec.Radiometers)
                {
                    if (r == null || !(r.MeasuredKwM2 > 0)) continue;
                    var position = ToPoint(r.Position);
                    var mode = ParseReceiverMode(r.ReceiverMode);
                    double predicted = fireball
                        ? FireballModel.FluxKwM2(source, position, mode, ambient.AmbientTemperatureK, ambient.RelativeHumidity)
                        : SolidFlameModel.FluxKwM2(emitter, position, mode,
                            ambient.AmbientTemperatureK, ambient.RelativeHumidity);

                    var check = Ratio("flux @ " + (r.Name ?? "radiometer"),
                        predicted, r.MeasuredKwM2, "kW/m²",
                        accept.FluxRatioMin, accept.FluxRatioMax);
                    result.Checks.Add(check);
                    fluxChecks++;
                    if (check.Passed) fluxPassed++;
                }
            }

            if (fluxChecks == 0)
                result.Notes.Add("no radiometer data — this bench only exercises the flame "
                               + "correlations and the emissive power, not the view factor "
                               + "or the atmospheric transmissivity");

            result.ChecksPassed = result.Checks.Count > 0;
            foreach (var c in result.Checks) if (!c.Passed) result.ChecksPassed = false;

            // The flux band tolerates a declared fraction of misses; a single stray
            // radiometer should not sink a bench with twenty of them.
            if (fluxChecks > 0)
            {
                double fraction = (double)fluxPassed / fluxChecks;
                if (fraction >= accept.MinFluxFac2Fraction)
                {
                    result.ChecksPassed = true;
                    foreach (var c in result.Checks)
                        if (!c.Passed && !c.Name.StartsWith("flux @", StringComparison.Ordinal))
                            result.ChecksPassed = false;
                    if (fraction < 1.0)
                        result.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0} of {1} radiometers inside the band ({2:P0} ≥ {3:P0} required)",
                            fluxPassed, fluxChecks, fraction, accept.MinFluxFac2Fraction));
                }
            }

            return result;
        }

        /// <summary>
        /// Runs the bench's fire on a solver extender that declares
        /// <see cref="SolverCapabilities.Fire"/> and scores the radiometers against the
        /// peak of the radiation field it stored (<c>_Q</c>). The flame-geometry checks
        /// need the analytic flame and are not run; the report says so.
        /// </summary>
        private static FireBenchmarkResult RunOnExtender(FireBenchmarkSpec spec, string solverName)
        {
            var result = new FireBenchmarkResult
            {
                Name = (spec.Name ?? "(unnamed)") + " [" + solverName + "]",
                Citation = spec.Citation ?? "(no citation)",
                DataConfidence = spec.DataConfidence ?? "Unverified"
            };
            var solver = SolverExtenderRegistry.FindByName(solverName);
            if (solver == null)
            {
                result.Notes.Add("no solver extender " + solverName + " is installed");
                return result;
            }
            if ((solver.Capabilities & SolverCapabilities.Fire) == 0)
            {
                result.Notes.Add(solver.DisplayName + " does not declare the Fire capability");
                return result;
            }

            var accept = spec.Acceptance ?? new FireBenchmarkAcceptance();
            var ambient = spec.Ambient ?? new FireBenchmarkAmbient();
            var domain = spec.Domain ?? new FireBenchmarkDomain();
            var meteo = new MeteorologicalConditions
            {
                WindSpeed = ambient.WindSpeed,
                WindDirectionDeg = ambient.WindDirectionDeg,
                AmbientTemperature = ambient.AmbientTemperatureK,
                AmbientPressure = ambient.AmbientPressurePa,
                RelativeHumidity = ambient.RelativeHumidity
            };
            var source = BuildFireSource(spec.Fire);
            var scene = new Scene3D();
            scene.FireScenario.Sources.Add(source);
            scene.WindFieldScenarios.Add(new WindFieldScenario { Name = "bench", Meteo = meteo });
            var scenario = new DispersionScenario
            {
                Name = result.Name,
                Meteo = meteo,
                SolverType = CfdSolverType.Extender,
                ExtenderSolverId = solver.Id,
                DomainSizeM = domain.SizeM,
                GridResolution = domain.GridResolution,
                SimulationDurationS = domain.DurationS
            };
            var request = new SolverExtenderRequest(scene, scenario, new CfdConfiguration(), new List<BoundingBox>(), null,
                System.Threading.CancellationToken.None)
            {
                Fire = new SolverFireTarget(source)
            };

            SolverExtenderResult run;
            try { run = solver.Run(request); }
            catch (Exception ex) { run = SolverExtenderResult.Fail(ex.GetType().Name + ": " + ex.Message); }
            if (run == null || !run.Success)
            {
                result.Notes.Add(solver.DisplayName + " failed: " + (run?.Error ?? "no result"));
                return result;
            }

            var sim = SimulationOfRun(run, scenario, solver.Id);
            var radiation = StoredFields.Load(sim, ResultChannels.Radiation);
            if (radiation == null)
            {
                result.Notes.Add(solver.DisplayName + " stored no radiation field (_Q) at " + run.CasePath);
                return result;
            }
            var sampler = StoredFields.Sampler(StoredFields.Collapse(radiation, ResultChannels.Radiation, ViewTimeMode.PeakOverTime), sim);

            int fluxChecks = 0, fluxPassed = 0;
            foreach (var r in spec.Radiometers ?? new List<FireBenchmarkRadiometer>())
            {
                if (r == null || !(r.MeasuredKwM2 > 0)) continue;
                var p = ToPoint(r.Position);
                var check = Ratio("flux @ " + (r.Name ?? "radiometer"), sampler.EvaluateConcentration(p.X, p.Y, p.Z),
                    r.MeasuredKwM2, "kW/m²", accept.FluxRatioMin, accept.FluxRatioMax);
                result.Checks.Add(check);
                fluxChecks++;
                if (check.Passed) fluxPassed++;
            }
            result.Notes.Add("radiometers sampled from the peak of " + solver.DisplayName + "'s stored radiation; "
                           + "the flame length, diameter and emissive power checks need the analytic flame and were not run");
            if (fluxChecks == 0)
            {
                result.Notes.Add("no radiometer data — nothing to score on a solver extender");
                return result;
            }
            double fraction = (double)fluxPassed / fluxChecks;
            result.ChecksPassed = fraction >= accept.MinFluxFac2Fraction;
            if (fraction < 1.0)
                result.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} of {1} radiometers inside the band ({2:P0}, {3:P0} required)",
                    fluxPassed, fluxChecks, fraction, accept.MinFluxFac2Fraction));
            return result;
        }

        /// <summary>
        /// A completed simulation standing for an extender's run, so its stored fields
        /// read through <see cref="StoredFields"/>: the result's grid and centre when it
        /// returned one, else the scenario's.
        /// </summary>
        internal static Simulation SimulationOfRun(SolverExtenderResult run, DispersionScenario scenario, string solverId)
        {
            var sim = new Simulation
            {
                Name = scenario.Name,
                SolverType = CfdSolverType.Extender,
                ExtenderSolverId = solverId,
                Status = SimulationStatus.Completed,
                CasePath = run.CasePath ?? run.Result?.CaseDir,
                SnapshotDomainSizeM = scenario.DomainSizeM,
                SnapshotGridResolution = scenario.GridResolution
            };
            var r = run.Result;
            if (r != null && r.DomainXMax > r.DomainXMin)
            {
                sim.SnapshotDomainSizeM = (r.DomainXMax - r.DomainXMin) / 2;
                sim.SnapshotDomainCenterX = (r.DomainXMax + r.DomainXMin) / 2;
                sim.SnapshotDomainCenterY = (r.DomainYMax + r.DomainYMin) / 2;
                if (r.GridNx > 0) sim.SnapshotGridResolution = r.GridNx;
            }
            return sim;
        }

        /// <summary>Runs every <c>.fbench</c> under a file or directory and prints a
        /// report. Returns true when every counted bench passed.</summary>
        public static bool RunAndPrint(string path, TextWriter writer) => RunAndPrint(path, writer, null);

        /// <summary>The same, every bench on the solver extender <paramref name="solverOverride"/> names when given.</summary>
        public static bool RunAndPrint(string path, TextWriter writer, string solverOverride)
        {
            var files = new List<string>();
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path, "*.fbench", SearchOption.AllDirectories));
            else if (File.Exists(path))
                files.Add(path);

            if (files.Count == 0)
            {
                writer.WriteLine("No .fbench files found under " + path);
                return false;
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            int passed = 0, failed = 0, uncounted = 0;
            foreach (var f in files)
            {
                FireBenchmarkResult r;
                try
                {
                    r = Run(Load(f), solverOverride);
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"── {Path.GetFileName(f)}");
                    writer.WriteLine("   ERROR: " + ex.Message);
                    failed++;
                    continue;
                }

                writer.WriteLine(r.Format());
                if (r.IsUnverified) uncounted++;
                else if (r.ChecksPassed) passed++;
                else failed++;
            }

            writer.WriteLine($"{passed} passed, {failed} failed, {uncounted} not counted "
                           + "(data unverified against the source).");
            if (uncounted > 0)
                writer.WriteLine("Set \"dataConfidence\": \"High\" or \"Medium\" once the numbers "
                               + "have been checked against the cited source.");
            return failed == 0 && passed > 0;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static Check Ratio(string name, double predicted, double observed,
            string unit, double min, double max)
        {
            double ratio = observed > 0 ? predicted / observed : 0;
            return new Check
            {
                Name = name,
                Predicted = predicted,
                Observed = observed,
                Unit = unit,
                Ratio = ratio,
                Passed = ratio >= min && ratio <= max
            };
        }

        private static FireSource BuildFireSource(FireBenchmarkFire fire)
        {
            bool isPool = string.Equals(fire.Kind, "pool", StringComparison.OrdinalIgnoreCase);
            double massFlow = fire.MassFlowRateKgS;
            if (isPool && !(massFlow > 0))
                massFlow = fire.BurnRateKgM2S * Math.PI * 0.25
                         * fire.PoolDiameterM * fire.PoolDiameterM;

            return new FireSource
            {
                Name = fire.Fuel ?? "benchmark fire",
                Position = ToPoint(fire.Position),
                Direction = ToVector(fire.Direction),
                Kind = isPool ? FireKind.Pool
                     : string.Equals(fire.Kind, "fireball", StringComparison.OrdinalIgnoreCase) ? FireKind.Fireball
                     : FireKind.Jet,
                PoolDiameterM = fire.PoolDiameterM,
                PoolBurnRateKgM2S = fire.BurnRateKgM2S,
                FireballMassKg = fire.FireballMassKg > 0 ? fire.FireballMassKg : 1000.0,
                MassFlowRateKgS = massFlow,
                OrificeDiameterM = fire.OrificeDiameterM > 0 ? fire.OrificeDiameterM : 0.025,
                HeatOfCombustionJKg = fire.HeatOfCombustionJKg,
                RadiativeFraction = fire.RadiativeFraction,
                FuelMolarMassKgMol = fire.FuelMolarMassKgMol,
                IsSootyFuel = fire.IsSootyFuel,
                StagnationPressurePa = fire.StagnationPressurePa,
                StagnationTemperatureK = fire.StagnationTemperatureK,
                RadiationModel = RadiationModel.SolidFlame
            };
        }

        private static ReceiverMode ParseReceiverMode(string mode)
            => Enum.TryParse(mode, ignoreCase: true, out ReceiverMode parsed)
                ? parsed
                : ReceiverMode.MaxOriented;

        private static Point3D ToPoint(double[] v)
            => v != null && v.Length >= 3 ? new Point3D(v[0], v[1], v[2]) : new Point3D(0, 0, 0);

        private static Vector3D ToVector(double[] v)
            => v != null && v.Length >= 3 ? new Vector3D(v[0], v[1], v[2]) : new Vector3D(1, 0, 0);
    }
}
