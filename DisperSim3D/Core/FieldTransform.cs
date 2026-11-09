using System;
using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Converts a raw CFD scalar field (typically the released-species mass fraction
    /// Y_i, kg_species / kg_mixture) into one of the human-friendly quantities the
    /// user can pick for Views, Detectors, and Monitors — %LFL, %UFL, ppm, ppb,
    /// mole fraction, mass concentration, thermal radiation, etc.
    ///
    /// Ambient density and mixture-molar-mass approximations: for dilute releases
    /// (Y_i &lt;&lt; 1) we use M_mix ≈ M_air = 28.97 g/mol and ρ_mix ≈ 1.205 kg/m³
    /// (standard atmosphere). At higher concentrations these would need real-time
    /// updates, but the dispersion solvers we target keep mass fractions well below
    /// 0.1 in the plume body where the user actually cares about the threshold.
    /// </summary>
    public static class FieldTransform
    {
        private const double MAirKgMol  = 0.02897;
        private const double RhoAir     = 1.205;       // kg/m³ at 15 °C, 101325 Pa

        /// <summary>True when the requested property needs the released-species mass
        /// fraction as the source field. False for fields like temperature, pressure,
        /// or thermal radiation that read a different field or are computed analytically.</summary>
        public static bool NeedsSpeciesField(ViewFieldProperty p)
        {
            switch (p)
            {
                case ViewFieldProperty.Concentration:
                case ViewFieldProperty.MassFraction:
                case ViewFieldProperty.MoleFraction:
                case ViewFieldProperty.ConcentrationKgM3:
                case ViewFieldProperty.ConcentrationPpm:
                case ViewFieldProperty.ConcentrationPpb:
                case ViewFieldProperty.PercentLFL:
                case ViewFieldProperty.PercentUFL:
                case ViewFieldProperty.FlashFireArrivalS:
                case ViewFieldProperty.FlashFireEnvelope:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the property is the per-triangle heat load on the plant
        /// surfaces ({time}_surface.bin), which a Surface view draws.</summary>
        public static bool IsSurface(ViewFieldProperty p)
            => p == ViewFieldProperty.SurfaceIncidentRadiationKwM2
            || p == ViewFieldProperty.SurfaceTotalHeatFluxKwM2
            || IsBlastSurface(p);

        /// <summary>True when the property is the per-triangle blast load ({time}_blast.bin).</summary>
        public static bool IsBlastSurface(ViewFieldProperty p)
            => p == ViewFieldProperty.SurfacePeakOverpressurePa || p == ViewFieldProperty.SurfaceImpulsePaS;

        /// <summary>True when the property is a combustion product or smoke field the
        /// simulation stored itself (soot, CO, CO2, O2, visibility); there is no analytic
        /// fallback for these.</summary>
        public static bool IsStoredOnly(ViewFieldProperty p)
            => p == ViewFieldProperty.SootDensityKgM3
            || p == ViewFieldProperty.COVolumeFraction
            || p == ViewFieldProperty.CO2VolumeFraction
            || p == ViewFieldProperty.O2VolumeFraction
            || p == ViewFieldProperty.VisibilityM;

        /// <summary>
        /// True when a simulation's species snapshots ({time}.bin, or OpenFOAM's field) are
        /// a concentration in kg/m³ rather than a mass fraction: the extender contract
        /// (<see cref="Extensibility.SolverExtenderResult.Result"/>, which imported results
        /// follow too) and the analytic engines (Gaussian puff and plume, the unified model)
        /// write kg/m³; OpenFOAM and the FluidX3D tracers write the mass fraction.
        /// </summary>
        public static bool StoresSpeciesAsKgPerM3(Simulation sim)
        {
            if (sim == null) return false;
            switch (sim.SolverType)
            {
                case CfdSolverType.GaussianPuff:
                case CfdSolverType.GaussianPlume:
                case CfdSolverType.UnifiedDispersion:
                case CfdSolverType.Extender:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A simulation's species snapshot as the mass fraction the transforms here take:
        /// kg/m³ divided by the ambient air density for solvers that store kg/m³
        /// (<see cref="StoresSpeciesAsKgPerM3"/>), unchanged otherwise. Returns a new array
        /// when it converts.
        /// </summary>
        public static double[,,] SpeciesAsMassFraction(double[,,] raw, Simulation sim)
        {
            if (raw == null || !StoresSpeciesAsKgPerM3(sim)) return raw;
            int nx = raw.GetLength(0), ny = raw.GetLength(1), nz = raw.GetLength(2);
            var y = new double[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        y[i, j, k] = raw[i, j, k] / RhoAir;
            return y;
        }

        /// <summary>The scalar form of <see cref="SpeciesAsMassFraction(double[,,], Simulation)"/>.</summary>
        public static double SpeciesAsMassFraction(double raw, Simulation sim)
            => StoresSpeciesAsKgPerM3(sim) ? raw / RhoAir : raw;

        /// <summary>True when the property is computed analytically from the scene,
        /// not sampled from the CFD result (e.g. thermal radiation from FireSources).</summary>
        public static bool IsAnalytic(ViewFieldProperty p)
            => p == ViewFieldProperty.ThermalRadiationKwM2
            || p == ViewFieldProperty.ThermalDose
            || p == ViewFieldProperty.FatalityProbability;

        /// <summary>True when the property is derived from a simulation result AND
        /// the scene: the concentration snapshot is loaded and transformed as usual,
        /// then burnt by <see cref="FlashFireEngine"/> using the scene's ignition.</summary>
        public static bool IsIgnitionDerived(ViewFieldProperty p)
            => p == ViewFieldProperty.FlashFireArrivalS
            || p == ViewFieldProperty.FlashFireEnvelope;

        /// <summary>Per-cell transform from raw mass fraction Y_i to the requested
        /// quantity. <paramref name="gas"/> must carry MolarMass, LFL, UFL (when the
        /// chosen quantity uses them). Returns the input array unchanged when
        /// <paramref name="target"/> is MassFraction / Concentration, to avoid an
        /// unnecessary allocation in the common case.</summary>
        public static double[,,] FromMassFraction(double[,,] yField, ViewFieldProperty target, GasProperties gas)
        {
            if (yField == null) return null;
            if (target == ViewFieldProperty.MassFraction || target == ViewFieldProperty.Concentration)
                return yField;

            int nx = yField.GetLength(0), ny = yField.GetLength(1), nz = yField.GetLength(2);
            double Mi = gas != null && gas.MolarMass > 0 ? gas.MolarMass : 0.029;
            double lfl = gas != null && gas.LFL > 0 ? gas.LFL : 0.033; // safe default (CH4)
            double ufl = gas != null && gas.UFL > 0 ? gas.UFL : 0.099;

            // mole_fraction = Y_i × M_mix / M_i, with M_mix ≈ M_air for dilute plumes.
            // ppm/ppb are volumetric (= mole) units.
            double molarRatio = MAirKgMol / Mi;

            var output = new double[nx, ny, nz];
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        double y = yField[i, j, k];
                        double v;
                        switch (target)
                        {
                            case ViewFieldProperty.MoleFraction:        v = y * molarRatio; break;
                            case ViewFieldProperty.ConcentrationKgM3:
                            case ViewFieldProperty.FlashFireArrivalS:
                            case ViewFieldProperty.FlashFireEnvelope:   v = y * RhoAir; break;
                            case ViewFieldProperty.ConcentrationPpm:    v = y * molarRatio * 1.0e6; break;
                            case ViewFieldProperty.ConcentrationPpb:    v = y * molarRatio * 1.0e9; break;
                            case ViewFieldProperty.PercentLFL:          v = (y * RhoAir / lfl) * 100.0; break;
                            case ViewFieldProperty.PercentUFL:          v = (y * RhoAir / ufl) * 100.0; break;
                            default:                                     v = y; break;
                        }
                        output[i, j, k] = v;
                    }
            return output;
        }

        /// <summary>Builds a thermal-radiation field (kW/m²) by summing the point-source
        /// model contribution of every active FireSource in the scene at each cell centre.
        /// Returns a fresh nx×ny×nz array.</summary>
        public static double[,,] BuildRadiationField(Scene3D scene, int nx, int ny, int nz, double halfM,
            double centerX = 0, double centerY = 0)
            => BuildRadiationField(scene, scene?.FireScenario?.Sources, nx, ny, nz, halfM, centerX, centerY);

        /// <summary>The same for some of the scene's fire sources only.</summary>
        private static double[,,] BuildRadiationField(Scene3D scene, IList<FireSource> sources,
            int nx, int ny, int nz, double halfM, double centerX, double centerY)
        {
            var output = new double[nx, ny, nz];
            if (sources == null || sources.Count == 0)
                return output;
            double dx = 2 * halfM / nx, dy = 2 * halfM / ny;
            double dz = halfM > 0 ? halfM / nz * 2.0 : 1.0; // matches BuildVisual's z scaling

            var meteo = ResolveMeteo(scene);
            double ambientT = meteo.AmbientTemperature;
            double humidity = meteo.RelativeHumidity;
            var receiverMode = scene.FireScenario.ReceiverMode;

            // Flame panels and emissive powers depend on the source and the wind, not on
            // the receiver, so they are resolved once here rather than per cell.
            var emitters = SolidFlameModel.PrepareAll(sources, meteo.WindVector);

            // Plant geometry casts radiation shadows. Null when the scene has no
            // decorations, which skips every occlusion test.
            var occluder = RayBoxIntersector.Occluder.From(SceneObstacles.Collect(scene));

            // One solid-flame cell costs ~100 panel evaluations against ~1 for the point
            // source, so a 60³ grid goes from ~10⁵ to ~10⁷ operations. Splitting on k
            // keeps that in the tenths of a second and the writes stay disjoint.
            System.Threading.Tasks.Parallel.For(0, nz, k =>
            {
                double z = (k + 0.5) * dz;
                for (int j = 0; j < ny; j++)
                {
                    double y = centerY - halfM + (j + 0.5) * dy;
                    for (int i = 0; i < nx; i++)
                    {
                        double x = centerX - halfM + (i + 0.5) * dx;
                        var receiver = new Geometry.Point3D(x, y, z);

                        // A cell inside solid geometry is not exposed at all.
                        if (occluder != null && occluder.Contains(x, y, z))
                        {
                            output[i, j, k] = 0;
                            continue;
                        }

                        double sumKwM2 = 0;

                        foreach (var src in sources)
                        {
                            if (src == null) continue;
                            // A fireball is its own sphere whatever the radiation model.
                            if (src.Kind == FireKind.Fireball)
                            {
                                sumKwM2 += FireballModel.FluxKwM2(src, receiver, receiverMode, ambientT, humidity, occluder);
                                continue;
                            }
                            if (src.RadiationModel != RadiationModel.PointSource) continue;
                            // The point source has one ray to block, from the source itself.
                            if (occluder != null && occluder.Blocks(receiver, src.Position)) continue;
                            double dxp = x - src.Position.X;
                            double dyp = y - src.Position.Y;
                            double dzp = z - src.Position.Z;
                            double r = Math.Sqrt(dxp * dxp + dyp * dyp + dzp * dzp);
                            sumKwM2 += JetFireModel.RadiationAtDistance(src, r) / 1000.0;
                        }

                        for (int e = 0; e < emitters.Count; e++)
                        {
                            sumKwM2 += SolidFlameModel.FluxKwM2(
                                emitters[e], receiver, receiverMode, ambientT, humidity, occluder);
                        }

                        output[i, j, k] = sumKwM2;
                    }
                }
            });

            return output;
        }

        /// <summary>
        /// The thermal radiation a simulation computed itself, kW/m²: the per-cell
        /// maximum over its <c>{time}_Q.bin</c> files. Null when it wrote none.
        /// </summary>
        public static double[,,] StoredRadiationField(Simulation sim, double halfM)
        {
            if (sim == null || string.IsNullOrEmpty(sim.CasePath)) return null;
            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60, ny = nx, nz = Math.Max(1, nx / 2);
            var result = OpenFoamResultReader.TryLoadFlatBinCase(sim.CasePath, ref nx, ref ny, ref nz, halfM,
                OpenFoamResultReader.RadiationChannel);
            if (result == null || !result.IsLoaded) return null;

            double[,,] peak = null;
            foreach (double t in result.TimeSteps)
            {
                var field = result.GetField(t);
                if (field == null) continue;
                if (peak == null) { peak = field; continue; }
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        for (int k = 0; k < nz; k++)
                            peak[i, j, k] = Math.Max(peak[i, j, k], field[i, j, k]);
            }
            return peak;
        }

        /// <summary>
        /// Builds whichever analytic field the property asks for. Radiation is the
        /// base: the field <paramref name="sim"/> computed itself when it wrote one
        /// (<see cref="StoredRadiationField"/>), else the analytic one from the scene's
        /// fire sources. Dose applies the scenario's exposure time to it, and the
        /// fatality probability runs the dose through the Eisenberg probit.
        /// </summary>
        public static double[,,] BuildAnalyticField(Scene3D scene, ViewFieldProperty property,
            int nx, int ny, int nz, double halfM, Simulation sim = null)
        {
            double cx = sim?.SnapshotDomainCenterX ?? 0, cy = sim?.SnapshotDomainCenterY ?? 0;
            double exposure = scene?.FireScenario?.ExposureTimeS ?? 20.0;
            var stored = StoredRadiationField(sim, halfM);
            if (property == ViewFieldProperty.ThermalRadiationKwM2)
                return stored ?? BuildRadiationField(scene, nx, ny, nz, halfM, cx, cy);

            double[,,] dose;
            if (stored != null)
                dose = StoredDoseField(sim, halfM, exposure);
            else
            {
                // A jet or pool burns for as long as the people stay; a fireball only for
                // its duration, so its dose takes the shorter of the two.
                var sources = scene?.FireScenario?.Sources ?? new List<FireSource>();
                var steady = sources.Where(s => s != null && s.Kind != FireKind.Fireball).ToList();
                dose = ThermalDose.BuildDoseField(BuildRadiationField(scene, steady, nx, ny, nz, halfM, cx, cy), exposure);
                foreach (var fireball in sources.Where(s => s != null && s.Kind == FireKind.Fireball))
                {
                    var flux = BuildRadiationField(scene, new[] { fireball }, nx, ny, nz, halfM, cx, cy);
                    var add = ThermalDose.BuildDoseField(flux, FireExposureS(fireball, exposure));
                    for (int i = 0; i < nx; i++)
                        for (int j = 0; j < ny; j++)
                            for (int k = 0; k < nz; k++)
                                dose[i, j, k] += add[i, j, k];
                }
            }
            return property == ViewFieldProperty.ThermalDose
                ? dose
                : ThermalDose.BuildFatalityField(dose);
        }

        /// <summary>
        /// How long a person is exposed to one fire source: the scene's escape time, cut
        /// to the burn duration for a fireball (<see cref="FireballModel.DurationS"/>).
        /// </summary>
        public static double FireExposureS(FireSource source, double escapeTimeS)
            => source != null && source.Kind == FireKind.Fireball
                ? Math.Min(escapeTimeS, FireballModel.DurationS(source.FireballMassKg))
                : escapeTimeS;

        /// <summary>
        /// The thermal dose from the radiation a simulation stored, per cell: the worst
        /// exposure window of the scene's length that starts at a stored time
        /// (<see cref="ThermalDose.WorstWindowDose"/>). A single snapshot is held for the
        /// whole window, as a steady fire is.
        /// </summary>
        public static double[,,] StoredDoseField(Simulation sim, double halfM, double exposureS)
        {
            if (sim == null || string.IsNullOrEmpty(sim.CasePath)) return null;
            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60, ny = nx, nz = Math.Max(1, nx / 2);
            var result = OpenFoamResultReader.TryLoadFlatBinCase(sim.CasePath, ref nx, ref ny, ref nz, halfM,
                OpenFoamResultReader.RadiationChannel);
            if (result == null || !result.IsLoaded) return null;

            var times = new List<double>();
            var fields = new List<double[,,]>();
            foreach (double t in result.TimeSteps)
            {
                var f = result.GetField(t);
                if (f == null) continue;
                times.Add(t);
                fields.Add(f);
            }
            if (fields.Count == 0) return null;

            var dose = new double[nx, ny, nz];
            System.Threading.Tasks.Parallel.For(0, nx, i =>
            {
                var flux = new double[fields.Count];
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        for (int n = 0; n < fields.Count; n++) flux[n] = fields[n][i, j, k];
                        dose[i, j, k] = ThermalDose.WorstWindowDose(times, flux, exposureS);
                    }
            });
            return dose;
        }

        /// <summary>Point sample of an analytic field, for monitors and detectors.</summary>
        public static double AnalyticAtPoint(Scene3D scene, ViewFieldProperty property,
            double x, double y, double z)
        {
            if (property == ViewFieldProperty.ThermalRadiationKwM2) return RadiationAtPoint(scene, x, y, z);

            double exposure = scene?.FireScenario?.ExposureTimeS ?? 20.0;
            double dose = 0;
            foreach (var src in scene?.FireScenario?.Sources ?? new List<FireSource>())
                if (src != null)
                    dose += ThermalDose.Dose(RadiationAtPoint(scene, new[] { src }, x, y, z), FireExposureS(src, exposure));
            return property == ViewFieldProperty.ThermalDose
                ? dose
                : ThermalDose.ProbitToProbability(ThermalDose.FatalityProbit(dose));
        }

        /// <summary>
        /// Meteorology backing the analytic radiation field: wind sets the flame tilt,
        /// ambient temperature and humidity set the atmospheric transmissivity. Follows
        /// the precedence the UI already uses — the first wind-field scenario, then the
        /// project defaults, then a standard atmosphere.
        /// </summary>
        public static MeteorologicalConditions ResolveMeteo(Scene3D scene)
        {
            if (scene?.WindFieldScenarios != null && scene.WindFieldScenarios.Count > 0
                && scene.WindFieldScenarios[0].Meteo != null)
                return scene.WindFieldScenarios[0].Meteo;
            if (scene?.GeneralSettings?.DefaultMeteo != null)
                return scene.GeneralSettings.DefaultMeteo;
            return new MeteorologicalConditions();
        }

        /// <summary>Scalar point-sample transform used by detectors and monitors.
        /// Converts a single Y_i value (or a temperature / radiation value) into the
        /// requested unit. Pass <paramref name="rawTemperatureK"/> only when the source
        /// field IS temperature — otherwise pass NaN.</summary>
        public static double ScalarFromMassFraction(double y, ViewFieldProperty target, GasProperties gas)
        {
            if (target == ViewFieldProperty.MassFraction || target == ViewFieldProperty.Concentration)
                return y;
            double Mi = gas != null && gas.MolarMass > 0 ? gas.MolarMass : 0.029;
            double lfl = gas != null && gas.LFL > 0 ? gas.LFL : 0.033;
            double ufl = gas != null && gas.UFL > 0 ? gas.UFL : 0.099;
            double molarRatio = MAirKgMol / Mi;
            switch (target)
            {
                case ViewFieldProperty.MoleFraction:        return y * molarRatio;
                case ViewFieldProperty.ConcentrationKgM3:   return y * RhoAir;
                case ViewFieldProperty.ConcentrationPpm:    return y * molarRatio * 1.0e6;
                case ViewFieldProperty.ConcentrationPpb:    return y * molarRatio * 1.0e9;
                case ViewFieldProperty.PercentLFL:          return (y * RhoAir / lfl) * 100.0;
                case ViewFieldProperty.PercentUFL:          return (y * RhoAir / ufl) * 100.0;
                default:                                     return y;
            }
        }

        /// <summary>Computes thermal radiation flux (kW/m²) at a single world-space
        /// point by summing every FireSource's point-model contribution.</summary>
        public static double RadiationAtPoint(Scene3D scene, double x, double y, double z)
            => RadiationAtPoint(scene, scene?.FireScenario?.Sources, x, y, z);

        /// <summary>The same for some of the scene's fire sources only.</summary>
        private static double RadiationAtPoint(Scene3D scene, IList<FireSource> sources, double x, double y, double z)
        {
            if (sources == null || sources.Count == 0)
                return 0;

            var meteo = ResolveMeteo(scene);
            var receiver = new Geometry.Point3D(x, y, z);
            var receiverMode = scene.FireScenario.ReceiverMode;
            var occluder = RayBoxIntersector.Occluder.From(SceneObstacles.Collect(scene));
            if (occluder != null && occluder.Contains(x, y, z)) return 0;

            double sumKwM2 = 0;

            foreach (var src in sources)
            {
                if (src == null) continue;
                if (src.Kind == FireKind.Fireball)
                {
                    sumKwM2 += FireballModel.FluxKwM2(src, receiver, receiverMode,
                        meteo.AmbientTemperature, meteo.RelativeHumidity, occluder);
                    continue;
                }

                if (src.RadiationModel == RadiationModel.SolidFlame)
                {
                    // One monitor or detector at a time: preparing the panels per call is
                    // cheap next to the grid sweep in BuildRadiationField.
                    var emitter = SolidFlameModel.Prepare(src, meteo.WindVector);
                    sumKwM2 += SolidFlameModel.FluxKwM2(emitter, receiver, receiverMode,
                        meteo.AmbientTemperature, meteo.RelativeHumidity, occluder);
                }
                else
                {
                    if (occluder != null && occluder.Blocks(receiver, src.Position)) continue;
                    double dx = x - src.Position.X;
                    double dy = y - src.Position.Y;
                    double dz = z - src.Position.Z;
                    double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    sumKwM2 += JetFireModel.RadiationAtDistance(src, r) / 1000.0;
                }
            }
            return sumKwM2;
        }

        /// <summary>
        /// Human-readable label for a field property. One list, so the two View editors
        /// stay in step with the enum instead of each carrying its own hardcoded set —
        /// which is how they came to offer only the first seven of nineteen values.
        /// </summary>
        public static string DisplayName(ViewFieldProperty p)
        {
            switch (p)
            {
                case ViewFieldProperty.Concentration:        return "Concentration (auto-resolves to source species)";
                case ViewFieldProperty.Temperature:          return "Temperature (T)";
                case ViewFieldProperty.WindSpeed:            return "Wind speed (|U|)";
                case ViewFieldProperty.Pressure:             return "Pressure (p_rgh)";
                case ViewFieldProperty.TurbulentK:           return "Turbulent k";
                case ViewFieldProperty.TurbulentEpsilon:     return "Turbulent epsilon";
                case ViewFieldProperty.TurbulentViscosity:   return "Turbulent viscosity (nut)";
                case ViewFieldProperty.MassFraction:         return "Mass fraction (kg/kg)";
                case ViewFieldProperty.MoleFraction:         return "Mole fraction (mol/mol)";
                case ViewFieldProperty.ConcentrationKgM3:    return "Concentration (kg/m³)";
                case ViewFieldProperty.ConcentrationPpm:     return "Concentration (ppm)";
                case ViewFieldProperty.ConcentrationPpb:     return "Concentration (ppb)";
                case ViewFieldProperty.PercentLFL:           return "% LFL";
                case ViewFieldProperty.PercentUFL:           return "% UFL";
                case ViewFieldProperty.ThermalRadiationKwM2: return "Thermal radiation (kW/m²) — fire sources";
                case ViewFieldProperty.FlashFireArrivalS:    return "Flash fire — flame arrival time (s)";
                case ViewFieldProperty.FlashFireEnvelope:    return "Flash fire — hazard envelope (iso 0.5)";
                case ViewFieldProperty.ThermalDose:          return "Thermal dose — fire sources";
                case ViewFieldProperty.FatalityProbability:  return "Fatality probability — fire sources";
                case ViewFieldProperty.SootDensityKgM3:      return "Soot (kg/m³)";
                case ViewFieldProperty.COVolumeFraction:     return "CO (volume fraction)";
                case ViewFieldProperty.CO2VolumeFraction:    return "CO2 (volume fraction)";
                case ViewFieldProperty.O2VolumeFraction:     return "O2 (volume fraction)";
                case ViewFieldProperty.VisibilityM:          return "Visibility (m)";
                case ViewFieldProperty.SurfaceIncidentRadiationKwM2: return "Surface — incident radiation (kW/m²)";
                case ViewFieldProperty.SurfaceTotalHeatFluxKwM2:     return "Surface — total heat flux (kW/m²)";
                case ViewFieldProperty.OverpressurePa:       return "Explosion — overpressure (Pa)";
                case ViewFieldProperty.PeakOverpressurePa:   return "Explosion — peak overpressure (Pa)";
                case ViewFieldProperty.ImpulsePaS:           return "Explosion — impulse (Pa·s)";
                case ViewFieldProperty.FlameArrivalS:        return "Explosion — flame arrival time (s)";
                case ViewFieldProperty.SurfacePeakOverpressurePa: return "Surface — peak overpressure (Pa)";
                case ViewFieldProperty.SurfaceImpulsePaS:    return "Surface — impulse (Pa·s)";
                default:                                     return p.ToString();
            }
        }

        /// <summary>Units suffix shown in legends / detector readouts. Matches the
        /// numeric value returned by <see cref="FromMassFraction"/>.</summary>
        public static string UnitFor(ViewFieldProperty p)
        {
            switch (p)
            {
                case ViewFieldProperty.MoleFraction:        return "mol/mol";
                case ViewFieldProperty.MassFraction:
                case ViewFieldProperty.Concentration:       return "kg/kg";
                case ViewFieldProperty.ConcentrationKgM3:   return "kg/m³";
                case ViewFieldProperty.ConcentrationPpm:    return "ppm";
                case ViewFieldProperty.ConcentrationPpb:    return "ppb";
                case ViewFieldProperty.PercentLFL:          return "%LFL";
                case ViewFieldProperty.PercentUFL:          return "%UFL";
                case ViewFieldProperty.Temperature:         return "K";
                case ViewFieldProperty.WindSpeed:           return "m/s";
                case ViewFieldProperty.Pressure:            return "Pa";
                case ViewFieldProperty.TurbulentK:          return "m²/s²";
                case ViewFieldProperty.TurbulentEpsilon:    return "m²/s³";
                case ViewFieldProperty.TurbulentViscosity:  return "m²/s";
                case ViewFieldProperty.ThermalRadiationKwM2: return "kW/m²";
                case ViewFieldProperty.FlashFireArrivalS:    return "s";
                case ViewFieldProperty.FlashFireEnvelope:    return "";
                case ViewFieldProperty.ThermalDose:          return "(W/m²)^4/3·s";
                case ViewFieldProperty.FatalityProbability:  return "";
                case ViewFieldProperty.SootDensityKgM3:      return "kg/m³";
                case ViewFieldProperty.COVolumeFraction:
                case ViewFieldProperty.CO2VolumeFraction:
                case ViewFieldProperty.O2VolumeFraction:     return "mol/mol";
                case ViewFieldProperty.VisibilityM:          return "m";
                case ViewFieldProperty.SurfaceIncidentRadiationKwM2:
                case ViewFieldProperty.SurfaceTotalHeatFluxKwM2: return "kW/m²";
                case ViewFieldProperty.OverpressurePa:
                case ViewFieldProperty.PeakOverpressurePa:
                case ViewFieldProperty.SurfacePeakOverpressurePa: return "Pa";
                case ViewFieldProperty.ImpulsePaS:
                case ViewFieldProperty.SurfaceImpulsePaS:    return "Pa·s";
                case ViewFieldProperty.FlameArrivalS:        return "s";
                default:                                     return "";
            }
        }
    }
}
