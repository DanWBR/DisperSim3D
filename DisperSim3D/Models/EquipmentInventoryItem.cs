using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>
    /// One row in a <see cref="ReleaseSource3D.EquipmentInventory"/>: a group of
    /// identical pieces of equipment contributing to the source's leak frequency
    /// via the IOGP 434-01 table.
    ///
    /// For pipe types (<see cref="IogpEquipmentType.SteelProcessPipe"/> and
    /// <see cref="IogpEquipmentType.FlexiblePipe"/>) the <see cref="Count"/> field
    /// is interpreted as **total length in metres** because IOGP reports those
    /// frequencies per metre·year. For everything else it is a unit count.
    ///
    /// The source's overall leak frequency is the sum of
    /// <c>IogpFrequencyTable.FrequencyFor(item.Type, item.NominalDiameterMm, holeBand) × item.Count</c>
    /// over the inventory, where <c>holeBand</c> is the
    /// <see cref="ReleaseSource3D.HoleSizeBand"/> on the parent source.
    /// </summary>
    public class EquipmentInventoryItem
    {
        /// <summary>IOGP 434-01 equipment category for this group. Picks one of the
        /// 24 datasheets in the IOGP report.</summary>
        [Category("IOGP")]
        [Description("IOGP 434-01 equipment type for this inventory entry. Selects which leak-frequency datasheet is consulted.")]
        public IogpEquipmentType Type { get; set; } = IogpEquipmentType.SteelProcessPipe;

        /// <summary>Nominal equipment diameter in millimetres. IOGP datasheets are
        /// tabulated at 6 anchor sizes: 50 / 150 / 300 / 450 / 600 / 900 mm (2" / 6" /
        /// 12" / 18" / 24" / 36"). Values between anchors are linearly interpolated by
        /// the <see cref="DisperSim3D.Core.IogpFrequencyTable"/>; values outside the
        /// range clamp to the nearest anchor.</summary>
        [Category("IOGP")]
        [Description("Nominal diameter (mm). IOGP tables anchor at 50/150/300/450/600/900 mm; values between are interpolated.")]
        public double NominalDiameterMm { get; set; } = 150;  // 6" default

        /// <summary>For pipe types (SteelProcessPipe / FlexiblePipe) this is the
        /// **total length in metres** of that pipe size at this source. For every
        /// other equipment type it is the **count** of identical items.</summary>
        [Category("IOGP")]
        [Description("Pipe length in metres for pipe types; count of items for everything else.")]
        public double Count { get; set; } = 1.0;

        /// <summary>Free-text label shown in the inventory grid — purely for the
        /// engineer's bookkeeping (e.g. "Discharge manifold to FCC riser").</summary>
        [Category("IOGP")]
        [Description("Optional free-text label shown in the inventory grid.")]
        public string Note { get; set; } = "";

        /// <summary>Returns true when <see cref="Count"/> represents pipe length in
        /// metres. False when it represents an integer count of items.</summary>
        public bool IsPipeLength =>
            Type == IogpEquipmentType.SteelProcessPipe ||
            Type == IogpEquipmentType.FlexiblePipe;
    }
}
