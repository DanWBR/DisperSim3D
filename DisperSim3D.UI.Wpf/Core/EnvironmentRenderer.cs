using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Builds the visual ingredients of the project Environment — sun + ambient
    /// lights, sky dome, ground texture. WPF Viewport3D has no shaders/shadows,
    /// so this is "pretty enough" rather than physically based. All routines
    /// return fresh ModelVisual3D / MaterialGroup instances ready to drop into
    /// the editor viewport.
    /// </summary>
    internal static class EnvironmentRenderer
    {
        /// <summary>Builds the lighting visual (one directional sun + ambient + small
        /// rim back-light to soften the dark side). Returns null when env.UseSunLighting
        /// is false (caller should fall back to HelixToolkit's DefaultLights).</summary>
        public static ModelVisual3D BuildLighting(EnvironmentSettings env)
        {
            if (env == null || !env.UseSunLighting) return null;

            var group = new Model3DGroup();

            // Sun direction: convert (az, el) to a vector pointing FROM the sun toward
            // the scene origin. WPF DirectionalLight.Direction is the photon direction.
            double azRad = env.SunAzimuthDeg * Math.PI / 180.0;
            double elRad = env.SunElevationDeg * Math.PI / 180.0;
            double cosEl = Math.Cos(elRad);
            var sunDir = new Vector3D(
                cosEl * Math.Sin(azRad),
                cosEl * Math.Cos(azRad),
                Math.Sin(elRad));
            sunDir.Normalize();
            sunDir.Negate(); // photons travel down toward the scene

            // Warm sun colour shifts toward orange at low elevation.
            double warmth = Math.Max(0, 1 - env.SunElevationDeg / 90.0);
            byte sunR = 255;
            byte sunG = (byte)(245 - warmth * 60);
            byte sunB = (byte)(230 - warmth * 130);
            double sunMul = Math.Max(0, Math.Min(2.0, env.SunIntensity));
            var sunColor = ScaleColor(Color.FromRgb(sunR, sunG, sunB), sunMul);
            group.Children.Add(new DirectionalLight(sunColor, sunDir));

            // Subtle rim light from the opposite side so dark faces aren't pitch black —
            // mimics sky bounce. Cool blue tint.
            var rimDir = new Vector3D(-sunDir.X, -sunDir.Y, Math.Max(-0.4, -sunDir.Z));
            rimDir.Normalize();
            var rimColor = ScaleColor(Color.FromRgb(140, 160, 200), 0.25 * sunMul);
            group.Children.Add(new DirectionalLight(rimColor, rimDir));

            // Ambient fill — sky tinted, hemispheric.
            double ambMul = Math.Max(0, Math.Min(1.0, env.AmbientIntensity));
            byte ar = (byte)(110 * ambMul);
            byte ag = (byte)(125 * ambMul);
            byte ab = (byte)(150 * ambMul);
            group.Children.Add(new AmbientLight(Color.FromRgb(ar, ag, ab)));

            return new ModelVisual3D { Content = group };
        }

        private static BitmapSource LoadImageSafe(string path, int maxPixelWidth)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                if (maxPixelWidth > 0)
                    bmp.DecodePixelWidth = maxPixelWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Builds the sky-dome visual: a large sphere centred on origin
        /// painted on the INSIDE with a vertical zenith→horizon gradient. The dome
        /// radius is sized to enclose <paramref name="sceneHalfM"/> with margin so
        /// it never clips into the camera. Returns null when disabled.</summary>
        public static ModelVisual3D BuildSkyDome(EnvironmentSettings env, double sceneHalfM)
        {
            if (env == null || !env.SkydomeEnabled) return null;

            bool useTexture = !string.IsNullOrEmpty(env.SkyTexturePath);
            BitmapSource skyBmp = null;
            if (useTexture)
            {
                string resolved = BuiltinAssetResolver.Resolve(env.SkyTexturePath);
                if (File.Exists(resolved))
                    skyBmp = LoadImageSafe(resolved, 4096);
            }

            bool isFullSphere = skyBmp != null;
            double radius = Math.Max(500.0, sceneHalfM * 5.0);
            int stacks = isFullSphere ? 48 : 24;
            int slices = isFullSphere ? 64 : 32;
            double maxPhi = isFullSphere ? Math.PI : Math.PI * 0.5;

            var mesh = new MeshGeometry3D();
            for (int s = 0; s <= stacks; s++)
            {
                double phi = maxPhi * s / stacks;
                double sinP = Math.Sin(phi);
                double cosP = Math.Cos(phi);
                for (int sl = 0; sl <= slices; sl++)
                {
                    double theta = 2 * Math.PI * sl / slices;
                    mesh.Positions.Add(new Point3D(
                        radius * sinP * Math.Cos(theta),
                        radius * sinP * Math.Sin(theta),
                        radius * cosP));

                    double u = (double)sl / slices;
                    double v = isFullSphere
                        ? (double)s / stacks
                        : (double)s / stacks;
                    mesh.TextureCoordinates.Add(new Point(u, v));
                }
            }
            int cols = slices + 1;
            for (int s = 0; s < stacks; s++)
            {
                for (int sl = 0; sl < slices; sl++)
                {
                    int a = s * cols + sl;
                    int b = a + 1;
                    int c = a + cols;
                    int d = c + 1;
                    mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
                }
            }

            Brush brush;
            if (skyBmp != null)
            {
                brush = new ImageBrush(skyBmp)
                {
                    TileMode = TileMode.None,
                    Stretch = Stretch.Fill
                };
                ((ImageBrush)brush).Freeze();
            }
            else
            {
                brush = new LinearGradientBrush(env.SkyZenithColor, env.SkyHorizonColor,
                    new Point(0.5, 0), new Point(0.5, 1));
                brush.Freeze();
            }

            var mat = new EmissiveMaterial(brush);
            var geom = new GeometryModel3D
            {
                Geometry = mesh,
                Material = mat,
                BackMaterial = mat
            };
            return new ModelVisual3D { Content = geom };
        }

        /// <summary>Builds a tiled procedural brush for the chosen ground material.
        /// All textures are generated in-memory (no external image files).</summary>
        public static Brush BuildGroundBrush(GroundMaterial mat, double sizeM, bool overlayGrid)
        {
            return BuildGroundBrush(mat, sizeM, overlayGrid, null, 25.0);
        }

        public static Brush BuildGroundBrush(GroundMaterial mat, double sizeM, bool overlayGrid,
            string groundTexturePath, double tileSize)
        {
            if (!string.IsNullOrEmpty(groundTexturePath))
            {
                string resolved = BuiltinAssetResolver.Resolve(groundTexturePath);
                if (File.Exists(resolved))
                {
                    var bmp = LoadImageSafe(resolved, 2048);
                    if (bmp != null)
                    {
                        double tiles = Math.Max(1.0, sizeM / tileSize);
                        var brush = new ImageBrush(bmp)
                        {
                            TileMode = TileMode.Tile,
                            Stretch = Stretch.Fill,
                            Viewport = new Rect(0, 0, 1.0 / tiles, 1.0 / tiles),
                            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
                        };
                        brush.Freeze();
                        return brush;
                    }
                }
            }

            switch (mat)
            {
                case GroundMaterial.Grass:    return MakeGrassBrush(sizeM, overlayGrid);
                case GroundMaterial.Concrete: return MakeConcreteBrush(sizeM, overlayGrid);
                case GroundMaterial.Sand:     return MakeSandBrush(sizeM, overlayGrid);
                case GroundMaterial.Asphalt:  return MakeAsphaltBrush(sizeM, overlayGrid);
                default:                      return MakeGridBrush(sizeM);
            }
        }

        // ── procedural textures ──

        private static Brush MakeGridBrush(double sizeM)
        {
            int tiles = Math.Max(1, (int)(sizeM / 5.0));
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(180, 195, 170)), null,
                new RectangleGeometry(new Rect(0, 0, tiles, tiles))));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(60, 100, 120, 90)), 0.02);
            for (int i = 0; i <= tiles; i++)
            {
                dg.Children.Add(new GeometryDrawing(null, pen,
                    new LineGeometry(new Point(i, 0), new Point(i, tiles))));
                dg.Children.Add(new GeometryDrawing(null, pen,
                    new LineGeometry(new Point(0, i), new Point(tiles, i))));
            }
            var b = new DrawingBrush(dg) { TileMode = TileMode.None };
            b.Freeze();
            return b;
        }

        private static Brush MakeGrassBrush(double sizeM, bool overlayGrid)
        {
            var dg = new DrawingGroup();
            // Base mottled green using overlapping ellipses with slight colour jitter.
            var baseRect = new Rect(0, 0, 100, 100);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(110, 140, 75)), null,
                new RectangleGeometry(baseRect)));
            var rnd = new Random(1337);
            for (int i = 0; i < 600; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y = rnd.NextDouble() * 100;
                double r = 0.4 + rnd.NextDouble() * 1.6;
                byte g = (byte)(120 + rnd.Next(50));
                byte rr = (byte)(85 + rnd.Next(35));
                byte bb = (byte)(55 + rnd.Next(40));
                byte a = (byte)(80 + rnd.Next(120));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, rr, g, bb)), null,
                    new EllipseGeometry(new Point(x, y), r, r * 0.6)));
            }
            return BuildTile(dg, sizeM, 25.0, overlayGrid);
        }

        private static Brush MakeConcreteBrush(double sizeM, bool overlayGrid)
        {
            var dg = new DrawingGroup();
            var baseRect = new Rect(0, 0, 100, 100);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(195, 195, 192)), null,
                new RectangleGeometry(baseRect)));
            var rnd = new Random(31);
            for (int i = 0; i < 250; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y = rnd.NextDouble() * 100;
                double r = 0.3 + rnd.NextDouble() * 0.9;
                byte v = (byte)(170 + rnd.Next(40));
                byte a = (byte)(30 + rnd.Next(80));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, v, v, v)), null,
                    new EllipseGeometry(new Point(x, y), r, r)));
            }
            // Slab joints every 50 units (= 5 m).
            var jointPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 90, 90, 90)), 0.4);
            for (int i = 0; i <= 100; i += 50)
            {
                dg.Children.Add(new GeometryDrawing(null, jointPen,
                    new LineGeometry(new Point(i, 0), new Point(i, 100))));
                dg.Children.Add(new GeometryDrawing(null, jointPen,
                    new LineGeometry(new Point(0, i), new Point(100, i))));
            }
            return BuildTile(dg, sizeM, 10.0, overlayGrid);
        }

        private static Brush MakeSandBrush(double sizeM, bool overlayGrid)
        {
            var dg = new DrawingGroup();
            var baseRect = new Rect(0, 0, 100, 100);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(220, 200, 155)), null,
                new RectangleGeometry(baseRect)));
            var rnd = new Random(7);
            for (int i = 0; i < 1500; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y = rnd.NextDouble() * 100;
                byte r = (byte)(200 + rnd.Next(40));
                byte g = (byte)(180 + rnd.Next(40));
                byte b = (byte)(135 + rnd.Next(40));
                byte a = (byte)(60 + rnd.Next(120));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, r, g, b)), null,
                    new EllipseGeometry(new Point(x, y), 0.2, 0.2)));
            }
            return BuildTile(dg, sizeM, 10.0, overlayGrid);
        }

        private static Brush MakeAsphaltBrush(double sizeM, bool overlayGrid)
        {
            var dg = new DrawingGroup();
            var baseRect = new Rect(0, 0, 100, 100);
            dg.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(58, 58, 62)), null,
                new RectangleGeometry(baseRect)));
            var rnd = new Random(99);
            for (int i = 0; i < 800; i++)
            {
                double x = rnd.NextDouble() * 100;
                double y = rnd.NextDouble() * 100;
                double r = 0.2 + rnd.NextDouble() * 0.6;
                byte v = (byte)(40 + rnd.Next(70));
                byte a = (byte)(80 + rnd.Next(100));
                dg.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromArgb(a, v, v, (byte)(v + 4))), null,
                    new EllipseGeometry(new Point(x, y), r, r)));
            }
            return BuildTile(dg, sizeM, 10.0, overlayGrid);
        }

        /// <summary>Wraps a procedural tile drawing in a DrawingBrush that tiles N
        /// times across the ground plane. Optionally overlays the metric grid.</summary>
        private static Brush BuildTile(DrawingGroup tile, double sizeM, double tileMetres, bool overlayGrid)
        {
            int repeats = Math.Max(1, (int)(sizeM / tileMetres));
            // Wrap tile + (optional) grid in a parent DrawingGroup at integer-units.
            var root = new DrawingGroup();
            var tb = new DrawingBrush(tile)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 1.0 / repeats, 1.0 / repeats),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox
            };
            tb.Freeze();
            root.Children.Add(new GeometryDrawing(tb, null,
                new RectangleGeometry(new Rect(0, 0, repeats, repeats))));

            if (overlayGrid)
            {
                var minorPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 0.02);
                var majorPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 0.05);
                for (int i = 0; i <= repeats; i++)
                {
                    var pen = (i % 5 == 0) ? majorPen : minorPen;
                    root.Children.Add(new GeometryDrawing(null, pen,
                        new LineGeometry(new Point(i, 0), new Point(i, repeats))));
                    root.Children.Add(new GeometryDrawing(null, pen,
                        new LineGeometry(new Point(0, i), new Point(repeats, i))));
                }
            }
            var brush = new DrawingBrush(root) { TileMode = TileMode.None };
            brush.Freeze();
            return brush;
        }

        private static Color ScaleColor(Color c, double k)
        {
            byte r = (byte)Math.Max(0, Math.Min(255, c.R * k));
            byte g = (byte)Math.Max(0, Math.Min(255, c.G * k));
            byte b = (byte)Math.Max(0, Math.Min(255, c.B * k));
            return Color.FromRgb(r, g, b);
        }
    }
}
