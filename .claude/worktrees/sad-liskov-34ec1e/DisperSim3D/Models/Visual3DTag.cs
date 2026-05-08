namespace DisperSim3D.Models
{
    /// <summary>
    /// Tag stored on ModelVisual3D to link viewport visuals back to model objects.
    /// </summary>
    public class Visual3DTag
    {
        /// <summary>
        /// Gets or sets the category of the tagged object (e.g., "Source", "Building", "Terrain").
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier of the model object this visual represents.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Visual3DTag"/> class.
        /// </summary>
        /// <param name="category">The category of the tagged object.</param>
        /// <param name="id">The unique identifier of the model object.</param>
        public Visual3DTag(string category, string id)
        {
            Category = category;
            Id = id;
        }
    }
}
