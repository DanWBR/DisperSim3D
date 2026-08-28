using System;
using System.Collections.Generic;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Checks for <see cref="FlashFireEngine"/> on synthetic concentration fields, where
    /// the right answer is known in closed form.
    ///
    /// The case that matters most is the two-pocket one: a cloud split by a wall must
    /// burn on the side that was lit and nowhere else. That is the whole point of doing
    /// a connected-component flood fill instead of simply integrating every flammable
    /// cell in the domain, and it is the check that fails if the obstacle test or the
    /// neighbour walk regresses.
    ///
    /// Run via <c>DisperSim3D.CLI --flash-fire-selftest</c>.
    /// </summary>
    public static class FlashFireSelfTest
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

        private const double Lfl = 0.033;   // methane, kg/m³
        private const double Ufl = 0.099;
        private const int N = 40;           // cells per side
        private const double Cell = 1.0;    // m

        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();

            // ── A single slab of flammable gas ──────────────────────────────
            {
                // Cells 10..29 in x, 10..29 in y, 0..9 in z hold gas at 1.5×LFL.
                var field = new double[N, N, N];
                FillBox(field, 10, 29, 10, 29, 0, 9, 1.5 * Lfl);

                var ignition = MakeIgnition(new Point3D(20, 20, 5));
                var flash = Compute(field, ignition, null);

                results.Add(new Result("a single pocket ignites",
                    flash.Ignited, $"ignited={flash.Ignited}"));
                results.Add(new Result("one connected component",
                    flash.ConnectedComponents == 1,
                    $"components={flash.ConnectedComponents}"));

                // Every cell of the slab is above 0.5×LFL, so the whole slab burns.
                double expectedVolume = 20 * 20 * 10 * Cell * Cell * Cell;
                results.Add(new Result("burnt volume equals the flammable slab",
                    Math.Abs(flash.EnvelopeVolumeM3 - expectedVolume) < 1e-6,
                    $"expected={expectedVolume:F0} m³ actual={flash.EnvelopeVolumeM3:F0} m³"));

                double expectedMass = expectedVolume * 1.5 * Lfl;
                results.Add(new Result("burnt mass is the fuel inside the envelope",
                    Math.Abs(flash.BurnedMassKg - expectedMass) < 1e-6,
                    $"expected={expectedMass:F2} kg actual={flash.BurnedMassKg:F2} kg"));

                // Free-field burn-back: duration is reach over flame speed.
                results.Add(new Result("duration is the reach over the flame speed",
                    Math.Abs(flash.DurationS - flash.MaxReachM / ignition.FlameSpeedMS) < 1e-9,
                    $"reach={flash.MaxReachM:F1} m, {flash.DurationS:F2} s at {ignition.FlameSpeedMS} m/s"));

                results.Add(new Result("the ignition cell is reached at t = 0",
                    flash.ArrivalTimeS[flash.IgnitionCell.I, flash.IgnitionCell.J, flash.IgnitionCell.K] == 0));

                results.Add(new Result("cells outside the cloud are never reached",
                    flash.ArrivalTimeS[0, 0, 0] == FlashFireEngine.UnreachedArrivalS
                    && flash.EnvelopeMask[0, 0, 0] == 0));
            }

            // ── Envelope reaches below the LFL ──────────────────────────────
            {
                // A flammable core surrounded by a lean skirt at 0.7×LFL. The skirt is
                // outside the LFL contour but inside the half-LFL envelope.
                var field = new double[N, N, N];
                FillBox(field, 5, 34, 5, 34, 0, 9, 0.7 * Lfl);
                FillBox(field, 15, 24, 15, 24, 0, 9, 1.5 * Lfl);

                var flash = Compute(field, MakeIgnition(new Point3D(20, 20, 5)), null);

                double coreVolume = 10 * 10 * 10 * Cell * Cell * Cell;
                double totalVolume = 30 * 30 * 10 * Cell * Cell * Cell;
                results.Add(new Result("the envelope includes the lean skirt",
                    Math.Abs(flash.EnvelopeVolumeM3 - totalVolume) < 1e-6,
                    $"expected={totalVolume:F0} m³ actual={flash.EnvelopeVolumeM3:F0} m³"));
                results.Add(new Result("the flammable core is reported separately",
                    Math.Abs(flash.FlammableVolumeM3 - coreVolume) < 1e-6,
                    $"expected={coreVolume:F0} m³ actual={flash.FlammableVolumeM3:F0} m³"));
            }

            // ── Two pockets, one wall ───────────────────────────────────────
            {
                // Two slabs with a one-cell gap between them: lighting one must not
                // reach the other.
                var field = new double[N, N, N];
                FillBox(field, 5, 14, 10, 29, 0, 9, 1.5 * Lfl);
                FillBox(field, 16, 25, 10, 29, 0, 9, 1.5 * Lfl);

                var flash = Compute(field, MakeIgnition(new Point3D(10, 20, 5)), null);
                double onePocket = 10 * 20 * 10 * Cell * Cell * Cell;

                results.Add(new Result("two separated pockets are counted",
                    flash.ConnectedComponents == 2,
                    $"components={flash.ConnectedComponents}"));
                results.Add(new Result("only the ignited pocket burns",
                    Math.Abs(flash.EnvelopeVolumeM3 - onePocket) < 1e-6,
                    $"expected={onePocket:F0} m³ actual={flash.EnvelopeVolumeM3:F0} m³"));
            }

            // ── A wall through a single pocket ──────────────────────────────
            {
                // One continuous slab, split in two by a solid box. The gas is still
                // connected in the concentration field, but the flame cannot cross.
                var field = new double[N, N, N];
                FillBox(field, 5, 34, 10, 29, 0, 9, 1.5 * Lfl);

                var wall = new List<BoundingBox>
                {
                    new BoundingBox(new Point3D(19.5, 5, -1), new Point3D(20.5, 35, 12))
                };

                var withoutWall = Compute(field, MakeIgnition(new Point3D(10, 20, 5)), null);
                var withWall = Compute(field, MakeIgnition(new Point3D(10, 20, 5)), wall);

                results.Add(new Result("without the wall the whole slab burns",
                    Math.Abs(withoutWall.EnvelopeVolumeM3 - 30 * 20 * 10.0) < 1e-6,
                    $"actual={withoutWall.EnvelopeVolumeM3:F0} m³"));
                results.Add(new Result("a wall stops the flame at roughly half the slab",
                    withWall.EnvelopeVolumeM3 < withoutWall.EnvelopeVolumeM3 * 0.6
                    && withWall.EnvelopeVolumeM3 > withoutWall.EnvelopeVolumeM3 * 0.4,
                    $"com parede={withWall.EnvelopeVolumeM3:F0} m³ "
                    + $"sem parede={withoutWall.EnvelopeVolumeM3:F0} m³"));
            }

            // ── Burn-back has to go around the obstacle ─────────────────────
            {
                // A U-shaped barrier forces the flame the long way round, so the arrival
                // time at a cell behind it exceeds the straight-line time.
                var field = new double[N, N, N];
                FillBox(field, 5, 34, 5, 34, 0, 9, 1.5 * Lfl);

                var barrier = new List<BoundingBox>
                {
                    new BoundingBox(new Point3D(19.5, 5, -1), new Point3D(20.5, 25, 12))
                };

                var flash = Compute(field, MakeIgnition(new Point3D(10, 15, 5)), barrier);
                // A point just behind the barrier: straight line is ~15 m, but the flame
                // has to come round the open end at y > 25.
                double arrival = flash.ArrivalTimeS[25, 15, 5];
                double straightLine = Math.Sqrt(15.0 * 15.0 + 0.0) / 10.0;
                results.Add(new Result("the flame goes around a barrier instead of through it",
                    arrival > straightLine * 1.5 && arrival < FlashFireEngine.UnreachedArrivalS,
                    $"contornando={arrival:F2} s, linha reta={straightLine:F2} s"));
            }

            // ── Degenerate inputs ───────────────────────────────────────────
            {
                var field = new double[N, N, N];
                FillBox(field, 10, 29, 10, 29, 0, 9, 1.5 * Lfl);

                var far = Compute(field, MakeIgnition(new Point3D(38, 38, 15)), null);
                results.Add(new Result("igniting outside the cloud burns nothing",
                    !far.Ignited && far.EnvelopeVolumeM3 == 0 && far.EnvelopeMask[20, 20, 5] == 0,
                    $"ignited={far.Ignited} volume={far.EnvelopeVolumeM3:F0} m³"));

                var lean = new double[N, N, N];
                FillBox(lean, 10, 29, 10, 29, 0, 9, 0.7 * Lfl);   // inside the envelope, below the LFL
                var leanFlash = Compute(lean, MakeIgnition(new Point3D(20, 20, 5)), null);
                results.Add(new Result("a cloud below the LFL does not light",
                    !leanFlash.Ignited,
                    $"ignited={leanFlash.Ignited}"));

                var empty = Compute(new double[N, N, N], MakeIgnition(new Point3D(20, 20, 5)), null);
                results.Add(new Result("an empty field is handled without throwing",
                    !empty.Ignited && empty.ConnectedComponents == 0));
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
                ? $"OK — {passed}/{results.Count} flash-fire checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        private static FlashFireEngine.FlashFireResult Compute(
            double[,,] field, IgnitionEvent ignition, IList<BoundingBox> obstacles)
            => FlashFireEngine.Compute(field, Lfl, Ufl, ignition,
                0, 0, 0, Cell, Cell, Cell, obstacles);

        private static IgnitionEvent MakeIgnition(Point3D position) => new IgnitionEvent
        {
            Name = "test",
            Position = position,
            TimeS = 0,
            EnvelopeFraction = 0.5,
            FlameSpeedMS = 10.0
        };

        private static void FillBox(double[,,] field,
            int i0, int i1, int j0, int j1, int k0, int k1, double value)
        {
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    for (int k = k0; k <= k1; k++)
                        field[i, j, k] = value;
        }
    }
}
