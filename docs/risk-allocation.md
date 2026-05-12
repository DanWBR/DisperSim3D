---
layout: default
title: Risk-Reduction Allocation
nav_order: 8
---

# Risk-Reduction Detector Allocation
{: .no_toc }

1. TOC
{:toc}

DisperSim 3D ships three gas-detector placement strategies. This page covers
the newest one: **greedy risk-reduction allocation** that minimises the
expected unmitigated risk across a portfolio of leak scenarios, with leak
frequencies derived from the embedded IOGP 434-01 database.

For the unweighted max-coverage allocator and the classic Vianna 2019 Set
Covering Problem, see [Dispersion Studies &amp; Detector Allocation](studies-detectors).

## When to use which strategy

| Strategy | Pick when |
|---|---|
| **Set Covering** (Vianna 2019) | One simulation, want the minimum detector count |
| **Greedy Max Coverage** | Many simulations, every cloud has equal importance |
| **Greedy Min Residual Risk** (this page) | Many simulations, scenarios differ in frequency and consequence — typical for QRA detector siting |

## Mathematical formulation

Per-scenario risk:

```
R_s = freq_s · cons_s · P_d                       (events/year × consequence)
```

`s` indexes the simulations in a `DispersionStudy`. `P_d` is a global
detection probability (default 1.0). The allocator greedily picks at each
step the candidate `c*` that maximises the weighted risk it covers:

```
c* = argmax_c   Σ_{s ∈ cover(c) \ covered}   R_s · w(c, s)
```

With distance weighting **off**: `w(c, s) ≡ 1` — every cloud within the
detection radius is "fully covered".

With distance weighting **on** (Rad &amp; Rashtchian 2016):

```
w(c, s) = w_min + (w_max − w_min) · (1 − d(c, s) / DetectionRadius)
```

where `d(c, s)` is the candidate-to-cloud-bbox closest-point distance — a
cheap proxy that preserves the monotonic "closer is better" property. With
defaults `w_min = 0.5, w_max = 1.0` a detector right on the edge of the
radius contributes half of a detector at the cloud centre.

The greedy loop stops when either `MaxDetectors` is reached or the residual
risk drops below `(1 − TargetRRF%/100) · TotalRisk`. Approximation guarantee
versus the exact MILP solution: `(1 − 1/e) ≈ 63%` worst case (Nemhauser et
al. 1978), typically &lt; 5% gap on industrial cases per Rad 2017.

A **risk-reduction curve** `(k, RRF)` is captured after every pick — the
"marginal utility" plot from Rad 2017 Fig. 7. The knee of that curve tells
you when adding more detectors stops paying off in risk terms.

## Inputs the allocator needs

Per scenario `s`:

- `freq_s` — events / year. Comes from **auto** (IOGP × wind rose, default)
  or **manual** override in the dialog grid.
- `cons_s` — consequence weight (relative scalar). Auto-derived from cloud
  volume × hazard, or manual.
- `P_d` — global detection probability (single dialog field).

Per candidate detector:

- Position `(x, y, z)` in the `[MinZ, MaxZ]` breathing zone.
- `DetectionRadiusM` — sphere within which a cloud is considered covered.

## Auto frequency — IOGP × wind rose

The default `freq_s` for a simulation is:

```
freq_s = source.EffectiveLeakFrequencyPerYear  ×  P_wind(WindDirection_deg)
```

### IOGP 434-01 leak-frequency database

`source.EffectiveLeakFrequencyPerYear` is computed from the source's
**equipment inventory** — a list of equipment items contributing to the
release scenario. Each item carries (type, nominal diameter, count). For
pipe types the "count" is total length in metres; for everything else it's
the number of items.

The embedded database is the **2006–2015 dataset of IOGP Report 434-01**
(September 2019, revision 1.1 May 2021): 24 equipment types × 5 hole-size
bands × 6 nominal diameters. Interpolation between the 6 anchor diameters
(50 / 150 / 300 / 450 / 600 / 900 mm) is linear; outside the range clamps.

The 24 equipment types are:

