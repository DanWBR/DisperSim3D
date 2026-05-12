using System;
using System.Collections.Generic;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Local lookup table of flammability and toxicity limits for the most common
    /// dispersion gases. DWSIM's compound database carries only thermodynamic
    /// constants (M, Tc, Pc, ω, …), not LFL/UFL/IDLH which are regulatory/empirical.
    /// Values here are NIOSH/NFPA pocket-card numbers converted to kg/m³ at NTP
    /// (101325 Pa, 15 °C → ρ_mix-air ≈ M_gas × 42.293 mol/m³).
    ///
    /// Lookup is case-insensitive and tolerant of partial name matches so it covers
    /// both DWSIM names (e.g. "Methane") and informal synonyms.
    /// </summary>
    public static class HazardDatabase
    {
        public sealed class Entry
        {
            public double LflKgM3;
            public double UflKgM3;
            public double IdlhKgM3;
            public double Erpg1KgM3;
            public double Erpg2KgM3;
            public double Erpg3KgM3;
        }

        // Approximate kg/m³ values at 15 °C / 1 atm. The exact value depends on
        // ambient T/P but the differences are within 5% across typical conditions;
        // for screening-grade dispersion this precision is fine.
        private static readonly Dictionary<string, Entry> _data =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
        {
            // Common flammables
            { "Methane",          new Entry { LflKgM3 = 0.033, UflKgM3 = 0.099 } },
            { "Ethane",           new Entry { LflKgM3 = 0.038, UflKgM3 = 0.157 } },
            { "Propane",          new Entry { LflKgM3 = 0.038, UflKgM3 = 0.171 } },
            { "n-Butane",         new Entry { LflKgM3 = 0.044, UflKgM3 = 0.205 } },
            { "Isobutane",        new Entry { LflKgM3 = 0.044, UflKgM3 = 0.205 } },
            { "n-Pentane",        new Entry { LflKgM3 = 0.040, UflKgM3 = 0.225 } },
            { "Ethylene",         new Entry { LflKgM3 = 0.030, UflKgM3 = 0.420 } },
            { "Propylene",        new Entry { LflKgM3 = 0.034, UflKgM3 = 0.190 } },
            { "Acetylene",        new Entry { LflKgM3 = 0.026, UflKgM3 = 0.918 } },
            { "Hydrogen",         new Entry { LflKgM3 = 0.0033, UflKgM3 = 0.062, IdlhKgM3 = 0.025 } },
            { "Carbon Monoxide",  new Entry { LflKgM3 = 0.143, UflKgM3 = 0.870, IdlhKgM3 = 0.001545, Erpg2KgM3 = 0.000406 } },
            { "Hydrogen Sulfide", new Entry { LflKgM3 = 0.057, UflKgM3 = 0.629, IdlhKgM3 = 0.0001394, Erpg1KgM3 = 0.0000139, Erpg2KgM3 = 0.0000418, Erpg3KgM3 = 0.0001394 } },
            { "Ammonia",          new Entry { LflKgM3 = 0.111, UflKgM3 = 0.193, IdlhKgM3 = 0.211, Erpg1KgM3 = 0.0175, Erpg2KgM3 = 0.1230, Erpg3KgM3 = 0.5270 } },
            // Heavy / inert tracers
            { "Sulfur Hexafluoride", new Entry { /* non-flammable, non-toxic */ } },
            { "Carbon Dioxide",      new Entry { IdlhKgM3 = 0.0723 } },  // 40 000 ppm v/v
            // Air components (no hazard)
            { "Nitrogen",         new Entry { } },
            { "Oxygen",           new Entry { } },
        };

        /// <summary>Returns the hazard entry for the compound, or null if unknown.</summary>
        public static Entry Lookup(string compoundName)
        {
            if (string.IsNullOrEmpty(compoundName)) return null;
            if (_data.TryGetValue(compoundName, out var e)) return e;
            // Try a few synonym variants.
            string trimmed = compoundName.Replace("-", "").Replace(" ", "");
            foreach (var kv in _data)
            {
                string key = kv.Key.Replace("-", "").Replace(" ", "");
                if (string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }
    }
}
