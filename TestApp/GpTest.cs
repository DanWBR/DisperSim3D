using DisperSim3D.Core;
using DisperSim3D.Models;
using System;

namespace TestApp
{
    static class GpTest
    {
        public static void Run()
        {
            var scenario = new DispersionScenario
            {
                Name = "Test",
                SimulationDurationS = 300,
                TimeStepS = 0.5,
                DomainSizeM = 200,
                GridResolution = 80  // 5m cells for 200m domain
            };
            scenario.Meteo.WindSpeed = 0.1;
            scenario.Meteo.WindDirectionDeg = 270;
            scenario.Meteo.StabilityClass = PasquillStabilityClass.D;

            var src = new ReleaseSource3D
            {
                Name = "Source1",
                ReleaseRateKgPerS = 0.5,
                ReleaseDurationS = 300,
                PuffIntervalS = 1,
                ReleaseHeightOffset = 2
            };
            src.Gas = new GasProperties { Name = "Methane", MolarMass = 0.01604, LFL = 0.033 };
            src.HighPressureLeak = new HighPressureLeakParams
            {
                VesselPressurePa = 1000000,
                VesselTemperatureK = 293.15,
                OrificeDiameterM = 0.025,
                VesselVolumeM3 = 10,
                GasGamma = 1.4,
                GasMolarMassKgMol = 0.01604
            };
            scenario.Sources.Add(src);

            Console.WriteLine("Initializing engine...");
            var engine = new GaussianPuffEngine();
            engine.Initialize(scenario);

            int nx = 80, ny = 80, nz = 40;
            double domain = 200;
            double cellSizeX = (domain * 2.0) / nx;
            double cellSizeY = (domain * 2.0) / ny;
            double cellSizeZ = domain / nz;

            double endTime = 300;
            double dt = 0.5;
            int totalSteps = (int)Math.Ceiling(endTime / dt);
            int writeEvery = Math.Max(1, totalSteps / 10);

            Console.WriteLine("totalSteps={0}, writeEvery={1}, grid={2}x{3}x{4}", totalSteps, writeEvery, nx, ny, nz);
            Console.WriteLine("EffectiveDiameter={0}, ExitVelocity={1}", src.EffectiveDiameterM, src.ComputedExitVelocity);

            int writtenCount = 0;
            for (int step = 1; step <= totalSteps; step++)
            {
                double t = step * dt;
                if (t > endTime) t = endTime;
                engine.StepTo(t);

                if (step % writeEvery == 0 || step == totalSteps)
                {
                    double maxC = 0;
                    int nonZero = 0;
                    for (int i = 0; i < nx; i++)
                        for (int j = 0; j < ny; j++)
                            for (int k = 0; k < nz; k++)
                            {
                                double x = -domain + (i + 0.5) * cellSizeX;
                                double y = -domain + (j + 0.5) * cellSizeY;
                                double z = (k + 0.5) * cellSizeZ;
                                double c = engine.EvaluateConcentration(x, y, z);
                                if (c > maxC) maxC = c;
                                if (c > 1e-15) nonZero++;
                            }

                    writtenCount++;
                    Console.WriteLine("  t={0,7:F1}s  puffs={1,4}  maxC={2:E3}  nonZeroCells={3}",
                        t, engine.ActivePuffs.Count, maxC, nonZero);
                }
            }

            Console.WriteLine("\nDone. {0} write steps.", writtenCount);

            // Reset and check puff details
            var engine2 = new GaussianPuffEngine();
            engine2.Initialize(scenario);
            engine2.StepTo(10.0);

            Console.WriteLine("\n--- Puff details at t=10s ---");
            Console.WriteLine("Active puffs: {0}", engine2.ActivePuffs.Count);
            Console.WriteLine("MassPerPuff: {0}", src.MassPerPuff);
            Console.WriteLine("WindVector: {0}", scenario.Meteo.WindVector);
            Console.WriteLine("WindSpeed: {0}", scenario.Meteo.WindSpeed);

            foreach (var p in engine2.ActivePuffs)
            {
                Console.WriteLine("  Puff: Q={0:F4} Origin=({1:F1},{2:F1},{3:F1}) Center=({4:F1},{5:F1},{6:F1})",
                    p.Q, p.Origin.X, p.Origin.Y, p.Origin.Z,
                    p.CenterX, p.CenterY, p.CenterZ);
                Console.WriteLine("    Sigma=({0:F3},{1:F3},{2:F3}) DecayFactor={3:F6}",
                    p.SigmaX, p.SigmaY, p.SigmaZ, p.DecayFactor);
                Console.WriteLine("    Bounds: ({0:F1},{1:F1},{2:F1})->({3:F1},{4:F1},{5:F1})",
                    p.MinBound.X, p.MinBound.Y, p.MinBound.Z,
                    p.MaxBound.X, p.MaxBound.Y, p.MaxBound.Z);
                Console.WriteLine("    Jet: HasJet={0} JetVel=({1:F1},{2:F1},{3:F1}) tau={4:F2}",
                    p.HasJet, p.JetVelocity.X, p.JetVelocity.Y, p.JetVelocity.Z, p.JetTimeConstantS);
                Console.WriteLine("    WindAtH=({0:F3},{1:F3},{2:F3})",
                    p.WindVectorAtHeight.X, p.WindVectorAtHeight.Y, p.WindVectorAtHeight.Z);

                double cCenter = engine2.EvaluateConcentration(p.CenterX, p.CenterY, p.CenterZ);
                Console.WriteLine("    Concentration at center: {0:E3}", cCenter);
            }
        }
    }
}
