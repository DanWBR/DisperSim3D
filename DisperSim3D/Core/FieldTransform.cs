using System;
using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Converts a raw CFD scalar field (typically the released-species mass fraction
    /// Y_i, kg_species / kg_mixture) into one of the human-friendly quantities the
    /// user can pick for Views, Detectors, and Monitors — %LFL, %UFL, ppm, ppb,
    /// mole fraction, mass concentration, thermal radiation, etc.
    ///
    /// Ambient density and mixture-molar-mass approximations: for dilute releases
    /// (Y_i &lt;&lt; 1) we use M_mix ≈ M_air = 28.97 g/mol and ρ_mix ≈ 1.205 kg/m³
    /// (standard atmosphere). At higher concentrations these would need real-time
    /// updates, but the dispersion solvers we target keep mass fractions well below
    /// 0.1 in the plume body where the user actually cares about the threshold.
    /// </summary>
    public static class FieldTransform
    {
        private const double MAirKgMol  = 0.02897;
        private const double RhoAir     = 1.205;       // kg/m³ at 15 °C, 101325 Pa

        /// <summary>True when the requested property needs the released-species mass
        /// fraction as the source field. False for fields like temperature, pressure,
        /// or thermal radiation that read a different field or are computed analytically.</summary>
        public static bool NeedsSpeciesField(ViewFieldProperty p)
        {
            switch (p)
            {
                case ViewFieldProperty.Concentration:
                case ViewFieldProperty.MassFraction:
                case ViewFieldProperty.MoleFraction:
                case ViewFieldProperty.ConcentrationKgM3:
                case ViewFieldProperty.ConcentrationPpm:
                case ViewFieldProperty.ConcentrationPpb:
                case ViewFieldProperty.PercentLFL:
                case ViewFieldProperty.PercentUFL:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the property is computed analytically from the scene,
        /// not sampled from the CFD result (e.g. thermal radiation from FireSources).</summary>
        public static bool IsAnalytic(ViewFieldProperty p)
            => p == ViewFieldProperty.ThermalRadiationKwM2;

        /// <summary>Per-cell transform from raw mass fraction Y_i to the requested
        /// quantity. <paramref name="gas"/> must carry MolarMass, LFL, UFL (when the
        /// chosen quantity uses them). Returns the input array unchanged when
        /// <paramref name="target"/> is MassFraction / Concentration, to avoid an
        /// unnecessary allocation in the common case.</summary>
        public static double[,,] FromMassFraction(double[,,] yField, ViewFieldProperty target, GasProperties gas)
        {
            if (yField == null) return null;
            if (target == ViewFieldProperty.MassFraction || target == ViewFieldProperty.Concentration)
                return yField;

            int nx = yField.GetLength(0), ny = yField.GetLength(1), nz = yField.GetLength(2);
            double Mi = gas != null && gas.MolarMass > 0 ? gas.MolarMass : 0.029;
            double lfl = gas != null && gas.LFL > 0 ? gas.LFL : 0.033; // safe default (CH4)
            double ufl = gas != null && gas.UFL > 0 ? gas.UFL : 0.099;

            // mole_fraction = Y_i × M_mix / M_i, with M_mix ≈ M_air for dilute plumes.
            // ppm/ppb are volumetric (= mole) units.
            double molarRatio = MAirKgMol / Mi;

            var output = new double[nx, ny, nz];
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        double y = yField[i, j, k];
                        double v;
                        switch (target)
                        {
                            case ViewFieldProperty.MoleFraction:        v = y * molarRatio; break;
                            case ViewFieldProperty.ConcentrationKgM3:   v = y * RhoAir; break;
                            case ViewFieldProperty.ConcentrationPpm:    v = y * molarRatio * 1.0e6; break;
                            case ViewFieldProperty.ConcentrationPpb:    v = y * molarRatio * 1.0e9; break;
                            case ViewFieldProperty.PercentLFL:          v = (y * RhoAir / lfl) * 100.0; break;
                            case ViewFieldProperty.PercentUFL:          v = (y * RhoAir / ufl) * 100.0; break;
                            default:                                     v = y; break;
                        }
                        output[i, j, k] = v;
                    }
            return output;
        }

        /// <summary>Builds a thermal-radiation field (kW/m²) by summing the point-source
        /// model contribution of every active FireSource in the scene at each cell centre.
        /// Returns a fresh nx×ny×nz array.</summary>
        public static double[,,] BuildRadiationField(Scene3D scene, int nx, int ny, int nz, double halfM)
        {
            var output = new double[nx, ny, nz];
            if (scene?.FireScenario?.Sources == null || scene.FireScenario.Sources.Count == 0)
                return output;
            double dx = 2 * halfM / nx, dy = 2 * halfM / ny;
            double dz = halfM > 0 ? halfM / nz * 2.0 : 1.0; // matches BuildVisual's z scaling
            var sources = scene.FireScenario.Sources;
            for (int k = 0; k < nz; k++)
            {
                double z = (k + 0.5) * dz;
                for (int j = 0; j < ny; j++)
                {
                    double y = -halfM + (j + 0.5) * dy;
                    for (int i = 0; i < nx; i++)
                    {
                        double x = -halfM + (i + 0.5) * dx;
                        double sumWm2 = 0;
                        foreach (var src in sources)
                        {
                            if (src == null) continue;
                            double dxp = x - src.Position.X;
                            double dyp = y - src.Position.Y;
                            double dzp = z - src.Position.Z;
                            double r = Math.Sqrt(dxp * dxp + dyp * dyp + dzp * dzp);
                            sumWm2 += JetFireModel.RadiationAtDistance(src, r);
                        }
                        output[i, j, k] = sumWm2 / 1000.0; // W/m² → kW/m²
                    }
                }
            }
            return output;
        }

        /// <summary>Scalar point-sample transform used by detectors and monitors.
        /// Converts a single Y_i value (or a temperature / radiation value) into the
        /// requested unit. Pass <paramref name="rawTemperatureK"/> only when the source
        /// field IS temperature — otherwise pass NaN.</summary>
        public static double ScalarFromMassFraction(double y, ViewFieldProperty target, GasProperties gas)
        {
            if (target == ViewFieldProperty.MassFraction || target == ViewFieldProperty.Concentration)
                return y;
            double Mi = gas != null && gas.MolarMass > 0 ? gas.MolarMass : 0.029;
            double lfl = gas != null && gas.LFL > 0 ? gas.LFL : 0.033;
            double ufl = gas != null && gas.UFL > 0 ? gas.UFL : 0.099;
            double molarRatio = MAirKgMol / Mi;
            switch (target)
            {
                case ViewFieldProperty.MoleFraction:        return y * molarRatio;
                case ViewFieldProperty.ConcentrationKgM3:   return y * RhoAir;
                case ViewFieldProperty.ConcentrationPpm:    return y * molarRatio * 1.0e6;
                case ViewFieldProperty.ConcentrationPpb:    return y * molarRatio * 1.0e9;
                case ViewFieldProperty.PercentLFL:          return (y * RhoAir / lfl) * 100.0;
                case ViewFieldProperty.PercentUFL:          return (y * RhoAir / ufl) * 100.0;
                default:                                     return y;
            }
        }

        /// <summary>Computes thermal radiation flux (kW/m²) at a single world-space
        /// point by summing every FireSource's point-model contribution.</summary>
        public static double RadiationAtPoint(Scene3D scene, double x, double y, double z)
        {
            if (scene?.FireScenario?.Sources == null || scene.FireScenario.Sources.Count == 0)
                return 0;
            double sumWm2 = 0;
            foreach (var src in scene.FireScenario.Sources)
            {
                if (src == null) continue;
                double dx = x - src.Position.X;
                double dy = y - src.Position.Y;
                double dz = z - src.Position.Z;
                double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                sumWm2 += JetFireModel.RadiationAtDistance(src, r);
            }
            return sumWm2 / 1000.0;
        }

        /// <summary>Units suffix shown in legends / detector readouts. Matches the
        /// numeric value returned by <see cref="FromMassFraction"/>.</summary>
        public static string UnitFor(ViewFieldProperty p)
        {
            switch (p)
            {
                case ViewFieldProperty.MoleFraction:        return "mol/mol";
                case ViewFieldProperty.MassFraction:
                case ViewFieldProperty.Concentration:       return "kg/kg";
                case ViewFieldProperty.ConcentrationKgM3:   return "kg/m³";
                case ViewFieldProperty.ConcentrationPpm:    return "ppm";
                case ViewFieldProperty.ConcentrationPpb:    return "ppb";
                case ViewFieldProperty.PercentLFL:          return "%LFL";
                case ViewFieldProperty.PercentUFL:          return "%UFL";
                case ViewFieldProperty.Temperature:         return "K";
                case ViewFieldProperty.WindSpeed:           return "m/s";
                case ViewFieldProperty.Pressure:            return "Pa";
                case ViewFieldProperty.TurbulentK:          return "m²/s²";
                case ViewFieldProperty.TurbulentEpsilon:    return "m²/s³";
                case ViewFieldProperty.TurbulentViscosity:  return "m²/s";
                case ViewFieldProperty.ThermalRadiationKwM2: return "kW/m²";
                default:                                     return "";
            }
        }
    }
}
