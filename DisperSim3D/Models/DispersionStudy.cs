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
    }
}
