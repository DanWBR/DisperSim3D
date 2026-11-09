using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// The blast load an explosion solver computed on the plant: <c>{t}_blast.bin</c> in the
    /// simulation's folder holds, for each triangle of the scene's merged decoration mesh in
    /// <see cref="DecorationSolids.Triangles"/> order, two little-endian doubles: the peak
    /// overpressure (Pa) and the positive-phase impulse (Pa·s) the surface saw up to time t.
    /// Sixteen bytes per triangle, no header; the companion of <see cref="SurfaceHeatLoad"/>.
    /// </summary>
    public sealed class SurfaceBlastLoad
    {
        public const string Suffix = "blast";

        private SurfaceBlastLoad(SortedList<double, string> paths) { Paths = paths; }

        public SortedList<double, string> Paths { get; }

        public static string FileName(double timeS) =>
            timeS.ToString("G9", CultureInfo.InvariantCulture) + "_" + Suffix + ".bin";

        public static void Write(string path, IReadOnlyList<double> peakPa, IReadOnlyList<double> impulsePaS)
        {
            if (peakPa == null || impulsePaS == null || peakPa.Count != impulsePaS.Count)
                throw new ArgumentException("One peak overpressure and one impulse per triangle.");
            var buf = new byte[peakPa.Count * 16];
            for (int t = 0; t < peakPa.Count; t++)
            {
                BitConverter.TryWriteBytes(new Span<byte>(buf, 16 * t, 8), peakPa[t]);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 16 * t + 8, 8), impulsePaS[t]);
            }
            File.WriteAllBytes(path, buf);
        }

        public static (double[] PeakPa, double[] ImpulsePaS) Read(string path)
        {
            var buf = File.ReadAllBytes(path);
            int n = buf.Length / 16;
            var peak = new double[n]; var imp = new double[n];
            for (int t = 0; t < n; t++) { peak[t] = BitConverter.ToDouble(buf, 16 * t); imp[t] = BitConverter.ToDouble(buf, 16 * t + 8); }
            return (peak, imp);
        }

        public static SurfaceBlastLoad Find(string caseDir)
        {
            if (string.IsNullOrEmpty(caseDir) || !Directory.Exists(caseDir)) return null;
            string suffix = "_" + Suffix;
            var paths = new SortedList<double, string>();
            foreach (string f in Directory.GetFiles(caseDir, "*" + suffix + ".bin", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(name.Substring(0, name.Length - suffix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                    paths[t] = f;
            }
            return paths.Count > 0 ? new SurfaceBlastLoad(paths) : null;
        }

        public static SurfaceBlastLoad Of(Simulation sim) => Find(sim?.CasePath);

        /// <summary>One value per triangle: the peak overpressure, or the impulse with
        /// <paramref name="impulse"/>, from the last file (both only grow with time) or the one
        /// nearest <paramref name="timeS"/>.</summary>
        public double[] Collapse(bool impulse, ViewTimeMode mode, double timeS = 0)
        {
            int best = Paths.Count - 1;
            if (mode == ViewTimeMode.SpecificTime)
                for (int i = 0; i < Paths.Count; i++)
                    if (Math.Abs(Paths.Keys[i] - timeS) < Math.Abs(Paths.Keys[best] - timeS)) best = i;
            var v = Read(Paths.Values[best]);
            return impulse ? v.ImpulsePaS : v.PeakPa;
        }
    }
}
