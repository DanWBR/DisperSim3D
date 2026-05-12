using System;
using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Runs a <see cref="WindFieldScenario"/> through FluidX3D's GPU LBM solver in-process.
    /// Mirrors the public contract of <see cref="WindFieldRunner"/> (Run + LoadFromCase) so
    /// the caller can swap implementations based on <see cref="WindFieldScenario"/> config
    /// without touching the rest of the pipeline.
    /// </summary>
    public class FluidX3DWindFieldRunner
    {
        /// <summary>
        /// Base lattice-step count. Scaled up at runtime by the actual grid size so the
        /// flow has time to traverse the domain ~5× regardless of grid resolution
        /// (≈ 5×N steps with U_lat≈0.05, which is one crossing every 20N steps).
        /// </summary>
        private const int DefaultStepsPerCell = 80;

        /// <summary>
        /// Re-loads the WindField3D from a previously saved windfield.bin in the scenario's
        /// CasePath. Returns null if the file is missing or unreadable. Mirrors
        /// <see cref="WindFieldRunner.LoadFromCase"/> for the OpenFOAM pipeline.
        /// </summary>
        public static WindField3D LoadFromCase(WindFieldScenario windScenario)
        {
            if (windScenario == null || string.IsNullOrEmpty(windScenario.CasePath))
                return null;
            string binPath = System.IO.Path.Combine(windScenario.CasePath, "windfield.bin");
            return WindFieldSerializer.TryLoad(binPath);
        }

        /// <summary>Runs FluidX3D for the given wind scenario. Obstacles are pre-computed
        /// world-space AABBs (one per child mesh, extracted on the UI thread before this
        /// background-worker entry point is called).</summary>
        public bool Run(WindFieldScenario windScenario, List<BoundingBox> obstacles,
            Action<double, string> progress)
        {
            return Run(windScenario, obstacles, null, progress);
        }

        /// <summary>Run with an optional GPU triangle bundle. When non-null and non-empty,
        /// FluidX3D's GPU raycasting voxelizer is used; otherwise the per-triangle AABB
        /// fallback runs on the CPU bridge.</summary>
        public bool Run(WindFieldScenario windScenario, List<BoundingBox> obstacles,
            TriangleBundle triangles, Action<double, string> progress)
        {
            if (windScenario == null) throw new ArgumentNullException(nameof(windScenario));

            try
            {
                windScenario.Status = WindFieldStatus.Running;
                windScenario.StatusMessage = "FluidX3D: starting...";

                if (!FluidX3DBridge.IsAvailable())
                {
                    windScenario.Status = WindFieldStatus.Failed;
                    windScenario.StatusMessage = "FluidX3D: no OpenCL device available (FluidX3D.dll missing or GPU busy)";
                    return false;
                }

                // LBM needs more cells than a finite-volume solver to give physically
                // useful results. On a coarse 40³ grid the obstacle wakes simply can't
                // be resolved — turbulence kernels need ~10 cells around any obstacle.
                // Quality preset controls both the floor for nx/ny and the steps-per-cell
                // multiplier below. Z stays at ~half of horizontal in all presets.
                int qMin, qStepsPerCell;
                switch (windScenario.FluidX3DQuality)
                {
                    case FluidX3DQuality.Balanced: qMin = 192; qStepsPerCell = 100; break;
                    case FluidX3DQuality.High:     qMin = 256; qStepsPerCell = 120; break;
                    case FluidX3DQuality.Ultra:    qMin = 384; qStepsPerCell = 150; break;
                    default:                       qMin = 128; qStepsPerCell = 80;  break; // Fast
                }
                int requested = windScenario.GridResolution;
                int nx = Math.Max(qMin, requested * 4);
                int ny = nx;
                int nz = Math.Max(qMin / 2, nx / 2);
                double domain = windScenario.DomainSizeM;
                double height = windScenario.DomainHeightM > 0 ? windScenario.DomainHeightM : domain;

                var meteo = windScenario.Meteo;
                var wind = meteo.WindVector;
                double speed = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y + wind.Z * wind.Z);
                if (speed < 1e-3) speed = 1e-3;

                // Pass a conservative target Mach (0.02) explicitly — keeps the BGK collision
                // operator well inside its compressibility-error envelope when obstacles cause
                // local velocity overshoots near sharp voxel corners.
                var units = new FluidX3DUnits(domain, height, nx, ny, nz, speed, inletULat: 0.02);

                // LBM Re-scaling: physical air Re on a 200×200×100 m domain at U=5 m/s is
                // ~6e7 — uncomputable on a 40³ grid. Standard practice for coarse-grid LBM
                // is to fix Re_grid ≈ 200–500 so τ stays comfortably above 0.5 and momentum
                // actually diffuses across the domain in O(1000) steps. SUBGRID (Smagorinsky)
                // restores the wake-shedding behaviour we'd lose at Re_grid that low.
                const double reGrid = 200.0;
                int minN = Math.Min(nx, Math.Min(ny, nz));
                float nuLat = (float)(0.05 * minN / reGrid);
                // No gravity for the pure-wind run — buoyancy belongs in the dispersion solver,
                // not here. Adding gz here was driving spurious vertical motion.
                float gLat = 0f;

                ulong handle = FluidX3DBridge.fx3d_create((uint)nx, (uint)ny, (uint)nz,
                    nuLat, 0f, 0f, gLat, 0f, 0f);
                if (handle == 0UL)
                {
                    windScenario.Status = WindFieldStatus.Failed;
                    windScenario.StatusMessage = "FluidX3D: fx3d_create failed (LBM allocation)";
                    return false;
                }

                try
                {
                    float uxLat = units.ULattice(wind.X);
                    float uyLat = units.ULattice(wind.Y);
                    float uzLat = units.ULattice(wind.Z);

                    // Boundary setup — order matters: lateral free-stream first (sets all
                    // outer cells to TYPE_E with free-stream U), then z-boundaries to overwrite
                    // the bottom row with TYPE_S (no-slip ground). The free-stream cap on the
                    // top face is included in the lateral call.
                    FluidX3DBridge.fx3d_set_lateral_free_stream(handle, uxLat, uyLat, uzLat);
                    FluidX3DBridge.fx3d_set_z_boundaries(handle);

                    // Obstacles — prefer GPU-side triangle voxelization (FluidX3D
                    // raycasts every cell against the mesh, accurate for curved
                    // surfaces). Fall back to per-triangle AABB on the CPU bridge
                    // when no triangle bundle was supplied.
                    int obstacleBoxes;
                    if (triangles != null && triangles.TriangleCount > 0)
                    {
                        obstacleBoxes = FluidX3DObstacleVoxelizer.VoxelizeTrianglesOnGpu(
                            triangles, handle, units);
                    }
                    else
                    {
                        obstacleBoxes = FluidX3DObstacleVoxelizer.VoxelizeBoxes(obstacles, handle, units);
                    }

                    // TEMPERATURE init — MUST be 1.0 (ambient), NOT zero. T=0 host buffer
                    // makes the thermal LBM diverge and pegs all velocities at ±c_s.
                    FluidX3DBridge.fx3d_initial_temperature(handle, 1.0f);

                    // Don't pre-initialize the interior or call write_to_device explicitly.
                    // Experimentally, doing either makes FluidX3D's first lbm.run() pick up
                    // a corrupted device state and clamp every cell at ±c_s within a few
                    // steps. Letting the LBM handle initialization on its own — and driving
                    // the interior purely through the TYPE_E boundary cells — is what works.

                    // Surface parameters in the status so we can verify they reach the DLL.
                    int steadySteps = Math.Max(2000, qStepsPerCell * Math.Max(nx, ny));
                    string logPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "fluidx3d_bridge.log");
                    windScenario.StatusMessage = string.Format(
                        "FluidX3D running {0}x{1}x{2}, {3} steps, {4} obstacle boxes: U_lat=({5:F4},{6:F4},{7:F4}) nu_lat={8:F4} dx={9:F2}m dt={10:F4}s  log={11}",
                        nx, ny, nz, steadySteps, obstacleBoxes, uxLat, uyLat, uzLat, nuLat, units.DxSi, units.DtSi, logPath);

                    // Run
                    FluidX3DBridge.ProgressCallback cb = (done, total) =>
                    {
                        progress?.Invoke((double)done / total, "FluidX3D wind: step " + done + "/" + total);
                        return 0;
                    };
                    int rc = FluidX3DBridge.fx3d_run(handle, (uint)steadySteps, cb);
                    if (rc != 0)
                    {
                        windScenario.Status = WindFieldStatus.Failed;
                        windScenario.StatusMessage = "FluidX3D: solver returned " + rc;
                        return false;
                    }

                    // Read back velocity and convert SI
                    int N = nx * ny * nz;
                    var ux = new float[N]; var uy = new float[N]; var uz = new float[N];
                    FluidX3DBridge.fx3d_read_velocity(handle, ux, uy, uz);

                    var uxArr = new double[nx, ny, nz];
                    var uyArr = new double[nx, ny, nz];
                    var uzArr = new double[nx, ny, nz];
                    double maxMagSi = 0;
                    double sumMagSi = 0;
                    int nNonZero = 0;
                    for (int k = 0; k < nz; k++)
                    {
                        for (int j = 0; j < ny; j++)
                        {
                            int rowBase = (k * ny + j) * nx;
                            for (int i = 0; i < nx; i++)
                            {
                                int n = rowBase + i;
                                double ux_si = units.USi(ux[n]);
                                double uy_si = units.USi(uy[n]);
                                double uz_si = units.USi(uz[n]);
                                uxArr[i, j, k] = ux_si;
                                uyArr[i, j, k] = uy_si;
                                uzArr[i, j, k] = uz_si;
                                double mag = Math.Sqrt(ux_si * ux_si + uy_si * uy_si + uz_si * uz_si);
                                if (mag > maxMagSi) maxMagSi = mag;
                                sumMagSi += mag;
                                if (mag > 1e-4) nNonZero++;
                            }
                        }
                    }
                    double meanMagSi = sumMagSi / Math.Max(1, N);

                    windScenario.WindField = new WindField3D(uxArr, uyArr, uzArr,
                        -domain, domain, -domain, domain, height);
                    windScenario.Status = WindFieldStatus.Ready;

                    // Persist the result so reopening the project doesn't require re-running
                    // the GPU LBM. Stored as windfield.bin in a temp case dir owned by this
                    // wind scenario (one per wf.Id).
                    string saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "DisperSim3D_fx3d_" + (windScenario.Id ?? Guid.NewGuid().ToString("N")));
                    try
                    {
                        System.IO.Directory.CreateDirectory(saveDir);
                        WindFieldSerializer.Save(System.IO.Path.Combine(saveDir, "windfield.bin"),
                            uxArr, uyArr, uzArr, -domain, domain, -domain, domain, height);
                        windScenario.CasePath = saveDir;
                    }
                    catch (Exception saveEx)
                    {
                        // Persistence failure shouldn't fail the run — wind field is in memory.
                        windScenario.CasePath = "";
                        windScenario.StatusMessage = "FluidX3D Ready but save failed: " + saveEx.Message;
                    }

                    windScenario.StatusMessage = string.Format(
                        "FluidX3D Ready ({0}x{1}x{2}, GPU LBM)  |U|_si mean={3:F3} max={4:F3} m/s, {5}% cells non-zero  saved to {6}",
                        nx, ny, nz, meanMagSi, maxMagSi, (int)(100.0 * nNonZero / N), windScenario.CasePath);
                    return true;
                }
                finally
                {
                    FluidX3DBridge.fx3d_destroy(handle);
                }
            }
            catch (Exception ex)
            {
                windScenario.Status = WindFieldStatus.Failed;
                windScenario.StatusMessage = "FluidX3D: " + ex.Message;
                return false;
            }
        }
    }
}
