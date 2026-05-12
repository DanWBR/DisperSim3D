// disp_bridge.h — C ABI exposed by FluidX3D.dll for DisperSim 3D.
// All coordinates are in LBM lattice cells (uint, in [0..N-1]); the C# side
// is responsible for SI <-> lattice unit conversion via FluidX3DUnits.
#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#ifdef _WIN32
#define FX3D_API __declspec(dllexport)
#else
#define FX3D_API
#endif

// Progress callback: called from inside fx3d_run between sub-runs. Returning
// non-zero from the callback asks the run to stop early (best-effort).
typedef int (*fx3d_progress_cb)(uint32_t steps_done, uint32_t steps_total);

// Create an LBM instance. Returns an opaque handle (>0 on success, 0 on failure).
// nu/alpha/beta are LBM lattice units. gx/gy/gz are lattice force per volume.
FX3D_API uint64_t fx3d_create(uint32_t Nx, uint32_t Ny, uint32_t Nz,
                              float nu, float gx, float gy, float gz,
                              float alpha, float beta);

// Mark every cell whose centre falls inside the AABB as TYPE_S (solid).
FX3D_API void fx3d_set_box_solid(uint64_t h,
                                 uint32_t xmin, uint32_t ymin, uint32_t zmin,
                                 uint32_t xmax, uint32_t ymax, uint32_t zmax);

// GPU-accelerated triangle-mesh voxelization. Each vertex array is laid out
// as [x0,y0,z0, x1,y1,z1, ...] in LATTICE coordinates (i.e. C# must convert
// SI -> lattice before calling). triangle_count entries from each array are
// consumed (so each array has 3*triangle_count floats). The mesh is voxelized
// to TYPE_S cells via FluidX3D's GPU raycasting kernel, which is orders of
// magnitude faster than per-triangle AABB voxelization on the CPU and produces
// far more accurate occupancy for curved surfaces (tanks, vessels, pipes).
FX3D_API void fx3d_voxelize_triangles(uint64_t h,
                                      const float* p0_xyz,
                                      const float* p1_xyz,
                                      const float* p2_xyz,
                                      uint32_t triangle_count);

// Mark inlet plane x=0 cells as TYPE_E with given velocity and density 1.0.
FX3D_API void fx3d_set_inlet_x(uint64_t h, float ux, float uy, float uz);

// Mark outlet plane x=Nx-1 cells as TYPE_E with zero velocity, density 1.0.
FX3D_API void fx3d_set_outlet_x(uint64_t h);

// Mark all four lateral faces (x=0, x=Nx-1, y=0, y=Ny-1) as TYPE_E with the
// given free-stream velocity and density 1.0. Use this for atmospheric wind
// fields where the direction is arbitrary — flow enters through whichever face
// faces upwind and exits the opposite side automatically.
FX3D_API void fx3d_set_lateral_free_stream(uint64_t h, float ux, float uy, float uz);

// Pre-populate every cell (regardless of flag) with the given velocity and
// density 1.0. Call AFTER the BC + obstacle setup but BEFORE run(); this gives
// the LBM a uniform initial condition and lets obstacles develop wake patterns
// in O(100) steps instead of waiting O(domain-crossings) for momentum to diffuse
// from the boundary.
FX3D_API void fx3d_initial_uniform(uint64_t h, float ux, float uy, float uz);

// Force-copy host-side flags, u, and rho buffers to the GPU. FluidX3D normally
// does this on the first lbm.run() call, but mixing custom setters with several
// extension flags (TEMPERATURE, EQUILIBRIUM_BOUNDARIES) has been observed to
// leave uninitialized device memory — calling this explicitly before run()
// guarantees the initialize kernel sees the values we set on the host.
FX3D_API void fx3d_commit_to_device(uint64_t h);

// Initialize every cell's temperature field to the given value. MUST be called
// before run() whenever TEMPERATURE is compiled in — leaving T=0 (the default
// host buffer state) makes the thermal LBM diverge instantly and clamps the
// velocity field at ±c_s. Use T=1.0 for ambient/inert runs; release-source
// cells get higher T via fx3d_set_source_sphere.
FX3D_API void fx3d_initial_temperature(uint64_t h, float t);

// Mark ground (z=0) cells as TYPE_S and top (z=Nz-1) as TYPE_E (open top).
FX3D_API void fx3d_set_z_boundaries(uint64_t h);

// Set a sphere of cells around (cx,cy,cz) with radius (cells) as TYPE_T fixed
// temperature — used as a release-source tracer when TEMPERATURE is enabled.
FX3D_API void fx3d_set_source_sphere(uint64_t h,
                                     uint32_t cx, uint32_t cy, uint32_t cz,
                                     uint32_t radius, float temperature);

// Advance the LBM by 'steps'. Calls progress_cb every chunk (~steps/20).
// Returns 0 on success, non-zero on cancel/error.
FX3D_API int fx3d_run(uint64_t h, uint32_t steps, fx3d_progress_cb cb);

// Copy velocity / temperature from device to host into caller-owned arrays
// of size Nx*Ny*Nz (index = x + Nx*(y + Ny*z)).
FX3D_API void fx3d_read_velocity(uint64_t h, float* ux, float* uy, float* uz);
FX3D_API void fx3d_read_temperature(uint64_t h, float* t);

// Destroy the LBM instance and release GPU memory.
FX3D_API void fx3d_destroy(uint64_t h);

#ifdef __cplusplus
} // extern "C"
#endif
