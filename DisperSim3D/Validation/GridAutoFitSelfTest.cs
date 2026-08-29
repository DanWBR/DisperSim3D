using System;
using System.Collections.Generic;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Checks for <see cref="GridAutoFit"/>: the grid opens up to hold an object
    /// that overhangs it, holds still for one that already fits, and never shrinks.
    ///
    /// The two cases worth pinning are the ones easy to get backwards. Reach is
    /// measured from the origin the grid is centred on, not across the object — a
    /// 30 m crate parked 100 m out needs a far bigger grid than the same crate at
    /// the centre. And the simulation domain is a separate quantity sized later
    /// from its own bounding box, so growing the grid must leave it alone.
    ///
    /// Run via <c>DisperSim3D.CLI --grid-autofit-selftest</c>.
    /// </summary>
    public static class GridAutoFitSelfTest
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

            // ── Required half-size ──────────────────────────────────────────
            {
                // A 30 m cube centred on the origin reaches 15 m; +20% is 18 m.
                Near(results, "centred 30 m cube needs 18 m",
                    GridAutoFit.RequiredHalfSize(Box(-15, -15, 0, 15, 15, 30)), 18.0);

                // Same cube pushed out to x = 100 reaches 115 m, so 138 m. Reach is
                // from the origin, not the width of the box.
                Near(results, "same cube 100 m out needs 138 m",
                    GridAutoFit.RequiredHalfSize(Box(85, -15, 0, 115, 15, 30)), 138.0);

                // Height must not count: the grid is a ground plane.
                Near(results, "height is ignored",
                    GridAutoFit.RequiredHalfSize(Box(-5, -5, 0, 5, 5, 400)), 6.0);

                Near(results, "margin 0.5",
                    GridAutoFit.RequiredHalfSize(Box(-10, -10, 0, 10, 10, 1), 0.5), 15.0);
                Near(results, "margin 0",
                    GridAutoFit.RequiredHalfSize(Box(-10, -10, 0, 10, 10, 1), 0.0), 10.0);
                Near(results, "degenerate box needs nothing",
                    GridAutoFit.RequiredHalfSize(Box(0, 0, 0, 0, 0, 0)), 0.0);
            }

            // ── Growth policy ───────────────────────────────────────────────
            {
                var scene = new Scene3D();
                results.Add(new Result("grid starts at 100 m",
                    Math.Abs(scene.Environment.GridHalfSize - 100.0) < 1e-9,
                    $"got {scene.Environment.GridHalfSize:0.##}"));

                results.Add(new Result("object inside the grid changes nothing",
                    !GridAutoFit.Fit(scene, Box(-5, -5, 0, 5, 5, 5))));
                Near(results, "grid still 100 m", scene.Environment.GridHalfSize, 100.0);

                results.Add(new Result("overhanging object grows the grid",
                    GridAutoFit.Fit(scene, Box(-250, -100, 0, 250, 100, 40))));
                Near(results, "grid grew to 300 m", scene.Environment.GridHalfSize, 300.0);

                results.Add(new Result("a smaller object afterwards does not shrink it",
                    !GridAutoFit.Fit(scene, Box(-20, -20, 0, 20, 20, 10))));
                Near(results, "grid held at 300 m", scene.Environment.GridHalfSize, 300.0);

                var edge = new Scene3D();
                results.Add(new Result("object reaching exactly the grid edge does not grow it",
                    !GridAutoFit.Fit(edge, Box(-83.33, -83.33, 0, 83.33, 83.33, 5))));
            }

            // ── The simulation domain is somebody else's business ───────────
            {
                var scene = new Scene3D();
                var scenario = new DispersionScenario();
                double before = scenario.DomainSizeM;
                scene.DispersionScenarios.Add(scenario);

                GridAutoFit.Fit(scene, Box(-500, -500, 0, 500, 500, 50));

                Near(results, "grid grew to 600 m", scene.Environment.GridHalfSize, 600.0);
                Near(results, "simulation domain untouched", scenario.DomainSizeM, before);
            }

            // ── Model space carried through the decoration transform ────────
            {
                var unit = Box(-0.5, -0.5, -0.5, 0.5, 0.5, 0.5);

                var scaled = GridAutoFit.ToWorld(unit, P(0, 0, 0), V(0, 0, 0), 200);
                Near(results, "scale carries into the world box",
                    GridAutoFit.RequiredHalfSize(scaled), 120.0);

                var moved = GridAutoFit.ToWorld(unit, P(300, 0, 0), V(0, 0, 0), 1);
                Near(results, "translation carries",
                    GridAutoFit.RequiredHalfSize(moved), 300.5 * 1.2);

                // Turning a square 45 degrees stretches its axis-aligned reach by root 2.
                var square = Box(-10, -10, 0, 10, 10, 1);
                Near(results, "45 degrees about Z widens the AABB",
                    GridAutoFit.RequiredHalfSize(
                        GridAutoFit.ToWorld(square, P(0, 0, 0), V(0, 0, 45), 1), 0.0),
                    10.0 * Math.Sqrt(2.0), 1e-9);

                Near(results, "90 degrees about Z is a no-op for a square",
                    GridAutoFit.RequiredHalfSize(
                        GridAutoFit.ToWorld(square, P(0, 0, 0), V(0, 0, 90), 1), 0.0),
                    10.0, 1e-9);
            }

            // ── Bad input ───────────────────────────────────────────────────
            {
                var scene = new Scene3D();
                results.Add(new Result("null box is refused",
                    !GridAutoFit.Fit(scene, (BoundingBox)null)));
                results.Add(new Result("null scene is refused",
                    !GridAutoFit.Fit(null, Box(-500, -500, 0, 500, 500, 5))));
                results.Add(new Result("null local box maps to a null world box",
                    GridAutoFit.ToWorld(null, P(0, 0, 0), V(0, 0, 0), 1) == null));
                Near(results, "grid untouched by bad input",
                    scene.Environment.GridHalfSize, 100.0);
            }

            // -- Authored mesh units -----------------------------------------
            {
                // The guess bets the object is plant-sized. A 40 m vessel authored in
                // each unit has to come back as that unit.
                Unit(results, "40 000 across is millimetres", 40000, ModelUnit.Millimetres);
                Unit(results, "4 000 across is centimetres", 4000, ModelUnit.Centimetres);
                Unit(results, "40 across is metres", 40, ModelUnit.Metres);
                Unit(results, "0.04 across is kilometres", 0.04, ModelUnit.Kilometres);

                // Band edges, so a change to the thresholds shows up here.
                Unit(results, "500 stays metres", 500, ModelUnit.Metres);
                Unit(results, "5 000 stays centimetres", 5000, ModelUnit.Centimetres);
                Unit(results, "a 10 cm fitting is still metres", 0.1, ModelUnit.Metres);
                Unit(results, "nothing to go on gives metres", 0, ModelUnit.Metres);
                Unit(results, "NaN gives metres", double.NaN, ModelUnit.Metres);

                // Each unit converts to the metres the scene works in.
                Near(results, "mm factor", ModelUnits.FactorFor(ModelUnit.Millimetres), 0.001);
                Near(results, "cm factor", ModelUnits.FactorFor(ModelUnit.Centimetres), 0.01);
                Near(results, "m factor", ModelUnits.FactorFor(ModelUnit.Metres), 1.0);
                Near(results, "km factor", ModelUnits.FactorFor(ModelUnit.Kilometres), 1000.0);
                Near(results, "custom has no factor", ModelUnits.FactorFor(ModelUnit.Custom), 1.0);

                // A hand-dialled scale must not claim to be a named unit.
                results.Add(new Result("0.001 matches millimetres",
                    ModelUnits.Match(0.001) == ModelUnit.Millimetres));
                results.Add(new Result("1.0 matches metres",
                    ModelUnits.Match(1.0) == ModelUnit.Metres));
                results.Add(new Result("0.0254 matches nothing named",
                    ModelUnits.Match(0.0254) == ModelUnit.Custom));

                // A 40 000 mm vessel lands at 40 m, so the grid stays at its 100 m default.
                var scene = new Scene3D();
                double mm = ModelUnits.FactorFor(ModelUnit.Millimetres);
                var asMetres = GridAutoFit.ToWorld(
                    Box(-20000, -20000, 0, 20000, 20000, 30000), P(0, 0, 0), V(0, 0, 0), mm);
                results.Add(new Result("vessel read as mm does not grow the grid",
                    !GridAutoFit.Fit(scene, asMetres)));
                Near(results, "grid stayed at 100 m", scene.Environment.GridHalfSize, 100.0);

                // Read as metres it is 40 km across, and the grid has to open up.
                var asKm = GridAutoFit.ToWorld(
                    Box(-20000, -20000, 0, 20000, 20000, 30000), P(0, 0, 0), V(0, 0, 0), 1.0);
                results.Add(new Result("same mesh read as metres does grow the grid",
                    GridAutoFit.Fit(scene, asKm)));
                Near(results, "grid grew to 24 km", scene.Environment.GridHalfSize, 24000.0);
            }

            return results;
        }

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
                ? $"OK — {passed}/{results.Count} grid auto-fit checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static BoundingBox Box(double x0, double y0, double z0,
            double x1, double y1, double z1) =>
            new BoundingBox(new Point3D(x0, y0, z0), new Point3D(x1, y1, z1));

        private static Point3D P(double x, double y, double z) => new Point3D(x, y, z);

        private static Vector3D V(double x, double y, double z) => new Vector3D(x, y, z);

        private static void Unit(List<Result> results, string name,
            double maxExtent, ModelUnit expected)
        {
            var actual = ModelUnits.Guess(maxExtent);
            results.Add(new Result(name, actual == expected, $"expected {expected}, got {actual}"));
        }

        private static void Near(List<Result> results, string name,
            double actual, double expected, double tol = 1e-6)
        {
            results.Add(new Result(name, Math.Abs(actual - expected) <= tol,
                $"expected {expected:0.######}, got {actual:0.######}"));
        }
    }
}
