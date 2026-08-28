using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>
    /// A curated collection of fire scenarios ranked by risk — the fire counterpart of
    /// <see cref="DispersionStudy"/>.
    ///
    /// It is a separate object rather than a flag on the dispersion study because the
    /// two answer different questions from different inputs. A dispersion study bundles
    /// simulations whose clouds a detector has to see; a fire study bundles the fires
    /// themselves — jet and pool fires from <see cref="FireScenario.Sources"/>, and
    /// flash fires from <see cref="IgnitionEvent"/>s — and asks how much of the plant
    /// each one can harm, how often, and which of them dominates.
    ///
    /// Risk is frequency × consequence per scenario, the same product the dispersion
    /// side uses. Consequence is an exposed footprint, not a body count: with no
    /// population model in the project, the volume where harm exceeds the study's
    /// threshold is the honest measure.
    /// </summary>
    public class FireStudy
    {
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Category("Identity")]
        [Description("Display name shown in the project tree.")]
        public string Name { get; set; } = "Fire Study";

        [Category("Identity")]
        [Description("Optional free-text description (purpose, assumptions, etc.).")]
        public string Description { get; set; } = "";

        [Category("Identity")]
        [Description("Creation timestamp.")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Category("Membership")]
        [Description("IDs of FireSource entries included in this study. Each contributes " +
            "one scenario, scored on its thermal radiation footprint.")]
        public List<string> FireSourceIds { get; set; } = new List<string>();

        [Category("Membership")]
        [Description("IDs of IgnitionEvent entries included in this study. Each contributes " +
            "one flash-fire scenario, scored on the envelope its ignition burns.")]
        public List<string> IgnitionIds { get; set; } = new List<string>();

        [Category("Harm criterion")]
        [Description("Quantity that defines the harm footprint: FatalityProbability (default), " +
            "ThermalDose, or ThermalRadiationKwM2 for a plain flux contour.")]
        public ViewFieldProperty HarmQuantity { get; set; } = ViewFieldProperty.FatalityProbability;

        [Category("Harm criterion")]
        [Description("Threshold above which a cell counts as harmed, in units of HarmQuantity. " +
            "The default 0.01 is 1% lethality; use 12.5 or 37.5 with ThermalRadiationKwM2.")]
        public double HarmThreshold { get; set; } = 0.01;

        [Category("Evaluation grid")]
        [Description("Half-width of the domain the footprint is integrated over, in metres.")]
        public double DomainHalfM { get; set; } = 100.0;

        [Category("Evaluation grid")]
        [Description("Cells per side of the evaluation grid. The footprint is a cell count " +
            "times a cell volume, so a coarse grid quantises it.")]
        public int GridResolution { get; set; } = 40;

        [Category("Frequency")]
        [Description("Probability that a release ignites, applied to the leak frequency of the " +
            "source behind a flash-fire scenario. 0.1 is a common screening value; site-specific " +
            "studies derive it from the ignition source inventory.")]
        public double IgnitionProbability { get; set; } = 0.1;

        [Category("Visualization")]
        [Description("Whether the study's footprints are drawn in the 3D viewport when selected.")]
        public bool IsVisible { get; set; } = true;

        /// <summary>Per-scenario risk metadata, keyed by fire source or ignition Id.
        /// Auto/Auto by default; a jet fire placed by hand has no leak frequency to
        /// derive from, so those usually need the frequency set manually.</summary>
        [Browsable(false)]
        public Dictionary<string, ScenarioRisk> RiskWeights { get; set; }
            = new Dictionary<string, ScenarioRisk>();

        /// <summary>Returns the <see cref="ScenarioRisk"/> for a scenario id, inserting a
        /// default entry if none existed yet.</summary>
        public ScenarioRisk EnsureRiskFor(string scenarioId)
        {
            if (string.IsNullOrEmpty(scenarioId)) return new ScenarioRisk();
            if (!RiskWeights.TryGetValue(scenarioId, out var r) || r == null)
            {
                r = new ScenarioRisk();
                RiskWeights[scenarioId] = r;
            }
            return r;
        }
    }
}
