using System;
using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Sanity tests for the <see cref="IogpFrequencyTable"/>. There's no test
    /// framework wired in this solution, so these run as self-checking static
    /// methods that throw <see cref="Exception"/> on a mismatch. Two entry
    /// points use them:
    /// <list type="bullet">
    ///   <item>Help → Run IOGP self-test in the desktop app — pops a MessageBox.</item>
    ///   <item><c>DisperSim3D.App.exe --iogp-selftest</c> — prints to stdout.</item>
    /// </list>
    ///
    /// The expected values are transcribed from IOGP 434-01 v1.1 (May 2021),
    /// "Tabulation" panels of the per-equipment datasheets in Section 2.2
    /// (2006–2015 dataset). Tolerance is ±5% to absorb any single-digit
    /// transcription typos and floating-point representation noise.
    /// </summary>
    public static class IogpTableTests
    {
        /// <summary>Runs every test and returns a human-readable summary. Throws
        /// on the first failure with a diagnostic message identifying the
        /// failing row.</summary>
        public static string RunAll()
        {
            var sb = new System.Text.StringBuilder();
            int pass = 0, fail = 0;
            foreach (var t in BuildCases())
            {
                try
                {
                    double actual = IogpFrequencyTable.FrequencyFor(t.Type, t.DiameterMm, t.Band);
                    AssertApproxEqual(t.Expected, actual, 0.05,
                        $"{t.Type} d={t.DiameterMm}mm band={t.Band}");
                    pass++;
                    sb.AppendLine($"PASS  {t.Type,-28}  d={t.DiameterMm,4}mm  {t.Band,-7}  expected={t.Expected:E2}  actual={actual:E2}");
                }
                catch (Exception ex)
                {
                    fail++;
                    sb.AppendLine($"FAIL  {ex.Message}");
                    throw new Exception(
                        $"IOGP table self-test failed:\n{sb}\n\nCheck transcription " +
                        $"of {t.Type} datasheet in IogpFrequencyTable.cs against IOGP 434-01.", ex);
                }
            }

            // Aggregate-inventory smoke test.
            sb.AppendLine();
            sb.AppendLine("Aggregate inventory test:");
            var inv = new List<EquipmentInventoryItem>
            {
                new EquipmentInventoryItem { Type = IogpEquipmentType.SteelProcessPipe, NominalDiameterMm = 150, Count = 50 },
                new EquipmentInventoryItem { Type = IogpEquipmentType.FlangedJoint,    NominalDiameterMm = 150, Count = 12 },
                new EquipmentInventoryItem { Type = IogpEquipmentType.ManualValve,     NominalDiameterMm = 150, Count = 4 },
            };
            double total = IogpFrequencyTable.TotalSourceFrequency(inv, IogpHoleSizeBand.Medium);
            // Hand calc per IOGP 2006–2015 at 150 mm Medium band:
            //   pipe   = 1.6e-6 per m·yr  → 50 m × 1.6e-6 = 8.00e-5
            //   flange = 1.4e-6 per joint → 12   × 1.4e-6 = 1.68e-5
            //   manual = 3.8e-6 per valve → 4    × 3.8e-6 = 1.52e-5
            //   total  = 1.12e-4 events/yr
            double expectedTotal = 50 * 1.6e-6 + 12 * 1.4e-6 + 4 * 3.8e-6;
            AssertApproxEqual(expectedTotal, total, 0.01, "inventory sum");
            sb.AppendLine($"PASS  inventory sum  expected={expectedTotal:E3}  actual={total:E3}");
            pass++;

            // Geometric-mean hole-size test.
            sb.AppendLine();
            sb.AppendLine("Geometric mean hole sizes:");
            AssertApproxEqual(Math.Sqrt(3.0), IogpFrequencyTable.GeometricMeanHoleSizeMm(IogpHoleSizeBand.Tiny), 0.001, "Tiny GM");
            AssertApproxEqual(Math.Sqrt(30.0), IogpFrequencyTable.GeometricMeanHoleSizeMm(IogpHoleSizeBand.Small), 0.001, "Small GM");
            AssertApproxEqual(Math.Sqrt(500.0), IogpFrequencyTable.GeometricMeanHoleSizeMm(IogpHoleSizeBand.Medium), 0.001, "Medium GM");
            AssertApproxEqual(Math.Sqrt(7500.0), IogpFrequencyTable.GeometricMeanHoleSizeMm(IogpHoleSizeBand.Large), 0.001, "Large GM");
            sb.AppendLine("PASS  geometric-mean diameters match Math.Sqrt(low × high)");
            pass++;

            sb.AppendLine();
            sb.AppendLine($"Total: {pass} passed, {fail} failed.");
            return sb.ToString();
        }

        private static IEnumerable<Case> BuildCases() => new[]
        {
            // ── Pipes (per metre·year), datasheet 1 ──
            new Case(IogpEquipmentType.SteelProcessPipe,  50, IogpHoleSizeBand.Medium, 2.8e-6),
            new Case(IogpEquipmentType.SteelProcessPipe, 150, IogpHoleSizeBand.Medium, 1.6e-6),
            new Case(IogpEquipmentType.SteelProcessPipe, 150, IogpHoleSizeBand.Tiny,   9.5e-6),
            new Case(IogpEquipmentType.SteelProcessPipe, 300, IogpHoleSizeBand.Rupture, 4.6e-7),

            // ── Flanged joints (per joint·year), datasheet 2 ──
            new Case(IogpEquipmentType.FlangedJoint,    150, IogpHoleSizeBand.Medium, 1.4e-6),
            new Case(IogpEquipmentType.FlangedJoint,    300, IogpHoleSizeBand.Tiny,   1.3e-5),

            // ── Manual valves (per valve·year), datasheet 3 ──
            new Case(IogpEquipmentType.ManualValve,     150, IogpHoleSizeBand.Medium, 3.8e-6),
            new Case(IogpEquipmentType.ManualValve,     300, IogpHoleSizeBand.Tiny,   2.9e-5),

            // ── Actuated valves (per valve·year), datasheet 4 ──
            new Case(IogpEquipmentType.ActuatedValve,   150, IogpHoleSizeBand.Medium, 1.8e-5),
            new Case(IogpEquipmentType.ActuatedValve,    50, IogpHoleSizeBand.Tiny,   1.4e-4),

            // ── Instrument connections (per connection·year), datasheet 5 ──
            new Case(IogpEquipmentType.InstrumentConnection, 50, IogpHoleSizeBand.Medium, 2.0e-5),
            new Case(IogpEquipmentType.InstrumentConnection, 50, IogpHoleSizeBand.Tiny,   1.2e-4),

            // ── Pressure vessels (per vessel·year), datasheet 6 ──
            new Case(IogpEquipmentType.PressureVessel,  150, IogpHoleSizeBand.Medium, 9.3e-5),
            new Case(IogpEquipmentType.PressureVessel,  300, IogpHoleSizeBand.Large,  2.5e-5),

            // ── Centrifugal pumps (per pump·year), datasheet 7 ──
            new Case(IogpEquipmentType.PumpCentrifugal, 150, IogpHoleSizeBand.Medium, 1.4e-4),
            new Case(IogpEquipmentType.PumpCentrifugal, 300, IogpHoleSizeBand.Rupture, 4.0e-6),

            // ── Centrifugal compressors (per compressor·year), datasheet 9 ──
            new Case(IogpEquipmentType.CompressorCentrifugal, 150, IogpHoleSizeBand.Medium, 6.7e-4),
            new Case(IogpEquipmentType.CompressorCentrifugal, 600, IogpHoleSizeBand.Rupture, 1.1e-4),

            // ── Plate HX (per HX·year), datasheet 13 ──
            new Case(IogpEquipmentType.HxPlate,         150, IogpHoleSizeBand.Tiny,   5.6e-3),

            // ── Flexible pipe (per metre·year), datasheet 17 ──
            new Case(IogpEquipmentType.FlexiblePipe,     50, IogpHoleSizeBand.Medium, 1.7e-4),
            new Case(IogpEquipmentType.FlexiblePipe,    600, IogpHoleSizeBand.Rupture, 7.5e-6),

            // ── Xmas trees (per tree·year), datasheet 21 ──
            new Case(IogpEquipmentType.XmasTree,        150, IogpHoleSizeBand.Medium, 4.4e-5),

            // ── Turbines (per turbine·year), datasheet 22 ──
            new Case(IogpEquipmentType.Turbine,         150, IogpHoleSizeBand.Medium, 7.9e-4),

            // ── Pipeline ESDVs (per valve·year), datasheet 23 ──
            new Case(IogpEquipmentType.PipelineEsdv,    150, IogpHoleSizeBand.Medium, 9.8e-5),

            // ── SSIV assemblies (per assembly·year), datasheet 24 ──
            new Case(IogpEquipmentType.SsivAssembly,    150, IogpHoleSizeBand.Medium, 1.8e-4),
        };

        private static void AssertApproxEqual(double expected, double actual, double relTol, string label)
        {
            if (expected == 0.0)
            {
                if (Math.Abs(actual) > 1e-12)
                    throw new Exception($"{label}: expected 0, got {actual:E3}");
                return;
            }
            double rel = Math.Abs(actual - expected) / Math.Abs(expected);
            if (rel > relTol)
                throw new Exception(
                    $"{label}: expected {expected:E3}, got {actual:E3}  (rel-err {rel:P1} > tol {relTol:P0})");
        }

        private sealed class Case
        {
            public IogpEquipmentType Type;
            public double DiameterMm;
            public IogpHoleSizeBand Band;
            public double Expected;

            public Case(IogpEquipmentType t, double d, IogpHoleSizeBand b, double e)
            {
                Type = t; DiameterMm = d; Band = b; Expected = e;
            }
        }
    }
}
