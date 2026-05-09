namespace DisperSim3D.Models
{
    /// <summary>
    /// Controls how much of an OpenFOAM case is embedded inside a self-contained .dsproj bundle.
    /// </summary>
    public enum BundleEmbedMode
    {
        /// <summary>
        /// Bundle only the files needed to re-render results: blockMeshDict and per-timestep
        /// scalar/vector fields. Keeps the .dsproj small but the case cannot be re-run from it
        /// without regeneration.
        /// </summary>
        ResultsOnly = 0,

        /// <summary>
        /// Bundle the entire OpenFOAM case tree (system/, constant/, 0/, all timesteps),
        /// excluding parallel-decomposed processor*/ folders. Allows direct re-run after
        /// extraction. Larger bundles.
        /// </summary>
        FullCase = 1
    }
}
