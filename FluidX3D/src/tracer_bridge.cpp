// tracer_bridge.cpp — GPU port of BuoyantTracerEngine.cs (phase 1).
//
// Compiles a small OpenCL program with the tracer kernels on a dedicated
// Device instance (sharing the same OpenCL platform / device as the wind
// field's LBM, but with its own program — so we don't have to graft new
// kernels onto FluidX3D's kernel.cpp).
//
// Phase 1 scope (one OpenCL kernel pipeline):
//   - frozen wind field (no buoyancy / no gravity-current spreading yet)
//   - forward semi-Lagrangian advection (no BFECC correction pass)
//   - obstacle masking after advection
//   - no diffusion / no decay / no source injection
//
// What that gives us: a verified end-to-end GPU pipeline (host → device →
// kernel dispatch → device → host) that we can extend kernel by kernel,
// validating each addition against the CPU baseline in
// benchmarks/baselines/buoyant-tracer-cpu-baseline-2026-05-16.md.

#define _CRT_SECURE_NO_WARNINGS
#include "disp_bridge.h"
#include "opencl.hpp"
#include <memory>
#include <unordered_map>
#include <mutex>
#include <cmath>
#include <cstring>

namespace {

// The OpenCL kernel source. R"OCL(...)OCL" is a C++11 raw string literal;
// the closing )OCL"; MUST stay at column 0 — anything else inside that
// block is kernel source verbatim.
static const char* TRACER_OPENCL_SRC = R"OCL(

// Forward semi-Lagrangian advection: for each cell n, trace backward
// along its effective velocity by dt and trilinearly sample the field
// at the departure point. Outside-of-domain samples return outside_val
// (0 for Y, ambient_T for T).
__kernel void tracer_advect_forward(
    __global const float* field_in,
    __global float* field_out,
    __global const float* vx,
    __global const float* vy,
    __global const float* vz,
    const uint Nx, const uint Ny, const uint Nz,
    const float dx, const float dy, const float dz,
    const float domain_half,
    const float dt,
    const float outside_val)
{
    const uint n = get_global_id(0);
    const uint NN = Nx*Ny*Nz;
    if(n >= NN) return;

    const uint k = n / (Nx*Ny);
    const uint j = (n - k*Nx*Ny) / Nx;
    const uint i = n - k*Nx*Ny - j*Nx;

    const float x = -domain_half + ((float)i + 0.5f) * dx;
    const float y = -domain_half + ((float)j + 0.5f) * dy;
    const float z = ((float)k + 0.5f) * dz;

    const float sx = x - vx[n] * dt;
    const float sy = y - vy[n] * dt;
    const float sz = z - vz[n] * dt;

    float fi = (sx + domain_half) / dx - 0.5f;
    float fj = (sy + domain_half) / dy - 0.5f;
    float fk = sz / dz - 0.5f;
    if(fk < 0.0f) fk = 0.0f;
    if(fi < 0.0f || fi > (float)(Nx-1u) || fj < 0.0f || fj > (float)(Ny-1u) || fk > (float)(Nz-1u)) {
        field_out[n] = outside_val;
        return;
    }
    int i0 = (int)floor(fi); if(i0 < 0) i0 = 0; if(i0 > (int)(Nx-2u)) i0 = (int)(Nx-2u);
    int j0 = (int)floor(fj); if(j0 < 0) j0 = 0; if(j0 > (int)(Ny-2u)) j0 = (int)(Ny-2u);
    int k0 = (int)floor(fk); if(k0 < 0) k0 = 0; if(k0 > (int)(Nz-2u)) k0 = (int)(Nz-2u);
    const float ax = fi - (float)i0;
    const float ay = fj - (float)j0;
    const float az = fk - (float)k0;
    const uint base = (uint)i0 + Nx*((uint)j0 + Ny*(uint)k0);
    const uint plane = Nx*Ny;
    const float c000 = field_in[base];
    const float c100 = field_in[base + 1u];
    const float c010 = field_in[base + Nx];
    const float c110 = field_in[base + Nx + 1u];
    const float c001 = field_in[base + plane];
    const float c101 = field_in[base + plane + 1u];
    const float c011 = field_in[base + plane + Nx];
    const float c111 = field_in[base + plane + Nx + 1u];
    const float c00 = c000*(1.0f-ax) + c100*ax;
    const float c10 = c010*(1.0f-ax) + c110*ax;
    const float c01 = c001*(1.0f-ax) + c101*ax;
    const float c11 = c011*(1.0f-ax) + c111*ax;
    const float c0 = c00*(1.0f-ay) + c10*ay;
    const float c1 = c01*(1.0f-ay) + c11*ay;
    field_out[n] = c0*(1.0f-az) + c1*az;
}

// Compute mixture density per cell.
//   M_mix  = 1 / (Y/M_gas + (1-Y)/M_air)
//   rho    = P / (R · T · (Y/M_gas + (1-Y)/M_air))
// For Y below a tiny threshold, falls back to pure-air density at the
// LOCAL temperature (not ambient — important once a precursor or other
// process has warmed/cooled the field).
__kernel void tracer_compute_density(
    __global const float* Y,
    __global const float* T,
    __global float* rho,
    const float gas_M,
    const float air_M,
    const float ambient_P,
    const float R_gas)
{
    const uint n = get_global_id(0);
    const float yy = Y[n];
    const float TT = fmax(T[n], 50.0f);
    if(yy > 1e-12f) {
        const float yc = fmin(yy, 1.0f);
        const float invMmix = yc / gas_M + (1.0f - yc) / air_M;
        rho[n] = ambient_P / (R_gas * TT * invMmix);
    } else {
        rho[n] = ambient_P * air_M / (R_gas * TT);
    }
}

// Effective transport velocity = wind + vertical buoyancy + horizontal
// gravity-current spreading (dense gas only), then clamped to ±vMax.
//
// Buoyancy:    v_buoy = g · (rho_air - rho_local) / rho_air     (+z = up)
// Gravity-current spreading (only when rho_local > rho_air):
//   g'      = g · (rho_local - rho_air) / rho_air
//   uScale  = Cgc · sqrt(g' · dz)
//   v_gc    = -uScale · ∇rho / |∇rho|                          (outward from dense core)
//
// rho_air is sampled at the LOCAL temperature using the ambient pressure
// so buoyancy isn't biased by global-vs-local T differences.
__kernel void tracer_compute_vEff(
    __global const float* ux,
    __global const float* uy,
    __global const float* uz,
    __global const float* rho,
    __global const float* T,
    __global float* vxEff,
    __global float* vyEff,
    __global float* vzEff,
    const uint Nx, const uint Ny, const uint Nz,
    const float dx, const float dy, const float dz,
    const float ambient_P, const float air_M, const float R_gas,
    const float gravity, const float Cgc,
    const float vMax)
{
    const uint n = get_global_id(0);
    const uint NN = Nx*Ny*Nz;
    if(n >= NN) return;

    const uint k = n / (Nx*Ny);
    const uint j = (n - k*Nx*Ny) / Nx;
    const uint i = n - k*Nx*Ny - j*Nx;

    const float TT = fmax(T[n], 50.0f);
    const float rho_air_local = ambient_P * air_M / (R_gas * TT);
    const float rho_local = rho[n];
    const float deltaRho = rho_local - rho_air_local;

    float vBuoy = 0.0f, vGcX = 0.0f, vGcY = 0.0f;
    if(fabs(deltaRho) > 1e-6f) {
        vBuoy = gravity * (rho_air_local - rho_local) / rho_air_local;

        if(deltaRho > 0.0f) {
            const float gPrime = gravity * deltaRho / rho_air_local;
            const float uScale = Cgc * sqrt(gPrime * dz);

            const uint im = (i > 0u) ? (i - 1u) : 0u;
            const uint ip = (i < Nx - 1u) ? (i + 1u) : (Nx - 1u);
            const uint jm = (j > 0u) ? (j - 1u) : 0u;
            const uint jp = (j < Ny - 1u) ? (j + 1u) : (Ny - 1u);
            const float spanX = (float)(ip - im) * dx;
            const float spanY = (float)(jp - jm) * dy;
            const float gradX = (spanX > 0.0f)
                ? (rho[ip + Nx*(j + Ny*k)] - rho[im + Nx*(j + Ny*k)]) / spanX : 0.0f;
            const float gradY = (spanY > 0.0f)
                ? (rho[i + Nx*(jp + Ny*k)] - rho[i + Nx*(jm + Ny*k)]) / spanY : 0.0f;
            const float gradMag = sqrt(gradX*gradX + gradY*gradY);
            if(gradMag > 1e-12f) {
                vGcX = -uScale * gradX / gradMag;
                vGcY = -uScale * gradY / gradMag;
            }
        }
    }

    vxEff[n] = clamp(ux[n] + vGcX,  -vMax, vMax);
    vyEff[n] = clamp(uy[n] + vGcY,  -vMax, vMax);
    vzEff[n] = clamp(uz[n] + vBuoy, -vMax, vMax);
}

// Zero out concentration / restore temperature inside obstacle cells.
__kernel void tracer_apply_obstacles(
    __global float* Y,
    __global float* T,
    __global const uchar* blocked,
    const float ambient_T)
{
    const uint n = get_global_id(0);
    if(blocked[n] != 0u) {
        Y[n] = 0.0f;
        T[n] = ambient_T;
    }
}

// Explicit Laplacian diffusion (single sub-step). Interior cells only —
// boundary cells (i/j/k on the domain edge) pass through unchanged, which
// matches the CPU engine's `Parallel.For(1, Nz - 1, ...)` loop.
// In-place is NOT safe (write order would race with neighbour reads), so
// caller must ping-pong via separate in/out buffers.
__kernel void tracer_diffuse_step(
    __global const float* f_in,
    __global float* f_out,
    const uint Nx, const uint Ny, const uint Nz,
    const float coeffX, const float coeffY, const float coeffZ)
{
    const uint n = get_global_id(0);
    const uint NN = Nx*Ny*Nz;
    if(n >= NN) return;
    const uint k = n / (Nx*Ny);
    const uint j = (n - k*Nx*Ny) / Nx;
    const uint i = n - k*Nx*Ny - j*Nx;
    if(i == 0u || i >= Nx - 1u || j == 0u || j >= Ny - 1u || k == 0u || k >= Nz - 1u) {
        f_out[n] = f_in[n];
        return;
    }
    const float c0 = f_in[n];
    const float lap =
        coeffX * (f_in[n + 1u] + f_in[n - 1u] - 2.0f * c0) +
        coeffY * (f_in[n + Nx] + f_in[n - Nx] - 2.0f * c0) +
        coeffZ * (f_in[n + Nx*Ny] + f_in[n - Nx*Ny] - 2.0f * c0);
    f_out[n] = c0 + lap;
}

// Per-step mass injection source. Handles both sphere and pool modes:
//   isPool = 0 → sphere: include cell if Δx²+Δy²+Δz² ≤ radius²
//   isPool = 1 → pool:   include cell if Δx²+Δy² ≤ radius² AND k ≤ poolMaxK
// For each included cell:
//   Y[n]   += dY                                 (mass-fraction injection)
//   T[n]    = ambient_T + min(Y[n], 1.0) · dT    (cold-jet for cryo, dT < 0)
// dY is computed on host as releaseRate·dt / (rho_air · cellVol · sourceCellCount)
// and dT = source_temperature − ambient_temperature.
__kernel void tracer_source_inject(
    __global float* Y,
    __global float* T,
    const uint Nx, const uint Ny, const uint Nz,
    const float dx, const float dy, const float dz,
    const float domain_half,
    const float sx, const float sy, const float sz,
    const float radius2,
    const int poolMaxK,
    const int isPool,
    const float dY,
    const float dT,
    const float ambient_T)
{
    const uint n = get_global_id(0);
    const uint NN = Nx*Ny*Nz;
    if(n >= NN) return;
    const uint k = n / (Nx*Ny);
    if(isPool != 0 && (int)k > poolMaxK) return;
    const uint j = (n - k*Nx*Ny) / Nx;
    const uint i = n - k*Nx*Ny - j*Nx;
    const float x = -domain_half + ((float)i + 0.5f) * dx;
    const float y = -domain_half + ((float)j + 0.5f) * dy;
    const float z = ((float)k + 0.5f) * dz;
    const float ddx = x - sx, ddy = y - sy, ddz = z - sz;
    const float r2 = (isPool != 0)
        ? (ddx*ddx + ddy*ddy)
        : (ddx*ddx + ddy*ddy + ddz*ddz);
    if(r2 > radius2) return;
    Y[n] += dY;
    const float yClamp = fmin(Y[n], 1.0f);
    T[n] = ambient_T + yClamp * dT;
}

// BFECC error correction. Inputs:
//   orig  — original field before any advection (snapshot at step start)
//   hat   — = AdvectReverse(AdvectForward(orig, dt), dt), the round-trip
//           estimate that should equal orig if advection were exact.
// Output:
//   corrected[n] = clamp_to_neighbour_range(1.5*orig[n] - 0.5*hat[n], orig)
// The clamping prevents the error-corrected field from overshooting the
// physical range present in the local neighbourhood of orig — without it
// BFECC introduces oscillatory artefacts near steep gradients.
// In-place safe when corrected == hat (we read hat[n] before writing).
__kernel void tracer_bfecc_correct(
    __global const float* orig,
    __global const float* hat,
    __global float* corrected,
    const uint Nx, const uint Ny, const uint Nz)
{
    const uint n = get_global_id(0);
    const uint NN = Nx*Ny*Nz;
    if(n >= NN) return;

    const uint k = n / (Nx*Ny);
    const uint j = (n - k*Nx*Ny) / Nx;
    const uint i = n - k*Nx*Ny - j*Nx;

    const float origN = orig[n];
    const float starVal = 1.5f * origN - 0.5f * hat[n];

    float lo = origN, hi = origN;
    if(i > 0u)      { float v = orig[n - 1u];      lo = fmin(lo, v); hi = fmax(hi, v); }
    if(i < Nx-1u)   { float v = orig[n + 1u];      lo = fmin(lo, v); hi = fmax(hi, v); }
    if(j > 0u)      { float v = orig[n - Nx];      lo = fmin(lo, v); hi = fmax(hi, v); }
    if(j < Ny-1u)   { float v = orig[n + Nx];      lo = fmin(lo, v); hi = fmax(hi, v); }
    if(k > 0u)      { float v = orig[n - Nx*Ny];   lo = fmin(lo, v); hi = fmax(hi, v); }
    if(k < Nz-1u)   { float v = orig[n + Nx*Ny];   lo = fmin(lo, v); hi = fmax(hi, v); }

    corrected[n] = clamp(starVal, lo, hi);
}

)OCL";

