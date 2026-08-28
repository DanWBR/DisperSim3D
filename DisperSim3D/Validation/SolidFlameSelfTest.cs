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
                // Lateral surface plus the tip cap: πDL + πr². The cap is a few percent
                // of the total, but it is what stops a receiver off the flame tip from
                // seeing an exact zero.
                double radius = 0.5 * emitter.FlameDiameterM;
                double expectedArea = Math.PI * emitter.FlameDiameterM * emitter.FlameLengthM
                                    + Math.PI * radius * radius;
                results.Add(new Result("panel areas sum to πDL + the tip cap",
                    RelativeNear(expectedArea, emitter.FlameAreaM2, 1e-9),
                    $"expected={expectedArea:F3} m² actual={emitter.FlameAreaM2:F3} m²"));

                // A receiver straight off the tip must see the cap, not nothing.
                var offTip = new Point3D(jet.Position.X + emitter.FlameLengthM + 30,
                                         jet.Position.Y, jet.Position.Z);
                double tipFlux = SolidFlameModel.FluxKwM2(emitter, offTip,
                    ReceiverMode.MaxOriented, AmbientTempK, 0.5);
                results.Add(new Result("a receiver off the flame tip sees the cap",
                    tipFlux > 0, $"{tipFlux:F3} kW/m² 30 m beyond the tip"));

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

                // SEP is normalised over the lateral area, so the lateral surface alone
                // accounts for exactly the radiated power.
                double lateralKw = emitter.SepKwM2 * emitter.LateralAreaM2;
                results.Add(new Result("the lateral surface carries the radiated power",
                    lateralKw <= radiatedKw * 1.0000001,
                    $"radiated={radiatedKw:F0} kW, SEP×lateral={lateralKw:F0} kW"));

                // Including the cap the panels emit a little more, and the overshoot is
                // the cap's share of the lateral area — 3% at this jet's L/D.
                double totalKw = emitter.SepKwM2 * emitter.FlameAreaM2;
                double overshoot = totalKw / radiatedKw - 1.0;
                results.Add(new Result("the tip cap overshoot stays small for a jet",
                    overshoot > 0 && overshoot < 0.06,
                    $"overshoot={overshoot:P1}"));

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

            // ── Buoyant arcing of a horizontal jet ──────────────────────────
            {
                // Same release twice: once at high pressure, once barely above ambient.
                // The fast jet is momentum-dominated and stays straight; the slow one
                // arcs. Nothing but the upstream pressure differs.
                var fast = MakeJet();
                fast.StagnationPressurePa = 70e5;
                fast.OrificeDiameterM = 0.02;
                var slow = MakeJet();
                slow.StagnationPressurePa = 1.3e5;
                slow.OrificeDiameterM = 0.15;

                var fastShape = SolidFlameModel.HorizontalFlameShape(
                    fast, SolidFlameModel.FlameLength(fast));
                var slowShape = SolidFlameModel.HorizontalFlameShape(
                    slow, SolidFlameModel.FlameLength(slow));

                results.Add(new Result("a fast jet has the higher momentum fraction",
                    fastShape.MomentumLengthM > slowShape.MomentumLengthM,
                    $"fast={fastShape.MomentumLengthM:F1} m slow={slowShape.MomentumLengthM:F1} m"));
                results.Add(new Result("a slow jet has the higher Richardson number",
                    slowShape.RichardsonNumber > fastShape.RichardsonNumber,
                    $"fast={fastShape.RichardsonNumber:F2} slow={slowShape.RichardsonNumber:F2}"));
                results.Add(new Result("a slow jet lifts at least as much as a fast one",
                    slowShape.LiftAngleRad >= fastShape.LiftAngleRad,
                    $"fast={fastShape.LiftAngleRad * 180 / Math.PI:F0} deg "
                    + $"slow={slowShape.LiftAngleRad * 180 / Math.PI:F0} deg"));

                // The arced flame must put mass above the release axis.
                var arced = SolidFlameModel.Prepare(slow, noWind);
                double highest = double.MinValue;
                foreach (var panel in arced.Panels)
                    if (panel.Center.Z > highest) highest = panel.Center.Z;
                results.Add(new Result("the arced flame rises above the release axis",
                    highest > slow.Position.Z + 1.0,
                    $"top panel at z={highest:F1} m"));

                // A pool fire has no release axis to arc away from.
                var pool = MakePool(diameterM: 10.0);
                var poolShape = SolidFlameModel.HorizontalFlameShape(pool,
                    SolidFlameModel.FlameLength(pool));
                var poolEmitter = SolidFlameModel.Prepare(pool, noWind);
                bool vertical = true;
                foreach (var panel in poolEmitter.Panels)
                    if (Math.Abs(panel.Center.X) > 5.1 || Math.Abs(panel.Center.Y) > 5.1)
                        vertical = false;
                results.Add(new Result("a pool flame does not arc", vertical));
            }

            // ── Obstacle shading ────────────────────────────────────────────
            {
                var jet = MakeJet();
                var emitter = SolidFlameModel.Prepare(jet, noWind);
                var receiver = new Point3D(jet.Position.X, jet.Position.Y + 30, jet.Position.Z);

                double clear = SolidFlameModel.FluxKwM2(emitter, receiver,
                    ReceiverMode.MaxOriented, AmbientTempK, 0.5);

                // Same call, empty obstacle set: the shading path must not perturb the
                // unobstructed answer at all.
                double emptyOccluder = SolidFlameModel.FluxKwM2(emitter, receiver,
                    ReceiverMode.MaxOriented, AmbientTempK, 0.5,
                    RayBoxIntersector.Occluder.From(new List<BoundingBox>()));
                results.Add(new Result("an empty obstacle set changes nothing",
                    emptyOccluder == clear,
                    $"clear={clear:F4} empty={emptyOccluder:F4} kW/m²"));

                // A wall spanning the whole line of sight, halfway to the receiver.
                var wall = RayBoxIntersector.Occluder.From(new List<BoundingBox>
                {
                    new BoundingBox(new Point3D(-60, 14.5, -1), new Point3D(60, 15.5, 40))
                });
                double shaded = SolidFlameModel.FluxKwM2(emitter, receiver,
                    ReceiverMode.MaxOriented, AmbientTempK, 0.5, wall);
                results.Add(new Result("a wall across the line of sight blocks the flux",
                    shaded == 0 && clear > 0,
                    $"clear={clear:F4} atrás da parede={shaded:F4} kW/m²"));

                // Just past the wall's edge the receiver sees the flame again.
                var shortWall = RayBoxIntersector.Occluder.From(new List<BoundingBox>
                {
                    new BoundingBox(new Point3D(-60, 14.5, -1), new Point3D(-2, 15.5, 40))
                });
                double besideWall = SolidFlameModel.FluxKwM2(emitter, receiver,
                    ReceiverMode.MaxOriented, AmbientTempK, 0.5, shortWall);
                results.Add(new Result("beside the wall the flux comes back",
                    Math.Abs(besideWall - clear) < 1e-9,
                    $"ao lado={besideWall:F4} livre={clear:F4} kW/m²"));

                // A narrow pillar close to the flame hides the panels behind it and
                // leaves the rest of the flame in view. (A wall tall enough to span the
                // flame's full height would block everything, partial in name only.)
                var pillar = RayBoxIntersector.Occluder.From(new List<BoundingBox>
                {
                    new BoundingBox(new Point3D(8, 1.0, -4), new Point3D(12, 2.0, 4))
                });
                double partial = SolidFlameModel.FluxKwM2(emitter, receiver,
                    ReceiverMode.MaxOriented, AmbientTempK, 0.5, pillar);
                results.Add(new Result("a partial wall removes part of the flux",
                    partial > 0 && partial < clear,
                    $"parcial={partial:F4} livre={clear:F4} kW/m²"));
            }

            // ── Ray / box intersection ──────────────────────────────────────
            {
                var box = new BoundingBox(new Point3D(-1, -1, -1), new Point3D(1, 1, 1));

                results.Add(new Result("a segment through the box hits it",
                    RayBoxIntersector.SegmentHitsBox(
                        new Point3D(-5, 0, 0), new Point3D(5, 0, 0), box)));
                results.Add(new Result("a segment past the box misses it",
                    !RayBoxIntersector.SegmentHitsBox(
                        new Point3D(-5, 3, 0), new Point3D(5, 3, 0), box)));
                results.Add(new Result("a segment stopping short of the box misses it",
                    !RayBoxIntersector.SegmentHitsBox(
                        new Point3D(-5, 0, 0), new Point3D(-2, 0, 0), box)));
                results.Add(new Result("a segment starting inside the box hits it",
                    RayBoxIntersector.SegmentHitsBox(
                        new Point3D(0, 0, 0), new Point3D(9, 9, 9), box)));
                // Parallel to an axis, offset just outside the slab: the degenerate
                // branch has to reject it rather than divide by zero.
                results.Add(new Result("a segment parallel to a face just outside misses",
                    !RayBoxIntersector.SegmentHitsBox(
                        new Point3D(-5, 1.001, 0), new Point3D(5, 1.001, 0), box)));
            }

            // ── Shading through the scene ───────────────────────────────────
            {
                var scene = new Scene3D();
                var jet = MakeJet();
                jet.Position = new Point3D(0, 0, 2);
                scene.FireScenario.Sources.Add(jet);

                double clear = FieldTransform.RadiationAtPoint(scene, 0, 40, 2);

                scene.Decorations.Add(new Decoration3D
                {
                    Name = "wall",
                    // Taller than the domain: the radiation grid's z runs to 2*half,
                    // so a shorter wall would leave the upper cells seeing over it.
                    BoundingBox = new BoundingBox(
                        new Point3D(-60, 19.5, -1), new Point3D(60, 20.5, 500))
                });
                double shaded = FieldTransform.RadiationAtPoint(scene, 0, 40, 2);
                results.Add(new Result("scene decorations shade the point sample",
                    clear > 0 && shaded == 0,
                    $"livre={clear:F4} sombreado={shaded:F4} kW/m²"));

                double insideWall = FieldTransform.RadiationAtPoint(scene, 0, 20, 2);
                results.Add(new Result("a receiver inside solid geometry gets nothing",
                    insideWall == 0));

                var field = FieldTransform.BuildRadiationField(scene, 24, 24, 12, 60);
                double behindWall = 0, beforeWall = 0;
                for (int i = 0; i < 24; i++)
                    for (int j = 0; j < 24; j++)
                        for (int k = 0; k < 12; k++)
                        {
                            double y = -60 + (j + 0.5) * (120.0 / 24);
                            if (y > 25 && field[i, j, k] > behindWall) behindWall = field[i, j, k];
                            if (y > 0 && y < 15 && field[i, j, k] > beforeWall) beforeWall = field[i, j, k];
                        }
                results.Add(new Result("the field is dark behind the wall and lit before it",
                    behindWall == 0 && beforeWall > 0,
                    $"antes={beforeWall:F3} atrás={behindWall:F3} kW/m²"));
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
