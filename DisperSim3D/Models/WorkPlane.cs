using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a working plane at a specific elevation
    /// </summary>
    public class WorkPlane
    {
        /// <summary>
        /// Gets or sets the elevation (Z coordinate) of this work plane in world units.
        /// </summary>
        public double Elevation { get; set; }

        /// <summary>
        /// Gets or sets the display name of this work plane.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this work plane is visible in the 3D viewport.
        /// </summary>
        public bool Visible { get; set; }

        /// <summary>
        /// Gets or sets the color used to render the grid lines on this work plane.
        /// </summary>
        public Color GridColor { get; set; }

        /// <summary>
        /// Gets or sets the spacing between grid lines in world units. Defaults to 5.0.
        /// </summary>
        public double GridSpacing { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkPlane"/> class at the specified elevation.
        /// </summary>
        /// <param name="elevation">The Z elevation of the work plane in world units.</param>
        /// <param name="name">The display name for this work plane.</param>
        public WorkPlane(double elevation, string name)
        {
            Elevation = elevation;
            Name = name;
            Visible = true;
            GridColor = Colors.Gray;
            GridSpacing = 5.0;
        }

        /// <summary>
        /// Creates a work plane at ground level (elevation 0).
        /// </summary>
        /// <returns>A new <see cref="WorkPlane"/> at elevation 0 named "Ground Level".</returns>
        public static WorkPlane CreateGroundLevel()
        {
            return new WorkPlane(0, "Ground Level");
        }

        /// <summary>
        /// Creates a work plane at mezzanine level (elevation 5).
        /// </summary>
        /// <returns>A new <see cref="WorkPlane"/> at elevation 5 named "Mezzanine".</returns>
        public static WorkPlane CreateMezzanine()
        {
            return new WorkPlane(5, "Mezzanine");
        }

        /// <summary>
        /// Creates a work plane at the upper level (elevation 10).
        /// </summary>
        /// <returns>A new <see cref="WorkPlane"/> at elevation 10 named "Upper Level".</returns>
        public static WorkPlane CreateUpperLevel()
        {
            return new WorkPlane(10, "Upper Level");
        }
    }
}