struct Tracer {
    // Geometry
    uint32_t Nx = 0, Ny = 0, Nz = 0;
    ulong N = 0;
    float domain_half_m = 0, domain_height_m = 0;
    float dx = 0, dy = 0, dz = 0;
    // Physics
    float gas_M = 0.029f;
    float ambient_T = 293.15f;
    float ambient_P = 101325.0f;
    float species_diff = 7.5e-3f;
    float thermal_diff = 2.2e-5f;
    // OpenCL — Device must outlive the Memory<T> / Kernel objects below.
    Device device;
    // Owned Memory buffers. Y0/Y1 (and T0/T1) ping-pong; the current and
    // next pointers (yCur etc.) below select which one is the "live" field
    // after each advection step.
    Memory<float> Y0, Y1, T0, T1;
    Memory<float> Yorig, Torig;          // BFECC snapshots (copy at step start)
    Memory<float> rho;                    // mixture density per cell
    Memory<float> ux, uy, uz;
    Memory<float> vxEff, vyEff, vzEff;
    Memory<uchar> blocked;
    // Ping-pong pointers (alias into Y0/Y1, T0/T1 — never own).
    Memory<float>* yCur = nullptr;
    Memory<float>* yNext = nullptr;
    Memory<float>* tCur = nullptr;
    Memory<float>* tNext = nullptr;
    // Kernels
    Kernel k_advect, k_velEff, k_obst, k_bfecc, k_density, k_diffuse, k_source;
    // Source params
    int src_kind = 0; // 0=none, 1=sphere, 2=pool
    float src_x = 0, src_y = 0, src_z = 0, src_r = 0;
    float src_rate_kgps = 0, src_air_rho = 1.0f, src_T_exit = 0;
    // Derived at set_source_* time:
    float src_inj_per_cell_per_s = 0.0f; // releaseRate / (rho_air · cellVol · #cells)
    int src_pool_max_k = 0;
};

