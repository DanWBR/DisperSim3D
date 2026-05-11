// disp_bridge.cpp — C ABI implementation. Forwards to FluidX3D's LBM class.
//
// Handle table: a static map<uint64_t, unique_ptr<LBM>> indexed by a
// monotonically increasing counter. Each call resolves the handle to its
// LBM instance, then sets per-cell flags / u / T, runs the solver, or reads
// fields back.
// MSVC flags std::getenv as "unsafe"; we only use it to find %TEMP%, so silence it.
#define _CRT_SECURE_NO_WARNINGS

#include "disp_bridge.h"
#include "lbm.hpp"
#include <memory>
#include <unordered_map>
#include <mutex>
#include <fstream>
#include <cstdlib>
#include <chrono>
#include <iomanip>

namespace {
    struct Bridge {
        std::unordered_map<uint64_t, std::unique_ptr<LBM>> sims;
        std::mutex mtx;
        uint64_t next_id = 1ULL;
        std::ofstream log;
    };
    Bridge& bridge() { static Bridge b; return b; }

    void log_open_if_needed() {
        auto& b = bridge();
        if (b.log.is_open()) return;
        // Resolve %TEMP%; fall back to current dir on failure.
        const char* tmp = std::getenv("TEMP");
        if (!tmp) tmp = std::getenv("TMP");
        std::string path = (tmp ? std::string(tmp) : std::string(".")) + "\\fluidx3d_bridge.log";
        b.log.open(path, std::ios::out | std::ios::trunc);
        if (b.log.is_open()) {
            b.log << "=== FluidX3D bridge log opened ===\n";
            b.log.flush();
        }
    }

    void log_line(const std::string& s) {
        auto& b = bridge();
        log_open_if_needed();
        if (!b.log.is_open()) return;
        auto now = std::chrono::system_clock::now();
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()).count() % 1000;
        std::time_t t = std::chrono::system_clock::to_time_t(now);
        std::tm tm; localtime_s(&tm, &t);
        b.log << std::put_time(&tm, "%H:%M:%S") << "."
              << std::setw(3) << std::setfill('0') << ms << "  " << s << "\n";
        b.log.flush();
    }

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
    {
        char buf[256];
        std::snprintf(buf, sizeof(buf),
            "fx3d_create  N=(%u,%u,%u) nu=%.6g g=(%.4g,%.4g,%.4g) alpha=%.4g beta=%.4g  tau=%.4f",
            Nx, Ny, Nz, nu, gx, gy, gz, alpha, beta, 3.0f * nu + 0.5f);
        log_line(buf);
    }
    try {
        auto lbm = std::make_unique<LBM>(Nx, Ny, Nz, nu, gx, gy, gz, 0.0f, alpha, beta);
        auto& b = bridge();
        std::lock_guard<std::mutex> lock(b.mtx);
        uint64_t id = b.next_id++;
        b.sims[id] = std::move(lbm);
        log_line("  -> handle=" + std::to_string(id));
        return id;
    } catch (const std::exception& ex) {
        log_line(std::string("  -> EXCEPTION: ") + ex.what());
        return 0ULL;
    } catch (...) {
        log_line("  -> UNKNOWN EXCEPTION");
        return 0ULL;
    }
}

