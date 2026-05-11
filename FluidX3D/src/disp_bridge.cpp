// disp_bridge.cpp — C ABI implementation. Forwards to FluidX3D's LBM class.
//
// Handle table: a static map<uint64_t, unique_ptr<LBM>> indexed by a
// monotonically increasing counter. Each call resolves the handle to its
// LBM instance, then sets per-cell flags / u / T, runs the solver, or reads
// fields back.
#include "disp_bridge.h"
#include "lbm.hpp"
#include <memory>
#include <unordered_map>
#include <mutex>

namespace {
    struct Bridge {
        std::unordered_map<uint64_t, std::unique_ptr<LBM>> sims;
        std::mutex mtx;
        uint64_t next_id = 1ULL;
    };
    Bridge& bridge() { static Bridge b; return b; }

    LBM* resolve(uint64_t h) {
        auto& b = bridge();
        std::lock_guard<std::mutex> lock(b.mtx);
        auto it = b.sims.find(h);
        return it == b.sims.end() ? nullptr : it->second.get();
    }
}

extern "C" {

FX3D_API uint64_t fx3d_create(uint32_t Nx, uint32_t Ny, uint32_t Nz,
                              float nu, float gx, float gy, float gz,
                              float alpha, float beta) {
    try {
        // sigma (surface tension) = 0; particles_N = 0.
        auto lbm = std::make_unique<LBM>(Nx, Ny, Nz, nu, gx, gy, gz, 0.0f, alpha, beta);
        auto& b = bridge();
        std::lock_guard<std::mutex> lock(b.mtx);
        uint64_t id = b.next_id++;
        b.sims[id] = std::move(lbm);
        return id;
    } catch (...) {
        return 0ULL;
    }
}

FX3D_API void fx3d_set_box_solid(uint64_t h,
                                 uint32_t xmin, uint32_t ymin, uint32_t zmin,
                                 uint32_t xmax, uint32_t ymax, uint32_t zmax) {
    LBM* lbm = resolve(h); if (!lbm) return;
    const uint32_t Nx = lbm->get_Nx(), Ny = lbm->get_Ny(), Nz = lbm->get_Nz();
    if (xmax >= Nx) xmax = Nx - 1u;
    if (ymax >= Ny) ymax = Ny - 1u;
    if (zmax >= Nz) zmax = Nz - 1u;
    for (uint32_t z = zmin; z <= zmax; z++)
        for (uint32_t y = ymin; y <= ymax; y++)
            for (uint32_t x = xmin; x <= xmax; x++)
                lbm->flags[lbm->index(x, y, z)] = TYPE_S;
}

FX3D_API void fx3d_set_inlet_x(uint64_t h, float ux, float uy, float uz) {
    LBM* lbm = resolve(h); if (!lbm) return;
    const uint32_t Ny = lbm->get_Ny(), Nz = lbm->get_Nz();
    for (uint32_t z = 0u; z < Nz; z++) {
        for (uint32_t y = 0u; y < Ny; y++) {
            const ulong n = lbm->index(0u, y, z);
            lbm->flags[n] = TYPE_E;
            lbm->u.x[n] = ux;
            lbm->u.y[n] = uy;
            lbm->u.z[n] = uz;
            lbm->rho[n] = 1.0f;
        }
    }
}

FX3D_API void fx3d_set_outlet_x(uint64_t h) {
    LBM* lbm = resolve(h); if (!lbm) return;
    const uint32_t Nx = lbm->get_Nx(), Ny = lbm->get_Ny(), Nz = lbm->get_Nz();
    for (uint32_t z = 0u; z < Nz; z++) {
        for (uint32_t y = 0u; y < Ny; y++) {
            const ulong n = lbm->index(Nx - 1u, y, z);
            lbm->flags[n] = TYPE_E;
            lbm->u.x[n] = 0.0f;
            lbm->u.y[n] = 0.0f;
            lbm->u.z[n] = 0.0f;
            lbm->rho[n] = 1.0f;
        }
    }
}

FX3D_API void fx3d_set_z_boundaries(uint64_t h) {
    LBM* lbm = resolve(h); if (!lbm) return;
    const uint32_t Nx = lbm->get_Nx(), Ny = lbm->get_Ny(), Nz = lbm->get_Nz();
    // Ground: TYPE_S (no-slip wall)
    for (uint32_t y = 0u; y < Ny; y++)
        for (uint32_t x = 0u; x < Nx; x++)
            lbm->flags[lbm->index(x, y, 0u)] = TYPE_S;
    // Top: TYPE_E (open boundary, free-stream)
    for (uint32_t y = 0u; y < Ny; y++) {
        for (uint32_t x = 0u; x < Nx; x++) {
            const ulong n = lbm->index(x, y, Nz - 1u);
            lbm->flags[n] = TYPE_E;
            lbm->rho[n] = 1.0f;
        }
    }
}

FX3D_API void fx3d_set_source_sphere(uint64_t h,
                                     uint32_t cx, uint32_t cy, uint32_t cz,
                                     uint32_t radius, float temperature) {
    LBM* lbm = resolve(h); if (!lbm) return;
#ifdef TEMPERATURE
    const uint32_t Nx = lbm->get_Nx(), Ny = lbm->get_Ny(), Nz = lbm->get_Nz();
    const int r = (int)radius;
    const int icx = (int)cx, icy = (int)cy, icz = (int)cz;
    for (int dz = -r; dz <= r; dz++) {
        const int z = icz + dz; if (z < 0 || z >= (int)Nz) continue;
        for (int dy = -r; dy <= r; dy++) {
            const int y = icy + dy; if (y < 0 || y >= (int)Ny) continue;
            for (int dx = -r; dx <= r; dx++) {
                const int x = icx + dx; if (x < 0 || x >= (int)Nx) continue;
                if (dx*dx + dy*dy + dz*dz > r*r) continue;
                const ulong n = lbm->index((uint)x, (uint)y, (uint)z);
                lbm->flags[n] = TYPE_T;
                lbm->T[n] = temperature;
            }
        }
    }
#else
    (void)cx; (void)cy; (void)cz; (void)radius; (void)temperature;
#endif
}

FX3D_API int fx3d_run(uint64_t h, uint32_t steps, fx3d_progress_cb cb) {
    LBM* lbm = resolve(h); if (!lbm) return -1;
    if (steps == 0u) return 0;
    const uint32_t chunks = 20u;
    const uint32_t chunk = (steps + chunks - 1u) / chunks;
    uint32_t done = 0u;
    while (done < steps) {
        const uint32_t this_chunk = (steps - done < chunk) ? (steps - done) : chunk;
        try {
            lbm->run((ulong)this_chunk);
        } catch (...) {
            return -2;
        }
        done += this_chunk;
        if (cb) {
            if (cb(done, steps) != 0) return 1; // user cancel
        }
    }
    return 0;
}

FX3D_API void fx3d_read_velocity(uint64_t h, float* ux, float* uy, float* uz) {
    LBM* lbm = resolve(h); if (!lbm || !ux || !uy || !uz) return;
    lbm->u.read_from_device();
    const ulong N = lbm->get_N();
    for (ulong n = 0; n < N; n++) {
        ux[n] = lbm->u.x[n];
        uy[n] = lbm->u.y[n];
        uz[n] = lbm->u.z[n];
    }
}

FX3D_API void fx3d_read_temperature(uint64_t h, float* t) {
    LBM* lbm = resolve(h); if (!lbm || !t) return;
#ifdef TEMPERATURE
    lbm->T.read_from_device();
    const ulong N = lbm->get_N();
    for (ulong n = 0; n < N; n++) t[n] = lbm->T[n];
#else
    const ulong N = lbm->get_N();
    for (ulong n = 0; n < N; n++) t[n] = 0.0f;
#endif
}

FX3D_API void fx3d_destroy(uint64_t h) {
    auto& b = bridge();
    std::lock_guard<std::mutex> lock(b.mtx);
    b.sims.erase(h);
}

} // extern "C"
