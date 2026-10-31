using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// The fields a simulation stored in its folder, read the way monitors, detectors and
    /// studies need them: one channel at a time, sampled at a point, or collapsed over
    /// time. A stored field wins over what DisperSim would derive itself — a fire solver's
    /// <c>_Q</c> over the analytic radiation models, its <c>_T</c> over nothing at all.
    /// </summary>
    public static class StoredFields
    {
        /// <summary>
        /// One channel of a simulation's result folder, or null when it wrote none. The
        /// species and the temperature are also read from an OpenFOAM case; the other
        /// channels only from the flat-bin layout.
        /// </summary>
        public static OpenFoamResult Load(Simulation sim, ResultChannel channel)
        {
            if (sim == null || channel == null || string.IsNullOrEmpty(sim.CasePath) || !Directory.Exists(sim.CasePath))
                return null;
            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60, ny = nx, nz = Math.Max(1, nx / 2);
            double half = Half(sim);

            OpenFoamResult result = null;
            if (File.Exists(Path.Combine(sim.CasePath, "system", "controlDict")))
            {
                string field = channel == ResultChannels.Species
                    ? OpenFoamCaseGenerator.ResolveOpenFoamSpecies(sim.SnapshotSource)
                    : channel == ResultChannels.Temperature ? "T" : null;
                if (field != null)
                    try { result = OpenFoamResultReader.ReadResults(sim.CasePath, nx, ny, nz, half, scalarFieldName: field); }
                    catch { result = null; }
            }
            if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                result = OpenFoamResultReader.TryLoadFlatBinCase(sim.CasePath, ref nx, ref ny, ref nz, half, channel);
            return result != null && result.IsLoaded && result.TimeSteps.Count > 0 ? result : null;
        }

        /// <summary>True when the simulation stored <paramref name="channel"/>.</summary>
        public static bool Has(Simulation sim, ResultChannel channel) => Load(sim, channel) != null;

        /// <summary>The half-width of a simulation's result grid, m.</summary>
        public static double Half(Simulation sim) => sim != null && sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

        /// <summary>
        /// Collapses a channel over time as a View asks: the last snapshot, the one nearest
        /// <paramref name="timeS"/>, or per cell the hazardous extreme — the maximum, or the
        /// minimum for a channel whose low values are the hazard (oxygen, visibility).
        /// </summary>
        public static double[,,] Collapse(OpenFoamResult result, ResultChannel channel, ViewTimeMode mode, double timeS = 0)
        {
            if (result == null || result.TimeSteps.Count == 0) return null;
            if (mode == ViewTimeMode.FinalSnapshot || result.TimeSteps.Count == 1)
                return result.GetField(result.TimeSteps[result.TimeSteps.Count - 1]);
            if (mode == ViewTimeMode.SpecificTime)
            {
                double best = result.TimeSteps[0];
                foreach (double t in result.TimeSteps)
                    if (Math.Abs(t - timeS) < Math.Abs(best - timeS)) best = t;
                return result.GetField(best);
            }

            bool low = channel != null && channel.LowIsHazard;
            double[,,] acc = null;
            foreach (double t in result.TimeSteps)
            {
                var f = result.GetField(t);
                if (f == null) continue;
                if (acc == null) { acc = (double[,,])f.Clone(); continue; }
                int nx = acc.GetLength(0), ny = acc.GetLength(1), nz = acc.GetLength(2);
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        for (int k = 0; k < nz; k++)
                        {
                            double v = f[i, j, k];
                            if (low ? v < acc[i, j, k] : v > acc[i, j, k]) acc[i, j, k] = v;
                        }
            }
            return acc;
        }

        /// <summary>
        /// Trilinear point sampling of a field on a simulation's grid: <see cref="Half"/>
        /// either side of its centre in x and y, cubic cells from the ground up.
        /// </summary>
        public static IConcentrationField Sampler(double[,,] field, Simulation sim)
        {
            double half = Half(sim);
            double cx = sim?.SnapshotDomainCenterX ?? 0, cy = sim?.SnapshotDomainCenterY ?? 0;
            int nx = field.GetLength(0), nz = field.GetLength(2);
            return new OpenFoamConcentrationField(field, cx - half, cx + half, cy - half, cy + half,
                2.0 * half * nz / Math.Max(1, nx));
        }

        /// <summary>A channel at a point, one sample per stored time; empty when it was not stored.</summary>
        public static List<MonitorSample> TimeSeries(Simulation sim, ResultChannel channel, double x, double y, double z)
        {
            var samples = new List<MonitorSample>();
            var result = Load(sim, channel);
            if (result == null) return samples;
            foreach (double t in result.TimeSteps)
            {
                var f = result.GetField(t);
                if (f == null) continue;
                samples.Add(new MonitorSample { TimeS = t, Concentration = Sampler(f, sim).EvaluateConcentration(x, y, z) });
            }
            return samples;
        }

        /// <summary>
        /// What a monitor or detector measuring <paramref name="quantity"/> reads at a point
        /// over a simulation, one sample per stored time:
        /// <list type="bullet">
        /// <item>temperature, soot, CO, CO2, O2 and visibility from their stored channels;</item>
        /// <item>radiation from the stored <c>_Q</c>; the dose and fatality probability
        /// from it integrated over the scene's exposure time from each stored time on
        /// (<see cref="ThermalDose.ForwardWindowDoses"/>) — or, when the simulation stored
        /// no radiation, one sample of the analytic fire models;</item>
        /// <item>the species quantities from the stored species, converted to the unit asked.</item>
        /// </list>
        /// Empty when the simulation stored nothing the quantity can be read from.
        /// </summary>
        public static List<MonitorSample> Measure(Scene3D scene, Simulation sim, ViewFieldProperty quantity,
            double x, double y, double z, GasProperties gas = null)
        {
            bool fromRadiation = quantity == ViewFieldProperty.ThermalDose || quantity == ViewFieldProperty.FatalityProbability;
            var channel = fromRadiation ? ResultChannels.Radiation : ResultChannels.For(quantity);
            if (channel != null)
            {
                var series = TimeSeries(sim, channel, x, y, z);
                if (series.Count > 0)
                {
                    if (fromRadiation)
                    {
                        // Each sample is the dose of someone who stays the exposure time from then on.
                        double exposure = scene?.FireScenario?.ExposureTimeS ?? 20.0;
                        var doses = ThermalDose.ForwardWindowDoses(series.Select(s => s.TimeS).ToList(),
                            series.Select(s => s.Concentration).ToList(), exposure);
                        for (int i = 0; i < series.Count; i++)
                            series[i].Concentration = quantity == ViewFieldProperty.ThermalDose
                                ? doses[i]
                                : ThermalDose.ProbitToProbability(ThermalDose.FatalityProbit(doses[i]));
                    }
                    return series;
                }
                if (FieldTransform.IsAnalytic(quantity))
                    return new List<MonitorSample>
                    {
                        new MonitorSample { TimeS = 0, Concentration = FieldTransform.AnalyticAtPoint(scene, quantity, x, y, z) }
                    };
                return series;
            }

            if (FieldTransform.NeedsSpeciesField(quantity) && !FieldTransform.IsIgnitionDerived(quantity))
            {
                var series = TimeSeries(sim, ResultChannels.Species, x, y, z);
                gas ??= FireStudyEngine.ResolveGas(sim, scene);
                foreach (var s in series)
                    s.Concentration = FieldTransform.ScalarFromMassFraction(
                        FieldTransform.SpeciesAsMassFraction(s.Concentration, sim), quantity, gas);
                return series;
            }
            return new List<MonitorSample>();
        }

        /// <summary>True when the quantity's hazard is a low reading (oxygen, visibility).</summary>
        public static bool LowIsHazard(ViewFieldProperty quantity) => ResultChannels.For(quantity)?.LowIsHazard == true;
    }
}
