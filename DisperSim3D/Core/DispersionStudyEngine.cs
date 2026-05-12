using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// One simulation's final-snapshot cloud, after applying the study's detection
    /// criterion. The mask is a flat <see cref="bool"/> array (z-major) of size
    /// <see cref="Nx"/>·<see cref="Ny"/>·<see cref="Nz"/>; entry n is true when the
    /// detection quantity at that cell exceeds the threshold.
    /// </summary>
    public sealed class CloudSnapshot
    {
        public string SimulationId;
        public string SimulationName;
        public int Nx, Ny, Nz;
        public double DomainHalfM, DomainHeightM;
        public bool[] Mask;           // length Nx*Ny*Nz
        public int CloudCellCount;    // popcount of Mask
        public string Error;

        public bool IsValid => Error == null && CloudCellCount > 0 && Mask != null;

        /// <summary>Returns the (xMin, yMin, zMin, xMax, yMax, zMax) AABB of the cloud
        /// in world-space metres. Useful for bounding-box culling during detector
        /// allocation. Returns null when the cloud is empty.</summary>
        public (double xMin, double yMin, double zMin, double xMax, double yMax, double zMax)? Bounds()
        {
            if (!IsValid) return null;
            double dx = 2.0 * DomainHalfM / Nx;
            double dy = 2.0 * DomainHalfM / Ny;
            double dz = DomainHeightM / Nz;
            double xMin = double.MaxValue, yMin = double.MaxValue, zMin = double.MaxValue;
            double xMax = double.MinValue, yMax = double.MinValue, zMax = double.MinValue;
            for (int k = 0; k < Nz; k++)
            {
                int kBase = k * Ny * Nx;
                double z = (k + 0.5) * dz;
                for (int j = 0; j < Ny; j++)
                {
                    int jBase = kBase + j * Nx;
                    double y = -DomainHalfM + (j + 0.5) * dy;
                    for (int i = 0; i < Nx; i++)
                    {
                        if (!Mask[jBase + i]) continue;
                        double x = -DomainHalfM + (i + 0.5) * dx;
                        if (x < xMin) xMin = x;
                        if (y < yMin) yMin = y;
                        if (z < zMin) zMin = z;
                        if (x > xMax) xMax = x;
                        if (y > yMax) yMax = y;
                        if (z > zMax) zMax = z;
                    }
                }
            }
            return (xMin, yMin, zMin, xMax, yMax, zMax);
        }

        /// <summary>Returns true if any cell of the cloud lies within
        /// <paramref name="radiusM"/> metres of (x, y, z). Used by the greedy
        /// detector allocator to score candidate positions.</summary>
        public bool CellWithinRadius(double xSi, double ySi, double zSi, double radiusM)
        {
            if (!IsValid) return false;
            double r2 = radiusM * radiusM;
            double dx = 2.0 * DomainHalfM / Nx;
            double dy = 2.0 * DomainHalfM / Ny;
            double dz = DomainHeightM / Nz;
            // Restrict cell search to the AABB intersected with the sphere bbox.
            int i0 = (int)Math.Floor((xSi - radiusM + DomainHalfM) / dx);
            int i1 = (int)Math.Ceiling((xSi + radiusM + DomainHalfM) / dx);
            int j0 = (int)Math.Floor((ySi - radiusM + DomainHalfM) / dy);
            int j1 = (int)Math.Ceiling((ySi + radiusM + DomainHalfM) / dy);
            int k0 = (int)Math.Floor((zSi - radiusM) / dz);
            int k1 = (int)Math.Ceiling((zSi + radiusM) / dz);
            if (i0 < 0) i0 = 0; if (i1 >= Nx) i1 = Nx - 1;
            if (j0 < 0) j0 = 0; if (j1 >= Ny) j1 = Ny - 1;
            if (k0 < 0) k0 = 0; if (k1 >= Nz) k1 = Nz - 1;
            for (int k = k0; k <= k1; k++)
            {
                double zCell = (k + 0.5) * dz;
                double dzd = zCell - zSi;
                double dzd2 = dzd * dzd;
                if (dzd2 > r2) continue;
                int kBase = k * Ny * Nx;
                for (int j = j0; j <= j1; j++)
                {
                    double yCell = -DomainHalfM + (j + 0.5) * dy;
                    double dyd = yCell - ySi;
                    double dyd2 = dyd * dyd;
                    if (dyd2 + dzd2 > r2) continue;
                    int jBase = kBase + j * Nx;
                    for (int i = i0; i <= i1; i++)
                    {
                        if (!Mask[jBase + i]) continue;
                        double xCell = -DomainHalfM + (i + 0.5) * dx;
                        double dxd = xCell - xSi;
                        if (dxd * dxd + dyd2 + dzd2 <= r2) return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Loads every simulation referenced by a <see cref="DispersionStudy"/> and
    /// materialises a <see cref="CloudSnapshot"/> for each — thresholding the
    /// final-timestep field at the study's detection criterion. Missing /
    /// failed results are reported via <see cref="CloudSnapshot.Error"/> but
    /// don't abort the load (so the allocator can still work on the rest).
    /// </summary>
    public static class DispersionStudyEngine
    {
        public static List<CloudSnapshot> LoadClouds(DispersionStudy study, Scene3D scene)
        {
            var output = new List<CloudSnapshot>();
            if (study == null || scene == null) return output;
            foreach (var simId in study.SimulationIds)
            {
                var sim = scene.Simulations.FirstOrDefault(s => s.Id == simId);
                if (sim == null) continue;
                output.Add(LoadCloud(sim, study, scene));
            }
            return output;
        }

        private static CloudSnapshot LoadCloud(Simulation sim, DispersionStudy study, Scene3D scene)
        {
            var snap = new CloudSnapshot
            {
                SimulationId = sim.Id,
                SimulationName = sim.Name ?? sim.Id
            };
            try
            {
                if (string.IsNullOrEmpty(sim.CasePath) || !Directory.Exists(sim.CasePath))
                {
                    snap.Error = "CasePath missing — simulation not run / not loaded.";
                    return snap;
                }

                int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60;
                int ny = nx;
                int nz = Math.Max(1, nx / 2);
                double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

                // Resolve the species name (CH4 / SF6 / s) and try OpenFOAM time-dir layout
                // first; fall back to flat-bin (FluidX3D dispersion/fire output).
                string speciesName = OpenFoamCaseGenerator.ResolveOpenFoamSpecies(sim.SnapshotSource);
                Models.OpenFoamResult result = null;
                try
                {
                    result = OpenFoamResultReader.ReadResults(sim.CasePath, nx, ny, nz, half,
                        scalarFieldName: speciesName);
                }
                catch { result = null; }

                if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                {
                    result = TryLoadFlatBin(sim.CasePath, ref nx, ref ny, ref nz, half);
                    if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                    {
                        snap.Error = "No readable result at " + sim.CasePath;
                        return snap;
                    }
                }

                // Last timestep: transient → final state; steady → only entry.
                double lastT = result.TimeSteps[result.TimeSteps.Count - 1];
                var field = result.GetField(lastT);
                if (field == null)
                {
                    snap.Error = "Final timestep field unavailable.";
                    return snap;
                }

                // Transform to detection unit.
                var gas = ResolveGasForSimulation(sim, scene);
                double[,,] qField;
                if (FieldTransform.NeedsSpeciesField(study.DetectionQuantity))
                    qField = FieldTransform.FromMassFraction(field, study.DetectionQuantity, gas);
                else
                    qField = field; // T / pressure / etc.: field is already in its unit

                // Build mask.
                int fnx = qField.GetLength(0), fny = qField.GetLength(1), fnz = qField.GetLength(2);
                snap.Nx = fnx; snap.Ny = fny; snap.Nz = fnz;
                snap.DomainHalfM = half;
                snap.DomainHeightM = half; // dispersion runners use height == half
                var mask = new bool[fnx * fny * fnz];
                int count = 0;
                double thr = study.DetectionThreshold;
                for (int k = 0; k < fnz; k++)
                {
                    int kBase = k * fny * fnx;
                    for (int j = 0; j < fny; j++)
                    {
                        int jBase = kBase + j * fnx;
                        for (int i = 0; i < fnx; i++)
                        {
                            if (qField[i, j, k] >= thr) { mask[jBase + i] = true; count++; }
                        }
                    }
                }
                snap.Mask = mask;
                snap.CloudCellCount = count;
                if (count == 0)
                    snap.Error = "Threshold not exceeded anywhere — cloud empty.";
                return snap;
            }
            catch (Exception ex)
            {
                snap.Error = ex.Message;
                return snap;
            }
        }

        /// <summary>Same flat-bin loader the View renderer uses. Duplicated here so
        /// we don't introduce a dependency cycle into <see cref="ViewRenderer"/>.</summary>
        private static Models.OpenFoamResult TryLoadFlatBin(string caseDir,
            ref int nx, ref int ny, ref int nz, double half)
        {
            if (string.IsNullOrEmpty(caseDir) || !Directory.Exists(caseDir)) return null;
            if (File.Exists(Path.Combine(caseDir, "system", "controlDict"))) return null;
            // Only the species/concentration channel — *_T.bin (fire temperature) excluded.
            var binFiles = Directory.GetFiles(caseDir, "*.bin", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileNameWithoutExtension(p).EndsWith("_T",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (binFiles.Length == 0) return null;

            try
            {
                long bytes = new FileInfo(binFiles[0]).Length;
                long doubles = bytes / sizeof(double);
                int bestNx = 0;
                for (int c = 8; c <= 1024; c++)
                {
                    int cnz = Math.Max(8, c / 2);
                    if ((long)c * c * cnz == doubles) { bestNx = c; break; }
                }
                if (bestNx > 0) { nx = bestNx; ny = bestNx; nz = Math.Max(8, bestNx / 2); }
            }
            catch { }

            var result = new Models.OpenFoamResult
            {
                GridNx = nx, GridNy = ny, GridNz = nz,
                DomainSizeM = half,
                DomainXMin = -half, DomainXMax = half,
                DomainYMin = -half, DomainYMax = half,
                DomainZMax = half,
                CaseDir = caseDir
            };
            foreach (var f in binFiles)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (double.TryParse(name, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double t))
                {
                    result.TimeSteps.Add(t);
                    result.TimeStepPaths[t] = f;
                }
            }
            result.TimeSteps.Sort();
            result.IsLoaded = result.TimeSteps.Count > 0;
            return result;
        }

        private static GasProperties ResolveGasForSimulation(Simulation sim, Scene3D scene)
        {
            if (sim?.SnapshotSource == null) return null;
            var src = sim.SnapshotSource;
            if (!string.IsNullOrEmpty(src.GasRefId) && scene?.GasLibrary != null)
            {
                var lib = scene.GasLibrary.Find(g => g.Id == src.GasRefId);
                if (lib != null) return lib.AsGasProperties();
            }
            return src.Gas;
        }
    }
}
