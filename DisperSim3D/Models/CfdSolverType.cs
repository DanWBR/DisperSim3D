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
        /// <summary>OpenFOAM scalarTransportFoam solver for full 3D transient scalar transport on a mesh.</summary>
        ScalarTransportFoam,
        /// <summary>OpenFOAM steady-state scalar transport using pseudo-transient iteration to convergence.</summary>
        ScalarTransportFoamSteady,
        /// <summary>OpenFOAM scalarSimpleFoam solver for SIMPLE-based steady-state scalar transport.</summary>
        ScalarSimpleFoam,
        /// <summary>OpenFOAM pimpleFoam transient solver with passive scalar transport via fvOptions.</summary>
        PimpleFoam,
        /// <summary>OpenFOAM buoyantPimpleFoam transient solver with buoyancy and passive scalar transport.</summary>
        BuoyantPimpleFoam,
        /// <summary>OpenFOAM reactingFoam compressible multi-species transient solver.</summary>
        ReactingFoam,
        /// <summary>OpenFOAM rhoSimpleFoam compressible steady-state RANS solver with passive scalar.</summary>
        RhoSimpleFoam
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
