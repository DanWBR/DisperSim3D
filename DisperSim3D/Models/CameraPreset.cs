using System;
using System.ComponentModel;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a saved camera viewpoint in the 3D viewport, storing position and orientation.
    /// </summary>
    public class CameraPreset
    {
        /// <summary>
        /// Gets or sets the unique identifier for this camera preset. Defaults to a new GUID.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the display name of this camera preset.
        /// </summary>
        public string Name { get; set; } = "Preset";

        /// <summary>
        /// Gets or sets the camera position in world coordinates.
        /// </summary>
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        [Editor("DisperSim3D.Controls.Point3DPropertyEditor, DisperSim3D.UI.Wpf",
            "HandyControl.Controls.PropertyEditorBase, HandyControl")]
        public Point3D Position { get; set; }

        /// <summary>
        /// Gets or sets the direction the camera is looking at, as a vector from the camera position.
        /// </summary>
        public Vector3D LookDirection { get; set; }

        /// <summary>
        /// Gets or sets the up direction vector for the camera orientation. Defaults to the positive Z axis.
        /// </summary>
        public Vector3D UpDirection { get; set; } = new Vector3D(0, 0, 1);
    }
}
