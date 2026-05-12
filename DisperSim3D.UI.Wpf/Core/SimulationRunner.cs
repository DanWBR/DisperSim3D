using System;
using System.Linq;
using DisperSim3D.Controls;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Snapshots a <see cref="Simulation"/> from project state at Run time and delegates to the
    /// existing Scene3DEditorControl execution pipeline.
    /// </summary>
    public static class SimulationRunner
    {
        public static bool RunSnapshot(Simulation sim, Scene3D scene, Scene3DEditorControl editor,
            Action<string> log = null)
        {
            if (sim == null || scene == null || editor == null) return false;

            var src = scene.TopLevelSources.FirstOrDefault(s => s.Id == sim.SourceId);
            if (src == null)
            {
                sim.Status = SimulationStatus.Failed;
                sim.StatusMessage = "Source not found in project";
                log?.Invoke(sim.StatusMessage);
                return false;
            }

            var wf = scene.WindFieldScenarios.FirstOrDefault(w => w.Id == sim.WindFieldId);
            if (wf == null)
            {
                sim.Status = SimulationStatus.Failed;
                sim.StatusMessage = "Wind field not found in project";
                log?.Invoke(sim.StatusMessage);
                return false;
            }
            if (wf.Status != WindFieldStatus.Ready)
            {
                sim.Status = SimulationStatus.Failed;
                sim.StatusMessage = "Wind field '" + wf.Name + "' is not Ready (status: " + wf.Status + ")";
                log?.Invoke(sim.StatusMessage);
                return false;
            }

            // Snapshot
            sim.SnapshotSource = CloneSource(src);
            GasLibraryItem gas = null;
            if (!string.IsNullOrEmpty(src.GasRefId))
                gas = scene.GasLibrary.FirstOrDefault(g => g.Id == src.GasRefId);
            if (gas == null && src.Gas != null)
                gas = GasLibraryItem.FromGasProperties(src.Gas);
            sim.SnapshotGas = gas;

            sim.SnapshotMeteo = wf.Meteo != null
                ? CloneMeteo(wf.Meteo)
                : (scene.GeneralSettings?.DefaultMeteo != null ? CloneMeteo(scene.GeneralSettings.DefaultMeteo) : new MeteorologicalConditions());
            sim.SnapshotCfdConfig = AppSettings.Instance.CreateCfdConfig();

            // Build a transient DispersionScenario for the existing pipeline
            var transientScenario = new DispersionScenario
            {
                Id = sim.Id,
                Name = sim.Name,
                Meteo = sim.SnapshotMeteo,
                SimulationDurationS = sim.SnapshotDurationS,
                TimeStepS = sim.SnapshotTimeStepS,
                SnapshotCount = sim.SnapshotCount > 0 ? sim.SnapshotCount : 20,
                DomainSizeM = sim.SnapshotDomainSizeM,
                GridResolution = sim.SnapshotGridResolution,
                SolverType = sim.SolverType,
                CfdConfig = sim.SnapshotCfdConfig,
                WindFieldScenarioId = sim.WindFieldId
            };
            transientScenario.Sources.Add(sim.SnapshotSource);

            sim.Status = SimulationStatus.Queued;
            sim.StatusMessage = "Queued";
            log?.Invoke("Simulation '" + sim.Name + "' queued");

            int idx = scene.DispersionScenarios.IndexOf(scene.DispersionScenario);
            scene.DispersionScenarios.Add(transientScenario);
            int newIdx = scene.DispersionScenarios.Count - 1;
            int prevActive = scene.ActiveScenarioIndex;
            scene.ActiveScenarioIndex = newIdx;

            try
            {
                editor.EnqueueSimulation(sim.SolverType, sim.SnapshotCfdConfig);
                sim.Status = SimulationStatus.Running;
                sim.StatusMessage = "Running";
                return true;
            }
            catch (Exception ex)
            {
                sim.Status = SimulationStatus.Failed;
                sim.StatusMessage = ex.Message;
                scene.DispersionScenarios.Remove(transientScenario);
                scene.ActiveScenarioIndex = prevActive;
                log?.Invoke("Failed to run: " + ex.Message);
                return false;
            }
        }

        private static ReleaseSource3D CloneSource(ReleaseSource3D src)
        {
            return new ReleaseSource3D
            {
                Id = src.Id,
                Name = src.Name,
                AttachedUnitId = src.AttachedUnitId,
                Gas = src.Gas != null ? new GasProperties
                {
                    Name = src.Gas.Name,
                    MolarMass = src.Gas.MolarMass,
                    LFL = src.Gas.LFL,
                    IDLH = src.Gas.IDLH,
                    ERPG1 = src.Gas.ERPG1,
                    ERPG2 = src.Gas.ERPG2,
                    ERPG3 = src.Gas.ERPG3,
                    HalfLifeS = src.Gas.HalfLifeS,
                    DryDepositionVelocityMPerS = src.Gas.DryDepositionVelocityMPerS
                } : null,
                GasRefId = src.GasRefId,
                Position = src.Position,
                ReleaseRateKgPerS = src.ReleaseRateKgPerS,
                PuffIntervalS = src.PuffIntervalS,
                ReleaseHeightOffset = src.ReleaseHeightOffset,
                ReleaseAzimuthDeg = src.ReleaseAzimuthDeg,
                ReleaseElevationDeg = src.ReleaseElevationDeg,
                StackDiameterM = src.StackDiameterM,
                ExitVelocityMPerS = src.ExitVelocityMPerS,
                ExitTemperatureK = src.ExitTemperatureK,
                HighPressureLeak = src.HighPressureLeak
            };
        }

        private static MeteorologicalConditions CloneMeteo(MeteorologicalConditions m)
        {
            return new MeteorologicalConditions
            {
                WindSpeed = m.WindSpeed,
                WindDirectionDeg = m.WindDirectionDeg,
                StabilityClass = m.StabilityClass,
                AmbientTemperature = m.AmbientTemperature,
                AmbientPressure = m.AmbientPressure
            };
        }
    }
}
