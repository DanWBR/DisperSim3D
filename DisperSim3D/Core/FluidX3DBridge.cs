using System;
using System.Runtime.InteropServices;

namespace DisperSim3D.Core
{
    /// <summary>
    /// P/Invoke layer for FluidX3D.dll. All coordinates are lattice cells (uint, [0..N-1]);
    /// callers do SI &lt;-&gt; lattice conversion via <see cref="FluidX3DUnits"/>.
    /// Layout matches the C ABI defined in <c>FluidX3D/src/disp_bridge.h</c>.
    /// </summary>
    public static class FluidX3DBridge
    {
        // No extension — lets .NET pick the platform-correct suffix and prefix:
        // Windows → FluidX3D.dll, Linux → libFluidX3D.so, macOS → libFluidX3D.dylib.
        // (With the explicit ".dll" suffix .NET on Linux only tries
        // FluidX3D.dll, libFluidX3D.dll, FluidX3D.dll.so, libFluidX3D.dll.so —
        // it never strips the ".dll" and so misses the obvious libFluidX3D.so.)
        private const string Dll = "FluidX3D";

        /// <summary>Returning non-zero from the callback asks the run to stop early.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int ProgressCallback(uint stepsDone, uint stepsTotal);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong fx3d_create(uint Nx, uint Ny, uint Nz,
            float nu, float gx, float gy, float gz, float alpha, float beta);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_set_box_solid(ulong h,
            uint xmin, uint ymin, uint zmin,
            uint xmax, uint ymax, uint zmax);

        /// <summary>Creates an LBM with explicit OpenCL device selection.
        /// device_id &lt; 0 = auto (fastest). device_id ≥ 0 picks the matching
        /// entry from <see cref="ListDevicesJson"/>.</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong fx3d_create_on_device(
            uint Nx, uint Ny, uint Nz,
            float nu, float gx, float gy, float gz,
            float alpha, float beta,
            int device_id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint fx3d_list_devices(
            [Out] byte[] buf, uint max_bytes);

        /// <summary>Last error from <see cref="ListDevicesJson"/> — populated when the
        /// call can't reach <c>fx3d_list_devices</c> (most often EntryPointNotFoundException
        /// because the running process loaded an older FluidX3D.dll that doesn't export
        /// the function — close the app and relaunch to pick up the new DLL).</summary>
        public static string LastListDevicesError { get; private set; } = "";

        /// <summary>Returns a JSON array describing every OpenCL device on this
        /// machine (id, name, vendor, memory_mb, tflops, compute_units, clock_mhz,
        /// is_gpu). Empty string on failure; check <see cref="LastListDevicesError"/>
        /// for diagnostic text.</summary>
        public static string ListDevicesJson()
        {
            LastListDevicesError = "";
            if (!IsAvailable())
            {
                // Forward the specific reason rather than the generic
                // "DllNotFound / no OpenCL device" blanket message — distinguishes
                // missing .so / .dll vs no OpenCL ICD installed vs arch mismatch.
                LastListDevicesError = string.IsNullOrEmpty(LastAvailabilityError)
                    ? "FluidX3D not available (unknown reason)."
                    : LastAvailabilityError;
                return "";
            }
            // Two-call protocol: first call with small buffer to size; second to fetch.
            var probe = new byte[2];
            uint required = 0;
            try { required = fx3d_list_devices(probe, (uint)probe.Length); }
            catch (EntryPointNotFoundException)
            {
                LastListDevicesError =
                    "fx3d_list_devices not exported by the loaded FluidX3D.dll. " +
                    "The process is still holding an older copy in memory — close DisperSim3D " +
                    "and relaunch so the new DLL gets loaded.";
                return "";
            }
            catch (Exception ex)
            {
                LastListDevicesError = ex.GetType().Name + ": " + ex.Message;
                return "";
            }
            if (required == 0)
            {
                LastListDevicesError = "fx3d_list_devices returned 0 (see %TEMP%/fluidx3d_bridge.log).";
                return "";
            }
            int cap = (int)Math.Min(required + 16u, 1_000_000u);
            var buf = new byte[cap];
            try { fx3d_list_devices(buf, (uint)buf.Length); }
            catch (Exception ex)
            {
                LastListDevicesError = "Second call: " + ex.GetType().Name + " " + ex.Message;
                return "";
            }
            int n = Array.IndexOf<byte>(buf, 0);
            if (n < 0) n = buf.Length;
            return System.Text.Encoding.ASCII.GetString(buf, 0, n);
        }

