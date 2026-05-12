using System;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Core;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a decorative 3D model placed in the scene, with support for transforms, materials, and clipping planes.
    /// </summary>
    public class Decoration3D
    {
        /// <summary>Gets or sets the unique identifier for this decoration.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the display name of this decoration.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the file path to the 3D model asset.</summary>
        public string FilePath { get; set; }

        /// <summary>Gets or sets the world-space position of this decoration.</summary>
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        [Editor(typeof(DisperSim3D.Controls.Point3DPropertyEditor),
            typeof(HandyControl.Controls.PropertyEditorBase))]
        public Point3D Position { get; set; }

        /// <summary>Gets or sets the Euler rotation angles (in degrees) around the X, Y, and Z axes.</summary>
        public Vector3D Rotation { get; set; }

        /// <summary>Gets or sets the uniform scale factor applied to the model.</summary>
        public double Scale { get; set; }

        /// <summary>Gets or sets the original unclipped 3D model geometry.</summary>
        public Model3DGroup OriginalModel3D { get; set; }

        /// <summary>Gets or sets the current (possibly clipped) 3D model geometry used for rendering.</summary>
        public Model3DGroup Model3D { get; set; }

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

        /// <summary>
        /// Initializes a new instance of the <see cref="Decoration3D"/> class with default values.
        /// </summary>
        public Decoration3D()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Decoration";
            FilePath = string.Empty;
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
        }

        /// <summary>
        /// Applies the current clipping configuration to the model. If clipping is disabled, restores the original model.
        /// </summary>
        public void ApplyClip()
        {
            if (OriginalModel3D == null) return;

            if (!ClipEnabled)
            {
                Model3D = OriginalModel3D;
                return;
            }

            Model3D = MeshClipper.ClipModel(OriginalModel3D, ClipAxis, ClipValue, ClipAbove);
        }

        /// <summary>
        /// Computes the composite world transform (scale, rotation, translation) for this decoration.
        /// </summary>
        /// <returns>A <see cref="Transform3D"/> representing the combined scale, rotation, and translation.</returns>
        public Transform3D GetWorldTransform()
        {
            var group = new Transform3DGroup();

            group.Children.Add(new ScaleTransform3D(Scale, Scale, Scale));

            if (Rotation.Z != 0)
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), Rotation.Z)));
            if (Rotation.Y != 0)
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(0, 1, 0), Rotation.Y)));
            if (Rotation.X != 0)
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(1, 0, 0), Rotation.X)));

            group.Children.Add(new TranslateTransform3D(Position.X, Position.Y, Position.Z));
            return group;
        }

        /// <summary>
        /// Recalculates the world-space bounding box from the current model geometry and transform.
        /// </summary>
        public void UpdateBoundingBox()
        {
            if (Model3D == null)
            {
                var size = 1.0 * Scale;
                BoundingBox = new BoundingBox(
                    new Point3D(Position.X - size / 2, Position.Y - size / 2, Position.Z - size / 2),
                    new Point3D(Position.X + size / 2, Position.Y + size / 2, Position.Z + size / 2));
            }
            else
            {
                var bounds = Model3D.Bounds;
                var transform = GetWorldTransform();
                var min = new Point3D(bounds.X, bounds.Y, bounds.Z);
                var max = new Point3D(bounds.X + bounds.SizeX, bounds.Y + bounds.SizeY, bounds.Z + bounds.SizeZ);
                BoundingBox = new BoundingBox(min, max).Transform(transform);
            }
        }
    }
}
