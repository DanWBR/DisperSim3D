using System;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Pure-function helpers that resolve the per-scenario risk weights consumed
    /// by <see cref="AllocationStrategy.MinResidualRisk"/> allocations. Split out
    /// of <see cref="DetectorAllocator"/> so the same logic can be reused by the
    /// dialog UI (to preview the auto-computed values) and a future probit-based
    /// consequence engine.
    ///
    /// Two responsibilities:
    /// <list type="bullet">
    ///   <item><c>AutoFrequency</c> — events/year for a scenario, derived from
    ///   the source's IOGP 434-01 leak frequency × wind-direction probability.</item>
    ///   <item><c>AutoConsequence</c> — relative severity weight per scenario,
    ///   from cloud volume scaled by gas hazard (IDLH / LFL).</item>
    /// </list>
    ///
    /// Both helpers fall back to sane defaults when their inputs are missing so
    /// the allocator never crashes on a half-configured project — partial data
    /// just produces a partial risk picture.
    /// </summary>
    public static class RiskWeightHelper
    {
        /// <summary>Per-scenario leak frequency in events/year, computed as
        /// `source.EffectiveLeakFrequencyPerYear × P_wind(direction)` where the
        /// wind probability comes from a nearest-bin lookup on the project
        /// <see cref="WindRoseData"/>. Falls back to 1/numBins (default 1/8) when
        /// the rose is null or empty.</summary>
        public static double AutoFrequency(ReleaseSource3D source,
            WindFieldScenario wf, WindRoseData rose)
        {
            if (source == null) return 0.0;
            double sourceFreq = source.EffectiveLeakFrequencyPerYear;
            if (!(sourceFreq > 0)) return 0.0;

            double pWind = ResolveWindProbability(wf, rose);
            return sourceFreq * pWind;
        }

        /// <summary>Returns the wind-direction probability (fraction in [0,1]) for
        /// the wind direction encoded in <paramref name="wf"/>, by looking up the
        /// nearest bin in <paramref name="rose"/>. Handles compass wrap-around.
        /// When <paramref name="rose"/> is null / empty, returns 1/8 (uniform
        /// 8-direction default).</summary>
        public static double ResolveWindProbability(WindFieldScenario wf, WindRoseData rose)
        {
            if (rose == null || rose.Bins == null || rose.Bins.Count == 0)
                return 1.0 / 8.0;
            double dirDeg = (wf?.Meteo?.WindDirectionDeg ?? 0.0);
            // Normalise to [0, 360).
            dirDeg = ((dirDeg % 360.0) + 360.0) % 360.0;

            // Find the nearest bin by minimum compass distance.
            double bestDist = double.MaxValue;
            double bestFreqPercent = 0;
            foreach (var bin in rose.Bins)
            {
                double bd = ((bin.DirectionDeg % 360.0) + 360.0) % 360.0;
                double diff = Math.Abs(dirDeg - bd);
                if (diff > 180) diff = 360 - diff;
                if (diff < bestDist)
                {
                    bestDist = diff;
                    bestFreqPercent = bin.Frequency;
                }
            }
            // IOGP bins are in percent; return as fraction.
            return Math.Max(0.0, bestFreqPercent / 100.0);
        }

        /// <summary>Heuristic consequence weight for a scenario, based on the
        /// cloud volume (m³) scaled by gas hazard. Returns a strictly positive
        /// scalar so it can be multiplied into risk products without zeroing them.
        ///
        /// Formula:
        ///   vol = cellVolM3 × CloudCellCount
        ///   if gas.IDLH > 0 AND quantity is toxic   → vol · max(1, peakConc / IDLH)
        ///   if gas.LFL  > 0 AND quantity is flam    → vol · (peakConc ≥ LFL ? 1.0 : 0.5)
        ///   else                                    → vol
        ///
        /// peakConc is in kg/m³ — same unit as IDLH / LFL on
        /// <see cref="GasProperties"/>.</summary>
        public static double AutoConsequence(CloudSnapshot snap, GasProperties gas,
            double peakConcKgPerM3, ViewFieldProperty quantity)
        {
            if (snap == null || !snap.IsValid) return 0.0;

            double dx = 2.0 * snap.DomainHalfM / Math.Max(1, snap.Nx);
            double dy = 2.0 * snap.DomainHalfM / Math.Max(1, snap.Ny);
            double dz = snap.DomainHeightM / Math.Max(1, snap.Nz);
            double cellVol = dx * dy * dz;
            double vol = cellVol * snap.CloudCellCount;
            if (!(vol > 0)) return 0.0;

            if (gas != null)
            {
                if (IsToxicQuantity(quantity) && gas.IDLH > 0)
                    return vol * Math.Max(1.0, peakConcKgPerM3 / gas.IDLH);
                if (IsFlammableQuantity(quantity) && gas.LFL > 0)
                    return vol * (peakConcKgPerM3 >= gas.LFL ? 1.0 : 0.5);
            }
            return vol;
        }

        /// <summary>Resolves the per-scenario risk product
        /// `R_s = freq_s × cons_s × P_d` for one cloud, honouring the auto/manual
        /// modes stored in <see cref="DispersionStudy.RiskWeights"/>. Used by the
        /// allocator and by the dialog UI to preview auto-derived values.</summary>
        public static (double freq, double cons, double risk) ResolveScenarioRisk(
            DispersionStudy study, CloudSnapshot snap,
            Simulation sim, Scene3D scene,
            double detectionProbability)
        {
            if (study == null || snap == null || sim == null)
                return (0.0, 0.0, 0.0);
            var risk = study.EnsureRiskFor(sim.Id);

            // ── Frequency ─────────────────────────────────────────────────────
            double freq;
            if (risk.FreqMode == RiskValueMode.Manual)
            {
                freq = risk.FreqPerYear;
            }
            else
            {
                // Resolve the LIVE source (not the snapshot) — leak frequencies are a
                // forward-looking input, the user wants the current best estimate.
                var liveSource = ResolveLiveSource(sim, scene) ?? sim.SnapshotSource;
                var wf = scene?.WindFieldScenarios?.FirstOrDefault(w => w.Id == sim.WindFieldId);
                freq = AutoFrequency(liveSource, wf, scene?.WindRose);
                // When inventory/wind rose data is missing, freq could be 0. Fall back
                // so the cloud still participates in the optimisation with a small risk.
                if (!(freq > 0))
                {
                    double srcFreq = liveSource?.EffectiveLeakFrequencyPerYear ?? 1e-4;
                    freq = srcFreq * (1.0 / 8.0);
                }
            }

            // ── Consequence ────────────────────────────────────────────────────
            double cons;
            if (risk.ConsMode == RiskValueMode.Manual)
            {
                cons = risk.Consequence;
            }
            else
            {
                var gas = ResolveGasForSimulation(sim, scene);
                double peak = ResolvePeakConc(sim);
                cons = AutoConsequence(snap, gas, peak, study.DetectionQuantity);
                // Floor at 1 m³ equivalent so auto-cons never zeroes out a real cloud.
                if (!(cons > 0)) cons = 1.0;
            }

            double pod = detectionProbability;
            if (!(pod > 0)) pod = 1.0;
            double rTotal = freq * cons * pod;
            return (freq, cons, rTotal);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsToxicQuantity(ViewFieldProperty q)
        {
            // ppm / ppb / fraction / kg-per-m³ quantities — toxic concentration measures.
            return q == ViewFieldProperty.ConcentrationPpm
                || q == ViewFieldProperty.ConcentrationPpb
                || q == ViewFieldProperty.MoleFraction
                || q == ViewFieldProperty.MassFraction
                || q == ViewFieldProperty.Concentration
                || q == ViewFieldProperty.ConcentrationKgM3;
        }

        private static bool IsFlammableQuantity(ViewFieldProperty q)
        {
            return q == ViewFieldProperty.PercentLFL
                || q == ViewFieldProperty.PercentUFL;
        }

        private static ReleaseSource3D ResolveLiveSource(Simulation sim, Scene3D scene)
        {
            if (sim == null || scene?.TopLevelSources == null) return null;
            return scene.TopLevelSources.FirstOrDefault(s => s.Id == sim.SourceId);
        }

        private static GasProperties ResolveGasForSimulation(Simulation sim, Scene3D scene)
        {
            if (sim?.SnapshotSource == null) return null;
            var src = sim.SnapshotSource;
            if (!string.IsNullOrEmpty(src.GasRefId) && scene?.GasLibrary != null)
            {
                var lib = scene.GasLibrary.FirstOrDefault(g => g.Id == src.GasRefId);
                if (lib != null) return lib.AsGasProperties();
            }
            return src.Gas;
        }

        /// <summary>Best-effort peak concentration (kg/m³) from a simulation. Falls
        /// back to <see cref="Simulation.MaxConcentration"/> when available; otherwise
        /// returns 0 (consequence becomes pure volume).</summary>
        private static double ResolvePeakConc(Simulation sim)
        {
            try
            {
                // Simulation has a MaxConcentration field in some build flavours.
                // Use reflection-style lookup to avoid hard-coupling — if absent,
                // return 0 and let AutoConsequence fall through to the volume branch.
                var prop = sim.GetType().GetProperty("MaxConcentration");
                if (prop != null)
                {
                    var v = prop.GetValue(sim);
                    if (v is double d && d > 0) return d;
                }
            }
            catch { }
            return 0.0;
        }
    }
}
