---
layout: default
title: Theory
nav_order: 4
---

# Theory
{: .no_toc }

Every model DisperSim 3D evaluates, with the equation as implemented, the
assumptions behind it, and the source it comes from.

1. TOC
{:toc}

## What this document is

[TECHNICAL_DOCUMENTATION.md](https://github.com/DanWBR/DisperSim3D/blob/master/TECHNICAL_DOCUMENTATION.md)
describes how the program is built: solver pipelines, case folder layouts,
the C-ABI surface, the project file format. This document describes what the
program computes. Where the two overlap, the equation here is the authority
on the physics and that one is the authority on the plumbing.

Each equation is written as the code evaluates it, including the guards and
clamps, because a floor at 0.5 m or a cap at 80° changes results and belongs
in a document someone checks the model against. Where the implementation
departs from the published form, that is called out rather than smoothed
over.

Whether these models reproduce measurements is a separate question, answered
in [validation.md](validation.md) and [benchmark-results.md](benchmark-results.md).

### Conventions

| Symbol | Meaning | Unit |
|---|---|---|
| `x, y, z` | East, north, up. Ground at `z = 0` | m |
| `u` | Wind speed | m/s |
| `Q` | Source strength, mass basis | kg/s |
| `C` | Concentration | kg/m³ |
| `σ_y, σ_z, σ_x` | Gaussian dispersion coefficients | m |
| `H` | Mixing height | m |
| `h` | Effective release height, stack plus rise | m |
| `I` | Incident radiant flux | kW/m² |
| `ṁ''` | Pool mass burn rate per unit area | kg/(m²·s) |
| `R` | Universal gas constant, 8.31446 | J/(mol·K) |
| `M` | Molar mass | kg/mol |
| `γ` | Ratio of specific heats | — |

SI throughout, with two exceptions that follow their source correlations:
radiant flux is quoted in kW/m², and the jet flame-length correlation takes
heat release in kW. Both are noted where they appear.

---

## 1. Atmospheric state

### 1.1 Wind profile

The wind speed at height follows a power law anchored on the measurement
height:

```
u(z) = u_ref · (z / z_ref)^p          z floored at 0.5 m
```

`z_ref` defaults to 10 m. The shear exponent `p` is taken from the scenario
when set, otherwise from a stability-and-terrain table (`MeteorologicalConditions.GetDefaultShearExponent`)
which carries separate open-country and urban columns.

**Assumption.** A power law is a fit, not a similarity solution. It has no
roughness length and no Monin-Obukhov scaling, so it cannot represent a
surface layer that changes character with height. The CFD paths do not use
it; they use the logarithmic ABL profiles of §5.3.

**File**: `Models/MeteorologicalConditions.cs`

### 1.2 Stability classes and dispersion coefficients

Pasquill-Gifford-Turner classes A (very unstable) through F (very stable)
select the coefficients of a power law in downwind distance:

```
σ_y = a_y · x^b_y
σ_z = a_z · x^b_z          x floored at 1 m, σ floored at 0.5 m
```

Continuous-plume coefficients are power-law fits to Briggs (1973)
open-country formulas over 50–1000 m:

| Class | `a_y` | `b_y` | `a_z` | `b_z` |
|---|--:|--:|--:|--:|
| A | 0.2293 | 0.9894 | 0.2000 | 1.0000 |
| B | 0.1667 | 0.9894 | 0.1200 | 1.0000 |
| C | 0.1146 | 0.9894 | 0.0872 | 0.9790 |
| D | 0.0833 | 0.9894 | 0.1090 | 0.8550 |
| E | 0.0625 | 0.9894 | 0.0383 | 0.9400 |
| F | 0.0417 | 0.9894 | 0.0202 | 0.9430 |

Instantaneous-puff coefficients follow Slade (1968), and add an along-wind
coefficient as a fixed fraction of the crosswind one, `σ_x = f_x · σ_y`:

| Class | `a_y` | `b_y` | `a_z` | `b_z` | `f_x` |
|---|--:|--:|--:|--:|--:|
| A | 0.40 | 0.91 | 0.40 | 0.91 | 0.50 |
| B | 0.36 | 0.86 | 0.33 | 0.86 | 0.50 |
| C | 0.32 | 0.78 | 0.22 | 0.78 | 0.55 |
| D | 0.31 | 0.71 | 0.15 | 0.71 | 0.60 |
| E | 0.31 | 0.68 | 0.10 | 0.68 | 0.65 |
| F | 0.31 | 0.68 | 0.05 | 0.68 | 0.70 |

**Validity.** These are open-country, flat-terrain, ~1 hour averaging
correlations extrapolated outside 50–1000 m without a change of form. They
carry no obstacle, no terrain and no density effect. Every heavy-gas failure
in [benchmark-results.md](benchmark-results.md) traces to this paragraph.

**File**: `Core/PasquillGiffordCoefficients.cs`

---

## 2. Analytical dispersion

### 2.1 Gaussian plume, steady state

For a continuous release, concentration at a point whose crosswind offset
from the centreline is `y`:

```
C = Q / (2π · u · σ_y · σ_z) · exp(−½ (y/σ_y)²) · V(z)
```

`V(z)` is the vertical term of §2.3. The lateral exponent is short-circuited
to zero below `exp(−18)`, which bounds the plume at roughly 6σ.

The centreline is not straight. It leaves the source along the release
direction and relaxes exponentially onto the wind direction over a
momentum-based bend length:

```
L_bend = max(R²·D·π/4, 10·D)          clamped to 0.8 · domain half-size
```

with `R` the release-to-wind velocity ratio and `D` the orifice diameter.
`σ_y` and `σ_z` are evaluated along arc length on that curved path, sampled
at 200 points, rather than along the straight downwind coordinate. When a
3D wind field is bound to the scenario, the wind speed and direction at the
source are interpolated from the field instead of taken from the uniform
meteorology.

**File**: `Core/GaussianPlumeEngine.cs`

### 2.2 Gaussian puff, transient

Each interval `ΔT_puff` releases a puff carrying mass `Q·ΔT_puff`. A puff's
contribution at a point offset `(dx, dy)` from its centre is

```
C = Q_eff / ((2π)^{3/2} · σ_x · σ_y) · exp(−½ (dx²/σ_x² + dy²/σ_y²)) · V(z)
```

`Q_eff = Q · exp(−t/τ)` when the gas has a finite half-life, `τ = t_½/ln 2`.
Puff centres are advected by the wind vector evaluated at the puff's own
height, or sampled from a bound 3D wind field. A momentum jet adds a
decaying displacement `x_jet = v_jet · τ_jet · (1 − exp(−t/τ_jet))`, so the
puff leaves along the release axis and turns into the wind over the jet
relaxation time.

The total field is the sum over live puffs. This is superposition of
independent Gaussians: puffs do not interact, merge or exchange mass.

**File**: `Core/GaussianPuffEngine.cs`

### 2.3 Ground reflection and mixing-height trapping

Both engines share the vertical term. Below the mixing height the plume
reflects off the ground and off the inversion lid, represented by an image
series:

```
V(z) = 1/(σ_z √(2π)) · Σ_{n=−N}^{N} [ exp(−½((z − h − 2nH)/σ_z)²)
                                    + exp(−½((z + h − 2nH)/σ_z)²) ]
```

Once `σ_z > 1.6 H` the series has converged on a uniformly mixed layer and
is replaced by `V(z) = 1/H`, which is both cheaper and better behaved.

Reflection is total: the ground is a perfect mirror, with no deposition and
no absorption.

### 2.4 Plume rise

Briggs rise is added to the stack height before dispersion. Buoyancy flux:

```
F_b = g · v_s · d_s² · (T_s − T_a) / (4 T_s)
```

For unstable and neutral conditions, with a downwind distance to final rise

```
x_f = min(3.5 · 14 · F_b^0.625, 10000)     F_b < 55
x_f = min(3.5 · 34 · F_b^0.4,   10000)     F_b ≥ 55

Δh = 1.6 · F_b^{1/3} · x_f^{2/3} / u
```

For stable conditions (classes E and F), with a stability parameter built
from an assumed lapse rate `dT/dz` of 0.020 K/m (E) or 0.035 K/m (F):

```
s  = g/T_a · (dT/dz + 0.0098)
Δh = 2.6 · (F_b / (u · s))^{1/3}
```

Momentum-dominated rise:

```
Δh = 1.44 · (v_s · d_s / u)^{2/3} · d_s^{1/3}
```

The model takes `max(Δh_buoyancy, Δh_momentum)` rather than combining them,
and the result is capped at the mixing height. Wind speed is floored at
0.5 m/s; a cold release (`T_s ≤ T_a`) gets no buoyant rise.

**File**: `Core/BriggsPlumerise.cs`

---

## 3. Source terms

### 3.1 Orifice mass flow

Choking is decided by the critical pressure ratio. When
`P_0 / P_amb > ((γ+1)/2)^{γ/(γ−1)}` the orifice is choked and

```
ṁ = C_d · A · P_0 · sqrt( (γM)/(R T) · (2/(γ+1))^{(γ+1)/(γ−1)} )
```

otherwise, with `p_r = P_amb / P_0` and `ρ_0 = P_0 M /(R T)`,

```
ṁ = C_d · A · sqrt( ρ_0 · P_0 · (2γ/(γ−1)) · (p_r^{2/γ} − p_r^{(γ+1)/γ}) )
```

`A` is the geometric orifice area and `C_d` the discharge coefficient. The
inverse, `OrificeDiameterFromMassFlow`, solves the same relations for `A`
so a scenario can be specified by release rate instead of hole size.

**File**: `Core/HighPressureLeakModel.cs`

### 3.2 Blowdown

`ComputeBlowdownProfile` steps vessel pressure and temperature forward under
isentropic expansion, re-evaluating §3.1 each step, and returns `ṁ(t)` until
the vessel reaches ambient or empties. The Gaussian engines consume that
profile directly, so a depressurising release is not modelled as a constant
source.

### 3.3 Two expanded sources, for two different questions

The project computes an expanded (pseudo) source in two places, and they are
not interchangeable.

**For a CFD inlet patch** — `HighPressureLeakModel.ComputeExpandedSource`
fixes the velocity at a convenient value (default 100 m/s) and solves for
the diameter that carries the mass flow at ambient density:

```
A_pseudo = ṁ / (ρ_amb · V_target)          d = sqrt(4A/π)
```

This answers "what inlet should I draw on the mesh so the solver does not
need to resolve a sonic jet". It is a meshing convenience, not a physical
state.

**For flame shape** — `JetExpandedSource.Compute` resolves where the jet
actually is once it has finished expanding, after Miller (2017) eq. (1)–(3).
The throat is taken at the isentropic sonic condition,

```
T_t = T_0 · 2/(γ+1)         p_t = P_0 / ((γ+1)/2)^{γ/(γ−1)}
ρ_t = p_t M /(R T_t)        u_t = sqrt(γ R T_t / M)
```

then expanded to atmospheric pressure through momentum, energy and mass:

```
(1)  u_e = u_t + (p_t − p_amb)/(u_t ρ_t)
(2)  T_e = T_t − ½(u_e² − u_t²)/c_p            c_p = γR/((γ−1)M)
(3)  ρ_e = p_amb M/(R T_e)    A_e = ṁ/(ρ_e u_e)    d_e = sqrt(4A_e/π)
```

`T_e` is floored at 50 K. A subsonic release is its own expanded source.

Using the meshing pseudo-source for flame shape would be wrong: it has the
velocity fixed by fiat, and flame shape depends on exactly that velocity.

**Files**: `Core/HighPressureLeakModel.cs`, `Core/JetExpandedSource.cs`

---

## 4. Scalar transport on a GPU wind field

The FluidX3D path splits the problem: the GPU solves for a steady wind
field, and a separate scalar solver transports species through it. The split
is deliberate — FluidX3D's built-in temperature extension couples back into
the velocity field even with thermal expansion off, which corrupts the wind
solution the field exists to provide.

### 4.1 What FluidX3D solves

[FluidX3D](https://github.com/ProjectPhysX/FluidX3D) is a third-party GPU
lattice Boltzmann solver. DisperSim compiles it with `D3Q19`, `FP32`,
`VOLUME_FORCE`, `EQUILIBRIUM_BOUNDARIES` and `SUBGRID`: a 19-velocity
lattice in single precision, with body forces, equilibrium inlet/outlet
cells, and Smagorinsky-Lilly subgrid viscosity, which is required at
atmospheric Reynolds numbers of 10⁵ and above. Combustion, free surface and
graphics are compiled out. The discretisation is FluidX3D's; this project
supplies the domain, the boundary state, the obstacle mask and the unit
conversion, and reads the velocity field back.

Obstacles enter as solid cells: `FluidX3DObstacleVoxelizer` rasterises every
decoration's bounding box onto the lattice.

### 4.2 Lattice units

The bridge converts SI to lattice units on the way in and back on the way
out. With `Nx` cells across a domain of half-width `L`:

```
Δx  = 2L / Nx
Δt  = Δx · C_SI / C_lattice                 C_lattice = 1/√3
ν_l = ν   · Δt / Δx²
g_l = g   · Δt² / Δx
α_l = α   · Δt / Δx²
```

`C_lattice = 1/√3` is the lattice speed of sound, which is what ties the
timestep to the cell size in an LBM.

**File**: `Core/FluidX3DUnits.cs`

### 4.3 Passive tracer

`DispersionTracerEngine` advances a passive scalar on the sampled wind
field by operator splitting: semi-Lagrangian advection, then explicit
diffusion and decay.

Advection traces each cell centre backwards along the local velocity and
interpolates trilinearly at the departure point. This is unconditionally
stable and does not restrict the timestep by a CFL condition, at the cost
of numerical diffusion. `BuoyantTracerEngine` adds BFECC error compensation
to reduce that.

Diffusion is an explicit Laplacian with per-axis coefficients

```
λ_i = D · Δt / Δx_i²
```

and the step is subdivided whenever `2(λ_x + λ_y + λ_z) ≥ 1`, which is the
stability bound for the explicit scheme.

Cells inside obstacles are forced to zero after every step. A source sphere
carves a hole in that mask, so a leak located inside an equipment bounding
box — the common case, since the leak *is* the equipment — can still vent.

Two source types are available: a clamped-concentration sphere, and a mass
injection depositing `Q·Δt/(ρ·V_source)` per source cell per step, which
gives a field in physical units without post-hoc scaling.

### 4.4 Subgrid diffusivity

The molecular diffusivity is a floor. The transport coefficient actually
used is a Smagorinsky-like subgrid estimate that scales with cell size and
wind speed:

```
D_t = (C_s² / Sc_t) · Δ · U          C_s = 0.092, Sc_t = 0.7
```

giving an effective constant of 0.0084/0.7 in the code. This is what lets
the same model span wind-tunnel scale (`D_t ≈ 8×10⁻⁴ m²/s` at `Δ = 0.067 m`)
and industrial scale (`D_t ≈ 0.4 m²/s` at `Δ = 6.7 m`) without retuning.

### 4.5 Buoyant and dense-gas transport

`BuoyantTracerEngine` carries concentration and temperature on the same
grid and derives a density field from them. Mixture molar mass from mass
fraction `Y`, then ideal-gas density:

```
M_mix = 1 / (Y/M_gas + (1−Y)/M_air)
ρ_mix = P · M_mix / (R T)
```

Buoyancy enters as a vertical velocity added to the wind:

```
v_buoy = g · (ρ_air − ρ_mix) / ρ_air          positive is upward
```

A cloud heavier than air also spreads laterally as a gravity current, with
a front speed from the reduced gravity and a direction down the density
gradient:

```
g'   = g · (ρ_mix − ρ_air) / ρ_air
U_gc = C_gc · sqrt(g' · Δz)                   C_gc = 0.5
n̂    = −∇ρ / |∇ρ|
```

The effective velocity is capped so no parcel crosses more than half a cell
per step.

**Assumption.** This is a one-way coupling. Density affects the tracer but
the tracer never affects the wind field, which was solved once and frozen.
A dense cloud large enough to alter the flow around it is outside this
model; that case needs the OpenFOAM path, where the coupling is two-way.

`BuoyantTracerEngineGpu` is the same formulation ported to seven OpenCL
kernels, in single precision. On a high-pressure sonic jet with gradients
crossing two or three cells, FP32 error reaches 15–25% on the centreline —
see [benchmark-results.md](benchmark-results.md).

**Files**: `Core/DispersionTracerEngine.cs`, `Core/BuoyantTracerEngine.cs`,
`Core/BuoyantTracerEngineGpu.cs`, `Core/FireTracerEngine.cs`

---

## 5. The OpenFOAM path

### 5.1 What `rhoReactingBuoyantFoam` solves

The universal CFD solver is stock `rhoReactingBuoyantFoam`, run with
combustion disabled. It solves the compressible, buoyant, multi-species
Favre-averaged equations: mass continuity, momentum with a gravity term and
the buoyant pressure split `p_rgh = p − ρ g·h`, energy in enthalpy form, and
one transport equation per species with the mixture closed by an ideal-gas
equation of state.

Running it with reactions off makes it a general dense-gas and cryogenic
dispersion solver: it keeps compressibility, buoyancy and multi-species
transport, which is what LNG and CO₂ releases need, and drops only the
chemistry.

### 5.2 Turbulence closure

Standard `kEpsilon`, or `buoyantKEpsilon` when the scenario asks for the
buoyancy-modified production term. Coefficients as written into
`constant/turbulenceProperties`:

| Coefficient | Value |
|---|--:|
| `Cmu` | 0.09 |
| `C1` | 1.44 |
| `C2` | 1.92 |
| `sigmak` | 1.0 |
| `sigmaEps` | scenario, OpenFOAM default 1.3 |
| `Ceps3` | scenario, −0.33 for the buoyant model |
| `Prt` | scenario |
| `Sct` | scenario |

Uniform initial fields are seeded from a 5% turbulence intensity:

```
k = 1.5 (U·I)²          I = 0.05
ε = C_mu k² / (0.1 k / U)
ν_t = C_mu k² / ε
```

**Known limitation, and it matters.** Stock `rhoReactingBuoyantFoam` does
not read `Sct` for species transport. Its equation uses
`fvm::laplacian(turbulence->muEff(), Yi)`, which is `Sc_t = 1.0` hard-coded,
so the `Sct` entry above reaches the turbulence model but not the species
equation. Vu (2019) reached `FAC2 = 1.0` on the LNG trials with a custom
solver at `Sc_t = 0.15`. A patched binary exists in this project and is
disabled by default — the measurements behind that decision are in
[benchmark-results.md](benchmark-results.md#vu-2019-reproduction-attempts).
Three LNG benchmark failures trace directly to this paragraph.

### 5.3 Atmospheric boundary layer

Inlets use OpenFOAM's `atmBoundaryLayerInletVelocity`, `...InletK` and
`...InletEpsilon`, which impose the logarithmic surface-layer profiles

```
u(z)  = (u*/κ) · ln((z − d + z_0)/z_0)
k     = u*² / sqrt(C_mu)
ε(z)  = u*³ / (κ (z − d + z_0))
```

with `z_0` from the scenario (default 0.03 m), `C_mu = 0.09`, and a
zero-plane displacement `d`. The ground uses `nutkAtmRoughWallFunction`,
which is undefined when the ground-adjacent cell midpoint falls below `z_0`;
the case generator checks this and warns, recommending a minimum cell size
of about `2 z_0`.

This is the profile the Gaussian engine's power law (§1.1) approximates. The
two will not agree exactly, and the CFD one is the defensible one.

### 5.4 Wind-only cases

A wind-field-only case is written with `simulationType laminar` and a fixed
`ν = 1.5×10⁻⁵ m²/s`. It exists to produce a velocity field for the tracer
engines quickly, not to resolve atmospheric turbulence.

**File**: `Core/OpenFoamCaseGenerator.cs`

---

## 6. Fire and thermal radiation

### 6.1 Flame length

Jet flames use Chamberlain (1987), with heat release in **kilowatts**:

```
L = 0.2 · Q^0.4          Q = ṁ · ΔH_c / 1000
```

Pool fires use Thomas (1963), through a dimensionless burn rate:

```
ṁ* = ṁ'' / (ρ_air sqrt(g D))
L  = 42 · D · (ṁ*)^0.61
```

Pool flames are always built on a vertical axis regardless of the source's
nominal direction; a pool fire has no release direction.

### 6.2 Flame tilt and buoyant arcing

Wind tilts the flame toward it:

```
θ = min( arctan(U_wind / U_exit), 80° )
```

A horizontal jet flame additionally does not stay on its release axis: it
runs straight while momentum dominates, then buoyancy turns the remainder
upward. The split follows Miller (2017) eq. (19)–(22), driven by the
Richardson number at the **expanded** source of §3.3:

```
d_s  = d_e · sqrt(ρ_e / ρ_air)
ξ    = (g / (d_s² u_e²))^{1/3} · L

bl/L = 1.25 − 0.125 ξ            momentum section, clamped to [0,1]
Ly/L = 0.125 ξ − 0.25            vertical lift,    clamped to [0,1]
δ    = arcsin( Ly / (L − bl) )   lift angle of the buoyant section
```

The flame is then panelled as two cylindrical sections joined at `bl`, the
second rotated up by `δ` in the vertical plane containing the release axis.
A source with no stagnation pressure is treated as subsonic, which yields a
straight flame.

**Limitation.** The flame bends only in the vertical plane containing its
own axis. It cannot bend sideways, so receivers to the side of a
strongly cross-wind flame are over-predicted — measured at 3.67× in the
worst position of the Johnson 1083 benchmark.

**Files**: `Core/JetFireModel.cs`, `Core/SolidFlameModel.cs`

### 6.3 Solid flame radiation

The flame is a tilted cylinder discretised into 12 circumferential × 8 axial
panels plus a tip cap of 12 wedges. Jet flames take a width-to-length ratio
of 0.13 when no diameter is given.

Incident flux at a receiver accumulates each panel's contribution as a
**vector** along the unit direction from receiver to panel:

```
G = Σ_panels  τ · cos θ_s · dA / (π r²) · û
```

so a single pass answers every receiver orientation: the view factor for a
surface with normal `n` is `F = n·G`, the worst-case orientation is `|G|`,
and a horizontal upward-facing receiver gets `G_z`. Panels facing away from
the receiver are culled. Flux is `SEP · F`.

The tip cap matters more than its area suggests. Without it, a receiver
directly off the flame tip sees every lateral panel edge-on or from behind,
every contribution is culled, and the sum is exactly zero — not small. Three
radiometers 50–60 m ahead of the Johnson 1083 flame measured 4.6, 3.3 and
2.2 kW/m² against a model that returned 0.

### 6.4 Surface emissive power

From the radiative fraction, normalised over the **lateral** area `πDL`:

```
SEP = χ · ṁ · ΔH_c / (π D L) / 1000
```

Lateral area, not total: published emissive powers are normalised that way,
and Montoir's 257–273 kW/m² is explicitly quoted for the entire visible fire
area of such a cylinder. The tip cap stays in the geometry for the view
factor but out of this denominator. The panels then emit slightly more than
`χ·Q` — 3% at a jet's `L/D` near 8, 13% at a pool's near 2 — which is the
conservative direction and the price of quoting the same quantity the
measurements quote.

The result is capped, and the cap depends on the fuel:

```
jet                     min(SEP, 350 kW/m²)
clean pool  (LNG, LPG)  min(SEP, 280 kW/m²)
sooty pool              min(SEP, SEP_Mudan(D))

SEP_Mudan(D) = 140 · exp(−0.12 D) + 20 · (1 − exp(−0.12 D))
```

The sooty blend interpolates between a clear luminous flame at small
diameter and a smoke-obscured one at large diameter, after Mudan (1984).
Applying it to LNG is wrong and was a real defect: it caps a 35 m pool near
22 kW/m² where the trials report 165–265. `FireSource.IsSootyFuel` routes
clean fuels past it.

An explicit `FireSource.SepKwM2` overrides all of the above.

### 6.5 Atmospheric transmissivity

Pietersen's correlation, with the water-vapour path length in Pa·m:

```
τ = 2.02 · (P_w · X)^{−0.09}          clamped to [0, 1]
```

`P_w = RH · P_sat(T)`, with the Buck (1981) saturation curve

```
P_sat = 611.21 · exp( (18.678 − T_c/234.5) · (T_c / (257.14 + T_c)) )
```

A path shorter than the correlation's range (`P_w·X < 1 Pa·m`) returns 1.

### 6.6 Obstacle shading

A panel-to-receiver ray is tested against every obstacle bounding box by the
slab method, behind a global-AABB pre-cull. A blocked ray contributes
nothing. Shading is binary: an obstacle is opaque and does not re-radiate,
so a shaded receiver reads lower than reality next to a hot wall.

**File**: `Core/RayBoxIntersector.cs`

### 6.7 Flash fire

Ignition of a drifting cloud burns the region between the flammability
limits. The engine takes the LFL–UFL envelope, contours it at ½·LFL, splits
it into connected components, and propagates a burn front outward from the
ignition point by a Dijkstra geodesic over the flammable cells. Cells the
front never reaches carry an arrival time of 10⁹ s, which is how
unreachability is represented rather than as a special case.

The output is a flame-arrival-time field, not a pressure field: this is a
flash fire, not a vapour cloud explosion, and no overpressure is computed.

**File**: `Core/FlashFireEngine.cs`

### 6.8 Thermal dose and harm

Dose uses the standard 4/3 power law, with flux in **W/m²**:

```
V = t · I^{4/3}
```

Probits, all of the form `Y = a + b·ln(·)`:

| Effect | Probit | Source |
|---|---|---|
| Fatality | `Y = −14.9 + 2.56 ln(V/10⁴)` | Eisenberg (1975) |
| First-degree burn | `Y = −39.83 + 3.0186 ln V` | TNO |
| Second-degree burn | `Y = −43.14 + 3.0188 ln V` | TNO |

Probit to probability through the normal integral, with the error function
from Abramowitz & Stegun 7.1.26:

```
P = ½ (1 + erf((Y − 5)/√2))
```

Anchors worth remembering, and asserted in the self-test: 20 s at about
18 kW/m² is 1% lethality (`Y = 2.67`), and 20 s at about 36 kW/m² is 50%
(`Y = 5.0`).

**Assumption.** A probit is a dose-response fit over a population. It gives
no protection credit for clothing, shelter or escape, and the exposure time
is an input the user supplies rather than something the model derives from
an escape model.

**File**: `Core/ThermalDose.cs`

---

## 7. Derived quantities

### 7.1 Flammable cloud volume

`FlammableCloudCalculator` sums cell volumes where the concentration lies
between LFL and UFL, and reports the lean and rich fractions separately. The
split matters for ignition studies: a rich core is not flammable, and a
model that reports only the total hides that.

**File**: `Core/FlammableCloudCalculator.cs`

### 7.2 Detector placement and risk

Detector siting is a set-covering problem over a study's cloud set, with
greedy and exact Balas branch-and-bound solvers. The risk-weighted variant
minimises expected unmitigated risk `Σ R_s`, with `R_s = f_s · c_s · P_d`,
frequencies from the embedded IOGP 434-01 leak database crossed with the
wind rose, and consequence from cloud volume × hazard.

That formulation, its inputs and its v1 limitations are documented in
[risk-allocation.md](risk-allocation.md) and TECHNICAL_DOCUMENTATION.md §3.9
rather than repeated here — it is decision theory built on top of the
physics above, not physics.

---

## 8. Model selection

Which model is defensible for a given release:

| Situation | Use | Why |
|---|---|---|
| Neutrally buoyant gas, flat open terrain, screening | Gaussian plume or puff | The correlations were fitted to exactly this |
| Dense or cryogenic gas | OpenFOAM `rhoReactingBuoyantFoam` | Needs compressibility and buoyancy; §1.2 has neither |
| Obstacle array, plant congestion | OpenFOAM, or FluidX3D with obstacles | Gaussian σ curves carry no geometry |
| Sonic jet, area classification | FluidX3D with the expanded source of §3.3 | Resolves the near field |
| Fast parametric sweep over many cases | FluidX3D wind field plus tracer | Seconds per case, wind solved once |
| Flashing or two-phase release | **None of them** | No rainout or aerosol model exists — §9 |

---

## 9. What is not modelled

Stated plainly, because a consequence model with undocumented limits cannot
support a safety case.

- **No two-phase, aerosol or rainout physics in the dispersion step.** The
  jet-source thermodynamics exist but do not feed the dispersion solvers.
  Ammonia, supercritical CO₂ and other flashing releases are outside the
  validated envelope.
- **No vapour cloud explosion model.** Flash fire gives flame arrival, never
  overpressure. No TNT equivalence, no TNO multi-energy, no Baker-Strehlow.
- **No cross-wind flame bending**, §6.2.
- **No chemistry.** Combustion is compiled out of both CFD paths. Fire is
  represented by correlations and a radiating solid, never by a reacting
  flow.
- **`Sc_t` is pinned at 1.0 in stock OpenFOAM species transport**, §5.2.
- **One-way coupling in the FluidX3D path**, §4.5. The cloud never modifies
  the wind field.
- **No deposition, no surface absorption, no pool re-evaporation feedback.**
- **A systematic 13% under-prediction of LNG pool emissive power**, held
  untuned — the evidence is in [validation.md](validation.md).

---

## 10. References

Full bibliography in [references.md](references.md). The sources for the
equations above, in order of appearance:

1. Briggs, G.A. (1973). *Diffusion Estimation for Small Emissions.*
   NOAA ATDL Contribution File No. 79. — §1.2, §2.4
2. Slade, D.H., ed. (1968). *Meteorology and Atomic Energy.*
   USAEC TID-24190. — §1.2
3. Turner, D.B. (1970). *Workbook of Atmospheric Dispersion Estimates.*
   USEPA. — §2.1, §2.3
4. Birch, A.D., Brown, D.R., Dodson, M.G., Swaffield, F. (1984). The
   structure and concentration decay of high pressure jets of natural gas.
   *Combustion Science and Technology* 36, 249–261. — §3.3
5. Miller, D. (2017). New model for predicting thermal radiation from flares
   and high pressure jet fires for hydrogen and syngas. *Process Safety
   Progress* 36(3), 237–251. DOI 10.1002/prs.11867. — §3.3, §6.2
6. Smagorinsky, J. (1963). General circulation experiments with the
   primitive equations. *Monthly Weather Review* 91(3), 99–164. — §4.1, §4.4
7. Lehmann, M. / ProjectPhysX. *FluidX3D.*
   <https://github.com/ProjectPhysX/FluidX3D> — §4.1, §4.2
8. Launder, B.E., Spalding, D.B. (1974). The numerical computation of
   turbulent flows. *Computer Methods in Applied Mechanics and Engineering*
   3(2), 269–289. — §5.2
9. Richards, P.J., Hoxey, R.P. (1993). Appropriate boundary conditions for
   computational wind engineering models using the k-ε turbulence model.
   *Journal of Wind Engineering and Industrial Aerodynamics* 46–47,
   145–153. — §5.3
10. Vu, T.L. (2019). *On numerical modelling of atmospheric gas dispersion
    using CFD approach.* PhD thesis, Nanyang Technological University. — §5.2
11. Chamberlain, G.A. (1987). Developments in design methods for predicting
    thermal radiation from flares. *Chemical Engineering Research and
    Design* 65, 299–309. — §6.1, §6.2
12. Thomas, P.H. (1963). The size of flames from natural fires. *9th
    Symposium (International) on Combustion*, 844–859. — §6.1
13. Mudan, K.S. (1984). Thermal radiation hazards from hydrocarbon pool
    fires. *Progress in Energy and Combustion Science* 10(1), 59–80. — §6.4
14. Raj, P.K. (2005). Large LNG fire thermal radiation — modeling issues and
    hazard criteria revisited. *Process Safety Progress* 24(3), 192–202. — §6.4
15. Pietersen, C.M., Huerta, S.C. (1985). *Analysis of the LPG incident in
    San Juan Ixhuatepec, Mexico City.* TNO Report 85-0222. — §6.5
16. Buck, A.L. (1981). New equations for computing vapor pressure and
    enhancement factor. *Journal of Applied Meteorology* 20, 1527–1532. — §6.5
17. Eisenberg, N.A., Lynch, C.J., Breeding, R.J. (1975). *Vulnerability
    Model.* US Coast Guard CG-D-136-75. — §6.8
18. Abramowitz, M., Stegun, I.A. (1964). *Handbook of Mathematical
    Functions*, eq. 7.1.26. — §6.8
19. IOGP (2018). *Risk Assessment Data Directory — Process Release
    Frequencies.* Report 434-01. — §7.2
