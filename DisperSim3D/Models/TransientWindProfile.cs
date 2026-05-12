using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a single entry in a transient wind profile, defining wind conditions at a specific time.
    /// </summary>
    public class WindProfileEntry
    {
        /// <summary>
        /// Gets or sets the time of this entry in seconds from the start of the simulation.
        /// </summary>
        public double TimeS { get; set; }

        /// <summary>
        /// Gets or sets the wind speed in meters per second. Default is 5.0 m/s.
        /// </summary>
        public double WindSpeed { get; set; } = 5.0;

        /// <summary>
        /// Gets or sets the wind direction in degrees (meteorological convention, 0 = North, 90 = East). Default is 270 degrees (from the west).
        /// </summary>
        public double WindDirectionDeg { get; set; } = 270.0;

        /// <summary>
        /// Gets or sets the Pasquill-Gifford atmospheric stability class. Default is class D (neutral).
        /// </summary>
        public PasquillStabilityClass StabilityClass { get; set; } = PasquillStabilityClass.D;

        /// <summary>
        /// Gets the 3D wind velocity vector computed from <see cref="WindSpeed"/> and <see cref="WindDirectionDeg"/>. The Z component is always zero.
        /// </summary>
        public Vector3D WindVector
        {
            get
            {
                var radians = WindDirectionDeg * Math.PI / 180.0;
                return new Vector3D(
                    WindSpeed * Math.Sin(radians),
                    WindSpeed * Math.Cos(radians),
                    0);
            }
        }
    }

    /// <summary>
    /// Defines a time-varying wind profile composed of multiple <see cref="WindProfileEntry"/> instances,
    /// supporting linear interpolation between entries for transient dispersion simulations.
    /// </summary>
    public class TransientWindProfile
    {
        /// <summary>
        /// Gets or sets the list of wind profile entries sorted by time.
        /// </summary>
        public List<WindProfileEntry> Entries { get; set; } = new List<WindProfileEntry>();

        /// <summary>
        /// Gets or sets a value indicating whether the transient wind profile is enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the Emergency Shutdown (ESD) activation time in seconds. A value of -1 indicates no ESD event.
        /// </summary>
        public double ESDTimeS { get; set; } = -1;

        /// <summary>
        /// Gets the interpolated wind profile entry at the specified simulation time.
        /// Linearly interpolates wind speed and direction between adjacent entries.
        /// Stability class is taken from the nearer entry by time.
        /// </summary>
        /// <param name="timeS">The simulation time in seconds.</param>
        /// <returns>
        /// An interpolated <see cref="WindProfileEntry"/> for the given time,
        /// or <c>null</c> if the profile contains no entries.
        /// </returns>
        public WindProfileEntry GetEntryAtTime(double timeS)
        {
            if (Entries.Count == 0) return null;
            if (Entries.Count == 1) return Entries[0];

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].TimeS <= timeS)
                {
                    if (i < Entries.Count - 1)
                    {
                        var a = Entries[i];
                        var b = Entries[i + 1];
                        double t = (timeS - a.TimeS) / (b.TimeS - a.TimeS);
                        t = Math.Max(0, Math.Min(1, t));
                        return new WindProfileEntry
                        {
                            TimeS = timeS,
                            WindSpeed = a.WindSpeed + t * (b.WindSpeed - a.WindSpeed),
                            WindDirectionDeg = a.WindDirectionDeg + t * (b.WindDirectionDeg - a.WindDirectionDeg),
                            StabilityClass = t < 0.5 ? a.StabilityClass : b.StabilityClass
                        };
                    }
                    return Entries[i];
                }
            }
            return Entries[0];
        }
    }
}
