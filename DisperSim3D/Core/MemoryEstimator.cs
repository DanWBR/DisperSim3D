using System;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Static byte budget estimator for the various solver paths DisperSim 3D
    /// supports. Used by <see cref="Dialogs.GpuPerformanceSettingsDialog"/> and
    /// by the simulation editor to warn the user before running something that
    /// won't fit in VRAM / RAM / disk.
    ///
    /// Per-cell byte costs come from the actual data layouts:
    /// - FluidX3D D3Q19 FP32: 19×4 (f) + 3×4 (u) + 4 (rho) + 1 (flags) = 93 B/cell.
    ///   With TEMPERATURE extension (D3Q7): +7×4 + 4 = +32 B/cell → 125 B/cell.
    ///   FP16S halves the f part: 19×2 + 16 + 4 + 1 = 59 B/cell (rarely used here).
    /// - <see cref="DispersionTracerEngine"/>: 3×8 (u SI doubles) + 2×8 (c, cNext)
    ///   + 1 (blocked) = 41 B/cell.
    /// - <see cref="FireTracerEngine"/>: 3×8 (u) + 4×8 (T,Y + nexts) + 1 (blocked)
    ///   = 57 B/cell.
    /// - OpenFOAM cases: ~150 B/cell for momentum solvers, ~400 B/cell for reactive
    ///   (extra species + h fields), based on field count × 8 bytes.
    /// </summary>
    public static class MemoryEstimator
    {
        public sealed class Estimate
        {
            /// <summary>Bytes allocated on the GPU (FluidX3D LBM grid + buffers).
            /// 0 for non-FluidX3D solvers.</summary>
            public long VRamBytes;
            /// <summary>Bytes allocated in host RAM (CPU tracer fields + cached
            /// snapshot results).</summary>
            public long RamBytes;
            /// <summary>Bytes written to disk (snapshots + bundle case files).</summary>
            public long DiskBytes;
            /// <summary>Effective grid the solver will use (after FluidX3D quality bumps).</summary>
            public int Nx, Ny, Nz;
            /// <summary>Human-readable breakdown line (one-liner shown next to the totals).</summary>
            public string Notes;

            public string Format()
            {
                return string.Format(
                    "Grid {0}×{1}×{2} ({3:N0} cells)\nVRAM: {4}\nRAM:  {5}\nDisk: {6}\n{7}",
                    Nx, Ny, Nz, (long)Nx * Ny * Nz,
                    HumanBytes(VRamBytes), HumanBytes(RamBytes), HumanBytes(DiskBytes),
                    Notes ?? "");
            }
        }

        public static string HumanBytes(long b)
        {
            if (b <= 0) return "—";
            double v = b;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return v < 10 ? v.ToString("F2") + " " + units[i]
                 : v < 100 ? v.ToString("F1") + " " + units[i]
                 :           v.ToString("F0") + " " + units[i];
        }

        /// <summary>Returns the effective LBM grid for a FluidX3D wind run given the
        /// scenario's requested resolution and the quality preset (Fast / Balanced /
        /// High / Ultra). Mirrors the auto-bump logic in <see cref="FluidX3DWindFieldRunner"/>.</summary>
        public static (int nx, int ny, int nz) FluidX3DEffectiveGrid(int requestedRes, FluidX3DQuality quality)
        {
            int qMin;
            switch (quality)
            {
                case FluidX3DQuality.Balanced: qMin = 192; break;
                case FluidX3DQuality.High:     qMin = 256; break;
                case FluidX3DQuality.Ultra:    qMin = 384; break;
                default:                       qMin = 128; break;
            }
            int nx = Math.Max(qMin, requestedRes * 4);
            int ny = nx;
            int nz = Math.Max(qMin / 2, nx / 2);
            return (nx, ny, nz);
        }

        /// <summary>Estimate for a FluidX3D LBM wind-field run.
        /// useTemperatureExtension controls whether the D3Q7 thermal lattice is included.</summary>
        public static Estimate EstimateFluidX3DWind(int requestedRes, FluidX3DQuality quality,
            bool useTemperatureExtension = false)
        {
            var (nx, ny, nz) = FluidX3DEffectiveGrid(requestedRes, quality);
            long cells = (long)nx * ny * nz;
            // D3Q19 FP32 + u + rho + flags.
            long perCell = 19L * 4 + 3 * 4 + 4 + 1;
            if (useTemperatureExtension) perCell += 7L * 4 + 4;
            // SUBGRID (Smagorinsky) adds a 6-component symmetric tensor: 6×4 = 24 B.
            // We don't currently enable it for stability, but quality=High/Ultra
            // implies finer mesh — budget for it conservatively at High+.
            if (quality == FluidX3DQuality.High || quality == FluidX3DQuality.Ultra)
                perCell += 24;
            long vram = cells * perCell;
            // Plus host-side read-back buffer for u (3 × float[N]).
            long readBack = cells * 3 * 4;
            return new Estimate
            {
                Nx = nx, Ny = ny, Nz = nz,
                VRamBytes = vram,
                RamBytes = readBack,
                DiskBytes = cells * 3 * 8, // windfield.bin = 3 × double[N]
                Notes = "D3Q19 FP32 LBM (" + quality + ", " + perCell + " B/cell)"
            };
        }

        /// <summary>Estimate for a CPU dispersion run on a FluidX3D-supplied wind
        /// field. Includes the engine's transported fields + N snapshot copies
        /// kept in <see cref="OpenFoamResult"/> + persisted .bin files.</summary>
        public static Estimate EstimateDispersionCpu(int gridRes, int snapshotCount,
            bool steadyState = false)
        {
            int nx = gridRes, ny = gridRes, nz = Math.Max(8, gridRes / 2);
            long cells = (long)nx * ny * nz;
            const long perCell = 3 * 8 + 2 * 8 + 1; // ux/uy/uz doubles, c+cNext, blocked
            long ram = cells * perCell;
            int snaps = steadyState ? 1 : Math.Max(1, snapshotCount);
            // OpenFoamResult.PreloadField keeps each snapshot in memory as double[,,].
            ram += cells * 8 * snaps;
            long disk = cells * 8 * snaps;
            return new Estimate
            {
                Nx = nx, Ny = ny, Nz = nz,
                VRamBytes = 0,
                RamBytes = ram,
                DiskBytes = disk,
                Notes = "CPU semi-Lagrangian tracer, " + snaps + " snapshot" + (snaps == 1 ? "" : "s")
            };
        }

        /// <summary>Estimate for the dual-tracer fire runner. Snapshots come in
        /// pairs (concentration + temperature).</summary>
        public static Estimate EstimateFire(int gridRes, int snapshotCount)
        {
            int nx = gridRes, ny = gridRes, nz = Math.Max(8, gridRes / 2);
            long cells = (long)nx * ny * nz;
            const long perCell = 3 * 8 + 4 * 8 + 1; // u + T,Tnext,Y,Ynext + blocked
            long ram = cells * perCell;
            int snaps = Math.Max(1, snapshotCount);
            ram += cells * 8 * snaps * 2; // Y + T per snapshot in cache
            long disk = cells * 8 * snaps * 2;
            return new Estimate
            {
                Nx = nx, Ny = ny, Nz = nz,
                VRamBytes = 0,
                RamBytes = ram,
                DiskBytes = disk,
                Notes = "Fire dual-tracer (T + smoke) with Boussinesq"
            };
        }

        /// <summary>Coarse OpenFOAM-case estimate. Per-cell cost is the dominant
        /// field-count × 8 bytes. Reactive cases hold many species + enthalpy +
        /// turbulence quantities, so they're 3× heavier than passive scalar runs.
        /// snappyHexMesh refinement is approximated by <paramref name="refinementLevel"/>:
        /// 0 = base mesh, 1 = ~3× cells, 2 = ~10× cells.</summary>
        public static Estimate EstimateOpenFoam(CfdSolverType solver, int gridRes, int snapshotCount,
            int refinementLevel = 0)
        {
            int nx = gridRes, ny = gridRes, nz = Math.Max(1, gridRes / 2);
            long baseCells = (long)nx * ny * nz;
            double refMult = refinementLevel <= 0 ? 1.0
                          : refinementLevel == 1 ? 3.0
                          :                       10.0;
            long cells = (long)(baseCells * refMult);

            long perCell;
            string notes;
            switch (solver)
            {
                case CfdSolverType.ScalarSimpleFoam:
                case CfdSolverType.RhoSimpleFoam:
                case CfdSolverType.ScalarTransportFoamSteady:
                    perCell = 150;   // U, p, k, eps, scalar
                    notes = "OpenFOAM steady (RANS)";
                    break;
                case CfdSolverType.RhoReactingBuoyantFoam:
                case CfdSolverType.ReactingFoam:
                    perCell = 450;   // U, p, p_rgh, T, h, rho, Y_i × ~4, k, eps, nut, alphat
                    notes = "OpenFOAM compressible reactive";
                    break;
                default:
                    perCell = 200;   // pimpleFoam / buoyantPimpleFoam transient
                    notes = "OpenFOAM transient";
                    break;
            }
            long ram = cells * perCell;
            int snaps = Math.Max(1, snapshotCount);
            long disk = cells * perCell * snaps;
            if (refinementLevel > 0)
                notes += "  + mesh refinement L" + refinementLevel + " (×" + refMult.ToString("F1") + ")";
            return new Estimate
            {
                Nx = nx, Ny = ny, Nz = nz,
                VRamBytes = 0,
                RamBytes = ram,
                DiskBytes = disk,
                Notes = notes
            };
        }

        /// <summary>Top-level convenience that picks the right estimator for the solver.
        /// FluidX3D Dispersion / Fire estimates are GPU (wind) + CPU (tracer) combined.</summary>
        public static Estimate For(CfdSolverType solver, int gridRes, int snapshotCount,
            FluidX3DQuality quality = FluidX3DQuality.Fast, int meshRefinementLevel = 0)
        {
            switch (solver)
            {
                case CfdSolverType.FluidX3DWind:
                    return EstimateFluidX3DWind(gridRes, quality);

                case CfdSolverType.FluidX3DDispersion:
                case CfdSolverType.FluidX3DDispersionSteady:
                {
                    var wind = EstimateFluidX3DWind(gridRes, quality);
                    var cpu  = EstimateDispersionCpu(gridRes, snapshotCount,
                        solver == CfdSolverType.FluidX3DDispersionSteady);
                    return new Estimate
                    {
                        Nx = wind.Nx, Ny = wind.Ny, Nz = wind.Nz,
                        VRamBytes = wind.VRamBytes,
                        RamBytes = wind.RamBytes + cpu.RamBytes,
                        DiskBytes = wind.DiskBytes + cpu.DiskBytes,
                        Notes = wind.Notes + " + " + cpu.Notes
                    };
                }

                case CfdSolverType.FluidX3DFire:
                {
                    var wind = EstimateFluidX3DWind(gridRes, quality);
                    var fire = EstimateFire(gridRes, snapshotCount);
                    return new Estimate
                    {
                        Nx = wind.Nx, Ny = wind.Ny, Nz = wind.Nz,
                        VRamBytes = wind.VRamBytes,
                        RamBytes = wind.RamBytes + fire.RamBytes,
                        DiskBytes = wind.DiskBytes + fire.DiskBytes,
                        Notes = wind.Notes + " + " + fire.Notes
                    };
                }

                case CfdSolverType.GaussianPuff:
                case CfdSolverType.GaussianPlume:
                    return EstimateDispersionCpu(gridRes, snapshotCount);

                default:
                    return EstimateOpenFoam(solver, gridRes, snapshotCount, meshRefinementLevel);
            }
        }
    }
}
