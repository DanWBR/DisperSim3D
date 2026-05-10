namespace DisperSim3D.Models
{
    /// <summary>
    /// Thermal boundary condition applied to the ground patch in OpenFOAM cases that solve
    /// for temperature. Critical for cryogenic releases (LNG): an adiabatic ground
    /// under-predicts cloud size because real LNG vapour gains substantial heat from the
    /// surface (Vu 2019 §5.4).
    /// </summary>
    public enum GroundThermalBoundary
    {
        /// <summary>Zero heat flux (zeroGradient on T). Default for non-cryogenic dispersion.</summary>
        Adiabatic = 0,

        /// <summary>Fixed ground temperature (fixedValue on T). Recommended for LNG/cryogenic.</summary>
        FixedTemperature = 1,

        /// <summary>Fixed heat flux (fixedGradient on T). Use when an experimental q" is known.</summary>
        FixedFlux = 2
    }
}
