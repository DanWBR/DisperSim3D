using System;
using System.Collections.Generic;
using System.ComponentModel;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>What burns in an explosion.</summary>
    public enum ExplosionCloud
    {
        /// <summary>The cloud of a finished dispersion simulation as it stood at <see cref="ExplosionEvent.TimeS"/>.</summary>
        Dispersed = 0,
        /// <summary>A box of quiescent stoichiometric mixture of the dispersion simulation's gas,
        /// between <see cref="ExplosionEvent.BoxMin"/> and <see cref="ExplosionEvent.BoxMax"/>:
        /// the equivalent cloud of explosion risk studies.</summary>
        EquivalentBox = 1,
    }

    /// <summary>
    /// A region of the plant whose clutter the geometry does not show (pipes, cable trays,
    /// small equipment), given as a surface area per volume of pipes of one diameter: the
    /// representative congestion of explosion studies.
    /// </summary>
    public class CongestionRegion
    {
        public string Name { get; set; } = "Congestion";

        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        public Point3D Min { get; set; }

        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        public Point3D Max { get; set; }

        /// <summary>Diameter of the pipes that stand for the clutter, m.</summary>
        public double PipeDiameterM { get; set; } = 0.15;

        /// <summary>Surface area of the clutter per volume of the region, m²/m³.</summary>
        public double AreaPerVolume { get; set; } = 1.0;
    }

    /// <summary>
    /// A gas explosion: the cloud of a dispersion simulation, or an equivalent box of its gas,
    /// ignited at a point, burning through the plant and its congestion. Run on a solver
    /// extender that declares <c>SolverCapabilities.Explosion</c>; its results are the
    /// overpressure, its peak and impulse, the flame arrival time, the pressure at the
    /// scene's point monitors over time and the blast load on the plant's surfaces.
    /// </summary>
    public class ExplosionEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Explosion";

        /// <summary>Id of the dispersion <see cref="Simulation"/> whose cloud (or gas) burns.</summary>
        public string SimulationId { get; set; }

        public ExplosionCloud Cloud { get; set; } = ExplosionCloud.Dispersed;

        /// <summary>Where the cloud is ignited, m.</summary>
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        public Point3D Position { get; set; }

        /// <summary>Time of the dispersion simulation whose cloud burns, s (the nearest snapshot).</summary>
        public double TimeS { get; set; }

        /// <summary>Corners of the equivalent cloud, m (<see cref="ExplosionCloud.EquivalentBox"/>).</summary>
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        public Point3D BoxMin { get; set; }

        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        public Point3D BoxMax { get; set; }

        /// <summary>Cell size in the cloud, m; 0 lets the solver choose.</summary>
        public double CellSizeM { get; set; }

        /// <summary>Clutter the geometry leaves out.</summary>
        public List<CongestionRegion> Congestion { get; set; } = new List<CongestionRegion>();

        public bool IsVisible { get; set; } = true;
    }
}
