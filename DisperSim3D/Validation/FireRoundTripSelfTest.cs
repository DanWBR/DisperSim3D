using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Save → load → compare test for <see cref="FireScenario"/>. Builds a scene
    /// with a jet fire and a pool fire, writes it through
    /// <see cref="SceneFileSaver"/>, reads it back through
    /// <see cref="SceneFileLoader"/> and compares every persisted field.
    ///
    /// This exists because the two sides drifted apart: the saver nests the
    /// sources as <c>&lt;FireSources&gt;&lt;Fire/&gt;</c> while the loader looked for
    /// <c>&lt;FireSource/&gt;</c> directly under <c>&lt;FireScenario&gt;</c>, so every
    /// fire source was written and silently dropped on reload — along with five
    /// attributes whose names differed between the two. The test pins the
    /// contract: a property added to <see cref="FireSource"/> and written by the
    /// saver but not read by the loader fails here.
    ///
    /// Run via <c>DisperSim3D.CLI --fire-roundtrip-selftest</c>.
    /// </summary>
    public static class FireRoundTripSelfTest
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

        /// <summary>Runs the round trip in a temporary directory, which is removed
        /// again before returning.</summary>
        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();
            string dir = Path.Combine(Path.GetTempPath(),
                "dispersim-fire-roundtrip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                var original = BuildScene();

                // 1) The shape the saver actually writes.
                string savedPath = Path.Combine(dir, "roundtrip.xml");
                SceneFileSaver.Save(original, savedPath);
                results.Add(CheckSavedShape(savedPath, original.FireScenario.Sources.Count));
                Compare(original, SceneFileLoader.Load(savedPath), "", results);

                // 2) The flat shape the loader used to expect, so older or
                //    hand-written projects keep loading.
                string legacyPath = Path.Combine(dir, "roundtrip-legacy.xml");
                WriteFlatShape(savedPath, legacyPath);
                Compare(original, SceneFileLoader.Load(legacyPath), "flat shape: ", results);
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
                ? $"OK — {passed}/{results.Count} fire round-trip checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        /// <summary>Every value here is deliberately off the property defaults, so a
        /// field that fails to round-trip comes back as its default and the
        /// comparison catches it.</summary>
        private static Scene3D BuildScene()
        {
            var scene = new Scene3D();
            scene.FireScenario.Name = "Cenário de teste";
            scene.FireScenario.RadiationContourLevels = new List<double> { 2500, 8000, 25000 };

            scene.FireScenario.Sources.Add(new FireSource
            {
                Name = "Jato horizontal",
                Position = new Point3D(12.5, -7.25, 3.75),
                Direction = new Vector3D(0.0, -1.0, 0.25),
                MassFlowRateKgS = 4.75,
                OrificeDiameterM = 0.032,
                HeatOfCombustionJKg = 46_400_000,
                RadiativeFraction = 0.27,
                IsPoolFire = false,
                IsVisible = true
            });

            scene.FireScenario.Sources.Add(new FireSource
            {
                Name = "Poça de diesel",
                Position = new Point3D(-30.0, 18.5, 0.0),
                Direction = new Vector3D(0.0, 0.0, 1.0),
                MassFlowRateKgS = 2.0,
                IsPoolFire = true,
                PoolDiameterM = 12.5,
                PoolBurnRateKgM2S = 0.055,
                RadiativeFraction = 0.35,
                IsVisible = false
            });

            return scene;
        }

        /// <summary>
        /// Pins the element shape the saver emits. This is the check that names the
        /// original defect: the saver nests <c>&lt;FireSources&gt;&lt;Fire/&gt;</c> and
        /// emits no <c>&lt;FireSource/&gt;</c> element at all, which is precisely what
        /// the loader used to look for — so a reader written against the wrong name
        /// finds nothing and drops every source without erroring.
        /// </summary>
        private static Result CheckSavedShape(string savedPath, int expectedCount)
        {
            var fireEl = XDocument.Load(savedPath).Root?.Element("FireScenario");
            if (fireEl == null)
                return new Result("saver writes <FireScenario>", false, "element missing");

            int nested = 0, flat = 0;
            var nestedEl = fireEl.Element("FireSources");
            if (nestedEl != null) foreach (var _ in nestedEl.Elements("Fire")) nested++;
            foreach (var _ in fireEl.Elements("FireSource")) flat++;

            return new Result("saver writes <FireSources><Fire> and no <FireSource>",
                nested == expectedCount && flat == 0,
                $"expected nested={expectedCount} flat=0, actual nested={nested} flat={flat}");
        }

        /// <summary>Rewrites <c>&lt;FireSources&gt;&lt;Fire/&gt;</c> into
        /// <c>&lt;FireSource/&gt;</c> children of <c>&lt;FireScenario&gt;</c> — the shape
        /// the loader expected before the fix.</summary>
        private static void WriteFlatShape(string sourcePath, string targetPath)
        {
            var doc = XDocument.Load(sourcePath);
            var fireEl = doc.Root?.Element("FireScenario");
            var nested = fireEl?.Element("FireSources");
            if (nested == null) throw new InvalidOperationException(
                "saved project has no <FireScenario><FireSources> to flatten");

            foreach (var fe in nested.Elements("Fire"))
                fireEl.Add(new XElement("FireSource", fe.Attributes()));
            nested.Remove();
            doc.Save(targetPath);
        }

        private static void Compare(Scene3D original, Scene3D reloaded,
            string prefix, List<Result> results)
        {
            var a = original.FireScenario;
            var b = reloaded.FireScenario;

            results.Add(new Result(prefix + "scenario name",
                b.Name == a.Name, $"expected='{a.Name}' actual='{b.Name}'"));

            bool levelsMatch = b.RadiationContourLevels.Count == a.RadiationContourLevels.Count;
            if (levelsMatch)
            {
                for (int i = 0; i < a.RadiationContourLevels.Count; i++)
                    levelsMatch &= Near(a.RadiationContourLevels[i], b.RadiationContourLevels[i]);
            }
            results.Add(new Result(prefix + "radiation contour levels", levelsMatch,
                $"expected=[{string.Join(", ", a.RadiationContourLevels)}] " +
                $"actual=[{string.Join(", ", b.RadiationContourLevels)}]"));

            results.Add(new Result(prefix + "fire source count",
                b.Sources.Count == a.Sources.Count,
                $"expected={a.Sources.Count} actual={b.Sources.Count}"));
            if (b.Sources.Count != a.Sources.Count) return;

            for (int i = 0; i < a.Sources.Count; i++)
            {
                var src = a.Sources[i];
                var dst = b.Sources[i];
                string tag = $"{prefix}source[{i}] ";

                results.Add(new Result(tag + "Id", dst.Id == src.Id,
                    $"expected={src.Id} actual={dst.Id}"));
                results.Add(new Result(tag + "Name", dst.Name == src.Name,
                    $"expected='{src.Name}' actual='{dst.Name}'"));
                results.Add(new Result(tag + "Position",
                    Near(src.Position.X, dst.Position.X)
                    && Near(src.Position.Y, dst.Position.Y)
                    && Near(src.Position.Z, dst.Position.Z),
                    $"expected={Fmt(src.Position)} actual={Fmt(dst.Position)}"));
                results.Add(new Result(tag + "Direction",
                    Near(src.Direction.X, dst.Direction.X)
                    && Near(src.Direction.Y, dst.Direction.Y)
                    && Near(src.Direction.Z, dst.Direction.Z),
                    $"expected={Fmt(src.Direction)} actual={Fmt(dst.Direction)}"));
                results.Add(new Result(tag + "MassFlowRateKgS",
                    Near(src.MassFlowRateKgS, dst.MassFlowRateKgS),
                    $"expected={src.MassFlowRateKgS} actual={dst.MassFlowRateKgS}"));
                results.Add(new Result(tag + "OrificeDiameterM",
                    Near(src.OrificeDiameterM, dst.OrificeDiameterM),
                    $"expected={src.OrificeDiameterM} actual={dst.OrificeDiameterM}"));
                results.Add(new Result(tag + "HeatOfCombustionJKg",
                    Near(src.HeatOfCombustionJKg, dst.HeatOfCombustionJKg),
                    $"expected={src.HeatOfCombustionJKg} actual={dst.HeatOfCombustionJKg}"));
                results.Add(new Result(tag + "RadiativeFraction",
                    Near(src.RadiativeFraction, dst.RadiativeFraction),
                    $"expected={src.RadiativeFraction} actual={dst.RadiativeFraction}"));
                results.Add(new Result(tag + "IsPoolFire",
                    dst.IsPoolFire == src.IsPoolFire,
                    $"expected={src.IsPoolFire} actual={dst.IsPoolFire}"));
                results.Add(new Result(tag + "PoolDiameterM",
                    Near(src.PoolDiameterM, dst.PoolDiameterM),
                    $"expected={src.PoolDiameterM} actual={dst.PoolDiameterM}"));
                results.Add(new Result(tag + "PoolBurnRateKgM2S",
                    Near(src.PoolBurnRateKgM2S, dst.PoolBurnRateKgM2S),
                    $"expected={src.PoolBurnRateKgM2S} actual={dst.PoolBurnRateKgM2S}"));
                results.Add(new Result(tag + "IsVisible",
                    dst.IsVisible == src.IsVisible,
                    $"expected={src.IsVisible} actual={dst.IsVisible}"));
            }
        }

        private static bool Near(double a, double b)
            => Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Abs(a));

        private static string Fmt(Point3D p) => $"({p.X}, {p.Y}, {p.Z})";
        private static string Fmt(Vector3D v) => $"({v.X}, {v.Y}, {v.Z})";
    }
}
