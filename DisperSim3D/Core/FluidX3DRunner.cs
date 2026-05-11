using System;
using System.ComponentModel;
using System.Threading;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// FluidX3D-backed transient dispersion runner. Mirrors <see cref="OpenFoamRunner"/>'s
    /// event surface (ProgressUpdated / Completed / Failed) so <c>SimulationManager.RunCfdAsync</c>
    /// can dispatch to either implementation without other plumbing changes.
    /// </summary>
    public class FluidX3DRunner
    {
        private BackgroundWorker _worker;
        private string _casePath;
        private CancellationTokenSource _cts;
        private volatile bool _cancelled;
        private ulong _handle;

        public event EventHandler<OpenFoamProgress> ProgressUpdated;
        public event EventHandler<OpenFoamResult> Completed;
        public event EventHandler<string> Failed;

        public bool IsRunning => _worker != null && _worker.IsBusy;
        public string CasePath => _casePath;

        public void Cancel()
        {
            _cancelled = true;
            try { _cts?.Cancel(); } catch { }
            if (_handle != 0UL)
            {
                // The native run loop checks the progress callback's return value to bail out.
                // It will see _cancelled via the callback closure and stop on the next chunk.
            }
        }

        public void RunAsync(DispersionScenario scenario, CfdConfiguration config,
            CfdSolverType solverType = CfdSolverType.FluidX3DDispersion)
        {
            if (IsRunning) return;
            _cancelled = false;
            _cts = new CancellationTokenSource();

            _worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            _worker.DoWork += (s, e) =>
            {
                try
                {
                    if (!FluidX3DBridge.IsAvailable())
                    {
                        Failed?.Invoke(this, "FluidX3D.dll missing or no OpenCL device available");
                        return;
                    }

                    Report(0.01, "FluidX3D: preparing case...");

                    int nx = scenario.GridResolution;
                    int ny = scenario.GridResolution;
                    int nz = Math.Max(8, scenario.GridResolution / 2);
                    double domain = scenario.DomainSizeM;
                    double height = domain; // wind-field height heuristic — caller can override later
                    double duration = scenario.SimulationDurationS;
                    double writeInterval = Math.Max(scenario.SimulationDurationS / 20.0, 1.0);

                    var meteo = scenario.Meteo;
                    var wind = meteo.WindVector;
                    double speed = Math.Sqrt(wind.X * wind.X + wind.Y * wind.Y + wind.Z * wind.Z);
                    if (speed < 1e-3) speed = 1e-3;

                    var units = new FluidX3DUnits(domain, height, nx, ny, nz, speed);
                    float nuLat = units.NuLattice(1.5e-5);
                    float gLat  = units.GLattice(-9.81);

                    // Boussinesq β: a denser-than-air gas (Mw_gas > Mw_air) settles → negative
                    // buoyancy proxy. β maps a fixed "tracer T = 1.0" inside the source to a
                    // weight gradient roughly equal to (ρ_gas - ρ_air)/ρ_air. We use a small
                    // value here; the source concentration is normalised to 1.0 inside the sphere.
                    double mwAir = 28.96;
                    double mwGas = scenario.Sources != null && scenario.Sources.Count > 0 && scenario.Sources[0].Gas != null
                        ? scenario.Sources[0].Gas.MolarMass : 16.04;
                    float betaLat = (float)((mwGas - mwAir) / mwAir * 0.001);

                    // Molecular diffusivity ~ 1e-5 m²/s for typical gases.
                    float alphaLat = units.AlphaLattice(1e-5);

                    _handle = FluidX3DBridge.fx3d_create((uint)nx, (uint)ny, (uint)nz,
                        nuLat, 0f, 0f, gLat, alphaLat, betaLat);
                    if (_handle == 0UL)
                    {
                        Failed?.Invoke(this, "FluidX3D: fx3d_create failed");
                        return;
                    }

                    try
                    {
                        FluidX3DBridge.fx3d_set_z_boundaries(_handle);
                        FluidX3DBridge.fx3d_set_inlet_x(_handle,
                            units.ULattice(wind.X), units.ULattice(wind.Y), units.ULattice(wind.Z));
                        FluidX3DBridge.fx3d_set_outlet_x(_handle);

                        // Source: one cell-radius sphere at the first source position.
                        if (scenario.Sources != null && scenario.Sources.Count > 0)
                        {
                            var src = scenario.Sources[0];
                            var (cx, cy, cz) = units.SiToLattice(src.Position.X, src.Position.Y, src.Position.Z);
                            FluidX3DBridge.fx3d_set_source_sphere(_handle, cx, cy, cz, 2u, 1.0f);
                        }

                        // Pre-roll: let wind develop for ~1500 LBM steps before scoring concentration
                        Report(0.05, "FluidX3D: developing wind field...");
                        FluidX3DBridge.ProgressCallback prerollCb = (done, total) =>
                        {
                            Report(0.05 + 0.10 * done / total, "FluidX3D pre-roll " + done + "/" + total);
                            return _cancelled ? 1 : 0;
                        };
                        int rc = FluidX3DBridge.fx3d_run(_handle, 1500u, prerollCb);
                        if (rc != 0) { Failed?.Invoke(this, "FluidX3D pre-roll failed (" + rc + ")"); return; }

                        // Transient: chunks corresponding to writeInterval SI seconds each.
                        uint totalSteps = units.StepsForSeconds(duration);
                        uint stepsPerSnap = Math.Max(10u, units.StepsForSeconds(writeInterval));
                        int snapshots = (int)Math.Max(1, totalSteps / stepsPerSnap);

                        var result = new OpenFoamResult
                        {
                            GridNx = nx, GridNy = ny, GridNz = nz,
                            DomainSizeM = domain,
                            DomainXMin = -domain, DomainXMax = domain,
                            DomainYMin = -domain, DomainYMax = domain,
                            DomainZMax = height,
                            IsLoaded = true
                        };

                        var tBuf = new float[nx * ny * nz];

                        for (int snap = 0; snap < snapshots; snap++)
                        {
                            if (_cancelled) break;
                            int snapIndex = snap;
                            FluidX3DBridge.ProgressCallback cb = (done, total) =>
                            {
                                double frac = 0.15 + 0.80 * (snapIndex + (double)done / total) / snapshots;
                                Report(frac, "FluidX3D snap " + (snapIndex + 1) + "/" + snapshots);
                                return _cancelled ? 1 : 0;
                            };
                            rc = FluidX3DBridge.fx3d_run(_handle, stepsPerSnap, cb);
                            if (rc != 0) { Failed?.Invoke(this, "FluidX3D solver failed (" + rc + ")"); return; }

                            FluidX3DBridge.fx3d_read_temperature(_handle, tBuf);

                            var field = new double[nx, ny, nz];
                            for (int k = 0; k < nz; k++)
                            {
                                for (int j = 0; j < ny; j++)
                                {
                                    int rowBase = (k * ny + j) * nx;
                                    for (int i = 0; i < nx; i++) field[i, j, k] = tBuf[rowBase + i];
                                }
                            }

                            double tSi = (snap + 1) * writeInterval;
                            result.TimeSteps.Add(tSi);
                            result.PreloadField(tSi, field);
                        }

                        Report(0.99, "FluidX3D: completed");
                        Completed?.Invoke(this, result);
                    }
                    finally
                    {
                        try { FluidX3DBridge.fx3d_destroy(_handle); } catch { }
                        _handle = 0UL;
                    }
                }
                catch (Exception ex)
                {
                    Failed?.Invoke(this, ex.Message);
                }
            };
            _worker.RunWorkerAsync();
        }

        private void Report(double fraction, string step)
        {
            ProgressUpdated?.Invoke(this, new OpenFoamProgress
            {
                Fraction = fraction,
                Step = step,
                LogLine = step
            });
        }
    }
}
