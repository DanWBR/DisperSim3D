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
    /// The FluidX3D tracers block the cells the decorations' meshes occupy, not
    /// their bounding boxes. Checks, on a 0.5 m grid: a closed cube is solid inside
    /// and free around; an open canopy turned 45° blocks only its own layer; paving
    /// at ground level blocks nothing; a primitive with no mesh falls back to its
    /// box; a decoration's own box passed alongside its mesh is not applied again;
    /// and a tracer run keeps the gas out of the cube while it flows past.
    ///
    /// Run via <c>DisperSim3D.CLI --obstacle-mask-selftest</c>.
    /// </summary>
    public static class ObstacleMaskSelfTest
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

        private const double Half = 10, Height = 10;
        private const int Nx = 40, Nz = 20; // 0.5 m cells

        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();
            string dir = Path.Combine(Path.GetTempPath(), "dispersim-solids-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string cube = Path.Combine(dir, "cube.stl");
                WriteCubeStl(cube, 4);
                string canopy = Path.Combine(dir, "canopy.obj");
                File.WriteAllText(canopy, "v -2 -2 3\nv 2 -2 3\nv 2 2 3\nv -2 2 3\nf 1 2 3 4\n");
                string paving = Path.Combine(dir, "paving.obj");
                File.WriteAllText(paving, "v -10 -10 0.05\nv 10 -10 0.05\nv 10 10 0.05\nv -10 10 0.05\nf 1/1/1 2/2/1 3/3/1 4/4/1\n");

                var cubeDeco = new Decoration3D { Name = "cube", FilePath = cube, Position = new Point3D(-6, -6, 0),
                    BoundingBox = new BoundingBox(new Point3D(-10, -10, 0), new Point3D(10, 10, 10)) };
                var canopyDeco = new Decoration3D { Name = "canopy", FilePath = canopy, Position = new Point3D(5, 5, 0),
                    Rotation = new Vector3D(0, 0, 45) };
                var pavingDeco = new Decoration3D { Name = "paving", FilePath = paving };
                var box = new BoundingBox(new Point3D(6.2, -7.8, 0), new Point3D(7.8, -6.2, 1.8));
                var primitive = new Decoration3D { Name = "primitive", FilePath = "primitive:cube", BoundingBox = box };

                var solids = DecorationSolids.FromDecorations(new[] { cubeDeco, canopyDeco, pavingDeco, primitive });
                results.Add(new Result("three meshes read, the primitive kept as its box",
                    solids.MeshCount == 3 && solids.Boxes.Count == 1,
                    Fmt("{0} meshes, {1} triangles, {2} box(es)", solids.MeshCount, solids.Triangles.TriangleCount, solids.Boxes.Count)));

                var stray = new BoundingBox(new Point3D(-9.8, 8.2, 0), new Point3D(-8.2, 9.8, 1.8));
                bool[,,] mask = DecorationSolids.ObstacleMask(
                    new List<BoundingBox> { cubeDeco.BoundingBox, box, stray }, solids, Half, Height, Nx, Nx, Nz);

                results.Add(new Result("the closed cube is solid inside",
                    At(mask, -4, -4, 2) && At(mask, -5.7, -2.3, 0.3) && At(mask, -4, -4, 3.7)));
                results.Add(new Result("around and above the cube is free",
                    !At(mask, -4, -4, 5.2) && !At(mask, -1.2, -4, 2) && !At(mask, -4, -0.7, 2) && !At(mask, 0, 0, 2)));
                results.Add(new Result("the canopy, turned 45°, blocks its own layer only",
                    At(mask, 5, 5, 3.2) && At(mask, 5 + 2.4, 5, 3.2) && !At(mask, 5 + 1.9, 5 + 1.9, 3.2)
                    && !At(mask, 5, 5, 1.2) && !At(mask, 5, 5, 4.7)));
                results.Add(new Result("paving at ground level blocks nothing",
                    !At(mask, 0, 0, 0.25) && !At(mask, 8, 8, 0.25)));
                results.Add(new Result("the primitive's box blocks its cells",
                    At(mask, 7, -7, 1)));
                results.Add(new Result("a decoration's own box is not applied over its mesh",
                    solids.Represents(cubeDeco.BoundingBox) && !At(mask, 0, 0, 5)));
                results.Add(new Result("a box from no decoration still blocks",
                    At(mask, -9, 9, 1)));

                bool[,,] boxes = DecorationSolids.ObstacleMask(new List<BoundingBox> { box }, null, Half, Height, Nx, Nx, Nz);
                results.Add(new Result("without meshes, boxes block as before",
                    At(boxes, 7, -7, 1) && !At(boxes, -4, -4, 2)));

                CheckTracer(results, cubeDeco);
            }
            catch (Exception ex)
            {
                results.Add(new Result("obstacle masks built", false, ex.GetType().Name + ": " + ex.Message));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
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
                ? $"OK — {passed}/{results.Count} obstacle mask checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        /// <summary>Gas released upwind of the cube flows past it and never inside.</summary>
        private static void CheckTracer(List<Result> results, Decoration3D cube)
        {
            int n = 20;
            var ux = new double[n, n, n];
            var uy = new double[n, n, n];
            var uz = new double[n, n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                        ux[i, j, k] = 1.0;
            var wind = new WindField3D(ux, uy, uz, -Half, Half, -Half, Half, Height);

            var solids = DecorationSolids.FromDecorations(new[] { cube });
            var engine = new DispersionTracerEngine(wind, Half, Height, Nx, Nx, Nz, 0.05, 0, null, solids);
            engine.SetMassSource(-9, -4, 2, 0.75, 1.0, 1.2);
            for (int s = 0; s < 400; s++) engine.Step(0.05);

            double[,,] c = engine.Snapshot();
            double inside = Value(c, -4, -4, 2), past = Value(c, 0, -4, 2), upwind = Value(c, -7, -4, 2);
            results.Add(new Result("a tracer run keeps the gas out of the cube",
                inside == 0 && upwind > 0 && past >= 0,
                Fmt("inside {0:G3}, upwind {1:G3}, past {2:G3} kg/m3", inside, upwind, past)));
        }

        private static bool At(bool[,,] mask, double x, double y, double z)
        {
            int i = (int)Math.Floor((x + Half) / (2 * Half / Nx));
            int j = (int)Math.Floor((y + Half) / (2 * Half / Nx));
            int k = (int)Math.Floor(z / (Height / Nz));
            return mask[i, j, k];
        }

        private static double Value(double[,,] c, double x, double y, double z)
        {
            int i = (int)Math.Floor((x + Half) / (2 * Half / Nx));
            int j = (int)Math.Floor((y + Half) / (2 * Half / Nx));
            int k = (int)Math.Floor(z / (Height / Nz));
            return c[i, j, k];
        }

        /// <summary>A closed cube of the given edge, corner at the origin, as a binary STL.</summary>
        private static void WriteCubeStl(string path, float a)
        {
            var v = new[]
            {
                new[] { 0f, 0, 0 }, new[] { a, 0, 0 }, new[] { a, a, 0 }, new[] { 0f, a, 0 },
                new[] { 0f, 0, a }, new[] { a, 0, a }, new[] { a, a, a }, new[] { 0f, a, a }
            };
            int[][] faces =
            {
                new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
                new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 },
                new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 }
            };
            using var w = new BinaryWriter(File.Create(path));
            w.Write(new byte[80]);
            w.Write((uint)faces.Length);
            foreach (var f in faces)
            {
                w.Write(0f); w.Write(0f); w.Write(0f);
                foreach (int corner in f)
                    foreach (float c in v[corner]) w.Write(c);
                w.Write((ushort)0);
            }
        }

        private static string Fmt(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
