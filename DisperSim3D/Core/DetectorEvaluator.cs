using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Evaluates gas detector responses during a dispersion simulation.
    /// </summary>
    public static class DetectorEvaluator
    {
        /// <summary>
        /// Samples concentration at each detector for a single time step, records the value,
        /// and marks detection if the threshold is exceeded.
        /// </summary>
        /// <param name="detectors">List of gas detectors to evaluate.</param>
        /// <param name="engine">Dispersion engine used to compute concentrations.</param>
        /// <param name="currentTimeS">Current simulation time in seconds.</param>
        public static void EvaluateStep(
            List<GasDetector3D> detectors, GaussianPuffEngine engine, double currentTimeS)
        {
            foreach (var det in detectors)
            {
                double c = engine.EvaluateConcentration(
                    det.Position.X, det.Position.Y, det.Position.Z);

                det.TimeSeries.Add(new MonitorSample
                {
                    TimeS = currentTimeS,
                    Concentration = c
                });

                if (!det.Detected && c >= det.ThresholdKgM3)
                {
                    det.Detected = true;
                    det.DetectionTimeS = currentTimeS;
                }
            }
        }

        /// <summary>
        /// Computes coverage statistics including total detectors, number triggered,
        /// and min/max/average detection times.
        /// </summary>
        /// <param name="detectors">List of gas detectors to summarize.</param>
        /// <returns>Aggregated detection results.</returns>
        public static DetectorEvaluationResult ComputeResults(List<GasDetector3D> detectors)
        {
            var result = new DetectorEvaluationResult
            {
                TotalDetectors = detectors.Count,
                DetectorsTriggered = detectors.Count(d => d.Detected)
            };

            var detected = detectors.Where(d => d.Detected).ToList();
            if (detected.Count > 0)
            {
                result.MinDetectionTimeS = detected.Min(d => d.DetectionTimeS);
                result.MaxDetectionTimeS = detected.Max(d => d.DetectionTimeS);
                result.AvgDetectionTimeS = detected.Average(d => d.DetectionTimeS);
            }

            return result;
        }

        /// <summary>
        /// Resets all detectors by clearing detection state and time series data.
        /// </summary>
        /// <param name="detectors">List of gas detectors to reset.</param>
        public static void Reset(List<GasDetector3D> detectors)
        {
            foreach (var det in detectors)
            {
                det.Detected = false;
                det.DetectionTimeS = -1;
                det.TimeSeries.Clear();
            }
        }
    }
}
