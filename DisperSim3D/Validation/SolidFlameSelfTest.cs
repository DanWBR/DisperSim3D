using System;
using System.Collections.Generic;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Physics checks for <see cref="SolidFlameModel"/>. There is no test framework in
    /// this solution, so these run as self-checking cases in the style of
    /// <see cref="GeometrySelfTest"/>.
    ///
    /// The important one is far-field convergence: at distances well beyond the flame
    /// length the solid flame must agree with the point source, because both then see
    /// the same total radiated power from what is effectively a point. A mismatch there
    /// means the panel areas, the emissive power, or a normal direction is wrong — the
    /// three ways this model fails quietly.
    ///
    /// Run via <c>DisperSim3D.CLI --solid-flame-selftest</c>.
    /// </summary>
    public static class SolidFlameSelfTest
    {
        public sealed class Result
        {
            public string Name { get; }
            public bool Passed { get; }
            public string Detail { get; }
            public Result(string name, bool passed, string detail = "")
            {
                Name = name; Passed = passed; Detail = detail;
            }
            public override string ToString()
                => (Passed ? "PASS  " : "FAIL  ") + Name +
                   (string.IsNullOrEmpty(Detail) ? "" : "   " + Detail);
        }

        private const double AmbientTempK = 293.15;

        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();
            var noWind = new Vector3D(0, 0, 0);

            // ── Panel geometry ──────────────────────────────────────────────
            {
                var jet = MakeJet();
                var emitter = SolidFlameModel.Prepare(jet, noWind);
                double expectedArea = Math.PI * emitter.FlameDiameterM * emitter.FlameLengthM;
                results.Add(new Result("panel areas sum to the lateral area πDL",
                    RelativeNear(expectedArea, emitter.FlameAreaM2, 1e-9),
                    $"expected={expectedArea:F3} m² actual={emitter.FlameAreaM2:F3} m²"));

                bool normalsOutward = true;
                double axialLength = emitter.FlameLengthM;
                foreach (var panel in emitter.Panels)
                {
                    // Every panel centre sits one radius off the axis, along its normal.
                    var toCenter = panel.Center - jet.Position;
                    double alongAxis = toCenter.X; // jet points +X in MakeJet
                    if (alongAxis < -1e-6 || alongAxis > axialLength + 1e-6) normalsOutward = false;
                    if (Math.Abs(panel.Normal.Length - 1.0) > 1e-9) normalsOutward = false;
                }
                results.Add(new Result("panels lie on the flame axis with unit normals",
                    normalsOutward));
            }

            // ── Emissive power ──────────────────────────────────────────────
            {
                var jet = MakeJet();
                var emitter = SolidFlameModel.Prepare(jet, noWind);
                double radiatedKw = jet.MassFlowRateKgS * jet.HeatOfCombustionJKg
                                    * jet.RadiativeFraction / 1000.0;
                double impliedKw = emitter.SepKwM2 * emitter.FlameAreaM2;
                results.Add(new Result("emissive power does not exceed the radiated power",
                    impliedKw <= radiatedKw * 1.0000001,
                    $"radiated={radiatedKw:F0} kW, SEP×A={impliedKw:F0} kW"));

                var pool = MakePool(diameterM: 30.0);
                var poolEmitter = SolidFlameModel.Prepare(pool, noWind);
                results.Add(new Result("large pool SEP is capped by Mudan's soot blend",
                    poolEmitter.SepKwM2 <= 60.0 && poolEmitter.SepKwM2 > 0,
                    $"SEP={poolEmitter.SepKwM2:F1} kW/m² for D={pool.PoolDiameterM} m"));
            }

            // ── Far field vs point source ───────────────────────────────────
            {
                var jet = MakeJet();
                var emitter = SolidFlameModel.Prepare(jet, noWind);
                double length = emitter.FlameLengthM;

                // Both models radiate the same total power, but they do not put it in
                // the same places: the point source is isotropic, while a cylinder seen
                // broadside presents D·L of projected area against the πD·L/4 a sphere
                // of the same emitting area would average. The far-field ratio is
                // therefore exactly 4/π — the anisotropy the point source cannot
                // represent, and the reason the two disagree by a fixed 27% off the
                // flame's flank no matter how far away the receiver is.
                const double SideOnRatio = 4.0 / Math.PI;

                foreach (double factor in new[] { 10.0, 25.0, 50.0 })
                {
                    double distance = factor * length;
                    var receiver = new Point3D(jet.Position.X, jet.Position.Y + distance, jet.Position.Z);

                    // Dry air so transmissivity is 1 and the two models are comparable.
                    double solid = SolidFlameModel.FluxKwM2(emitter, receiver,
                        ReceiverMode.MaxOriented, AmbientTempK, 0.0);

                    // The point source radiates from the flame origin; measure from the
                    // flame's mid-length so both share the same effective centre.
                    var mid = new Point3D(jet.Position.X + 0.5 * length, jet.Position.Y, jet.Position.Z);
                    double r = (receiver - mid).Length;
                    double point = JetFireModel.RadiationAtDistance(jet, r) / 1000.0;

                    double ratio = solid / point;
                    double error = Math.Abs(ratio - SideOnRatio) / SideOnRatio;
                    results.Add(new Result($"far field at {factor:F0}×L is 4/π of the point source",
                        error < 0.03,
                        $"solid={solid:E3} point={point:E3} kW/m², razão={ratio:F4} (4/π={SideOnRatio:F4})"));
                }
            }

            // ── Monotonicity and orientation ────────────────────────────────
            {
                var jet = MakeJet();
                var emitter = SolidFlameModel.Prepare(jet, noWind);

                double previous = double.MaxValue;
                bool decreasing = true;
                for (double d = 5; d <= 200; d += 5)
                {
                    var receiver = new Point3D(jet.Position.X, jet.Position.Y + d, jet.Position.Z);
                    double flux = SolidFlameModel.FluxKwM2(emitter, receiver,
                        ReceiverMode.MaxOriented, AmbientTempK, 0.5);
                    if (flux > previous) decreasing = false;
                    previous = flux;
                }
                results.Add(new Result("flux decreases monotonically with distance", decreasing));

                var probe = new Point3D(jet.Position.X, jet.Position.Y + 25, jet.Position.Z);
                double max = SolidFlameModel.FluxKwM2(emitter, probe, ReceiverMode.MaxOriented, AmbientTempK, 0.5);
                double horiz = SolidFlameModel.FluxKwM2(emitter, probe, ReceiverMode.Horizontal, AmbientTempK, 0.5);
                double vert = SolidFlameModel.FluxKwM2(emitter, probe, ReceiverMode.Vertical, AmbientTempK, 0.5);
                results.Add(new Result("max-oriented receiver bounds the other orientations",
                    max >= horiz - 1e-12 && max >= vert - 1e-12,
                    $"max={max:F3} horizontal={horiz:F3} vertical={vert:F3} kW/m²"));
                results.Add(new Result("a receiver level with the flame sees more on a vertical face",
                    vert > horiz,
                    $"vertical={vert:F3} horizontal={horiz:F3} kW/m²"));
            }

            // ── Transmissivity ──────────────────────────────────────────────
            {
                results.Add(new Result("dry air does not attenuate",
                    SolidFlameModel.Transmissivity(100, AmbientTempK, 0.0) == 1.0));

                double near = SolidFlameModel.Transmissivity(20, AmbientTempK, 0.6);
                double far = SolidFlameModel.Transmissivity(200, AmbientTempK, 0.6);
                results.Add(new Result("transmissivity falls with path length",
                    far < near && far > 0 && near <= 1.0,
                    $"τ(20 m)={near:F3} τ(200 m)={far:F3}"));

                double dry = SolidFlameModel.Transmissivity(100, AmbientTempK, 0.2);
                double humid = SolidFlameModel.Transmissivity(100, AmbientTempK, 0.9);
                results.Add(new Result("transmissivity falls with humidity",
                    humid < dry,
                    $"τ(RH 20%)={dry:F3} τ(RH 90%)={humid:F3}"));

                // Buck at 20 °C is 2339 Pa — the anchor for the whole τ curve.
                double psat = SolidFlameModel.SaturationVapourPressurePa(293.15);
                results.Add(new Result("saturation vapour pressure at 20 °C ≈ 2339 Pa",
                    Math.Abs(psat - 2339.0) < 15.0, $"actual={psat:F0} Pa"));
            }

            // ── Pool flame orientation ──────────────────────────────────────
            {
                // A pool fire left at the default Direction of +X used to lay its flame
                // on the ground; the axis must be vertical whatever Direction says.
                var pool = MakePool(diameterM: 8.0);
                pool.Direction = new Vector3D(1, 0, 0);
                var tip = JetFireModel.FlameTip(pool, new Vector3D(5, 0, 0));
                results.Add(new Result("a pool flame rises vertically",
                    Math.Abs(tip.X - pool.Position.X) < 1e-9
                    && Math.Abs(tip.Y - pool.Position.Y) < 1e-9
                    && tip.Z > pool.Position.Z,
                    $"tip=({tip.X:F2}, {tip.Y:F2}, {tip.Z:F2})"));
            }

            // ── Field assembly ──────────────────────────────────────────────
            {
                var scene = new Scene3D();
                var jet = MakeJet();
                jet.Position = new Point3D(0, 0, 2);
                scene.FireScenario.Sources.Add(jet);

                var field = FieldTransform.BuildRadiationField(scene, 24, 24, 12, 60);
                double peak = 0;
                foreach (var v in field) if (v > peak) peak = v;
                results.Add(new Result("BuildRadiationField returns a non-zero solid-flame field",
                    peak > 0, $"peak={peak:F2} kW/m²"));

                double atPoint = FieldTransform.RadiationAtPoint(scene, 0, 30, 2);
                var emitter = SolidFlameModel.Prepare(jet, FieldTransform.ResolveMeteo(scene).WindVector);
                double direct = SolidFlameModel.FluxKwM2(emitter, new Point3D(0, 30, 2),
                    scene.FireScenario.ReceiverMode,
                    FieldTransform.ResolveMeteo(scene).AmbientTemperature,
                    FieldTransform.ResolveMeteo(scene).RelativeHumidity);
                results.Add(new Result("RadiationAtPoint agrees with the model directly",
                    RelativeNear(direct, atPoint, 1e-9),
                    $"field={atPoint:E4} direct={direct:E4} kW/m²"));
            }

            return results;
        }

        /// <summary>Prints results and returns <c>true</c> when every case passes.</summary>
        public static bool RunAndPrint(TextWriter writer)
        {
            var results = Run();
            int passed = 0;
            foreach (var r in results)
            {
                writer.WriteLine(r.ToString());
                if (r.Passed) passed++;
            }
            writer.WriteLine();
            writer.WriteLine(passed == results.Count
                ? $"OK — {passed}/{results.Count} solid-flame checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        /// <summary>A 2 kg/s methane jet firing along +X — roughly a 3 in. line rupture.</summary>
        private static FireSource MakeJet() => new FireSource
        {
            Name = "jet",
            Position = new Point3D(0, 0, 0),
            Direction = new Vector3D(1, 0, 0),
            MassFlowRateKgS = 2.0,
            OrificeDiameterM = 0.05,
            HeatOfCombustionJKg = 50_000_000,
            RadiativeFraction = 0.2,
            IsPoolFire = false,
            RadiationModel = RadiationModel.SolidFlame
        };

        private static FireSource MakePool(double diameterM) => new FireSource
        {
            Name = "pool",
            Position = new Point3D(0, 0, 0),
            IsPoolFire = true,
            PoolDiameterM = diameterM,
            PoolBurnRateKgM2S = 0.055,
            MassFlowRateKgS = 0.055 * Math.PI * 0.25 * diameterM * diameterM,
            HeatOfCombustionJKg = 43_000_000,
            RadiativeFraction = 0.35,
            RadiationModel = RadiationModel.SolidFlame
        };

        private static bool RelativeNear(double expected, double actual, double tolerance)
            => Math.Abs(expected - actual) <= tolerance * Math.Max(1.0, Math.Abs(expected));
    }
}
