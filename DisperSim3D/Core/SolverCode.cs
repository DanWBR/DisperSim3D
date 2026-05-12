using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Stable 6-character codes for each <see cref="CfdSolverType"/>. The codes are
    /// shown as a compact prefix in the project tree, the simulation manager grid,
    /// CFD result entries, and log messages so the user can tell at a glance which
    /// solver produced a given simulation without reading the full enum name.
    ///
    /// Codes are FIXED — never rename them, since they appear in transcripts and
    /// (eventually) bundle metadata. New solvers get a new 6-char tag picked from
    /// uppercase letters; collisions must be avoided across the enum.
    /// </summary>
    public static class SolverCode
    {
        /// <summary>Six-character upper-case tag for the given solver.</summary>
        public static string Of(CfdSolverType type)
        {
            switch (type)
            {
                case CfdSolverType.GaussianPuff:              return "GAUPUF";
                case CfdSolverType.GaussianPlume:             return "GAUPLM";
                case CfdSolverType.ScalarTransportFoam:       return "SCATRF";
                case CfdSolverType.ScalarTransportFoamSteady: return "SCATRS";
                case CfdSolverType.ScalarSimpleFoam:          return "SCASMF";
                case CfdSolverType.PimpleFoam:                return "PIMPLF";
                case CfdSolverType.BuoyantPimpleFoam:         return "BUOPIM";
                case CfdSolverType.ReactingFoam:              return "REACTF";
                case CfdSolverType.RhoSimpleFoam:             return "RHOSMF";
                case CfdSolverType.RhoReactingBuoyantFoam:    return "RHRBUF";
                case CfdSolverType.FluidX3DWind:              return "FX3DWN";
                case CfdSolverType.FluidX3DDispersion:        return "FX3DDP";
                case CfdSolverType.FluidX3DFire:              return "FX3DFR";
                case CfdSolverType.FluidX3DDispersionSteady:  return "FX3DDS";
                default:                                       return "UNKSLV";
            }
        }

        /// <summary>Human-friendly long-form name (matches the editor dialog labels).</summary>
        public static string DisplayName(CfdSolverType type)
        {
            switch (type)
            {
                case CfdSolverType.GaussianPuff:              return "Gaussian Puff (Transient)";
                case CfdSolverType.GaussianPlume:             return "Gaussian Plume (Steady)";
                case CfdSolverType.ScalarTransportFoam:       return "scalarTransportFoam (Transient)";
                case CfdSolverType.ScalarTransportFoamSteady: return "scalarTransportFoam (Steady)";
                case CfdSolverType.ScalarSimpleFoam:          return "simpleFoam + scalar (Steady)";
                case CfdSolverType.PimpleFoam:                return "pimpleFoam (Transient)";
                case CfdSolverType.BuoyantPimpleFoam:         return "buoyantPimpleFoam (Transient)";
                case CfdSolverType.ReactingFoam:              return "reactingFoam (Transient)";
                case CfdSolverType.RhoSimpleFoam:             return "rhoSimpleFoam (Steady)";
                case CfdSolverType.RhoReactingBuoyantFoam:    return "rhoReactingBuoyantFoam (Transient)";
                case CfdSolverType.FluidX3DWind:              return "FluidX3D Wind (GPU LBM)";
                case CfdSolverType.FluidX3DDispersion:        return "FluidX3D Dispersion (GPU LBM)";
                case CfdSolverType.FluidX3DFire:              return "FluidX3D Fire (Hot Buoyant Plume)";
                case CfdSolverType.FluidX3DDispersionSteady:  return "FluidX3D Dispersion (Steady State)";
                default:                                       return type.ToString();
            }
        }
    }
}
