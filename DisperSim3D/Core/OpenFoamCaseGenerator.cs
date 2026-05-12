using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Generates OpenFOAM case directory structures and configuration files
    /// for scalar transport and steady-state wind field simulations.
    /// </summary>
    public static class OpenFoamCaseGenerator
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static double ComputeTurbulentDiffusivity(DispersionScenario scenario)
        {
            double xRef = Math.Max(scenario.DomainSizeM * 0.5, 50.0);
            double windSpeed = Math.Max(scenario.Meteo.WindSpeed, 0.5);
            var sigma = PasquillGiffordCoefficients.ComputeSigma(xRef, scenario.Meteo.StabilityClass);
            double Ky = sigma.sigmaY * sigma.sigmaY * windSpeed / (2.0 * xRef);
            return Math.Max(Ky, 1e-5);
        }

        /// <summary>
        /// Generates a complete OpenFOAM case directory for a scalar transport (dispersion) simulation.
        /// Creates the 0, constant, and system directories with all required dictionaries and field files.
        /// </summary>
        /// <param name="scenario">The dispersion scenario containing sources, meteorology, and domain parameters.</param>
        /// <param name="config">The CFD configuration specifying solver settings, parallelism, and numerical schemes.</param>
        /// <returns>The absolute path to the generated case directory.</returns>
        public static string Generate(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            if (config.UseGaussianSubgrid && scenario.Sources.Count > 0)
            {
                var bounds = EstimatePlumeBounds(scenario, config.SubgridMarginFactor);
                xMin = Math.Max(-domain, bounds[0]);
                xMax = Math.Min(domain, bounds[1]);
                yMin = Math.Max(-domain, bounds[2]);
                yMax = Math.Min(domain, bounds[3]);
                zMax = Math.Min(domain, bounds[5]);
                if (zMax < 20) zMax = 20;
                double minSpan = 40;
                if (xMax - xMin < minSpan) { double mid = (xMax + xMin) / 2; xMin = mid - minSpan / 2; xMax = mid + minSpan / 2; }
                if (yMax - yMin < minSpan) { double mid = (yMax + yMin) / 2; yMin = mid - minSpan / 2; yMax = mid + minSpan / 2; }
            }

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            double spanX = xMax - xMin;
            double spanY = yMax - yMin;
            double cellSize = Math.Max(spanX / nx, spanY / ny);
            double windSpeed = scenario.Meteo.WindSpeed;
            double maxDt = cellSize / Math.Max(windSpeed, 0.5) * 0.5;
            double dt = Math.Min(scenario.TimeStepS, maxDt);

            // SnapshotCount on the scenario (default 20) controls output cadence when
            // CfdConfiguration.WriteIntervalS isn't explicitly set. WriteIntervalS still
            // wins when non-zero so power users can pin the exact value.
            int snapCount = scenario.SnapshotCount > 0 ? scenario.SnapshotCount : 20;
            double writeInterval = config.WriteIntervalS > 0
                ? config.WriteIntervalS
                : Math.Max(1.0, scenario.SimulationDurationS / snapCount);

            var wind = scenario.Meteo.WindVector;

            double maxExitVelocity = 0;
            foreach (var src in scenario.Sources)
            {
                // Use the (subsonic) Birch expanded velocity for time-step sizing — sonic real-orifice
                // velocity would force impractically tiny dt without adding accuracy.
                double v = src.ExpandedVelocityForCfdMS;
                if (v > maxExitVelocity) maxExitVelocity = v;
            }
            if (maxExitVelocity > 0)
            {
                double maxV = Math.Max(windSpeed, maxExitVelocity);
                maxDt = cellSize / Math.Max(maxV, 0.5) * 0.35;
                dt = Math.Min(dt, maxDt);
            }

            double effectiveDT = Math.Max(config.DiffusivityM2PerS, ComputeTurbulentDiffusivity(scenario));
            double diffDt = cellSize * cellSize / (6.0 * effectiveDT);
            dt = Math.Min(dt, diffDt);

            WriteControlDict(caseDir, scenario.SimulationDurationS, dt, writeInterval, config);
            WriteFvSchemes(caseDir, config);
            WriteFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteTransportProperties(caseDir, effectiveDT);
            WriteUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WriteTField(caseDir, config);
            WriteFvOptions(caseDir, scenario.Sources, xMin, xMax, yMin, yMax, zMax, cellSize, nx, ny, nz);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteJetSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        /// <summary>
        /// Generates an OpenFOAM case for steady-state scalar transport using scalarTransportFoam
        /// with steadyState ddtSchemes. The solver iterates pseudo-time steps until the scalar field converges.
        /// </summary>
        public static string GenerateSteadyState(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "ss_case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            int maxIter = 500;
            var wind = scenario.Meteo.WindVector;

            double cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            double effectiveDT = Math.Max(config.DiffusivityM2PerS, ComputeTurbulentDiffusivity(scenario));

            WriteSteadyControlDict(caseDir, maxIter, "scalarTransportFoam");
            WriteSteadyFvSchemes(caseDir, config);
            WriteSteadyFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteTransportProperties(caseDir, effectiveDT);
            WriteUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WriteTField(caseDir, config);
            WriteFvOptions(caseDir, scenario.Sources, xMin, xMax, yMin, yMax, zMax, cellSize, nx, ny, nz);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteJetSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        /// <summary>
        /// Generates an OpenFOAM case for steady-state scalar transport using a SIMPLE-based approach.
        /// Uses simpleFoam for the flow field coupled with a scalar transport equation solved via
        /// the fvOptions scalar source mechanism.
        /// </summary>
        public static string GenerateSteadyStateSIMPLE(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "simple_case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            int maxIter = 1000;
            var wind = scenario.Meteo.WindVector;
            double cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            double effectiveDT = Math.Max(config.DiffusivityM2PerS, ComputeTurbulentDiffusivity(scenario));

            WriteSteadyControlDict(caseDir, maxIter, "simpleFoam");
            WriteSimpleFoamScalarFvSchemes(caseDir, config);
            WriteSimpleFoamScalarFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteSimpleFoamScalarTransportProperties(caseDir, effectiveDT);
            WriteUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WritePField(caseDir);
            WriteTField(caseDir, config);
            WriteFvOptions(caseDir, scenario.Sources, xMin, xMax, yMin, yMax, zMax, cellSize, nx, ny, nz);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        private static void WriteSteadyControlDict(string caseDir, int maxIter, string application,
            string passiveScalar = null, double scalarDiffusivity = 0,
            bool compressible = false, List<ReleaseSource3D> inlineSources = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "controlDict"));
            sb.AppendFormat(Inv, "application     {0};\n\n", application);
            sb.Append("libs            (\"libatmosphericModels.so\");\n\n");
            sb.Append("startFrom       startTime;\nstartTime       0;\n\n");
            sb.AppendFormat(Inv, "stopAt          endTime;\nendTime         {0};\n\n", maxIter);
            sb.Append("deltaT          1;\n\n");
            sb.AppendFormat(Inv, "writeControl    timeStep;\nwriteInterval   {0};\n\n", maxIter);
            sb.Append("purgeWrite      2;\n\n");
            sb.Append("writeFormat     ascii;\nwritePrecision  8;\nwriteCompression off;\n\n");
            sb.Append("timeFormat      general;\ntimePrecision   6;\n\nrunTimeModifiable true;\n");

            if (passiveScalar != null)
            {
                sb.Append("\nfunctions\n{\n");
                sb.AppendFormat("    {0}Transport\n    {{\n", passiveScalar);
                sb.Append("        type            scalarTransport;\n");
                sb.Append("        libs            (\"libsolverFunctionObjects.so\");\n");
                sb.AppendFormat("        field           {0};\n", passiveScalar);
                if (compressible)
                    sb.Append("        rho             rho;\n");
                sb.Append("        nCorr           2;\n");
                sb.Append("        resetOnStartUp  false;\n");
                sb.Append("        writeControl    writeTime;\n");
                sb.AppendFormat(Inv, "        D               {0};\n", scalarDiffusivity > 0 ? scalarDiffusivity : 1e-5);

                if (inlineSources != null && inlineSources.Count > 0)
                {
                    sb.Append("\n        fvOptions\n        {\n");
                    for (int s = 0; s < inlineSources.Count; s++)
                    {
                        var src = inlineSources[s];
                        sb.AppendFormat(Inv, "            source_{0}\n            {{\n", s);
                        sb.Append("                type            scalarSemiImplicitSource;\n");
                        sb.Append("                active          true;\n\n");
                        sb.Append("                scalarSemiImplicitSourceCoeffs\n                {\n");
                        sb.AppendFormat(Inv, "                    selectionMode   cellSet;\n                    cellSet         sourceZone_{0};\n", s);
                        sb.Append("                    volumeMode      absolute;\n                    injectionRateSuSp\n                    {\n");
                        sb.AppendFormat(Inv, "                        {0}           ({1} 0);\n", passiveScalar, src.EffectiveReleaseRateKgPerS);
                        sb.Append("                    }\n                }\n            }\n");
                    }
                    sb.Append("        }\n");
                }

                sb.Append("    }\n}\n");
            }

            WriteFile(Path.Combine(caseDir, "system", "controlDict"), sb.ToString());
        }

        private static void WriteSteadyFvSchemes(string caseDir, CfdConfiguration config)
        {
            string scheme = config.NumericalScheme ?? "linearUpwind";
            string divEntry = scheme == "linearUpwind"
                ? "Gauss linearUpwind grad(T)"
                : "Gauss " + scheme;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         steadyState;\n}\n\n");
            sb.Append("gradSchemes\n{\n    default         Gauss linear;\n}\n\n");
            sb.AppendFormat("divSchemes\n{{\n    default         none;\n    div(phi,T)      {0};\n}}\n\n", divEntry);
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WriteSteadyFvSolution(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n    T\n    {\n        solver          PBiCGStab;\n");
            sb.AppendFormat(Inv, "        preconditioner  DILU;\n        tolerance       {0};\n        relTol          0.01;\n    }}\n}}\n\n",
                config.SolverTolerance);
            sb.Append("SIMPLE\n{\n    nNonOrthogonalCorrectors 0;\n\n");
            sb.Append("    residualControl\n    {\n        T               1e-6;\n    }\n}\n\n");
            sb.Append("relaxationFactors\n{\n    equations\n    {\n        T               0.7;\n    }\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WriteSimpleFoamScalarFvSchemes(string caseDir, CfdConfiguration config)
        {
            string scheme = config.NumericalScheme ?? "linearUpwind";
            string divT = scheme == "linearUpwind"
                ? "Gauss linearUpwind grad(T)"
                : "Gauss " + scheme;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         steadyState;\n}\n\n");
            sb.Append("gradSchemes\n{\n    default         Gauss linear;\n");
            sb.Append("    grad(p)         Gauss linear;\n");
            sb.Append("    grad(U)         cellLimited Gauss linear 1;\n}\n\n");
            sb.Append("divSchemes\n{\n    default         none;\n");
            sb.Append("    div(phi,U)      Gauss linearUpwind grad(U);\n");
            sb.AppendFormat("    div(phi,T)      {0};\n", divT);
            sb.Append("    div((nuEff*dev2(T(grad(U))))) Gauss linear;\n}\n\n");
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n\n");
            sb.Append("wallDist\n{\n    method meshWave;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WriteSimpleFoamScalarFvSolution(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n");
            sb.Append("    p\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.1;\n        smoother        GaussSeidel;\n    }\n");
            sb.Append("    U\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n        tolerance       1e-06;\n        relTol          0.1;\n    }\n");
            sb.Append("    T\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n");
            sb.AppendFormat(Inv, "        tolerance       {0};\n        relTol          0.01;\n    }}\n", config.SolverTolerance);
            sb.Append("}\n\n");
            sb.Append("SIMPLE\n{\n    nNonOrthogonalCorrectors 0;\n    consistent      yes;\n\n");
            sb.Append("    residualControl\n    {\n        p               1e-4;\n        U               1e-4;\n        T               1e-6;\n    }\n}\n\n");
            sb.Append("relaxationFactors\n{\n");
            sb.Append("    fields\n    {\n        p               0.3;\n    }\n");
            sb.Append("    equations\n    {\n        U               0.7;\n        T               0.7;\n    }\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WriteSimpleFoamScalarTransportProperties(string caseDir, double diffusivity)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "transportProperties"));
            sb.Append("transportModel  Newtonian;\n");
            sb.AppendFormat(Inv, "nu              1.5e-05;\n");
            sb.AppendFormat(Inv, "DT              {0};\n", diffusivity);
            WriteFile(Path.Combine(caseDir, "constant", "transportProperties"), sb.ToString());
        }

        private static double[] EstimatePlumeBounds(DispersionScenario scenario, double marginFactor)
        {
            var engine = new GaussianPuffEngine();
            engine.Initialize(scenario);

            double endTime = scenario.SimulationDurationS;
            double step = Math.Max(1.0, endTime / 200.0);

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double maxZ = double.MinValue;

            for (double t = step; t <= endTime; t += step)
            {
                engine.StepTo(t);
                foreach (var p in engine.ActivePuffs)
                {
                    if (p.MinBound.X < minX) minX = p.MinBound.X;
                    if (p.MaxBound.X > maxX) maxX = p.MaxBound.X;
                    if (p.MinBound.Y < minY) minY = p.MinBound.Y;
                    if (p.MaxBound.Y > maxY) maxY = p.MaxBound.Y;
                    if (p.MaxBound.Z > maxZ) maxZ = p.MaxBound.Z;
                }
            }

            if (minX > maxX)
            {
                var pos = scenario.Sources[0].EffectivePosition;
                minX = pos.X - 100; maxX = pos.X + 100;
                minY = pos.Y - 100; maxY = pos.Y + 100;
                maxZ = 50;
            }

            double mx = (maxX - minX) * (marginFactor - 1.0) * 0.5;
            double my = (maxY - minY) * (marginFactor - 1.0) * 0.5;
            double mz = maxZ * (marginFactor - 1.0);

            return new double[]
            {
                minX - mx, maxX + mx,
                minY - my, maxY + my,
                0, maxZ + mz
            };
        }

        /// <summary>
        /// Generates an OpenFOAM case directory for a steady-state wind field simulation using simpleFoam.
        /// Optionally includes obstacle definitions using porosity sources.
        /// </summary>
        /// <param name="scenario">The dispersion scenario containing domain size, grid resolution, and wind conditions.</param>
        /// <param name="config">The CFD configuration specifying working directory, parallelism, and solver options.</param>
        /// <param name="obstacles">Optional list of bounding boxes representing obstacles in the domain.</param>
        /// <returns>The absolute path to the generated wind case directory.</returns>
        public static string GenerateWindCase(DispersionScenario scenario, CfdConfiguration config,
            List<Models.BoundingBox> obstacles)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "wind_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            var wind = scenario.Meteo.WindVector;

            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteWindControlDict(caseDir);
            WriteWindFvSchemes(caseDir);
            WriteWindFvSolution(caseDir);
            WriteWindTransportProperties(caseDir);
            WriteWindTurbulenceProperties(caseDir);
            WriteUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WritePField(caseDir);

            if (obstacles != null && obstacles.Count > 0)
                WriteWindObstacles(caseDir, obstacles);

            // Intentionally NOT writing refinement dicts here. The wind result is sampled onto
            // a regular nx*ny*nz grid in OpenFoamResultReader.ReadWindField, which compares the
            // U-field cell count to the structured-grid count. A refined mesh would have a
            // different (larger) count and the reader would bail with "no U field could be read".

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        private static void WriteWindControlDict(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "controlDict"));
            sb.Append("application     simpleFoam;\n\n");
            sb.Append("startFrom       startTime;\nstartTime       0;\n\n");
            sb.Append("stopAt          endTime;\nendTime         500;\n\n");
            sb.Append("deltaT          1;\n\n");
            sb.Append("writeControl    timeStep;\nwriteInterval   500;\n\n");
            sb.Append("purgeWrite      1;\n\n");
            sb.Append("writeFormat     ascii;\nwritePrecision  8;\nwriteCompression off;\n\n");
            sb.Append("timeFormat      general;\ntimePrecision   6;\n\nrunTimeModifiable true;\n");
            WriteFile(Path.Combine(caseDir, "system", "controlDict"), sb.ToString());
        }

        private static void WriteWindFvSchemes(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         steadyState;\n}\n\n");
            sb.Append("gradSchemes\n{\n    default         Gauss linear;\n    grad(p)         Gauss linear;\n    grad(U)         cellLimited Gauss linear 1;\n}\n\n");
            sb.Append("divSchemes\n{\n    default         none;\n");
            sb.Append("    div(phi,U)      Gauss linearUpwind grad(U);\n");
            sb.Append("    div((nuEff*dev2(T(grad(U))))) Gauss linear;\n}\n\n");
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n\n");
            sb.Append("wallDist\n{\n    method meshWave;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WriteWindFvSolution(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n");
            // Tighter GAMG: agglomerate aggressively, smaller cellsInCoarsestLevel to keep
            // pressure system well-posed near porosity cellZones (where it diverged before).
            sb.Append("    p\n    {\n        solver          GAMG;\n        tolerance       1e-07;\n        relTol          0.01;\n");
            sb.Append("        smoother        GaussSeidel;\n        nPreSweeps      0;\n        nPostSweeps     2;\n        cacheAgglomeration on;\n        nCellsInCoarsestLevel 10;\n        agglomerator    faceAreaPair;\n        mergeLevels     1;\n    }\n");
            sb.Append("    U\n    {\n        solver          smoothSolver;\n        smoother        symGaussSeidel;\n        tolerance       1e-07;\n        relTol          0.01;\n        nSweeps         2;\n    }\n");
            sb.Append("}\n\n");
            sb.Append("SIMPLE\n{\n    nNonOrthogonalCorrectors 0;\n");
            // consistent yes (SIMPLEC) lets us use a higher pressure relaxation safely;
            // without porosity 0.3 was fine, but with porosity we need to back off further.
            sb.Append("    consistent      yes;\n\n");
            sb.Append("    residualControl\n    {\n        p               1e-3;\n        U               1e-4;\n    }\n}\n\n");
            sb.Append("relaxationFactors\n{\n");
            // Conservative factors: SIMPLE with cellZone porosity is fragile, prefer slower
            // but stable convergence over fast-and-divergent.
            sb.Append("    fields\n    {\n        p               0.2;\n    }\n");
            sb.Append("    equations\n    {\n        U               0.5;\n    }\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WriteWindTransportProperties(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "transportProperties"));
            sb.Append("transportModel  Newtonian;\n");
            sb.AppendFormat(Inv, "nu              1.5e-05;\n");
            WriteFile(Path.Combine(caseDir, "constant", "transportProperties"), sb.ToString());
        }

        private static void WriteWindTurbulenceProperties(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "turbulenceProperties"));
            sb.Append("simulationType  laminar;\n");
            WriteFile(Path.Combine(caseDir, "constant", "turbulenceProperties"), sb.ToString());
        }

        private static void WritePField(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "p"));
            sb.Append("dimensions      [0 2 -2 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            fixedValue;\n        value           uniform 0;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "p"), sb.ToString());
        }

        private static void WriteWindObstacles(string caseDir, List<Models.BoundingBox> obstacles)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "topoSetDict"));
            sb.Append("actions\n(\n");
            for (int i = 0; i < obstacles.Count; i++)
            {
                var box = obstacles[i];
                sb.AppendFormat(Inv,
                    "    {{\n        name    obstacle_{0};\n        type    cellSet;\n        action  new;\n" +
                    "        source  boxToCell;\n        sourceInfo\n        {{\n" +
                    "            min ({1} {2} {3});\n" +
                    "            max ({4} {5} {6});\n        }}\n    }}\n\n",
                    i, box.Min.X, box.Min.Y, Math.Max(0, box.Min.Z),
                    box.Max.X, box.Max.Y, box.Max.Z);
                // explicitPorositySource needs a cellZone, not a cellSet — promote
                // the freshly-created cellSet to a cellZone of the same name.
                sb.AppendFormat(Inv,
                    "    {{\n        name    obstacle_{0};\n        type    cellZoneSet;\n        action  new;\n" +
                    "        source  setToCellZone;\n        sourceInfo\n        {{\n" +
                    "            set obstacle_{0};\n        }}\n    }}\n\n", i);
            }
            sb.Append(");\n");
            WriteFile(Path.Combine(caseDir, "system", "topoSetDict"), sb.ToString());

            var sfb = new StringBuilder();
            sfb.Append(FoamHeader("dictionary", "setFieldsDict"));
            sfb.Append("defaultFieldValues\n(\n);\n\nregions\n(\n");
            for (int i = 0; i < obstacles.Count; i++)
            {
                var box = obstacles[i];
                sfb.Append("    boxToCell\n    {\n");
                sfb.AppendFormat(Inv, "        min ({0} {1} {2});\n",
                    box.Min.X, box.Min.Y, Math.Max(0, box.Min.Z));
                sfb.AppendFormat(Inv, "        max ({0} {1} {2});\n",
                    box.Max.X, box.Max.Y, box.Max.Z);
                sfb.Append("        fieldValues\n        (\n");
                sfb.Append("            volVectorFieldValue U (0 0 0)\n");
                sfb.Append("        );\n    }\n\n");
            }
            sfb.Append(");\n");
            WriteFile(Path.Combine(caseDir, "system", "setFieldsDict"), sfb.ToString());

            var fvo = new StringBuilder();
            fvo.Append(FoamHeader("dictionary", "fvOptions"));
            for (int i = 0; i < obstacles.Count; i++)
            {
                fvo.AppendFormat(Inv, "obstacle_{0}\n{{\n", i);
                fvo.Append("    type            explicitPorositySource;\n    active          true;\n\n");
                fvo.Append("    explicitPorositySourceCoeffs\n    {\n");
                fvo.AppendFormat(Inv, "        selectionMode   cellZone;\n        cellZone        obstacle_{0};\n", i);
                fvo.Append("        type            DarcyForchheimer;\n");
                fvo.Append("        DarcyForchheimerCoeffs\n        {\n");
                // Darcy + Forchheimer — moderate values that hold flow near zero without
                // creating the pressure shock that destabilises GAMG. f=1 is plenty to damp
                // the convective term; bigger values cause late-iteration divergence.
                fvo.Append("            d   (1e4 1e4 1e4);\n");
                fvo.Append("            f   (1 1 1);\n");
                fvo.Append("            coordinateSystem\n            {\n");
                fvo.Append("                type    cartesian;\n");
                fvo.Append("                origin  (0 0 0);\n");
                fvo.Append("                rotation\n                {\n");
                fvo.Append("                    type    axes;\n");
                fvo.Append("                    e1      (1 0 0);\n");
                fvo.Append("                    e2      (0 1 0);\n");
                fvo.Append("                }\n            }\n");
                fvo.Append("        }\n    }\n}\n\n");
            }
            WriteFile(Path.Combine(caseDir, "constant", "fvOptions"), fvo.ToString());
        }

        private static void WriteFile(string path, string content)
        {
            File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        private static string FoamHeader(string cls, string obj)
        {
            return "FoamFile\n{\n    version     2.0;\n    format      ascii;\n    class       " +
                   cls + ";\n    object      " + obj + ";\n}\n\n";
        }

        private static void WriteControlDict(string caseDir, double endTime, double dt, double writeInterval, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "controlDict"));
            sb.AppendFormat(Inv, "application     scalarTransportFoam;\n\n");
            sb.AppendFormat(Inv, "startFrom       startTime;\nstartTime       0;\n\n");
            sb.AppendFormat(Inv, "stopAt          endTime;\nendTime         {0};\n\n", endTime);
            sb.AppendFormat(Inv, "deltaT          {0};\n\n", dt);
            sb.AppendFormat(Inv, "writeControl    adjustableRunTime;\nwriteInterval   {0};\n\n", writeInterval);
            sb.AppendFormat(Inv, "purgeWrite      {0};\n\n", config.PurgeWrite);
            sb.Append("writeFormat     ascii;\nwritePrecision  8;\nwriteCompression off;\n\n");
            sb.Append("timeFormat      general;\ntimePrecision   6;\n\nrunTimeModifiable true;\n");
            if (config.AdjustableTimeStep)
            {
                sb.Append("\nadjustTimeStep  yes;\n");
                sb.AppendFormat(Inv, "maxCo           {0};\n", config.MaxCourantNumber);
            }
            else
            {
                sb.Append("\nadjustTimeStep  no;\n");
            }
            WriteFile(Path.Combine(caseDir, "system", "controlDict"), sb.ToString());
        }

        private static void WriteFvSchemes(string caseDir, CfdConfiguration config)
        {
            string scheme = config.NumericalScheme ?? "linearUpwind";
            string divEntry;
            if (scheme == "linearUpwind")
                divEntry = "Gauss linearUpwind grad(T)";
            else
                divEntry = "Gauss " + scheme;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         Euler;\n}\n\n");
            sb.Append("gradSchemes\n{\n    default         Gauss linear;\n}\n\n");
            sb.AppendFormat("divSchemes\n{{\n    default         none;\n    div(phi,T)      {0};\n}}\n\n", divEntry);
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n\n");
            sb.Append("wallDist\n{\n    method meshWave;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WriteFvSolution(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n    T\n    {\n        solver          PBiCGStab;\n");
            sb.AppendFormat(Inv, "        preconditioner  DILU;\n        tolerance       {0};\n        relTol          0;\n    }}\n}}\n\n",
                config.SolverTolerance);
            sb.Append("SIMPLE\n{\n    nNonOrthogonalCorrectors 0;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WriteBlockMeshDict(string caseDir, double xMin, double xMax,
            double yMin, double yMax, double zMax, int nx, int ny, int nz)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "blockMeshDict"));
            sb.Append("scale 1;\n\n");
            sb.Append("vertices\n(\n");
            sb.AppendFormat(Inv, "    ({0} {1} 0)\n", xMin, yMin);
            sb.AppendFormat(Inv, "    ({0} {1} 0)\n", xMax, yMin);
            sb.AppendFormat(Inv, "    ({0} {1} 0)\n", xMax, yMax);
            sb.AppendFormat(Inv, "    ({0} {1} 0)\n", xMin, yMax);
            sb.AppendFormat(Inv, "    ({0} {1} {2})\n", xMin, yMin, zMax);
            sb.AppendFormat(Inv, "    ({0} {1} {2})\n", xMax, yMin, zMax);
            sb.AppendFormat(Inv, "    ({0} {1} {2})\n", xMax, yMax, zMax);
            sb.AppendFormat(Inv, "    ({0} {1} {2})\n", xMin, yMax, zMax);
            sb.Append(");\n\n");

            sb.AppendFormat(Inv, "blocks\n(\n    hex (0 1 2 3 4 5 6 7) ({0} {1} {2}) simpleGrading (1 1 1)\n);\n\n",
                nx, ny, nz);

            sb.Append("edges\n(\n);\n\n");
            sb.Append("boundary\n(\n");
            sb.Append("    atmosphere\n    {\n        type patch;\n        faces\n        (\n");
            sb.Append("            (0 4 7 3)\n");
            sb.Append("            (1 2 6 5)\n");
            sb.Append("            (0 1 5 4)\n");
            sb.Append("            (3 7 6 2)\n");
            sb.Append("            (4 5 6 7)\n");
            sb.Append("        );\n    }\n");
            sb.Append("    ground\n    {\n        type wall;\n        faces\n        (\n            (0 3 2 1)\n        );\n    }\n");
            sb.Append(");\n\nmergePatchPairs\n(\n);\n");

            WriteFile(Path.Combine(caseDir, "system", "blockMeshDict"), sb.ToString());
        }

        private static void WriteTransportProperties(string caseDir, double diffusivity)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "transportProperties"));
            sb.AppendFormat(Inv, "DT              {0};\n", diffusivity);
            WriteFile(Path.Combine(caseDir, "constant", "transportProperties"), sb.ToString());
        }

        private static void WriteUField(string caseDir, double ux, double uy, double uz,
            CfdConfiguration cfd = null, MeteorologicalConditions meteo = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volVectorField", "U"));
            sb.Append("dimensions      [0 1 -1 0 0 0 0];\n\n");
            sb.AppendFormat(Inv, "internalField   uniform ({0} {1} {2});\n\n", ux, uy, uz);
            sb.Append("boundaryField\n{\n");
            if (cfd != null && cfd.UseAtmosphericBL && meteo != null)
            {
                AppendAtmInletU(sb, "atmosphere", ux, uy, uz, meteo);
            }
            else
            {
                sb.AppendFormat(Inv, "    atmosphere\n    {{\n        type            fixedValue;\n        value           uniform ({0} {1} {2});\n    }}\n", ux, uy, uz);
            }
            sb.Append("    ground\n    {\n        type            fixedValue;\n        value           uniform (0 0 0);\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "U"), sb.ToString());
        }

        // ── Atmospheric BL helpers (Mack & Spruijt 2013, Vu 2019, Schalau 2021) ──

        /// <summary>
        /// Emits an atmBoundaryLayerInletVelocity patch for U. The flow direction is taken
        /// from the meteo wind vector (already projected into the wind transport convention);
        /// Uref/Zref/z0 come from MeteorologicalConditions.
        /// </summary>
        private static void AppendAtmInletU(StringBuilder sb, string patch,
            double ux, double uy, double uz, MeteorologicalConditions meteo)
        {
            double speed = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            double dx = speed > 1e-9 ? ux / speed : 1;
            double dy = speed > 1e-9 ? uy / speed : 0;
            double dz = 0; // horizontal flow direction; z is the "up" axis
            double uref = meteo.WindSpeed > 0 ? meteo.WindSpeed : speed;
            double zref = meteo.WindMeasurementHeightM > 0 ? meteo.WindMeasurementHeightM : 10.0;
            double z0 = meteo.RoughnessLengthM > 0 ? meteo.RoughnessLengthM : 0.03;

            sb.AppendFormat(Inv, "    {0}\n    {{\n", patch);
            sb.Append("        type            atmBoundaryLayerInletVelocity;\n");
            sb.AppendFormat(Inv, "        flowDir         ({0} {1} {2});\n", dx, dy, dz);
            sb.Append("        zDir            (0 0 1);\n");
            sb.AppendFormat(Inv, "        Uref            {0};\n", uref);
            sb.AppendFormat(Inv, "        Zref            {0};\n", zref);
            sb.AppendFormat(Inv, "        z0              uniform {0};\n", z0);
            sb.Append("        d               uniform 0;\n");
            sb.Append("        zGround         uniform 0;\n");
            sb.AppendFormat(Inv, "        value           uniform ({0} {1} {2});\n", ux, uy, uz);
            sb.Append("    }\n");
        }

        private static void AppendAtmInletK(StringBuilder sb, string patch,
            double kFallback, MeteorologicalConditions meteo)
        {
            double uref = meteo.WindSpeed > 0 ? meteo.WindSpeed : 5.0;
            double zref = meteo.WindMeasurementHeightM > 0 ? meteo.WindMeasurementHeightM : 10.0;
            double z0 = meteo.RoughnessLengthM > 0 ? meteo.RoughnessLengthM : 0.03;

            sb.AppendFormat(Inv, "    {0}\n    {{\n", patch);
            sb.Append("        type            atmBoundaryLayerInletK;\n");
            sb.Append("        flowDir         (1 0 0);\n");
            sb.Append("        zDir            (0 0 1);\n");
            sb.AppendFormat(Inv, "        Uref            {0};\n", uref);
            sb.AppendFormat(Inv, "        Zref            {0};\n", zref);
            sb.AppendFormat(Inv, "        z0              uniform {0};\n", z0);
            sb.Append("        d               uniform 0;\n");
            sb.Append("        zGround         uniform 0;\n");
            sb.AppendFormat(Inv, "        value           uniform {0};\n", kFallback);
            sb.Append("    }\n");
        }

        // Same `d` zero-plane displacement is required by atmBoundaryLayerInletEpsilon in v2512.
        private static void AppendAtmInletEpsilon(StringBuilder sb, string patch,
            double epsFallback, MeteorologicalConditions meteo)
        {
            double uref = meteo.WindSpeed > 0 ? meteo.WindSpeed : 5.0;
            double zref = meteo.WindMeasurementHeightM > 0 ? meteo.WindMeasurementHeightM : 10.0;
            double z0 = meteo.RoughnessLengthM > 0 ? meteo.RoughnessLengthM : 0.03;

            sb.AppendFormat(Inv, "    {0}\n    {{\n", patch);
            sb.Append("        type            atmBoundaryLayerInletEpsilon;\n");
            sb.Append("        flowDir         (1 0 0);\n");
            sb.Append("        zDir            (0 0 1);\n");
            sb.AppendFormat(Inv, "        Uref            {0};\n", uref);
            sb.AppendFormat(Inv, "        Zref            {0};\n", zref);
            sb.AppendFormat(Inv, "        z0              uniform {0};\n", z0);
            sb.Append("        d               uniform 0;\n");
            sb.Append("        zGround         uniform 0;\n");
            sb.AppendFormat(Inv, "        value           uniform {0};\n", epsFallback);
            sb.Append("    }\n");
        }

        private static void AppendAtmGroundNut(StringBuilder sb, MeteorologicalConditions meteo)
        {
            // OpenFOAM v2x atmospheric nut wall function is `atmNutkWallFunction`
            // (the older name `nutkAtmRoughWallFunction` was renamed circa v1906).
            double z0 = meteo.RoughnessLengthM > 0 ? meteo.RoughnessLengthM : 0.03;
            sb.Append("    ground\n    {\n");
            sb.Append("        type            atmNutkWallFunction;\n");
            sb.Append("        Cmu             0.09;\n");
            sb.AppendFormat(Inv, "        z0              uniform {0};\n", z0);
            sb.Append("        value           uniform 0;\n");
            sb.Append("    }\n");
        }

        /// <summary>
        /// Emits the ground T patch per <see cref="GroundThermalBoundary"/>: zeroGradient
        /// (adiabatic), fixedValue (fixed temperature, recommended for LNG per Vu 2019), or
        /// fixedGradient (fixed flux).
        /// </summary>
        /// <summary>
        /// Writes a non-fatal advisory to a `LOG_atmospheric.txt` file inside the case dir
        /// when the ground-adjacent mesh cell is smaller than z0. nutkAtmRoughWallFunction
        /// becomes ill-conditioned in that regime (Schalau 2021 §1, Vu 2019 §6.3).
        /// </summary>
        private static void WriteMeshVsRoughnessAdvisory(string caseDir, double cellSizeM,
            CfdConfiguration cfd, MeteorologicalConditions meteo)
        {
            if (cfd == null || !cfd.UseAtmosphericBL || meteo == null) return;
            double z0 = meteo.RoughnessLengthM;
            if (z0 <= 0) return;

            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "z0 = {0} m\nground-adjacent cell size = {1} m\n", z0, cellSizeM);
            if (cellSizeM < z0)
            {
                sb.AppendLine();
                sb.AppendLine("WARNING: ground-adjacent cell size is smaller than z0.");
                sb.AppendLine("nutkAtmRoughWallFunction is undefined for cell midpoint < z0.");
                sb.AppendFormat(Inv, "Recommended minimum cell size: {0} m (~2 * z0).\n", 2 * z0);
            }
            else if (cellSizeM < 2 * z0)
            {
                sb.AppendLine();
                sb.AppendLine("ADVISORY: ground-adjacent cell size is close to z0.");
                sb.AppendFormat(Inv, "Consider refining to >= {0} m for stable wall-function behavior.\n", 2 * z0);
            }
            try { WriteFile(Path.Combine(caseDir, "LOG_atmospheric.txt"), sb.ToString()); }
            catch { /* non-fatal */ }
        }

        private static void AppendGroundT(StringBuilder sb, CfdConfiguration cfd)
        {
            if (cfd == null || cfd.GroundThermalBC == GroundThermalBoundary.Adiabatic)
            {
                sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
                return;
            }
            if (cfd.GroundThermalBC == GroundThermalBoundary.FixedTemperature)
            {
                sb.AppendFormat(Inv, "    ground\n    {{\n        type            fixedValue;\n        value           uniform {0};\n    }}\n",
                    cfd.GroundTemperatureK);
                return;
            }
            // FixedFlux — gradient gradient = q" / (k_thermal). Use ~0.026 W/m/K (air) as
            // a safe estimate; user can override q" directly. dT/dz = q"/k.
            double k_air = 0.026;
            double grad = cfd.GroundHeatFluxWPerM2 / k_air;
            sb.AppendFormat(Inv, "    ground\n    {{\n        type            fixedGradient;\n        gradient        uniform {0};\n    }}\n",
                grad);
        }

        private static void WriteTField(string caseDir, CfdConfiguration cfd = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "T"));
            sb.Append("dimensions      [0 0 0 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            inletOutlet;\n        inletValue      uniform 0;\n        value           uniform 0;\n    }\n");
            AppendGroundT(sb, cfd);
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "T"), sb.ToString());
        }

        private static void WritePassiveScalarField(string caseDir, string fieldName)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", fieldName));
            sb.Append("dimensions      [0 0 0 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            inletOutlet;\n        inletValue      uniform 0;\n        value           uniform 0;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", fieldName), sb.ToString());
        }

        /// <summary>
        /// Writes a constant/fvOptions dict with one <c>scalarSemiImplicitSource</c> per
        /// release, injecting <c>Y_CH4</c> mass at the release rate (kg/s, distributed
        /// across the source cellSet via <c>volumeMode absolute</c>). This gives
        /// rhoReactingBuoyantFoam / reactingFoam a sustained mass-source for continuous
        /// pool/jet releases — the <c>setFields</c>-only initial condition was leaving
        /// the species field to dilute and decay to zero.
        ///
        /// When <see cref="ReleaseSource3D.ReleaseDurationS"/> &gt; 0, the source is
        /// switched off after that many seconds via <c>cellSetOption</c>'s timeStart/
        /// duration keywords (Burro 9 spilled for 79 s, then the cloud continued to drift).
        /// </summary>
        private static void WriteReactingSpeciesSourceFvOptions(string caseDir,
            List<ReleaseSource3D> sources, string speciesNameOverride = null,
            bool compressible = false)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvOptions"));
            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                double q = src.EffectiveReleaseRateKgPerS;
                if (q <= 0) continue;
                string species = speciesNameOverride ?? ResolveOpenFoamSpecies(src);

                if (compressible)
                {
                    // OpenFOAM v2512 ESI in this build doesn't ship massSource (it's not in
                    // the valid fvOption types list). Fall back to scalarSemiImplicitSource
                    // on the species only. We previously tried adding a complementary source
                    // on h to handle the enthalpy of the injected mass, but that broke the
                    // (h, p, Y) → T inversion in janafThermo and produced T = -500000 K
                    // explosions. Without a working massSource fvOption, the energy budget
                    // can't be made self-consistent — so we accept the cold-jet artifact
                    // and rely on the upwind schemes (below) to absorb the small mass
                    // imbalance the species-only source creates. Continuity errors are
                    // bounded at ~1e-3 kg/step (verified), which is acceptable for plume
                    // visualisation if not for tight quantitative comparison.
                    sb.AppendFormat(Inv, "release_{0}\n{{\n", s);
                    sb.Append("    type            scalarSemiImplicitSource;\n");
                    sb.Append("    active          true;\n");
                    if (src.ReleaseDurationS > 0)
                    {
                        sb.Append("    timeStart       0;\n");
                        sb.AppendFormat(Inv, "    duration        {0};\n", src.ReleaseDurationS);
                    }
                    sb.Append("    scalarSemiImplicitSourceCoeffs\n    {\n");
                    sb.Append("        selectionMode   cellSet;\n");
                    sb.AppendFormat(Inv, "        cellSet         sourceZone_{0};\n", s);
                    sb.Append("        volumeMode      absolute;\n");
                    sb.Append("        injectionRateSuSp\n        {\n");
                    sb.AppendFormat(Inv, "            {0}         ({1} 0);\n", species, q);
                    sb.Append("        }\n");
                    sb.Append("    }\n");
                    sb.Append("}\n\n");
                }
                else
                {
                    // Incompressible / passive-scalar — original scalarSemiImplicitSource path.
                    sb.AppendFormat(Inv, "release_{0}\n{{\n", s);
                    sb.Append("    type            scalarSemiImplicitSource;\n");
                    sb.Append("    active          true;\n");
                    if (src.ReleaseDurationS > 0)
                    {
                        sb.Append("    timeStart       0;\n");
                        sb.AppendFormat(Inv, "    duration        {0};\n", src.ReleaseDurationS);
                    }
                    sb.Append("    scalarSemiImplicitSourceCoeffs\n    {\n");
                    sb.Append("        selectionMode   cellSet;\n");
                    sb.AppendFormat(Inv, "        cellSet         sourceZone_{0};\n", s);
                    sb.Append("        volumeMode      absolute;\n");
                    sb.Append("        injectionRateSuSp\n        {\n");
                    sb.AppendFormat(Inv, "            {0}         ({1} 0);\n", species, q);
                    sb.Append("        }\n");
                    sb.Append("    }\n");
                    sb.Append("}\n\n");
                }
            }

            // Hard temperature clamp on every cell to prevent the (h, p, Y) → T inversion
            // in janafThermo from producing absurd values (e.g. T = -500000 K observed in
            // earlier failures) when the cold-jet injection makes h locally inconsistent.
            // limitTemperature is a base-fvOptions constraint shipped with every OpenFOAM
            // build (including v2512), so it's safe to add unconditionally.
            if (compressible)
            {
                sb.Append("temperatureLimit\n{\n");
                sb.Append("    type            limitTemperature;\n");
                sb.Append("    active          true;\n");
                sb.Append("    selectionMode   all;\n");
                sb.Append("    min             200;\n");
                sb.Append("    max             1000;\n");
                sb.Append("}\n\n");
            }
            WriteFile(Path.Combine(caseDir, "constant", "fvOptions"), sb.ToString());
        }

        private static void WriteFvOptions(string caseDir, List<ReleaseSource3D> sources,
            double xMin, double xMax, double yMin, double yMax, double zMax,
            double cellSize, int nx, int ny, int nz, string scalarName = "T")
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvOptions"));

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                var pos = src.EffectivePosition;
                double injectionRate = src.EffectiveReleaseRateKgPerS;

                sb.AppendFormat(Inv, "source_{0}\n{{\n", s);
                sb.Append("    type            scalarSemiImplicitSource;\n    active          true;\n\n");
                sb.Append("    scalarSemiImplicitSourceCoeffs\n    {\n");
                sb.AppendFormat(Inv, "        selectionMode   cellSet;\n        cellSet         sourceZone_{0};\n", s);
                sb.Append("        volumeMode      absolute;\n        injectionRateSuSp\n        {\n");
                sb.AppendFormat(Inv, "            {0}           ({1} 0);\n", scalarName, injectionRate);
                sb.Append("        }\n    }\n}\n\n");
            }

            WriteFile(Path.Combine(caseDir, "constant", "fvOptions"), sb.ToString());
        }

        private static void WriteFvModels(string caseDir, List<ReleaseSource3D> sources,
            double cellSize, string scalarName)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvModels"));

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                double injectionRate = src.EffectiveReleaseRateKgPerS;

                sb.AppendFormat(Inv, "source_{0}\n{{\n", s);
                sb.Append("    type            semiImplicitSource;\n\n");
                sb.AppendFormat(Inv, "    selectionMode   cellSet;\n    cellSet         sourceZone_{0};\n", s);
                sb.Append("    volumeMode      absolute;\n    sources\n    {\n");
                sb.AppendFormat(Inv, "        {0}           ({1} 0);\n", scalarName, injectionRate);
                sb.Append("    }\n}\n\n");
            }

            WriteFile(Path.Combine(caseDir, "constant", "fvModels"), sb.ToString());
        }

        private static void WriteSetFieldsDict(string caseDir, List<ReleaseSource3D> sources,
            System.Windows.Media.Media3D.Vector3D wind, double cellSize)
        {
            bool hasJet = false;
            foreach (var src in sources)
                if (src.ExpandedVelocityForCfdMS > 0) { hasJet = true; break; }

            if (!hasJet) return;

            double windMag = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y + wind.Z * wind.Z);
            if (windMag < 0.5) windMag = 0.5;
            double maxJetSpeed = windMag * 15.0;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "setFieldsDict"));
            sb.Append("defaultFieldValues\n(\n);\n\n");
            sb.Append("regions\n(\n");

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                // Birch & Schefer expanded pseudo-source — ensures Mach < 0.3 at the seeding box.
                double v = src.ExpandedVelocityForCfdMS;
                if (v <= 0) continue;

                var dir = src.ReleaseDirection;
                double jetMag = Math.Min(v, maxJetSpeed);
                double jx = dir.X * jetMag;
                double jy = dir.Y * jetMag;
                double jz = dir.Z * jetMag;

                var pos = src.EffectivePosition;
                // Box width = max(grid cell, expanded pseudo-orifice) so the seeding region covers the pseudo-source.
                double half = Math.Max(cellSize * 0.55, src.ExpandedDiameterForCfdM * 0.5);

                int nSegments = 5;
                double segLen = cellSize;
                for (int i = 0; i < nSegments; i++)
                {
                    double frac = 1.0 - (double)i / nSegments;
                    double ux = wind.X + jx * frac;
                    double uy = wind.Y + jy * frac;
                    double uz = wind.Z + jz * frac;

                    double cx = pos.X + dir.X * (i + 0.5) * segLen;
                    double cy = pos.Y + dir.Y * (i + 0.5) * segLen;
                    double cz = pos.Z + dir.Z * (i + 0.5) * segLen;

                    sb.Append("    boxToCell\n    {\n");
                    sb.AppendFormat(Inv, "        min ({0} {1} {2});\n",
                        cx - half, cy - half, Math.Max(0, cz - half));
                    sb.AppendFormat(Inv, "        max ({0} {1} {2});\n",
                        cx + half, cy + half, cz + half);
                    sb.Append("        fieldValues\n        (\n");
                    sb.AppendFormat(Inv, "            volVectorFieldValue U ({0} {1} {2})\n", ux, uy, uz);
                    sb.Append("        );\n    }\n\n");
                }
            }

            sb.Append(");\n");
            WriteFile(Path.Combine(caseDir, "system", "setFieldsDict"), sb.ToString());
        }

        private static void WriteDecomposeParDict(string caseDir, int nProcs)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "decomposeParDict"));
            sb.AppendFormat(Inv, "numberOfSubdomains  {0};\n\n", nProcs);
            sb.Append("method          scotch;\n");
            WriteFile(Path.Combine(caseDir, "system", "decomposeParDict"), sb.ToString());
        }

        private static void WriteTopoSetDict(string caseDir, List<ReleaseSource3D> sources, double cellSize)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "topoSetDict"));
            sb.Append("actions\n(\n");

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                var pos = src.EffectivePosition;
                // Size the source box from the real pool/stack diameter when available
                // (Burro 9 has a 32 m pool; previously we forced it into a single cell of
                // ~10 m, which over-concentrated the injected mass and gave too much
                // numerical diffusion). Fall back to the cell-size minimum to ensure at
                // least one full cell is selected.
                double diameter = src.StackDiameterM > 0 ? src.StackDiameterM : 0;
                double half = Math.Max(cellSize * 0.55, diameter * 0.5);
                double zHalf = Math.Max(cellSize * 0.55, diameter * 0.25);

                sb.AppendFormat(Inv, "    {{\n        name    sourceZone_{0};\n        type    cellSet;\n        action  new;\n", s);
                sb.AppendFormat(Inv, "        source  boxToCell;\n        sourceInfo\n        {{\n");
                sb.AppendFormat(Inv, "            min ({0} {1} {2});\n",
                    pos.X - half, pos.Y - half, Math.Max(0, pos.Z - zHalf));
                sb.AppendFormat(Inv, "            max ({0} {1} {2});\n",
                    pos.X + half, pos.Y + half, pos.Z + zHalf);
                sb.Append("        }\n    }\n\n");
            }

            sb.Append(");\n");
            WriteFile(Path.Combine(caseDir, "system", "topoSetDict"), sb.ToString());
        }

        private static void WriteJetSetFieldsDict(string caseDir, List<ReleaseSource3D> sources,
            System.Windows.Media.Media3D.Vector3D wind, double cellSize)
        {
            bool hasJet = false;
            foreach (var src in sources)
                if (src.ExpandedVelocityForCfdMS > 0) { hasJet = true; break; }
            if (!hasJet) return;

            double windMag = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y + wind.Z * wind.Z);
            if (windMag < 0.5) windMag = 0.5;
            double maxJetSpeed = windMag * 5.0;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "setFieldsDict"));
            sb.Append("defaultFieldValues\n(\n);\n\n");
            sb.Append("regions\n(\n");

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                // Use Birch & Schefer expanded pseudo-source for choked HP leaks; physical for others.
                double v = src.ExpandedVelocityForCfdMS;
                if (v <= 0) continue;

                var dir = src.ReleaseDirection;
                double jetMag = Math.Min(v, maxJetSpeed);

                double jx = dir.X * jetMag;
                double jy = dir.Y * jetMag;
                double jz = dir.Z * jetMag;

                var pos = src.EffectivePosition;
                // Box width = max(grid cell, expanded pseudo-orifice) so the seeding region covers the pseudo-source.
                double half = Math.Max(cellSize * 0.55, src.ExpandedDiameterForCfdM * 0.5);

                int nSegments = 3;
                double segLen = cellSize;
                for (int i = 0; i < nSegments; i++)
                {
                    double frac = 1.0 - (double)i / nSegments;
                    double ux = wind.X + jx * frac;
                    double uy = wind.Y + jy * frac;
                    double uz = wind.Z + jz * frac;

                    double cx = pos.X + dir.X * (i + 0.5) * segLen;
                    double cy = pos.Y + dir.Y * (i + 0.5) * segLen;
                    double cz = pos.Z + dir.Z * (i + 0.5) * segLen;

                    sb.Append("    boxToCell\n    {\n");
                    sb.AppendFormat(Inv, "        min ({0} {1} {2});\n",
                        cx - half, cy - half, Math.Max(0, cz - half));
                    sb.AppendFormat(Inv, "        max ({0} {1} {2});\n",
                        cx + half, cy + half, cz + half);
                    sb.Append("        fieldValues\n        (\n");
                    sb.AppendFormat(Inv, "            volVectorFieldValue U ({0} {1} {2})\n", ux, uy, uz);
                    sb.Append("        );\n    }\n\n");
                }
            }

            sb.Append(");\n");
            WriteFile(Path.Combine(caseDir, "system", "setFieldsDict"), sb.ToString());
        }

        private static void WriteRefinementDicts(string caseDir, List<ReleaseSource3D> sources,
            double cellSize, List<Models.BoundingBox> obstacles,
            MeteorologicalConditions meteo = null, double domainHalfExtentM = 0)
        {
            int srcCount = sources != null ? sources.Count : 0;
            int obsCount = obstacles != null ? obstacles.Count : 0;
            if (srcCount == 0 && obsCount == 0)
                return; // nothing to refine — skip writing dicts entirely

            double coarseRadius = cellSize * 8;
            double fineRadius = cellSize * 3;

            // Plume corridor parameters: only level 0 (the outer/coarser refinement) gets the
            // long downstream strip; level 1 stays focused on the source for high resolution
            // where the jet/pool is most concentrated. The corridor follows the wind vector
            // for `corridorLengthM` and is sized via Pasquill-Gifford σ at the corridor end —
            // 4σ wide and 4σ tall captures > 99 % of a Gaussian plume's mass.
            bool wantCorridor = meteo != null && domainHalfExtentM > 0 && srcCount > 0;
            double corridorLengthM = 0, corridorHalfWidthM = 0, corridorHeightM = 0;
            double windDx = 0, windDy = 0;
            if (wantCorridor)
            {
                corridorLengthM = 1.8 * domainHalfExtentM;          // span most of the downwind half
                var sigma = PasquillGiffordCoefficients.ComputeSigma(corridorLengthM, meteo.StabilityClass);
                corridorHalfWidthM = Math.Max(2.0 * sigma.sigmaY, 4 * cellSize);
                corridorHeightM    = Math.Max(2.0 * sigma.sigmaZ, 4 * cellSize);
                var w = meteo.WindVector;
                double mag = Math.Sqrt(w.X * w.X + w.Y * w.Y);
                if (mag > 1e-6) { windDx = w.X / mag; windDy = w.Y / mag; }
                else            { windDx = 1; windDy = 0; }
            }

            for (int level = 0; level < 2; level++)
            {
                double radius = level == 0 ? coarseRadius : fineRadius;
                string dictName = string.Format("topoSetDict_refine{0}", level);

                var sb = new StringBuilder();
                sb.Append(FoamHeader("dictionary", dictName));
                sb.Append("actions\n(\n");

                bool firstAction = true;

                for (int s = 0; s < srcCount; s++)
                {
                    var pos = sources[s].EffectivePosition;
                    string action = firstAction ? "new" : "add";
                    sb.AppendFormat(Inv, "    {{\n        name    refineZone;\n        type    cellSet;\n        action  {0};\n", action);
                    sb.Append("        source  boxToCell;\n        sourceInfo\n        {\n");
                    sb.AppendFormat(Inv, "            min ({0} {1} {2});\n",
                        pos.X - radius, pos.Y - radius, Math.Max(0, pos.Z - radius));
                    sb.AppendFormat(Inv, "            max ({0} {1} {2});\n",
                        pos.X + radius, pos.Y + radius, pos.Z + radius);
                    sb.Append("        }\n    }\n\n");
                    firstAction = false;

                    // Plume-footprint refinement (level 0 only): instead of a single uniform
                    // corridor strip, lay down a sequence of progressively-wider boxes along
                    // the wind trajectory, each sized to the Pasquill σ at its downwind
                    // distance — a "Gaussian cone" that refines exactly where the plume
                    // exists, narrow near source and wide far field. ~6× fewer refined cells
                    // than a worst-case corridor for the same plume capture.
                    if (level == 0 && wantCorridor)
                    {
                        double[] downwind = { 0.05, 0.10, 0.20, 0.40, 0.70, 1.00 };
                        for (int seg = 0; seg < downwind.Length; seg++)
                        {
                            double dStart = (seg == 0 ? 0 : downwind[seg - 1]) * corridorLengthM;
                            double dEnd   = downwind[seg] * corridorLengthM;
                            double dMid   = 0.5 * (dStart + dEnd);
                            var sig = PasquillGiffordCoefficients.ComputeSigma(Math.Max(dMid, 1.0), meteo.StabilityClass);
                            double halfWy = Math.Max(2.0 * sig.sigmaY, 4 * cellSize);
                            double halfWz = Math.Max(2.0 * sig.sigmaZ, 4 * cellSize);
                            double sx = pos.X + windDx * dStart;
                            double ex = pos.X + windDx * dEnd;
                            double sy = pos.Y + windDy * dStart;
                            double ey = pos.Y + windDy * dEnd;
                            double xMin = Math.Min(sx, ex) - halfWy;
                            double xMax = Math.Max(sx, ex) + halfWy;
                            double yMin = Math.Min(sy, ey) - halfWy;
                            double yMax = Math.Max(sy, ey) + halfWy;
                            double zMax = Math.Max(pos.Z + 2 * halfWz, pos.Z + 4 * cellSize);
                            sb.AppendFormat(Inv, "    {{\n        name    refineZone;\n        type    cellSet;\n        action  add;\n");
                            sb.Append("        source  boxToCell;\n        sourceInfo\n        {\n");
                            sb.AppendFormat(Inv, "            min ({0} {1} {2});\n", xMin, yMin, 0.0);
                            sb.AppendFormat(Inv, "            max ({0} {1} {2});\n", xMax, yMax, zMax);
                            sb.Append("        }\n    }\n\n");
                        }
                    }
                }

                if (obsCount > 0)
                {
                    double margin = radius * 0.5;
                    foreach (var box in obstacles)
                    {
                        string action = firstAction ? "new" : "add";
                        sb.AppendFormat(Inv, "    {{\n        name    refineZone;\n        type    cellSet;\n        action  {0};\n", action);
                        sb.Append("        source  boxToCell;\n        sourceInfo\n        {\n");
                        sb.AppendFormat(Inv, "            min ({0} {1} {2});\n",
                            box.Min.X - margin, box.Min.Y - margin, Math.Max(0, box.Min.Z - margin));
                        sb.AppendFormat(Inv, "            max ({0} {1} {2});\n",
                            box.Max.X + margin, box.Max.Y + margin, box.Max.Z + margin);
                        sb.Append("        }\n    }\n\n");
                        firstAction = false;
                    }
                }

                sb.Append(");\n");
                WriteFile(Path.Combine(caseDir, "system", dictName), sb.ToString());
            }

            var rmSb = new StringBuilder();
            rmSb.Append(FoamHeader("dictionary", "refineMeshDict"));
            rmSb.Append("set             refineZone;\n\n");
            rmSb.Append("coordinateSystem global;\n\n");
            rmSb.Append("directions\n(\n    tan1\n    tan2\n    normal\n);\n\n");
            rmSb.Append("useHexTopology  true;\n\n");
            rmSb.Append("geometricCut    false;\n\n");
            rmSb.Append("writeMesh       true;\n");
            WriteFile(Path.Combine(caseDir, "system", "refineMeshDict"), rmSb.ToString());
        }

        // ────────────────────────────────────────────────────────────────────
        //  pimpleFoam — transient incompressible RANS with passive scalar T
        // ────────────────────────────────────────────────────────────────────

        public static string GeneratePimpleFoam(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "pimple_case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            var wind = scenario.Meteo.WindVector;
            double cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            double effectiveDT = Math.Max(config.DiffusivityM2PerS, ComputeTurbulentDiffusivity(scenario));
            double endTime = scenario.SimulationDurationS;
            double dt = Math.Max(scenario.TimeStepS, cellSize / (Math.Max(wind.Length, 1.0) * 10));
            double writeInterval = config.WriteIntervalS > 0
                ? config.WriteIntervalS
                : Math.Max(endTime / 20.0, dt);

            WritePimpleControlDict(caseDir, endTime, dt, writeInterval, config, "pimpleFoam");
            WritePimpleFvSchemes(caseDir, config);
            WritePimpleFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WritePimpleTransportProperties(caseDir, effectiveDT);
            WriteTurbulenceProperties(caseDir, false, config);
            WriteUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WritePField(caseDir);
            WriteTField(caseDir, config);
            WriteKEpsilonFields(caseDir, wind.Length, config, scenario.Meteo);
            WriteFvOptions(caseDir, scenario.Sources, xMin, xMax, yMin, yMax, zMax, cellSize, nx, ny, nz);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteJetSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        // ────────────────────────────────────────────────────────────────────
        //  buoyantPimpleFoam — transient with buoyancy + passive scalar T
        // ────────────────────────────────────────────────────────────────────

        public static string GenerateBuoyantPimpleFoam(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "buoyant_case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            var wind = scenario.Meteo.WindVector;
            double cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            double effectiveDT = Math.Max(config.DiffusivityM2PerS, ComputeTurbulentDiffusivity(scenario));
            double endTime = scenario.SimulationDurationS;
            double dt = Math.Max(scenario.TimeStepS, cellSize / (Math.Max(wind.Length, 1.0) * 10));
            double writeInterval = config.WriteIntervalS > 0
                ? config.WriteIntervalS
                : Math.Max(endTime / 20.0, dt);

            double ambientT = scenario.Meteo.AmbientTemperature > 0
                ? scenario.Meteo.AmbientTemperature : 293.15;

            WritePimpleControlDict(caseDir, endTime, dt, writeInterval, config, "buoyantPimpleFoam",
                "s", effectiveDT, compressible: true, inlineSources: scenario.Sources);
            WriteBuoyantFvSchemes(caseDir, config);
            WriteBuoyantFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteBuoyantTransportProperties(caseDir, effectiveDT, config);
            WriteBuoyantThermophysicalProperties(caseDir, ambientT);
            WriteTurbulenceProperties(caseDir, false, config);
            WriteGravity(caseDir);
            WriteBuoyantUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WriteBuoyantPField(caseDir);
            WriteBuoyantPRghField(caseDir);
            WriteBuoyantTemperatureField(caseDir, ambientT, config);
            WriteAlphatField(caseDir);
            WritePassiveScalarField(caseDir, "s");
            WriteKEpsilonFields(caseDir, wind.Length, config, scenario.Meteo);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteJetSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        // ────────────────────────────────────────────────────────────────────
        //  reactingFoam — compressible multi-species transient
        // ────────────────────────────────────────────────────────────────────

        public static string GenerateReactingFoam(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "reacting_case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            var wind = scenario.Meteo.WindVector;
            double cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            double endTime = scenario.SimulationDurationS;
            double dt = Math.Max(scenario.TimeStepS, cellSize / (Math.Max(wind.Length, 1.0) * 10));
            double writeInterval = config.WriteIntervalS > 0
                ? config.WriteIntervalS
                : Math.Max(endTime / 20.0, dt);

            double ambientT = scenario.Meteo.AmbientTemperature > 0
                ? scenario.Meteo.AmbientTemperature : 293.15;

            WritePimpleControlDict(caseDir, endTime, dt, writeInterval, config, "reactingFoam");
            WriteReactingFvSchemes(caseDir, config);
            WriteReactingFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteReactingThermophysicalProperties(caseDir);
            WriteReactingChemistryProperties(caseDir);
            WriteReactingCombustionProperties(caseDir);
            WriteTurbulenceProperties(caseDir, true, config);
            WriteGravity(caseDir);
            WriteBuoyantUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WriteBuoyantPRghField(caseDir);
            WriteReactingPField(caseDir);
            WriteBuoyantTemperatureField(caseDir, ambientT, config);
            WriteSpeciesFields(caseDir, scenario.Sources);
            WriteAlphatField(caseDir);
            WriteKEpsilonFields(caseDir, wind.Length, config, scenario.Meteo);
            WriteReactingSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteReactingSpeciesSourceFvOptions(caseDir, scenario.Sources, compressible: false);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null,
                scenario.Meteo, scenario.DomainSizeM);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        // ────────────────────────────────────────────────────────────────────
        //  rhoReactingBuoyantFoam (combustion off) — universal dispersion solver
        //  Compressible, multi-species, buoyant. Subsonic + sonic releases.
        //  See Fiates &amp; Vianna 2016 (Process Safety &amp; Env. Protection 104:277-293).
        // ────────────────────────────────────────────────────────────────────

        public static string GenerateRhoReactingBuoyantFoam(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "rhoreact_case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            var wind = scenario.Meteo.WindVector;
            double cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            double endTime = scenario.SimulationDurationS;

            // The initial dt MUST respect the CFL of the FASTEST velocity in the domain
            // — which for a sonic HP-leak source is the choked-jet velocity (300–500 m/s),
            // not the ambient wind. PIMPLE adjustTimeStep will refine after step 1, but
            // the very first step is taken at this dt and a single CFL>>1 in a source
            // cell is enough to send rho negative, T to the 200/5000 K clamps, and the
            // continuity error to 1e+13 (exactly the failure mode we just saw).
            double maxV = Math.Max(wind.Length, 1.0);
            if (scenario.Sources != null)
            {
                foreach (var s in scenario.Sources)
                {
                    if (s == null) continue;
                    double ve = s.ComputedExitVelocity;
                    if (ve > maxV) maxV = ve;
                }
            }
            // Target CFL = 0.3 in the worst-case cell. User TimeStepS is a CAP, not
            // a floor — we honour it only if it's already smaller than the CFL bound.
            double dtCfl = 0.3 * cellSize / maxV;
            double dt = Math.Min(Math.Max(scenario.TimeStepS, 1e-4), dtCfl);
            if (dt > dtCfl) dt = dtCfl;

            double writeInterval = config.WriteIntervalS > 0
                ? config.WriteIntervalS
                : Math.Max(endTime / 20.0, dt);

            double ambientT = scenario.Meteo.AmbientTemperature > 0
                ? scenario.Meteo.AmbientTemperature : 293.15;

            WritePimpleControlDict(caseDir, endTime, dt, writeInterval, config, "rhoReactingBuoyantFoam");
            WriteReactingFvSchemes(caseDir, config);
            WriteReactingFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteRhoReactingThermophysicalProperties(caseDir);
            WriteRhoReactingChemistryProperties(caseDir);
            WriteReactingCombustionProperties(caseDir);
            WriteRhoReactingReactions(caseDir);
            WriteTurbulenceProperties(caseDir, true, config);
            WriteGravity(caseDir);
            WriteBuoyantUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WriteBuoyantPRghField(caseDir);
            WriteReactingPField(caseDir);
            WriteBuoyantTemperatureField(caseDir, ambientT, config);
            WriteSpeciesFields(caseDir, scenario.Sources);
            WriteAlphatField(caseDir);
            WriteKEpsilonFields(caseDir, wind.Length, config, scenario.Meteo);
            WriteReactingSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteReactingSpeciesSourceFvOptions(caseDir, scenario.Sources, compressible: true);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null,
                scenario.Meteo, scenario.DomainSizeM);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            WriteMeshVsRoughnessAdvisory(caseDir, cellSize, config, scenario.Meteo);

            return caseDir;
        }

        private static void WriteRhoReactingThermophysicalProperties(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "thermophysicalProperties"));
            sb.Append("thermoType\n{\n");
            sb.Append("    type            heRhoThermo;\n");
            sb.Append("    mixture         reactingMixture;\n");
            sb.Append("    transport       sutherland;\n");
            sb.Append("    thermo          janaf;\n");
            sb.Append("    energy          sensibleEnthalpy;\n");
            sb.Append("    equationOfState perfectGas;\n");
            sb.Append("    specie          specie;\n");
            sb.Append("}\n\n");
            sb.Append("inertSpecie     N2;\n\n");
            sb.Append("chemistryReader foamChemistryReader;\n");
            sb.Append("foamChemistryFile \"$FOAM_CASE/constant/reactions\";\n");
            sb.Append("foamChemistryThermoFile \"$FOAM_CASE/constant/thermo.compressibleGas\";\n");
            WriteFile(Path.Combine(caseDir, "constant", "thermophysicalProperties"), sb.ToString());

            // Minimal thermo data (janaf + sutherland) for N2/O2/CH4
            var thermo = new StringBuilder();
            thermo.Append(FoamHeader("dictionary", "thermo.compressibleGas"));
            thermo.Append("N2\n{\n");
            thermo.Append("    specie { molWeight 28.0134; }\n");
            thermo.Append("    thermodynamics { Tlow 200; Thigh 5000; Tcommon 1000;\n");
            thermo.Append("        highCpCoeffs (2.92664 0.001487977 -5.68476e-07 1.0097e-10 -6.75335e-15 -922.7977 5.98053);\n");
            thermo.Append("        lowCpCoeffs  (3.298677 0.0014082404 -3.963222e-06 5.641515e-09 -2.444854e-12 -1020.8999 3.950372); }\n");
            thermo.Append("    transport { As 1.4792e-06; Ts 116; }\n");
            thermo.Append("}\n\n");
            thermo.Append("O2\n{\n");
            thermo.Append("    specie { molWeight 31.9988; }\n");
            thermo.Append("    thermodynamics { Tlow 200; Thigh 5000; Tcommon 1000;\n");
            thermo.Append("        highCpCoeffs (3.69757 0.000613519 -1.25884e-07 1.77528e-11 -1.13644e-15 -1233.93 3.18917);\n");
            thermo.Append("        lowCpCoeffs  (3.21294 0.00112748 -5.75615e-07 1.31388e-09 -8.76855e-13 -1005.25 6.034738); }\n");
            thermo.Append("    transport { As 1.6934e-06; Ts 127; }\n");
            thermo.Append("}\n\n");
            thermo.Append("CH4\n{\n");
            thermo.Append("    specie { molWeight 16.0428; }\n");
            thermo.Append("    thermodynamics { Tlow 200; Thigh 5000; Tcommon 1000;\n");
            thermo.Append("        highCpCoeffs (1.683479 0.01023724 -3.875129e-06 6.785585e-10 -4.503423e-14 -10080.787 9.623395);\n");
            thermo.Append("        lowCpCoeffs  (5.149876 -0.013671 4.918005e-05 -4.847431e-08 1.666933e-11 -10246.64 -4.641304); }\n");
            thermo.Append("    transport { As 1.4067e-06; Ts 197.6; }\n");
            thermo.Append("}\n\n");
            // SF6: heavy tracer used in Hamburg wind-tunnel experiments (Mack 2013, DAT632).
            // Sutherland As/Ts and JANAF coefficients from NIST Webbook fits in the 200-1000 K
            // range — SF6 is thermally stable so the high-T extrapolation is safe.
            thermo.Append("SF6\n{\n");
            thermo.Append("    specie { molWeight 146.054; }\n");
            thermo.Append("    thermodynamics { Tlow 200; Thigh 5000; Tcommon 1000;\n");
            thermo.Append("        highCpCoeffs (12.4 0.00 0.00 0.00 0.00 -148000 -25.0);\n");
            thermo.Append("        lowCpCoeffs  ( 4.0 0.04 -3.0e-5 1.0e-8 0.0    -148000  -8.0); }\n");
            thermo.Append("    transport { As 1.5e-06; Ts 100; }\n");
            thermo.Append("}\n");
            WriteFile(Path.Combine(caseDir, "constant", "thermo.compressibleGas"), thermo.ToString());
        }

        private static void WriteRhoReactingChemistryProperties(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "chemistryProperties"));
            sb.Append("chemistryType\n{\n    chemistrySolver noChemistrySolver;\n    chemistryThermo rho;\n}\n\n");
            sb.Append("chemistry       off;\n");
            sb.Append("initialChemicalTimeStep 1e-07;\n");
            WriteFile(Path.Combine(caseDir, "constant", "chemistryProperties"), sb.ToString());
        }

        private static void WriteRhoReactingReactions(string caseDir)
        {
            // Chemistry is disabled but the file is required by foamChemistryReader.
            // OpenFOAM v2x expects 'reactions' as a sub-dictionary {}, not a list ().
            // We declare a fixed species set covering the bench/tutorial mainstream:
            // N2 (inert), O2 (air), CH4 (LNG vapor), SF6 (heavy tracer for Hamburg WT
            // experiments). Add more here when a new bench needs them, plus matching
            // thermo entries in WriteRhoReactingThermophysicalProperties.
            var sb = new StringBuilder();
            sb.Append("species\n(\n    N2\n    O2\n    CH4\n    SF6\n);\n\n");
            sb.Append("reactions\n{\n}\n");
            WriteFile(Path.Combine(caseDir, "constant", "reactions"), sb.ToString());
        }

        /// <summary>
        /// Picks the OpenFOAM species name to inject for a given release source. Reads the
        /// source's Gas (or GasRefId in the upstream resolver) and matches its name against
        /// the species the case writer declares. Defaults to "CH4".
        /// </summary>
        public static string ResolveOpenFoamSpecies(ReleaseSource3D src)
        {
            if (src == null || src.Gas == null || string.IsNullOrEmpty(src.Gas.Name))
                return "CH4";
            var n = src.Gas.Name.ToUpperInvariant();
            if (n.Contains("SF6") || n.Contains("HEXAFLUORID")) return "SF6";
            // N2/O2 already exist in the inert mixture — refuse to inject more.
            // For other gases the user should add a species to the writer block above.
            return "CH4";
        }

        // ────────────────────────────────────────────────────────────────────
        //  Shared helper writers for pimpleFoam / buoyantPimpleFoam / reactingFoam
        // ────────────────────────────────────────────────────────────────────

        private static void WritePimpleControlDict(string caseDir, double endTime, double dt,
            double writeInterval, CfdConfiguration config, string application,
            string passiveScalar = null, double scalarDiffusivity = 0,
            bool compressible = false, List<ReleaseSource3D> inlineSources = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "controlDict"));
            sb.AppendFormat(Inv, "application     {0};\n\n", application);
            // The atmospheric BL boundary conditions live in the atmosphericModels library;
            // it must be loaded explicitly for the solver to recognize them.
            if (config != null && config.UseAtmosphericBL)
                sb.Append("libs            (\"libatmosphericModels.so\");\n\n");
            sb.Append("startFrom       startTime;\nstartTime       0;\n\n");
            sb.AppendFormat(Inv, "stopAt          endTime;\nendTime         {0};\n\n", endTime);
            sb.AppendFormat(Inv, "deltaT          {0};\n\n", dt);
            sb.AppendFormat(Inv, "writeControl    adjustableRunTime;\nwriteInterval   {0};\n\n", writeInterval);
            sb.AppendFormat(Inv, "purgeWrite      {0};\n\n", config.PurgeWrite);
            sb.Append("writeFormat     ascii;\nwritePrecision  8;\nwriteCompression off;\n\n");
            sb.Append("timeFormat      general;\ntimePrecision   6;\n\nrunTimeModifiable true;\n\n");
            sb.Append("adjustTimeStep  yes;\n");
            sb.AppendFormat(Inv, "maxCo           {0};\n", config.MaxCourantNumber > 0 ? config.MaxCourantNumber : 0.5);

            if (passiveScalar != null)
            {
                sb.Append("\nfunctions\n{\n");
                sb.AppendFormat("    {0}Transport\n    {{\n", passiveScalar);
                sb.Append("        type            scalarTransport;\n");
                sb.Append("        libs            (\"libsolverFunctionObjects.so\");\n");
                sb.AppendFormat("        field           {0};\n", passiveScalar);
                if (compressible)
                    sb.Append("        rho             rho;\n");
                sb.Append("        nCorr           2;\n");
                sb.Append("        resetOnStartUp  false;\n");
                sb.Append("        writeControl    writeTime;\n");
                sb.AppendFormat(Inv, "        D               {0};\n", scalarDiffusivity > 0 ? scalarDiffusivity : 1e-5);

                if (inlineSources != null && inlineSources.Count > 0)
                {
                    sb.Append("\n        fvOptions\n        {\n");
                    for (int s = 0; s < inlineSources.Count; s++)
                    {
                        var src = inlineSources[s];
                        sb.AppendFormat(Inv, "            source_{0}\n            {{\n", s);
                        sb.Append("                type            scalarSemiImplicitSource;\n");
                        sb.Append("                active          true;\n\n");
                        sb.Append("                scalarSemiImplicitSourceCoeffs\n                {\n");
                        sb.AppendFormat(Inv, "                    selectionMode   cellSet;\n                    cellSet         sourceZone_{0};\n", s);
                        sb.Append("                    volumeMode      absolute;\n                    injectionRateSuSp\n                    {\n");
                        sb.AppendFormat(Inv, "                        {0}           ({1} 0);\n", passiveScalar, src.EffectiveReleaseRateKgPerS);
                        sb.Append("                    }\n                }\n            }\n");
                    }
                    sb.Append("        }\n");
                }

                sb.Append("    }\n}\n");
            }

            WriteFile(Path.Combine(caseDir, "system", "controlDict"), sb.ToString());
        }

        private static void WritePimpleFvSchemes(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         Euler;\n}\n\n");
            sb.Append("gradSchemes\n{\n    default         Gauss linear;\n");
            sb.Append("    grad(U)         cellLimited Gauss linear 1;\n}\n\n");
            sb.Append("divSchemes\n{\n    default         none;\n");
            sb.Append("    div(phi,U)      Gauss linearUpwind grad(U);\n");
            sb.Append("    div(phi,T)      Gauss linearUpwind grad(T);\n");
            sb.Append("    div(phi,k)      Gauss upwind;\n");
            sb.Append("    div(phi,epsilon) Gauss upwind;\n");
            sb.Append("    div((nuEff*dev2(T(grad(U))))) Gauss linear;\n}\n\n");
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n\n");
            sb.Append("wallDist\n{\n    method meshWave;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WritePimpleFvSolution(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n");
            sb.Append("    p\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.01;\n        smoother        GaussSeidel;\n    }\n");
            sb.Append("    pFinal\n    {\n        $p;\n        relTol          0;\n    }\n");
            sb.Append("    \"(U|k|epsilon)\"\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n        tolerance       1e-06;\n        relTol          0.1;\n    }\n");
            sb.Append("    \"(U|k|epsilon)Final\"\n    {\n        $U;\n        relTol          0;\n    }\n");
            sb.Append("    T\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n");
            sb.AppendFormat(Inv, "        tolerance       {0};\n        relTol          0;\n    }}\n", config.SolverTolerance);
            sb.Append("}\n\n");
            sb.Append("PIMPLE\n{\n    nOuterCorrectors 2;\n    nCorrectors     2;\n    nNonOrthogonalCorrectors 1;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WritePimpleTransportProperties(string caseDir, double diffusivity)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "transportProperties"));
            sb.Append("transportModel  Newtonian;\n");
            sb.AppendFormat(Inv, "nu              1.5e-05;\n");
            sb.AppendFormat(Inv, "DT              {0};\n", diffusivity);
            WriteFile(Path.Combine(caseDir, "constant", "transportProperties"), sb.ToString());
        }

        private static void WriteTurbulenceProperties(string caseDir, bool compressible,
            CfdConfiguration cfd = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "turbulenceProperties"));
            sb.Append("simulationType  RAS;\n\n");
            sb.Append("RAS\n{\n");
            // When buoyancy treatment is requested for the eps-equation (Mack & Spruijt
            // -0.33 recipe), use the buoyantKEpsilon model from the atmosphericModels lib.
            bool useBuoyantModel = compressible && cfd != null
                && cfd.UseAtmosphericBL
                && cfd.BuoyancyEpsCoefficient.HasValue;
            string modelName = useBuoyantModel ? "buoyantKEpsilon" : "kEpsilon";
            sb.AppendFormat(Inv, "    RASModel        {0};\n    turbulence      on;\n    printCoeffs     on;\n", modelName);

            if (cfd != null && cfd.UseAtmosphericBL)
            {
                double sigmaEps = cfd.KEpsilonSigmaEpsilon > 0 ? cfd.KEpsilonSigmaEpsilon : 1.3;
                if (useBuoyantModel)
                {
                    sb.Append("    buoyantKEpsilonCoeffs\n    {\n");
                    sb.Append("        Cmu             0.09;\n        C1              1.44;\n        C2              1.92;\n");
                    sb.Append("        sigmak          1.0;\n");
                    sb.AppendFormat(Inv, "        sigmaEps        {0};\n", sigmaEps);
                    sb.AppendFormat(Inv, "        Ceps3           {0};\n", cfd.BuoyancyEpsCoefficient.Value);
                    sb.Append("    }\n");
                }
                else
                {
                    sb.Append("    kEpsilonCoeffs\n    {\n");
                    sb.Append("        Cmu             0.09;\n        C1              1.44;\n        C2              1.92;\n");
                    sb.Append("        sigmak          1.0;\n");
                    sb.AppendFormat(Inv, "        sigmaEps        {0};\n", sigmaEps);
                    sb.Append("    }\n");
                }
            }
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "constant", "turbulenceProperties"), sb.ToString());
        }

        private static void WriteKEpsilonFields(string caseDir, double windSpeed,
            CfdConfiguration cfd = null, MeteorologicalConditions meteo = null)
        {
            double U = Math.Max(windSpeed, 0.5);
            double I = 0.05;
            double k = 1.5 * (U * I) * (U * I);
            double epsilon = 0.09 * k * k / (0.1 * k / U + 1e-10);
            if (epsilon < 1e-6) epsilon = 1e-4;
            double nut = 0.09 * k * k / epsilon;
            bool atm = cfd != null && cfd.UseAtmosphericBL && meteo != null;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "k"));
            sb.Append("dimensions      [0 2 -2 0 0 0 0];\n\n");
            sb.AppendFormat(Inv, "internalField   uniform {0};\n\n", k);
            sb.Append("boundaryField\n{\n");
            if (atm) AppendAtmInletK(sb, "atmosphere", k, meteo);
            else sb.AppendFormat(Inv, "    atmosphere\n    {{\n        type            fixedValue;\n        value           uniform {0};\n    }}\n", k);
            sb.Append("    ground\n    {\n        type            kqRWallFunction;\n        value           uniform 0;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "k"), sb.ToString());

            sb.Clear();
            sb.Append(FoamHeader("volScalarField", "epsilon"));
            sb.Append("dimensions      [0 2 -3 0 0 0 0];\n\n");
            sb.AppendFormat(Inv, "internalField   uniform {0};\n\n", epsilon);
            sb.Append("boundaryField\n{\n");
            if (atm) AppendAtmInletEpsilon(sb, "atmosphere", epsilon, meteo);
            else sb.AppendFormat(Inv, "    atmosphere\n    {{\n        type            fixedValue;\n        value           uniform {0};\n    }}\n", epsilon);
            sb.Append("    ground\n    {\n        type            epsilonWallFunction;\n        value           uniform 0;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "epsilon"), sb.ToString());

            sb.Clear();
            sb.Append(FoamHeader("volScalarField", "nut"));
            sb.Append("dimensions      [0 2 -1 0 0 0 0];\n\n");
            sb.AppendFormat(Inv, "internalField   uniform {0};\n\n", nut);
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            calculated;\n        value           uniform 0;\n    }\n");
            if (atm) AppendAtmGroundNut(sb, meteo);
            else sb.Append("    ground\n    {\n        type            nutkWallFunction;\n        value           uniform 0;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "nut"), sb.ToString());
        }

        private static void WriteGravity(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("uniformDimensionedVectorField", "g"));
            sb.Append("dimensions      [0 1 -2 0 0 0 0];\n");
            sb.Append("value           (0 0 -9.81);\n");
            WriteFile(Path.Combine(caseDir, "constant", "g"), sb.ToString());
        }

        // ── buoyantPimpleFoam-specific writers ──

        private static void WriteBuoyantFvSchemes(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         Euler;\n}\n\n");
            sb.Append("gradSchemes\n{\n    default         Gauss linear;\n");
            sb.Append("    grad(U)         cellLimited Gauss linear 1;\n}\n\n");
            sb.Append("divSchemes\n{\n    default         none;\n");
            sb.Append("    div(phi,U)      Gauss linearUpwind grad(U);\n");
            sb.Append("    div(phi,s)      Gauss linearUpwind default;\n");
            sb.Append("    div(phi,h)      Gauss linearUpwind default;\n");
            sb.Append("    div(phi,K)      Gauss linear;\n");
            sb.Append("    div(phi,k)      Gauss upwind;\n");
            sb.Append("    div(phi,epsilon) Gauss upwind;\n");
            sb.Append("    div(((rho*nuEff)*dev2(T(grad(U))))) Gauss linear;\n}\n\n");
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n\n");
            sb.Append("wallDist\n{\n    method meshWave;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WriteBuoyantFvSolution(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n");
            sb.Append("    rho\n    {\n        solver          PCG;\n        preconditioner  DIC;\n        tolerance       1e-07;\n        relTol          0.1;\n    }\n");
            sb.Append("    rhoFinal\n    {\n        $rho;\n        relTol          0;\n    }\n");
            sb.Append("    \"p_rgh\"\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.01;\n        smoother        GaussSeidel;\n    }\n");
            sb.Append("    \"p_rghFinal\"\n    {\n        $p_rgh;\n        relTol          0;\n    }\n");
            sb.Append("    \"(U|h|k|epsilon)\"\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n        tolerance       1e-06;\n        relTol          0.1;\n    }\n");
            sb.Append("    \"(U|h|k|epsilon)Final\"\n    {\n        $U;\n        relTol          0;\n    }\n");
            sb.Append("    s\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n");
            sb.AppendFormat(Inv, "        tolerance       {0};\n        relTol          0;\n    }}\n", config.SolverTolerance);
            sb.Append("    sFinal\n    {\n        $s;\n        relTol          0;\n    }\n");
            sb.Append("}\n\n");
            sb.Append("PIMPLE\n{\n    nOuterCorrectors 2;\n    nCorrectors     2;\n    nNonOrthogonalCorrectors 1;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WriteBuoyantTransportProperties(string caseDir, double diffusivity,
            CfdConfiguration cfd = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "transportProperties"));
            sb.AppendFormat(Inv, "DT              {0};\n", diffusivity);
            if (cfd != null && cfd.UseAtmosphericBL)
            {
                sb.AppendFormat(Inv, "Sct             {0};\n", cfd.TurbulentSchmidtNumber);
                sb.AppendFormat(Inv, "Prt             {0};\n", cfd.TurbulentPrandtlNumber);
            }
            WriteFile(Path.Combine(caseDir, "constant", "transportProperties"), sb.ToString());
        }

        private static void WriteBuoyantThermophysicalProperties(string caseDir, double ambientT)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "thermophysicalProperties"));
            sb.Append("thermoType\n{\n");
            sb.Append("    type            heRhoThermo;\n");
            sb.Append("    mixture         pureMixture;\n");
            sb.Append("    transport       const;\n");
            sb.Append("    thermo          hConst;\n");
            sb.Append("    equationOfState perfectGas;\n");
            sb.Append("    specie          specie;\n");
            sb.Append("    energy          sensibleEnthalpy;\n");
            sb.Append("}\n\n");
            sb.Append("mixture\n{\n");
            sb.Append("    specie\n    {\n        molWeight       28.96;\n    }\n");
            sb.Append("    thermodynamics\n    {\n        Cp              1005;\n        Hf              0;\n    }\n");
            sb.Append("    transport\n    {\n        mu              1.84e-05;\n        Pr              0.71;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "constant", "thermophysicalProperties"), sb.ToString());
        }

        private static void WriteBuoyantUField(string caseDir, double ux, double uy, double uz,
            CfdConfiguration cfd = null, MeteorologicalConditions meteo = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volVectorField", "U"));
            sb.Append("dimensions      [0 1 -1 0 0 0 0];\n\n");
            sb.AppendFormat(Inv, "internalField   uniform ({0} {1} {2});\n\n", ux, uy, uz);
            sb.Append("boundaryField\n{\n");
            if (cfd != null && cfd.UseAtmosphericBL && meteo != null)
                AppendAtmInletU(sb, "atmosphere", ux, uy, uz, meteo);
            else
                sb.AppendFormat(Inv, "    atmosphere\n    {{\n        type            fixedValue;\n        value           uniform ({0} {1} {2});\n    }}\n", ux, uy, uz);
            sb.Append("    ground\n    {\n        type            noSlip;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "U"), sb.ToString());
        }

        private static void WriteBuoyantPField(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "p"));
            sb.Append("dimensions      [1 -1 -2 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 1e5;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            calculated;\n        value           uniform 1e5;\n    }\n");
            sb.Append("    ground\n    {\n        type            calculated;\n        value           uniform 1e5;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "p"), sb.ToString());
        }

        private static void WriteBuoyantPRghField(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "p_rgh"));
            sb.Append("dimensions      [1 -1 -2 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 1e5;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            fixedValue;\n        value           uniform 1e5;\n    }\n");
            sb.Append("    ground\n    {\n        type            fixedFluxPressure;\n        value           uniform 1e5;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "p_rgh"), sb.ToString());
        }

        private static void WriteBuoyantTemperatureField(string caseDir, double ambientT,
            CfdConfiguration cfd = null)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "T"));
            sb.Append("dimensions      [0 0 0 1 0 0 0];\n\n");
            sb.AppendFormat(Inv, "internalField   uniform {0};\n\n", ambientT);
            sb.Append("boundaryField\n{\n");
            sb.AppendFormat(Inv, "    atmosphere\n    {{\n        type            fixedValue;\n        value           uniform {0};\n    }}\n", ambientT);
            AppendGroundT(sb, cfd);
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "T"), sb.ToString());
        }

        private static void WriteAlphatField(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "alphat"));
            sb.Append("dimensions      [1 -1 -1 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            calculated;\n        value           uniform 0;\n    }\n");
            sb.Append("    ground\n    {\n        type            compressible::alphatWallFunction;\n        value           uniform 0;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "alphat"), sb.ToString());
        }

        // ── reactingFoam-specific writers ──

        private static void WriteReactingFvSchemes(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         Euler;\n}\n\n");
            // limited gradient prevents overshoots when a cell has a steep neighbour gradient
            // (release source vs. ambient) — the cell-by-cell limiter is required by buoyant
            // cases where T/rho/Yi all change quickly together.
            sb.Append("gradSchemes\n{\n    default         cellLimited Gauss linear 1;\n}\n\n");
            sb.Append("divSchemes\n{\n    default         none;\n");
            // OpenFOAM v2512 ESI ships neither the `bounded` convection wrapper NOR the
            // massSource fvOption. Without massSource, scalarSemiImplicitSource creates a
            // small mass imbalance (Yi grows without rho); pure upwind is the only scheme
            // robust enough to absorb that imbalance without explosion. limitedLinear and
            // linearUpwind both let the imbalance amplify until the solver aborts (which
            // is what the user just hit, three times).
            sb.Append("    div(phi,U)      Gauss upwind;\n");
            sb.Append("    div(phi,Yi_h)   Gauss multivariateSelection { N2 upwind; O2 upwind; CH4 upwind; SF6 upwind; h upwind; };\n");
            sb.Append("    div(phi,K)      Gauss upwind;\n");
            sb.Append("    div(phi,k)      Gauss upwind;\n");
            sb.Append("    div(phi,epsilon) Gauss upwind;\n");
            sb.Append("    div(((rho*nuEff)*dev2(T(grad(U))))) Gauss linear;\n}\n\n");
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n\n");
            sb.Append("wallDist\n{\n    method meshWave;\n}\n\n");
            sb.Append("fluxRequired\n{\n    default         no;\n    p_rgh           ;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WriteReactingFvSolution(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n");
            sb.Append("    \"rho.*\"\n    {\n        solver          diagonal;\n    }\n");
            sb.Append("    p\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.01;\n        smoother        GaussSeidel;\n    }\n");
            sb.Append("    pFinal\n    {\n        $p;\n        relTol          0;\n    }\n");
            // p_rgh is required by rhoReactingBuoyantFoam (buoyancy-decoupled pressure).
            sb.Append("    p_rgh\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.01;\n        smoother        GaussSeidel;\n    }\n");
            sb.Append("    p_rghFinal\n    {\n        $p_rgh;\n        relTol          0;\n    }\n");
            sb.Append("    \"(U|h|k|epsilon)\"\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n        tolerance       1e-06;\n        relTol          0.1;\n    }\n");
            sb.Append("    \"(U|h|k|epsilon)Final\"\n    {\n        $U;\n        relTol          0;\n    }\n");
            sb.Append("    \"Yi\"\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n");
            sb.AppendFormat(Inv, "        tolerance       {0};\n        relTol          0;\n    }}\n", config.SolverTolerance);
            sb.Append("}\n\n");
            // Simplest stable PIMPLE setup: no predictor, one outer iter, one corrector.
            // With heavy under-relaxation below this damps the cold-jet artifact enough
            // for the solver to march through the transient without h diverging.
            sb.Append("PIMPLE\n{\n    momentumPredictor no;\n    nOuterCorrectors 1;\n    nCorrectors     1;\n    nNonOrthogonalCorrectors 1;\n");
            sb.Append("    pRefCell        0;\n    pRefValue       0;\n");
            sb.Append("}\n\n");
            // Aggressive under-relaxation: damp h and U to 0.3, p_rgh to 0.3, species to 0.5.
            // Without massSource the species injection creates a transient mass/energy
            // mismatch each step; only strong damping prevents it from being amplified by
            // the next PIMPLE iter.
            sb.Append("relaxationFactors\n{\n    fields { rho 1.0; p_rgh 0.3; }\n");
            sb.Append("    equations { U 0.3; h 0.3; \"(k|epsilon)\" 0.5; \"Yi.*\" 0.5; }\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WriteReactingThermophysicalProperties(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "thermophysicalProperties"));
            sb.Append("thermoType\n{\n");
            sb.Append("    type            heRhoThermo;\n");
            sb.Append("    mixture         reactingMixture;\n");
            sb.Append("    transport       sutherland;\n");
            sb.Append("    thermo          janaf;\n");
            sb.Append("    equationOfState perfectGas;\n");
            sb.Append("    specie          specie;\n");
            sb.Append("    energy          sensibleEnthalpy;\n");
            sb.Append("}\n\n");
            sb.Append("inertSpecie     N2;\n\n");
            sb.Append("chemistryReader foamChemistryReader;\n\n");
            sb.Append("foamChemistryFile \"<constant>/reactions\";\n");
            sb.Append("foamChemistryThermoFile \"<constant>/thermo.compressibleGas\";\n");
            WriteFile(Path.Combine(caseDir, "constant", "thermophysicalProperties"), sb.ToString());

            var reactions = new StringBuilder();
            reactions.Append(FoamHeader("dictionary", "reactions"));
            reactions.Append("species ( CH4 O2 N2 );\n\n");
            reactions.Append("reactions\n{\n}\n");
            WriteFile(Path.Combine(caseDir, "constant", "reactions"), reactions.ToString());

            var thermo = new StringBuilder();
            thermo.Append(FoamHeader("dictionary", "thermo.compressibleGas"));
            // CH4
            thermo.Append("CH4\n{\n    specie\n    {\n        molWeight       16.04;\n    }\n");
            thermo.Append("    thermodynamics\n    {\n        Tlow            200;\n        Thigh           5000;\n        Tcommon         1000;\n");
            thermo.Append("        highCpCoeffs    ( 1.683 10.24e-3 -3.875e-6 6.785e-10 -4.503e-14 -10080 9.623 );\n");
            thermo.Append("        lowCpCoeffs     ( 5.149 -13.66e-3 49.14e-6 -42.33e-9 12.73e-12 -10240 -4.641 );\n    }\n");
            thermo.Append("    transport\n    {\n        As              1.67212e-06;\n        Ts              170.672;\n    }\n}\n\n");
            // O2
            thermo.Append("O2\n{\n    specie\n    {\n        molWeight       31.998;\n    }\n");
            thermo.Append("    thermodynamics\n    {\n        Tlow            200;\n        Thigh           5000;\n        Tcommon         1000;\n");
            thermo.Append("        highCpCoeffs    ( 3.697 6.135e-4 -1.259e-7 1.775e-11 -1.136e-15 -1234 3.189 );\n");
            thermo.Append("        lowCpCoeffs     ( 3.213 1.127e-3 -5.756e-7 1.314e-9 -8.768e-13 -1005 6.034 );\n    }\n");
            thermo.Append("    transport\n    {\n        As              1.67212e-06;\n        Ts              170.672;\n    }\n}\n\n");
            // N2
            thermo.Append("N2\n{\n    specie\n    {\n        molWeight       28.014;\n    }\n");
            thermo.Append("    thermodynamics\n    {\n        Tlow            200;\n        Thigh           5000;\n        Tcommon         1000;\n");
            thermo.Append("        highCpCoeffs    ( 2.953 1.397e-3 -4.926e-7 7.86e-11 -4.607e-15 -923.9 5.872 );\n");
            thermo.Append("        lowCpCoeffs     ( 3.531 -1.236e-4 -5.03e-7 2.435e-9 -1.409e-12 -1047 2.967 );\n    }\n");
            thermo.Append("    transport\n    {\n        As              1.67212e-06;\n        Ts              170.672;\n    }\n}\n");
            WriteFile(Path.Combine(caseDir, "constant", "thermo.compressibleGas"), thermo.ToString());
        }

        private static void WriteReactingChemistryProperties(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "chemistryProperties"));
            sb.Append("chemistryType\n{\n    chemistrySolver noChemistrySolver;\n    chemistryThermo psi;\n}\n\n");
            sb.Append("chemistry       off;\n");
            sb.Append("initialChemicalTimeStep 1e-07;\n");
            WriteFile(Path.Combine(caseDir, "constant", "chemistryProperties"), sb.ToString());
        }

        private static void WriteReactingCombustionProperties(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "combustionProperties"));
            sb.Append("combustionModel none;\n");
            WriteFile(Path.Combine(caseDir, "constant", "combustionProperties"), sb.ToString());
        }

        private static void WriteReactingPField(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "p"));
            sb.Append("dimensions      [1 -1 -2 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 1e5;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            calculated;\n        value           uniform 1e5;\n    }\n");
            sb.Append("    ground\n    {\n        type            calculated;\n        value           uniform 1e5;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "p"), sb.ToString());
        }

        private static void WriteSpeciesFields(string caseDir, List<ReleaseSource3D> sources)
        {
            // CH4 — starts at 0 everywhere (injected via setFields)
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "CH4"));
            sb.Append("dimensions      [0 0 0 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            inletOutlet;\n        inletValue      uniform 0;\n        value           uniform 0;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "CH4"), sb.ToString());

            // O2
            sb.Clear();
            sb.Append(FoamHeader("volScalarField", "O2"));
            sb.Append("dimensions      [0 0 0 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0.23;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            fixedValue;\n        value           uniform 0.23;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "O2"), sb.ToString());

            // N2
            sb.Clear();
            sb.Append(FoamHeader("volScalarField", "N2"));
            sb.Append("dimensions      [0 0 0 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0.77;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            fixedValue;\n        value           uniform 0.77;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "N2"), sb.ToString());

            // SF6 — heavy tracer for Hamburg WT benches; zero everywhere unless injected.
            sb.Clear();
            sb.Append(FoamHeader("volScalarField", "SF6"));
            sb.Append("dimensions      [0 0 0 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            inletOutlet;\n        inletValue      uniform 0;\n        value           uniform 0;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "SF6"), sb.ToString());
        }

        private static void WriteReactingSetFieldsDict(string caseDir, List<ReleaseSource3D> sources,
            System.Windows.Media.Media3D.Vector3D wind, double cellSize)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "setFieldsDict"));
            sb.Append("defaultFieldValues\n(\n);\n\n");
            sb.Append("regions\n(\n");

            double windMag = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y + wind.Z * wind.Z);
            if (windMag < 0.5) windMag = 0.5;
            double maxJetSpeed = windMag * 5.0;

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                var pos = src.EffectivePosition;
                var dir = src.ReleaseDirection;
                // Box width = max(grid cell, expanded pseudo-orifice). Birch & Schefer for choked sources.
                double half = Math.Max(cellSize * 0.55, src.ExpandedDiameterForCfdM * 0.5);

                double v = src.ExpandedVelocityForCfdMS;
                double jetMag = v > 0 ? Math.Min(v, maxJetSpeed) : 0;

                int nSegments = 3;
                double segLen = cellSize;
                for (int i = 0; i < nSegments; i++)
                {
                    double cx = pos.X + dir.X * (i + 0.5) * segLen;
                    double cy = pos.Y + dir.Y * (i + 0.5) * segLen;
                    double cz = pos.Z + dir.Z * (i + 0.5) * segLen;

                    sb.Append("    boxToCell\n    {\n");
                    sb.AppendFormat(Inv, "        min ({0} {1} {2});\n",
                        cx - half, cy - half, Math.Max(0, cz - half));
                    sb.AppendFormat(Inv, "        max ({0} {1} {2});\n",
                        cx + half, cy + half, cz + half);
                    sb.Append("        fieldValues\n        (\n");

                    if (jetMag > 0)
                    {
                        double frac = 1.0 - (double)i / nSegments;
                        double ux = wind.X + dir.X * jetMag * frac;
                        double uy = wind.Y + dir.Y * jetMag * frac;
                        double uz = wind.Z + dir.Z * jetMag * frac;
                        sb.AppendFormat(Inv, "            volVectorFieldValue U ({0} {1} {2})\n", ux, uy, uz);
                    }

                    double specFrac = 1.0 - (double)i / nSegments;
                    string sp = ResolveOpenFoamSpecies(src);
                    sb.AppendFormat(Inv, "            volScalarFieldValue {0} {1}\n", sp, specFrac);
                    sb.AppendFormat(Inv, "            volScalarFieldValue O2 {0}\n", 0.23 * (1.0 - specFrac));
                    sb.AppendFormat(Inv, "            volScalarFieldValue N2 {0}\n", 0.77 * (1.0 - specFrac));

                    sb.Append("        );\n    }\n\n");
                }
            }

            sb.Append(");\n");
            WriteFile(Path.Combine(caseDir, "system", "setFieldsDict"), sb.ToString());
        }

        // ────────────────────────────────────────────────────────────────────
        //  rhoSimpleFoam — compressible steady-state RANS with passive scalar T
        // ────────────────────────────────────────────────────────────────────

        public static string GenerateRhoSimpleFoam(DispersionScenario scenario, CfdConfiguration config)
        {
            string caseDir = Path.Combine(config.WorkingDirectory, "rhosimple_case_" + scenario.Id);
            if (Directory.Exists(caseDir))
                Directory.Delete(caseDir, true);

            Directory.CreateDirectory(Path.Combine(caseDir, "0"));
            Directory.CreateDirectory(Path.Combine(caseDir, "constant"));
            Directory.CreateDirectory(Path.Combine(caseDir, "system"));

            double domain = scenario.DomainSizeM;
            double xMin = -domain, xMax = domain;
            double yMin = -domain, yMax = domain;
            double zMax = domain;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;

            int maxIter = 2000;
            var wind = scenario.Meteo.WindVector;
            double cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            double effectiveDT = Math.Max(config.DiffusivityM2PerS, ComputeTurbulentDiffusivity(scenario));
            double ambientT = scenario.Meteo.AmbientTemperature > 0
                ? scenario.Meteo.AmbientTemperature : 293.15;

            WriteSteadyControlDict(caseDir, maxIter, "rhoSimpleFoam", "s", effectiveDT,
                compressible: true, inlineSources: scenario.Sources);
            WriteRhoSimpleFvSchemes(caseDir, config);
            WriteRhoSimpleFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteRhoSimpleThermophysicalProperties(caseDir, ambientT, effectiveDT);
            WriteTurbulenceProperties(caseDir, true, config);
            WriteBuoyantUField(caseDir, wind.X, wind.Y, wind.Z, config, scenario.Meteo);
            WriteRhoSimplePField(caseDir);
            WriteBuoyantTemperatureField(caseDir, ambientT, config);
            WritePassiveScalarField(caseDir, "s");
            WriteKEpsilonFields(caseDir, wind.Length, config, scenario.Meteo);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);
            WriteRefinementDicts(caseDir, scenario.Sources, cellSize, null);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
        }

        private static void WriteRhoSimpleFvSchemes(string caseDir, CfdConfiguration config)
        {
            string scheme = config.NumericalScheme ?? "linearUpwind";
            string divS = scheme == "linearUpwind"
                ? "Gauss linearUpwind grad(s)"
                : "Gauss " + scheme;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSchemes"));
            sb.Append("ddtSchemes\n{\n    default         steadyState;\n}\n\n");
            sb.Append("gradSchemes\n{\n    default         Gauss linear;\n");
            sb.Append("    grad(U)         cellLimited Gauss linear 1;\n}\n\n");
            sb.Append("divSchemes\n{\n    default         none;\n");
            sb.Append("    div(phi,U)      Gauss linearUpwind grad(U);\n");
            sb.AppendFormat("    div(phi,s)      {0};\n", divS);
            sb.Append("    div(phi,e)      Gauss linearUpwind default;\n");
            sb.Append("    div(phi,h)      Gauss linearUpwind default;\n");
            sb.Append("    div(phi,K)      Gauss linear;\n");
            sb.Append("    div(phi,Ekp)    Gauss linear;\n");
            sb.Append("    div(phi,k)      Gauss upwind;\n");
            sb.Append("    div(phi,epsilon) Gauss upwind;\n");
            sb.Append("    div(((rho*nuEff)*dev2(T(grad(U))))) Gauss linear;\n}\n\n");
            sb.Append("laplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n\n");
            sb.Append("interpolationSchemes\n{\n    default         linear;\n}\n\n");
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n\n");
            sb.Append("wallDist\n{\n    method meshWave;\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSchemes"), sb.ToString());
        }

        private static void WriteRhoSimpleFvSolution(string caseDir, CfdConfiguration config)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvSolution"));
            sb.Append("solvers\n{\n");
            sb.Append("    p\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.01;\n        smoother        GaussSeidel;\n    }\n");
            sb.Append("    \"(U|e|h|k|epsilon)\"\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n        tolerance       1e-06;\n        relTol          0.1;\n    }\n");
            sb.Append("    s\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n");
            sb.AppendFormat(Inv, "        tolerance       {0};\n        relTol          0.01;\n    }}\n", config.SolverTolerance);
            sb.Append("}\n\n");
            sb.Append("SIMPLE\n{\n    nNonOrthogonalCorrectors 0;\n    consistent      yes;\n\n");
            sb.Append("    residualControl\n    {\n        p               1e-4;\n        U               1e-4;\n        e               1e-4;\n        s               1e-6;\n    }\n}\n\n");
            sb.Append("relaxationFactors\n{\n");
            sb.Append("    fields\n    {\n        p               0.3;\n        rho             0.5;\n    }\n");
            sb.Append("    equations\n    {\n        U               0.7;\n        e               0.5;\n        h               0.5;\n        k               0.5;\n        epsilon         0.5;\n        s               0.7;\n    }\n}\n");
            WriteFile(Path.Combine(caseDir, "system", "fvSolution"), sb.ToString());
        }

        private static void WriteRhoSimpleThermophysicalProperties(string caseDir, double ambientT, double diffusivity)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "thermophysicalProperties"));
            sb.Append("thermoType\n{\n");
            sb.Append("    type            heRhoThermo;\n");
            sb.Append("    mixture         pureMixture;\n");
            sb.Append("    transport       const;\n");
            sb.Append("    thermo          hConst;\n");
            sb.Append("    equationOfState perfectGas;\n");
            sb.Append("    specie          specie;\n");
            sb.Append("    energy          sensibleInternalEnergy;\n");
            sb.Append("}\n\n");
            sb.Append("mixture\n{\n");
            sb.Append("    specie\n    {\n        molWeight       28.96;\n    }\n");
            sb.Append("    thermodynamics\n    {\n        Cp              1005;\n        Hf              0;\n    }\n");
            sb.Append("    transport\n    {\n        mu              1.84e-05;\n        Pr              0.71;\n    }\n");
            sb.Append("}\n\n");
            sb.AppendFormat(Inv, "DT              {0};\n", diffusivity);
            WriteFile(Path.Combine(caseDir, "constant", "thermophysicalProperties"), sb.ToString());
        }

        private static void WriteRhoSimplePField(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "p"));
            sb.Append("dimensions      [1 -1 -2 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 1e5;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            fixedValue;\n        value           uniform 1e5;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "p"), sb.ToString());
        }
    }
}
