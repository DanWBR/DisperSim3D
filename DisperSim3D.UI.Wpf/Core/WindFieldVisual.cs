using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Builds an animated 3D arrow visualization of a <see cref="WindField3D"/>.
    /// Arrows are placed on a regular grid; their alpha and length pulse with time
    /// to simulate the impression of advected particles.
    /// </summary>
    public static class WindFieldVisual
    {
        /// <summary>
        /// Builds the arrows. Call <see cref="UpdateAnimation"/> on the returned visual to advance time.
        /// </summary>
        public static AnimatedArrowField Build(WindField3D field,
            double xMin, double xMax, double yMin, double yMax, double zMax,
            int nxArrows = 24, int nyArrows = 24, int nzArrows = 1,
            Color? arrowColor = null,
            double lengthFactor = 0.30,
            double thicknessFactor = 0.025,
            double maxOpacity = 0.55,
            bool animated = true)
        {
            var arrows = new List<ArrowData>
            {
                // Settings stored on the field so BuildVisual can use them
            };
            var color = arrowColor ?? Colors.Black;
            if (field == null)
                return new AnimatedArrowField
                {
                    Arrows = arrows,
                    ThicknessFactor = thicknessFactor,
                    MaxOpacity = maxOpacity,
                    Animated = animated
                };

            double dx = (xMax - xMin) / nxArrows;
            double dy = (yMax - yMin) / nyArrows;
            double dz = zMax / Math.Max(1, nzArrows);

            double ZAt(int k) => nzArrows == 1
                ? Math.Min(5.0, zMax * 0.1)
                : (k + 0.5) * dz;

            double maxMag = 0.001;
            for (int i = 0; i < nxArrows; i++)
            {
                for (int j = 0; j < nyArrows; j++)
                {
                    for (int k = 0; k < nzArrows; k++)
                    {
                        double x = xMin + (i + 0.5) * dx;
                        double y = yMin + (j + 0.5) * dy;
                        double z = ZAt(k);
                        var v = field.Interpolate(x, y, z);
                        double mag = v.Length;
                        if (mag > maxMag) maxMag = mag;
                    }
                }
            }

            for (int i = 0; i < nxArrows; i++)
            {
                for (int j = 0; j < nyArrows; j++)
                {
                    for (int k = 0; k < nzArrows; k++)
                    {
                        double x = xMin + (i + 0.5) * dx;
                        double y = yMin + (j + 0.5) * dy;
                        double z = ZAt(k);
                        var v = field.Interpolate(x, y, z);
                        double mag = v.Length;
                        if (mag < 0.05) continue;

                        var dir = v;
                        dir.Normalize();
                        double normMag = Math.Min(1.0, mag / maxMag);

                        double phase = ((i * 31) ^ (j * 47) ^ (k * 13)) % 100 / 100.0;
                        double cellMin = Math.Min(dx, dy);
                        double baseLen = cellMin * lengthFactor;

                        arrows.Add(new ArrowData
                        {
                            BasePosition = new Point3D(x, y, z),
                            Direction = dir,
                            BaseLength = baseLen,
                            Magnitude = normMag,
                            Phase = phase,
                            Color = color
                        });
                    }
                }
            }

            return new AnimatedArrowField
            {
                Arrows = arrows,
                ThicknessFactor = thicknessFactor,
                MaxOpacity = maxOpacity,
                Animated = animated
            };
        }
    }

    public class AnimatedArrowField
    {
        public List<ArrowData> Arrows { get; set; } = new List<ArrowData>();
        public double ThicknessFactor { get; set; } = 0.025;
        public double MaxOpacity { get; set; } = 0.55;
        public bool Animated { get; set; } = true;

        /// <summary>
        /// Builds a Model3DGroup at the given animation time (seconds).
        /// Arrow alpha and length pulse with time to evoke flowing wind.
        /// </summary>
        public Model3DGroup BuildVisual(double timeSeconds)
        {
            var group = new Model3DGroup();
            foreach (var a in Arrows)
            {
                double pulse = 1.0;
                double advance = 0;
                if (Animated)
                {
                    double t = (timeSeconds * 0.6 + a.Phase) % 1.0;
                    pulse = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t));
                    advance = a.BaseLength * 0.4 * t;
                }

                double len = a.BaseLength * (0.6 + 0.6 * a.Magnitude) * (0.7 + 0.5 * pulse);

                var from = new Point3D(
                    a.BasePosition.X + a.Direction.X * advance,
                    a.BasePosition.Y + a.Direction.Y * advance,
                    a.BasePosition.Z + a.Direction.Z * advance);
                var to = new Point3D(from.X + a.Direction.X * len,
                                     from.Y + a.Direction.Y * len,
                                     from.Z + a.Direction.Z * len);

                double opacity = MaxOpacity * (0.4 + 0.6 * pulse * a.Magnitude);
                byte alpha = (byte)Math.Max(8, Math.Min(255, opacity * 255));
                var color = Color.FromArgb(alpha, a.Color.R, a.Color.G, a.Color.B);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var mat = new System.Windows.Media.Media3D.DiffuseMaterial(brush);

                var mesh = MakeArrow(from, to, a.BaseLength * ThicknessFactor);
                group.Children.Add(new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = mat,
                    BackMaterial = mat
                });
            }
            group.Freeze();
            return group;
        }

        private static MeshGeometry3D MakeArrow(Point3D from, Point3D to, double radius)
        {
            var mesh = new MeshGeometry3D();
            var dir = to - from;
            double len = dir.Length;
            if (len < 0.001) return mesh;
            dir.Normalize();

            var up = Math.Abs(dir.Z) < 0.99 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var right = Vector3D.CrossProduct(dir, up);
            right.Normalize();
            up = Vector3D.CrossProduct(right, dir);

            int seg = 4;
            double shaftLen = len * 0.75;
            double headRadius = radius * 1.6;

            for (int i = 0; i < seg; i++)
            {
                double a1 = 2 * Math.PI * i / seg;
                double a2 = 2 * Math.PI * ((i + 1) % seg) / seg;
                var r1 = right * Math.Cos(a1) * radius + up * Math.Sin(a1) * radius;
                var r2 = right * Math.Cos(a2) * radius + up * Math.Sin(a2) * radius;
                var p0 = from + r1;
                var p1 = from + r2;
                var p2 = from + dir * shaftLen + r1;
                var p3 = from + dir * shaftLen + r2;
                int b = mesh.Positions.Count;
                mesh.Positions.Add(p0); mesh.Positions.Add(p1);
                mesh.Positions.Add(p2); mesh.Positions.Add(p3);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
                mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 3); mesh.TriangleIndices.Add(b + 2);
                var hr1 = right * Math.Cos(a1) * headRadius + up * Math.Sin(a1) * headRadius;
                var hr2 = right * Math.Cos(a2) * headRadius + up * Math.Sin(a2) * headRadius;
                var hp0 = from + dir * shaftLen + hr1;
                var hp1 = from + dir * shaftLen + hr2;
                b = mesh.Positions.Count;
                mesh.Positions.Add(hp0); mesh.Positions.Add(hp1); mesh.Positions.Add(to);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
            }
            return mesh;
        }
    }

    public class ArrowData
    {
        public Point3D BasePosition;
        public Vector3D Direction;
        public double BaseLength;
        public double Magnitude;
        public double Phase;
        public Color Color;
    }
}
