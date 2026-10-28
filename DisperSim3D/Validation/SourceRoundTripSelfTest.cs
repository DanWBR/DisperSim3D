using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Saves a release source with every field set away from its default, reloads
    /// the project and compares the three places a source is stored: the top-level
    /// list, a dispersion scenario and the snapshot of a project simulation.
    ///
    /// The three used to be written and read by different code, which is how the
    /// high-pressure leak, the per-source gas, the IOGP inventory, the hole band,
    /// the leak frequency and the stack geometry came to be dropped on reload.
    ///
    /// Run via <c>DisperSim3D.CLI --source-roundtrip-selftest</c>.
    /// </summary>
    public static class SourceRoundTripSelfTest
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

        /// <summary>Runs the round trip in a temporary directory, removed before returning.</summary>
        public static IReadOnlyList<Result> Run()
        {
            var results = new List<Result>();
            string dir = Path.Combine(Path.GetTempPath(),
                "dispersim-source-roundtrip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                Scene3D original = BuildScene();
                string path = Path.Combine(dir, "roundtrip.xml");
                SceneFileSaver.Save(original, path);
                Scene3D reloaded = SceneFileLoader.Load(path);

                if (reloaded.TopLevelSources.Count == 1)
                    Compare(original.TopLevelSources[0], reloaded.TopLevelSources[0], "top-level source: ", results);
                else
                    results.Add(new Result("top-level source survives", false,
                        $"expected 1, got {reloaded.TopLevelSources.Count}"));

                if (reloaded.DispersionScenarios.Count == 1 && reloaded.DispersionScenarios[0].Sources.Count == 1)
                    Compare(original.DispersionScenarios[0].Sources[0],
                            reloaded.DispersionScenarios[0].Sources[0], "scenario source: ", results);
                else
                    results.Add(new Result("scenario source survives", false, "scenario or its source is missing"));

                if (reloaded.Simulations.Count == 1 && reloaded.Simulations[0].SnapshotSource != null)
                    Compare(original.Simulations[0].SnapshotSource,
                            reloaded.Simulations[0].SnapshotSource, "snapshot source: ", results);
                else
                    results.Add(new Result("simulation snapshot source survives", false, "snapshot is missing"));

                // The rate a pressurised release produces is derived, so it is the
                // end-to-end check that the leak definition really came back.
                ReleaseSource3D before = original.TopLevelSources[0];
                ReleaseSource3D after = reloaded.TopLevelSources.Count == 1
                    ? reloaded.TopLevelSources[0] : new ReleaseSource3D();
                results.Add(Near("derived release rate (kg/s)",
                    before.EffectiveReleaseRateKgPerS, after.EffectiveReleaseRateKgPerS, 1e-9));

                // The wind rose travels the same save/load contract and used to be
                // written without ever being read back.
                int roseBins = reloaded.WindRose?.Bins.Count ?? 0;
                results.Add(Same("wind rose bin count", original.WindRose.Bins.Count, roseBins));
                if (roseBins == original.WindRose.Bins.Count && roseBins > 0)
                {
                    WindRoseBin a = original.WindRose.Bins[0], b = reloaded.WindRose.Bins[0];
                    results.Add(Near("wind rose direction", a.DirectionDeg, b.DirectionDeg, 1e-9));
                    results.Add(Near("wind rose frequency", a.Frequency, b.Frequency, 1e-12));
                    results.Add(Near("wind rose speed", a.WindSpeed, b.WindSpeed, 1e-9));
                    results.Add(Same("wind rose stability", a.StabilityClass, b.StabilityClass));
                }
            }
            catch (Exception ex)
            {
                results.Add(new Result("round trip completed without throwing", false, ex.Message));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* temp dir, best effort */ }
            }

            return results;
        }

        /// <summary>Prints results and returns <c>true</c> when every case passes.</summary>
        public static bool RunAndPrint(TextWriter writer)
        {
            IReadOnlyList<Result> results = Run();
            int passed = 0;
            foreach (Result r in results)
            {
                writer.WriteLine(r.ToString());
                if (r.Passed) passed++;
            }
            writer.WriteLine();
            writer.WriteLine(passed == results.Count
                ? $"OK — {passed}/{results.Count} source round-trip checks passed."
                : $"FAIL — {passed}/{results.Count} passed.");
            return passed == results.Count;
        }

        /// <summary>
        /// One source, every field off its default, stored in all three places a
        /// project keeps sources.
        /// </summary>
        private static Scene3D BuildScene()
        {
            var scene = new Scene3D();
            scene.GeneralSettings.Name = "source round trip";

            ReleaseSource3D src = BuildSource();
            scene.TopLevelSources.Add(src);

            var scenario = new DispersionScenario { Name = "scenario" };
            scenario.Sources.Add(BuildSource());
            scene.DispersionScenarios.Add(scenario);

            scene.WindRose = new WindRoseData { ShowIn3D = false };
            scene.WindRose.Bins.Add(new WindRoseBin
            {
                DirectionDeg = 225,
                Frequency = 0.1234,
                WindSpeed = 3.5,
                StabilityClass = PasquillStabilityClass.F
            });

            scene.Simulations.Add(new Simulation
            {
                Id = Guid.NewGuid().ToString(),
                Name = "simulation",
                SourceId = src.Id,
                SnapshotSource = BuildSource()
            });

            return scene;
        }

        private static ReleaseSource3D BuildSource() => new ReleaseSource3D
        {
            Id = "11111111-2222-3333-4444-555555555555",
            Name = "Pressurised release",
            AttachedUnitId = "unit-7",
            GasRefId = "gas-ref-9",
            Position = new Geometry.Point3D(12.5, -3.25, 4.75),
            ReleaseRateKgPerS = 1.75,
            ReleaseDurationS = 420,
            PuffIntervalS = 2.5,
            ReleaseHeightOffset = 1.25,
            ReleaseAzimuthDeg = 123.5,
            ReleaseElevationDeg = -17.5,
            StackDiameterM = 0.0254,
            ExitVelocityMPerS = 31.4,
            ExitTemperatureK = 263.15,
            UseJetTransition = true,
            CarryAerosol = true,
            AerosolCompound = "Propane",
            IsVisible = false,
            HoleSizeBand = IogpHoleSizeBand.Large,
            AutoComputeLeakFrequency = false,
            LeakFrequencyPerYear = 3.7e-5,
            HighPressureLeak = new HighPressureLeakParams
            {
                VesselPressurePa = 5.0e5,
                VesselTemperatureK = 303.15,
                OrificeDiameterM = 0.0211,
                VesselVolumeM3 = 42.5,
                GasGamma = 1.28,
                GasMolarMassKgMol = 0.0274,
                DischargeCoefficient = 0.8,
                SpecifyMassFlow = false,
                SpecifiedMassFlowKgPerS = 2.25
            },
            Gas = new GasProperties
            {
                Name = "Test mixture",
                MolarMass = 0.0274,
                SpecificHeatJPerKgK = 1798.4,
                LFL = 0.037,
                UFL = 0.1301,
                IDLH = 0.0123,
                ERPG1 = 0.001,
                ERPG2 = 0.002,
                ERPG3 = 0.003,
                HalfLifeS = 3600,
                DryDepositionVelocityMPerS = 0.002,
                IsCryogenic = true
            },
            EquipmentInventory = new List<EquipmentInventoryItem>
            {
                new EquipmentInventoryItem
                {
                    Type = IogpEquipmentType.FlangedJoint,
                    NominalDiameterMm = 150,
                    Count = 12,
                    Note = "header"
                }
            }
        };

        private static void Compare(ReleaseSource3D a, ReleaseSource3D b, string prefix, List<Result> results)
        {
            results.Add(Same(prefix + "name", a.Name, b.Name));
            results.Add(Same(prefix + "attached unit", a.AttachedUnitId, b.AttachedUnitId));
            results.Add(Same(prefix + "gas reference", a.GasRefId, b.GasRefId));
            results.Add(Near(prefix + "position", a.Position.X + a.Position.Y + a.Position.Z,
                                                   b.Position.X + b.Position.Y + b.Position.Z, 1e-9));
            results.Add(Near(prefix + "release rate", a.ReleaseRateKgPerS, b.ReleaseRateKgPerS, 1e-9));
            results.Add(Near(prefix + "release duration", a.ReleaseDurationS, b.ReleaseDurationS, 1e-9));
            results.Add(Near(prefix + "puff interval", a.PuffIntervalS, b.PuffIntervalS, 1e-9));
            results.Add(Near(prefix + "height offset", a.ReleaseHeightOffset, b.ReleaseHeightOffset, 1e-9));
            results.Add(Near(prefix + "azimuth", a.ReleaseAzimuthDeg, b.ReleaseAzimuthDeg, 1e-9));
            results.Add(Near(prefix + "elevation", a.ReleaseElevationDeg, b.ReleaseElevationDeg, 1e-9));
            results.Add(Near(prefix + "stack diameter", a.StackDiameterM, b.StackDiameterM, 1e-12));
            results.Add(Near(prefix + "exit velocity", a.ExitVelocityMPerS, b.ExitVelocityMPerS, 1e-9));
            results.Add(Near(prefix + "exit temperature", a.ExitTemperatureK, b.ExitTemperatureK, 1e-9));
            results.Add(Same(prefix + "jet transition", a.UseJetTransition, b.UseJetTransition));
            results.Add(Same(prefix + "carry aerosol", a.CarryAerosol, b.CarryAerosol));
            results.Add(Same(prefix + "aerosol compound", a.AerosolCompound, b.AerosolCompound));
            results.Add(Same(prefix + "visible", a.IsVisible, b.IsVisible));
            results.Add(Same(prefix + "hole size band", a.HoleSizeBand, b.HoleSizeBand));
            results.Add(Same(prefix + "auto leak frequency", a.AutoComputeLeakFrequency, b.AutoComputeLeakFrequency));
            results.Add(Near(prefix + "leak frequency", a.LeakFrequencyPerYear, b.LeakFrequencyPerYear, 1e-15));

            if (a.HighPressureLeak == null || b.HighPressureLeak == null)
            {
                results.Add(new Result(prefix + "high-pressure leak", b.HighPressureLeak != null,
                    b.HighPressureLeak == null ? "lost on reload" : ""));
            }
            else
            {
                HighPressureLeakParams x = a.HighPressureLeak, y = b.HighPressureLeak;
                results.Add(Near(prefix + "hp vessel pressure", x.VesselPressurePa, y.VesselPressurePa, 1e-6));
                results.Add(Near(prefix + "hp vessel temperature", x.VesselTemperatureK, y.VesselTemperatureK, 1e-9));
                results.Add(Near(prefix + "hp orifice", x.OrificeDiameterM, y.OrificeDiameterM, 1e-12));
                results.Add(Near(prefix + "hp vessel volume", x.VesselVolumeM3, y.VesselVolumeM3, 1e-9));
                results.Add(Near(prefix + "hp gamma", x.GasGamma, y.GasGamma, 1e-9));
                results.Add(Near(prefix + "hp molar mass", x.GasMolarMassKgMol, y.GasMolarMassKgMol, 1e-12));
                results.Add(Near(prefix + "hp discharge coefficient", x.DischargeCoefficient, y.DischargeCoefficient, 1e-9));
                results.Add(Same(prefix + "hp specify mass flow", x.SpecifyMassFlow, y.SpecifyMassFlow));
                results.Add(Near(prefix + "hp specified mass flow", x.SpecifiedMassFlowKgPerS, y.SpecifiedMassFlowKgPerS, 1e-9));
            }

            if (a.Gas == null || b.Gas == null)
            {
                results.Add(new Result(prefix + "per-source gas", b.Gas != null,
                    b.Gas == null ? "lost on reload" : ""));
            }
            else
            {
                results.Add(Same(prefix + "gas name", a.Gas.Name, b.Gas.Name));
                results.Add(Near(prefix + "gas molar mass", a.Gas.MolarMass, b.Gas.MolarMass, 1e-12));
                results.Add(Near(prefix + "gas cp", a.Gas.SpecificHeatJPerKgK, b.Gas.SpecificHeatJPerKgK, 1e-9));
                results.Add(Near(prefix + "gas LFL", a.Gas.LFL, b.Gas.LFL, 1e-12));
                results.Add(Near(prefix + "gas UFL", a.Gas.UFL, b.Gas.UFL, 1e-12));
                results.Add(Near(prefix + "gas IDLH", a.Gas.IDLH, b.Gas.IDLH, 1e-12));
                results.Add(Near(prefix + "gas ERPG2", a.Gas.ERPG2, b.Gas.ERPG2, 1e-12));
                results.Add(Near(prefix + "gas half life", a.Gas.HalfLifeS, b.Gas.HalfLifeS, 1e-9));
                results.Add(Near(prefix + "gas dry deposition", a.Gas.DryDepositionVelocityMPerS,
                                                                 b.Gas.DryDepositionVelocityMPerS, 1e-12));
                results.Add(Same(prefix + "gas cryogenic", a.Gas.IsCryogenic, b.Gas.IsCryogenic));
            }

            int expected = a.EquipmentInventory?.Count ?? 0;
            int got = b.EquipmentInventory?.Count ?? 0;
            results.Add(Same(prefix + "inventory item count", expected, got));
            if (expected > 0 && got == expected)
            {
                EquipmentInventoryItem x = a.EquipmentInventory[0], y = b.EquipmentInventory[0];
                results.Add(Same(prefix + "inventory type", x.Type, y.Type));
                results.Add(Near(prefix + "inventory diameter", x.NominalDiameterMm, y.NominalDiameterMm, 1e-9));
                results.Add(Same(prefix + "inventory count", x.Count, y.Count));
                results.Add(Same(prefix + "inventory note", x.Note, y.Note));
            }
        }

        private static Result Same<T>(string name, T expected, T actual)
            => new Result(name, Equals(expected, actual),
                Equals(expected, actual) ? "" : $"expected {expected?.ToString() ?? "null"}, got {actual?.ToString() ?? "null"}");

        private static Result Near(string name, double expected, double actual, double tolerance)
        {
            bool ok = Math.Abs(expected - actual) <= tolerance
                      || (expected != 0 && Math.Abs((expected - actual) / expected) <= tolerance);
            return new Result(name, ok, ok ? "" : string.Format(CultureInfo.InvariantCulture,
                "expected {0:G9}, got {1:G9}", expected, actual));
        }
    }
}
