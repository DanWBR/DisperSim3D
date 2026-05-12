using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;
using HelixToolkit.Wpf;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Builds a streamline visualisation of a <see cref="WindField3D"/> as a list of
    /// <see cref="LinesVisual3D"/> (one per streamline). Geometry is built once; animation
    /// is performed by modulating each line's <c>Color</c> alpha on the timer tick — no
    /// per-frame mesh rebuild. Each streamline is coloured by its average wind speed via
    /// the Jet colour map (blue = slow → red = fast).
    /// </summary>
    public static class WindFieldStreamlineVisual
    {
        public static AnimatedStreamlineField Build(WindField3D field,
            double xMin, double xMax, double yMin, double yMax, double zMax,
            int seedCount = 256, int verticalLayers = 1,
            double thicknessFactor = 0.025,
            bool animated = true)
        {
            var result = new AnimatedStreamlineField { Animated = animated };
            if (field == null) return result;

            // Step size: a fraction of the smaller horizontal cell — small enough to
            // resolve curvature, large enough to cap polyline length.
            int nSeedAxis = (int)Math.Max(2, Math.Round(Math.Sqrt(seedCount)));
            double dx = (xMax - xMin) / nSeedAxis;
            double step = dx * 0.5;
            int maxSteps = (int)Math.Min(2000, Math.Ceiling((xMax - xMin) * 1.5 / step));

            // Speed range from a coarse probe → normalised colour.
            double minSpeed = double.MaxValue, maxSpeed = 0;
            int probe = 16;
            for (int i = 0; i < probe; i++)
                for (int j = 0; j < probe; j++)
                {
                    double x = xMin + (i + 0.5) * (xMax - xMin) / probe;
                    double y = yMin + (j + 0.5) * (yMax - yMin) / probe;
                    double z = Math.Min(5.0, zMax * 0.1);
                    double s = field.Interpolate(x, y, z).Length;
                    if (s < minSpeed) minSpeed = s;
                    if (s > maxSpeed) maxSpeed = s;
                }
            if (maxSpeed < 0.001) maxSpeed = 1.0;
            if (minSpeed >= maxSpeed) minSpeed = 0;
            double speedRange = Math.Max(maxSpeed - minSpeed, 1e-6);

            // Tube/line thickness in screen-pixels via LinesVisual3D.Thickness — much
            // cheaper than triangulated tubes and crisp at any zoom.
            double pxThickness = Math.Max(1.0, 60.0 * thicknessFactor);

            int nz = Math.Max(1, verticalLayers);
            for (int i = 0; i < nSeedAxis; i++)
                for (int j = 0; j < nSeedAxis; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        double x0 = xMin + (i + 0.5) * (xMax - xMin) / nSeedAxis;
                        double y0 = yMin + (j + 0.5) * (yMax - yMin) / nSeedAxis;
                        double z0 = nz == 1 ? Math.Min(5.0, zMax * 0.1) : (k + 0.5) * zMax / nz;

                        var poly = IntegrateStreamline(field,
                            new Point3D(x0, y0, z0), step, maxSteps,
                            xMin, xMax, yMin, yMax, zMax);
                        if (poly == null || poly.Points.Count < 4) continue;

                        // Average speed for the colour bin.
                        double sumSpeed = 0;
                        foreach (var s in poly.Speeds) sumSpeed += s;
                        double avg = sumSpeed / poly.Speeds.Count;
                        double tNorm = (avg - minSpeed) / speedRange;
                        if (tNorm < 0) tNorm = 0; else if (tNorm > 1) tNorm = 1;
                        var baseColor = ColorMapHelper.Sample(ColorMapName.Jet, tNorm);

                        var lines = new LinesVisual3D
                        {
                            Color = baseColor,
                            Thickness = pxThickness
                        };
                        // LinesVisual3D draws disjoint segments — every consecutive pair
                        // (p_i, p_{i+1}) becomes one line. Build the strip explicitly.
                        for (int p = 0; p < poly.Points.Count - 1; p++)
                        {
                            lines.Points.Add(poly.Points[p]);
                            lines.Points.Add(poly.Points[p + 1]);
                        }
                        result.Visuals.Add(new StreamlineVisual
                        {
                            Lines = lines,
                            BaseColor = baseColor,
                            Phase = poly.Phase
                        });
                    }
            return result;
        }

        private static Streamline IntegrateStreamline(WindField3D field, Point3D seed,
            double step, int maxSteps,
            double xMin, double xMax, double yMin, double yMax, double zMax)
        {
            var fwd = March(field, seed, +step, maxSteps, xMin, xMax, yMin, yMax, zMax);
            var bwd = March(field, seed, -step, maxSteps, xMin, xMax, yMin, yMax, zMax);
            var line = new Streamline();
            for (int i = bwd.Count - 1; i >= 0; i--) { line.Points.Add(bwd[i].Item1); line.Speeds.Add(bwd[i].Item2); }
            line.Points.Add(seed);
            line.Speeds.Add(field.Interpolate(seed.X, seed.Y, seed.Z).Length);
            for (int i = 0; i < fwd.Count; i++) { line.Points.Add(fwd[i].Item1); line.Speeds.Add(fwd[i].Item2); }
            int hash = seed.X.GetHashCode() ^ (seed.Y.GetHashCode() << 1) ^ (seed.Z.GetHashCode() << 2);
            line.Phase = ((hash & 0xFFFF) / 65536.0);
            return line;
        }

        private static List<Tuple<Point3D, double>> March(WindField3D field, Point3D seed,
            double step, int maxSteps,
            double xMin, double xMax, double yMin, double yMax, double zMax)
        {
            var pts = new List<Tuple<Point3D, double>>();
            var p = seed;
            for (int s = 0; s < maxSteps; s++)
            {
                var v1 = field.Interpolate(p.X, p.Y, p.Z);
                double speed = v1.Length;
                if (speed < 0.05) break;
                var dir1 = v1; dir1.Normalize();
                var pHalf = new Point3D(p.X + dir1.X * step * 0.5,
                                        p.Y + dir1.Y * step * 0.5,
                                        p.Z + dir1.Z * step * 0.5);
                var v2 = field.Interpolate(pHalf.X, pHalf.Y, pHalf.Z);
                if (v2.Length < 0.05) break;
                var dir2 = v2; dir2.Normalize();
                var pn = new Point3D(p.X + dir2.X * step,
                                     p.Y + dir2.Y * step,
                                     p.Z + dir2.Z * step);
                if (pn.X < xMin || pn.X > xMax || pn.Y < yMin || pn.Y > yMax
                    || pn.Z < 0 || pn.Z > zMax) break;
                pts.Add(Tuple.Create(pn, speed));
                p = pn;
            }
            return pts;
        }
    }

    /// <summary>One streamline visual: a LinesVisual3D + the colour-map base colour.</summary>
    public class StreamlineVisual
    {
        public LinesVisual3D Lines;
        public Color BaseColor;
        public double Phase;
    }

    public class AnimatedStreamlineField
    {
        public List<StreamlineVisual> Visuals { get; set; } = new List<StreamlineVisual>();
        public bool Animated { get; set; } = true;

        /// <summary>Adds every line visual to the viewport. Call once per Build.</summary>
        public void AddTo(HelixViewport3D viewport)
        {
            foreach (var v in Visuals) viewport.Children.Add(v.Lines);
        }

        /// <summary>Removes every line visual from the viewport. Call before disposing.</summary>
        public void RemoveFrom(HelixViewport3D viewport)
        {
            foreach (var v in Visuals) viewport.Children.Remove(v.Lines);
        }

        /// <summary>
        /// Updates per-streamline colour to create a flowing brightness pulse without
        /// rebuilding any geometry. Call once per timer tick — O(N) cheap pass.
        /// </summary>
        public void Animate(double timeSeconds)
        {
            if (!Animated) return;
            foreach (var v in Visuals)
            {
                double t = (timeSeconds * 0.6 + v.Phase) % 1.0;
                double pulse = 0.55 + 0.45 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t));
                byte alpha = (byte)Math.Min(255, 255 * pulse);
                v.Lines.Color = Color.FromArgb(alpha, v.BaseColor.R, v.BaseColor.G, v.BaseColor.B);
            }
        }
    }

    public class Streamline
    {
        public List<Point3D> Points { get; set; } = new List<Point3D>();
        public List<double> Speeds { get; set; } = new List<double>();
        public double Phase { get; set; }
    }
}
