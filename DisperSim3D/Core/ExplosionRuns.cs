#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Extensibility;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Explosion simulations: a <see cref="Simulation"/> whose <see cref="Simulation.ExplosionId"/>
    /// names an <see cref="ExplosionEvent"/>, run on a solver extender that declares
    /// <see cref="SolverCapabilities.Explosion"/>. Shared by the desktop UI, the headless runner
    /// and the automation server so all of them ask the solver the same thing.
    /// </summary>
    public static class ExplosionRuns
    {
        /// <summary>An explosion simulation, centred on the ignition point, with the dispersion
        /// simulation's meteorology and gas: 1 s of explosion, 20 snapshots.</summary>
        public static Simulation CreateFor(Scene3D scene, ExplosionEvent explosion, ISolverExtender solver)
        {
            var dispersion = scene.Simulations?.FirstOrDefault(s => s.Id == explosion.SimulationId);
            return new Simulation
            {
                Name = (explosion.Name ?? "Explosion") + " (" + solver.DisplayName + ")",
                ExplosionId = explosion.Id,
                SourceId = dispersion?.SourceId,
                WindFieldId = dispersion?.WindFieldId,
                SolverType = CfdSolverType.Extender,
                ExtenderSolverId = solver.Id,
                SnapshotSource = dispersion?.SnapshotSource,
                SnapshotGas = dispersion?.SnapshotGas,
                SnapshotMeteo = dispersion?.SnapshotMeteo,
                SnapshotDomainSizeM = dispersion?.SnapshotDomainSizeM ?? 100,
                SnapshotDomainCenterX = explosion.Position.X,
                SnapshotDomainCenterY = explosion.Position.Y,
                SnapshotGridResolution = dispersion?.SnapshotGridResolution ?? 40,
                SnapshotDurationS = 1.0,
                SnapshotTimeStepS = 1e-3,
                SnapshotCount = 20
            };
        }

        /// <summary>The scene's point monitors, as the probes of an explosion.</summary>
        public static IReadOnlyList<(string Name, Geometry.Point3D Position)> Probes(Scene3D scene)
            => (scene.MonitorPoints ?? new List<MonitorPoint3D>())
                .Where(m => m.Type == MonitorType.Point)
                .Select(m => (m.Name, m.Position)).ToList();

        /// <summary>
        /// The request an explosion simulation sends its solver, or why it cannot run: the solver
        /// missing or not an explosion solver, the explosion gone, or its dispersion simulation not
        /// finished.
        /// </summary>
        public static (SolverExtenderRequest? Request, string? Error) Prepare(Scene3D scene, Simulation sim,
            CfdConfiguration config, IReadOnlyList<BoundingBox> obstacles,
            Action<double, string>? progress, System.Threading.CancellationToken cancel)
        {
            var solver = SolverExtenderRegistry.Find(sim.ExtenderSolverId);
            if (solver == null) return (null, "Extender solver '" + sim.ExtenderSolverId + "' is not installed.");
            if ((solver.Capabilities & SolverCapabilities.Explosion) == 0)
                return (null, solver.DisplayName + " does not run explosions.");
            var explosion = scene.Explosions?.FirstOrDefault(e => e.Id == sim.ExplosionId);
            if (explosion == null) return (null, "The explosion of '" + sim.Name + "' is not in the project.");
            var dispersion = scene.Simulations?.FirstOrDefault(s => s.Id == explosion.SimulationId);
            if (dispersion == null || dispersion.Status != SimulationStatus.Completed || string.IsNullOrEmpty(dispersion.CasePath))
                return (null, "Explosion '" + explosion.Name + "' needs its dispersion simulation run first.");
            if (explosion.Cloud == ExplosionCloud.EquivalentBox
                && !(explosion.BoxMax.X > explosion.BoxMin.X && explosion.BoxMax.Y > explosion.BoxMin.Y && explosion.BoxMax.Z > explosion.BoxMin.Z))
                return (null, "Explosion '" + explosion.Name + "' has no box for its equivalent cloud.");
            var target = new SolverExplosionTarget(explosion, dispersion)
            {
                Gas = FireStudyEngine.ResolveGas(dispersion, scene),
                Probes = Probes(scene),
            };
            var meteo = sim.SnapshotMeteo
                        ?? dispersion.SnapshotMeteo
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
            };
            if (sim.SnapshotDomainSizeM > 0) scenario.DomainSizeM = sim.SnapshotDomainSizeM;
            if (sim.SnapshotGridResolution > 0) scenario.GridResolution = sim.SnapshotGridResolution;
            if (sim.SnapshotDurationS > 0) scenario.SimulationDurationS = sim.SnapshotDurationS;
            if (sim.SnapshotTimeStepS > 0) scenario.TimeStepS = sim.SnapshotTimeStepS;
            if (sim.SnapshotCount > 0) scenario.SnapshotCount = sim.SnapshotCount;
            var request = new SolverExtenderRequest(scene, scenario, config, obstacles, progress, cancel)
            {
                Explosion = target,
                DomainCenterX = sim.SnapshotDomainCenterX,
                DomainCenterY = sim.SnapshotDomainCenterY
            };
            return (request, null);
        }

        /// <summary>The result grid of an explosion run: what the solver returned, else the first
        /// explosion channel in its folder (peak overpressure, then overpressure, then the rest).</summary>
        public static OpenFoamResult? ResultOf(SolverExtenderResult run, SolverExtenderRequest request)
        {
            if (run.Result != null) return run.Result;
            if (string.IsNullOrEmpty(run.CasePath)) return null;
            double half = request.Scenario.DomainSizeM;
            foreach (var channel in new[] { ResultChannels.PeakOverpressure, ResultChannels.Overpressure }.Concat(ResultChannels.All))
            {
                int nx = request.Scenario.GridResolution, ny = nx, nz = Math.Max(1, nx / 2);
                var r = OpenFoamResultReader.TryLoadFlatBinCase(run.CasePath, ref nx, ref ny, ref nz, half, channel.Suffix);
                if (r == null || !r.IsLoaded) continue;
                r.DomainXMin = request.DomainCenterX - half;
                r.DomainXMax = request.DomainCenterX + half;
                r.DomainYMin = request.DomainCenterY - half;
                r.DomainYMax = request.DomainCenterY + half;
                return r;
            }
            return null;
        }
    }
}
