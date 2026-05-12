using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Renders 3D flame visuals and thermal radiation contours for fire sources.
    /// Generates conical flame geometry deflected by wind and concentric ground-level radiation rings.
    /// </summary>
    public static class FireRenderer
    {
        /// <summary>
        /// Generates a 3D conical flame visual for the specified fire source, deflected by the wind vector.
        /// The cone base radius is derived from pool or orifice diameter; the tip is computed via the jet fire model.
        /// </summary>
        /// <param name="source">The fire source defining position, type, and dimensional parameters.</param>
        /// <param name="windVector">The wind velocity vector used to compute flame tilt and tip position.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the flame cone mesh with a yellow-to-red gradient material, or an empty visual if the flame length is negligible.</returns>
        public static ModelVisual3D GenerateFlameVisual(FireSource source, Vector3D windVector)
        {
            var visual = new ModelVisual3D();
            var group = new Model3DGroup();

            var tip = JetFireModel.FlameTip(source, windVector);
            var basePos = source.Position;
            double L = (tip - basePos).Length;
            if (L < 0.1) return visual;

            var dir = tip - basePos;
            dir.Normalize();

            int segments = 8;
            double baseRadius = source.IsPoolFire ? source.PoolDiameterM * 0.5 : source.OrificeDiameterM * 3 + 0.3;

            var mesh = new MeshGeometry3D();

            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                var up = Math.Abs(dir.Z) < 0.99 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                var right = Vector3D.CrossProduct(dir, up);
                right.Normalize();
                up = Vector3D.CrossProduct(right, dir);

                var r = right * Math.Cos(a) * baseRadius + up * Math.Sin(a) * baseRadius;
                mesh.Positions.Add(basePos + r);
            }
            mesh.Positions.Add(tip);
            int tipIdx = mesh.Positions.Count - 1;

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                mesh.TriangleIndices.Add(i);
                mesh.TriangleIndices.Add(next);
                mesh.TriangleIndices.Add(tipIdx);
            }

            var gradient = new LinearGradientBrush();
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(220, 255, 200, 0), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(200, 255, 80, 0), 0.5));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(150, 200, 30, 0), 1.0));
            gradient.Freeze();

            var material = new DiffuseMaterial(gradient);
            group.Children.Add(new GeometryModel3D { Geometry = mesh, Material = material, BackMaterial = material });

            visual.Content = group;
            return visual;
        }

        /// <summary>
        /// Generates concentric ring contours on the ground plane representing thermal radiation intensity levels around a fire source.
        /// Each ring corresponds to a radiation threshold and is color-coded from yellow (lowest) through orange to red (highest).
        /// </summary>
        /// <param name="source">The fire source used to compute radiation distances.</param>
        /// <param name="levels">A list of radiation intensity levels (kW/m^2) for which to generate contour rings. At most three levels are rendered.</param>
        /// <param name="groundZ">The Z-coordinate of the ground plane on which rings are drawn. Defaults to 0.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the radiation contour ring meshes.</returns>
        public static ModelVisual3D GenerateRadiationContours(
            FireSource source, List<double> levels, double groundZ = 0)
        {
            var visual = new ModelVisual3D();
            var group = new Model3DGroup();

            Color[] colors = {
                Color.FromArgb(100, 255, 255, 0),
                Color.FromArgb(100, 255, 140, 0),
                Color.FromArgb(100, 255, 0, 0)
            };

            for (int i = 0; i < levels.Count && i < colors.Length; i++)
            {
                double radius = JetFireModel.RadiationDistanceForLevel(source, levels[i]);
                if (radius > 500 || radius < 0.5) continue;

                var ringMesh = CreateRing(source.Position.X, source.Position.Y, groundZ, radius, 24);
                var brush = new SolidColorBrush(colors[i]);
                brush.Freeze();
                var mat = new DiffuseMaterial(brush);
                group.Children.Add(new GeometryModel3D { Geometry = ringMesh, Material = mat, BackMaterial = mat });
            }

            visual.Content = group;
            return visual;
        }

        private static MeshGeometry3D CreateRing(double cx, double cy, double z, double radius, int segments)
        {
            var mesh = new MeshGeometry3D();
            double innerR = radius * 0.95;

            for (int i = 0; i <= segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                mesh.Positions.Add(new Point3D(cx + innerR * Math.Cos(a), cy + innerR * Math.Sin(a), z));
                mesh.Positions.Add(new Point3D(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a), z));
            }

            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 3);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 3); mesh.TriangleIndices.Add(b + 2);
            }

            return mesh;
        }
    }
}
