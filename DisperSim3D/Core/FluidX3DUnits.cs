using System;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Conversion helpers between SI units (m, s, m/s, m²/s) and FluidX3D's lattice units.
    /// LBM works in dimensionless units where Δx_lat = Δt_lat = 1 and the speed of sound is
    /// c_s = 1/√3. Physical quantities are mapped via the standard non-dimensionalisation:
    ///   Δx_si        = (2·half_m) / Nx                         [m / cell]
    ///   u_ref_lat    = chosen so that u_inlet_lat ≪ 0.1 c_s    [stability]
    ///   Δt_si        = Δx_si · (u_ref_lat / u_inlet_si)        [s / step]
    ///   nu_lat       = nu_si · Δt_si / Δx_si²                  [diffusivity scale]
    ///   g_lat        = g_si  · Δt_si² / Δx_si                  [acceleration scale]
    /// </summary>
    public class FluidX3DUnits
    {
        public double DomainHalfM { get; }     // SI half-extent of horizontal domain
        public double DomainHeightM { get; }   // SI vertical extent
        public int Nx { get; }
        public int Ny { get; }
        public int Nz { get; }

        public double DxSi { get; }            // metres per lattice cell
        public double DtSi { get; }            // seconds per lattice step
        public double InletULat { get; }       // target inlet velocity in lattice units (≪0.1)
        public double InletUSi { get; }        // physical inlet wind speed (m/s)

        public FluidX3DUnits(double domainHalfM, double domainHeightM,
            int nx, int ny, int nz, double inletUSi, double inletULat = 0.05)
        {
            if (nx <= 0 || ny <= 0 || nz <= 0)
                throw new ArgumentException("Grid resolution must be positive");
            if (inletUSi <= 0)
                throw new ArgumentException("Inlet wind speed must be positive");

            DomainHalfM = domainHalfM;
            DomainHeightM = domainHeightM;
            Nx = nx; Ny = ny; Nz = nz;
            InletUSi = inletUSi;
            // Conservative default: 0.02 lattice u/step ≈ Mach 0.035 over c_s. Anything
            // above 0.05 starts pushing the BGK collision toward its compressibility error;
            // with obstacle wakes adding local fluctuations, 0.05 was hitting the safety
            // clamp at ±c_s.
            InletULat = Math.Min(0.05, Math.Max(0.005, inletULat));

            DxSi = (2.0 * domainHalfM) / nx;
            DtSi = DxSi * InletULat / inletUSi;
        }

        /// <summary>Maps SI kinematic viscosity (m²/s) to lattice units.</summary>
        public float NuLattice(double nuSi) => (float)(nuSi * DtSi / (DxSi * DxSi));

        /// <summary>Maps SI scalar diffusivity (m²/s) to lattice α used by the TEMPERATURE extension.</summary>
        public float AlphaLattice(double alphaSi) => (float)(alphaSi * DtSi / (DxSi * DxSi));

        /// <summary>Maps SI gravity component (m/s²) to lattice force per unit mass.</summary>
        public float GLattice(double gSi) => (float)(gSi * DtSi * DtSi / DxSi);

        /// <summary>Maps SI velocity (m/s) to lattice velocity.</summary>
        public float ULattice(double uSi) => (float)(uSi * DtSi / DxSi);

        /// <summary>Maps lattice velocity back to SI (m/s).</summary>
        public double USi(float uLat) => uLat * DxSi / DtSi;

        /// <summary>
        /// Convert an SI x/y position centred on origin (range [-half_m, +half_m]) plus a
        /// z height (range [0, height_m]) to lattice cell coordinates. Clamps to valid range.
        /// </summary>
        public (uint x, uint y, uint z) SiToLattice(double xSi, double ySi, double zSi)
        {
            int x = (int)Math.Round((xSi + DomainHalfM) / DxSi);
            int y = (int)Math.Round((ySi + DomainHalfM) / DxSi);
            // Z uses its own scale derived from height: cells span [0, Nz-1] across [0, height_m]
            double dz = DomainHeightM / Nz;
            int z = (int)Math.Round(zSi / dz);
            x = Math.Max(0, Math.Min(Nx - 1, x));
            y = Math.Max(0, Math.Min(Ny - 1, y));
            z = Math.Max(0, Math.Min(Nz - 1, z));
            return ((uint)x, (uint)y, (uint)z);
        }

        /// <summary>Inverse of <see cref="SiToLattice"/>: lattice cell index → SI position (cell centre).</summary>
        public (double xSi, double ySi, double zSi) LatticeToSi(uint xCell, uint yCell, uint zCell)
        {
            double dz = DomainHeightM / Nz;
            double xSi = (xCell + 0.5) * DxSi - DomainHalfM;
            double ySi = (yCell + 0.5) * DxSi - DomainHalfM;
            double zSi = (zCell + 0.5) * dz;
            return (xSi, ySi, zSi);
        }

        /// <summary>Wall-clock steps for a desired physical duration.</summary>
        public uint StepsForSeconds(double durationSi)
            => (uint)Math.Max(1, Math.Round(durationSi / DtSi));
    }
}
