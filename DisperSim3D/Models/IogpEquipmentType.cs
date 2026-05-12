namespace DisperSim3D.Models
{
    /// <summary>
    /// Equipment type as catalogued by IOGP 434-01 "Risk Assessment Data Directory —
    /// Process Release Frequencies" (Sep 2019, rev 1.1 May 2021), Section 1.1.
    ///
    /// Each value here matches one of the 24 datasheets that hold per-hole-size-band
    /// leak frequencies. Consumed by <see cref="EquipmentInventoryItem"/> and looked up
    /// via <see cref="DisperSim3D.Core.IogpFrequencyTable"/>.
    ///
    /// Note that <see cref="SteelProcessPipe"/> and <see cref="FlexiblePipe"/> are
    /// reported by IOGP on a **per metre·year** basis — for those entries the
    /// inventory <c>Count</c> means total length in metres. Every other type is
    /// per-item·year and <c>Count</c> means the number of such items.
    /// </summary>
    public enum IogpEquipmentType
    {
        /// <summary>Steel process pipes — IOGP datasheet 1. Per metre·year.
        /// Scope: pipes on topsides / process units, includes welds, excludes valves,
        /// flanges and instruments.</summary>
        SteelProcessPipe = 1,

        /// <summary>Flanged joints — IOGP datasheet 2. Per joint·year. Includes the
        /// gasket and 2 welds to the pipe. Ring-type-joint, spiral wound, clamp
        /// (Grayloc) and hammer union all map here. Spectacle blinds / orifice plates
        /// count as 1.5 flanged joints.</summary>
        FlangedJoint = 2,

        /// <summary>Manual valves — IOGP datasheet 3. Per valve·year. Block, bleed,
        /// check and choke valves; gate / ball / plug / globe / needle / butterfly.
        /// Excludes flanges, controls and instrumentation.</summary>
        ManualValve = 3,

        /// <summary>Actuated valves — IOGP datasheet 4. Per valve·year. Block,
        /// blowdown, choke, control, ESDV and relief valves; gate / ball / plug /
        /// globe / needle. Excludes pipeline ESDVs and SSIVs.</summary>
        ActuatedValve = 4,

        /// <summary>Instrument connections — IOGP datasheet 5. Per connection·year.
        /// Small-bore flow / pressure / temperature sensing connections; includes the
        /// instrument itself plus up to 2 valves, 4 flanged joints, 1 fitting and
        /// associated small-bore piping ≤ 1".</summary>
        InstrumentConnection = 5,

        /// <summary>Process (pressure) vessels — IOGP datasheet 6. Per vessel·year.
        /// Adsorber, knock-out drum, reboiler, scrubber, separator, stabiliser,
        /// distillation column. Excludes "horizontal other" / "vertical other"
        /// (use <see cref="PressureVesselOther"/>) and storage vessels.</summary>
        PressureVessel = 6,

        /// <summary>Centrifugal pumps — IOGP datasheet 7. Per pump·year. Single-seal
        /// and double-seal types (no statistical difference per IOGP §2.1.2.7).</summary>
        PumpCentrifugal = 7,

        /// <summary>Reciprocating pumps — IOGP datasheet 8. Per pump·year. Note that
        /// IOGP only publishes the 1992–2015 dataset for this type (small incident
        /// count).</summary>
        PumpReciprocating = 8,

        /// <summary>Centrifugal compressors — IOGP datasheet 9. Per compressor·year.
        /// One compressor = all stages on one shaft.</summary>
        CompressorCentrifugal = 9,

        /// <summary>Reciprocating compressors — IOGP datasheet 10. Per compressor·year.
        /// </summary>
        CompressorReciprocating = 10,

        /// <summary>Shell &amp; tube heat exchangers, hydrocarbon on the shell side —
        /// IOGP datasheet 11. Per heat-exchanger·year.</summary>
        HxShellTubeShellSide = 11,

        /// <summary>Shell &amp; tube heat exchangers, hydrocarbon on the tube side —
        /// IOGP datasheet 12. Per heat-exchanger·year.</summary>
        HxShellTubeTubeSide = 12,

        /// <summary>Plate heat exchangers — IOGP datasheet 13. Per heat-exchanger·year.
        /// Also covers printed circuit heat exchangers.</summary>
        HxPlate = 13,

        /// <summary>Air-cooled (fin-fan) heat exchangers — IOGP datasheet 14. Per
        /// heat-exchanger·year. 1992–2015 dataset only; high uncertainty.</summary>
        HxAirCooled = 14,

        /// <summary>Filters — IOGP datasheet 15. Per filter·year.</summary>
        Filter = 15,

        /// <summary>Pig traps (launchers / receivers) — IOGP datasheet 16. Per pig
        /// trap·year. Frequencies assume the trap is depressurised for some fraction
        /// of the year; scale if your operating profile differs significantly.</summary>
        PigTrap = 16,

        /// <summary>Flexible pipework — IOGP datasheet 17. Per metre·year.</summary>
        FlexiblePipe = 17,

        /// <summary>Other process (pressure) vessels — IOGP datasheet 18. Per
        /// vessel·year. Catches the HCRD "horizontal other" / "vertical other"
        /// categories, typically produced-water treatment vessels.</summary>
        PressureVesselOther = 18,

        /// <summary>Degassers — IOGP datasheet 19. Per vessel·year. 1992–2015 dataset
        /// only; high uncertainty.</summary>
        Degasser = 19,

        /// <summary>Expanders — IOGP datasheet 20. Per equipment·year. 1992–2015
        /// dataset only; high uncertainty.</summary>
        Expander = 20,

        /// <summary>Xmas trees — IOGP datasheet 21. Per tree·year. Includes valves,
        /// flanges, rams down to the wellhead connection and up to the first flange.
        /// </summary>
        XmasTree = 21,

        /// <summary>Turbines — IOGP datasheet 22. Per turbine·year.</summary>
        Turbine = 22,

        /// <summary>Pipeline ESDVs — IOGP datasheet 23. Per valve·year. Emergency
        /// shut-down valves on pipelines beyond the riser ESDV.</summary>
        PipelineEsdv = 23,

        /// <summary>SSIV (sub-sea isolation valve) assemblies — IOGP datasheet 24. Per
        /// assembly·year. High uncertainty.</summary>
        SsivAssembly = 24
    }

    /// <summary>
    /// Hole-size band used by IOGP 434-01 to bucket leak frequencies. The IOGP
    /// datasheets always publish exactly these 5 bands plus a TOTAL row.
    ///
    /// The "representative" hole diameter for QRA consequence modelling is the
    /// **geometric mean** of the band — IOGP §2.1.2 recommendation — exposed via
    /// <see cref="DisperSim3D.Core.IogpFrequencyTable.GeometricMeanHoleSizeMm"/>.
    /// </summary>
    public enum IogpHoleSizeBand
    {
        /// <summary>1 – 3 mm (geometric mean 1.73 mm).</summary>
        Tiny = 0,

        /// <summary>3 – 10 mm (geometric mean 5.48 mm).</summary>
        Small = 1,

        /// <summary>10 – 50 mm (geometric mean 22.36 mm). Default for typical
        /// process-leak scenarios.</summary>
        Medium = 2,

        /// <summary>50 – 150 mm (geometric mean 86.60 mm).</summary>
        Large = 3,

        /// <summary>&gt; 150 mm — full-bore rupture. Representative diameter is the
        /// nominal equipment ID (or 152.4 mm for 6" nominal per IOGP §2.1.2).</summary>
        Rupture = 4
    }
}
