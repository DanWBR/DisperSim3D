#nullable enable
using System;
using System.Linq;
using DisperSim3D.Extensibility;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Fire simulations: a <see cref="Simulation"/> whose <see cref="Simulation.FireSourceId"/>
    /// or <see cref="Simulation.IgnitionId"/> names the fire, run on a solver extender that
    /// declares <see cref="SolverCapabilities.Fire"/>. Shared by the desktop UI, the headless
    /// runner and the validation harness so all three ask the solver the same thing.
    /// </summary>
    public static class FireRuns
    {
        /// <summary>A fire simulation for a fire source, on a grid centred on it.</summary>
        public static Simulation CreateFor(Scene3D scene, FireSource source, ISolverExtender solver)
        {
            var wind = scene.WindFieldScenarios?.FirstOrDefault();
            return new Simulation
            {
                Name = (source.Name ?? "Fire") + " (" + solver.DisplayName + ")",
                FireSourceId = source.Id,
                WindFieldId = wind?.Id,
                SolverType = CfdSolverType.Extender,
                ExtenderSolverId = solver.Id,
                SnapshotMeteo = wind?.Meteo,
                SnapshotDomainSizeM = scene.GeneralSettings?.DefaultDomainSizeM > 0 ? scene.GeneralSettings.DefaultDomainSizeM : 100,
                SnapshotDomainCenterX = source.Position.X,
                SnapshotDomainCenterY = source.Position.Y,
                SnapshotGridResolution = scene.GeneralSettings?.DefaultGridResolution > 0 ? scene.GeneralSettings.DefaultGridResolution : 40,
                SnapshotDurationS = 60,
                SnapshotTimeStepS = 0.5,
                SnapshotCount = 10
            };
        }

        /// <summary>A fire simulation for an ignition, on the grid of the dispersion simulation it ignites.</summary>
        public static Simulation CreateFor(Scene3D scene, IgnitionEvent ignition, ISolverExtender solver)
        {
            var dispersion = scene.Simulations?.FirstOrDefault(s => s.Id == ignition.SimulationId);
            return new Simulation
            {
                Name = (ignition.Name ?? "Ignition") + " (" + solver.DisplayName + ")",
                IgnitionId = ignition.Id,
                SourceId = dispersion?.SourceId,
                WindFieldId = dispersion?.WindFieldId,
                SolverType = CfdSolverType.Extender,
                ExtenderSolverId = solver.Id,
                SnapshotSource = dispersion?.SnapshotSource,
                SnapshotGas = dispersion?.SnapshotGas,
                SnapshotMeteo = dispersion?.SnapshotMeteo,
                SnapshotDomainSizeM = dispersion?.SnapshotDomainSizeM ?? 100,
                SnapshotDomainCenterX = dispersion?.SnapshotDomainCenterX ?? 0,
                SnapshotDomainCenterY = dispersion?.SnapshotDomainCenterY ?? 0,
                SnapshotGridResolution = dispersion?.SnapshotGridResolution ?? 40,
                SnapshotDurationS = 30,
                SnapshotTimeStepS = 0.1,
                SnapshotCount = 10
            };
        }

        /// <summary>
        /// The request a fire simulation sends its solver, or why it cannot run: the
        /// solver missing or not a fire solver, the fire source or ignition gone, or the
        /// ignited dispersion simulation not finished.
        /// </summary>
        public static (SolverExtenderRequest? Request, string? Error) Prepare(Scene3D scene, Simulation sim,
            CfdConfiguration config, System.Collections.Generic.IReadOnlyList<BoundingBox> obstacles,
            Action<double, string>? progress, System.Threading.CancellationToken cancel)
        {
            var solver = SolverExtenderRegistry.Find(sim.ExtenderSolverId);
            if (solver == null) return (null, "Extender solver '" + sim.ExtenderSolverId + "' is not installed.");
            if ((solver.Capabilities & SolverCapabilities.Fire) == 0)
                return (null, solver.DisplayName + " does not run fires.");

            SolverFireTarget target;
            if (!string.IsNullOrEmpty(sim.FireSourceId))
            {
                var source = scene.FireScenario?.Sources?.FirstOrDefault(f => f.Id == sim.FireSourceId);
                if (source == null) return (null, "The fire source of '" + sim.Name + "' is not in the project.");
                target = new SolverFireTarget(source);
            }
            else
            {
                var ignition = scene.Ignitions?.FirstOrDefault(g => g.Id == sim.IgnitionId);
                if (ignition == null) return (null, "The ignition of '" + sim.Name + "' is not in the project.");
                var dispersion = scene.Simulations?.FirstOrDefault(s => s.Id == ignition.SimulationId);
                if (dispersion == null || dispersion.Status != SimulationStatus.Completed || string.IsNullOrEmpty(dispersion.CasePath))
                    return (null, "Ignition '" + ignition.Name + "' needs its dispersion simulation run first.");
                target = new SolverFireTarget(ignition, dispersion) { Gas = FireStudyEngine.ResolveGas(dispersion, scene) };
            }

            var meteo = sim.SnapshotMeteo
                        ?? scene.WindFieldScenarios?.FirstOrDefault(w => w.Id == sim.WindFieldId)?.Meteo
                        ?? scene.GeneralSettings?.DefaultMeteo
                        ?? new MeteorologicalConditions();
            var scenario = new DispersionScenario
            {
                Id = sim.Id,
                Name = sim.Name,
                Meteo = meteo,
                SolverType = CfdSolverType.Extender,
                ExtenderSolverId = solver.Id,
                CfdConfig = config,
                WindFieldScenarioId = string.IsNullOrEmpty(sim.WindFieldId) ? null : sim.WindFieldId
            };
            if (sim.SnapshotDomainSizeM > 0) scenario.DomainSizeM = sim.SnapshotDomainSizeM;
            if (sim.SnapshotGridResolution > 0) scenario.GridResolution = sim.SnapshotGridResolution;
            if (sim.SnapshotDurationS > 0) scenario.SimulationDurationS = sim.SnapshotDurationS;
            if (sim.SnapshotTimeStepS > 0) scenario.TimeStepS = sim.SnapshotTimeStepS;
            if (sim.SnapshotCount > 0) scenario.SnapshotCount = sim.SnapshotCount;

            var request = new SolverExtenderRequest(scene, scenario, config, obstacles, progress, cancel)
            {
                Fire = target,
                DomainCenterX = sim.SnapshotDomainCenterX,
                DomainCenterY = sim.SnapshotDomainCenterY
            };
            return (request, null);
        }

        /// <summary>
        /// The result grid of a run: what the solver returned, else the first channel it
        /// wrote to its folder (temperature, then radiation, then the rest), placed on the
        /// grid the request asked for. Null when it left nothing to read.
        /// </summary>
        public static OpenFoamResult? ResultOf(SolverExtenderResult run, SolverExtenderRequest request)
        {
            if (run.Result != null) return run.Result;
            if (string.IsNullOrEmpty(run.CasePath)) return null;
            double half = request.Scenario.DomainSizeM;
            foreach (var channel in new[] { ResultChannels.Temperature, ResultChannels.Radiation }
                         .Concat(ResultChannels.All))
            {
                int nx = request.Scenario.GridResolution, ny = nx, nz = Math.Max(1, nx / 2);
                var r = OpenFoamResultReader.TryLoadFlatBinCase(run.CasePath, ref nx, ref ny, ref nz, half, channel);
                if (r == null || !r.IsLoaded) continue;
                r.DomainXMin = request.DomainCenterX - half;
                r.DomainXMax = request.DomainCenterX + half;
                r.DomainYMin = request.DomainCenterY - half;
                r.DomainYMax = request.DomainCenterY + half;
                return r;
            }
            return null;
        }

        /// <summary>
        /// Takes the grid an extender's run reports onto its simulation: the half-width,
        /// the centre and the resolution, so views and sensors place its fields where the
        /// solver put them.
        /// </summary>
        public static void ApplyResultGrid(Simulation sim, OpenFoamResult? result)
        {
            if (sim == null || result == null || !(result.DomainXMax > result.DomainXMin)) return;
            sim.SnapshotDomainSizeM = (result.DomainXMax - result.DomainXMin) / 2;
            sim.SnapshotDomainCenterX = (result.DomainXMax + result.DomainXMin) / 2;
            sim.SnapshotDomainCenterY = (result.DomainYMax + result.DomainYMin) / 2;
            if (result.GridNx > 0) sim.SnapshotGridResolution = result.GridNx;
        }
    }
}
