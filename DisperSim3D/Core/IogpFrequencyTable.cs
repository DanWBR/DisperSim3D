using System;
using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Embedded copy of the leak-frequency tables published in
    /// **IOGP Report 434-01 — Risk Assessment Data Directory: Process Release
    /// Frequencies**, September 2019 (revision 1.1, May 2021), Section 2.2.
    ///
    /// Layout
    /// ------
    /// Every IOGP datasheet (one per equipment type, 24 total) publishes a
    /// "Tabulation" panel with five hole-size bands × six anchor equipment
    /// diameters (2" / 6" / 12" / 18" / 24" / 36" = 50 / 150 / 300 / 450 / 600 /
    /// 900 mm). The values are leak frequencies in **events per item·year** —
    /// except for <see cref="IogpEquipmentType.SteelProcessPipe"/> and
    /// <see cref="IogpEquipmentType.FlexiblePipe"/> which are **per metre·year**;
    /// callers multiply those by pipe length.
    ///
    /// Several IOGP datasheets only publish two columns ("Inlets 50–150 mm" and
    /// "Inlets &gt;150 mm"). For those types both 50 mm and 150 mm anchors share
    /// the "50–150" value, and 300 / 450 / 600 / 900 mm share the "&gt;150" value.
    /// Instrument connections (small-bore only) clamp to the 2" column for any
    /// requested diameter ≥ 50 mm.
    ///
    /// Dataset
    /// -------
    /// Only the **recommended 2006–2015 dataset** is embedded. The 1992–2015
    /// historical dataset and the LNG FRT (§2.3) are out of scope for v1 — see
    /// the plan file for the rationale.
    ///
    /// Source
    /// ------
    /// Tabulation panels of datasheets 1–24, pages 12–42 of IOGP 434-01 v1.1.
    /// Where a datasheet only publishes the 1992–2015 dataset (pumps reciprocating,
    /// air-cooled HX, degassers, expanders), those values are used as the
    /// "2006–2015" entry because no newer dataset exists.
    /// </summary>
    public static class IogpFrequencyTable
    {
        /// <summary>Anchor diameters in mm matching the columns of every IOGP
        /// tabulation panel: 2" / 6" / 12" / 18" / 24" / 36".</summary>
        public static readonly double[] AnchorDiametersMm = { 50, 150, 300, 450, 600, 900 };

        /// <summary>Number of equipment types (= datasheets).</summary>
        public const int EquipmentTypeCount = 24;

        /// <summary>Number of hole-size bands per datasheet (matches
        /// <see cref="IogpHoleSizeBand"/>).</summary>
        public const int BandCount = 5;

        // Indexed as [typeIndex (0..23), diameterIndex (0..5), bandIndex (0..4)].
        // typeIndex = (int)IogpEquipmentType - 1.
        // diameterIndex = position in AnchorDiametersMm.
        // bandIndex = (int)IogpHoleSizeBand.
        //
        // Zeros represent IOGP's "---" (not applicable, e.g. >150 mm rupture on 2"
        // equipment): the band is unreachable at that equipment size, contributing
        // nothing to the total frequency.
        private static readonly double[,,] Freq2006_2015 = BuildTable();

        private static double[,,] BuildTable()
        {
            var t = new double[EquipmentTypeCount, AnchorDiametersMm.Length, BandCount];

            // (1) Steel process pipes — per metre·year. Datasheet 1, page 12.
            // Bands: Tiny / Small / Medium / Large / Rupture.
            // Cols : 2" / 6" / 12" / 18" / 24" / 36".
            Fill6Col(t, IogpEquipmentType.SteelProcessPipe, new[,]
            {
                // 2"      6"      12"     18"     24"     36"
                { 1.5e-5, 9.5e-6, 8.6e-6, 8.1e-6, 7.7e-6, 7.7e-6 }, // Tiny
                { 6.4e-6, 3.9e-6, 4.2e-6, 4.8e-6, 4.9e-6, 4.9e-6 }, // Small
                { 2.8e-6, 1.6e-6, 2.1e-6, 3.0e-6, 3.3e-6, 3.3e-6 }, // Medium
                { 1.0e-6, 3.2e-7, 5.2e-7, 9.7e-7, 1.2e-6, 1.2e-6 }, // Large
                { 0.0,    2.0e-7, 4.6e-7, 1.3e-6, 1.7e-6, 1.7e-6 }, // Rupture
            });

            // (2) Flanged joints — per joint·year. Datasheet 2, page 14.
            Fill6Col(t, IogpEquipmentType.FlangedJoint, new[,]
            {
                { 4.4e-6, 7.0e-6, 1.3e-5, 1.9e-5, 2.1e-5, 2.1e-5 },
                { 2.0e-6, 3.1e-6, 5.0e-6, 6.5e-6, 6.9e-6, 6.9e-6 },
                { 9.1e-7, 1.4e-6, 1.9e-6, 2.1e-6, 2.2e-6, 2.2e-6 },
                { 3.8e-7, 3.2e-7, 3.7e-7, 3.4e-7, 3.3e-7, 3.3e-7 },
                { 0.0,    5.7e-7, 1.3e-6, 2.0e-6, 2.2e-6, 2.2e-6 },
            });

            // (3) Manual valves — per valve·year. Datasheet 3, page 16.
            Fill6Col(t, IogpEquipmentType.ManualValve, new[,]
            {
                { 1.5e-5, 1.7e-5, 2.9e-5, 3.9e-5, 4.1e-5, 4.1e-5 },
                { 8.0e-6, 8.0e-6, 1.5e-5, 2.2e-5, 2.5e-5, 2.5e-5 },
                { 4.6e-6, 3.8e-6, 8.0e-6, 1.4e-5, 1.6e-5, 1.6e-5 },
                { 2.7e-6, 9.1e-7, 2.2e-6, 4.3e-6, 5.3e-6, 5.3e-6 },
                { 0.0,    7.2e-7, 2.2e-6, 5.5e-6, 7.2e-6, 7.2e-6 },
            });

            // (4) Actuated valves — per valve·year. Datasheet 4, page 18.
            Fill6Col(t, IogpEquipmentType.ActuatedValve, new[,]
            {
                { 1.4e-4, 7.9e-5, 7.5e-5, 8.4e-5, 8.6e-5, 8.6e-5 },
                { 5.8e-5, 3.7e-5, 3.3e-5, 3.3e-5, 3.3e-5, 3.3e-5 },
                { 2.3e-5, 1.8e-5, 1.5e-5, 1.3e-5, 1.3e-5, 1.3e-5 },
                { 7.3e-6, 4.3e-6, 3.3e-6, 2.6e-6, 2.4e-6, 2.4e-6 },
                { 0.0,    3.6e-6, 2.6e-6, 1.7e-6, 1.4e-6, 1.4e-6 },
            });

            // (5) Instrument connections — per connection·year. Datasheet 5, page 20.
            // IOGP publishes only 1" (25 mm) and 2" (50 mm) columns. We clamp to the
            // 2" values for any diameter >= 50 mm; small-bore equipment beyond that
            // does not exist in this category.
            FillClamp2Col(t, IogpEquipmentType.InstrumentConnection,
                // Cols: 1"(25) / 2"(50). Map both to ALL 6 anchors clamped to 2".
                new[] { 1.2e-4, 5.0e-5, 2.0e-5, 6.6e-6, 0.0 });

            // (6) Process (pressure) vessels — per vessel·year. Datasheet 6, page 21.
            // 2 columns: "Inlets 50–150 mm" / "Inlets >150 mm".
            Fill2Col(t, IogpEquipmentType.PressureVessel,
                /* 50-150 */ new[] { 3.3e-4, 1.7e-4, 9.3e-5, 4.9e-5, 0.0 },
                /* >150   */ new[] { 3.3e-4, 1.7e-4, 9.3e-5, 2.5e-5, 2.4e-5 });

            // (7) Centrifugal pumps — per pump·year. Datasheet 7, page 23.
            Fill2Col(t, IogpEquipmentType.PumpCentrifugal,
                new[] { 2.7e-3, 6.4e-4, 1.4e-4, 1.8e-5, 0.0 },
                new[] { 2.7e-3, 6.4e-4, 1.4e-4, 1.4e-5, 4.0e-6 });

            // (8) Reciprocating pumps — per pump·year. Datasheet 8, page 24.
            // IOGP only publishes the 1992–2015 dataset for this type; we use those
            // values as the "current" (2006–2015 equivalent).
            Fill2Col(t, IogpEquipmentType.PumpReciprocating,
                new[] { 8.1e-4, 5.5e-4, 4.2e-4, 4.4e-4, 0.0 },
                new[] { 8.1e-4, 5.5e-4, 4.2e-4, 1.6e-4, 2.8e-4 });

            // (9) Centrifugal compressors — per compressor·year. Datasheet 9, page 25.
            Fill2Col(t, IogpEquipmentType.CompressorCentrifugal,
                new[] { 3.4e-3, 1.5e-3, 6.7e-4, 2.5e-4, 0.0 },
                new[] { 3.4e-3, 1.5e-3, 6.7e-4, 1.5e-4, 1.1e-4 });

            // (10) Reciprocating compressors — per compressor·year. Datasheet 10, p 26.
            Fill2Col(t, IogpEquipmentType.CompressorReciprocating,
                new[] { 6.8e-3, 3.1e-3, 1.4e-3, 5.6e-4, 0.0 },
                new[] { 6.8e-3, 3.1e-3, 1.4e-3, 3.2e-4, 2.4e-4 });

            // (11) Shell & tube HX, HC shell side — per HX·year. Datasheet 11, p 27.
            Fill2Col(t, IogpEquipmentType.HxShellTubeShellSide,
                new[] { 9.0e-4, 4.3e-4, 2.1e-4, 9.7e-5, 0.0 },
                new[] { 9.0e-4, 4.3e-4, 2.1e-4, 5.3e-5, 4.4e-5 });

            // (12) Shell & tube HX, HC tube side — per HX·year. Datasheet 12, p 28.
            Fill2Col(t, IogpEquipmentType.HxShellTubeTubeSide,
                new[] { 3.9e-4, 2.3e-4, 1.5e-4, 1.1e-4, 0.0 },
                new[] { 3.9e-4, 2.3e-4, 1.5e-4, 4.9e-5, 6.2e-5 });

            // (13) Plate HX — per HX·year. Datasheet 13, p 29.
            Fill2Col(t, IogpEquipmentType.HxPlate,
                new[] { 5.6e-3, 2.0e-3, 6.8e-4, 1.7e-4, 0.0 },
                new[] { 5.6e-3, 2.0e-3, 6.8e-4, 1.1e-4, 5.8e-5 });

            // (14) Air-cooled HX — per HX·year. Datasheet 14, p 30 (1992–2015 only).
            Fill2Col(t, IogpEquipmentType.HxAirCooled,
                new[] { 8.9e-4, 3.1e-4, 1.1e-4, 2.8e-5, 0.0 },
                new[] { 8.9e-4, 3.1e-4, 1.1e-4, 1.8e-5, 9.3e-6 });

            // (15) Filters — per filter·year. Datasheet 15, p 31.
            Fill2Col(t, IogpEquipmentType.Filter,
                new[] { 1.2e-3, 4.4e-4, 1.5e-4, 3.9e-5, 0.0 },
                new[] { 1.2e-3, 4.4e-4, 1.5e-4, 2.6e-5, 1.3e-5 });

            // (16) Pig traps — per pig trap·year. Datasheet 16, p 32.
            Fill2Col(t, IogpEquipmentType.PigTrap,
                new[] { 1.4e-3, 7.4e-4, 4.1e-4, 2.2e-4, 0.0 },
                new[] { 1.4e-3, 7.4e-4, 4.1e-4, 1.1e-4, 1.1e-4 });

            // (17) Flexible pipes — per metre·year. Datasheet 17, page 33.
            Fill6Col(t, IogpEquipmentType.FlexiblePipe, new[,]
            {
                { 5.8e-4, 9.7e-5, 1.9e-5, 6.2e-6, 3.2e-6, 3.2e-6 },
                { 3.0e-4, 6.4e-5, 1.5e-5, 5.3e-6, 2.9e-6, 2.9e-6 },
                { 1.7e-4, 4.6e-5, 1.3e-5, 5.3e-6, 3.0e-6, 3.0e-6 },
                { 9.2e-5, 1.7e-5, 5.6e-6, 2.7e-6, 1.6e-6, 1.6e-6 },
                { 0.0,    2.8e-5, 1.4e-5, 1.0e-5, 7.5e-6, 7.5e-6 },
            });

            // (18) Process vessels (Other) — per vessel·year. Datasheet 18, p 35.
            Fill2Col(t, IogpEquipmentType.PressureVesselOther,
                new[] { 1.7e-3, 1.1e-3, 7.1e-4, 5.6e-4, 0.0 },
                new[] { 1.7e-3, 1.1e-3, 7.1e-4, 2.4e-4, 3.2e-4 });

            // (19) Degassers — per vessel·year. Datasheet 19, p 37 (1992–2015 only).
            Fill2Col(t, IogpEquipmentType.Degasser,
                new[] { 8.7e-4, 5.5e-4, 3.8e-4, 3.4e-4, 0.0 },
                new[] { 8.7e-4, 5.5e-4, 3.8e-4, 1.4e-4, 2.0e-4 });

            // (20) Expanders — per equipment·year. Datasheet 20, p 38 (1992–2015 only).
            Fill2Col(t, IogpEquipmentType.Expander,
                new[] { 2.3e-3, 1.0e-3, 4.5e-4, 1.7e-4, 0.0 },
                new[] { 2.3e-3, 1.0e-3, 4.5e-4, 9.9e-5, 7.0e-5 });

            // (21) Xmas trees — per tree·year. Datasheet 21, page 39.
            Fill2Col(t, IogpEquipmentType.XmasTree,
                new[] { 2.4e-4, 1.0e-4, 4.4e-5, 1.6e-5, 0.0 },
                new[] { 2.4e-4, 1.0e-4, 4.4e-5, 9.6e-6, 6.5e-6 });

            // (22) Turbines — per turbine·year. Datasheet 22, page 40.
            Fill2Col(t, IogpEquipmentType.Turbine,
                new[] { 6.9e-3, 2.4e-3, 7.9e-4, 3.4e-4, 0.0 },
                new[] { 6.9e-3, 2.4e-3, 7.9e-4, 1.3e-4, 2.1e-4 });

            // (23) Pipeline ESDVs — per valve·year. Datasheet 23, page 41.
            Fill2Col(t, IogpEquipmentType.PipelineEsdv,
                new[] { 3.3e-4, 1.8e-4, 9.8e-5, 7.2e-5, 0.0 },
                new[] { 3.3e-4, 1.8e-4, 9.8e-5, 2.8e-5, 4.4e-5 });

            // (24) SSIV assemblies — per assembly·year. Datasheet 24, page 42.
            Fill2Col(t, IogpEquipmentType.SsivAssembly,
                new[] { 6.2e-4, 3.3e-4, 1.8e-4, 1.3e-4, 0.0 },
                new[] { 6.2e-4, 3.3e-4, 1.8e-4, 5.1e-5, 8.2e-5 });

            return t;
        }

        /// <summary>Writes a full 5×6 datasheet (5 bands × 6 diameters) into the
        /// frequency table. Used for types where IOGP publishes per-diameter values.</summary>
        /// <param name="band5_dia6">[band, diameter] block, e.g. [5,6] as in the IOGP datasheets.</param>
        private static void Fill6Col(double[,,] t, IogpEquipmentType type, double[,] band5_dia6)
        {
            int ti = (int)type - 1;
            for (int b = 0; b < BandCount; b++)
                for (int d = 0; d < AnchorDiametersMm.Length; d++)
                    t[ti, d, b] = band5_dia6[b, d];
        }

        /// <summary>Writes a 2-column datasheet ("Inlets 50–150 mm" / "Inlets &gt;150 mm")
        /// across the 6-anchor array: 50 mm and 150 mm get the small column,
        /// 300/450/600/900 mm get the large column.</summary>
        private static void Fill2Col(double[,,] t, IogpEquipmentType type,
            double[] small5Bands, double[] large5Bands)
        {
            int ti = (int)type - 1;
            for (int b = 0; b < BandCount; b++)
            {
                t[ti, 0, b] = small5Bands[b];  // 50 mm
                t[ti, 1, b] = small5Bands[b];  // 150 mm
                t[ti, 2, b] = large5Bands[b];  // 300 mm
                t[ti, 3, b] = large5Bands[b];  // 450 mm
                t[ti, 4, b] = large5Bands[b];  // 600 mm
                t[ti, 5, b] = large5Bands[b];  // 900 mm
            }
        }

        /// <summary>Writes a small-bore datasheet (only 1"/2" columns) clamped to
        /// the 2" values for every anchor. Used for InstrumentConnection.</summary>
        private static void FillClamp2Col(double[,,] t, IogpEquipmentType type, double[] bands)
        {
            int ti = (int)type - 1;
            for (int b = 0; b < BandCount; b++)
                for (int d = 0; d < AnchorDiametersMm.Length; d++)
                    t[ti, d, b] = bands[b];
        }

        /// <summary>
        /// Leak frequency in events per year for one item of the given equipment
        /// type, diameter and hole-size band.
        ///
        /// For <see cref="IogpEquipmentType.SteelProcessPipe"/> and
        /// <see cref="IogpEquipmentType.FlexiblePipe"/> the returned value is
        /// **per metre·year** — the caller multiplies by length. For everything else
        /// it is per item·year.
        ///
        /// Diameter is linearly interpolated between the 6 IOGP anchor sizes;
        /// outside the [50 mm, 900 mm] envelope it clamps to the nearest anchor.
        /// </summary>
        public static double FrequencyFor(IogpEquipmentType type, double diameterMm,
            IogpHoleSizeBand band)
        {
            int ti = (int)type - 1;
            if (ti < 0 || ti >= EquipmentTypeCount) return 0.0;
            int b = (int)band;
            if (b < 0 || b >= BandCount) return 0.0;

            // Clamp diameter to anchor envelope.
            double d = diameterMm;
            if (!(d > 0)) return 0.0;
            var anchors = AnchorDiametersMm;
            if (d <= anchors[0]) return Freq2006_2015[ti, 0, b];
            if (d >= anchors[anchors.Length - 1]) return Freq2006_2015[ti, anchors.Length - 1, b];

            // Find the bracketing pair and linearly interpolate.
            for (int i = 0; i < anchors.Length - 1; i++)
            {
                if (d >= anchors[i] && d <= anchors[i + 1])
                {
                    double t = (d - anchors[i]) / (anchors[i + 1] - anchors[i]);
                    double a = Freq2006_2015[ti, i, b];
                    double bv = Freq2006_2015[ti, i + 1, b];
                    return a + t * (bv - a);
                }
            }
            return 0.0;
        }

        /// <summary>
        /// Aggregate leak frequency for an inventory contributing to the SAME
        /// hole-size band release scenario. Sums (per-item freq × count) for items
        /// and (per-metre freq × length) for pipe types.
        /// </summary>
        public static double TotalSourceFrequency(IList<EquipmentInventoryItem> inventory,
            IogpHoleSizeBand band)
        {
            if (inventory == null || inventory.Count == 0) return 0.0;
            double sum = 0;
            foreach (var item in inventory)
            {
                if (item == null) continue;
                if (item.Count <= 0) continue;
                double f = FrequencyFor(item.Type, item.NominalDiameterMm, band);
                sum += f * item.Count;
            }
            return sum;
        }

        /// <summary>Representative hole diameter for a band in millimetres,
        /// computed as the geometric mean of the band endpoints per IOGP §2.1.2.
        /// For <see cref="IogpHoleSizeBand.Rupture"/> returns 152.4 mm (IOGP's
        /// default for 6" nominal) — callers can override with the equipment ID.</summary>
        public static double GeometricMeanHoleSizeMm(IogpHoleSizeBand band)
        {
            switch (band)
            {
                case IogpHoleSizeBand.Tiny:    return Math.Sqrt(1.0 * 3.0);    // 1.7320508...
                case IogpHoleSizeBand.Small:   return Math.Sqrt(3.0 * 10.0);   // 5.4772256...
                case IogpHoleSizeBand.Medium:  return Math.Sqrt(10.0 * 50.0);  // 22.3606798...
                case IogpHoleSizeBand.Large:   return Math.Sqrt(50.0 * 150.0); // 86.6025404...
                case IogpHoleSizeBand.Rupture: return 152.4;                   // 6" nominal
                default: return 0.0;
            }
        }

        /// <summary>Human-readable label for the band, useful for combo boxes.</summary>
        public static string DescribeBand(IogpHoleSizeBand band)
        {
            switch (band)
            {
                case IogpHoleSizeBand.Tiny:    return "Tiny (1–3 mm)";
                case IogpHoleSizeBand.Small:   return "Small (3–10 mm)";
                case IogpHoleSizeBand.Medium:  return "Medium (10–50 mm)";
                case IogpHoleSizeBand.Large:   return "Large (50–150 mm)";
                case IogpHoleSizeBand.Rupture: return "Rupture (>150 mm)";
                default: return band.ToString();
            }
        }
    }
}
