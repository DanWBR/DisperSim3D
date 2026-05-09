using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a gas detector placed in the 3D scene that monitors concentration levels
    /// and records detection events over time.
    /// </summary>
    public class GasDetector3D
    {
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Category("Identity")]
        [Description("Display name shown in the project tree and detector reports.")]
        public string Name { get; set; } = "Detector1";

        [Category("Position")]
        [Description("3D position of the detector in the scene (m).")]
        public Point3D Position { get; set; }

        [Category("Detection")]
        [Description("Concentration threshold above which the detector triggers an alarm (kg/m³).")]
        public double ThresholdKgM3 { get; set; } = 0.01;

        [Category("Display")]
        [Description("Whether the detector marker is shown in the 3D viewport.")]
        public bool Visible { get; set; } = true;

        [Category("Detection")]
        [Description("Simulation time when the detector first triggered (s). -1 = not triggered.")]
        public double DetectionTimeS { get; set; } = -1;

        [Category("Detection")]
        [Description("Whether this detector has been triggered during the latest run.")]
        public bool Detected { get; set; }

        [Category("Detection")]
        [Description("Concentration time-series recorded at this detector during the run.")]
        public List<MonitorSample> TimeSeries { get; set; } = new List<MonitorSample>();
    }

    /// <summary>
    /// Aggregated evaluation result for all gas detectors in a simulation scenario,
    /// providing coverage and response time statistics.
    /// </summary>
    public class DetectorEvaluationResult
    {
        /// <summary>
        /// Gets or sets the total number of gas detectors in the scenario.
        /// </summary>
        public int TotalDetectors { get; set; }

        /// <summary>
        /// Gets or sets the number of detectors that were triggered during the simulation.
        /// </summary>
        public int DetectorsTriggered { get; set; }

        /// <summary>
        /// Gets the percentage of detectors that were triggered, calculated as
        /// (<see cref="DetectorsTriggered"/> / <see cref="TotalDetectors"/>) * 100.
        /// Returns 0 if there are no detectors.
        /// </summary>
        public double CoveragePercent => TotalDetectors > 0 ? 100.0 * DetectorsTriggered / TotalDetectors : 0;

        /// <summary>
        /// Gets or sets the minimum detection time in seconds among all triggered detectors.
        /// Defaults to <see cref="double.MaxValue"/> when no detectors have been triggered.
        /// </summary>
        public double MinDetectionTimeS { get; set; } = double.MaxValue;

        /// <summary>
        /// Gets or sets the maximum detection time in seconds among all triggered detectors.
        /// </summary>
        public double MaxDetectionTimeS { get; set; }

        /// <summary>
        /// Gets or sets the average detection time in seconds among all triggered detectors.
        /// </summary>
        public double AvgDetectionTimeS { get; set; }
    }
}
