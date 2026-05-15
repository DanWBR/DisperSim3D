using System;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a concentration threshold used to visualize hazard zones in a gas dispersion simulation.
    /// </summary>
    public class DispersionThreshold
    {
        /// <summary>
        /// Gets or sets the display name of this threshold.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the type of concentration threshold (e.g., LFL, IDLH, ERPG, or Custom).
        /// </summary>
        public DispersionThresholdType Type { get; set; }

        /// <summary>
        /// Gets or sets the concentration value for this threshold in kg/m^3.
        /// </summary>
        public double ConcentrationValue { get; set; }

        /// <summary>
        /// Gets or sets the color used to render the isosurface for this threshold.
        /// </summary>
        public Color Color { get; set; }

        /// <summary>
        /// Gets or sets the opacity of the rendered isosurface, ranging from 0.0 (fully transparent) to 1.0 (fully opaque).
        /// </summary>
        public double Opacity { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the threshold isosurface is visible in the 3D viewport.
        /// </summary>
        public bool Visible { get; set; }

        /// <summary>
        /// When true, renders as a realistic turbulent gas cloud instead of a solid isosurface.
        /// </summary>
        public bool UseCloudAppearance { get; set; }

        /// <summary>
        /// Tint colour for cloud-style rendering. Ignored when <see cref="UseCloudAppearance"/> is false.
        /// </summary>
        public Color CloudColor { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DispersionThreshold"/> class with default values.
        /// </summary>
        public DispersionThreshold()
        {
            Name = "Threshold";
            Type = DispersionThresholdType.Custom;
            ConcentrationValue = 0.01;
            Color = Colors.Red;
            Opacity = 0.3;
            Visible = true;
            UseCloudAppearance = false;
            CloudColor = Color.FromRgb(200, 200, 210);
        }
    }
}
