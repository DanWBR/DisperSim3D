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
        /// Lattice steps run to let the inlet front traverse the domain and the field
        /// settle. ~3 domain-crossings at the chosen lattice velocity is enough for
        /// statistically steady streamlines around bluff obstacles.
        /// </summary>
        private const int DefaultSteadySteps = 3000;

        public bool Run(WindFieldScenario windScenario, List<BoundingBox> obstacles,
            Action<double, string> progress)
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

                int nx = windScenario.GridResolution;
                int ny = windScenario.GridResolution;
                int nz = Math.Max(8, windScenario.GridResolution / 2);
                double domain = windScenario.DomainSizeM;
                double height = windScenario.DomainHeightM > 0 ? windScenario.DomainHeightM : domain;

                var meteo = windScenario.Meteo;
                var wind = meteo.WindVector;
                double speed = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y + wind.Z * wind.Z);
                if (speed < 1e-3) speed = 1e-3;

                var units = new FluidX3DUnits(domain, height, nx, ny, nz, speed);

                // Kinematic viscosity of air ≈ 1.5e-5 m²/s; with SUBGRID enabled, the
                // effective LBM nu still uses the molecular value — Smagorinsky augments.
                float nuLat = units.NuLattice(1.5e-5);
                float gLat  = units.GLattice(-9.81);

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
                    // Boundary setup
                    FluidX3DBridge.fx3d_set_z_boundaries(handle);
                    FluidX3DBridge.fx3d_set_inlet_x(handle,
                        units.ULattice(wind.X), units.ULattice(wind.Y), units.ULattice(wind.Z));
                    FluidX3DBridge.fx3d_set_outlet_x(handle);

                    // Obstacles
                    if (obstacles != null)
                    {
                        // Reuse the decoration voxelizer logic but on raw BBoxes.
                        foreach (var bb in obstacles)
                        {
                            var (x0, y0, z0) = units.SiToLattice(bb.Min.X, bb.Min.Y, Math.Max(0, bb.Min.Z));
                            var (x1, y1, z1) = units.SiToLattice(bb.Max.X, bb.Max.Y, Math.Max(0, bb.Max.Z));
                            FluidX3DBridge.fx3d_set_box_solid(handle,
                                Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1),
                                Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));
                        }
                    }

                    // Run
                    FluidX3DBridge.ProgressCallback cb = (done, total) =>
                    {
                        progress?.Invoke((double)done / total, "FluidX3D wind: step " + done + "/" + total);
                        return 0;
                    };
                    int rc = FluidX3DBridge.fx3d_run(handle, (uint)DefaultSteadySteps, cb);
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
                    for (int k = 0; k < nz; k++)
                    {
                        for (int j = 0; j < ny; j++)
                        {
                            int rowBase = (k * ny + j) * nx;
                            for (int i = 0; i < nx; i++)
                            {
                                int n = rowBase + i;
                                uxArr[i, j, k] = units.USi(ux[n]);
                                uyArr[i, j, k] = units.USi(uy[n]);
                                uzArr[i, j, k] = units.USi(uz[n]);
                            }
                        }
                    }

                    windScenario.WindField = new WindField3D(uxArr, uyArr, uzArr,
                        -domain, domain, -domain, domain, height);
                    windScenario.Status = WindFieldStatus.Ready;
                    windScenario.StatusMessage = string.Format("FluidX3D Ready ({0}x{1}x{2}, GPU LBM)", nx, ny, nz);
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
