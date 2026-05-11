using System;
using System.Runtime.InteropServices;

namespace DisperSim3D.Core
{
    /// <summary>
    /// P/Invoke layer for FluidX3D.dll. All coordinates are lattice cells (uint, [0..N-1]);
    /// callers do SI &lt;-&gt; lattice conversion via <see cref="FluidX3DUnits"/>.
    /// Layout matches the C ABI defined in <c>FluidX3D/src/disp_bridge.h</c>.
    /// </summary>
    internal static class FluidX3DBridge
    {
        private const string Dll = "FluidX3D.dll";

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

        /// <summary>Probes whether FluidX3D.dll is loadable in the current process.</summary>
        public static bool IsAvailable()
        {
            try
            {
                // Tiny instance just to verify the DLL + OpenCL device exists. Destroyed immediately.
                ulong h = fx3d_create(8u, 8u, 8u, 0.1f, 0f, 0f, 0f, 0f, 0f);
                if (h == 0UL) return false;
                fx3d_destroy(h);
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
            catch { return false; }
        }
    }
}
