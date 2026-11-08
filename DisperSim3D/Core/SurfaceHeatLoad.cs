using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// The heat load a fire solver computed on the plant: <c>{t:F3}_surface.bin</c> in the
    /// simulation's folder holds, for each triangle of the scene's merged decoration mesh
    /// in <see cref="DecorationSolids.Triangles"/> order
    /// (<c>DecorationSolids.FromDecorations(scene.Decorations)</c>), two little-endian
    /// doubles: the incident radiative heat flux and the total (radiative + convective)
    /// heat flux into the surface, both kW/m². Sixteen bytes per triangle, no header.
    /// </summary>
    public sealed class SurfaceHeatLoad
    {
        private SurfaceHeatLoad(string caseDir, SortedList<double, string> paths)
        {
            CaseDir = caseDir;
            Paths = paths;
        }

        public string CaseDir { get; }

        /// <summary>The written times and their files, in time order.</summary>
        public SortedList<double, string> Paths { get; }

        public IList<double> Times => Paths.Keys;

        /// <summary>The file name at time <paramref name="timeS"/>.</summary>
        public static string FileName(double timeS) =>
            timeS.ToString("F3", CultureInfo.InvariantCulture) + "_" + ResultChannels.SurfaceSuffix + ".bin";

        /// <summary>Writes one time: incident and total flux per triangle, kW/m².</summary>
        public static void Write(string path, IReadOnlyList<double> incidentKwM2, IReadOnlyList<double> totalKwM2)
        {
            if (incidentKwM2 == null || totalKwM2 == null || incidentKwM2.Count != totalKwM2.Count)
                throw new ArgumentException("One incident and one total flux per triangle.");
            var buf = new byte[incidentKwM2.Count * 2 * sizeof(double)];
            for (int t = 0; t < incidentKwM2.Count; t++)
            {
                BitConverter.TryWriteBytes(new Span<byte>(buf, 16 * t, 8), incidentKwM2[t]);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 16 * t + 8, 8), totalKwM2[t]);
            }
            File.WriteAllBytes(path, buf);
        }

        /// <summary>Reads one time: (incident, total) per triangle, kW/m².</summary>
        public static (double[] Incident, double[] Total) Read(string path)
        {
            var buf = File.ReadAllBytes(path);
            int n = buf.Length / 16;
            var incident = new double[n];
            var total = new double[n];
            for (int t = 0; t < n; t++)
            {
                incident[t] = BitConverter.ToDouble(buf, 16 * t);
                total[t] = BitConverter.ToDouble(buf, 16 * t + 8);
            }
            return (incident, total);
        }

        /// <summary>The surface files of a simulation folder; null when it has none.</summary>
        public static SurfaceHeatLoad Find(string caseDir)
        {
            if (string.IsNullOrEmpty(caseDir) || !Directory.Exists(caseDir)) return null;
            string suffix = "_" + ResultChannels.SurfaceSuffix;
            var paths = new SortedList<double, string>();
            foreach (string f in Directory.GetFiles(caseDir, "*" + suffix + ".bin", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (double.TryParse(name.Substring(0, name.Length - suffix.Length), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double t))
                    paths[t] = f;
            }
            return paths.Count > 0 ? new SurfaceHeatLoad(caseDir, paths) : null;
        }

        /// <summary>The surface heat load of a simulation, or null when it wrote none.</summary>
        public static SurfaceHeatLoad Of(Simulation sim) => Find(sim?.CasePath);

        /// <summary>
        /// One value per triangle: the total flux when <paramref name="total"/>, else the
        /// incident radiation, collapsed over time as a View asks (per-triangle peak, last
        /// time, or the time closest to <paramref name="timeS"/>).
        /// </summary>
        public double[] Collapse(bool total, ViewTimeMode mode, double timeS = 0)
        {
            if (mode == ViewTimeMode.FinalSnapshot)
                return Pick(Read(Paths.Values[Paths.Count - 1]), total);
            if (mode == ViewTimeMode.SpecificTime)
            {
                int best = 0;
                for (int i = 1; i < Paths.Count; i++)
                    if (Math.Abs(Paths.Keys[i] - timeS) < Math.Abs(Paths.Keys[best] - timeS)) best = i;
                return Pick(Read(Paths.Values[best]), total);
            }
            double[] peak = null;
            foreach (string path in Paths.Values)
            {
                var v = Pick(Read(path), total);
                if (peak == null) { peak = v; continue; }
                for (int t = 0; t < Math.Min(peak.Length, v.Length); t++) peak[t] = Math.Max(peak[t], v[t]);
            }
            return peak;
        }

        private static double[] Pick((double[] Incident, double[] Total) values, bool total) =>
            total ? values.Total : values.Incident;
    }
}
