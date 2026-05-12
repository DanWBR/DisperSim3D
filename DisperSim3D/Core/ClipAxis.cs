namespace DisperSim3D.Core
{
    /// <summary>
    /// Specifies the axis along which mesh clipping is performed. Lives in the
    /// portable engine because <see cref="DisperSim3D.Models.Decoration3D"/>
    /// stores a <c>ClipAxis</c> field as part of its persisted state, while the
    /// actual mesh clipping (which depends on WPF <c>MeshGeometry3D</c>) lives
    /// in <c>DisperSim3D.UI.Wpf</c>.
    /// </summary>
    public enum ClipAxis
    {
        /// <summary>Clip along the X axis.</summary>
        X,
        /// <summary>Clip along the Y axis.</summary>
        Y,
        /// <summary>Clip along the Z axis.</summary>
        Z
    }
}
