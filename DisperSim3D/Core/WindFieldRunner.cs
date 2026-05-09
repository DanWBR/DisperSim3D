using System;
using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Runs a steady-state wind field CFD simulation (simpleFoam) for a <see cref="WindFieldScenario"/>
    /// and stores the resulting <see cref="WindField3D"/> on the scenario.
    /// </summary>
    public class WindFieldRunner
    {
        private readonly OpenFoamEnvironment _env;

        public WindFieldRunner(OpenFoamEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        /// <summary>
        /// Synchronously runs the wind field simulation. Updates the scenario's status, case path,
        /// and computed <see cref="WindField3D"/>. Returns true on success.
        /// </summary>
        public bool Run(WindFieldScenario windScenario, List<BoundingBox> obstacles,
            Action<double, string> progress)
        {
            if (windScenario == null) throw new ArgumentNullException(nameof(windScenario));

            try
            {
                windScenario.Status = WindFieldStatus.Running;
                windScenario.StatusMessage = "Starting...";

                var config = windScenario.CfdConfig ?? new CfdConfiguration();
                _env.Configure(config.OpenFoamPath, config.DetectedEnvironment, config.WslDistroName);
                if (!_env.IsAvailable)
                {
                    windScenario.Status = WindFieldStatus.Failed;
                    windScenario.StatusMessage = "OpenFOAM not available: " + _env.StatusMessage;
                    return false;
                }

                var proxy = new DispersionScenario
                {
                    Id = windScenario.Id,
                    Name = windScenario.Name,
                    DomainSizeM = windScenario.DomainSizeM,
                    GridResolution = windScenario.GridResolution,
                    Meteo = windScenario.Meteo,
                    SolverType = CfdSolverType.ScalarSimpleFoam
                };

                string caseDir = OpenFoamCaseGenerator.GenerateWindCase(
                    proxy, config, obstacles != null && obstacles.Count > 0 ? obstacles : null);
                windScenario.CasePath = caseDir;

                int nx = windScenario.GridResolution;
                int ny = windScenario.GridResolution;
                int nz = Math.Max(1, windScenario.GridResolution / 2);
                double domain = windScenario.DomainSizeM;
                double height = windScenario.DomainHeightM > 0 ? windScenario.DomainHeightM : domain;

                var runner = new OpenFoamRunner(_env);
                var windField = runner.RunWindCase(caseDir, nx, ny, nz,
                    -domain, domain, -domain, domain, height,
                    obstacles != null && obstacles.Count > 0,
                    config.NumberOfProcessors > 1 ? config.NumberOfProcessors : 1,
                    progress);

                if (windField == null)
                {
                    windScenario.Status = WindFieldStatus.Failed;
                    windScenario.StatusMessage = "simpleFoam completed but no U field could be read";
                    return false;
                }

                windScenario.WindField = windField;
                windScenario.Status = WindFieldStatus.Ready;
                windScenario.StatusMessage = string.Format("Ready ({0}x{1}x{2})", nx, ny, nz);
                return true;
            }
            catch (Exception ex)
            {
                windScenario.Status = WindFieldStatus.Failed;
                windScenario.StatusMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Re-loads the WindField3D from a previously written case path, without re-running the solver.
        /// </summary>
        public static WindField3D LoadFromCase(WindFieldScenario windScenario)
        {
            if (windScenario == null || string.IsNullOrEmpty(windScenario.CasePath))
                return null;
            if (!System.IO.Directory.Exists(windScenario.CasePath))
                return null;

            int nx = windScenario.GridResolution;
            int ny = windScenario.GridResolution;
            int nz = Math.Max(1, windScenario.GridResolution / 2);
            double domain = windScenario.DomainSizeM;
            double height = windScenario.DomainHeightM > 0 ? windScenario.DomainHeightM : domain;

            try
            {
                var wf = OpenFoamResultReader.ReadWindField(windScenario.CasePath, nx, ny, nz,
                    -domain, domain, -domain, domain, height);
                if (wf != null)
                {
                    windScenario.WindField = wf;
                    windScenario.Status = WindFieldStatus.Ready;
                }
                return wf;
            }
            catch
            {
                return null;
            }
        }
    }
}
