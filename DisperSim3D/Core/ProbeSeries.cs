using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DisperSim3D.Geometry;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Pressure–time series a solver recorded at points every time step, far denser in time
    /// than its stored fields: <c>probes.bin</c> in the simulation's folder, little-endian:
    /// int32 version (1), int32 probe count n, int32 sample count m; per probe an int32 byte
    /// length, its UTF-8 name and three doubles x, y, z (m); then per sample a double time (s)
    /// and n doubles, the overpressure at each probe (Pa). Named <c>.bin</c> so a project
    /// bundle keeps it with the run's fields.
    /// </summary>
    public sealed class ProbeSeries
    {
        public const string FileName = "probes.bin";

        public IReadOnlyList<string> Names { get; }
        public IReadOnlyList<Point3D> Positions { get; }
        public double[] Times { get; }

        /// <summary>Values[probe][sample], Pa.</summary>
        public double[][] Values { get; }

        public ProbeSeries(IReadOnlyList<string> names, IReadOnlyList<Point3D> positions, double[] times, double[][] values)
        {
            Names = names; Positions = positions; Times = times; Values = values;
        }

        public static void Write(string path, IReadOnlyList<string> names, IReadOnlyList<Point3D> positions,
            IReadOnlyList<double> times, IReadOnlyList<double[]> samples)
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write(1); w.Write(names.Count); w.Write(times.Count);
            for (int p = 0; p < names.Count; p++)
            {
                var bytes = Encoding.UTF8.GetBytes(names[p] ?? "");
                w.Write(bytes.Length); w.Write(bytes);
                w.Write(positions[p].X); w.Write(positions[p].Y); w.Write(positions[p].Z);
            }
            for (int s = 0; s < times.Count; s++)
            {
                w.Write(times[s]);
                for (int p = 0; p < names.Count; p++) w.Write(samples[s][p]);
            }
        }

        public static ProbeSeries Read(string path)
        {
            using var r = new BinaryReader(File.OpenRead(path));
            int version = r.ReadInt32();
            if (version != 1) throw new InvalidDataException("Unknown probes.bin version " + version + ".");
            int n = r.ReadInt32(), m = r.ReadInt32();
            var names = new string[n]; var pos = new Point3D[n];
            for (int p = 0; p < n; p++)
            {
                int len = r.ReadInt32();
                names[p] = Encoding.UTF8.GetString(r.ReadBytes(len));
                pos[p] = new Point3D(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            }
            var times = new double[m];
            var values = new double[n][];
            for (int p = 0; p < n; p++) values[p] = new double[m];
            for (int s = 0; s < m; s++)
            {
                times[s] = r.ReadDouble();
                for (int p = 0; p < n; p++) values[p][s] = r.ReadDouble();
            }
            return new ProbeSeries(names, pos, times, values);
        }

        /// <summary>The probes of a simulation's folder, or null when it has none.</summary>
        public static ProbeSeries Of(Models.Simulation sim)
        {
            string path = string.IsNullOrEmpty(sim?.CasePath) ? null : Path.Combine(sim.CasePath, FileName);
            return path != null && File.Exists(path) ? Read(path) : null;
        }

        /// <summary>Peak overpressure (Pa), positive-phase impulse (Pa·s) and the time of the peak at one probe.</summary>
        public (double Peak, double Impulse, double PeakTime) Summary(int probe)
        {
            double peak = double.NegativeInfinity, imp = 0, tPeak = 0;
            var v = Values[probe];
            for (int s = 0; s < Times.Length; s++)
            {
                if (v[s] > peak) { peak = v[s]; tPeak = Times[s]; }
                if (s > 0) imp += 0.5 * (Math.Max(0, v[s]) + Math.Max(0, v[s - 1])) * (Times[s] - Times[s - 1]);
            }
            return (Times.Length > 0 ? peak : 0, imp, tPeak);
        }
    }
}
