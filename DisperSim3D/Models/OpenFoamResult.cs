using System;
using System.Collections.Generic;
using System.IO;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Holds the results of an OpenFOAM dispersion simulation, including grid metadata, time-step paths, and an LRU cache of loaded concentration fields.
    /// </summary>
    public class OpenFoamResult
    {
        /// <summary>Gets or sets the list of simulation time steps in seconds.</summary>
        public List<double> TimeSteps { get; set; }

        /// <summary>Gets or sets the number of grid cells in the X direction.</summary>
        public int GridNx { get; set; }

        /// <summary>Gets or sets the number of grid cells in the Y direction.</summary>
        public int GridNy { get; set; }

        /// <summary>Gets or sets the number of grid cells in the Z direction.</summary>
        public int GridNz { get; set; }

        /// <summary>Gets or sets the physical domain size in meters.</summary>
        public double DomainSizeM { get; set; }

        /// <summary>Gets or sets the minimum X coordinate of the domain in meters.</summary>
        public double DomainXMin { get; set; }

        /// <summary>Gets or sets the maximum X coordinate of the domain in meters.</summary>
        public double DomainXMax { get; set; }

        /// <summary>Gets or sets the minimum Y coordinate of the domain in meters.</summary>
        public double DomainYMin { get; set; }

        /// <summary>Gets or sets the maximum Y coordinate of the domain in meters.</summary>
        public double DomainYMax { get; set; }

        /// <summary>Gets or sets the maximum Z coordinate (height) of the domain in meters.</summary>
        public double DomainZMax { get; set; }

        /// <summary>Gets or sets a value indicating whether the result data has been loaded from disk.</summary>
        public bool IsLoaded { get; set; }

        /// <summary>Gets or sets the path to the OpenFOAM case directory.</summary>
        public string CaseDir { get; set; }

        /// <summary>Gets or sets a mapping from simulation time to the file path containing that time step's field data.</summary>
        public Dictionary<double, string> TimeStepPaths { get; set; }

        private readonly Dictionary<double, double[,,]> _cache = new Dictionary<double, double[,,]>();
        private readonly LinkedList<double> _cacheOrder = new LinkedList<double>();
        private int _maxCacheSize = 5;

        /// <summary>
        /// Gets all concentration fields by loading every time step. Prefer <see cref="GetField(double)"/> for on-demand access.
        /// </summary>
        [Obsolete("Use GetField(time) instead")]
        public Dictionary<double, double[,,]> ConcentrationFields
        {
            get
            {
                var all = new Dictionary<double, double[,,]>();
                foreach (var t in TimeSteps)
                {
                    var f = GetField(t);
                    if (f != null) all[t] = f;
                }
                return all;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenFoamResult"/> class with empty time-step collections.
        /// </summary>
        public OpenFoamResult()
        {
            TimeSteps = new List<double>();
            TimeStepPaths = new Dictionary<double, string>();
        }

        /// <summary>Gets or sets the maximum number of concentration fields retained in the LRU cache. Minimum value is 2.</summary>
        public int MaxCacheSize
        {
            get => _maxCacheSize;
            set => _maxCacheSize = Math.Max(2, value);
        }

        /// <summary>
        /// Inserts a pre-computed concentration field into the cache for the specified time step.
        /// </summary>
        /// <param name="time">The simulation time in seconds.</param>
        /// <param name="field">The 3D concentration field array indexed as [x, y, z].</param>
        public void PreloadField(double time, double[,,] field)
        {
            AddToCache(time, field);
        }

        /// <summary>
        /// Retrieves the concentration field for the specified time step, loading from disk and caching if necessary.
        /// </summary>
        /// <param name="time">The simulation time in seconds.</param>
        /// <returns>A 3D concentration array indexed as [x, y, z], or <c>null</c> if the time step is not available.</returns>
        public double[,,] GetField(double time)
        {
            if (_cache.TryGetValue(time, out var cached))
            {
                _cacheOrder.Remove(time);
                _cacheOrder.AddLast(time);
                return cached;
            }

            string path;
            if (!TimeStepPaths.TryGetValue(time, out path))
                return null;

            double[,,] field;
            if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                field = LoadBinaryField(path, GridNx, GridNy, GridNz);
            else
                field = Core.OpenFoamResultReader.LoadSingleTimestep(path, GridNx, GridNy, GridNz);
            if (field == null) return null;

            AddToCache(time, field);
            return field;
        }

        private void AddToCache(double time, double[,,] field)
        {
            if (_cache.ContainsKey(time))
            {
                _cacheOrder.Remove(time);
            }
            else
            {
                while (_cache.Count >= _maxCacheSize && _cacheOrder.Count > 0)
                {
                    double oldest = _cacheOrder.First.Value;
                    _cacheOrder.RemoveFirst();
                    _cache.Remove(oldest);
                }
            }

            _cache[time] = field;
            _cacheOrder.AddLast(time);
        }

        /// <summary>
        /// Removes all cached concentration fields from memory.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            _cacheOrder.Clear();
        }

        /// <summary>
        /// Saves a 3D concentration field to a binary file in row-major order (X, Y, Z).
        /// </summary>
        /// <param name="path">The output file path.</param>
        /// <param name="field">The 3D concentration field array to serialize.</param>
        public static void SaveBinaryField(string path, double[,,] field)
        {
            int nx = field.GetLength(0), ny = field.GetLength(1), nz = field.GetLength(2);
            var buf = new byte[nx * ny * nz * sizeof(double)];
            int offset = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        BitConverter.GetBytes(field[i, j, k]).CopyTo(buf, offset);
                        offset += sizeof(double);
                    }
            File.WriteAllBytes(path, buf);
        }

        private static double[,,] LoadBinaryField(string path, int nx, int ny, int nz)
        {
            var buf = File.ReadAllBytes(path);
            if (buf.Length < nx * ny * nz * sizeof(double)) return null;
            var field = new double[nx, ny, nz];
            int offset = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        field[i, j, k] = BitConverter.ToDouble(buf, offset);
                        offset += sizeof(double);
                    }
            return field;
        }
    }
}
