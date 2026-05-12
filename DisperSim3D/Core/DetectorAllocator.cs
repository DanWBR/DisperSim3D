using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Greedy detector-placement solver. Two strategies are supported via the
    /// <see cref="DetectorAllocation.Strategy"/> selector:
    ///
    /// <list type="bullet">
    ///   <item><see cref="AllocationStrategy.GreedyMaxCoverage"/> — original
    ///   unweighted set-cover: pick the candidate covering the most still-
    ///   uncovered clouds. (1 − 1/e) approximation vs MILP.</item>
    ///   <item><see cref="AllocationStrategy.MinResidualRisk"/> — Rad et al.
    ///   2017's Maximum Risk Reduction (MRR): pick the candidate maximising
    ///   the sum of `R_s = freq_s · cons_s · P_d` for uncovered scenarios.
    ///   Optional Rad &amp; Rashtchian 2016 distance weighting (closer detector
    ///   contributes more).</item>
    /// </list>
    ///
    /// Each cloud comes from a <see cref="CloudSnapshot"/> produced by
    /// <see cref="DispersionStudyEngine"/> — a binary mask of cells where the
    /// chosen detection quantity exceeds the threshold. Candidate detector
    /// positions are sampled on a 3D grid restricted to a vertical breathing
    /// zone and culled against scene obstacles. A detector at p "covers" cloud
    /// C iff at least one cell of C lies within the detection radius of p.
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

            // Risk-strategy outputs (populated by RunRiskReductionGreedy; left
            // at zero / empty by RunGreedy for clean back-compat).
            public double TotalRisk;
            public double ResidualRisk;
            public double RiskReductionFraction;
            public List<int> RiskCurveK = new List<int>();
            public List<double> RiskCurveRRF = new List<double>();
            public Dictionary<string, double> PerCloudResidualRisk = new Dictionary<string, double>();
        }

        /// <summary>Dispatches to the correct strategy based on
        /// <paramref name="cfg"/>'s <see cref="DetectorAllocation.Strategy"/>.
        /// Falls back to greedy max-coverage if the study or scene are null.</summary>
        public static Result Run(
            DetectorAllocation cfg,
            DispersionStudy study,
            Scene3D scene,
            List<CloudSnapshot> clouds,
            IList<BoundingBox> obstacles,
            IList<GasDetector3D> existingDetectors,
            double domainHalfM)
        {
            if (cfg != null && cfg.Strategy == AllocationStrategy.MinResidualRisk
                && study != null && scene != null)
            {
                return RunRiskReductionGreedy(cfg, study, scene, clouds, obstacles,
                    existingDetectors, domainHalfM);
            }
            return RunGreedy(cfg, clouds, obstacles, existingDetectors, domainHalfM);
        }

        /// <summary>
        /// Runs greedy maximum-coverage allocation. <paramref name="existingDetectors"/>
        /// (if any) seed the "already covered" set so the algorithm only adds NEW
        /// positions. This is the original behaviour — every cloud counts the same.
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

            var bundle = BuildCandidates(cfg, validClouds, obstacles, domainHalfM);
            if (bundle.Candidates.Count == 0)
            {
                result.Message = "All candidate positions are inside obstacles — relax the grid or Z range.";
                result.CandidateCount = 0;
                return result;
            }
            result.CandidateCount = bundle.Candidates.Count;

            double r = Math.Max(0.01, cfg.DetectionRadiusM);
            var cover = bundle.Cover;
            var candidates = bundle.Candidates;

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

        /// <summary>
        /// Runs greedy <i>risk-reduction</i> allocation (Rad et al. 2017 MRR).
        /// At each step picks the candidate that maximises the sum of
        /// `R_s = freq_s · cons_s · P_d` over still-uncovered scenarios — so a
        /// candidate that covers ONE high-risk cloud may beat one that covers
        /// many low-risk clouds. Optionally distance-weighted per Rad &amp;
        /// Rashtchian 2016: closer to the cloud → stronger contribution.
        /// </summary>
        public static Result RunRiskReductionGreedy(
            DetectorAllocation cfg,
            DispersionStudy study,
            Scene3D scene,
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

            var bundle = BuildCandidates(cfg, validClouds, obstacles, domainHalfM);
            if (bundle.Candidates.Count == 0)
            {
                result.Message = "All candidate positions are inside obstacles — relax the grid or Z range.";
                result.CandidateCount = 0;
                return result;
            }
            result.CandidateCount = bundle.Candidates.Count;

            double r = Math.Max(0.01, cfg.DetectionRadiusM);
            var cover = bundle.Cover;
            var candidates = bundle.Candidates;

            // Resolve per-scenario risk weight R[s] from the study + scene.
            double pod = cfg.DetectionProbability > 0 ? cfg.DetectionProbability : 1.0;
            var simBySimId = scene.Simulations.ToDictionary(s => s.Id, s => s);
            double[] R = new double[validClouds.Count];
            double totalRisk = 0;
            for (int s = 0; s < validClouds.Count; s++)
            {
                if (!simBySimId.TryGetValue(validClouds[s].SimulationId, out var sim))
                    continue;
                var (_, _, rs) = RiskWeightHelper.ResolveScenarioRisk(
                    study, validClouds[s], sim, scene, pod);
                R[s] = rs;
                totalRisk += rs;
            }
            result.TotalRisk = totalRisk;
            if (!(totalRisk > 0))
            {
                result.Message = "Total risk is zero — every scenario evaluated to 0. "
                    + "Check leak frequencies, wind rose and consequence inputs.";
                return result;
            }

            // Optional distance-weighted contribution table (precomputed lazily —
            // only when UseDistanceWeighting is true).
            double[][] distW = null;
            if (cfg.UseDistanceWeighting)
            {
                double wMin = Math.Max(0.0, cfg.DistanceWeightMin);
                double wMax = Math.Max(wMin, cfg.DistanceWeightMax);
                distW = new double[candidates.Count][];
                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    var p = candidates[ci];
                    var row = new double[validClouds.Count];
                    foreach (var s in cover[ci])
                    {
                        double d = ApproxDistanceToCloud(p, validClouds[s]);
                        double frac = 1.0 - Math.Max(0.0, Math.Min(1.0, d / r));
                        row[s] = wMin + (wMax - wMin) * frac;
                    }
                    distW[ci] = row;
                }
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

            // residual = total - covered (initial).
            double residual = totalRisk;
            foreach (var s in covered) residual -= R[s];
            if (residual < 0) residual = 0;

            // Initial point on the curve (0 detectors placed by us).
            result.RiskCurveK.Add(0);
            result.RiskCurveRRF.Add(1.0 - residual / totalRisk);

            // Greedy loop — Rad et al. 2017 §3.2.
            int budget = cfg.MaxDetectors > 0 ? cfg.MaxDetectors : int.MaxValue;
            double targetFrac = cfg.Objective == AllocationObjective.CoverPercentage
                ? Math.Max(0, Math.Min(1, cfg.TargetCoveragePercent / 100.0))
                : 1.0;

            const double eps = 1e-12;
            while (result.Positions.Count < budget && residual > eps)
            {
                double rrf = 1.0 - residual / totalRisk;
                if (rrf >= targetFrac) break;

                int bestCi = -1;
                double bestGain = 0;
                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    double gain = 0;
                    var coverSet = cover[ci];
                    var w = distW?[ci];
                    foreach (var s in coverSet)
                    {
                        if (covered.Contains(s)) continue;
                        gain += R[s] * (w != null ? w[s] : 1.0);
                    }
                    if (gain > bestGain) { bestGain = gain; bestCi = ci; }
                }
                if (bestCi < 0 || bestGain <= eps) break;

                result.Positions.Add(candidates[bestCi]);
                foreach (var s in cover[bestCi])
                {
                    if (covered.Add(s)) residual -= R[s];
                }
                if (residual < 0) residual = 0;
                result.RiskCurveK.Add(result.Positions.Count);
                result.RiskCurveRRF.Add(1.0 - residual / totalRisk);
            }

            // Build per-cloud coverage and residual-risk maps.
            for (int s = 0; s < validClouds.Count; s++)
            {
                bool isCovered = covered.Contains(s);
                result.PerCloudCovered[validClouds[s].SimulationId] = isCovered;
                result.PerCloudResidualRisk[validClouds[s].SimulationId] = isCovered ? 0.0 : R[s];
            }
            foreach (var c in clouds.Where(c => !c.IsValid))
            {
                if (!result.PerCloudCovered.ContainsKey(c.SimulationId))
                    result.PerCloudCovered[c.SimulationId] = false;
                if (!result.PerCloudResidualRisk.ContainsKey(c.SimulationId))
                    result.PerCloudResidualRisk[c.SimulationId] = 0.0;
            }

            result.ResidualRisk = residual;
            result.RiskReductionFraction = 1.0 - residual / totalRisk;
            result.CoveragePercent = 100.0 * covered.Count / validClouds.Count;
            int existingUsed = cfg.UseExistingDetectors && existingDetectors != null ? existingDetectors.Count : 0;
            result.Message = string.Format(
                "Allocated {0} new detector(s){1} → RRF {2:P1} (residual {3:E2} of total {4:E2}).",
                result.Positions.Count,
                existingUsed > 0 ? " (plus " + existingUsed + " pre-existing)" : "",
                result.RiskReductionFraction,
                residual,
                totalRisk);
            return result;
        }

        // ── Shared candidate-grid builder ─────────────────────────────────────

        private struct CandidateBundle
        {
            public List<Point3D> Candidates;
            public List<HashSet<int>> Cover;
        }

        /// <summary>Builds the per-candidate cover sets used by both strategies.
        /// Candidate sampling and obstacle culling are identical between the two —
        /// only the scoring loop differs.</summary>
        private static CandidateBundle BuildCandidates(DetectorAllocation cfg,
            List<CloudSnapshot> validClouds, IList<BoundingBox> obstacles, double domainHalfM)
        {
            double half = domainHalfM > 0 ? domainHalfM : validClouds.Max(c => c.DomainHalfM);
            double minZ = Math.Max(0, cfg.MinZ);
            double maxZ = Math.Max(minZ + 0.1, cfg.MaxZ);

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

            return new CandidateBundle { Candidates = candidates, Cover = cover };
        }

        /// <summary>Cheap proxy for "distance from candidate to nearest flagged cell
        /// of the cloud" — uses the cloud's AABB closest point. Sufficient for the
        /// Paper-2 distance weight, which only needs a monotone function of distance.</summary>
        private static double ApproxDistanceToCloud(Point3D p, CloudSnapshot snap)
        {
            var bb = snap.Bounds();
            if (bb == null) return 0.0;
            var b = bb.Value;
            double cx = Math.Max(b.xMin, Math.Min(p.X, b.xMax));
            double cy = Math.Max(b.yMin, Math.Min(p.Y, b.yMax));
            double cz = Math.Max(b.zMin, Math.Min(p.Z, b.zMax));
            double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
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
