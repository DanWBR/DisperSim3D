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
            List<GasDetector3D> detectors, IConcentrationField engine, double currentTimeS,
            GasProperties gas = null, Scene3D sceneForRadiation = null)
        {
            foreach (var det in detectors)
            {
                double cRaw = engine.EvaluateConcentration(
                    det.Position.X, det.Position.Y, det.Position.Z);

                // Convert from kg/m³ (engine native) to the user-picked quantity.
                // The engine returns kg/m³ — we back-derive Y by dividing by ρ_air
                // before feeding the transform helper.
                double measured;
                if (FieldTransform.IsAnalytic(det.MeasuredQuantity))
                {
                    // Radiation, thermal dose and fatality probability all come from
                    // the scene's fire sources rather than the dispersion field.
                    measured = FieldTransform.AnalyticAtPoint(sceneForRadiation,
                        det.MeasuredQuantity, det.Position.X, det.Position.Y, det.Position.Z);
                }
                else if (det.MeasuredQuantity == ViewFieldProperty.ConcentrationKgM3
                      || det.MeasuredQuantity == ViewFieldProperty.Concentration
                      || det.MeasuredQuantity == ViewFieldProperty.MassFraction)
                {
                    // Engine already gives kg/m³ — leave as-is for ConcentrationKgM3,
                    // back-derive Y for the others.
                    measured = det.MeasuredQuantity == ViewFieldProperty.ConcentrationKgM3
                        ? cRaw
                        : cRaw / 1.205; // approximate Y back from kg/m³
                }
                else
                {
                    double y = cRaw / 1.205;
                    measured = FieldTransform.ScalarFromMassFraction(y, det.MeasuredQuantity, gas);
                }

                det.TimeSeries.Add(new MonitorSample
                {
                    TimeS = currentTimeS,
                    Concentration = measured
                });

                // Trigger logic: when MeasuredQuantity is the legacy kg/m³ field and
                // Threshold is left at its default 25, the user might still rely on
                // the legacy ThresholdKgM3 field. Prefer the new Threshold when the
                // user selected a non-kg/m³ measurement.
                double trigger = det.MeasuredQuantity == ViewFieldProperty.ConcentrationKgM3
                    ? det.ThresholdKgM3
                    : det.Threshold;
                if (!det.Detected && measured >= trigger)
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
