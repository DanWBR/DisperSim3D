namespace DisperSim3D.Models
{
    /// <summary>
    /// Camera viewing modes for the 3D viewport.
    /// </summary>
    public enum CameraMode
    {
        /// <summary>
        /// Top-down 2D view from directly above the scene.
        /// </summary>
        TopDown,

        /// <summary>
        /// 45-degree isometric projection view.
        /// </summary>
        Isometric,

        /// <summary>
        /// Free 3D perspective projection view.
        /// </summary>
        Perspective,

        /// <summary>
        /// Front elevation view.
        /// </summary>
        Front,

        /// <summary>
        /// Side elevation view.
        /// </summary>
        Side,

        /// <summary>
        /// User-controlled free camera mode.
        /// </summary>
        Free
    }

    /// <summary>
    /// Editor interaction modes that determine how user input is handled in the 3D viewport.
    /// </summary>
    public enum EditMode
    {
        /// <summary>
        /// Select and move objects in the scene.
        /// </summary>
        Select,

        /// <summary>
        /// Measure distances between points in the scene.
        /// </summary>
        Measure,

        /// <summary>
        /// View-only mode with no editing capabilities.
        /// </summary>
        View,

        /// <summary>
        /// Place imported 3D geometry as a decoration in the scene.
        /// </summary>
        PlaceDecoration,

        /// <summary>
        /// Place a gas release source in the scene.
        /// </summary>
        PlaceReleaseSource,

        /// <summary>
        /// Place a concentration monitor probe in the scene.
        /// </summary>
        PlaceMonitorPoint,

        /// <summary>
        /// Place a fire source in the scene.
        /// </summary>
        PlaceFireSource,

        /// <summary>
        /// Place a gas detector in the scene.
        /// </summary>
        PlaceGasDetector
    }
}
