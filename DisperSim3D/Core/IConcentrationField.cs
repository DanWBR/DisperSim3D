namespace DisperSim3D.Core
{
    /// <summary>
    /// Defines a spatial concentration field that can be evaluated at any 3D point.
    /// </summary>
    public interface IConcentrationField
    {
        /// <summary>
        /// Evaluates the concentration at the specified 3D coordinates.
        /// </summary>
        /// <param name="x">The x-coordinate in meters.</param>
        /// <param name="y">The y-coordinate in meters.</param>
        /// <param name="z">The z-coordinate (height) in meters.</param>
        /// <returns>The concentration value at the given point.</returns>
        double EvaluateConcentration(double x, double y, double z);
    }
}
