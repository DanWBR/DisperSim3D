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

            double writeInterval = config.WriteIntervalS > 0
                ? config.WriteIntervalS
                : Math.Max(1.0, scenario.SimulationDurationS / 100.0);

            var wind = scenario.Meteo.WindVector;

            double maxExitVelocity = 0;
            foreach (var src in scenario.Sources)
            {
                double v = src.ComputedExitVelocity;
                if (v > maxExitVelocity) maxExitVelocity = v;
            }
            if (maxExitVelocity > 0)
            {
                double maxV = Math.Max(windSpeed, maxExitVelocity);
                maxDt = cellSize / Math.Max(maxV, 0.5) * 0.35;
                dt = Math.Min(dt, maxDt);
            }

            WriteControlDict(caseDir, scenario.SimulationDurationS, dt, writeInterval, config);
            WriteFvSchemes(caseDir, config);
            WriteFvSolution(caseDir, config);
            WriteBlockMeshDict(caseDir, xMin, xMax, yMin, yMax, zMax, nx, ny, nz);
            WriteTransportProperties(caseDir, config.DiffusivityM2PerS);
            WriteUField(caseDir, wind.X, wind.Y, wind.Z);
            WriteTField(caseDir);
            WriteFvOptions(caseDir, scenario.Sources, xMin, xMax, yMin, yMax, zMax, cellSize, nx, ny, nz);
            WriteTopoSetDict(caseDir, scenario.Sources, cellSize);
            WriteSetFieldsDict(caseDir, scenario.Sources, wind, cellSize);

            if (config.NumberOfProcessors > 1)
                WriteDecomposeParDict(caseDir, config.NumberOfProcessors);

            return caseDir;
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
            WriteUField(caseDir, wind.X, wind.Y, wind.Z);
            WritePField(caseDir);

            if (obstacles != null && obstacles.Count > 0)
                WriteWindObstacles(caseDir, obstacles);

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
            sb.Append("    div(phi,U)      bounded Gauss linearUpwind grad(U);\n");
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
            sb.Append("    p\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.1;\n        smoother        GaussSeidel;\n    }\n");
            sb.Append("    U\n    {\n        solver          PBiCGStab;\n        preconditioner  DILU;\n        tolerance       1e-06;\n        relTol          0.1;\n    }\n");
            sb.Append("}\n\n");
            sb.Append("SIMPLE\n{\n    nNonOrthogonalCorrectors 0;\n");
            sb.Append("    consistent      yes;\n\n");
            sb.Append("    residualControl\n    {\n        p               1e-4;\n        U               1e-4;\n    }\n}\n\n");
            sb.Append("relaxationFactors\n{\n");
            sb.Append("    fields\n    {\n        p               0.3;\n    }\n");
            sb.Append("    equations\n    {\n        U               0.7;\n    }\n}\n");
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
                    "            box ({1} {2} {3}) ({4} {5} {6});\n        }}\n    }}\n\n",
                    i, box.Min.X, box.Min.Y, Math.Max(0, box.Min.Z),
                    box.Max.X, box.Max.Y, box.Max.Z);
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
                sfb.AppendFormat(Inv, "        box ({0} {1} {2}) ({3} {4} {5});\n",
                    box.Min.X, box.Min.Y, Math.Max(0, box.Min.Z),
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
                fvo.AppendFormat(Inv, "        selectionMode   cellSet;\n        cellSet         obstacle_{0};\n", i);
                fvo.Append("        type            DarcyForchheimer;\n");
                fvo.Append("        DarcyForchheimerCoeffs\n        {\n");
                fvo.Append("            d   (1e10 1e10 1e10);\n");
                fvo.Append("            f   (0 0 0);\n");
                fvo.Append("            coordinateSystem\n            {\n");
                fvo.Append("                type    cartesian;\n");
                fvo.Append("                origin  (0 0 0);\n");
                fvo.Append("                coordinateRotation\n                {\n");
                fvo.Append("                    type    axesRotation;\n");
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
            sb.Append("snGradSchemes\n{\n    default         corrected;\n}\n");
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

        private static void WriteUField(string caseDir, double ux, double uy, double uz)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volVectorField", "U"));
            sb.Append("dimensions      [0 1 -1 0 0 0 0];\n\n");
            sb.AppendFormat(Inv, "internalField   uniform ({0} {1} {2});\n\n", ux, uy, uz);
            sb.Append("boundaryField\n{\n");
            sb.AppendFormat(Inv, "    atmosphere\n    {{\n        type            fixedValue;\n        value           uniform ({0} {1} {2});\n    }}\n", ux, uy, uz);
            sb.Append("    ground\n    {\n        type            fixedValue;\n        value           uniform (0 0 0);\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "U"), sb.ToString());
        }

        private static void WriteTField(string caseDir)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("volScalarField", "T"));
            sb.Append("dimensions      [0 0 0 0 0 0 0];\n\n");
            sb.Append("internalField   uniform 0;\n\n");
            sb.Append("boundaryField\n{\n");
            sb.Append("    atmosphere\n    {\n        type            inletOutlet;\n        inletValue      uniform 0;\n        value           uniform 0;\n    }\n");
            sb.Append("    ground\n    {\n        type            zeroGradient;\n    }\n");
            sb.Append("}\n");
            WriteFile(Path.Combine(caseDir, "0", "T"), sb.ToString());
        }

        private static void WriteFvOptions(string caseDir, List<ReleaseSource3D> sources,
            double xMin, double xMax, double yMin, double yMax, double zMax,
            double cellSize, int nx, int ny, int nz)
        {
            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "fvOptions"));

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                var pos = src.EffectivePosition;
                double cellVolume = cellSize * cellSize * cellSize;
                double injectionRate = src.ReleaseRateKgPerS / cellVolume;

                sb.AppendFormat(Inv, "source_{0}\n{{\n", s);
                sb.Append("    type            scalarSemiImplicitSource;\n    active          true;\n\n");
                sb.Append("    scalarSemiImplicitSourceCoeffs\n    {\n");
                sb.AppendFormat(Inv, "        selectionMode   cellSet;\n        cellSet         sourceZone_{0};\n", s);
                sb.Append("        volumeMode      absolute;\n        injectionRateSuSp\n        {\n");
                sb.AppendFormat(Inv, "            T           ({0} 0);\n", injectionRate);
                sb.Append("        }\n    }\n}\n\n");
            }

            WriteFile(Path.Combine(caseDir, "constant", "fvOptions"), sb.ToString());
        }

        private static void WriteSetFieldsDict(string caseDir, List<ReleaseSource3D> sources,
            System.Windows.Media.Media3D.Vector3D wind, double cellSize)
        {
            bool hasJet = false;
            foreach (var src in sources)
                if (src.ComputedExitVelocity > 0) { hasJet = true; break; }

            if (!hasJet) return;

            var sb = new StringBuilder();
            sb.Append(FoamHeader("dictionary", "setFieldsDict"));
            sb.Append("defaultFieldValues\n(\n);\n\n");
            sb.Append("regions\n(\n");

            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                double v = src.ComputedExitVelocity;
                if (v <= 0) continue;

                var jet = src.ExitVelocityVector;
                double ux = wind.X + jet.X;
                double uy = wind.Y + jet.Y;
                double uz = wind.Z + jet.Z;

                var pos = src.EffectivePosition;
                double half = cellSize * 1.5;

                sb.Append("    boxToCell\n    {\n");
                sb.AppendFormat(Inv, "        box ({0} {1} {2}) ({3} {4} {5});\n",
                    pos.X - half, pos.Y - half, Math.Max(0, pos.Z - half),
                    pos.X + half, pos.Y + half, pos.Z + half);
                sb.Append("        fieldValues\n        (\n");
                sb.AppendFormat(Inv, "            volVectorFieldValue U ({0} {1} {2})\n", ux, uy, uz);
                sb.Append("        );\n    }\n\n");
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
                var pos = sources[s].EffectivePosition;
                double half = cellSize * 1.5;

                sb.AppendFormat(Inv, "    {{\n        name    sourceZone_{0};\n        type    cellSet;\n        action  new;\n", s);
                sb.AppendFormat(Inv, "        source  boxToCell;\n        sourceInfo\n        {{\n");
                sb.AppendFormat(Inv, "            box ({0} {1} {2}) ({3} {4} {5});\n",
                    pos.X - half, pos.Y - half, Math.Max(0, pos.Z - half),
                    pos.X + half, pos.Y + half, pos.Z + half);
                sb.Append("        }\n    }\n\n");
            }

            sb.Append(");\n");
            WriteFile(Path.Combine(caseDir, "system", "topoSetDict"), sb.ToString());
        }
    }
}
