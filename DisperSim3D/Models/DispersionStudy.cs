using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>
    /// A curated collection of dispersion simulations whose final-snapshot cloud
    /// volumes are combined into a single "what we need to detect" set, used by
    /// downstream <see cref="DetectorAllocation"/> blocks to size and place gas
    /// detectors.
    ///
    /// Each member simulation contributes ONE cloud — the iso-volume of the chosen
    /// <see cref="DetectionQuantity"/> at <see cref="DetectionThreshold"/>. For
    /// transient runs we read the last timestep only; for steady runs we read the
    /// (single) snapshot. Different simulations can use different gases — the
    /// quantity transform pulls each simulation's gas from its snapshot source.
    /// </summary>
    public class DispersionStudy
    {
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Category("Identity")]
        [Description("Display name shown in the project tree.")]
        public string Name { get; set; } = "Dispersion Study";

        [Category("Identity")]
        [Description("Optional free-text description (purpose, assumptions, etc.).")]
        public string Description { get; set; } = "";

        [Category("Membership")]
        [Description("IDs of Simulation entries included in this study. The final-snapshot " +
            "cloud of each is treated as one independent detection target.")]
        public List<string> SimulationIds { get; set; } = new List<string>();

        [Category("Detection criterion")]
        [Description("Quantity used to define each cloud (%LFL, ppm, ppb, kg/m³, K, mole fraction, mass fraction). " +
            "The quantity is computed per-simulation using that simulation's gas data.")]
        public ViewFieldProperty DetectionQuantity { get; set; } = ViewFieldProperty.PercentLFL;

        [Category("Detection criterion")]
        [Description("Threshold above which a cell is considered part of the cloud " +
            "(units of DetectionQuantity — e.g. 80 = 80 %LFL, 100 = 100 ppm).")]
        public double DetectionThreshold { get; set; } = 50.0;

        [Category("Identity")]
        [Description("Creation timestamp.")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Category("Visualization")]
        [Description("Whether the study's cloud envelopes are drawn in the 3D viewport when this study is the active selection.")]
        public bool IsVisible { get; set; } = true;

        /// <summary>Per-simulation risk metadata consumed by
        /// <see cref="AllocationStrategy.MinResidualRisk"/> allocations. Missing entries
        /// fall through to <see cref="ScenarioRisk"/>'s defaults (Auto / Auto). Lives on
        /// the study (not on the immutable <see cref="Simulation"/> snapshot) so the
        /// same simulation can carry different risk weights in different studies.</summary>
        [Browsable(false)]
        public Dictionary<string, ScenarioRisk> RiskWeights { get; set; }
            = new Dictionary<string, ScenarioRisk>();

        /// <summary>Returns the <see cref="ScenarioRisk"/> for <paramref name="simulationId"/>,
        /// inserting a default entry if none existed yet.</summary>
        public ScenarioRisk EnsureRiskFor(string simulationId)
        {
            if (string.IsNullOrEmpty(simulationId))
                return new ScenarioRisk();
            if (!RiskWeights.TryGetValue(simulationId, out var r) || r == null)
            {
                r = new ScenarioRisk();
                RiskWeights[simulationId] = r;
            }
            return r;
        }
    }

    /// <summary>Selects whether a risk-weight field is auto-derived (from IOGP +
    /// wind-rose for frequency, or cloud-volume × hazard heuristic for consequence)
    /// or supplied manually by the engineer.</summary>
    public enum RiskValueMode
    {
        /// <summary>Auto-derive on every allocation run.</summary>
        Auto = 0,
        /// <summary>Use the explicit numeric override.</summary>
        Manual = 1
    }

    /// <summary>Per-simulation entry in <see cref="DispersionStudy.RiskWeights"/>.
    /// Carries frequency (events/year) and consequence weight for the
    /// <see cref="AllocationStrategy.MinResidualRisk"/> objective
    /// `R_s = freq_s × cons_s × P_d`.</summary>
    public sealed class ScenarioRisk
    {
        /// <summary>Whether <see cref="FreqPerYear"/> is auto-derived or manual.</summary>
        public RiskValueMode FreqMode { get; set; } = RiskValueMode.Auto;

        /// <summary>Manual frequency override in events per year. Consulted only
        /// when <see cref="FreqMode"/> = <see cref="RiskValueMode.Manual"/>.</summary>
        public double FreqPerYear { get; set; } = 1.0;

        /// <summary>Whether <see cref="Consequence"/> is auto-derived or manual.</summary>
        public RiskValueMode ConsMode { get; set; } = RiskValueMode.Auto;

        /// <summary>Manual consequence weight (positive scalar — relative severity).
        /// Consulted only when <see cref="ConsMode"/> = <see cref="RiskValueMode.Manual"/>.</summary>
        public double Consequence { get; set; } = 1.0;
    }
}
