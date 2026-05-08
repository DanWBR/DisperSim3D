using System.Collections.Generic;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a single directional bin in a wind rose, holding wind speed, frequency, and stability data.
    /// </summary>
    public class WindRoseBin
    {
        /// <summary>
        /// Gets or sets the compass direction of this bin in degrees (0 = North, 90 = East).
        /// </summary>
        public double DirectionDeg { get; set; }

        /// <summary>
        /// Gets or sets the frequency of occurrence for this wind direction, as a percentage.
        /// </summary>
        public double Frequency { get; set; }

        /// <summary>
        /// Gets or sets the Pasquill atmospheric stability class for this bin. Defaults to class D (neutral).
        /// </summary>
        public PasquillStabilityClass StabilityClass { get; set; } = PasquillStabilityClass.D;

        /// <summary>
        /// Gets or sets the representative wind speed for this bin in meters per second. Defaults to 5.0 m/s.
        /// </summary>
        public double WindSpeed { get; set; } = 5.0;
    }

    /// <summary>
    /// Contains wind rose data composed of directional bins, used for atmospheric dispersion modeling.
    /// </summary>
    public class WindRoseData
    {
        /// <summary>
        /// Gets or sets the collection of directional wind bins that make up this wind rose.
        /// </summary>
        public List<WindRoseBin> Bins { get; set; } = new List<WindRoseBin>();

        /// <summary>
        /// Gets or sets a value indicating whether this wind rose should be rendered in the 3D viewport.
        /// </summary>
        public bool ShowIn3D { get; set; } = true;

        /// <summary>
        /// Creates a wind rose with 8 equally spaced compass directions (N, NE, E, SE, S, SW, W, NW), each at 12.5% frequency.
        /// </summary>
        /// <returns>A new <see cref="WindRoseData"/> instance with 8 uniform bins.</returns>
        public static WindRoseData Create8Directions()
        {
            var data = new WindRoseData();
            double[] dirs = { 0, 45, 90, 135, 180, 225, 270, 315 };
            foreach (double d in dirs)
                data.Bins.Add(new WindRoseBin { DirectionDeg = d, Frequency = 12.5 });
            return data;
        }

        /// <summary>
        /// Creates a wind rose with 16 equally spaced compass directions at 22.5-degree intervals, each at 6.25% frequency.
        /// </summary>
        /// <returns>A new <see cref="WindRoseData"/> instance with 16 uniform bins.</returns>
        public static WindRoseData Create16Directions()
        {
            var data = new WindRoseData();
            for (int i = 0; i < 16; i++)
                data.Bins.Add(new WindRoseBin { DirectionDeg = i * 22.5, Frequency = 6.25 });
            return data;
        }
    }
}
