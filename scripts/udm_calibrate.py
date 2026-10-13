# -*- coding: utf-8 -*-
"""
Runs the UnifiedDispersion solver on the continuous-release benchmarks with a given set of closure
constants (passed as UDM_* environment variables) and prints the Hanna statistics per bench plus an
aggregate score. Usage:
    python scripts/udm_calibrate.py                       # defaults
    python scripts/udm_calibrate.py RIDAMP=0.2 GEDGE=1.0  # override constants
    python scripts/udm_calibrate.py --grid                # small grid search
"""
import os, re, subprocess, sys, itertools, math

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLI = os.path.join(ROOT, "DisperSim3D.CLI", "bin", "Debug", "net10.0", "DisperSim3D.CLI.exe")
# benches with field data (Medium/High); RegressionBaseline ones are excluded from the score
BENCHES = {
    "passive": ["prairie-grass-07-B", "prairie-grass-11-C", "prairie-grass-22-D", "prairie-grass-29-E", "prairie-grass-35-E"],
    "dense": ["burro3", "burro5", "burro6", "burro7", "coyote-03", "coyote-05", "falcon-01", "falcon-03", "falcon-04", "maplin-sands-27", "kit-fox-u5-2"],
    "jet": ["desert-tortoise-04"],
}

def run(bench, env):
    r = subprocess.run([CLI, "--validate", os.path.join(ROOT, "benchmarks", bench + ".dsbench"), "-s", "UnifiedDispersion"],
                       capture_output=True, text=True, env=env)
    m = re.search(r"Result:\s+(\w+)\s+MRB=([-\d.E]+)\s+RMSE=([-\d.E]+)\s+FAC2=([-\d.E]+)\s+MG=([-\d.E]+)\s+VG=([-\d.E]+)", r.stdout)
    if not m: return None
    return dict(result=m.group(1), MRB=float(m.group(2)), FAC2=float(m.group(4)), MG=float(m.group(5)), VG=float(m.group(6)))

def score(params, verbose=True):
    env = dict(os.environ)
    for k, v in params.items(): env["UDM_" + k] = str(v)
    rows = []; lnMG = []; fac2 = []
    for grp, bl in BENCHES.items():
        for b in bl:
            s = run(b, env)
            if s is None: rows.append((grp, b, None)); continue
            rows.append((grp, b, s)); lnMG.append(abs(math.log(max(s["MG"], 1e-6)))); fac2.append(s["FAC2"])
            if verbose: print(f"  {grp:8s} {b:22s} {s['result']:5s} MG={s['MG']:6.2f} FAC2={s['FAC2']:4.2f} MRB={s['MRB']:6.2f} VG={s['VG']:5.2f}")
    agg = dict(mean_absLnMG=sum(lnMG) / len(lnMG), mean_FAC2=sum(fac2) / len(fac2), n=len(lnMG))
    if verbose: print(f"  >> {params}  |ln MG|={agg['mean_absLnMG']:.3f}  FAC2={agg['mean_FAC2']:.2f}")
    return agg, rows

if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    params = dict(a.split("=") for a in args)
    if "--grid" in sys.argv:
        grid = dict(RIDAMP=[0.1, 0.3, 0.8], GEDGE=[0.6, 1.0, 1.5], KTOP=[0.0, 0.4, 1.0], KAMB=[0.7, 1.0])
        best = None
        for combo in itertools.product(*grid.values()):
            p = dict(zip(grid.keys(), combo)); p.update(params)
            agg, _ = score(p, verbose=False)
            print(f"{p}  |lnMG|={agg['mean_absLnMG']:.3f} FAC2={agg['mean_FAC2']:.2f}", flush=True)
            if best is None or agg["mean_absLnMG"] < best[0]["mean_absLnMG"]: best = (agg, p)
        print("BEST", best)
    else:
        score(params)
