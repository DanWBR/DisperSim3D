using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Specifies the geometry type of a monitor used for concentration sampling.
    /// </summary>
    public enum MonitorType
    {
        /// <summary>A single-point monitor.</summary>
        Point,
        /// <summary>A line monitor defined by a start and end position, sampled at discrete intervals.</summary>
        Line,
        /// <summary>A volumetric region monitor defined by a corner position and box dimensions.</summary>
        Region
    }

    /// <summary>
    /// Represents a 3D monitor that samples concentration data at a point, along a line, or within a volumetric region.
    /// </summary>
    public class MonitorPoint3D
    {
        /// <summary>Gets or sets the unique identifier for this monitor.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Gets or sets the display name of this monitor.</summary>
        public string Name { get; set; } = "Monitor1";

        /// <summary>Gets or sets the 3D position of this monitor (start point for line monitors, corner for region monitors).</summary>
        public Point3D Position { get; set; }

        /// <summary>Gets or sets a value indicating whether this monitor is visible in the 3D viewport.</summary>
        public bool Visible { get; set; } = true;

        /// <summary>Gets or sets the time-series collection of sampled concentration data.</summary>
        public List<MonitorSample> TimeSeries { get; set; } = new List<MonitorSample>();

        /// <summary>Gets or sets the geometry type of this monitor.</summary>
        public MonitorType Type { get; set; } = MonitorType.Point;

        /// <summary>Gets or sets the endpoint for line-type monitors. <see cref="Position"/> is the start point.</summary>
        public Point3D EndPosition { get; set; }

        /// <summary>Gets or sets the box dimensions for region-type monitors, measured from <see cref="Position"/>.</summary>
        public Vector3D RegionSize { get; set; } = new Vector3D(10, 10, 5);

        /// <summary>Gets or sets the number of equally spaced sample points along a line monitor.</summary>
        public int LineSampleCount { get; set; } = 20;

        /// <summary>Gets or sets the number of sub-grid divisions per axis for region monitors.</summary>
        public int RegionResolution { get; set; } = 5;

        /// <summary>Gets the most recent concentration value from the time series, or zero if no data exists.</summary>
        public double LastConcentration => TimeSeries.Count > 0 ? TimeSeries[TimeSeries.Count - 1].Concentration : 0.0;

        /// <summary>Gets or sets the minimum concentration observed in the last sampling step (for line/region monitors).</summary>
        public double LastMinConcentration { get; set; }

        /// <summary>Gets or sets the maximum concentration observed in the last sampling step (for line/region monitors).</summary>
        public double LastMaxConcentration { get; set; }

        /// <summary>Gets or sets the total gas volume computed in the last sampling step.</summary>
        public double LastGasVolume { get; set; }

        /// <summary>
        /// Generates equally spaced sample points along the line from <see cref="Position"/> to <see cref="EndPosition"/>.
        /// </summary>
        /// <returns>A list of interpolated 3D points along the line, or an empty list if <see cref="LineSampleCount"/> is less than 2.</returns>
        public List<Point3D> GetLineSamplePoints()
        {
            var pts = new List<Point3D>();
            if (LineSampleCount < 2) return pts;
            for (int i = 0; i < LineSampleCount; i++)
            {
                double t = (double)i / (LineSampleCount - 1);
                pts.Add(new Point3D(
                    Position.X + (EndPosition.X - Position.X) * t,
                    Position.Y + (EndPosition.Y - Position.Y) * t,
                    Position.Z + (EndPosition.Z - Position.Z) * t));
            }
            return pts;
        }

        /// <summary>
        /// Generates a uniform 3D grid of sample points within the region defined by <see cref="Position"/> and <see cref="RegionSize"/>.
        /// </summary>
        /// <returns>A list of 3D points forming a regular sub-grid within the region volume.</returns>
        public List<Point3D> GetRegionSamplePoints()
        {
            var pts = new List<Point3D>();
            int n = Math.Max(2, RegionResolution);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                    {
                        pts.Add(new Point3D(
                            Position.X + RegionSize.X * i / (n - 1),
                            Position.Y + RegionSize.Y * j / (n - 1),
                            Position.Z + RegionSize.Z * k / (n - 1)));
                    }
            return pts;
        }
    }

    /// <summary>
    /// Represents a single time-stamped concentration measurement from a monitor.
    /// </summary>
    public class MonitorSample
    {
        /// <summary>Gets or sets the simulation time in seconds at which this sample was taken.</summary>
        public double TimeS { get; set; }

        /// <summary>Gets or sets the concentration value at this sample time.</summary>
        public double Concentration { get; set; }
    }
}