| Datasheet | Type | Units |
|---|---|---|
| 1 | Steel process pipe | per metre·year |
| 2 | Flanged joint | per joint·year |
| 3 | Manual valve | per valve·year |
| 4 | Actuated valve | per valve·year |
| 5 | Instrument connection | per connection·year |
| 6 | Pressure vessel | per vessel·year |
| 7 | Pump, centrifugal | per pump·year |
| 8 | Pump, reciprocating | per pump·year |
| 9 | Compressor, centrifugal | per compressor·year |
| 10 | Compressor, reciprocating | per compressor·year |
| 11 | Shell &amp; tube HX, HC shell side | per HX·year |
| 12 | Shell &amp; tube HX, HC tube side | per HX·year |
| 13 | Plate HX | per HX·year |
| 14 | Air-cooled HX | per HX·year |
| 15 | Filter | per filter·year |
| 16 | Pig trap | per pig trap·year |
| 17 | Flexible pipe | per metre·year |
| 18 | Pressure vessel (Other) | per vessel·year |
| 19 | Degasser | per vessel·year |
| 20 | Expander | per equipment·year |
| 21 | Xmas tree | per tree·year |
| 22 | Turbine | per turbine·year |
| 23 | Pipeline ESDV | per valve·year |
| 24 | SSIV assembly | per assembly·year |

### Hole-size band

Every IOGP datasheet bins leaks into 5 bands. The representative diameter
for QRA consequence work is the geometric mean per IOGP §2.1.2:

