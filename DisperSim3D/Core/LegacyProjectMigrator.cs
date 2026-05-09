using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Converts a Scene3D loaded from a legacy XML file (where sources/gases were inline under
    /// DispersionScenarios) into the new project layout: top-level sources, gas library, simulations.
    /// Idempotent — safe to call after every load.
    /// </summary>
    public static class LegacyProjectMigrator
    {
        public static void MigrateInPlace(Scene3D scene)
        {
            if (scene == null) return;

            if (scene.GeneralSettings == null) scene.GeneralSettings = new ProjectSettings();
            if (scene.GasLibrary == null) scene.GasLibrary = new List<GasLibraryItem>();
            if (scene.TopLevelSources == null) scene.TopLevelSources = new List<ReleaseSource3D>();
            if (scene.Simulations == null) scene.Simulations = new List<Simulation>();

            if (scene.DispersionScenarios == null || scene.DispersionScenarios.Count == 0)
                return;

            bool alreadyMigrated = scene.TopLevelSources.Count > 0 || scene.GasLibrary.Count > 0;

            foreach (var sc in scene.DispersionScenarios)
            {
                if (sc.Sources == null) continue;
                foreach (var src in sc.Sources)
                {
                    if (alreadyMigrated && scene.TopLevelSources.Any(s => s.Id == src.Id))
                        continue;

                    if (src.Gas != null && string.IsNullOrEmpty(src.GasRefId))
                    {
                        var libItem = scene.GasLibrary.FirstOrDefault(g =>
                            g.Kind == GasLibraryItemKind.Pure && g.PureGas != null &&
                            g.PureGas.Name == src.Gas.Name &&
                            g.PureGas.MolarMass == src.Gas.MolarMass);
                        if (libItem == null)
                        {
                            libItem = GasLibraryItem.FromGasProperties(src.Gas);
                            libItem.Name = src.Gas.Name ?? ("Gas " + (scene.GasLibrary.Count + 1));
                            scene.GasLibrary.Add(libItem);
                        }
                        src.GasRefId = libItem.Id;
                    }

                    if (!scene.TopLevelSources.Any(s => s.Id == src.Id))
                        scene.TopLevelSources.Add(src);
                }
            }

            if (scene.Simulations.Count == 0 && scene.CfdSimulations != null)
            {
                foreach (var entry in scene.CfdSimulations)
                {
                    if (entry == null) continue;
                    var sim = new Simulation
                    {
                        Id = entry.Id ?? System.Guid.NewGuid().ToString(),
                        Name = entry.Name ?? entry.ScenarioName ?? "Migrated Run",
                        Status = entry.HasResults ? SimulationStatus.Completed : SimulationStatus.Failed,
                        CasePath = entry.CasePath,
                        CreatedAt = entry.CreatedAt,
                        CompletedAt = entry.HasResults ? entry.CreatedAt : (System.DateTime?)null,
                        SnapshotDurationS = entry.DurationS,
                        TimeStepCount = entry.TimeStepCount,
                        SnapshotDomainSizeM = entry.DomainSizeM
                    };
                    scene.Simulations.Add(sim);
                }
            }
        }
    }
}
