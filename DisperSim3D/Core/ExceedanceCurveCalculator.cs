using System;
using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Represents a single point on an exceedance probability curve.
    /// </summary>
    public class ExceedancePoint
    {
        /// <summary>Gets or sets the concentration threshold value.</summary>
        public double Threshold { get; set; }

        /// <summary>Gets or sets the probability of exceeding the threshold.</summary>
        public double Probability { get; set; }
    }

    /// <summary>
    /// Contains the exceedance curve results for a single monitor location.
    /// </summary>
    public class ExceedanceCurveResult
    {
        /// <summary>Gets or sets the name of the monitor location.</summary>
        public string LocationName { get; set; }

        /// <summary>Gets or sets the list of exceedance points forming the curve.</summary>
        public List<ExceedancePoint> Points { get; set; } = new List<ExceedancePoint>();
    }

    /// <summary>
    /// Calculates exceedance probability curves for concentration thresholds at monitor locations.
    /// </summary>
    public static class ExceedanceCurveCalculator
    {
        /// <summary>
        /// Computes exceedance probability curves across multiple scenarios with frequency weighting.
        /// For each monitor and threshold, calculates the probability that the maximum concentration
        /// exceeds the threshold based on weighted scenario frequencies.
        /// </summary>
        /// <param name="scenarios">List of dispersion scenarios.</param>
        /// <param name="monitors">List of monitor points where concentrations are evaluated.</param>
        /// <param name="thresholds">Array of concentration thresholds to evaluate.</param>
        /// <param name="scenarioFrequencies">Optional array of scenario frequencies; defaults to uniform distribution if null or mismatched length.</param>
        /// <returns>List of exceedance curve results, one per monitor location.</returns>
        public static List<ExceedanceCurveResult> Compute(
            List<DispersionScenario> scenarios,
            List<MonitorPoint3D> monitors,
            double[] thresholds,
            double[] scenarioFrequencies = null)
        {
            var results = new List<ExceedanceCurveResult>();

            if (scenarios.Count == 0 || monitors.Count == 0 || thresholds.Length == 0)
                return results;

            double[] freqs = scenarioFrequencies;
            if (freqs == null || freqs.Length != scenarios.Count)
            {
                freqs = new double[scenarios.Count];
                double uniform = 1.0 / scenarios.Count;
                for (int i = 0; i < freqs.Length; i++)
                    freqs[i] = uniform;
            }

            double totalFreq = freqs.Sum();
            if (totalFreq > 0)
            {
                for (int i = 0; i < freqs.Length; i++)
                    freqs[i] /= totalFreq;
            }

            foreach (var monitor in monitors)
            {
                var curveResult = new ExceedanceCurveResult
                {
                    LocationName = monitor.Name
                };

                foreach (double threshold in thresholds.OrderBy(t => t))
                {
                    double exceedProb = 0;

                    for (int s = 0; s < scenarios.Count; s++)
                    {
                        double maxConc = GetMaxConcentrationAtMonitor(scenarios[s], monitor);
                        if (maxConc >= threshold)
                            exceedProb += freqs[s];
                    }

                    curveResult.Points.Add(new ExceedancePoint
                    {
                        Threshold = threshold,
                        Probability = Math.Min(1.0, exceedProb)
                    });
                }

                results.Add(curveResult);
            }

            return results;
        }

        private static double GetMaxConcentrationAtMonitor(DispersionScenario scenario, MonitorPoint3D monitor)
        {
            double maxC = 0;
            foreach (var sample in monitor.TimeSeries)
            {
                if (sample.Concentration > maxC)
                    maxC = sample.Concentration;
            }
            return maxC;
        }

        /// <summary>
        /// Computes an exceedance curve from a single monitor's time series data.
        /// The probability is the fraction of samples that exceed each threshold.
        /// </summary>
        /// <param name="monitor">Monitor point with recorded time series.</param>
        /// <param name="thresholds">Array of concentration thresholds to evaluate.</param>
        /// <returns>Exceedance curve result for the monitor location.</returns>
        public static ExceedanceCurveResult ComputeFromTimeSeries(
            MonitorPoint3D monitor, double[] thresholds)
        {
            var result = new ExceedanceCurveResult { LocationName = monitor.Name };
            int totalSamples = monitor.TimeSeries.Count;

            if (totalSamples == 0)
            {
                foreach (var t in thresholds)
                    result.Points.Add(new ExceedancePoint { Threshold = t, Probability = 0 });
                return result;
            }

            foreach (double threshold in thresholds.OrderBy(t => t))
            {
                int exceedCount = monitor.TimeSeries.Count(s => s.Concentration >= threshold);
                result.Points.Add(new ExceedancePoint
                {
                    Threshold = threshold,
                    Probability = (double)exceedCount / totalSamples
                });
            }

            return result;
        }
    }
}
