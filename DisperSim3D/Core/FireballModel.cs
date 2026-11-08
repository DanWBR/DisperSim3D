using System;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// The fireball of a BLEVE as the CCPS consequence guidelines size it: a sphere that
    /// radiates at a uniform emissive power for the duration of the burn.
    ///
    /// <para>CCPS, Guidelines for Chemical Process Quantitative Risk Analysis, 2nd ed.
    /// (2000), §2.2.6, from Roberts (1981/82):</para>
    /// <list type="bullet">
    /// <item>maximum diameter <c>D = 5.8·M^(1/3)</c> (m, M in kg);</item>
    /// <item>duration <c>t = 0.45·M^(1/3)</c> below 30 000 kg, <c>2.6·M^(1/6)</c> above (s);</item>
    /// <item>centre height <c>H = 0.75·D</c> above the release;</item>
    /// <item>emissive power <c>E = F_rad·M·ΔH_c / (π·D²·t)</c>, with
    /// <c>F_rad = 0.27·P^0.32</c> from the vessel pressure P before failure (MPa);</item>
    /// <item>view factor of a sphere seen face-on, <c>F = (D/2)²/L²</c>, L the distance
    /// to its centre; incident flux <c>q = τ·E·F</c>.</item>
    /// </list>
    ///
    /// <para>A receiver oriented other than face-on gets <c>F·cos θ</c>, θ between its
    /// normal and the line to the centre — exact while the whole sphere is above the
    /// receiver's plane, the usual case at hazard distances. A receiver inside the
    /// fireball gets the emissive power itself.</para>
    /// </summary>
    public static class FireballModel
    {
        /// <summary>Mass above which the CCPS duration correlation changes form (kg).</summary>
        public const double DurationBreakMassKg = 30000;

        public static double DiameterM(double massKg) => 5.8 * Math.Cbrt(Math.Max(0, massKg));

        public static double DurationS(double massKg)
        {
            double m = Math.Max(0, massKg);
            return m < DurationBreakMassKg ? 0.45 * Math.Cbrt(m) : 2.6 * Math.Pow(m, 1.0 / 6.0);
        }

        public static double CentreHeightM(double massKg) => 0.75 * DiameterM(massKg);

        /// <summary>The fireball's centre: the source position lifted by <see cref="CentreHeightM"/>.</summary>
        public static Point3D Centre(FireSource source) =>
            new Point3D(source.Position.X, source.Position.Y, source.Position.Z + CentreHeightM(source.FireballMassKg));

        /// <summary>
        /// Radiated fraction: Roberts' <c>0.27·P^0.32</c> when the source carries the vessel
        /// pressure (<see cref="FireSource.StagnationPressurePa"/>), else the source's own
        /// <see cref="FireSource.RadiativeFraction"/>.
        /// </summary>
        public static double RadiativeFraction(FireSource source) =>
            source.StagnationPressurePa > 0
                ? 0.27 * Math.Pow(source.StagnationPressurePa / 1.0e6, 0.32)
                : source.RadiativeFraction;

        /// <summary>Surface emissive power (kW/m²): the source's own when set, else the energy balance.</summary>
        public static double SepKwM2(FireSource source)
        {
            if (source.SepKwM2 > 0) return source.SepKwM2;
            double m = source.FireballMassKg;
            double d = DiameterM(m), t = DurationS(m);
            if (!(d > 0) || !(t > 0)) return 0;
            return RadiativeFraction(source) * m * source.HeatOfCombustionJKg / (Math.PI * d * d * t) / 1000.0;
        }

        /// <summary>Incident flux (kW/m²) at a receiver.</summary>
        public static double FluxKwM2(FireSource source, Point3D receiver, ReceiverMode mode,
            double ambientTempK, double relativeHumidity, RayBoxIntersector.Occluder occluder = null)
        {
            if (source == null || !(source.FireballMassKg > 0)) return 0;
            var c = Centre(source);
            double radius = 0.5 * DiameterM(source.FireballMassKg);
            double dx = c.X - receiver.X, dy = c.Y - receiver.Y, dz = c.Z - receiver.Z;
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double sep = SepKwM2(source);
            if (l <= radius) return sep;
            if (occluder != null && occluder.Blocks(receiver, c)) return 0;

            double cos;
            switch (mode)
            {
                case ReceiverMode.Horizontal: cos = Math.Max(0, dz) / l; break;
                case ReceiverMode.Vertical: cos = Math.Sqrt(dx * dx + dy * dy) / l; break;
                default: cos = 1; break;
            }
            double view = radius * radius / (l * l) * cos;
            return Transmissivity(l - radius, ambientTempK, relativeHumidity) * sep * view;
        }

        private static double Transmissivity(double pathM, double ambientTempK, double relativeHumidity) =>
            SolidFlameModel.Transmissivity(pathM, ambientTempK, relativeHumidity);
    }
}
