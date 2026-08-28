using System;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Thermal dose and harm probability.
    ///
    /// A radiant flux is not a consequence: 12.5 kW/m² for two seconds and for two
    /// minutes are different events. The dose <c>V = t·I^(4/3)</c> combines the two,
    /// and a probit turns the dose into a probability of harm.
    ///
    /// <para>Every probit here takes the dose with <b>I in W/m²</b> and <b>t in
    /// seconds</b> — the constants are calibrated for those units and give nonsense in
    /// kW/m². <see cref="Dose"/> does the conversion, so callers work in the kW/m² the
    /// rest of the engine uses.</para>
    ///
    /// <para>References: Eisenberg et al. (1975) for the fatality probit as quoted by
    /// CCPS; TNO Green Book (CPR 16E) for the burn probits and the dose form.</para>
    /// </summary>
    public static class ThermalDose
    {
        /// <summary>Probit value at 1% probability of harm.</summary>
        public const double Probit1Percent = 2.67;

        /// <summary>Probit value at 50% probability of harm.</summary>
        public const double Probit50Percent = 5.0;

        /// <summary>
        /// Thermal dose <c>V = t·I^(4/3)</c> in (W/m²)^(4/3)·s.
        /// </summary>
        /// <param name="fluxKwM2">Incident radiant flux (kW/m²).</param>
        /// <param name="exposureS">Exposure time (s).</param>
        public static double Dose(double fluxKwM2, double exposureS)
        {
            if (fluxKwM2 <= 0 || exposureS <= 0) return 0;
            double fluxWm2 = fluxKwM2 * 1000.0;
            return exposureS * Math.Pow(fluxWm2, 4.0 / 3.0);
        }

        /// <summary>
        /// Eisenberg fatality probit: <c>Y = −14.9 + 2.56·ln(V/10⁴)</c>. Anchors worth
        /// remembering: 20 s at ~18 kW/m² is 1% lethality, 20 s at ~36 kW/m² is 50%.
        /// </summary>
        public static double FatalityProbit(double dose)
            => dose <= 0 ? double.NegativeInfinity : -14.9 + 2.56 * Math.Log(dose / 1.0e4);

        /// <summary>TNO probit for first-degree burns: <c>Y = −39.83 + 3.0186·ln(V)</c>.</summary>
        public static double FirstDegreeBurnProbit(double dose)
            => dose <= 0 ? double.NegativeInfinity : -39.83 + 3.0186 * Math.Log(dose);

        /// <summary>TNO probit for second-degree burns: <c>Y = −43.14 + 3.0188·ln(V)</c>.</summary>
        public static double SecondDegreeBurnProbit(double dose)
            => dose <= 0 ? double.NegativeInfinity : -43.14 + 3.0188 * Math.Log(dose);

        /// <summary>
        /// Probit to probability: <c>P = ½·(1 + erf((Y − 5)/√2))</c>. A probit of 5 is
        /// 50% by construction, 2.67 is 1%.
        /// </summary>
        public static double ProbitToProbability(double probit)
        {
            if (double.IsNegativeInfinity(probit)) return 0;
            double p = 0.5 * (1.0 + Erf((probit - 5.0) / Math.Sqrt(2.0)));
            return p < 0 ? 0 : (p > 1 ? 1 : p);
        }

        /// <summary>
        /// Error function, Abramowitz &amp; Stegun 7.1.26 — maximum absolute error
        /// 1.5×10⁻⁷. There is no Math.Erf in .NET and the engine had no implementation.
        /// </summary>
        public static double Erf(double x)
        {
            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;
            const double p = 0.3275911;

            int sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);

            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            return sign * y;
        }

        /// <summary>Fatality probability straight from flux and exposure.</summary>
        public static double FatalityProbability(double fluxKwM2, double exposureS)
            => ProbitToProbability(FatalityProbit(Dose(fluxKwM2, exposureS)));

        /// <summary>
        /// Flux (kW/m²) that produces a given probit at a given exposure — the inverse
        /// of <see cref="FatalityProbit"/>. Useful for labelling a contour: at 20 s,
        /// <c>FluxForFatalityProbit(Probit1Percent, 20)</c> is the 1% lethality level.
        /// </summary>
        public static double FluxForFatalityProbit(double probit, double exposureS)
        {
            if (exposureS <= 0) return double.PositiveInfinity;
            double dose = 1.0e4 * Math.Exp((probit + 14.9) / 2.56);
            double fluxWm2 = Math.Pow(dose / exposureS, 3.0 / 4.0);
            return fluxWm2 / 1000.0;
        }

        // ── Fields ──────────────────────────────────────────────────────────

        /// <summary>Dose field from a flux field and one exposure time for every cell —
        /// the jet and pool fire case, where the exposure is how long a person takes to
        /// get out rather than a property of the fire.</summary>
        public static double[,,] BuildDoseField(double[,,] fluxKwM2, double exposureS)
        {
            if (fluxKwM2 == null) return null;
            int nx = fluxKwM2.GetLength(0), ny = fluxKwM2.GetLength(1), nz = fluxKwM2.GetLength(2);
            var output = new double[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        output[i, j, k] = Dose(fluxKwM2[i, j, k], exposureS);
            return output;
        }

        /// <summary>
        /// Dose field with a per-cell exposure — the flash-fire case, where the time the
        /// flame takes to reach a cell comes out of
        /// <see cref="FlashFireEngine"/>. Cells the flame never reaches
        /// (<see cref="FlashFireEngine.UnreachedArrivalS"/>) get zero dose from this
        /// term.
        /// </summary>
        public static double[,,] BuildDoseField(double[,,] fluxKwM2, double[,,] exposureFieldS)
        {
            if (fluxKwM2 == null || exposureFieldS == null) return null;
            int nx = fluxKwM2.GetLength(0), ny = fluxKwM2.GetLength(1), nz = fluxKwM2.GetLength(2);
            var output = new double[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        double exposure = exposureFieldS[i, j, k];
                        if (exposure >= FlashFireEngine.UnreachedArrivalS) continue;
                        output[i, j, k] = Dose(fluxKwM2[i, j, k], exposure);
                    }
            return output;
        }

        /// <summary>Fatality probability field (0–1) from a dose field.</summary>
        public static double[,,] BuildFatalityField(double[,,] doseField)
        {
            if (doseField == null) return null;
            int nx = doseField.GetLength(0), ny = doseField.GetLength(1), nz = doseField.GetLength(2);
            var output = new double[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        output[i, j, k] = ProbitToProbability(FatalityProbit(doseField[i, j, k]));
            return output;
        }

        /// <summary>
        /// Volume (m³) where the fatality probability is at or above
        /// <paramref name="probabilityThreshold"/>. Without a population model this is
        /// the honest consequence metric — an exposed footprint, not a body count.
        /// </summary>
        public static double FootprintVolumeM3(double[,,] fatalityField, double cellVolumeM3,
            double probabilityThreshold = 0.01)
        {
            if (fatalityField == null) return 0;
            int nx = fatalityField.GetLength(0), ny = fatalityField.GetLength(1), nz = fatalityField.GetLength(2);
            double volume = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        if (fatalityField[i, j, k] >= probabilityThreshold)
                            volume += cellVolumeM3;
            return volume;
        }
    }
}
