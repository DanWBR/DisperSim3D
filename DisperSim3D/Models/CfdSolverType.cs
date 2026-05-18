namespace DisperSim3D.Models
{
    /// <summary>
    /// Specifies the dispersion solver algorithm to use for a CFD simulation.
    /// </summary>
    public enum CfdSolverType
    {
        /// <summary>Analytical Gaussian puff model for fast approximate dispersion calculations.</summary>
        GaussianPuff,
        /// <summary>Steady-state Gaussian plume model for continuous releases in uniform wind.</summary>
        GaussianPlume,
        /// <summary>OpenFOAM rhoReactingBuoyantFoam with combustion disabled — compressible, multi-species,
        /// buoyant transport. Universal dispersion solver covering subsonic and sonic releases (Fiates &amp; Vianna 2016).
        /// In WSL builds the patched <c>rhoReactingBuoyantFoamSct</c> variant (Vu 2019 thesis) honouring an
        /// explicit turbulent Schmidt number is used automatically.</summary>
        RhoReactingBuoyantFoam,
        /// <summary>FluidX3D GPU Lattice-Boltzmann wind field — replaces simpleFoam for fast design-iteration wind runs.</summary>
        FluidX3DWind,
        /// <summary>FluidX3D GPU LBM transient dispersion with passive scalar tracer (T-field as concentration) and Boussinesq buoyancy.</summary>
        FluidX3DDispersion,
        /// <summary>FluidX3D wind field + CPU dual-tracer (temperature + smoke) with Boussinesq buoyancy — for jet/pool fire convective plume modelling. Radiation footprint is overlaid analytically via JetFireModel.</summary>
        FluidX3DFire,
        /// <summary>FluidX3D-driven CPU dispersion that runs until the concentration field stops changing (convergence detection). Writes one converged steady-state snapshot.</summary>
        FluidX3DDispersionSteady
    }

    /// <summary>
    /// Specifies the runtime environment used to execute OpenFOAM solvers.
    /// </summary>
    public enum OpenFoamEnvironmentType
    {
        /// <summary>No OpenFOAM environment detected.</summary>
        None,
        /// <summary>OpenFOAM running inside a WSL2 Linux distribution.</summary>
        WSL2,
        /// <summary>OpenFOAM running inside a Docker container.</summary>
        Docker,
        /// <summary>BlueCFD native Windows port of OpenFOAM.</summary>
        BlueCFD,
        /// <summary>Native Windows build of OpenFOAM.</summary>
        NativeWindows
    }
}
