using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DisperSim3D.Core;
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

        public static FireBenchmarkResult Run(FireBenchmarkSpec spec)
        {
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

            var emitter = SolidFlameModel.Prepare(source, wind);

            // ── Flame geometry and emissive power ───────────────────────────
            var expected = spec.ExpectedFlame;
            if (expected?.LengthM is double length && length > 0)
                result.Checks.Add(Ratio("flame length", emitter.FlameLengthM, length, "m",
                    accept.FlameLengthRatioMin, accept.FlameLengthRatioMax));

            if (expected?.DiameterM is double diameter && diameter > 0)
                result.Checks.Add(Ratio("flame diameter", emitter.FlameDiameterM, diameter, "m",
                    accept.FlameDiameterRatioMin, accept.FlameDiameterRatioMax));

            if (expected?.SurfaceEmissivePowerKwM2 is double sep && sep > 0)
                result.Checks.Add(Ratio("surface emissive power", emitter.SepKwM2, sep, "kW/m²",
                    accept.SepRatioMin, accept.SepRatioMax));

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
                    double predicted = SolidFlameModel.FluxKwM2(emitter, position, mode,
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

        /// <summary>Runs every <c>.fbench</c> under a file or directory and prints a
        /// report. Returns true when every counted bench passed.</summary>
        public static bool RunAndPrint(string path, TextWriter writer)
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
                    r = Run(Load(f));
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
                IsPoolFire = isPool,
                PoolDiameterM = fire.PoolDiameterM,
                PoolBurnRateKgM2S = fire.BurnRateKgM2S,
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
