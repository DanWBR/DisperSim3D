using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Builds procedural texture brushes for industrial decoration materials and
    /// auto-generates texture coordinates on imported meshes (which usually lack
    /// UVs — STL has none, OBJ often omits them). Uses cylindrical projection
    /// around Z, since the bulk of refinery equipment (tanks, vessels, pipes) is
    /// vertical and roughly axisymmetric. Flat panels still get reasonable
    /// patterning thanks to the V-axis falling out as simple height.
    /// </summary>
    internal static class DecorationTextureRenderer
    {
        /// <summary>Generates a tiled texture brush for the given material type.
        /// <paramref name="tint"/> mixes user-picked colour into the procedural
        /// pattern so each instance can still be customised.</summary>
        public static Brush BuildBrush(MaterialType3D type, Color tint)
        {
            switch (type)
            {
                case MaterialType3D.RustedMetal:     return MakeRust(tint);
                case MaterialType3D.GalvanizedMetal: return MakeGalvanized(tint);
                case MaterialType3D.BrushedMetal:    return MakeBrushed(tint);
                case MaterialType3D.PaintedMetal:    return MakePainted(tint);
                case MaterialType3D.Concrete:        return MakeConcrete(tint);
                default:                             return new SolidColorBrush(tint);
            }
        }

        /// <summary>True when the type benefits from generated UVs (i.e. uses a
        /// non-uniform brush). Saves us the cost of touching meshes that are still
        /// SolidColor-based.</summary>
        public static bool NeedsUV(MaterialType3D type)
        {
            switch (type)
            {
                case MaterialType3D.RustedMetal:
                case MaterialType3D.GalvanizedMetal:
                case MaterialType3D.BrushedMetal:
                case MaterialType3D.PaintedMetal:
                case MaterialType3D.Concrete:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Cylindrical projection of mesh vertices onto (u, v) where
        /// u = atan2(y, x) wrapped to [0,1] and v = (z - zmin) / (zmax - zmin).
        /// Overwrites any existing TextureCoordinates — imported meshes that DO
        /// have UVs already shouldn't be using these procedural materials anyway.</summary>
        public static void GenerateCylindricalUVs(MeshGeometry3D mesh)
        {
            if (mesh == null || mesh.Positions.Count == 0) return;
            double zmin = double.MaxValue, zmax = double.MinValue;
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                double z = mesh.Positions[i].Z;
                if (z < zmin) zmin = z;
                if (z > zmax) zmax = z;
            }
            double zr = Math.Max(zmax - zmin, 1e-6);
            // Each cylindrical revolution maps the brush once; for tall vertical
            // surfaces we tile vertically every 4 metres.
            const double vTileM = 4.0;
            double vScale = zr / vTileM;
            if (vScale < 1) vScale = 1;

            var newUVs = new PointCollection(mesh.Positions.Count);
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                var p = mesh.Positions[i];
                double u = (Math.Atan2(p.Y, p.X) + Math.PI) / (2 * Math.PI); // 0..1
                // Multiply by ~3 to give a few wraps around tanks so the texture
                // doesn't smear unrealistically wide.
                u *= 3.0;
                double v = (p.Z - zmin) / zr * vScale;
                newUVs.Add(new Point(u, v));
            }
            mesh.TextureCoordinates = newUVs;
        }

        // ── procedural brushes ──

        private static Brush MakeRust(Color tint)
        {
            var dg = new DrawingGroup();
            // Dark steel base — mix toward gunmetal regardless of tint so rust pops.
            byte br = (byte)((tint.R + 60) / 2);
            byte bg = (byte)((tint.G + 60) / 2);
            byte bb = (byte)((tint.B + 65) / 2);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(br, bg, bb)), null,
                new RectangleGeometry(new Rect(0, 0, 100, 100))));
            // Orange-brown rust patches of varying density and size.
            var rnd = new Random(17);
            for (int i = 0; i < 220; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y = rnd.NextDouble() * 100;
                double r = 1.5 + rnd.NextDouble() * 4.5;
                byte rR = (byte)(140 + rnd.Next(80));
                byte rG = (byte)(60 + rnd.Next(60));
                byte rB = (byte)(20 + rnd.Next(40));
                byte a = (byte)(140 + rnd.Next(110));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, rR, rG, rB)), null,
                    new EllipseGeometry(new Point(x, y), r, r * (0.7 + rnd.NextDouble() * 0.6))));
            }
            // Dark streaks running down (V direction = vertical on a tank wall).
            var streakPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 30, 18, 8)), 0.3);
            for (int i = 0; i < 12; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y1 = rnd.NextDouble() * 50;
                double y2 = y1 + 30 + rnd.NextDouble() * 60;
                dg.Children.Add(new GeometryDrawing(null, streakPen,
                    new LineGeometry(new Point(x, y1), new Point(x + (rnd.NextDouble() - 0.5) * 4, y2))));
            }
            return FreezeBrush(dg);
        }

        private static Brush MakeGalvanized(Color tint)
        {
            var dg = new DrawingGroup();
            byte br = (byte)((tint.R + 195) / 2);
            byte bg = (byte)((tint.G + 200) / 2);
            byte bb = (byte)((tint.B + 210) / 2);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(br, bg, bb)), null,
                new RectangleGeometry(new Rect(0, 0, 100, 100))));
            // Spangle pattern — overlapping polygonal "crystals" of light/dark grey.
            var rnd = new Random(53);
            for (int i = 0; i < 120; i++)
            {
                double cx = rnd.NextDouble() * 100;
                double cy = rnd.NextDouble() * 100;
                double r = 2 + rnd.NextDouble() * 5;
                int sides = 5 + rnd.Next(3);
                var poly = new System.Windows.Media.PathGeometry();
                var fig = new System.Windows.Media.PathFigure();
                double startAngle = rnd.NextDouble() * Math.PI * 2;
                fig.StartPoint = new Point(cx + r * Math.Cos(startAngle), cy + r * Math.Sin(startAngle));
                for (int s = 1; s < sides; s++)
                {
                    double a = startAngle + 2 * Math.PI * s / sides;
                    fig.Segments.Add(new System.Windows.Media.LineSegment(
                        new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)), true));
                }
                fig.IsClosed = true;
                poly.Figures.Add(fig);
                byte v = (byte)(170 + rnd.Next(60));
                byte alpha = (byte)(60 + rnd.Next(80));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(alpha, v, v, (byte)(v + 5))), null, poly));
            }
            return FreezeBrush(dg);
        }

        private static Brush MakeBrushed(Color tint)
        {
            var dg = new DrawingGroup();
            byte br = (byte)((tint.R + 210) / 2);
            byte bg = (byte)((tint.G + 210) / 2);
            byte bb = (byte)((tint.B + 215) / 2);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(br, bg, bb)), null,
                new RectangleGeometry(new Rect(0, 0, 100, 100))));
            // Horizontal brushed lines — short, varying intensity, parallel.
            var rnd = new Random(91);
            for (int i = 0; i < 600; i++)
            {
                double y = rnd.NextDouble() * 100;
                double x1 = rnd.NextDouble() * 100;
                double w = 4 + rnd.NextDouble() * 12;
                byte v = (byte)(150 + rnd.Next(80));
                byte a = (byte)(40 + rnd.Next(70));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(a, v, v, v)), 0.25);
                dg.Children.Add(new GeometryDrawing(null, pen,
                    new LineGeometry(new Point(x1, y), new Point(x1 + w, y))));
            }
            return FreezeBrush(dg);
        }

        private static Brush MakePainted(Color tint)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(tint), null,
                new RectangleGeometry(new Rect(0, 0, 100, 100))));
            var rnd = new Random(7);
            // Random rivets along simulated panel seams.
            for (int seam = 0; seam < 4; seam++)
            {
                double y = seam * 25 + 2;
                for (int r = 0; r < 18; r++)
                {
                    double x = 3 + r * 5.5;
                    byte v = (byte)(40 + rnd.Next(40));
                    dg.Children.Add(new GeometryDrawing(
                        new SolidColorBrush(Color.FromArgb(180, v, v, v)), null,
                        new EllipseGeometry(new Point(x, y), 0.5, 0.5)));
                }
            }
            // Chip damage — small bare-metal patches.
            for (int i = 0; i < 25; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y = rnd.NextDouble() * 100;
                double r = 0.4 + rnd.NextDouble() * 1.2;
                byte v = (byte)(120 + rnd.Next(60));
                byte a = (byte)(120 + rnd.Next(100));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, v, v, v)), null,
                    new EllipseGeometry(new Point(x, y), r, r)));
            }
            // Weathering streaks fading downward.
            var sPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 0.3);
            for (int i = 0; i < 8; i++)
            {
                double x = rnd.NextDouble() * 100;
                dg.Children.Add(new GeometryDrawing(null, sPen,
                    new LineGeometry(new Point(x, 0), new Point(x + (rnd.NextDouble() - 0.5) * 6, 100))));
            }
            return FreezeBrush(dg);
        }

        private static Brush MakeConcrete(Color tint)
        {
            var dg = new DrawingGroup();
            byte br = (byte)((tint.R + 190) / 2);
            byte bg = (byte)((tint.G + 190) / 2);
            byte bb = (byte)((tint.B + 188) / 2);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(br, bg, bb)), null,
                new RectangleGeometry(new Rect(0, 0, 100, 100))));
            var rnd = new Random(41);
            // Aggregate noise.
            for (int i = 0; i < 600; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y = rnd.NextDouble() * 100;
                double r = 0.2 + rnd.NextDouble() * 0.8;
                byte v = (byte)(150 + rnd.Next(80));
                byte a = (byte)(50 + rnd.Next(100));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, v, v, v)), null,
                    new EllipseGeometry(new Point(x, y), r, r)));
            }
            // Slab joints every 50 units.
            var jp = new Pen(new SolidColorBrush(Color.FromArgb(120, 80, 80, 80)), 0.4);
            dg.Children.Add(new GeometryDrawing(null, jp,
                new LineGeometry(new Point(0, 50), new Point(100, 50))));
            dg.Children.Add(new GeometryDrawing(null, jp,
                new LineGeometry(new Point(50, 0), new Point(50, 100))));
            return FreezeBrush(dg);
        }

        private static Brush FreezeBrush(DrawingGroup dg)
        {
            var b = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 1, 1), ViewportUnits = BrushMappingMode.RelativeToBoundingBox };
            b.Freeze();
            return b;
        }
    }
}
