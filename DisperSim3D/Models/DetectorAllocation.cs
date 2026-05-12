using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>How the greedy detector allocator decides when to stop.</summary>
    public enum AllocationObjective
    {
        /// <summary>Keep adding detectors until every cloud in the study is detected.</summary>
        CoverAll = 0,
        /// <summary>Stop once <see cref="DetectorAllocation.TargetCoveragePercent"/> of clouds are detected, OR <see cref="DetectorAllocation.MaxDetectors"/> reached.</summary>
        CoverPercentage = 1
    }

    /// <summary>Reserved — only Greedy is implemented today; placeholder for future
    /// MILP / simulated-annealing strategies.</summary>
    public enum AllocationStrategy
    {
        GreedyMaxCoverage = 0
    }

    public enum AllocationStatus
    {
        Configured = 0,
        Running = 1,
        Completed = 2,
        Failed = 3
    }

    /// <summary>
    /// A solved (or pending) detector-placement problem over a <see cref="DispersionStudy"/>.
    /// The allocator samples candidate positions on a 3D grid restricted to a vertical
    /// breathing zone, scores each by how many clouds it covers (where "covers" means
    /// "any detection-threshold cell of the cloud lies within DetectionRadiusM"), and
    /// greedily picks positions until the coverage objective is met.
    /// </summary>
    public class DetectorAllocation
    {
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Category("Identity")]
        [Description("Display name shown in the project tree.")]
        public string Name { get; set; } = "Detector Allocation";

        [Category("Input")]
        [Description("DispersionStudy this allocation targets (must contain at least one Completed simulation).")]
        public string DispersionStudyId { get; set; } = "";

        [Category("Objective")]
        [Description("CoverAll = keep adding until every cloud is detected. CoverPercentage = stop at TargetCoveragePercent or MaxDetectors, whichever comes first.")]
        public AllocationObjective Objective { get; set; } = AllocationObjective.CoverAll;

        [Category("Objective")]
        [Description("Target coverage in percent (0–100). Used only when Objective = CoverPercentage.")]
        public double TargetCoveragePercent { get; set; } = 100.0;

        [Category("Objective")]
        [Description("Maximum number of detectors the allocator may place. 0 = unlimited.")]
        public int MaxDetectors { get; set; } = 0;

        [Category("Detector")]
        [Description("Effective detection radius (m). A detector at p detects cloud C if AT LEAST ONE threshold-exceeding cell of C lies within this radius of p. Default 5 m (typical IR point sensor).")]
        public double DetectionRadiusM { get; set; } = 5.0;

        [Category("Detector")]
        [Description("Lower bound of the candidate-placement Z range (m). Default 1.5 m = breathing-zone floor.")]
        public double MinZ { get; set; } = 1.5;

        [Category("Detector")]
        [Description("Upper bound of the candidate-placement Z range (m). Default 3.0 m = breathing-zone ceiling.")]
        public double MaxZ { get; set; } = 3.0;

        [Category("Candidate grid")]
        [Description("Number of candidate positions along X.")]
        public int CandidateNx { get; set; } = 60;

        [Category("Candidate grid")]
        [Description("Number of candidate positions along Y.")]
        public int CandidateNy { get; set; } = 60;

        [Category("Candidate grid")]
        [Description("Number of candidate positions along Z (within [MinZ, MaxZ]).")]
        public int CandidateNz { get; set; } = 3;

        [Category("Existing detectors")]
        [Description("When true, the project's current GasDetector3D positions are treated as already placed: the allocator only adds NEW detectors to cover any remaining clouds. When false, allocation runs from scratch and the user must Apply to materialise the result.")]
        public bool UseExistingDetectors { get; set; } = false;

        [Category("Strategy")]
        [Description("Optimisation strategy. Greedy max-coverage is the only one implemented today.")]
        public AllocationStrategy Strategy { get; set; } = AllocationStrategy.GreedyMaxCoverage;

        // ── Results ──

        [Category("Result")]
        [Description("Positions chosen by the allocator (world-space metres). Apply turns these into GasDetector3D entries.")]
        public List<Point3D> AllocatedPositions { get; set; } = new List<Point3D>();

        [Category("Result")]
        [Description("Fraction (%) of clouds in the study that are covered by the allocated + existing detectors.")]
        public double AchievedCoveragePercent { get; set; }

        [Category("Result")]
        [Description("Per-simulation-Id coverage flag (true = detected by at least one detector).")]
        public Dictionary<string, bool> PerCloudCovered { get; set; } = new Dictionary<string, bool>();

        [Category("Result")]
        [Description("Status of the last allocation run.")]
        public AllocationStatus Status { get; set; } = AllocationStatus.Configured;

        [Category("Result")]
        [Description("Human-readable detail of the last run (errors, warnings, summary).")]
        public string StatusMessage { get; set; } = "";

        [Category("Result")]
        [Description("Timestamp of the last completed allocation run.")]
        public DateTime RunAt { get; set; }

        [Category("Visualization")]
        [Description("Whether allocated-detector markers are drawn in the 3D viewport when this allocation is the active selection.")]
        public bool IsVisible { get; set; } = true;
    }
}
