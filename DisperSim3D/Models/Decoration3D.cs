using System;
using System.ComponentModel;
using DisperSim3D.Core;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a decorative 3D model placed in the scene, with support for
    /// transforms, materials, and clipping planes. The engine holds the data;
    /// WPF-coupled operations (clip, world transform, bounding-box recompute)
    /// live as extension methods in <c>DisperSim3D.UI.Wpf</c> next to the
    /// renderers that consume them.
    /// </summary>
    public class Decoration3D
    {
        /// <summary>Gets or sets the unique identifier for this decoration.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the display name of this decoration.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the file path to the 3D model asset.</summary>
        public string FilePath { get; set; }

        /// <summary>Gets or sets an optional texture image path (PNG/JPG) applied
        /// to the model, overriding any MTL-embedded textures. Only effective for
        /// OBJ models that have UV coordinates.</summary>
        public string TexturePath { get; set; }

        /// <summary>Gets or sets the world-space position of this decoration.</summary>
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        [Editor("DisperSim3D.Controls.Point3DPropertyEditor, DisperSim3D.UI.Wpf",
            "HandyControl.Controls.PropertyEditorBase, HandyControl")]
        public Point3D Position { get; set; }

        /// <summary>Gets or sets the Euler rotation angles (in degrees) around the X, Y, and Z axes.</summary>
        public Vector3D Rotation { get; set; }

        /// <summary>Gets or sets the uniform scale factor applied to the model.</summary>
        public double Scale { get; set; }

        /// <summary>Original unclipped 3D model geometry. Typed as <c>object</c>
        /// so the engine assembly doesn't depend on <c>System.Windows.Media.Media3D</c>;
        /// the UI layer stores a <c>Model3DGroup</c> here and casts on read.</summary>
        public object OriginalModel3D { get; set; }

        /// <summary>Current (possibly clipped) 3D model geometry used for rendering.
        /// Typed as <c>object</c> for the same reason as <see cref="OriginalModel3D"/>.</summary>
        public object Model3D { get; set; }

        /// <summary>Gets or sets the axis-aligned bounding box of this decoration in world space.</summary>
        public BoundingBox BoundingBox { get; set; }

        /// <summary>Gets or sets the material shading type for this decoration.</summary>
        public MaterialType3D MaterialType { get; set; }

        /// <summary>Gets or sets the material color applied to this decoration.</summary>
        public Color MaterialColor { get; set; }

        /// <summary>Gets or sets the specular highlight power for shiny materials.</summary>
        public double SpecularPower { get; set; }

        /// <summary>Gets or sets the opacity of this decoration, from 0 (transparent) to 1 (opaque).</summary>
        public double Opacity { get; set; }

        /// <summary>Gets or sets a value indicating whether a custom material overrides the model's original materials.</summary>
        public bool UseCustomMaterial { get; set; }

        /// <summary>Gets or sets a value indicating whether clipping is enabled for this decoration.</summary>
        public bool ClipEnabled { get; set; }

        /// <summary>Gets or sets the axis along which the clipping plane is defined.</summary>
        public ClipAxis ClipAxis { get; set; }

        /// <summary>Gets or sets the position of the clipping plane along the chosen axis.</summary>
        public double ClipValue { get; set; }

        /// <summary>Gets or sets a value indicating whether geometry above (true) or below (false) the clip plane is retained.</summary>
        public bool ClipAbove { get; set; }

        /// <summary>Gets or sets a value indicating whether this decoration is visible in the 3D viewport.</summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Decoration3D"/> class with default values.
        /// </summary>
        public Decoration3D()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Decoration";
            FilePath = string.Empty;
            TexturePath = string.Empty;
            Scale = 1.0;
            MaterialType = MaterialType3D.Matte;
            MaterialColor = Colors.LightGray;
            SpecularPower = 40;
            Opacity = 1.0;
            UseCustomMaterial = false;
            ClipEnabled = false;
            ClipAxis = ClipAxis.Y;
            ClipValue = 0;
            ClipAbove = true;
            IsVisible = true;
        }

    }
}
