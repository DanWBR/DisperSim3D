using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Replays a finished simulation through the monitors, from the fields it stored
    /// (<see cref="StoredFields.Measure"/>): a point monitor records the value at its
    /// position; a line or region monitor records, per time, the hazardous extreme over
    /// its sample points — the maximum, or the minimum for oxygen and visibility — and
    /// keeps the last time's minimum and maximum.
    /// </summary>
    public static class MonitorEvaluator
    {
        public static void EvaluateSimulation(Scene3D scene, Simulation sim, IEnumerable<MonitorPoint3D> monitors)
        {
            if (monitors == null) return;
            var gas = FireStudyEngine.ResolveGas(sim, scene);
            foreach (var m in monitors)
            {
                if (m == null) continue;
                m.TimeSeries.Clear();
                List<Point3D> points = m.Type == MonitorType.Line ? m.GetLineSamplePoints()
                    : m.Type == MonitorType.Region ? m.GetRegionSamplePoints()
                    : new List<Point3D> { m.Position };
                bool low = StoredFields.LowIsHazard(m.MeasuredQuantity);

                var perPoint = new List<List<MonitorSample>>();
                foreach (var p in points)
                    perPoint.Add(StoredFields.Measure(scene, sim, m.MeasuredQuantity, p.X, p.Y, p.Z, gas));
                if (perPoint.Count == 0 || perPoint[0].Count == 0) continue;

                int times = perPoint[0].Count;
                double lastMin = 0, lastMax = 0;
                for (int t = 0; t < times; t++)
                {
                    double min = double.MaxValue, max = double.MinValue;
                    foreach (var series in perPoint)
                    {
                        if (t >= series.Count) continue;
                        min = Math.Min(min, series[t].Concentration);
                        max = Math.Max(max, series[t].Concentration);
                    }
                    m.TimeSeries.Add(new MonitorSample { TimeS = perPoint[0][t].TimeS, Concentration = low ? min : max });
                    lastMin = min;
                    lastMax = max;
                }
                m.LastMinConcentration = lastMin;
                m.LastMaxConcentration = lastMax;
            }
        }
    }
}
