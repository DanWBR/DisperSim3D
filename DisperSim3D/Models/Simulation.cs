using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace DisperSim3D.Models
{
    public enum SimulationStatus
    {
        Configured,
        Queued,
        Running,
        Completed,
        Failed
    }

    /// <summary>
    /// A runnable dispersion simulation: a snapshot of (Source × WindField × Meteo × CfdConfig)
    /// captured at Run time. Simulations are first-class items inside a project.
    /// </summary>
    public class Simulation
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // User-selected references (live data — used to populate the snapshot at Run)
        public string SourceId { get; set; }
        public string WindFieldId { get; set; }

        public CfdSolverType SolverType { get; set; }
        public SimulationStatus Status { get; set; }
        public string StatusMessage { get; set; }
        public double Progress { get; set; }

        // Snapshot — frozen at Run, immutable history
        public ReleaseSource3D SnapshotSource { get; set; }
        public GasLibraryItem SnapshotGas { get; set; }
        public MeteorologicalConditions SnapshotMeteo { get; set; }
        public CfdConfiguration SnapshotCfdConfig { get; set; }
        public double SnapshotDomainSizeM { get; set; }
        public int SnapshotGridResolution { get; set; }
        public double SnapshotDurationS { get; set; }
        public double SnapshotTimeStepS { get; set; }
        public List<DispersionThreshold> SnapshotThresholds { get; set; }

        // Result — set on success
        public string CasePath { get; set; }
        public int TimeStepCount { get; set; }
        public double MaxConcentration { get; set; }

        /// <summary>Whether this simulation's result is visible in the 3D viewport.</summary>
        public bool IsVisible { get; set; }

        [XmlIgnore]
        public object ResultTag { get; set; } // OpenFoamResult or SteadyStateResultData (transient)

        public Simulation()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Simulation";
            CreatedAt = DateTime.Now;
            Status = SimulationStatus.Configured;
            SolverType = CfdSolverType.GaussianPuff;
            SnapshotThresholds = new List<DispersionThreshold>();
            SnapshotDomainSizeM = 200;
            SnapshotGridResolution = 40;
            SnapshotDurationS = 300;
            SnapshotTimeStepS = 0.5;
            Progress = 0;
        }

        public string GetSummary()
        {
            string status = Status.ToString();
            if (Status == SimulationStatus.Completed && CompletedAt.HasValue)
                status = "Completed " + CompletedAt.Value.ToString("yyyy-MM-dd HH:mm");
            return string.Format("{0} [{1}]", Name ?? "(unnamed)", status);
        }
    }
}
