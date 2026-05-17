using System;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Common interface for the CPU (<see cref="BuoyantTracerEngine"/>) and
    /// GPU (<see cref="BuoyantTracerEngineGpu"/>) implementations of the
    /// buoyant scalar transport solver. FluidX3DRunner picks one based on
    /// <see cref="DisperSim3D.Models.CfdConfiguration.UseGpuBuoyantTracer"/>.
    /// </summary>
    public interface IBuoyantTracerEngine : IDisposable
    {
        double DxM { get; }
        double DyM { get; }
        double DzM { get; }

        void SetMassSource(double xSi, double ySi, double zSi,
            double radiusM, double releaseRateKgPerS, double airDensityKgPerM3,
            double exitTemperatureK);

        void SetPoolSource(double xSi, double ySi,
            double poolRadiusM, double releaseRateKgPerS, double airDensityKgPerM3,
            double exitTemperatureK);

        void SetSphericalSource(double xSi, double ySi, double zSi,
            double radiusM, double concentration, double exitTemperatureK);

        double EstimateBuoyantVelocity();

        double[,,] Step(double dtS);

        double[,,] SnapshotConcentration();

        double[,,] SnapshotTemperature();
    }
}
