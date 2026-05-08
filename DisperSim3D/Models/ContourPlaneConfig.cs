namespace DisperSim3D.Models
{
    /// <summary>
    /// Specifies the axis-aligned plane orientation for a contour slice.
    /// </summary>
    public enum ContourAxis
    {
        /// <summary>
        /// Horizontal plane (constant Z), showing the XY cross-section.
        /// </summary>
        XY,

        /// <summary>
        /// Vertical plane (constant Y), showing the XZ cross-section.
        /// </summary>
        XZ,

        /// <summary>
        /// Vertical plane (constant X), showing the YZ cross-section.
        /// </summary>
        YZ
    }

    /// <summary>
    /// Configuration for a 2D contour plane that slices through the 3D concentration field.
    /// </summary>
    public class ContourPlaneConfig
    {
        /// <summary>
        /// Gets or sets the axis-aligned orientation of the contour plane. Default is <see cref="ContourAxis.XY"/>.
        /// </summary>
        public ContourAxis Axis { get; set; } = ContourAxis.XY;

        /// <summary>
        /// Gets or sets the position of the contour plane along the axis normal, in meters.
        /// </summary>
        public double Position { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the contour plane is visible. Default is <c>true</c>.
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Gets or sets the opacity of the contour plane, ranging from 0.0 (fully transparent) to 1.0 (fully opaque). Default is 0.8.
        /// </summary>
        public double Opacity { get; set; } = 0.8;

        /// <summary>
        /// Gets or sets the color map used to render concentration values on the contour plane. Default is Jet.
        /// </summary>
        public Core.ColorMapName ColorMap { get; set; } = Core.ColorMapName.Jet;
    }
}
