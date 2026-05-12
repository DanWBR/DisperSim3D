using System;
using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Implements the gas-detector placement optimisation procedure of Vianna 2019
    /// (Computers and Chemical Engineering 121:388-395, "The set covering problem
    /// applied to optimisation of gas detectors in chemical process plants").
    ///
    /// Pipeline:
    ///   1) Resolve concentration fields from a set of completed Simulations.
    ///   2) Choose mesh size L = ∛(min flammable cloud volume) (Eq. 23 of the paper),
    ///      unless the user supplied an explicit value.
    ///   3) Discretise the protected volume into cubic cells of side L.
    ///   4) Mark every cell that lies inside any flammable cloud across the scenarios.
    ///      These are the rows of the SCP that must be covered.
    ///   5) Build the dominance adjacency: a candidate detector at cell j covers
    ///      cell i if i is within the detector dominance neighbourhood of j
    ///      (default: self + 6 cardinal neighbours, ie cell ±1 step in x/y/z).
    ///   6) Solve the SCP via <see cref="SetCoveringSolver"/>.
    ///   7) Return the world-space coordinates of the optimal detector cells.
    /// </summary>
    public static class DetectorOptimizer
    {
        public enum NeighborhoodKind
        {
            /// <summary>Self + 6 face-adjacent cells (±x, ±y, ±z) — Vianna 2019 Fig 4 / 5.</summary>
            Cardinal,
            /// <summary>Self + 26 surrounding cells (Moore neighbourhood) — denser cover, fewer detectors.</summary>
            Moore
        }

        public class Input
        {
            public IList<Simulation> Simulations;
            public Scene3D Scene;
            public BoundingBox ProtectedRegion;
            /// <summary>Flammable concentration threshold (kg/m³). Defaults to gas LFL.</summary>
            public double ConcentrationThresholdKgM3;
            /// <summary>Optional override for cell side length (m). When 0, computed via Eq. 23.</summary>
            public double MeshSizeMOverride;
            /// <summary>Detector dominance radius in cells. 1 = nearest neighbours only.</summary>
            public int DominanceRadiusCells = 1;
            /// <summary>Cardinal (6 face-adjacent) or Moore (26 surrounding) neighbourhood pattern.</summary>
            public NeighborhoodKind Neighborhood = NeighborhoodKind.Cardinal;
            /// <summary>True = exact Balas-style branch-and-bound with greedy upper bound.
            /// False = greedy + local refinement only (faster, near-optimum).</summary>
            public bool UseExactSolver = true;
        }

        public class OptimizationResult
        {
            public List<Point3D> DetectorPositions = new List<Point3D>();
            public double MeshSizeM;
            public int TotalCells;
            public int RequiredCoverageCells;
            public bool FullyCovered;
            public string Notes;
        }

        public static OptimizationResult Run(Input input, Action<string> log = null)
        {
            var result = new OptimizationResult();
            if (input?.Simulations == null || input.Simulations.Count == 0)
            {
                result.Notes = "No simulations provided";
                return result;
            }

            // 1) Load every concentration field + extract flammable bounds (LFL)
            log?.Invoke("Loading simulation results...");

            double thresholdC = input.ConcentrationThresholdKgM3;
            if (thresholdC <= 0)
                thresholdC = ResolveLfl(input.Simulations, input.Scene);
            if (thresholdC <= 0)
            {
                result.Notes = "No flammability threshold available (set the gas LFL or pass ConcentrationThresholdKgM3)";
                return result;
            }

            var loadedFields = new List<LoadedField>();
            foreach (var sim in input.Simulations)
            {
                if (sim.Status != SimulationStatus.Completed) continue;
                var lf = LoadField(sim);
                if (lf != null) loadedFields.Add(lf);
            }
            if (loadedFields.Count == 0)
            {
                result.Notes = "No completed simulation has a readable concentration field";
                return result;
            }

            // 2) Cell size — Eq. 23 of the paper: L = ∛(min flammable volume)
            double meshSize = input.MeshSizeMOverride;
            if (meshSize <= 0)
            {
                double minVol = double.MaxValue;
                foreach (var f in loadedFields)
                {
                    var cloud = FlammableCloudCalculator.Compute(f.Concentration,
                        f.CellSizeX, f.CellSizeY, f.CellSizeZ, thresholdC, double.MaxValue);
                    if (cloud.VolumeM3 > 0 && cloud.VolumeM3 < minVol) minVol = cloud.VolumeM3;
                }
                if (minVol <= 0 || double.IsInfinity(minVol)) minVol = 27.0; // fallback: 3³
                meshSize = Math.Pow(minVol, 1.0 / 3.0);
            }
            result.MeshSizeM = meshSize;
            log?.Invoke(string.Format("Mesh size L = {0:F2} m", meshSize));

            // 3) Discretise the protected region into cubic cells
            var bbox = input.ProtectedRegion;
            double xMin = bbox.Min.X, yMin = bbox.Min.Y, zMin = Math.Max(0, bbox.Min.Z);
            double xMax = bbox.Max.X, yMax = bbox.Max.Y, zMax = bbox.Max.Z;
            int nx = Math.Max(1, (int)Math.Ceiling((xMax - xMin) / meshSize));
            int ny = Math.Max(1, (int)Math.Ceiling((yMax - yMin) / meshSize));
            int nz = Math.Max(1, (int)Math.Ceiling((zMax - zMin) / meshSize));
            int totalCells = nx * ny * nz;
            result.TotalCells = totalCells;
            log?.Invoke(string.Format("Grid: {0}×{1}×{2} = {3} cells", nx, ny, nz, totalCells));
            if (totalCells > 50000)
            {
                result.Notes = "Grid too large (>50k cells). Increase mesh size or shrink the region.";
                return result;
            }

            // 4) Mark cells touched by any flammable cloud across all scenarios
            var leakSignature = new bool[totalCells];
            int signatureCount = 0;
            for (int gi = 0; gi < nx; gi++)
            for (int gj = 0; gj < ny; gj++)
            for (int gk = 0; gk < nz; gk++)
            {
                double cx = xMin + (gi + 0.5) * meshSize;
                double cy = yMin + (gj + 0.5) * meshSize;
                double cz = zMin + (gk + 0.5) * meshSize;
                foreach (var f in loadedFields)
                {
                    if (SampleConcentration(f, cx, cy, cz) >= thresholdC)
                    {
                        int idx = (gi * ny + gj) * nz + gk;
                        if (!leakSignature[idx]) { leakSignature[idx] = true; signatureCount++; }
                        break;
                    }
                }
            }
            result.RequiredCoverageCells = signatureCount;
            log?.Invoke(string.Format("Cells inside flammable envelope: {0}", signatureCount));
            if (signatureCount == 0)
            {
                result.Notes = "No cell is reached by any flammable cloud — check thresholds and region bounds.";
                return result;
            }

            // 5) Build dominance adjacency
            //    rows[i] = set of column indices that cover row i (signature cell i)
            int radius = Math.Max(1, input.DominanceRadiusCells);
            var rows = new List<HashSet<int>>(signatureCount);
            var rowToSignatureCellIndex = new List<int>(signatureCount);
            for (int i = 0; i < totalCells; i++)
            {
                if (!leakSignature[i]) continue;
                rows.Add(BuildDominanceColumns(i, nx, ny, nz, radius, input.Neighborhood));
                rowToSignatureCellIndex.Add(i);
            }

            // 6) Solve SCP
            log?.Invoke(input.UseExactSolver
                ? "Solving SCP (Balas branch-and-bound)..."
                : "Solving SCP (greedy)...");
            var scp = input.UseExactSolver
                ? SetCoveringSolver.SolveExact(rows, totalCells)
                : SetCoveringSolver.Solve(rows, totalCells);
            result.FullyCovered = scp.AllCovered;
            log?.Invoke(string.Format("Optimal detectors: {0}", scp.SelectedColumns.Count));

            // 7) Convert column indices back to world coordinates
            foreach (var colIdx in scp.SelectedColumns)
            {
                int gi = colIdx / (ny * nz);
                int rem = colIdx % (ny * nz);
                int gj = rem / nz;
                int gk = rem % nz;
                double x = xMin + (gi + 0.5) * meshSize;
                double y = yMin + (gj + 0.5) * meshSize;
                double z = zMin + (gk + 0.5) * meshSize;
                result.DetectorPositions.Add(new Point3D(x, y, z));
            }

            return result;
        }

        // ─── helpers ───────────────────────────────────────────────

        private static HashSet<int> BuildDominanceColumns(int cellIndex, int nx, int ny, int nz,
            int radius, NeighborhoodKind kind)
        {
            int gi = cellIndex / (ny * nz);
            int rem = cellIndex % (ny * nz);
            int gj = rem / nz;
            int gk = rem % nz;
            var set = new HashSet<int>();

            if (kind == NeighborhoodKind.Cardinal)
            {
                // Cardinal-only dominance per Vianna 2019 Fig 4 (extended to 3D in Fig 5)
                for (int d = -radius; d <= radius; d++)
                {
                    AddIfInside(set, gi + d, gj, gk, nx, ny, nz);
                    AddIfInside(set, gi, gj + d, gk, nx, ny, nz);
                    AddIfInside(set, gi, gj, gk + d, nx, ny, nz);
                }
            }
            else
            {
                // Moore: every cell within Chebyshev distance `radius`
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dz = -radius; dz <= radius; dz++)
                            AddIfInside(set, gi + dx, gj + dy, gk + dz, nx, ny, nz);
            }
            return set;
        }

        private static void AddIfInside(HashSet<int> set, int i, int j, int k, int nx, int ny, int nz)
        {
            if (i < 0 || i >= nx || j < 0 || j >= ny || k < 0 || k >= nz) return;
            set.Add((i * ny + j) * nz + k);
        }

        private static double ResolveLfl(IList<Simulation> sims, Scene3D scene)
        {
            foreach (var sim in sims)
            {
                var snap = sim.SnapshotGas;
                if (snap?.PureGas != null && snap.PureGas.LFL > 0) return snap.PureGas.LFL;
            }
            if (scene?.GasLibrary != null)
                foreach (var g in scene.GasLibrary)
                    if (g.PureGas != null && g.PureGas.LFL > 0) return g.PureGas.LFL;
            return 0;
        }

        private class LoadedField
        {
            public double[,,] Concentration;
            public double XMin, YMin, ZMin;
            public double XMax, YMax, ZMax;
            public double CellSizeX, CellSizeY, CellSizeZ;
        }

        private static LoadedField LoadField(Simulation sim)
        {
            // Prefer the in-memory result if the user has it open (faster, no disk I/O)
            var tag = sim.ResultTag as OpenFoamResult;
            if (tag != null && tag.IsLoaded && tag.TimeSteps.Count > 0)
                return ToLoadedField(tag);

            // Fallback: re-read from CasePath on disk
            if (string.IsNullOrEmpty(sim.CasePath) || !System.IO.Directory.Exists(sim.CasePath))
                return null;

            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 40;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double domain = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

            string fieldName = ResolveFieldName(sim);
            try
            {
                var result = OpenFoamResultReader.ReadResults(sim.CasePath, nx, ny, nz, domain,
                    null, fieldName);
                if (result.IsLoaded && result.TimeSteps.Count > 0)
                {
                    sim.ResultTag = result; // cache on the sim for next time
                    return ToLoadedField(result);
                }
            }
            catch { /* fall through */ }
            return null;
        }

        private static string ResolveFieldName(Simulation sim)
        {
            switch (sim.SolverType)
            {
                case CfdSolverType.ReactingFoam:
                case CfdSolverType.RhoReactingBuoyantFoam:
                    return "CH4";
                case CfdSolverType.BuoyantPimpleFoam:
                    return "s";
                default:
                    return "T";
            }
        }

        private static LoadedField ToLoadedField(OpenFoamResult r)
        {
            var last = r.GetField(r.TimeSteps[r.TimeSteps.Count - 1]);
            if (last == null) return null;
            int nx = last.GetLength(0), ny = last.GetLength(1), nz = last.GetLength(2);
            double dx = (r.DomainXMax - r.DomainXMin) / nx;
            double dy = (r.DomainYMax - r.DomainYMin) / ny;
            double dz = r.DomainZMax / nz;
            return new LoadedField
            {
                Concentration = last,
                XMin = r.DomainXMin, XMax = r.DomainXMax,
                YMin = r.DomainYMin, YMax = r.DomainYMax,
                ZMin = 0, ZMax = r.DomainZMax,
                CellSizeX = dx, CellSizeY = dy, CellSizeZ = dz
            };
        }

        private static double SampleConcentration(LoadedField f, double x, double y, double z)
        {
            int i = (int)((x - f.XMin) / f.CellSizeX);
            int j = (int)((y - f.YMin) / f.CellSizeY);
            int k = (int)((z - f.ZMin) / f.CellSizeZ);
            int nx = f.Concentration.GetLength(0);
            int ny = f.Concentration.GetLength(1);
            int nz = f.Concentration.GetLength(2);
            if (i < 0 || i >= nx || j < 0 || j >= ny || k < 0 || k >= nz) return 0;
            return f.Concentration[i, j, k];
        }
    }
}
