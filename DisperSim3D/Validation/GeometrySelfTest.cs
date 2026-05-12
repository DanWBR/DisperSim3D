using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Self-test for the portable <see cref="Point3D"/> / <see cref="Vector3D"/>
    /// types. Mirrors the same operators the engine relies on. Run via
    /// <c>DisperSim3D.CLI --geometry-selftest</c> to confirm the portable
    /// primitives produce identical results to their WPF counterparts.
    /// </summary>
    public static class GeometrySelfTest
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
            const double eps = 1e-12;

            // Point3D ctor + accessors
            {
                var p = new Point3D(1, 2, 3);
                results.Add(new Result("Point3D ctor + XYZ",
                    p.X == 1 && p.Y == 2 && p.Z == 3));
            }

            // Vector3D ctor + accessors
            {
                var v = new Vector3D(4, 5, 6);
                results.Add(new Result("Vector3D ctor + XYZ",
                    v.X == 4 && v.Y == 5 && v.Z == 6));
            }

            // Point - Point → Vector
            {
                var a = new Point3D(3, 5, 7);
                var b = new Point3D(1, 2, 3);
                Vector3D v = a - b;
                results.Add(new Result("Point - Point → Vector",
                    v == new Vector3D(2, 3, 4)));
            }

            // Point + Vector → Point
            {
                var p = new Point3D(1, 2, 3);
                var v = new Vector3D(10, 20, 30);
                Point3D q = p + v;
                results.Add(new Result("Point + Vector → Point",
                    q == new Point3D(11, 22, 33)));
            }

            // Vector + Vector, Vector - Vector
            {
                var a = new Vector3D(1, 2, 3);
                var b = new Vector3D(4, 5, 6);
                results.Add(new Result("Vector + Vector",
                    (a + b) == new Vector3D(5, 7, 9)));
                results.Add(new Result("Vector - Vector",
                    (b - a) == new Vector3D(3, 3, 3)));
                results.Add(new Result("Unary -Vector",
                    (-a) == new Vector3D(-1, -2, -3)));
            }

            // Scalar arithmetic
            {
                var v = new Vector3D(2, 4, 6);
                results.Add(new Result("Vector * scalar",
                    (v * 2.5) == new Vector3D(5, 10, 15)));
                results.Add(new Result("scalar * Vector",
                    (3 * v) == new Vector3D(6, 12, 18)));
                results.Add(new Result("Vector / scalar",
                    (v / 2) == new Vector3D(1, 2, 3)));
            }

            // Length / LengthSquared
            {
                var v = new Vector3D(3, 0, 4);
                results.Add(new Result("Vector.Length",
                    Math.Abs(v.Length - 5.0) < eps));
                results.Add(new Result("Vector.LengthSquared",
                    Math.Abs(v.LengthSquared - 25.0) < eps));
            }

            // Normalize (mutates in place)
            {
                var v = new Vector3D(0, 0, 7);
                v.Normalize();
                results.Add(new Result("Vector.Normalize",
                    Math.Abs(v.Length - 1.0) < eps && v.X == 0 && v.Y == 0));
            }

            // Negate (instance + static)
            {
                var v = new Vector3D(1, -2, 3);
                v.Negate();
                results.Add(new Result("Vector.Negate (instance)",
                    v == new Vector3D(-1, 2, -3)));
                results.Add(new Result("Vector3D.Negate (static)",
                    Vector3D.Negate(new Vector3D(1, 2, 3)) == new Vector3D(-1, -2, -3)));
            }

            // Cross product — right-hand rule: x × y = z
            {
                var x = new Vector3D(1, 0, 0);
                var y = new Vector3D(0, 1, 0);
                results.Add(new Result("Vector3D.CrossProduct (x × y = z)",
                    Vector3D.CrossProduct(x, y) == new Vector3D(0, 0, 1)));
            }

            // Dot product
            {
                results.Add(new Result("Vector3D.DotProduct",
                    Math.Abs(Vector3D.DotProduct(
                        new Vector3D(1, 2, 3), new Vector3D(4, -5, 6)) - 12.0) < eps));
            }

            // AngleBetween — 90° for x ⟂ y
            {
                var angle = Vector3D.AngleBetween(
                    new Vector3D(1, 0, 0), new Vector3D(0, 1, 0));
                results.Add(new Result("Vector3D.AngleBetween (x ⟂ y = 90°)",
                    Math.Abs(angle - 90.0) < 1e-9));
            }

            // Explicit cast Point3D → Vector3D
            {
                Vector3D v = (Vector3D)new Point3D(7, 8, 9);
                results.Add(new Result("(Vector3D)Point3D",
                    v == new Vector3D(7, 8, 9)));
            }

            return results;
        }

        /// <summary>Prints results and returns <c>true</c> when every case passes.</summary>
        public static bool RunAndPrint(System.IO.TextWriter writer)
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
                ? $"OK — {passed}/{results.Count} geometry self-tests passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }
    }
}
