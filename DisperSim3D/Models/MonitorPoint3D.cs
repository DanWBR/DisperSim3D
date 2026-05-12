using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Category("Identity")]
        [Description("Display name shown in the project tree and result tables.")]
        public string Name { get; set; } = "Monitor1";

        [Category("Position")]
        [Description("3D position (start point for line monitors, corner for region monitors).")]
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        [Editor(typeof(DisperSim3D.Controls.Point3DPropertyEditor),
            typeof(HandyControl.Controls.PropertyEditorBase))]
        public Point3D Position { get; set; }

        [Category("Display")]
        [Description("Whether the monitor marker is shown in the 3D viewport.")]
        public bool Visible { get; set; } = true;

        [Category("Data")]
        [Description("Quantity the monitor records in its time series. Units: %LFL, %UFL, ppm, ppb, K, mole/mass fraction, kW/m².")]
        public ViewFieldProperty MeasuredQuantity { get; set; } = ViewFieldProperty.ConcentrationKgM3;

        [Category("Data")]
        [Description("Concentration time-series collected during the run.")]
        public List<MonitorSample> TimeSeries { get; set; } = new List<MonitorSample>();

        [Category("Geometry")]
        [Description("Monitor shape: Point, Line, or Region.")]
        public MonitorType Type { get; set; } = MonitorType.Point;

        [Category("Geometry")]
        [Description("End position (used only by Line monitors).")]
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        [Editor(typeof(DisperSim3D.Controls.Point3DPropertyEditor),
            typeof(HandyControl.Controls.PropertyEditorBase))]
        public Point3D EndPosition { get; set; }

        [Category("Geometry")]
        [Description("Box dimensions extending from Position (used only by Region monitors).")]
        public Vector3D RegionSize { get; set; } = new Vector3D(10, 10, 5);

        [Category("Geometry")]
        [Description("Number of equally spaced sample points along the line.")]
        public int LineSampleCount { get; set; } = 20;

        [Category("Geometry")]
        [Description("Number of sub-grid divisions per axis for region monitors.")]
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
