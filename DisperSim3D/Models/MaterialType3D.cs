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
        Emissive,

        /// <summary>Rusted steel — orange-brown patches on a dark gunmetal base.</summary>
        RustedMetal,

        /// <summary>Galvanised steel — irregular spangle pattern on a cool grey base.</summary>
        GalvanizedMetal,

        /// <summary>Brushed stainless — horizontal scratch pattern with high specular.</summary>
        BrushedMetal,

        /// <summary>Painted steel with weathering streaks (rivets, chips).</summary>
        PaintedMetal,

        /// <summary>Industrial concrete (slab joints + aggregate noise).</summary>
        Concrete
    }
}
