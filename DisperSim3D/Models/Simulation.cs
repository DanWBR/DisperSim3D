using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; }

        [Category("Identity")]
        [Description("Display name shown in the project tree and reports.")]
        public string Name { get; set; }

        [Category("Identity")]
        [Description("Date and time the simulation was created.")]
        public DateTime CreatedAt { get; set; }

        [Category("Identity")]
        [Description("Date and time the simulation finished. Empty if still running.")]
        public DateTime? CompletedAt { get; set; }

        [Category("References")]
        [Description("ID of the source from the project Sources section.")]
        public string SourceId { get; set; }

        [Category("References")]
        [Description("ID of the wind field from the project Wind Fields section.")]
        public string WindFieldId { get; set; }

        [Category("Solver")]
        [Description("Dispersion solver type (Gaussian or CFD variant).")]
        public CfdSolverType SolverType { get; set; }

        [Category("Solver")]
        [Description("Current state: Configured, Queued, Running, Completed or Failed.")]
        public SimulationStatus Status { get; set; }

        [Category("Solver")]
        [Description("Human-readable status detail (error message on failure).")]
        public string StatusMessage { get; set; }

        [Category("Solver")]
        [Description("Run progress 0..1 (used by background workers).")]
        public double Progress { get; set; }

        [Category("Snapshot")]
        [Description("Frozen copy of the source configuration at Run time. Editing the live source does not affect this.")]
        public ReleaseSource3D SnapshotSource { get; set; }

        [Category("Snapshot")]
        [Description("Frozen copy of the gas library item at Run time.")]
        public GasLibraryItem SnapshotGas { get; set; }

        [Category("Snapshot")]
        [Description("Frozen meteorological conditions used by the run.")]
        public MeteorologicalConditions SnapshotMeteo { get; set; }

        [Category("Snapshot")]
        [Description("Frozen CFD configuration used by the run.")]
        public CfdConfiguration SnapshotCfdConfig { get; set; }

        [Category("Snapshot")]
        [Description("Half-extent of the simulation box used by the run (m).")]
        public double SnapshotDomainSizeM { get; set; }

        [Category("Snapshot")]
        [Description("Grid resolution per axis used by the run.")]
        public int SnapshotGridResolution { get; set; }

        [Category("Snapshot")]
        [Description("Total simulation time used by the run (s).")]
        public double SnapshotDurationS { get; set; }

        [Category("Snapshot")]
        [Description("Output write interval used by the run (s).")]
        public double SnapshotTimeStepS { get; set; }

        [Category("Snapshot")]
        [Description("Concentration thresholds used by the run for visualization and alerting.")]
        public List<DispersionThreshold> SnapshotThresholds { get; set; }

        [Category("Result")]
        [Description("Path to the OpenFOAM case directory on disk (empty for Gaussian runs).")]
        public string CasePath { get; set; }

        [Category("Result")]
        [Description("Number of time-step output frames produced.")]
        public int TimeStepCount { get; set; }

        [Category("Result")]
        [Description("Peak concentration observed in the result (kg/m³).")]
        public double MaxConcentration { get; set; }

        [Category("Visualization")]
        [Description("Whether this simulation's result is visible in the 3D viewport.")]
        public bool IsVisible { get; set; }

        [Category("Bundling")]
        [Description("How the OpenFOAM case is packed into a .dsproj bundle. ResultsOnly = small, FullCase = re-runnable after extraction.")]
        public BundleEmbedMode EmbedMode { get; set; } = BundleEmbedMode.ResultsOnly;

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