        /// <summary>GPU-accelerated triangle-mesh voxelization. Each array is laid out
        /// as [x0,y0,z0, x1,y1,z1, ...] in LATTICE coordinates and has length
        /// 3*triangleCount. FluidX3D's raycasting kernel is ~100× faster than the
        /// CPU per-triangle AABB approach and produces accurate curved-surface fits
        /// for tanks/vessels/pipes.</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_voxelize_triangles(ulong h,
            [In] float[] p0_xyz,
            [In] float[] p1_xyz,
            [In] float[] p2_xyz,
            uint triangleCount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_set_inlet_x(ulong h, float ux, float uy, float uz);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_set_outlet_x(ulong h);

        /// <summary>Free-stream boundary on all four lateral faces — preferred for
        /// atmospheric wind fields where the wind direction can be arbitrary.</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_set_lateral_free_stream(ulong h, float ux, float uy, float uz);

        /// <summary>Pre-fill every non-solid cell with a uniform velocity — start the LBM
        /// from a free-stream state instead of zero, so wakes around obstacles develop quickly.</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_initial_uniform(ulong h, float ux, float uy, float uz);

        /// <summary>Force-flush host buffers (flags, u, rho, T) to the GPU before run().
        /// FluidX3D auto-transfers on first run(), but on Windows with TEMPERATURE +
        /// EQUILIBRIUM_BOUNDARIES we've observed the device memory remaining uninitialized
        /// in some cases, leading to LBM blow-up that pegs all velocities at ±c_s.</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_commit_to_device(ulong h);

        /// <summary>Initialize every cell's T field to <paramref name="t"/>. MUST be
        /// called before run() when TEMPERATURE is enabled in the DLL — default T=0
        /// makes the thermal LBM blow up. Use 1.0f for ambient runs.</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_initial_temperature(ulong h, float t);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_set_z_boundaries(ulong h);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_set_source_sphere(ulong h,
            uint cx, uint cy, uint cz, uint radius, float temperature);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int fx3d_run(ulong h, uint steps, ProgressCallback cb);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_read_velocity(ulong h,
            [Out] float[] ux, [Out] float[] uy, [Out] float[] uz);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_read_temperature(ulong h, [Out] float[] t);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void fx3d_destroy(ulong h);

        /// <summary>Last reason <see cref="IsAvailable"/> returned <c>false</c>.
        /// Populated with one of: "" (available), "DllNotFound: <message>",
        /// "BadImageFormat: <message>", "fx3d_create returned 0 (no OpenCL device)",
        /// or "<ExceptionType>: <message>".</summary>
        public static string LastAvailabilityError { get; private set; } = "";

        /// <summary>Probes whether FluidX3D.dll is loadable in the current process
        /// AND at least one OpenCL device is reachable. On failure, the specific
        /// reason is surfaced via <see cref="LastAvailabilityError"/> for diagnostics.</summary>
        public static bool IsAvailable()
        {
            try
            {
                // Tiny instance just to verify the DLL + OpenCL device exists. Destroyed immediately.
                ulong h = fx3d_create(8u, 8u, 8u, 0.1f, 0f, 0f, 0f, 0f, 0f);
                if (h == 0UL)
                {
                    LastAvailabilityError = "fx3d_create returned 0 (FluidX3D library loaded, but no OpenCL device available — install your GPU vendor's ICD).";
                    return false;
                }
                fx3d_destroy(h);
                LastAvailabilityError = "";
                return true;
            }
            catch (DllNotFoundException ex)
            {
                LastAvailabilityError = "DllNotFound: " + ex.Message +
                    " (the native FluidX3D library was not found next to the .NET binary; on Linux it must be named libFluidX3D.so, on macOS libFluidX3D.dylib).";
                return false;
            }
            catch (BadImageFormatException ex)
            {
                LastAvailabilityError = "BadImageFormat: " + ex.Message +
                    " (architecture mismatch between the .NET host and the native library — rebuild the native library for the same arch as the .NET runtime).";
                return false;
            }
            catch (Exception ex)
            {
                LastAvailabilityError = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
