using System;
using System.Collections.Generic;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Integral free-jet relations used to pre-dilute a high-momentum release before handing it
    /// to a plume model that has no jet physics (Gaussian plume, FluidX3D passive tracer).
    /// Axial decay and spreading follow Chen &amp; Rodi (1980) for a round turbulent gas jet in air;
    /// the momentum length is the distance at which the jet centreline velocity has decayed to the
    /// wind speed, i.e. where the plume model takes over. The source must be given as a
    /// momentum-conserving pseudo-source (expanded to ambient pressure).
    /// </summary>
    public static class JetTransitionModel
    {
        /// <summary>Axial concentration decay constant: C/C0 = K·(d/x)·√(ρg/ρa).</summary>
        public const double AxialConcentrationK = 5.4;
        /// <summary>Axial velocity decay constant: u/u0 = K·(d/x)·√(ρg/ρa).</summary>
        public const double AxialVelocityK = 6.2;
        /// <summary>Radial spread: 1/e radius of the Gaussian jet profile = Rate · x.</summary>
        public const double SpreadRate = 0.114;

        /// <summary>Distance along the jet axis at which the centreline velocity equals the wind speed.</summary>
        public static double MomentumLengthM(double dEqM, double uEqMPerS, double windMPerS, double rhoGas, double rhoAir)
        {
            if (dEqM <= 0 || uEqMPerS <= 0) return 0;
            double u = Math.Max(windMPerS, 0.5);
            double dens = rhoAir > 0 && rhoGas > 0 ? Math.Sqrt(rhoGas / rhoAir) : 1.0;
            return AxialVelocityK * dEqM * (uEqMPerS / u) * dens;
        }

        /// <summary>Gaussian sigma of the jet concentration profile at axial distance s (1/e radius / √2).</summary>
        public static double SigmaAtM(double sM) => SpreadRate * Math.Max(sM, 0) / Math.Sqrt(2.0);

        /// <summary>Centreline concentration (volume fraction) of the free jet at axial distance x.</summary>
        public static double AxialVolumeFraction(double dEqM, double xM, double rhoGas, double rhoAir)
        {
            if (xM <= 0) return 1.0;
            double dens = rhoAir > 0 && rhoGas > 0 ? Math.Sqrt(rhoGas / rhoAir) : 1.0;
            return Math.Min(1.0, AxialConcentrationK * dEqM / xM * dens);
        }

        /// <summary>
        /// Briggs momentum rise of a (partly) vertical jet in a crosswind at downwind distance x:
        /// transitional Δh = 1.44·d·(vs/u)^(2/3)·(x/d)^(1/3), capped at the final neutral rise 3·d·vs/u,
        /// scaled by the vertical component of the release direction.
        /// </summary>
        public static double RiseM(double dEqM, double uEqMPerS, double windMPerS, double xM, double sinElevation)
        {
            if (sinElevation <= 0 || dEqM <= 0 || uEqMPerS <= 0) return 0;
            double u = Math.Max(windMPerS, 0.5);
            double x = Math.Max(xM, dEqM);
            double transitional = 1.44 * dEqM * Math.Pow(uEqMPerS / u, 2.0 / 3.0) * Math.Pow(x / dEqM, 1.0 / 3.0);
            double final = 3.0 * dEqM * uEqMPerS / u;
            return Math.Min(transitional, final) * sinElevation;
        }

        /// <summary>
        /// Bent jet centreline: starts along the release direction and turns into the wind direction
        /// with the momentum length as the e-folding scale. Returns points with their arc length,
        /// stopping at <paramref name="totalLengthM"/> or when leaving the box
        /// [-halfM, halfM]² × [0, heightM].
        /// </summary>
        public static List<(double x, double y, double z, double s)> Trajectory(
            double x0, double y0, double z0,
            double dirX, double dirY, double dirZ,
            double windDirX, double windDirY,
            double momentumLengthM, double totalLengthM, double stepM,
            double halfM, double heightM)
        {
            var pts = new List<(double, double, double, double)>();
            double x = x0, y = y0, z = z0, s = 0;
            double Lm = Math.Max(momentumLengthM, stepM);
            while (s <= totalLengthM)
            {
                if (Math.Abs(x) > halfM || Math.Abs(y) > halfM || z < 0 || z > heightM) break;
                pts.Add((x, y, z, s));
                double blend = 1.0 - Math.Exp(-s / Lm);
                double tx = dirX * (1 - blend) + windDirX * blend;
                double ty = dirY * (1 - blend) + windDirY * blend;
                double tz = dirZ * (1 - blend);
                double mag = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                if (mag < 1e-9) break;
                x += tx / mag * stepM; y += ty / mag * stepM; z += tz / mag * stepM; s += stepM;
            }
            if (pts.Count == 0) pts.Add((x0, y0, z0, 0));
            return pts;
        }
    }
}