| Band | Range | Geometric mean |
|---|---|---|
| Tiny | 1–3 mm | 1.73 mm |
| Small | 3–10 mm | 5.48 mm |
| Medium | 10–50 mm | 22.36 mm |
| Large | 50–150 mm | 86.60 mm |
| Rupture | &gt; 150 mm | 152.4 mm (6") |

### Wind probability

`P_wind(direction)` is a nearest-bin lookup on the project's
`Scene3D.WindRose.Bins[]`. Each bin carries a compass direction (deg) and
a frequency (%). The default uniform 8-bin rose gives 12.5% per direction.
When no rose is configured, the allocator falls back to 1/8 uniform so the
optimisation still runs.

### Worked example

A 6" carbon-steel process loop with the following inventory:

```
50 m  steel pipe       6"   → 50 × 1.6×10⁻⁶ = 8.0×10⁻⁵
12    flanged joints   6"   → 12 × 1.4×10⁻⁶ = 1.68×10⁻⁵
 4    manual valves    6"   →  4 × 3.8×10⁻⁶ = 1.52×10⁻⁵
                                       ──────────────────
Total source frequency, Medium band   = 1.12×10⁻⁴ events/yr
```

At a wind probability of 0.125 (default uniform rose), `freq_s` for one
simulation = `1.4×10⁻⁵` events/year.

### Three levels of override

From coarse to fine, the user can take control:

1. **Manual `ScenarioRisk.Freq`** (per-simulation, in the allocation dialog grid).
2. **Manual `ReleaseSource3D.LeakFrequencyPerYear`** (per-source, uncheck `AutoComputeLeakFrequency`).
3. **IOGP inventory** (default) — `AutoComputeLeakFrequency = true`.

Levels 1 and 2 take precedence when set.

## Auto consequence — cloud volume × hazard

For each cloud snapshot the allocator computes:

```
cellVol = (2·DomainHalfM / Nx) · (2·DomainHalfM / Ny) · (DomainHeightM / Nz)
vol     = CloudCellCount · cellVol

toxic     and gas.IDLH > 0   →  cons = vol · max(1, peakConc/IDLH)
flammable and gas.LFL  > 0   →  cons = vol · (peakConc ≥ LFL ? 1.0 : 0.5)
otherwise                    →  cons = vol
```

The IDLH / LFL come from the gas on the simulation's snapshot source. A more
sophisticated probit-fatality model (TNT-equivalent overpressure integrated
over the cloud) is out of scope for v1; the heuristic above keeps consequence
proportional to "amount of bad gas × how bad it is".

Manual override via `ScenarioRisk.ConsMode = Manual`.

## UI walkthrough

### Step 1 — equipment inventory per source

Right-click a release source in the project tree → **"Equipment Inventory
(IOGP)..."**.

In the dialog:

- Pick the **Hole-size band** dropdown that represents the modelled
  release scenario (Medium = 10–50 mm is the default).
- **Add item** to insert inventory rows. Each row has:
  - **Type** combo of the 24 IOGP categories.
  - **Diameter (mm)** — 50, 150, 300, 450, 600, 900 (anchors) or any value
    in between.
  - **Count / Length (m)** — metres for pipe types, count for everything
    else.
  - Free-text **Note**.
- The bottom **Effective** label shows the computed leak frequency live
  as you edit.
- Uncheck **Auto-compute from inventory** to type a manual override.

OK commits the inventory to the source; Cancel reverts.

### Step 2 — dispersion study

Right-click **Dispersion Studies → Add Study...** and pick the simulations
plus the detection criterion (e.g. PercentLFL ≥ 50). The clouds used by the
allocator are the final-snapshot iso-volumes at this threshold.

### Step 3 — risk allocation

Right-click **Detector Allocations → Add Allocation...** Pick the study,
then in the new **Strategy** radio group at the top of settings:

- **Greedy max coverage** keeps the original unweighted set-cover.
- **Min residual risk (IOGP × wind rose × consequence)** turns on the
  new strategy. A risk-weights `DataGridView` appears below with one row
  per study simulation. Columns:
  - `Simulation` (read-only)
  - `Freq Auto` checkbox + `Freq/yr` cell (read-only when Auto)
  - `Cons Auto` checkbox + `Consequence` cell (read-only when Auto)
  - `Risk R_s` (read-only — the computed `freq × cons × P_d`)
- Knob row: `Detector POD`, `Distance weight` checkbox + `Wmin/Wmax`.

Click **Run allocation**. The results panel surfaces:

- **Total risk** Σ R_s across all scenarios.
- **Residual** risk after the chosen positions.
- **RRF** as a percentage — the headline number for stakeholders.
- A small `(K, RRF)` ListView — the marginal-utility curve. Look for the
  knee.

The per-cloud ListView gains a `Residual R_s` column showing which
scenarios remain uncovered (and their risk weight, so you can rank them).

## Persistence

The new fields all round-trip through the project XML:

- `<Source HoleSize="Medium" AutoLeakFreq="true" LeakFreq="...">` + an
  `<Inventory><I Type="..." DiamMm="..." Count="..."/></Inventory>` child.
- `<DispersionStudy>` gets a `<RiskWeights><R SimId="..." FreqMode="Auto"
  ConsMode="Manual" .../></RiskWeights>` child.
- `<DetectorAllocation>` gains `Strategy / DetectionProbability /
  UseDistanceWeighting / DistanceWeightMin/Max / TotalRisk / ResidualRisk
  / RiskReductionFraction` attributes plus `<ResidualRisks>` and
  `<RiskCurve>` children.

All numeric serialisation goes through `CultureInfo.InvariantCulture`.

## Limitations

| Limitation | Workaround / future work |
|---|---|
| Greedy worst-case approximation 63% | Add an LP solver (e.g. Google.OrTools) for exact MILP — out of scope for v1 |
| Single global `DetectionProbability` scalar | No per-detector POD curves yet |
| Consequence is volume × hazard heuristic | Probit-fatality / TNT overpressure left for future |
| Only IOGP 2006–2015 dataset embedded | 1992–2015 historical dataset not bundled |
| Only IOGP §2.1 (offshore/onshore/refinery) | §2.3 LNG FRT not bundled |
| Cloud rendering colour is palette-cycled | Risk-heatmap shading is a planned follow-up |

## Verification

Run `DisperSim3D.App.exe --iogp-selftest` from the CLI. The self-test
checks 25 representative IOGP table cells against the printed values in
the published IOGP 434-01 v1.1 PDF (5% tolerance), validates a
hand-computed aggregate inventory frequency, and verifies the geometric
mean hole sizes. All 27 assertions must pass.

## References

- **Rad, A., Rashtchian, D. &amp; Badri, N.** (2017). *A risk-based methodology
  for optimum placement of flammable gas detectors.* Process Safety and
  Environmental Protection, 105, 175–183. — the MRR greedy implemented by
  `RunRiskReductionGreedy`.
- **Rad, A. &amp; Rashtchian, D.** (2016). *A new approach for optimal placement
  of gas detectors.* Chemical Engineering Transactions, 53, 145–150. — the
  distance-weighted refinement.
- **IOGP Report 434-01** (2019, rev 1.1 May 2021). *Risk Assessment Data
  Directory — Process Release Frequencies.* International Association of
  Oil &amp; Gas Producers.
  [iogp.org](https://www.iogp.org/bookstore/product/risk-assessment-data-directory-process-release-frequencies/).
- **Nemhauser, G., Wolsey, L. &amp; Fisher, M.** (1978). *An analysis of
  approximations for maximizing submodular set functions.* Mathematical
  Programming, 14 (1), 265–294. — origin of the `(1 − 1/e)` greedy bound.
