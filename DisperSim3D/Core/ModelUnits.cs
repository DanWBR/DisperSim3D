using System;

namespace DisperSim3D.Core
{
    /// <summary>
    /// The unit an imported mesh was authored in.
    ///
    /// <para>STL and OBJ carry no unit. The numbers in the file are bare, and the
    /// scene works in metres, so something has to decide whether a vessel that reads
    /// 40 000 across is 40 km or 40 m. Authors get this wrong often enough that
    /// guessing silently is worse than asking: the importer picks the likeliest unit
    /// and the user confirms or overrides it.</para>
    /// </summary>
    public enum ModelUnit
    {
        /// <summary>File units are millimetres.</summary>
        Millimetres = 0,
        /// <summary>File units are centimetres.</summary>
        Centimetres = 1,
        /// <summary>File units are metres. The scene's own unit.</summary>
        Metres = 2,
        /// <summary>File units are kilometres.</summary>
        Kilometres = 3,
        /// <summary>Scale set by hand, matching no named unit.</summary>
        Custom = 4
    }

    /// <summary>
    /// Converts between an authored mesh unit and the metres the scene works in.
    /// </summary>
    public static class ModelUnits
    {
        /// <summary>Metres per file unit, indexed by <see cref="ModelUnit"/>.</summary>
        private static readonly double[] Factors = { 0.001, 0.01, 1.0, 1000.0 };

        /// <summary>Display labels, indexed by <see cref="ModelUnit"/>.</summary>
        public static readonly string[] Labels =
        {
            "Millimetres (mm)",
            "Centimetres (cm)",
            "Metres (m)",
            "Kilometres (km)",
            "Custom"
        };

        /// <summary>
        /// Scale factor that takes file units to metres. <see cref="ModelUnit.Custom"/>
        /// has no fixed factor and returns 1.
        /// </summary>
        public static double FactorFor(ModelUnit unit)
        {
            int i = (int)unit;
            return i >= 0 && i < Factors.Length ? Factors[i] : 1.0;
        }

        /// <summary>
        /// Likeliest unit for a model whose largest side measures
        /// <paramref name="maxExtent"/> file units.
        ///
        /// <para>The bet is that an imported object is plant-sized: a vessel, a pipe
        /// rack, a building, somewhere between waist height and a few hundred metres.
        /// Whichever unit puts the model in that band is the one the author probably
        /// meant. It is a starting guess, not an answer.</para>
        /// </summary>
        public static ModelUnit Guess(double maxExtent)
        {
            if (maxExtent <= 0 || double.IsNaN(maxExtent) || double.IsInfinity(maxExtent))
                return ModelUnit.Metres;         // nothing to go on
            if (maxExtent > 5000) return ModelUnit.Millimetres;   // 40 000 across
            if (maxExtent > 500) return ModelUnit.Centimetres;    // 4 000 across
            if (maxExtent < 0.05) return ModelUnit.Kilometres;    // 0.04 across
            return ModelUnit.Metres;
        }

        /// <summary>
        /// The named unit a scale factor corresponds to, or
        /// <see cref="ModelUnit.Custom"/> when it matches none of them. Used to drop a
        /// unit picker back to Custom once the user dials the scale by hand.
        /// </summary>
        public static ModelUnit Match(double scale)
        {
            for (int i = 0; i < Factors.Length; i++)
                if (Math.Abs(scale - Factors[i]) < Factors[i] * 1e-6)
                    return (ModelUnit)i;
            return ModelUnit.Custom;
        }
    }
}
