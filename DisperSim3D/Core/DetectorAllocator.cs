using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Greedy maximum-coverage solver for the
    /// "place K detectors to detect N clouds" set-cover problem.
    ///
    /// Each cloud comes from a <see cref="CloudSnapshot"/> produced by
    /// <see cref="DispersionStudyEngine"/> — it's a binary mask of cells where
    /// the chosen detection quantity exceeds the threshold. Candidate detector
    /// positions are sampled on a 3D grid restricted to a vertical breathing
    /// zone and culled against scene obstacles. A detector at p "covers" cloud
    /// C iff at least one cell of C lies within the detection radius of p.
    ///
    /// The greedy step picks the candidate covering the most CURRENTLY-uncovered
    /// clouds. This yields a (1 − 1/e) ≈ 63% optimality bound versus the optimal
    /// MILP solution (Nemhauser et al. 1978) — fine for engineering screening.
    /// </summary>
    public static class DetectorAllocator
    {
        public sealed class Result
        {
            public List<Point3D> Positions = new List<Point3D>();
            public Dictionary<string, bool> PerCloudCovered = new Dictionary<string, bool>();
            public double CoveragePercent;
            public int CandidateCount;
            public int CloudCount;
            public string Message;
        }

        /// <summary>
        /// Runs greedy allocation. <paramref name="existingDetectors"/> (if any) seed
        /// the "already covered" set so the algorithm only adds NEW positions.
        /// </summary>
        public static Result RunGreedy(
            DetectorAllocation cfg,
            List<CloudSnapshot> clouds,
            IList<BoundingBox> obstacles,
            IList<GasDetector3D> existingDetectors,
            double domainHalfM)
        {
            var result = new Result();
            if (clouds == null) clouds = new List<CloudSnapshot>();
            var validClouds = clouds.Where(c => c.IsValid).ToList();
            result.CloudCount = validClouds.Count;
            if (validClouds.Count == 0)
            {
                result.Message = "No valid clouds (all empty or unreadable).";
                return result;
            }

            // Resolve domain extents for the candidate grid. Use the maximum extent
            // across all clouds so all are reachable; fall back to the supplied
            // domain half if not provided.
            double half = domainHalfM > 0 ? domainHalfM : validClouds.Max(c => c.DomainHalfM);
            double minZ = Math.Max(0, cfg.MinZ);
            double maxZ = Math.Max(minZ + 0.1, cfg.MaxZ);

            // Build candidate grid.
            int nx = Math.Max(2, cfg.CandidateNx);
            int ny = Math.Max(2, cfg.CandidateNy);
            int nz = Math.Max(1, cfg.CandidateNz);
            double dx = 2.0 * half / nx;
            double dy = 2.0 * half / ny;
            double dz = nz > 1 ? (maxZ - minZ) / (nz - 1) : 0;

            var candidates = new List<Point3D>(nx * ny * nz);
            for (int k = 0; k < nz; k++)
            {
                double z = nz > 1 ? minZ + k * dz : 0.5 * (minZ + maxZ);
                for (int j = 0; j < ny; j++)
                {
                    double y = -half + (j + 0.5) * dy;
                    for (int i = 0; i < nx; i++)
                    {
                        double x = -half + (i + 0.5) * dx;
                        if (IsInsideObstacle(x, y, z, obstacles)) continue;
                        candidates.Add(new Point3D(x, y, z));
                    }
                }
            }
            result.CandidateCount = candidates.Count;
            if (candidates.Count == 0)
            {
                result.Message = "All candidate positions are inside obstacles — relax the grid or Z range.";
                return result;
            }

            // Pre-compute the per-candidate coverage set (indices into validClouds).
            // For 60·60·3 = 10800 candidates × N clouds × O(per-cloud cell radius check),
            // this is the costliest step. Run it once up front.
            double r = Math.Max(0.01, cfg.DetectionRadiusM);
            var cover = new List<HashSet<int>>(candidates.Count);
            for (int ci = 0; ci < candidates.Count; ci++)
            {
                var set = new HashSet<int>();
                var p = candidates[ci];
                for (int s = 0; s < validClouds.Count; s++)
                {
                    if (validClouds[s].CellWithinRadius(p.X, p.Y, p.Z, r)) set.Add(s);
                }
                cover.Add(set);
            }

            // Seed "covered" with existing detectors when requested.
            var covered = new HashSet<int>();
            if (cfg.UseExistingDetectors && existingDetectors != null)
            {
                foreach (var d in existingDetectors)
                {
                    for (int s = 0; s < validClouds.Count; s++)
                        if (validClouds[s].CellWithinRadius(d.Position.X, d.Position.Y, d.Position.Z, r))
                            covered.Add(s);
                }
            }

            // Greedy picking.
            int budget = cfg.MaxDetectors > 0 ? cfg.MaxDetectors : int.MaxValue;
            double targetFrac = cfg.Objective == AllocationObjective.CoverPercentage
                ? Math.Max(0, Math.Min(1, cfg.TargetCoveragePercent / 100.0))
                : 1.0;

            while (result.Positions.Count < budget && covered.Count < validClouds.Count)
            {
                double frac = (double)covered.Count / validClouds.Count;
                if (frac >= targetFrac) break;

                int bestCi = -1;
                int bestGain = 0;
                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    int gain = 0;
                    foreach (var s in cover[ci]) if (!covered.Contains(s)) gain++;
                    if (gain > bestGain) { bestGain = gain; bestCi = ci; }
                }
                if (bestCi < 0 || bestGain == 0) break;
                result.Positions.Add(candidates[bestCi]);
                foreach (var s in cover[bestCi]) covered.Add(s);
            }

            // Build per-cloud coverage map.
            for (int s = 0; s < validClouds.Count; s++)
                result.PerCloudCovered[validClouds[s].SimulationId] = covered.Contains(s);
            // Also include clouds that were SKIPPED (empty/failed) as not-covered.
            foreach (var c in clouds.Where(c => !c.IsValid))
                if (!result.PerCloudCovered.ContainsKey(c.SimulationId))
                    result.PerCloudCovered[c.SimulationId] = false;

            result.CoveragePercent = 100.0 * covered.Count / validClouds.Count;
            int existingUsed = cfg.UseExistingDetectors && existingDetectors != null ? existingDetectors.Count : 0;
            result.Message = string.Format(
                "Allocated {0} new detector(s){1} → {2:F1}% coverage of {3} valid clouds.",
                result.Positions.Count,
                existingUsed > 0 ? " (plus " + existingUsed + " pre-existing)" : "",
                result.CoveragePercent,
                validClouds.Count);
            return result;
        }

        private static bool IsInsideObstacle(double x, double y, double z, IList<BoundingBox> obstacles)
        {
            if (obstacles == null) return false;
            foreach (var bb in obstacles)
            {
                if (bb == null) continue;
                if (x >= bb.Min.X && x <= bb.Max.X
                    && y >= bb.Min.Y && y <= bb.Max.Y
                    && z >= bb.Min.Z && z <= bb.Max.Z)
                    return true;
            }
            return false;
        }
    }
}
