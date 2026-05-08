namespace DisperSim3D.Models
{
    /// <summary>
    /// Specifies the surface material type applied to 3D objects in the viewport.
    /// </summary>
    public enum MaterialType3D
    {
        /// <summary>
        /// A diffuse, non-reflective surface material.
        /// </summary>
        Matte,

        /// <summary>
        /// A reflective, metallic surface material with specular highlights.
        /// </summary>
        Metallic,

        /// <summary>
        /// A transparent glass-like material with refraction.
        /// </summary>
        Glass,

        /// <summary>
        /// A self-illuminating emissive material that appears to glow.
        /// </summary>
        Emissive
    }
}
