using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Stores the configuration parameters for a CFD (OpenFOAM) dispersion simulation.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class CfdConfiguration
    {
        /// <summary>Gets or sets the detected OpenFOAM runtime environment type.</summary>
        public OpenFoamEnvironmentType DetectedEnvironment { get; set; }

        /// <summary>Gets or sets the filesystem path to the OpenFOAM installation.</summary>
        public string OpenFoamPath { get; set; }

        /// <summary>Gets or sets the WSL2 distribution name used when the environment is <see cref="OpenFoamEnvironmentType.WSL2"/>.</summary>
        public string WslDistroName { get; set; }

        /// <summary>Gets or sets the Docker image name used when the environment is <see cref="OpenFoamEnvironmentType.Docker"/>.</summary>
        public string DockerImageName { get; set; }

        /// <summary>Gets or sets the BlueCFD installation path used when the environment is <see cref="OpenFoamEnvironmentType.BlueCFD"/>.</summary>
        public string BlueCfdPath { get; set; }

        /// <summary>Gets or sets the working directory where OpenFOAM case files are written.</summary>
        public string WorkingDirectory { get; set; }

        /// <summary>Gets or sets the molecular diffusivity in m^2/s used for scalar transport.</summary>
        public double DiffusivityM2PerS { get; set; }

        /// <summary>Gets or sets the number of parallel processors for domain decomposition.</summary>
        public int NumberOfProcessors { get; set; }

        /// <summary>Gets or sets the interval in seconds at which results are written to disk. A value of -1 uses the solver default.</summary>
        public double WriteIntervalS { get; set; }

        /// <summary>Gets or sets the number of grid cells per domain dimension.</summary>
        public int GridResolution { get; set; }

        /// <summary>Gets or sets the iterative solver convergence tolerance.</summary>
        public double SolverTolerance { get; set; }

        /// <summary>Gets or sets the convection numerical scheme name (e.g., "linearUpwind").</summary>
        public string NumericalScheme { get; set; }

        /// <summary>Gets or sets a value indicating whether the solver uses an adjustable time step based on the Courant number.</summary>
        public bool AdjustableTimeStep { get; set; }

        /// <summary>Gets or sets the maximum Courant number allowed when using adjustable time stepping.</summary>
        public double MaxCourantNumber { get; set; }

        /// <summary>Gets or sets the number of time-step directories retained on disk; 0 keeps all.</summary>
        public int PurgeWrite { get; set; }

        /// <summary>Gets or sets a value indicating whether the case directory is cleaned after the simulation completes.</summary>
        public bool CleanCaseOnCompletion { get; set; }

        /// <summary>Gets or sets a value indicating whether a Gaussian sub-grid refinement is applied around emission sources.</summary>
        public bool UseGaussianSubgrid { get; set; }

        /// <summary>Gets or sets the margin factor for the Gaussian sub-grid region around sources.</summary>
        public double SubgridMarginFactor { get; set; }

        /// <summary>Gets or sets a value indicating whether the 3D wind field is used as the advection velocity.</summary>
        public bool UseWindField { get; set; }

        // ─── Atmospheric Boundary Layer (Mack & Spruijt 2013, Vu 2019, Schalau 2021) ───

        [Category("Atmospheric")]
        [Description("Master switch. When true: log-law inlet (atmBoundaryLayerInletVelocity), z0-based ground wall functions (nutkAtmRoughWallFunction), HHTSL k-eps constants.")]
        public bool UseAtmosphericBL { get; set; }

        [Category("Atmospheric")]
        [Description("Turbulent Schmidt number Sc_t. Default 0.7. Use 0.3 for dense gas, 0.15 for cryogenic LNG (Vu 2019 §5.4).")]
        public double TurbulentSchmidtNumber { get; set; }

        [Category("Atmospheric")]
        [Description("Turbulent Prandtl number Pr_t. Default 0.85.")]
        public double TurbulentPrandtlNumber { get; set; }

        [Category("Atmospheric")]
        [Description("Buoyancy coefficient C_eps3 in the epsilon equation. Null = OpenFOAM default tanh formulation. Mack & Spruijt 2013 recommend -0.33 for heavy gas.")]
        public double? BuoyancyEpsCoefficient { get; set; }

        [Category("Atmospheric")]
        [Description("k-epsilon sigma_eps constant. Default 1.3 (OpenFOAM standard). Use 1.167 for horizontally homogeneous ABL (Vu 2019 §3.2.2).")]
        public double KEpsilonSigmaEpsilon { get; set; }

        [Category("Atmospheric")]
        [Description("Thermal boundary condition applied to the ground patch. Adiabatic for non-cryogenic; FixedTemperature for LNG.")]
        public GroundThermalBoundary GroundThermalBC { get; set; }

        [Category("Atmospheric")]
        [Description("Ground temperature (K) used when GroundThermalBC = FixedTemperature.")]
        public double GroundTemperatureK { get; set; }

        [Category("Atmospheric")]
        [Description("Ground heat flux (W/m^2, into the gas) used when GroundThermalBC = FixedFlux.")]
        public double GroundHeatFluxWPerM2 { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CfdConfiguration"/> class with default solver parameters.
        /// </summary>
        public CfdConfiguration()
        {
            DetectedEnvironment = OpenFoamEnvironmentType.None;
            OpenFoamPath = "";
            WslDistroName = "Ubuntu";
            DockerImageName = "openfoam/openfoam2312-default";
            WorkingDirectory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "DisperSim_OpenFOAM");
            DiffusivityM2PerS = 1e-5;
            NumberOfProcessors = System.Math.Max(1, System.Environment.ProcessorCount / 2);
            WriteIntervalS = -1;
            GridResolution = 40;
            SolverTolerance = 1e-8;
            NumericalScheme = "linearUpwind";
            AdjustableTimeStep = true;
            // Higher Courant numbers (up to 10) are stable for dispersion-only runs
            // (combustion off) and dramatically reduce wallclock time per the validation
            // study by Fiates & Vianna 2016 (Process Safety & Env. Protection 104:277).
            MaxCourantNumber = 10.0;
            PurgeWrite = 0;
            CleanCaseOnCompletion = true;
            UseGaussianSubgrid = true;
            SubgridMarginFactor = 1.5;
            UseWindField = true;

            // Atmospheric defaults — backward-compatible (off until preset turns it on)
            UseAtmosphericBL = false;
            TurbulentSchmidtNumber = 0.7;
            TurbulentPrandtlNumber = 0.85;
            BuoyancyEpsCoefficient = null;
            KEpsilonSigmaEpsilon = 1.3;
            GroundThermalBC = GroundThermalBoundary.Adiabatic;
            GroundTemperatureK = 293.15;
            GroundHeatFluxWPerM2 = 0;
        }
    }
}
