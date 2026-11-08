using System;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Combustion data of the common process gases, looked up by name, for gases whose
    /// project entry leaves a combustion field at zero (every file written before these
    /// fields existed).
    ///
    /// <para><b>Heat of combustion, soot and CO yields:</b> Tewarson, "Generation of Heat
    /// and Chemical Compounds in Fires", SFPE Handbook of Fire Protection Engineering,
    /// 3rd ed., 2002, Table 3-4.14 — the net heat of complete combustion and the yields
    /// in well-ventilated flaming combustion, kg per kg of fuel burnt. Tewarson tabulates
    /// no CO or soot yield for methane; its flame is close to soot-free and both are
    /// zero here. Hydrogen, ammonia and hydrogen sulphide carry no carbon, so neither
    /// can form; their heats of combustion are the lower heating values.</para>
    ///
    /// <para><b>Laminar burning velocity:</b> the peak over equivalence ratio, at 25 °C
    /// and 1 atm. Hydrocarbons and hydrogen: the fundamental burning velocities of
    /// NFPA 68 (2018), Annex D. Ammonia: Hayakawa et al., Fuel 159 (2015) 98–106.
    /// Hydrogen sulphide has no value here.</para>
    /// </summary>
    public sealed class FuelCombustionData
    {
        public FuelCombustionData(double heatOfCombustionJPerKg, double carbonAtoms, double hydrogenAtoms,
            double sootYield, double coYield, double laminarBurningVelocityMPerS)
        {
            HeatOfCombustionJPerKg = heatOfCombustionJPerKg;
            CarbonAtoms = carbonAtoms;
            HydrogenAtoms = hydrogenAtoms;
            SootYield = sootYield;
            CoYield = coYield;
            LaminarBurningVelocityMPerS = laminarBurningVelocityMPerS;
        }

        public double HeatOfCombustionJPerKg { get; }
        public double CarbonAtoms { get; }
        public double HydrogenAtoms { get; }
        public double SootYield { get; }
        public double CoYield { get; }
        public double LaminarBurningVelocityMPerS { get; }

        public static readonly FuelCombustionData Methane = new FuelCombustionData(50.1e6, 1, 4, 0, 0, 0.40);
        public static readonly FuelCombustionData Ethane = new FuelCombustionData(47.1e6, 2, 6, 0.013, 0.001, 0.47);
        public static readonly FuelCombustionData Propane = new FuelCombustionData(46.0e6, 3, 8, 0.024, 0.005, 0.46);
        public static readonly FuelCombustionData Butane = new FuelCombustionData(45.4e6, 4, 10, 0.029, 0.007, 0.45);
        public static readonly FuelCombustionData Ethylene = new FuelCombustionData(48.0e6, 2, 4, 0.043, 0.013, 0.80);
        public static readonly FuelCombustionData Propylene = new FuelCombustionData(46.0e6, 3, 6, 0.095, 0.017, 0.66);
        public static readonly FuelCombustionData Hydrogen = new FuelCombustionData(120.0e6, 0, 2, 0, 0, 3.12);
        public static readonly FuelCombustionData Ammonia = new FuelCombustionData(18.6e6, 0, 3, 0, 0, 0.07);
        public static readonly FuelCombustionData HydrogenSulfide = new FuelCombustionData(15.2e6, 0, 2, 0, 0, 0);

        /// <summary>
        /// <paramref name="explicitValue"/> when it is set (positive), else the value the
        /// table gives for <paramref name="name"/>, else zero.
        /// </summary>
        public static double Resolve(double explicitValue, string name, Func<FuelCombustionData, double> field)
        {
            if (explicitValue > 0) return explicitValue;
            var data = ForName(name);
            return data != null ? field(data) : 0;
        }

        /// <summary>The data for a gas name (English or Portuguese, or its formula); null when unknown.</summary>
        public static FuelCombustionData ForName(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (n.Length == 0) return null;
            // Hydrogen sulphide before hydrogen, which its names contain.
            if (n.Contains("h2s") || n.Contains("sulfeto") || n.Contains("sulfide") || n.Contains("sulphide")) return HydrogenSulfide;
            if (n.Contains("hydrogen") || n.Contains("hidrog") || n == "h2") return Hydrogen;
            if (n.Contains("ammonia") || n.Contains("amônia") || n.Contains("amonia") || n.Contains("nh3")) return Ammonia;
            if (n.Contains("propylene") || n.Contains("propene") || n.Contains("propeno") || n.Contains("propileno") || n.Contains("c3h6")) return Propylene;
            if (n.Contains("ethylene") || n.Contains("ethene") || n.Contains("eteno") || n.Contains("etileno") || n.Contains("c2h4")) return Ethylene;
            if (n.Contains("propane") || n.Contains("propano") || n.Contains("c3h8") || n.Contains("lpg") || n.Contains("glp")) return Propane;
            if (n.Contains("butane") || n.Contains("butano") || n.Contains("c4h10")) return Butane;
            // Methane before ethane, which its names contain.
            if (n.Contains("methane") || n.Contains("metano") || n.Contains("ch4") || n.Contains("lng") || n.Contains("gnl")
                || n.Contains("natural gas") || n.Contains("gás natural") || n.Contains("gas natural")) return Methane;
            if (n.Contains("ethane") || n.Contains("etano") || n.Contains("c2h6")) return Ethane;
            return null;
        }
    }
}
