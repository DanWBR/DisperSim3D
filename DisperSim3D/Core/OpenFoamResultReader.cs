using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Reads and parses OpenFOAM simulation output files, including scalar concentration fields
    /// and vector velocity fields, from case time-step directories.
    /// </summary>
    public static class OpenFoamResultReader
    {
        private const int MaxTimeSteps = 50;

        /// <summary>
        /// Reads all available time-step results from an OpenFOAM scalar transport case directory.
        /// Scans for time-step directories containing a "T" field file, subsamples to at most 50 steps,
        /// and validates the last time step by loading its concentration data.
        /// </summary>
        /// <param name="caseDir">The absolute path to the OpenFOAM case directory.</param>
        /// <param name="nx">Number of grid cells in the X direction.</param>
        /// <param name="ny">Number of grid cells in the Y direction.</param>
        /// <param name="nz">Number of grid cells in the Z direction.</param>
        /// <param name="domainSize">The half-size of the domain in meters (domain extends from -domainSize to +domainSize).</param>
        /// <param name="progressCallback">Optional callback invoked with progress fraction and status message.</param>
        /// <returns>An <see cref="OpenFoamResult"/> containing indexed time steps and domain bounds.</returns>
        public static OpenFoamResult ReadResults(string caseDir, int nx, int ny, int nz, double domainSize,
            Action<double, string> progressCallback = null, string scalarFieldName = "T")
        {
            var result = new OpenFoamResult
            {
                GridNx = nx,
                GridNy = ny,
                GridNz = nz,
                DomainSizeM = domainSize,
                DomainXMin = -domainSize,
                DomainXMax = domainSize,
                DomainYMin = -domainSize,
                DomainYMax = domainSize,
                DomainZMax = domainSize,
                CaseDir = caseDir
            };

            ParseBlockMeshBounds(caseDir, result);

            progressCallback?.Invoke(0, "Scanning timesteps...");

            var timeDirs = new List<Tuple<double, string>>();
            foreach (var dir in Directory.GetDirectories(caseDir))
            {
                string name = Path.GetFileName(dir);
                double timeVal;
                if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out timeVal))
                {
                    if (timeVal > 0 && File.Exists(Path.Combine(dir, scalarFieldName)))
                        timeDirs.Add(Tuple.Create(timeVal, dir));
                }
            }

            timeDirs.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            if (timeDirs.Count == 0)
            {
                result.IsLoaded = false;
                return result;
            }

            var selected = SubsampleTimesteps(timeDirs);

            foreach (var entry in selected)
            {
                result.TimeSteps.Add(entry.Item1);
                result.TimeStepPaths[entry.Item1] = Path.Combine(entry.Item2, scalarFieldName);
            }

            progressCallback?.Invoke(0.5, "Validating last timestep...");

            double lastTime = result.TimeSteps[result.TimeSteps.Count - 1];
            var lastField = LoadSingleTimestep(result.TimeStepPaths[lastTime], nx, ny, nz);
            if (lastField != null)
            {
                result.PreloadField(lastTime, lastField);
                result.IsLoaded = true;
            }

            progressCallback?.Invoke(1.0, string.Format("{0} timesteps indexed", result.TimeSteps.Count));
            return result;
        }

        /// <summary>
        /// Loads a single scalar field time step from an OpenFOAM "T" file and reshapes it into a 3D array.
        /// The flat OpenFOAM data is reordered from (k, j, i) storage to [i, j, k] indexing.
        /// </summary>
        /// <param name="filePath">The absolute path to the scalar field file.</param>
        /// <param name="nx">Number of grid cells in the X direction.</param>
        /// <param name="ny">Number of grid cells in the Y direction.</param>
        /// <param name="nz">Number of grid cells in the Z direction.</param>
        /// <returns>A 3D array of scalar values indexed as [x, y, z], or <c>null</c> if parsing fails.</returns>
        public static double[,,] LoadSingleTimestep(string filePath, int nx, int ny, int nz)
        {
            int expectedCount = nx * ny * nz;
            var flatValues = ParseScalarFieldStreaming(filePath, expectedCount);
            if (flatValues != null)
            {
                var field = new double[nx, ny, nz];
                for (int k = 0; k < nz; k++)
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i++)
                            field[i, j, k] = flatValues[k * nx * ny + j * nx + i];
                return field;
            }

            return LoadRefinedTimestep(filePath, nx, ny, nz);
        }

        private static double[,,] LoadRefinedTimestep(string filePath, int nx, int ny, int nz)
        {
            string timeDir = Path.GetDirectoryName(filePath);
            string caseDir = Path.GetDirectoryName(Path.GetDirectoryName(filePath));

            var scalarValues = ParseScalarFieldAny(filePath);
            if (scalarValues == null || scalarValues.Length == 0) return null;

            int cellCount = scalarValues.Length;
            double[] cx = null, cy = null, cz = null;

            var searchDirs = new List<string> { timeDir };
            foreach (var dir in Directory.GetDirectories(caseDir))
            {
                string name = Path.GetFileName(dir);
                double t;
                if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out t))
                    if (!searchDirs.Contains(dir)) searchDirs.Add(dir);
            }
            string zeroDir = Path.Combine(caseDir, "0");
            if (Directory.Exists(zeroDir) && !searchDirs.Contains(zeroDir))
                searchDirs.Add(zeroDir);

            foreach (var sd in searchDirs)
            {
                if (cx != null) break;

                string cxPath = Path.Combine(sd, "Cx");
                string cyPath = Path.Combine(sd, "Cy");
                string czPath = Path.Combine(sd, "Cz");
                string cPath = Path.Combine(sd, "C");

                if (File.Exists(cxPath) && File.Exists(cyPath) && File.Exists(czPath))
                {
                    cx = ParseScalarFieldAny(cxPath);
                    cy = ParseScalarFieldAny(cyPath);
                    cz = ParseScalarFieldAny(czPath);
                }
                else if (File.Exists(cPath))
                {
                    var vectors = ParseVectorFieldAny(cPath);
                    if (vectors != null && vectors.Length >= cellCount * 3)
                    {
                        cx = new double[cellCount];
                        cy = new double[cellCount];
                        cz = new double[cellCount];
                        for (int i = 0; i < cellCount; i++)
                        {
                            cx[i] = vectors[i * 3];
                            cy[i] = vectors[i * 3 + 1];
                            cz[i] = vectors[i * 3 + 2];
                        }
                    }
                }
            }

            if (cx == null || cy == null || cz == null) return null;
            if (cx.Length != cellCount || cy.Length != cellCount || cz.Length != cellCount) return null;

            var result = new OpenFoamResult();
            ParseBlockMeshBounds(caseDir, result);
            double xMin = result.DomainXMin, xMax = result.DomainXMax;
            double yMin = result.DomainYMin, yMax = result.DomainYMax;
            double zMax = result.DomainZMax;

            double dxInv = nx / (xMax - xMin);
            double dyInv = ny / (yMax - yMin);
            double dzInv = nz / zMax;

            var field = new double[nx, ny, nz];
            var weight = new double[nx, ny, nz];

            for (int c = 0; c < cellCount; c++)
            {
                int i = (int)((cx[c] - xMin) * dxInv);
                int j = (int)((cy[c] - yMin) * dyInv);
                int k = (int)(cz[c] * dzInv);
                if (i < 0) i = 0; if (i >= nx) i = nx - 1;
                if (j < 0) j = 0; if (j >= ny) j = ny - 1;
                if (k < 0) k = 0; if (k >= nz) k = nz - 1;
                field[i, j, k] += scalarValues[c];
                weight[i, j, k] += 1.0;
            }

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        if (weight[i, j, k] > 0)
                            field[i, j, k] /= weight[i, j, k];

            return field;
        }

        private static List<Tuple<double, string>> SubsampleTimesteps(List<Tuple<double, string>> sorted)
        {
            if (sorted.Count <= MaxTimeSteps)
                return sorted;

            var selected = new List<Tuple<double, string>>();
            selected.Add(sorted[0]);

            double step = (double)(sorted.Count - 1) / (MaxTimeSteps - 1);
            for (int i = 1; i < MaxTimeSteps - 1; i++)
            {
                int idx = (int)Math.Round(i * step);
                if (idx > 0 && idx < sorted.Count)
                    selected.Add(sorted[idx]);
            }

            selected.Add(sorted[sorted.Count - 1]);
            return selected;
        }

        private static void ParseBlockMeshBounds(string caseDir, OpenFoamResult result)
        {
            string bmPath = Path.Combine(caseDir, "system", "blockMeshDict");
            if (!File.Exists(bmPath)) return;

            try
            {
                string content = File.ReadAllText(bmPath);
                var vertexRegex = new Regex(@"\(\s*([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s*\)", RegexOptions.Compiled);
                int vertStart = content.IndexOf("vertices");
                if (vertStart < 0) return;

                var matches = vertexRegex.Matches(content, vertStart);
                if (matches.Count < 8) return;

                double xMin = double.MaxValue, xMax = double.MinValue;
                double yMin = double.MaxValue, yMax = double.MinValue;
                double zMax = double.MinValue;

                for (int i = 0; i < Math.Min(8, matches.Count); i++)
                {
                    double vx = double.Parse(matches[i].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    double vy = double.Parse(matches[i].Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    double vz = double.Parse(matches[i].Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (vx < xMin) xMin = vx;
                    if (vx > xMax) xMax = vx;
                    if (vy < yMin) yMin = vy;
                    if (vy > yMax) yMax = vy;
                    if (vz > zMax) zMax = vz;
                }

                result.DomainXMin = xMin;
                result.DomainXMax = xMax;
                result.DomainYMin = yMin;
                result.DomainYMax = yMax;
                result.DomainZMax = zMax;
                result.DomainSizeM = Math.Max(xMax - xMin, yMax - yMin) / 2.0;
            }
            catch { }
        }

        /// <summary>
        /// Reads the converged velocity field from a steady-state wind simulation case directory.
        /// Locates the latest time-step directory containing a "U" file and parses the vector field.
        /// </summary>
        /// <param name="caseDir">The absolute path to the wind case directory.</param>
        /// <param name="nx">Number of grid cells in the X direction.</param>
        /// <param name="ny">Number of grid cells in the Y direction.</param>
        /// <param name="nz">Number of grid cells in the Z direction.</param>
        /// <param name="xMin">Domain minimum X coordinate in meters.</param>
        /// <param name="xMax">Domain maximum X coordinate in meters.</param>
        /// <param name="yMin">Domain minimum Y coordinate in meters.</param>
        /// <param name="yMax">Domain maximum Y coordinate in meters.</param>
        /// <param name="zMax">Domain maximum Z coordinate in meters.</param>
        /// <returns>A <see cref="WindField3D"/> containing the 3D velocity components, or <c>null</c> if no valid field is found.</returns>
        public static WindField3D ReadWindField(string caseDir, int nx, int ny, int nz,
            double xMin, double xMax, double yMin, double yMax, double zMax)
        {
            string uPath = null;
            var dirs = new List<Tuple<double, string>>();
            foreach (var dir in Directory.GetDirectories(caseDir))
            {
                string name = Path.GetFileName(dir);
                double t;
                if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out t) && t > 0)
                {
                    string candidate = Path.Combine(dir, "U");
                    if (File.Exists(candidate))
                        dirs.Add(Tuple.Create(t, candidate));
                }
            }
            if (dirs.Count > 0)
            {
                dirs.Sort((a, b) => b.Item1.CompareTo(a.Item1));
                uPath = dirs[0].Item2;
            }
            if (uPath == null) return null;

            int count = nx * ny * nz;
            var vectors = ParseVectorFieldStreaming(uPath, count);
            if (vectors == null) return null;

            var ux = new double[nx, ny, nz];
            var uy = new double[nx, ny, nz];
            var uz = new double[nx, ny, nz];

            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int idx = (k * nx * ny + j * nx + i) * 3;
                        ux[i, j, k] = vectors[idx];
                        uy[i, j, k] = vectors[idx + 1];
                        uz[i, j, k] = vectors[idx + 2];
                    }

            return new WindField3D(ux, uy, uz, xMin, xMax, yMin, yMax, zMax);
        }

        private static double[] ParseVectorFieldStreaming(string filePath, int expectedCount)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.TrimStart().StartsWith("internalField"))
                            break;
                    }
                    if (line == null) return null;

                    string trimmed = line.Trim();
                    if (trimmed.Contains("uniform") && !trimmed.Contains("nonuniform"))
                    {
                        var m = Regex.Match(trimmed, @"\(\s*([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s*\)");
                        if (!m.Success) return null;
                        double vx = double.Parse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                        double vy = double.Parse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                        double vz = double.Parse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                        var uniform = new double[expectedCount * 3];
                        for (int i = 0; i < expectedCount; i++)
                        {
                            uniform[i * 3] = vx;
                            uniform[i * 3 + 1] = vy;
                            uniform[i * 3 + 2] = vz;
                        }
                        return uniform;
                    }

                    int count = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (int.TryParse(line.Trim(), out count))
                            break;
                    }
                    if (count != expectedCount) return null;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Trim().StartsWith("("))
                            break;
                    }

                    var values = new double[count * 3];
                    int vi = 0;
                    var vecRegex = new Regex(@"\(\s*([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s*\)", RegexOptions.Compiled);
                    while (vi < count && (line = reader.ReadLine()) != null)
                    {
                        string t = line.Trim();
                        if (t == ")" || t == ");") break;
                        var match = vecRegex.Match(t);
                        if (match.Success)
                        {
                            values[vi * 3] = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                            values[vi * 3 + 1] = double.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                            values[vi * 3 + 2] = double.Parse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                            vi++;
                        }
                    }
                    return vi == count ? values : null;
                }
            }
            catch { return null; }
        }

        private static double[] ParseScalarFieldAny(string filePath)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.TrimStart().StartsWith("internalField")) break;
                    if (line == null) return null;

                    if (line.Trim().Contains("uniform") && !line.Trim().Contains("nonuniform"))
                        return null;

                    int count = 0;
                    while ((line = reader.ReadLine()) != null)
                        if (int.TryParse(line.Trim(), out count)) break;
                    if (count <= 0) return null;

                    while ((line = reader.ReadLine()) != null)
                        if (line.Trim().StartsWith("(")) break;

                    var values = new double[count];
                    int vi = 0;
                    while (vi < count && (line = reader.ReadLine()) != null)
                    {
                        string t = line.Trim();
                        if (t == ")" || t == ");") break;
                        double val;
                        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                            values[vi++] = val;
                    }
                    return vi == count ? values : null;
                }
            }
            catch { return null; }
        }

        private static double[] ParseVectorFieldAny(string filePath)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.TrimStart().StartsWith("internalField")) break;
                    if (line == null) return null;

                    if (line.Trim().Contains("uniform") && !line.Trim().Contains("nonuniform"))
                        return null;

                    int count = 0;
                    while ((line = reader.ReadLine()) != null)
                        if (int.TryParse(line.Trim(), out count)) break;
                    if (count <= 0) return null;

                    while ((line = reader.ReadLine()) != null)
                        if (line.Trim().StartsWith("(")) break;

                    var values = new double[count * 3];
                    int vi = 0;
                    var vecRegex = new Regex(@"\(\s*([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s*\)", RegexOptions.Compiled);
                    while (vi < count && (line = reader.ReadLine()) != null)
                    {
                        string t = line.Trim();
                        if (t == ")" || t == ");") break;
                        var match = vecRegex.Match(t);
                        if (match.Success)
                        {
                            values[vi * 3] = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                            values[vi * 3 + 1] = double.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                            values[vi * 3 + 2] = double.Parse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                            vi++;
                        }
                    }
                    return vi == count ? values : null;
                }
            }
            catch { return null; }
        }

        private static double[] ParseScalarFieldStreaming(string filePath, int expectedCount)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.TrimStart().StartsWith("internalField"))
                            break;
                    }

                    if (line == null) return null;

                    string trimmed = line.Trim();

                    if (trimmed.Contains("uniform") && !trimmed.Contains("nonuniform"))
                        return null;

                    int count = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string t = line.Trim();
                        if (int.TryParse(t, out count))
                            break;
                    }

                    if (count != expectedCount)
                        return null;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Trim().StartsWith("("))
                            break;
                    }

                    var values = new double[count];
                    int vi = 0;
                    while (vi < count && (line = reader.ReadLine()) != null)
                    {
                        string t = line.Trim();
                        if (t == ")" || t == ");") break;

                        double val;
                        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                            values[vi++] = val;
                    }

                    return vi == count ? values : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
