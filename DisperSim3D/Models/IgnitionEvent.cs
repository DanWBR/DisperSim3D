using System;
using System.ComponentModel;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Ignition of a dispersion result: a point and an instant. Turns a concentration
    /// snapshot into a flash fire — the part of the cloud connected to the ignition
    /// point burns back at a constant flame speed, and what comes out is the hazard
    /// envelope plus the time the flame reaches every cell.
    ///
    /// This is what ties dispersion to fire. A <see cref="FireSource"/> is placed and
    /// parameterised by hand; an <see cref="IgnitionEvent"/> is a consequence of a
    /// simulation the engine already ran.
    /// </summary>
    public class IgnitionEvent
    {
        /// <summary>Unique identifier.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Display name shown in the project tree.</summary>
        public string Name { get; set; } = "Ignition";

        /// <summary>Id of the dispersion <see cref="Simulation"/> whose concentration
        /// field is ignited. A View renders the flash fire of the ignition attached to
        /// its own simulation.</summary>
        public string SimulationId { get; set; }

        /// <summary>Where the cloud is ignited, in scene coordinates (m).</summary>
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        public Point3D Position { get; set; }

        /// <summary>Simulated time of the ignition (s). The snapshot closest to this
        /// time is the one that burns — a cloud ignited at 30 s is not the cloud at
        /// 300 s.</summary>
        public double TimeS { get; set; }

        /// <summary>
        /// Fraction of the LFL that bounds the hazard envelope. Dispersion models give
        /// time-averaged concentrations, and turbulent fluctuation puts the momentary
        /// flammable cloud outside the averaged LFL contour, so consequence practice
        /// (TNO, CCPS) draws the flash-fire envelope at half the LFL. Default 0.5.
        /// </summary>
        public double EnvelopeFraction { get; set; } = 0.5;

        /// <summary>
        /// Flame speed through the cloud (m/s), used to turn burn-back distance into
        /// arrival time and from there the exposure duration. Default 10 m/s, in the
        /// usual range for an unconfined methane deflagration.
        /// </summary>
        public double FlameSpeedMS { get; set; } = 10.0;

        /// <summary>Whether the ignition marker is drawn in the 3D viewport.</summary>
        public bool IsVisible { get; set; } = true;
    }
}