struct TracerBridge {
    std::unordered_map<uint64_t, std::unique_ptr<Tracer>> tracers;
    std::mutex mtx;
    uint64_t next_id = 1ull;
};
inline TracerBridge& tbridge() { static TracerBridge b; return b; }

inline Tracer* resolve_tracer(uint64_t h) {
    auto& b = tbridge();
    std::lock_guard<std::mutex> lock(b.mtx);
    auto it = b.tracers.find(h);
    return it == b.tracers.end() ? nullptr : it->second.get();
}

} // namespace

extern "C" {

FX3D_API uint64_t fx3d_tracer_create(
    uint32_t Nx, uint32_t Ny, uint32_t Nz,
    float domain_half_m, float domain_height_m,
    float gas_molar_mass_kg_per_mol,
    float ambient_T_k, float ambient_P_pa,
    float species_diff_m2_per_s, float thermal_diff_m2_per_s,
    int32_t device_id)
{
    try {
        auto t = std::make_unique<Tracer>();
        t->Nx = Nx; t->Ny = Ny; t->Nz = Nz;
        t->N = (ulong)Nx * (ulong)Ny * (ulong)Nz;
        t->domain_half_m = domain_half_m;
        t->domain_height_m = domain_height_m;
        t->dx = (2.0f * domain_half_m) / (float)Nx;
        t->dy = (2.0f * domain_half_m) / (float)Ny;
        t->dz = domain_height_m / (float)Nz;
        t->gas_M = (gas_molar_mass_kg_per_mol > 0.0f) ? gas_molar_mass_kg_per_mol : 0.029f;
        t->ambient_T = (ambient_T_k > 0.0f) ? ambient_T_k : 293.15f;
        t->ambient_P = (ambient_P_pa > 0.0f) ? ambient_P_pa : 101325.0f;
        t->species_diff = species_diff_m2_per_s;
        t->thermal_diff = thermal_diff_m2_per_s;

        // Pick an OpenCL device and compile a fresh program with JUST our
        // tracer kernels (much faster than recompiling FluidX3D's main
        // kernel.cpp). The Device class accepts a custom opencl_c_code
        // string as its second constructor argument.
        const auto devices = get_devices(/*print_info=*/false);
        if(devices.empty()) return 0ull;
        const uint pick = (device_id < 0 || (uint)device_id >= devices.size())
            ? 0u : (uint)device_id;
        t->device = Device(devices[pick], std::string(TRACER_OPENCL_SRC));

        // Allocate device buffers.
        t->Y0    = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->Y1    = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->T0    = Memory<float>(t->device, t->N, 1u, true, true, t->ambient_T);
        t->T1    = Memory<float>(t->device, t->N, 1u, true, true, t->ambient_T);
        t->Yorig = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->Torig = Memory<float>(t->device, t->N, 1u, true, true, t->ambient_T);
        // Initial density = ambient air density (Y=0 everywhere at start).
        const float air_M_const = 0.029f;
        const float R_const = 8.314f;
        const float rho_amb = t->ambient_P * air_M_const / (R_const * t->ambient_T);
        t->rho = Memory<float>(t->device, t->N, 1u, true, true, rho_amb);
        t->ux    = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->uy    = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->uz    = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->vxEff = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->vyEff = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->vzEff = Memory<float>(t->device, t->N, 1u, true, true, 0.0f);
        t->blocked = Memory<uchar>(t->device, t->N, 1u, true, true, (uchar)0);
        t->yCur = &t->Y0; t->yNext = &t->Y1;
        t->tCur = &t->T0; t->tNext = &t->T1;

        // Bind kernels with placeholder constants — fx3d_tracer_step
        // overwrites them via set_parameters before each dispatch.
        const float zero_f = 0.0f;
        const uint Nxu = Nx, Nyu = Ny, Nzu = Nz;
        t->k_advect = Kernel(t->device, t->N, "tracer_advect_forward",
            *t->yCur, *t->yNext, t->vxEff, t->vyEff, t->vzEff,
            Nxu, Nyu, Nzu, t->dx, t->dy, t->dz, t->domain_half_m,
            zero_f, zero_f);
        t->k_velEff = Kernel(t->device, t->N, "tracer_compute_vEff",
            t->ux, t->uy, t->uz, t->rho, *t->tCur,
            t->vxEff, t->vyEff, t->vzEff,
            Nxu, Nyu, Nzu, t->dx, t->dy, t->dz,
            t->ambient_P, 0.029f, 8.314f, 9.81f, 0.5f, zero_f);
        t->k_obst = Kernel(t->device, t->N, "tracer_apply_obstacles",
            *t->yCur, *t->tCur, t->blocked, t->ambient_T);
        t->k_bfecc = Kernel(t->device, t->N, "tracer_bfecc_correct",
            t->Yorig, *t->yCur, *t->yCur, Nxu, Nyu, Nzu);
        t->k_density = Kernel(t->device, t->N, "tracer_compute_density",
            *t->yCur, *t->tCur, t->rho,
            t->gas_M, 0.029f, t->ambient_P, 8.314f);
        t->k_diffuse = Kernel(t->device, t->N, "tracer_diffuse_step",
            *t->yCur, *t->yNext, Nxu, Nyu, Nzu, zero_f, zero_f, zero_f);
        const int int_zero = 0;
        t->k_source = Kernel(t->device, t->N, "tracer_source_inject",
            *t->yCur, *t->tCur, Nxu, Nyu, Nzu, t->dx, t->dy, t->dz, t->domain_half_m,
            zero_f, zero_f, zero_f, zero_f, int_zero, int_zero, zero_f, zero_f, t->ambient_T);

        auto& b = tbridge();
        std::lock_guard<std::mutex> lock(b.mtx);
        uint64_t id = b.next_id++;
        b.tracers[id] = std::move(t);
        return id;
    } catch(...) {
        return 0ull;
    }
}

FX3D_API void fx3d_tracer_set_wind(uint64_t h,
    const float* ux, const float* uy, const float* uz)
{
    Tracer* t = resolve_tracer(h);
    if(!t || !ux || !uy || !uz) return;
    for(ulong n = 0; n < t->N; n++) {
        t->ux[n] = ux[n];
        t->uy[n] = uy[n];
        t->uz[n] = uz[n];
    }
    t->ux.write_to_device();
    t->uy.write_to_device();
    t->uz.write_to_device();
}

FX3D_API void fx3d_tracer_set_obstacles(uint64_t h, const uint8_t* blocked)
{
    Tracer* t = resolve_tracer(h);
    if(!t) return;
    if(blocked == nullptr) {
        for(ulong n = 0; n < t->N; n++) t->blocked[n] = (uchar)0;
    } else {
        for(ulong n = 0; n < t->N; n++) t->blocked[n] = (uchar)blocked[n];
    }
    t->blocked.write_to_device();
}

// Count source cells on host and derive injection rate per cell. This is
// a one-shot at set_source_* time — the kernel itself just gets the
// per-cell rate as a constant. Mirrors the CPU engine's
// SetMassSource / SetPoolSource arithmetic.
static void compute_source_rate(Tracer* t)
{
    const double cellVol = (double)t->dx * (double)t->dy * (double)t->dz;
    const double r2 = (double)t->src_r * (double)t->src_r;
    int count = 0;
    const bool isPool = (t->src_kind == 2);
    const int kMax = isPool ? t->src_pool_max_k : ((int)t->Nz - 1);
    for(int k = 0; k <= kMax; k++) {
        const double z = ((double)k + 0.5) * (double)t->dz;
        const double dz2 = isPool ? 0.0 : (z - (double)t->src_z) * (z - (double)t->src_z);
        if(!isPool && dz2 > r2) continue;
        for(int j = 0; j < (int)t->Ny; j++) {
            const double y = -(double)t->domain_half_m + ((double)j + 0.5) * (double)t->dy;
            const double dy2 = (y - (double)t->src_y) * (y - (double)t->src_y);
            if(dy2 + dz2 > r2) continue;
            for(int i = 0; i < (int)t->Nx; i++) {
                const double x = -(double)t->domain_half_m + ((double)i + 0.5) * (double)t->dx;
                const double dx2 = (x - (double)t->src_x) * (x - (double)t->src_x);
                if(dx2 + dy2 + (isPool ? 0.0 : dz2) <= r2) count++;
            }
        }
    }
    if(count < 1) count = 1;
    const double rho = (double)t->src_air_rho;
    t->src_inj_per_cell_per_s = (float)
        ((double)t->src_rate_kgps / (rho * cellVol * (double)count));
}

FX3D_API void fx3d_tracer_set_source_sphere(uint64_t h,
    float x_si, float y_si, float z_si,
    float radius_m,
    float release_rate_kg_per_s,
    float air_density_kg_per_m3,
    float exit_temperature_k)
{
    Tracer* t = resolve_tracer(h);
    if(!t) return;
    t->src_kind = 1;
    t->src_x = x_si; t->src_y = y_si; t->src_z = z_si;
    t->src_r = radius_m;
    t->src_rate_kgps = release_rate_kg_per_s;
    t->src_air_rho = air_density_kg_per_m3 > 0 ? air_density_kg_per_m3 : 1.2f;
    t->src_T_exit = exit_temperature_k;
    t->src_pool_max_k = 0;
    compute_source_rate(t);
}

FX3D_API void fx3d_tracer_set_source_pool(uint64_t h,
    float x_si, float y_si,
    float radius_m,
    float release_rate_kg_per_s,
    float air_density_kg_per_m3,
    float exit_temperature_k)
{
    Tracer* t = resolve_tracer(h);
    if(!t) return;
    t->src_kind = 2;
    t->src_x = x_si; t->src_y = y_si; t->src_z = 0;
    t->src_r = radius_m;
    t->src_rate_kgps = release_rate_kg_per_s;
    t->src_air_rho = air_density_kg_per_m3 > 0 ? air_density_kg_per_m3 : 1.2f;
    t->src_T_exit = exit_temperature_k;
    // CPU engine's SetPoolSource caps the vertical extent at the first
    // ground layer (`(int)Math.Ceiling(DzM / DzM)` = 1). Replicate exactly.
    t->src_pool_max_k = (t->Nz > 1) ? 1 : 0;
    compute_source_rate(t);
}

FX3D_API void fx3d_tracer_set_initial_concentration(uint64_t h, const float* Y)
{
    Tracer* t = resolve_tracer(h);
    if(!t || !Y) return;
    for(ulong n = 0; n < t->N; n++) (*t->yCur)[n] = Y[n];
    t->yCur->write_to_device();
}

// Helper: enqueue a device-to-device copy of N floats.
static void copy_buffer_floats(Tracer* t, const Memory<float>& src, Memory<float>& dst)
{
    const cl::CommandQueue& q = t->device.get_cl_queue();
    q.enqueueCopyBuffer(src.get_cl_buffer(), dst.get_cl_buffer(),
        0, 0, sizeof(float) * t->N);
}

// Explicit sub-stepped Laplacian diffusion. Per the CPU engine, the
// sub-step count is `ceil(2 · (Cx + Cy + Cz))` where Cα = D·dt/dα², which
// keeps each sub-step's effective coefficient below the explicit-Euler
// stability bound. Ping-pongs the relevant field's yCur/yNext (or
// tCur/tNext) pointers so the live field ends up in *Cur regardless of
// the parity of the sub-step count.
static void diffuse_field(Tracer* t, float D, float dt_s, bool isY)
{
    if(D <= 0.0f) return;
    float cx = D * dt_s / (t->dx * t->dx);
    float cy = D * dt_s / (t->dy * t->dy);
    float cz = D * dt_s / (t->dz * t->dz);
    int sub = (int)ceilf(2.0f * (cx + cy + cz));
    if(sub < 1) sub = 1;
    cx /= sub; cy /= sub; cz /= sub;
    Memory<float>** cur  = isY ? &t->yCur  : &t->tCur;
    Memory<float>** next = isY ? &t->yNext : &t->tNext;
    for(int s = 0; s < sub; s++) {
        t->k_diffuse.set_parameters(0u, **cur, **next,
            t->Nx, t->Ny, t->Nz, cx, cy, cz);
        t->k_diffuse.run();
        Memory<float>* tmp = *cur; *cur = *next; *next = tmp;
    }
}

// Run advect kernel with a custom (in, out) pair and dt sign. The
// existing tracer_advect_forward kernel does `sx = x - vx * dt`, so:
//   dt = +real_dt → trace BACKWARD in time (forward advection sample)
//   dt = -real_dt → trace FORWARD  in time (reverse advection sample)
static void run_advect(Tracer* t, Memory<float>& in, Memory<float>& out,
    float dt, float outside)
{
    t->k_advect.set_parameters(0u,
        in, out,
        t->vxEff, t->vyEff, t->vzEff,
        t->Nx, t->Ny, t->Nz, t->dx, t->dy, t->dz, t->domain_half_m,
        dt, outside);
    t->k_advect.run();
}

FX3D_API int fx3d_tracer_step(uint64_t h, float dt_s)
{
    Tracer* t = resolve_tracer(h);
    if(!t) return -1;

    const float vMax = 0.5f * fminf(t->dx, fminf(t->dy, t->dz)) / fmaxf(dt_s, 1e-6f);
    const float outside_Y = 0.0f;
    const float outside_T = t->ambient_T;

    // ---- Phase-2.3: density field from current Y, T ----
    t->k_density.set_parameters(0u, *t->yCur, *t->tCur, t->rho,
        t->gas_M, 0.029f, t->ambient_P, 8.314f);
    t->k_density.run();

    // ---- Phase-2.3: effective velocity = wind + buoyancy + gravity-current ----
    t->k_velEff.set_parameters(0u,
        t->ux, t->uy, t->uz, t->rho, *t->tCur,
        t->vxEff, t->vyEff, t->vzEff,
        t->Nx, t->Ny, t->Nz, t->dx, t->dy, t->dz,
        t->ambient_P, 0.029f, 8.314f, 9.81f, 0.5f, vMax);
    t->k_velEff.run();

    // ---- Phase-2.1: BFECC advection for Y and T ----
    // Snapshot the current state into the *orig buffers.
    copy_buffer_floats(t, *t->yCur, t->Yorig);
    copy_buffer_floats(t, *t->tCur, t->Torig);

    // Pass 1 — forward advect: orig → next  (dt = +dt_s)
    run_advect(t, *t->yCur,  *t->yNext, +dt_s, outside_Y);
    run_advect(t, *t->tCur,  *t->tNext, +dt_s, outside_T);

    // Pass 2 — reverse advect: next → cur   (dt = -dt_s)
    // Now cur holds the "hat" (round-trip estimate that should ≈ orig).
    run_advect(t, *t->yNext, *t->yCur,  -dt_s, outside_Y);
    run_advect(t, *t->tNext, *t->tCur,  -dt_s, outside_T);

    // Correction: cur := clamp_neighbour(1.5·orig - 0.5·hat, orig)
    // In-place safe (the kernel reads hat[n] before writing corrected[n],
    // and reads orig at neighbours from a different buffer).
    t->k_bfecc.set_parameters(0u, t->Yorig, *t->yCur, *t->yCur,
        t->Nx, t->Ny, t->Nz);
    t->k_bfecc.run();
    t->k_bfecc.set_parameters(0u, t->Torig, *t->tCur, *t->tCur,
        t->Nx, t->Ny, t->Nz);
    t->k_bfecc.run();

    // Pass 3 — forward advect the corrected field: cur → next
    run_advect(t, *t->yCur,  *t->yNext, +dt_s, outside_Y);
    run_advect(t, *t->tCur,  *t->tNext, +dt_s, outside_T);

    // Swap so yCur points at the final post-BFECC field.
    { Memory<float>* tmp = t->yCur; t->yCur = t->yNext; t->yNext = tmp; }
    { Memory<float>* tmp = t->tCur; t->tCur = t->tNext; t->tNext = tmp; }

    // ---- Phase-2.3: explicit sub-stepped Laplacian diffusion ----
    diffuse_field(t, t->species_diff, dt_s, /*isY=*/true);
    diffuse_field(t, t->thermal_diff, dt_s, /*isY=*/false);

    // ---- Phase-2.2: obstacle mask on the new current fields ----
    t->k_obst.set_parameters(0u, *t->yCur, *t->tCur, t->blocked, t->ambient_T);
    t->k_obst.run();

    // ---- Phase-2.4: mass injection source (sphere / pool) ----
    if(t->src_kind != 0 && t->src_inj_per_cell_per_s > 0.0f) {
        const float dY = t->src_inj_per_cell_per_s * dt_s;
        const float dT = t->src_T_exit - t->ambient_T;
        const float radius2 = t->src_r * t->src_r;
        const int isPool = (t->src_kind == 2) ? 1 : 0;
        t->k_source.set_parameters(0u,
            *t->yCur, *t->tCur,
            t->Nx, t->Ny, t->Nz, t->dx, t->dy, t->dz, t->domain_half_m,
            t->src_x, t->src_y, t->src_z, radius2,
            t->src_pool_max_k, isPool,
            dY, dT, t->ambient_T);
        t->k_source.run();
    }

    return 0;
}

FX3D_API void fx3d_tracer_read_concentration(uint64_t h, float* out_Y)
{
    Tracer* t = resolve_tracer(h);
    if(!t || !out_Y) return;
    t->yCur->read_from_device();
    for(ulong n = 0; n < t->N; n++) out_Y[n] = (*t->yCur)[n];
}

FX3D_API void fx3d_tracer_read_temperature(uint64_t h, float* out_T)
{
    Tracer* t = resolve_tracer(h);
    if(!t || !out_T) return;
    t->tCur->read_from_device();
    for(ulong n = 0; n < t->N; n++) out_T[n] = (*t->tCur)[n];
}

FX3D_API void fx3d_tracer_destroy(uint64_t h)
{
    auto& b = tbridge();
    std::lock_guard<std::mutex> lock(b.mtx);
    b.tracers.erase(h);
}

} // extern "C"
