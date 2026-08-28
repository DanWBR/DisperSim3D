using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Solid-flame thermal radiation: the flame is a tilted cylinder radiating at a
    /// surface emissive power (SEP), and the flux at a receiver is
    /// <c>I = τ · SEP · F</c> with <c>F</c> the view factor from the flame surface to
    /// the receiver and <c>τ</c> the atmospheric transmissivity.
    ///
    /// This replaces — as an option, see <see cref="RadiationModel"/> — the point
    /// source in <see cref="JetFireModel.RadiationAtDistance"/>, which ignores flame
    /// shape and tilt and diverges as the receiver approaches the source. The two
    /// converge in the far field, which is what <c>SolidFlameSelfTest</c> asserts.
    ///
    /// <para><b>Why numeric panels instead of a closed-form cylinder view factor.</b>
    /// The analytic expressions are tabulated per receiver orientation and assume an
    /// upright cylinder. Discretising the flame surface handles the wind-tilted axis,
    /// any receiver orientation, and — once obstacle shading lands — lets a blocked
    /// panel simply drop out of the sum. Cost is one distance and two dot products per
    /// panel, and the panels are built once per fire source, not per receiver.</para>
    ///
    /// <para>References: Mudan (1984) for the soot-obscured pool-fire SEP;
    /// Chamberlain (1987) for the jet frustum width; Pietersen &amp; Huerta
    /// (TNO Yellow Book) for the transmissivity correlation.</para>
    /// </summary>
    public static class SolidFlameModel
    {
        /// <summary>Panel count around the flame axis. 12 keeps the view factor within
        /// ~1% of a converged solution for receivers more than one flame diameter away.</summary>
        public const int DefaultCircumferentialPanels = 12;

        /// <summary>Panel count along the flame axis.</summary>
        public const int DefaultAxialPanels = 8;

        /// <summary>Optically thin jet flames radiate at up to a few hundred kW/m²;
        /// beyond that an energy-balance SEP means the flame area is underestimated.</summary>
        private const double JetSepCapKwM2 = 350.0;

        /// <summary>Mudan's clear-flame and soot-layer emissive powers for hydrocarbon
        /// pool fires, blended by diameter with an extinction coefficient of 0.12 1/m.</summary>
        private const double PoolSepClearKwM2 = 140.0;
        private const double PoolSepSootKwM2 = 20.0;
        private const double PoolSootExtinction = 0.12;

        /// <summary>Chamberlain's jet-flame frustum is roughly an eighth of the flame
        /// length across; used when the source doesn't override the diameter.</summary>
        private const double JetWidthToLengthRatio = 0.13;

        /// <summary>One radiating patch of the flame surface.</summary>
        public readonly struct FlamePanel
        {
            /// <summary>Patch centre, on the flame surface.</summary>
            public readonly Point3D Center;
            /// <summary>Outward unit normal.</summary>
            public readonly Vector3D Normal;
            /// <summary>Patch area (m²).</summary>
            public readonly double AreaM2;

            public FlamePanel(Point3D center, Vector3D normal, double areaM2)
            {
                Center = center; Normal = normal; AreaM2 = areaM2;
            }
        }

        /// <summary>A fire source with its flame geometry and emissive power resolved.
        /// Build once per source, then evaluate at as many receivers as needed.</summary>
        public sealed class Emitter
        {
            public FireSource Source { get; internal set; }
            public FlamePanel[] Panels { get; internal set; }
            public double SepKwM2 { get; internal set; }
            public double FlameLengthM { get; internal set; }
            public double FlameDiameterM { get; internal set; }
            /// <summary>Total radiating area (m²) — the sum of the panel areas.</summary>
            public double FlameAreaM2 { get; internal set; }
        }

        // ── Geometry ────────────────────────────────────────────────────────────

        /// <summary>Flame length: Thomas for a pool, Chamberlain's Q^0.4 for a jet.</summary>
        public static double FlameLength(FireSource source)
            => source.IsPoolFire ? JetFireModel.PoolFlameLength(source) : JetFireModel.FlameLength(source);

        /// <summary>Flame diameter (m). The source's own <see cref="FireSource.FlameDiameterM"/>
        /// wins when set; otherwise the pool diameter for a pool fire, and Chamberlain's
        /// frustum width for a jet.</summary>
        public static double FlameDiameter(FireSource source, double flameLengthM)
        {
            if (source.FlameDiameterM > 0) return source.FlameDiameterM;
            if (source.IsPoolFire) return Math.Max(source.PoolDiameterM, 0.1);
            return Math.Max(JetWidthToLengthRatio * flameLengthM, 2.0 * source.OrificeDiameterM);
        }

        /// <summary>
        /// Discretises the flame envelope into outward-facing panels. The axis runs from
        /// the source position to <see cref="JetFireModel.FlameTip"/>, so a jet flame
        /// carries the wind tilt the renderer already draws. Only the lateral surface is
        /// panelled — the end caps are a small fraction of πDL for the L/D ratios these
        /// correlations produce, and leaving them out keeps the panel areas consistent
        /// with the area used for the SEP.
        /// </summary>
        public static FlamePanel[] BuildPanels(FireSource source, Vector3D windVector,
            int circumferentialPanels = DefaultCircumferentialPanels,
            int axialPanels = DefaultAxialPanels)
        {
            if (source == null) return Array.Empty<FlamePanel>();
            int nCirc = Math.Max(3, circumferentialPanels);
            int nAxial = Math.Max(1, axialPanels);

            var basePoint = source.Position;
            var tip = JetFireModel.FlameTip(source, windVector);
            var axis = tip - basePoint;
            double length = axis.Length;
            if (length < 1e-3) return Array.Empty<FlamePanel>();
            axis.Normalize();

            double radius = 0.5 * FlameDiameter(source, length);
            if (radius < 1e-4) return Array.Empty<FlamePanel>();

            // Orthonormal basis across the axis.
            var seed = Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var u = Vector3D.CrossProduct(axis, seed); u.Normalize();
            var v = Vector3D.CrossProduct(axis, u);    v.Normalize();

            double panelArea = (2.0 * Math.PI * radius / nCirc) * (length / nAxial);
            var panels = new FlamePanel[nCirc * nAxial];
            int p = 0;

            for (int a = 0; a < nAxial; a++)
            {
                double s = (a + 0.5) / nAxial * length;
                var centerline = new Point3D(
                    basePoint.X + axis.X * s,
                    basePoint.Y + axis.Y * s,
                    basePoint.Z + axis.Z * s);

                for (int c = 0; c < nCirc; c++)
                {
                    double phi = 2.0 * Math.PI * (c + 0.5) / nCirc;
                    double cos = Math.Cos(phi), sin = Math.Sin(phi);
                    var normal = new Vector3D(
                        u.X * cos + v.X * sin,
                        u.Y * cos + v.Y * sin,
                        u.Z * cos + v.Z * sin);
                    var center = new Point3D(
                        centerline.X + normal.X * radius,
                        centerline.Y + normal.Y * radius,
                        centerline.Z + normal.Z * radius);
                    panels[p++] = new FlamePanel(center, normal, panelArea);
                }
            }

            return panels;
        }

        // ── Emissive power ──────────────────────────────────────────────────────

        /// <summary>
        /// Surface emissive power (kW/m²). Starts from the energy balance
        /// <c>χ·ṁ·ΔHc / A_flame</c> and caps it: a pool fire cannot exceed Mudan's
        /// soot-obscured value for its diameter, and a jet flame is capped at
        /// <see cref="JetSepCapKwM2"/>. <see cref="FireSource.SepKwM2"/> overrides both.
        /// </summary>
        public static double SurfaceEmissivePowerKwM2(FireSource source, double flameAreaM2)
        {
            if (source == null) return 0;
            if (source.SepKwM2 > 0) return source.SepKwM2;
            if (flameAreaM2 <= 1e-6) return 0;

            double radiatedW = source.MassFlowRateKgS * source.HeatOfCombustionJKg * source.RadiativeFraction;
            double sep = radiatedW / flameAreaM2 / 1000.0;

            if (source.IsPoolFire)
            {
                double d = Math.Max(source.PoolDiameterM, 0.1);
                double mudan = PoolSepClearKwM2 * Math.Exp(-PoolSootExtinction * d)
                             + PoolSepSootKwM2 * (1.0 - Math.Exp(-PoolSootExtinction * d));
                return Math.Min(sep, mudan);
            }
            return Math.Min(sep, JetSepCapKwM2);
        }

        // ── Atmosphere ──────────────────────────────────────────────────────────

        /// <summary>
        /// Atmospheric transmissivity by the Pietersen correlation
        /// <c>τ = 2.02·(P_w·X)^−0.09</c>, with the water-vapour partial pressure from the
        /// relative humidity and the Buck saturation curve. Clamped to [0, 1]; a path
        /// shorter than the correlation's range returns 1 (no attenuation).
        /// </summary>
        public static double Transmissivity(double distanceM, double ambientTempK, double relativeHumidity)
        {
            if (distanceM <= 0) return 1.0;
            double rh = relativeHumidity;
            if (rh <= 0) return 1.0;
            if (rh > 1.0) rh /= 100.0;             // tolerate 0..100 as well as 0..1
            rh = Math.Min(rh, 1.0);

            double pw = rh * SaturationVapourPressurePa(ambientTempK);
            double x = pw * distanceM;             // Pa·m
            if (x < 1.0) return 1.0;

            double tau = 2.02 * Math.Pow(x, -0.09);
            return tau < 0 ? 0 : (tau > 1 ? 1 : tau);
        }

        /// <summary>Buck (1981) saturation vapour pressure over water, in Pa.</summary>
        public static double SaturationVapourPressurePa(double tempK)
        {
            double tc = tempK - 273.15;
            if (tc < -60) tc = -60;
            if (tc > 100) tc = 100;
            return 611.21 * Math.Exp((18.678 - tc / 234.5) * (tc / (257.14 + tc)));
        }

        // ── Flux ────────────────────────────────────────────────────────────────

        /// <summary>Resolves flame geometry and emissive power for one source.</summary>
        public static Emitter Prepare(FireSource source, Vector3D windVector,
            int circumferentialPanels = DefaultCircumferentialPanels,
            int axialPanels = DefaultAxialPanels)
        {
            double length = FlameLength(source);
            var panels = BuildPanels(source, windVector, circumferentialPanels, axialPanels);

            double area = 0;
            for (int i = 0; i < panels.Length; i++) area += panels[i].AreaM2;

            return new Emitter
            {
                Source = source,
                Panels = panels,
                FlameLengthM = length,
                FlameDiameterM = FlameDiameter(source, length),
                FlameAreaM2 = area,
                SepKwM2 = SurfaceEmissivePowerKwM2(source, area)
            };
        }

        /// <summary>
        /// Incident radiant flux (kW/m²) at a receiver.
        ///
        /// <para>Each panel contributes <c>τ·cosθ_s·dA/(π·r²)</c> along the unit vector
        /// pointing from the receiver to that panel. Accumulating those as a vector
        /// makes every receiver orientation fall out of one pass: the view factor for a
        /// surface with normal <c>n</c> is <c>n·G</c>, so the worst-case orientation is
        /// <c>|G|</c>, a horizontal (upward-facing) receiver gets <c>G_z</c>, and the
        /// best vertical orientation gets the magnitude of the horizontal part.</para>
        /// </summary>
        /// <param name="occluder">Plant geometry that can hide part of the flame.
        /// Null means an unobstructed scene, which skips the tests entirely.</param>
        public static double FluxKwM2(Emitter emitter, Point3D receiver, ReceiverMode mode,
            double ambientTempK, double relativeHumidity,
            RayBoxIntersector.Occluder occluder = null)
        {
            if (emitter?.Panels == null || emitter.Panels.Length == 0 || emitter.SepKwM2 <= 0)
                return 0;

            // A receiver closer than this to a panel centre sits essentially on the flame
            // surface; clamping keeps 1/r² finite instead of returning an infinite flux.
            double minDistance = Math.Max(0.05, 0.1 * emitter.FlameDiameterM);

            double gx = 0, gy = 0, gz = 0;
            var panels = emitter.Panels;

            for (int i = 0; i < panels.Length; i++)
            {
                double dx = panels[i].Center.X - receiver.X;
                double dy = panels[i].Center.Y - receiver.Y;
                double dz = panels[i].Center.Z - receiver.Z;
                double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (r < minDistance) r = minDistance;

                double ux = dx / r, uy = dy / r, uz = dz / r;

                // cos θ_s: the panel only radiates toward the receiver if the receiver is
                // on the outward side of it — this is what makes the far side of the
                // flame cylinder invisible, i.e. self-shielding.
                var n = panels[i].Normal;
                double cosSource = -(n.X * ux + n.Y * uy + n.Z * uz);
                if (cosSource <= 0) continue;

                // A panel the geometry hides contributes nothing — this is the whole
                // of the obstacle shading, and it works precisely because the view
                // factor is integrated panel by panel.
                if (occluder != null && occluder.Blocks(receiver, panels[i].Center)) continue;

                double tau = Transmissivity(r, ambientTempK, relativeHumidity);
                double w = tau * cosSource * panels[i].AreaM2 / (Math.PI * r * r);

                gx += w * ux; gy += w * uy; gz += w * uz;
            }

            double viewFactor;
            switch (mode)
            {
                case ReceiverMode.Horizontal:
                    viewFactor = gz > 0 ? gz : 0;
                    break;
                case ReceiverMode.Vertical:
                    viewFactor = Math.Sqrt(gx * gx + gy * gy);
                    break;
                default: // MaxOriented — the orientation that sees the most flame.
                    viewFactor = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                    break;
            }

            return emitter.SepKwM2 * viewFactor;
        }

        /// <summary>Prepares every source in a scenario that is set to the solid-flame
        /// model. Sources left on the point-source model are not included.</summary>
        public static List<Emitter> PrepareAll(IEnumerable<FireSource> sources, Vector3D windVector)
        {
            var list = new List<Emitter>();
            if (sources == null) return list;
            foreach (var s in sources)
            {
                if (s == null || s.RadiationModel != RadiationModel.SolidFlame) continue;
                var e = Prepare(s, windVector);
                if (e.Panels.Length > 0) list.Add(e);
            }
            return list;
        }
    }
}
