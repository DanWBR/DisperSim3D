namespace DisperSim3D.Validation
{
    public class BenchmarkSensor
    {
        public string Name { get; set; }
        /// <summary>Position [x, y, z] in metres relative to the source.</summary>
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };
        /// <summary>
        /// Observed concentration at this sensor in the unit declared by
        /// <see cref="BenchmarkSpec.Unit"/>. Field name kept generic for back-compat with
        /// kg/m³ benchmarks; the type is dimensionless to allow mole/mass fraction too.
        /// </summary>
        public double MeasuredKgM3 { get; set; }
    }
}
