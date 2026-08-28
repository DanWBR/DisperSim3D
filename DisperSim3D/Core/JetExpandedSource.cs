using System;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Expanded (effective) source condition for a gas jet — the fictitious larger,
    /// slower orifice at atmospheric pressure that carries the same mass, momentum and
    /// energy as the real one.
    ///
    /// <para>A jet fire's shape is governed by the ratio of buoyancy to momentum, and
    /// that ratio has to be evaluated where the jet has finished expanding, not at the
    /// hole. A 20 mm hole at 66 barg and a 152 mm hole at 0.3 barg can pass similar mass
    /// flows and produce completely different flames.</para>
    ///
    /// <para>Equations follow Miller (2017) §"Jet release expanded source condition for
    /// choked flow releases", equations (1)-(3), with the throat taken as the isentropic
    /// sonic condition when the release is choked. For a subsonic release the throat is
    /// already at atmospheric pressure and the expanded source is the orifice itself.</para>
    ///
    /// <para>This is distinct from
    /// <see cref="HighPressureLeakModel.ComputeExpandedSource"/>, which sizes a pseudo-source
    /// for a CFD mesh by fixing the velocity at a convenient value. That one answers "what
    /// inlet patch should I draw"; this one answers "how fast is the gas actually moving",
    /// which is the question the flame shape depends on.</para>
    /// </summary>
    public static class JetExpandedSource
    {
        private const double R = 8.31446;

        public readonly struct Result
        {
            /// <summary>Expanded source diameter (m).</summary>
            public readonly double DiameterM;
            /// <summary>Expanded source velocity (m/s).</summary>
            public readonly double VelocityMS;
            /// <summary>Expanded source temperature (K).</summary>
            public readonly double TemperatureK;
            /// <summary>Expanded source density (kg/m³).</summary>
            public readonly double DensityKgM3;
            /// <summary>True when the release was choked at the orifice.</summary>
            public readonly bool IsChoked;

            public Result(double diameter, double velocity, double temperature,
                double density, bool isChoked)
            {
                DiameterM = diameter; VelocityMS = velocity; TemperatureK = temperature;
                DensityKgM3 = density; IsChoked = isChoked;
            }
        }

        /// <summary>
        /// Resolves the expanded source.
        /// </summary>
        /// <param name="massFlowKgS">Release rate (kg/s).</param>
        /// <param name="orificeDiameterM">Real hole diameter (m).</param>
        /// <param name="stagnationPressurePa">Absolute stagnation pressure upstream (Pa).
        /// Pass 0 or anything at or below ambient to treat the release as subsonic, in
        /// which case the orifice itself is the expanded source.</param>
        /// <param name="stagnationTemperatureK">Stagnation temperature upstream (K).</param>
        /// <param name="molarMassKgMol">Fuel molar mass (kg/mol).</param>
        /// <param name="gamma">Ratio of specific heats. 1.31 for methane.</param>
        /// <param name="ambientPressurePa">Atmospheric pressure (Pa).</param>
        public static Result Compute(double massFlowKgS, double orificeDiameterM,
            double stagnationPressurePa, double stagnationTemperatureK,
            double molarMassKgMol, double gamma = 1.31, double ambientPressurePa = 101325.0)
        {
            double m = molarMassKgMol > 0 ? molarMassKgMol : 0.016;
            double t0 = stagnationTemperatureK > 1 ? stagnationTemperatureK : 288.15;
            double area = Math.PI * 0.25 * orificeDiameterM * orificeDiameterM;
            if (area <= 0 || massFlowKgS <= 0)
                return new Result(orificeDiameterM, 0, t0, 0, false);

            // Critical pressure ratio: below it the orifice is not choked and the gas
            // leaves at atmospheric pressure, so there is nothing to expand.
            double criticalRatio = Math.Pow((gamma + 1.0) / 2.0, gamma / (gamma - 1.0));
            bool choked = stagnationPressurePa > ambientPressurePa * criticalRatio;

            if (!choked)
            {
                double rhoExit = ambientPressurePa * m / (R * t0);
                double uExit = massFlowKgS / (rhoExit * area);
                return new Result(orificeDiameterM, uExit, t0, rhoExit, false);
            }

            // ── Throat: isentropic sonic conditions ─────────────────────────
            double tThroat = t0 * 2.0 / (gamma + 1.0);
            double pThroat = stagnationPressurePa / criticalRatio;
            double rhoThroat = pThroat * m / (R * tThroat);
            double uThroat = Math.Sqrt(gamma * R * tThroat / m);

            // ── Expansion to atmospheric pressure, Miller (2017) eq. (1)-(3) ─
            // (1) momentum: the pressure left over at the throat accelerates the gas.
            double uExp = uThroat + (pThroat - ambientPressurePa) / (uThroat * rhoThroat);

            // (2) energy: that acceleration comes out of the enthalpy, cooling the gas.
            double cp = gamma * R / ((gamma - 1.0) * m);           // J/(kg·K)
            double tExp = tThroat - 0.5 * (uExp * uExp - uThroat * uThroat) / cp;
            if (tExp < 50) tExp = 50;                              // physical floor

            // (3) mass: the diameter that carries the flow at that state.
            double rhoExp = ambientPressurePa * m / (R * tExp);
            double areaExp = massFlowKgS / (rhoExp * uExp);
            double dExp = Math.Sqrt(4.0 * areaExp / Math.PI);

            return new Result(dExp, uExp, tExp, rhoExp, true);
        }

        /// <summary>
        /// Richardson number at the expanded source, Chamberlain's ξ(L) — the ratio of
        /// buoyancy to momentum flux over the flame length. Low values mean a
        /// momentum-dominated jet that stays straight; high values mean a flame that
        /// arcs upward within its own length.
        /// </summary>
        /// <param name="expanded">Expanded source condition.</param>
        /// <param name="flameLengthM">Zero-wind flame length (m).</param>
        /// <param name="ambientDensityKgM3">Air density (kg/m³).</param>
        public static double RichardsonNumber(Result expanded, double flameLengthM,
            double ambientDensityKgM3 = 1.2)
        {
            if (expanded.VelocityMS <= 0 || expanded.DensityKgM3 <= 0 || flameLengthM <= 0)
                return 0;

            // Equivalent air source diameter: the diameter a jet of ambient-density gas
            // would need to carry the same momentum.
            double ds = expanded.DiameterM * Math.Sqrt(expanded.DensityKgM3 / ambientDensityKgM3);
            if (ds <= 0) return 0;

            double denominator = ds * ds * expanded.VelocityMS * expanded.VelocityMS;
            if (denominator <= 0) return 0;
            return Math.Pow(9.81 / denominator, 1.0 / 3.0) * flameLengthM;
        }
    }
}
