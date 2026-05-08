using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a gas detector placed in the 3D scene that monitors concentration levels
    /// and records detection events over time.
    /// </summary>
    public class GasDetector3D
    {
        /// <summary>
        /// Gets or sets the unique identifier for this gas detector.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the display name of this gas detector.
        /// </summary>
        public string Name { get; set; } = "Detector1";

        /// <summary>
        /// Gets or sets the 3D position of the detector in the scene, in meters.
        /// </summary>
        public Point3D Position { get; set; }

        /// <summary>
        /// Gets or sets the concentration threshold for triggering detection, in kg/m^3. Default is 0.01 kg/m^3.
        /// </summary>
        public double ThresholdKgM3 { get; set; } = 0.01;

        /// <summary>
        /// Gets or sets a value indicating whether the detector is visible in the 3D viewport. Default is <c>true</c>.
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Gets or sets the simulation time in seconds at which the detector first triggered. A value of -1 indicates no detection has occurred.
        /// </summary>
        public double DetectionTimeS { get; set; } = -1;

        /// <summary>
        /// Gets or sets a value indicating whether the detector has been triggered (concentration exceeded the threshold).
        /// </summary>
        public bool Detected { get; set; }

        /// <summary>
        /// Gets or sets the time-series of concentration samples recorded by this detector.
        /// </summary>
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