FX3D_API void fx3d_set_box_solid(uint64_t h,
                                 uint32_t xmin, uint32_t ymin, uint32_t zmin,
                                 uint32_t xmax, uint32_t ymax, uint32_t zmax) {
    {
        char buf[200];
        std::snprintf(buf, sizeof(buf), "fx3d_set_box_solid h=%llu (%u,%u,%u)..(%u,%u,%u)",
            (unsigned long long)h, xmin, ymin, zmin, xmax, ymax, zmax);
        log_line(buf);
    }
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

FX3D_API void fx3d_set_lateral_free_stream(uint64_t h, float ux, float uy, float uz) {
    {
        char buf[160];
        std::snprintf(buf, sizeof(buf), "fx3d_set_lateral_free_stream h=%llu  u=(%.6g,%.6g,%.6g)",
            (unsigned long long)h, ux, uy, uz);
        log_line(buf);
    }
    LBM* lbm = resolve(h); if (!lbm) return;
    const uint32_t Nx = lbm->get_Nx(), Ny = lbm->get_Ny(), Nz = lbm->get_Nz();
    auto setCell = [&](uint32_t x, uint32_t y, uint32_t z) {
        const ulong n = lbm->index(x, y, z);
        lbm->flags[n] = TYPE_E;
        lbm->u.x[n] = ux;
        lbm->u.y[n] = uy;
        lbm->u.z[n] = uz;
        lbm->rho[n] = 1.0f;
    };
    // X-min and X-max faces (full Z range — overrides ground cells too; caller should
    // re-apply ground TYPE_S afterwards if needed).
    for (uint32_t z = 0u; z < Nz; z++)
        for (uint32_t y = 0u; y < Ny; y++) {
            setCell(0u, y, z);
            setCell(Nx - 1u, y, z);
        }
    // Y-min and Y-max faces (skip corners already set above).
    for (uint32_t z = 0u; z < Nz; z++)
        for (uint32_t x = 1u; x < Nx - 1u; x++) {
            setCell(x, 0u, z);
            setCell(x, Ny - 1u, z);
        }
    // Top z=Nz-1 — free-stream cap. Without this the top is TYPE_E with u=0, which
    // acts as a rigid ceiling and creates spurious recirculation in the interior.
    for (uint32_t y = 1u; y < Ny - 1u; y++)
        for (uint32_t x = 1u; x < Nx - 1u; x++)
            setCell(x, y, Nz - 1u);
}

FX3D_API void fx3d_set_z_boundaries(uint64_t h) {
    log_line("fx3d_set_z_boundaries h=" + std::to_string(h));
    LBM* lbm = resolve(h); if (!lbm) return;
    const uint32_t Nx = lbm->get_Nx(), Ny = lbm->get_Ny(), Nz = lbm->get_Nz();
    // Ground: TYPE_S (no-slip wall)
    for (uint32_t y = 0u; y < Ny; y++)
        for (uint32_t x = 0u; x < Nx; x++)
            lbm->flags[lbm->index(x, y, 0u)] = TYPE_S;
    // Top: TYPE_E with whatever velocity is already on the cell — caller should set
    // the lateral free-stream velocity AFTER calling this, which will overwrite the
    // ring at z=Nz-1 with the correct values. We just clear flags + density here.
    for (uint32_t y = 0u; y < Ny; y++) {
        for (uint32_t x = 0u; x < Nx; x++) {
            const ulong n = lbm->index(x, y, Nz - 1u);
            lbm->flags[n] = TYPE_E;
            lbm->rho[n] = 1.0f;
        }
    }
}

FX3D_API void fx3d_initial_uniform(uint64_t h, float ux, float uy, float uz) {
    {
        char buf[160];
        std::snprintf(buf, sizeof(buf), "fx3d_initial_uniform h=%llu  u=(%.6g,%.6g,%.6g)",
            (unsigned long long)h, ux, uy, uz);
        log_line(buf);
    }
    LBM* lbm = resolve(h); if (!lbm) return;
    const ulong N = lbm->get_N();
    for (ulong n = 0; n < N; n++) {
        // Solid cells keep their flag but get u=0 (no-slip).
        if (lbm->flags[n] == TYPE_S) {
            lbm->u.x[n] = 0.0f;
            lbm->u.y[n] = 0.0f;
            lbm->u.z[n] = 0.0f;
            lbm->rho[n] = 1.0f;
        } else {
            lbm->u.x[n] = ux;
            lbm->u.y[n] = uy;
            lbm->u.z[n] = uz;
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
    log_line("fx3d_run h=" + std::to_string(h) + "  steps=" + std::to_string(steps));
    LBM* lbm = resolve(h); if (!lbm) { log_line("  -> bad handle"); return -1; }
    if (steps == 0u) return 0;
    const auto start = std::chrono::steady_clock::now();
    const uint32_t chunks = 20u;
    const uint32_t chunk = (steps + chunks - 1u) / chunks;
    uint32_t done = 0u;
    while (done < steps) {
        const uint32_t this_chunk = (steps - done < chunk) ? (steps - done) : chunk;
        try {
            lbm->run((ulong)this_chunk);
        } catch (const std::exception& ex) {
            log_line(std::string("  -> EXCEPTION in lbm->run: ") + ex.what());
            return -2;
        } catch (...) {
            log_line("  -> UNKNOWN EXCEPTION in lbm->run");
            return -2;
        }
        done += this_chunk;
        if (cb) {
            if (cb(done, steps) != 0) {
                log_line("  -> cancelled at " + std::to_string(done) + "/" + std::to_string(steps));
                return 1;
            }
        }
    }
    auto elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
    char buf[120];
    std::snprintf(buf, sizeof(buf), "  -> ok, %u steps in %.3fs (%.1f steps/s)",
        steps, elapsed, steps / std::max(elapsed, 1e-6));
    log_line(buf);
    return 0;
}

FX3D_API void fx3d_read_velocity(uint64_t h, float* ux, float* uy, float* uz) {
    log_line("fx3d_read_velocity h=" + std::to_string(h));
    LBM* lbm = resolve(h); if (!lbm || !ux || !uy || !uz) { log_line("  -> bad args"); return; }
    lbm->u.read_from_device();
    const ulong N = lbm->get_N();
    float minX=1e9f, maxX=-1e9f, minY=1e9f, maxY=-1e9f, minZ=1e9f, maxZ=-1e9f;
    double sumX=0, sumY=0, sumZ=0, sumMag=0;
    ulong nonZero = 0;
    for (ulong n = 0; n < N; n++) {
        float fx = lbm->u.x[n], fy = lbm->u.y[n], fz = lbm->u.z[n];
        ux[n] = fx; uy[n] = fy; uz[n] = fz;
        if (fx < minX) minX = fx; if (fx > maxX) maxX = fx;
        if (fy < minY) minY = fy; if (fy > maxY) maxY = fy;
        if (fz < minZ) minZ = fz; if (fz > maxZ) maxZ = fz;
        sumX += fx; sumY += fy; sumZ += fz;
        float mag = std::sqrt(fx*fx + fy*fy + fz*fz);
        sumMag += mag;
        if (mag > 1e-8f) nonZero++;
    }
    double inv = 1.0 / (double)N;
    char buf[400];
    std::snprintf(buf, sizeof(buf),
        "  N=%llu nonZero=%llu (%.1f%%)  u.x[min,mean,max]=[%.4g,%.4g,%.4g]  "
        "u.y=[%.4g,%.4g,%.4g]  u.z=[%.4g,%.4g,%.4g]  |U|.mean=%.4g",
        (unsigned long long)N, (unsigned long long)nonZero, 100.0 * nonZero / N,
        minX, sumX*inv, maxX,
        minY, sumY*inv, maxY,
        minZ, sumZ*inv, maxZ,
        sumMag*inv);
    log_line(buf);
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

FX3D_API void fx3d_initial_temperature(uint64_t h, float t) {
    {
        char buf[120];
        std::snprintf(buf, sizeof(buf), "fx3d_initial_temperature h=%llu  T=%.4g",
            (unsigned long long)h, t);
        log_line(buf);
    }
    LBM* lbm = resolve(h); if (!lbm) return;
#ifdef TEMPERATURE
    const ulong N = lbm->get_N();
    for (ulong n = 0; n < N; n++) lbm->T[n] = t;
#else
    (void)t;
    log_line("  -> TEMPERATURE disabled at compile time; no-op");
#endif
}

FX3D_API void fx3d_commit_to_device(uint64_t h) {
    log_line("fx3d_commit_to_device h=" + std::to_string(h));
    LBM* lbm = resolve(h); if (!lbm) { log_line("  -> bad handle"); return; }
    try {
        lbm->flags.write_to_device();
        lbm->u.write_to_device();
        lbm->rho.write_to_device();
#ifdef TEMPERATURE
        lbm->T.write_to_device();
#endif
        log_line("  -> ok (flags+u+rho copied)");
    } catch (const std::exception& ex) {
        log_line(std::string("  -> EXCEPTION: ") + ex.what());
    } catch (...) {
        log_line("  -> UNKNOWN EXCEPTION");
    }
}

FX3D_API void fx3d_destroy(uint64_t h) {
    log_line("fx3d_destroy h=" + std::to_string(h));
    auto& b = bridge();
    std::lock_guard<std::mutex> lock(b.mtx);
    b.sims.erase(h);
}

} // extern "C"
