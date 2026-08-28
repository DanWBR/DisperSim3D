#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Cross-platform save half of the .dsproj XML round-trip. Engine-resident
    /// twin of <see cref="SceneFileLoader"/>; mirrors every <c>Deserialize*</c>
    /// helper there with a corresponding <c>Serialize*</c> here. The WinForms
    /// <c>Scene3DEditorControl</c> used to host this code (with <c>_scene.*</c>
    /// references and progress events). All UI-side responsibilities (progress
    /// fan-out, .dsproj bundle wrap-up) now live in a thin wrapper there; the
    /// pure XML writing lives here so the Avalonia shell can save projects
    /// without any WPF dependency.
    /// </summary>
    public static class SceneFileSaver
    {
        /// <summary>Progress callback signature: <c>(step, fraction[, done])</c>.
        /// Fraction is in [0,1] and may be <see cref="double.NaN"/> for
        /// terminal failure states.</summary>
        public delegate void ProgressCallback(string step, double fraction, bool done);

        /// <summary>Serialises <paramref name="scene"/> to <paramref name="filePath"/>.
        /// Backs up an existing file as <c>.bak</c> beforehand (best-effort).
        /// For .dsproj bundle files, the caller handles the zip step — pass a
        /// non-null <paramref name="bundleWriter"/> to delegate to
        /// <see cref="ProjectBundle.Save"/>; for plain .xml files we just
        /// write the XDocument.</summary>
        public static void Save(Scene3D scene, string filePath,
            ProgressCallback? progress = null,
            Action<string, Scene3D, XDocument, Action<string, double>>? bundleWriter = null)
        {
            progress?.Invoke("Building project XML...", 0.05, false);
            XDocument doc = BuildSceneXDocument(scene, filePath);

            progress?.Invoke("Backing up previous file...", 0.10, false);
            BackupExistingProjectFile(filePath);

            if (bundleWriter != null && ProjectBundle.IsBundleFile(filePath))
            {
                bundleWriter(filePath, scene, doc, (step, frac) =>
                    progress?.Invoke(step, 0.15 + 0.80 * Math.Max(0, Math.Min(1, frac)), false));
            }
            else
            {
                progress?.Invoke("Writing XML...", 0.50, false);
                doc.Save(filePath);
            }
            progress?.Invoke("Saved: " + filePath, 1.0, true);
        }

        // ── Top-level XDocument builder ─────────────────────────────────────
        public static XDocument BuildSceneXDocument(Scene3D scene, string filePath)
        {
            var inv = CultureInfo.InvariantCulture;
            return new XDocument(
                new XElement("Scene3D",
                    new XAttribute("Version", "1"),
                    new XAttribute("Name", scene.Name ?? ""),
                    new XAttribute("Description", scene.Description ?? ""),

                    new XElement("GridSettings",
                        new XAttribute("Spacing", scene.GridSpacing.ToString(inv)),
                        new XAttribute("SnapToGrid", scene.SnapToGrid)),

                    new XElement("WorkPlanes",
                        scene.WorkPlanes.Select(wp =>
                            new XElement("WorkPlane",
                                new XAttribute("Name", wp.Name ?? ""),
                                new XAttribute("Elevation", wp.Elevation.ToString(inv)),
                                new XAttribute("Visible", wp.Visible),
                                new XAttribute("GridColor", wp.GridColor.ToString()),
                                new XAttribute("GridSpacing", wp.GridSpacing.ToString(inv))))),

                    new XElement("CurrentWorkPlane",
                        new XAttribute("Name", scene.CurrentWorkPlane != null
                            ? scene.CurrentWorkPlane.Name ?? "" : "")),

                    new XElement("Decorations",
                        scene.Decorations.Select(d =>
                            new XElement("Decoration",
                                new XAttribute("Id", d.Id),
                                new XAttribute("Name", d.Name ?? ""),
                                new XAttribute("FilePath", d.FilePath ?? ""),
                                new XAttribute("TexturePath", d.TexturePath ?? ""),
                                new XAttribute("PosX", d.Position.X.ToString(inv)),
                                new XAttribute("PosY", d.Position.Y.ToString(inv)),
                                new XAttribute("PosZ", d.Position.Z.ToString(inv)),
                                new XAttribute("RotX", d.Rotation.X.ToString(inv)),
                                new XAttribute("RotY", d.Rotation.Y.ToString(inv)),
                                new XAttribute("RotZ", d.Rotation.Z.ToString(inv)),
                                new XAttribute("Scale", d.Scale.ToString(inv)),
                                new XAttribute("ClipEnabled", d.ClipEnabled.ToString()),
                                new XAttribute("ClipAxis", d.ClipAxis.ToString()),
                                new XAttribute("ClipValue", d.ClipValue.ToString(inv)),
                                new XAttribute("ClipAbove", d.ClipAbove.ToString()),
                                new XAttribute("UseCustomMaterial", d.UseCustomMaterial.ToString()),
                                new XAttribute("MaterialType", d.MaterialType.ToString()),
                                new XAttribute("MaterialColor", d.MaterialColor.ToString()),
                                new XAttribute("SpecularPower", d.SpecularPower.ToString(inv)),
                                new XAttribute("Opacity", d.Opacity.ToString(inv)),
                                new XAttribute("IsVisible", d.IsVisible.ToString())))),

                    SerializeGeneralSettings(scene, inv),
                    SerializeEnvironment(scene, inv),
                    SerializeGasLibrary(scene, inv),
                    SerializeTopLevelSources(scene, inv),
                    SerializeWindFieldScenarios(scene, inv),
                    SerializeSimulations(scene, inv),
                    SerializeViews(scene, inv),
                    SerializeDispersionStudies(scene, inv),
                    SerializeDetectorAllocations(scene, inv),
                    SerializeDispersionScenarios(scene, inv),
                    SerializeMonitorPoints(scene, inv),
                    SerializeWindRose(scene, inv),
                    SerializeFireScenario(scene, inv),
                SerializeIgnitions(scene, inv),
                    SerializeGasDetectors(scene, inv),
                    SerializeCfdSimulations(scene, inv, filePath)
                ));
        }

        // ── Pre-save backup ──────────────────────────────────────────────────
        private static void BackupExistingProjectFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return;
                if (!File.Exists(filePath)) return;
                File.Copy(filePath, filePath + ".bak", overwrite: true);
            }
            catch { /* best-effort */ }
        }

        // ── Section serialisers ─────────────────────────────────────────────
        private static XElement? SerializeEnvironment(Scene3D scene, CultureInfo inv)
        {
            var e = scene.Environment;
            if (e == null) return null;
            return new XElement("Environment",
                new XAttribute("UseSunLighting", e.UseSunLighting.ToString()),
                new XAttribute("SunAzimuthDeg", e.SunAzimuthDeg.ToString(inv)),
                new XAttribute("SunElevationDeg", e.SunElevationDeg.ToString(inv)),
                new XAttribute("SunIntensity", e.SunIntensity.ToString(inv)),
                new XAttribute("AmbientIntensity", e.AmbientIntensity.ToString(inv)),
                new XAttribute("UseSolarClock", e.UseSolarClock.ToString()),
                new XAttribute("Latitude", e.Latitude.ToString(inv)),
                new XAttribute("DayOfYear", e.DayOfYear.ToString(inv)),
                new XAttribute("TimeOfDayHours", e.TimeOfDayHours.ToString(inv)),
                new XAttribute("SkydomeEnabled", e.SkydomeEnabled.ToString()),
                new XAttribute("SkyZenith", e.SkyZenithColor.ToString()),
                new XAttribute("SkyHorizon", e.SkyHorizonColor.ToString()),
                new XAttribute("Ground", e.Ground.ToString()),
                new XAttribute("ShowGridOverlay", e.ShowGridOverlay.ToString()),
                new XAttribute("ShowClouds", e.ShowClouds.ToString()),
                new XAttribute("CloudSpeed", e.CloudSpeed.ToString(inv)),
                new XAttribute("ShowGrassBlades", e.ShowGrassBlades.ToString()),
                new XAttribute("GrassBladeCount", e.GrassBladeCount.ToString(inv)),
                new XAttribute("SkyTexturePath", e.SkyTexturePath ?? ""),
                new XAttribute("GroundTexturePath", e.GroundTexturePath ?? ""),
                new XAttribute("GroundTextureTileSize", e.GroundTextureTileSize.ToString(inv)),
                new XAttribute("GridMinorSpacing", e.GridMinorSpacing.ToString(inv)),
                new XAttribute("GridMajorSpacing", e.GridMajorSpacing.ToString(inv)),
                new XAttribute("GridHalfSize", e.GridHalfSize.ToString(inv)),
                new XAttribute("ShadowsEnabled", e.ShadowsEnabled.ToString()),
                new XAttribute("FogEnabled", e.FogEnabled.ToString()),
                new XAttribute("FogDensity", e.FogDensity.ToString(inv)),
                new XAttribute("SkyTextureBrightness", e.SkyTextureBrightness.ToString(inv)),
                new XAttribute("SkyTextureVOffset", e.SkyTextureVOffset.ToString(inv)),
                new XAttribute("NightMode", e.NightMode.ToString()),
                new XAttribute("MoonAzimuthDeg", e.MoonAzimuthDeg.ToString(inv)),
                new XAttribute("MoonElevationDeg", e.MoonElevationDeg.ToString(inv)),
                new XAttribute("MoonIntensity", e.MoonIntensity.ToString(inv)),
                new XAttribute("ShowStars", e.ShowStars.ToString()));
        }

        private static XElement? SerializeGeneralSettings(Scene3D scene, CultureInfo inv)
        {
            var s = scene.GeneralSettings;
            if (s == null) return null;
            return new XElement("GeneralSettings",
                new XAttribute("Name", s.Name ?? ""),
                new XAttribute("Description", s.Description ?? ""),
                new XAttribute("Author", s.Author ?? ""),
                new XAttribute("CreatedAt", s.CreatedAt.ToString("o", inv)),
                new XAttribute("DefaultDomainSize", s.DefaultDomainSizeM.ToString(inv)),
                new XAttribute("DefaultGridRes", s.DefaultGridResolution.ToString(inv)),
                SerializeMeteo("DefaultMeteo", s.DefaultMeteo, inv));
        }

        private static XElement? SerializeGasLibrary(Scene3D scene, CultureInfo inv)
        {
            if (scene.GasLibrary == null || scene.GasLibrary.Count == 0) return null;
            return new XElement("GasLibrary",
                scene.GasLibrary.Select(g =>
                {
                    if (g.Kind == GasLibraryItemKind.Mixture && g.Mixture != null)
                    {
                        return new XElement("Gas",
                            new XAttribute("Id", g.Id ?? ""),
                            new XAttribute("Name", g.Name ?? ""),
                            new XAttribute("Kind", "Mixture"),
                            new XAttribute("Cryogenic", g.IsCryogenic ? "1" : "0"),
                            new XElement("Mixture",
                                g.Mixture.Components.Select(c =>
                                    new XElement("Component",
                                        new XAttribute("Name", c.Name ?? ""),
                                        new XAttribute("MolarMass", c.MolarMass.ToString(inv)),
                                        new XAttribute("MoleFrac", c.MoleFraction.ToString(inv)),
                                        new XAttribute("LFL", c.LFL.ToString(inv)),
                                        new XAttribute("UFL", c.UFL.ToString(inv)),
                                        new XAttribute("IDLH", c.IDLH.ToString(inv))))));
                    }
                    var gp = g.PureGas ?? new GasProperties();
                    return new XElement("Gas",
                        new XAttribute("Id", g.Id ?? ""),
                        new XAttribute("Name", g.Name ?? ""),
                        new XAttribute("Kind", "Pure"),
                        new XAttribute("Cryogenic", g.IsCryogenic ? "1" : "0"),
                        new XAttribute("MolarMass", gp.MolarMass.ToString(inv)),
                        new XAttribute("LFL", gp.LFL.ToString(inv)),
                        new XAttribute("IDLH", gp.IDLH.ToString(inv)),
                        new XAttribute("ERPG1", gp.ERPG1.ToString(inv)),
                        new XAttribute("ERPG2", gp.ERPG2.ToString(inv)),
                        new XAttribute("ERPG3", gp.ERPG3.ToString(inv)));
                }));
        }

        private static XElement? SerializeTopLevelSources(Scene3D scene, CultureInfo inv)
        {
            if (scene.TopLevelSources == null || scene.TopLevelSources.Count == 0) return null;
            return new XElement("TopLevelSources",
                scene.TopLevelSources.Select(src => SerializeSourceCommon(src, inv)));
        }

        private static XElement SerializeSourceCommon(ReleaseSource3D src, CultureInfo inv)
        {
            XElement? invEl = null;
            if (src.EquipmentInventory != null && src.EquipmentInventory.Count > 0)
            {
                invEl = new XElement("Inventory",
                    src.EquipmentInventory.Select(it => new XElement("I",
                        new XAttribute("Type", it.Type.ToString()),
                        new XAttribute("DiamMm", it.NominalDiameterMm.ToString(inv)),
                        new XAttribute("Count", it.Count.ToString(inv)),
                        new XAttribute("Note", it.Note ?? ""))));
            }
            return new XElement("Source",
                new XAttribute("Id", src.Id ?? ""),
                new XAttribute("Name", src.Name ?? ""),
                new XAttribute("AttachedUnitId", src.AttachedUnitId ?? ""),
                new XAttribute("GasRefId", src.GasRefId ?? ""),
                new XAttribute("PosX", src.Position.X.ToString(inv)),
                new XAttribute("PosY", src.Position.Y.ToString(inv)),
                new XAttribute("PosZ", src.Position.Z.ToString(inv)),
                new XAttribute("ReleaseRate", src.ReleaseRateKgPerS.ToString(inv)),
                new XAttribute("PuffInterval", src.PuffIntervalS.ToString(inv)),
                new XAttribute("HeightOffset", src.ReleaseHeightOffset.ToString(inv)),
                new XAttribute("Azimuth", src.ReleaseAzimuthDeg.ToString(inv)),
                new XAttribute("Elevation", src.ReleaseElevationDeg.ToString(inv)),
                new XAttribute("HoleSize", src.HoleSizeBand.ToString()),
                new XAttribute("AutoLeakFreq", src.AutoComputeLeakFrequency.ToString()),
                new XAttribute("LeakFreq", src.LeakFrequencyPerYear.ToString(inv)),
                src.HighPressureLeak != null ? new XElement("HPLeak",
                    new XAttribute("VesselP", src.HighPressureLeak.VesselPressurePa.ToString(inv)),
                    new XAttribute("VesselT", src.HighPressureLeak.VesselTemperatureK.ToString(inv)),
                    new XAttribute("Orifice", src.HighPressureLeak.OrificeDiameterM.ToString(inv)),
                    new XAttribute("Volume", src.HighPressureLeak.VesselVolumeM3.ToString(inv)),
                    new XAttribute("Gamma", src.HighPressureLeak.GasGamma.ToString(inv)),
                    new XAttribute("MolarMass", src.HighPressureLeak.GasMolarMassKgMol.ToString(inv)),
                    new XAttribute("Cd", src.HighPressureLeak.DischargeCoefficient.ToString(inv))) : null,
                src.Gas != null ? new XElement("Gas",
                    new XAttribute("Name", src.Gas.Name ?? ""),
                    new XAttribute("MolarMass", src.Gas.MolarMass.ToString(inv)),
                    new XAttribute("LFL", src.Gas.LFL.ToString(inv)),
                    new XAttribute("IDLH", src.Gas.IDLH.ToString(inv))) : null,
                invEl);
        }

        private static XElement? SerializeSimulations(Scene3D scene, CultureInfo inv)
        {
            if (scene.Simulations == null || scene.Simulations.Count == 0) return null;
            return new XElement("Simulations",
                scene.Simulations.Select(s => new XElement("Simulation",
                    new XAttribute("Id", s.Id ?? ""),
                    new XAttribute("Name", s.Name ?? ""),
                    new XAttribute("CreatedAt", s.CreatedAt.ToString("o", inv)),
                    s.CompletedAt.HasValue ? new XAttribute("CompletedAt", s.CompletedAt.Value.ToString("o", inv)) : null,
                    new XAttribute("SourceId", s.SourceId ?? ""),
                    new XAttribute("WindFieldId", s.WindFieldId ?? ""),
                    new XAttribute("SolverType", s.SolverType.ToString()),
                    new XAttribute("Status", s.Status.ToString()),
                    new XAttribute("StatusMessage", s.StatusMessage ?? ""),
                    new XAttribute("DomainSize", s.SnapshotDomainSizeM.ToString(inv)),
                    new XAttribute("GridRes", s.SnapshotGridResolution.ToString(inv)),
                    new XAttribute("Duration", s.SnapshotDurationS.ToString(inv)),
                    new XAttribute("TimeStep", s.SnapshotTimeStepS.ToString(inv)),
                    new XAttribute("SnapshotCount", s.SnapshotCount.ToString(inv)),
                    new XAttribute("CasePath", s.CasePath ?? ""),
                    new XAttribute("EmbedMode", s.EmbedMode.ToString()),
                    new XAttribute("MaxC", s.MaxConcentration.ToString(inv)),
                    s.SnapshotSource != null
                        ? new XElement("SnapshotSource",
                            SerializeSourceCommon(s.SnapshotSource, inv).Attributes(),
                            SerializeSourceCommon(s.SnapshotSource, inv).Elements())
                        : null,
                    SerializeMeteo("SnapshotMeteo", s.SnapshotMeteo, inv),
                    SerializeAtmosphericCfd(s.SnapshotCfdConfig, inv))));
        }

        private static XElement? SerializeViews(Scene3D scene, CultureInfo inv)
        {
            if (scene.Views == null || scene.Views.Count == 0) return null;
            return new XElement("Views",
                scene.Views.Select(v => new XElement("View",
                    new XAttribute("Id", v.Id ?? ""),
                    new XAttribute("Name", v.Name ?? ""),
                    new XAttribute("Kind", v.Kind.ToString()),
                    new XAttribute("SimulationId", v.SimulationId ?? ""),
                    new XAttribute("FieldProperty", v.FieldProperty.ToString()),
                    new XAttribute("TimeMode", v.TimeMode.ToString()),
                    new XAttribute("SpecificTime", v.SpecificTimeS.ToString(inv)),
                    new XAttribute("IsVisible", v.IsVisible.ToString()),
                    new XAttribute("Opacity", v.Opacity.ToString(inv)),
                    new XAttribute("IsoValue", v.IsoValue.ToString(inv)),
                    new XAttribute("IsoColor", v.IsoColor.ToString()),
                    new XAttribute("CloudAppearance", v.UseCloudAppearance),
                    new XAttribute("CloudColor", v.CloudColor.ToString()),
                    new XAttribute("PlanePosition", v.PlanePosition.ToString(inv)),
                    new XAttribute("ColorMap", v.ColorMap.ToString()),
                    new XAttribute("MinValue", v.MinValue.ToString(inv)),
                    new XAttribute("MaxValue", v.MaxValue.ToString(inv)),
                    new XAttribute("SampleResolution", v.SampleResolution.ToString(inv)))));
        }

        private static XElement? SerializeDispersionStudies(Scene3D scene, CultureInfo inv)
        {
            if (scene.DispersionStudies == null || scene.DispersionStudies.Count == 0) return null;
            return new XElement("DispersionStudies",
                scene.DispersionStudies.Select(st => new XElement("Study",
                    new XAttribute("Id", st.Id ?? ""),
                    new XAttribute("Name", st.Name ?? ""),
                    new XAttribute("Description", st.Description ?? ""),
                    new XAttribute("DetectionQuantity", st.DetectionQuantity.ToString()),
                    new XAttribute("DetectionThreshold", st.DetectionThreshold.ToString(inv)),
                    new XAttribute("CreatedAt", st.CreatedAt.ToString("o")),
                    new XAttribute("IsVisible", st.IsVisible.ToString()),
                    new XElement("Simulations",
                        (st.SimulationIds ?? new List<string>()).Select(sid =>
                            new XElement("Simulation", new XAttribute("Id", sid)))),
                    (st.RiskWeights != null && st.RiskWeights.Count > 0)
                        ? new XElement("RiskWeights",
                            st.RiskWeights.Select(kv => new XElement("R",
                                new XAttribute("SimId", kv.Key ?? ""),
                                new XAttribute("FreqMode", kv.Value.FreqMode.ToString()),
                                new XAttribute("FreqValue", kv.Value.FreqPerYear.ToString(inv)),
                                new XAttribute("ConsMode", kv.Value.ConsMode.ToString()),
                                new XAttribute("ConsValue", kv.Value.Consequence.ToString(inv)))))
                        : null)));
        }

        private static XElement? SerializeDetectorAllocations(Scene3D scene, CultureInfo inv)
        {
            if (scene.DetectorAllocations == null || scene.DetectorAllocations.Count == 0) return null;
            return new XElement("DetectorAllocations",
                scene.DetectorAllocations.Select(a => new XElement("Allocation",
                    new XAttribute("Id", a.Id ?? ""),
                    new XAttribute("Name", a.Name ?? ""),
                    new XAttribute("DispersionStudyId", a.DispersionStudyId ?? ""),
                    new XAttribute("Objective", a.Objective.ToString()),
                    new XAttribute("TargetCoveragePercent", a.TargetCoveragePercent.ToString(inv)),
                    new XAttribute("MaxDetectors", a.MaxDetectors.ToString(inv)),
                    new XAttribute("DetectionRadiusM", a.DetectionRadiusM.ToString(inv)),
                    new XAttribute("MinZ", a.MinZ.ToString(inv)),
                    new XAttribute("MaxZ", a.MaxZ.ToString(inv)),
                    new XAttribute("CandidateNx", a.CandidateNx.ToString(inv)),
                    new XAttribute("CandidateNy", a.CandidateNy.ToString(inv)),
                    new XAttribute("CandidateNz", a.CandidateNz.ToString(inv)),
                    new XAttribute("UseExistingDetectors", a.UseExistingDetectors.ToString()),
                    new XAttribute("Strategy", a.Strategy.ToString()),
                    new XAttribute("DetectionProbability", a.DetectionProbability.ToString(inv)),
                    new XAttribute("UseDistanceWeighting", a.UseDistanceWeighting.ToString()),
                    new XAttribute("DistanceWeightMin", a.DistanceWeightMin.ToString(inv)),
                    new XAttribute("DistanceWeightMax", a.DistanceWeightMax.ToString(inv)),
                    new XAttribute("AchievedCoveragePercent", a.AchievedCoveragePercent.ToString(inv)),
                    new XAttribute("TotalRisk", a.TotalRisk.ToString(inv)),
                    new XAttribute("ResidualRisk", a.ResidualRisk.ToString(inv)),
                    new XAttribute("RiskReductionFraction", a.RiskReductionFraction.ToString(inv)),
                    new XAttribute("Status", a.Status.ToString()),
                    new XAttribute("StatusMessage", a.StatusMessage ?? ""),
                    new XAttribute("RunAt", a.RunAt.ToString("o")),
                    new XAttribute("IsVisible", a.IsVisible.ToString()),
                    new XElement("Positions",
                        (a.AllocatedPositions ?? new List<Point3D>()).Select(p =>
                            new XElement("P",
                                new XAttribute("X", p.X.ToString(inv)),
                                new XAttribute("Y", p.Y.ToString(inv)),
                                new XAttribute("Z", p.Z.ToString(inv))))),
                    new XElement("Coverage",
                        (a.PerCloudCovered ?? new Dictionary<string, bool>()).Select(kv =>
                            new XElement("C",
                                new XAttribute("SimId", kv.Key),
                                new XAttribute("Covered", kv.Value.ToString())))),
                    (a.PerCloudResidualRisk != null && a.PerCloudResidualRisk.Count > 0)
                        ? new XElement("ResidualRisks",
                            a.PerCloudResidualRisk.Select(kv => new XElement("R",
                                new XAttribute("SimId", kv.Key),
                                new XAttribute("R", kv.Value.ToString(inv)))))
                        : null,
                    (a.RiskCurveK != null && a.RiskCurveK.Count > 0)
                        ? new XElement("RiskCurve",
                            Enumerable.Range(0, Math.Min(a.RiskCurveK.Count, a.RiskCurveRRF?.Count ?? 0))
                                .Select(i => new XElement("P",
                                    new XAttribute("K", a.RiskCurveK[i].ToString(inv)),
                                    new XAttribute("RRF", a.RiskCurveRRF[i].ToString(inv)))))
                        : null)));
        }

        private static XElement? SerializeWindFieldScenarios(Scene3D scene, CultureInfo inv)
        {
            if (scene.WindFieldScenarios == null || scene.WindFieldScenarios.Count == 0) return null;
            return new XElement("WindFieldScenarios",
                scene.WindFieldScenarios.Select(wf =>
                    new XElement("WindFieldScenario",
                        new XAttribute("Id", wf.Id ?? ""),
                        new XAttribute("Name", wf.Name ?? ""),
                        new XAttribute("DomainSize", wf.DomainSizeM.ToString(inv)),
                        new XAttribute("DomainHeight", wf.DomainHeightM.ToString(inv)),
                        new XAttribute("GridRes", wf.GridResolution.ToString(inv)),
                        new XAttribute("Status", wf.Status.ToString()),
                        new XAttribute("CasePath", wf.CasePath ?? ""),
                        new XAttribute("EmbedMode", wf.EmbedMode.ToString()),
                        new XAttribute("UseFluidX3D", wf.UseFluidX3D.ToString()),
                        new XAttribute("FluidX3DQuality", wf.FluidX3DQuality.ToString()),
                        new XAttribute("FluidX3DGroundBC", wf.FluidX3DGroundBC.ToString()),
                        new XAttribute("IsVisible", wf.IsVisible.ToString()),
                        SerializeMeteo("Meteo", wf.Meteo, inv),
                        SerializeAtmosphericCfd(wf.CfdConfig, inv))));
        }

        private static XElement? SerializeAtmosphericCfd(CfdConfiguration? cfd, CultureInfo inv)
        {
            if (cfd == null) return null;
            var el = new XElement("Cfd",
                new XAttribute("AtmBL", cfd.UseAtmosphericBL ? "1" : "0"),
                new XAttribute("Sct", cfd.TurbulentSchmidtNumber.ToString(inv)),
                new XAttribute("Prt", cfd.TurbulentPrandtlNumber.ToString(inv)),
                new XAttribute("SigmaEps", cfd.KEpsilonSigmaEpsilon.ToString(inv)),
                new XAttribute("GroundBC", cfd.GroundThermalBC.ToString()),
                new XAttribute("GroundT", cfd.GroundTemperatureK.ToString(inv)),
                new XAttribute("GroundQ", cfd.GroundHeatFluxWPerM2.ToString(inv)));
            if (cfd.BuoyancyEpsCoefficient.HasValue)
                el.Add(new XAttribute("Ceps3", cfd.BuoyancyEpsCoefficient.Value.ToString(inv)));
            if (cfd.UsePatchedSctSolver)
            {
                el.Add(new XAttribute("PatchedSct", "1"));
                if (!string.IsNullOrEmpty(cfd.PatchedSctSolverBinary))
                    el.Add(new XAttribute("PatchedBin", cfd.PatchedSctSolverBinary));
                if (!string.IsNullOrEmpty(cfd.PatchedSctSolverWslDistro))
                    el.Add(new XAttribute("PatchedDistro", cfd.PatchedSctSolverWslDistro));
                if (!string.IsNullOrEmpty(cfd.PatchedSctSolverBashrc))
                    el.Add(new XAttribute("PatchedBashrc", cfd.PatchedSctSolverBashrc));
            }
            if (cfd.UseVu2019MeshRefinement)
                el.Add(new XAttribute("VuMesh", "1"));
            if (cfd.UseAblPrecursor)
            {
                el.Add(new XAttribute("AblPrec", "1"));
                if (cfd.AblPrecursorIterations > 0 && cfd.AblPrecursorIterations != 500)
                    el.Add(new XAttribute("AblPrecIters", cfd.AblPrecursorIterations.ToString(inv)));
            }
            if (cfd.UseCryogenicPatchInjection)
                el.Add(new XAttribute("CryoPatch", "1"));
            // Always emit so user's explicit OFF persists across saves
            // (default is true, so missing attribute reads back as true).
            el.Add(new XAttribute("GpuTracer", cfd.UseGpuBuoyantTracer ? "1" : "0"));
            return el;
        }

        private static XElement? SerializeDispersionScenarios(Scene3D scene, CultureInfo inv)
        {
            if (scene.DispersionScenarios.Count == 0) return null;
            return new XElement("DispersionScenarios",
                new XAttribute("ActiveIndex", scene.ActiveScenarioIndex.ToString(inv)),
                scene.DispersionScenarios.Select(sc => SerializeSingleScenario(sc, inv)));
        }

        private static XElement SerializeSingleScenario(DispersionScenario sc, CultureInfo inv)
        {
            return new XElement("DispersionScenario",
                new XAttribute("Name", sc.Name ?? ""),
                new XAttribute("Duration", sc.SimulationDurationS.ToString(inv)),
                new XAttribute("TimeStep", sc.TimeStepS.ToString(inv)),
                new XAttribute("DomainSize", sc.DomainSizeM.ToString(inv)),
                new XAttribute("GridRes", sc.GridResolution.ToString(inv)),
                new XAttribute("SolverType", sc.SolverType.ToString()),
                new XAttribute("WindFieldId", sc.WindFieldScenarioId ?? ""),

                SerializeMeteo("Meteo", sc.Meteo, inv),

                new XElement("Sources",
                    sc.Sources.Select(src =>
                        new XElement("Source",
                            new XAttribute("Id", src.Id),
                            new XAttribute("Name", src.Name ?? ""),
                            new XAttribute("AttachedUnitId", src.AttachedUnitId ?? ""),
                            new XAttribute("PosX", src.Position.X.ToString(inv)),
                            new XAttribute("PosY", src.Position.Y.ToString(inv)),
                            new XAttribute("PosZ", src.Position.Z.ToString(inv)),
                            new XAttribute("ReleaseRate", src.ReleaseRateKgPerS.ToString(inv)),
                            new XAttribute("PuffInterval", src.PuffIntervalS.ToString(inv)),
                            new XAttribute("HeightOffset", src.ReleaseHeightOffset.ToString(inv)),
                            new XAttribute("Azimuth", src.ReleaseAzimuthDeg.ToString(inv)),
                            new XAttribute("Elevation", src.ReleaseElevationDeg.ToString(inv)),
                            src.HighPressureLeak != null ? new XElement("HPLeak",
                                new XAttribute("VesselP", src.HighPressureLeak.VesselPressurePa.ToString(inv)),
                                new XAttribute("VesselT", src.HighPressureLeak.VesselTemperatureK.ToString(inv)),
                                new XAttribute("Orifice", src.HighPressureLeak.OrificeDiameterM.ToString(inv)),
                                new XAttribute("Volume", src.HighPressureLeak.VesselVolumeM3.ToString(inv)),
                                new XAttribute("Gamma", src.HighPressureLeak.GasGamma.ToString(inv)),
                                new XAttribute("MolarMass", src.HighPressureLeak.GasMolarMassKgMol.ToString(inv)),
                                new XAttribute("Cd", src.HighPressureLeak.DischargeCoefficient.ToString(inv)),
                                new XAttribute("SpecifyMdot", src.HighPressureLeak.SpecifyMassFlow ? "1" : "0"),
                                new XAttribute("Mdot", src.HighPressureLeak.SpecifiedMassFlowKgPerS.ToString(inv))) : null,
                            src.Gas != null ? new XElement("Gas",
                                new XAttribute("Name", src.Gas.Name ?? ""),
                                new XAttribute("MolarMass", src.Gas.MolarMass.ToString(inv)),
                                new XAttribute("LFL", src.Gas.LFL.ToString(inv)),
                                new XAttribute("IDLH", src.Gas.IDLH.ToString(inv)),
                                new XAttribute("ERPG1", src.Gas.ERPG1.ToString(inv)),
                                new XAttribute("ERPG2", src.Gas.ERPG2.ToString(inv)),
                                new XAttribute("ERPG3", src.Gas.ERPG3.ToString(inv))) : null))),

                new XElement("Thresholds",
                    sc.Thresholds.Select(t =>
                        new XElement("Threshold",
                            new XAttribute("Name", t.Name ?? ""),
                            new XAttribute("Type", t.Type.ToString()),
                            new XAttribute("Value", t.ConcentrationValue.ToString(inv)),
                            new XAttribute("Color", t.Color.ToString()),
                            new XAttribute("Opacity", t.Opacity.ToString(inv)),
                            new XAttribute("Visible", t.Visible),
                            new XAttribute("CloudAppearance", t.UseCloudAppearance),
                            new XAttribute("CloudColor", t.CloudColor.ToString())))),

                new XElement("ContourPlanes",
                    sc.ContourPlanes.Select(cp =>
                        new XElement("ContourPlane",
                            new XAttribute("Axis", cp.Axis.ToString()),
                            new XAttribute("Position", cp.Position.ToString(inv)),
                            new XAttribute("Visible", cp.Visible),
                            new XAttribute("Opacity", cp.Opacity.ToString(inv)),
                            new XAttribute("ColorMap", cp.ColorMap.ToString())))),

                sc.TransientWind != null && sc.TransientWind.Entries.Count > 0
                    ? new XElement("TransientWind",
                        new XAttribute("Enabled", sc.TransientWind.Enabled),
                        new XAttribute("ESD", sc.TransientWind.ESDTimeS.ToString(inv)),
                        sc.TransientWind.Entries.Select(we =>
                            new XElement("Entry",
                                new XAttribute("Time", we.TimeS.ToString(inv)),
                                new XAttribute("Speed", we.WindSpeed.ToString(inv)),
                                new XAttribute("Dir", we.WindDirectionDeg.ToString(inv)),
                                new XAttribute("Stability", we.StabilityClass.ToString()))))
                    : null,

                sc.GasMixture != null && sc.GasMixture.Components.Count > 0
                    ? new XElement("GasMixture",
                        sc.GasMixture.Components.Select(gc =>
                            new XElement("Component",
                                new XAttribute("Name", gc.Name ?? ""),
                                new XAttribute("MolarMass", gc.MolarMass.ToString(inv)),
                                new XAttribute("MoleFrac", gc.MoleFraction.ToString(inv)),
                                new XAttribute("LFL", gc.LFL.ToString(inv)),
                                new XAttribute("IDLH", gc.IDLH.ToString(inv)))))
                    : null);
        }

        private static XElement? SerializeMonitorPoints(Scene3D scene, CultureInfo inv)
        {
            if (scene.MonitorPoints.Count == 0) return null;
            return new XElement("MonitorPoints",
                scene.MonitorPoints.Select(m =>
                    new XElement("Monitor",
                        new XAttribute("Id", m.Id),
                        new XAttribute("Name", m.Name ?? ""),
                        new XAttribute("PosX", m.Position.X.ToString(inv)),
                        new XAttribute("PosY", m.Position.Y.ToString(inv)),
                        new XAttribute("PosZ", m.Position.Z.ToString(inv)),
                        new XAttribute("MeasuredQuantity", m.MeasuredQuantity.ToString()),
                        new XAttribute("Visible", m.Visible))));
        }

        private static XElement? SerializeWindRose(Scene3D scene, CultureInfo inv)
        {
            var wr = scene.WindRose;
            if (wr == null || wr.Bins.Count == 0) return null;
            return new XElement("WindRose",
                new XAttribute("ShowIn3D", wr.ShowIn3D),
                wr.Bins.Select(b =>
                    new XElement("Bin",
                        new XAttribute("Dir", b.DirectionDeg.ToString(inv)),
                        new XAttribute("Freq", b.Frequency.ToString(inv)),
                        new XAttribute("Speed", b.WindSpeed.ToString(inv)),
                        new XAttribute("Stability", b.StabilityClass.ToString()))));
        }

        /// <summary>
        /// One writer for every MeteorologicalConditions element in the file —
        /// DefaultMeteo, SnapshotMeteo and the two &lt;Meteo&gt; elements — so a field
        /// added here reaches all four instead of three. Returns null for a null
        /// meteo, which XElement drops from the tree.
        /// </summary>
        private static XElement? SerializeMeteo(string elementName, MeteorologicalConditions? meteo, CultureInfo inv)
        {
            if (meteo == null) return null;
            return new XElement(elementName,
                new XAttribute("WindSpeed", meteo.WindSpeed.ToString(inv)),
                new XAttribute("WindDir", meteo.WindDirectionDeg.ToString(inv)),
                new XAttribute("Stability", meteo.StabilityClass.ToString()),
                new XAttribute("Temp", meteo.AmbientTemperature.ToString(inv)),
                new XAttribute("Pressure", meteo.AmbientPressure.ToString(inv)),
                new XAttribute("Humidity", meteo.RelativeHumidity.ToString(inv)),
                new XAttribute("Roughness", meteo.RoughnessLengthM.ToString(inv)));
        }

        /// <summary>Ignition events. Read back by
        /// <c>SceneFileLoader.DeserializeIgnitions</c> — keep the two in step.</summary>
        private static XElement? SerializeIgnitions(Scene3D scene, CultureInfo inv)
        {
            if (scene.Ignitions == null || scene.Ignitions.Count == 0) return null;
            return new XElement("Ignitions",
                scene.Ignitions.Select(g =>
                    new XElement("Ignition",
                        new XAttribute("Id", g.Id),
                        new XAttribute("Name", g.Name ?? ""),
                        new XAttribute("SimulationId", g.SimulationId ?? ""),
                        new XAttribute("PosX", g.Position.X.ToString(inv)),
                        new XAttribute("PosY", g.Position.Y.ToString(inv)),
                        new XAttribute("PosZ", g.Position.Z.ToString(inv)),
                        new XAttribute("TimeS", g.TimeS.ToString(inv)),
                        new XAttribute("EnvelopeFraction", g.EnvelopeFraction.ToString(inv)),
                        new XAttribute("FlameSpeed", g.FlameSpeedMS.ToString(inv)),
                        new XAttribute("IsVisible", g.IsVisible))));
        }

        private static XElement? SerializeFireScenario(Scene3D scene, CultureInfo inv)
        {
            var fs = scene.FireScenario;
            if (fs == null || fs.Sources.Count == 0) return null;
            return new XElement("FireScenario",
                new XAttribute("Name", fs.Name ?? ""),
                new XAttribute("ReceiverMode", fs.ReceiverMode.ToString()),
                new XAttribute("ExposureTimeS", fs.ExposureTimeS.ToString(inv)),
                new XElement("RadLevels",
                    string.Join(",", fs.RadiationContourLevels.Select(l => l.ToString(inv)))),
                new XElement("FireSources",
                    fs.Sources.Select(f =>
                        new XElement("Fire",
                            new XAttribute("Id", f.Id),
                            new XAttribute("Name", f.Name ?? ""),
                            new XAttribute("PosX", f.Position.X.ToString(inv)),
                            new XAttribute("PosY", f.Position.Y.ToString(inv)),
                            new XAttribute("PosZ", f.Position.Z.ToString(inv)),
                            new XAttribute("DirX", f.Direction.X.ToString(inv)),
                            new XAttribute("DirY", f.Direction.Y.ToString(inv)),
                            new XAttribute("DirZ", f.Direction.Z.ToString(inv)),
                            new XAttribute("MassFlow", f.MassFlowRateKgS.ToString(inv)),
                            new XAttribute("Orifice", f.OrificeDiameterM.ToString(inv)),
                            new XAttribute("HeatComb", f.HeatOfCombustionJKg.ToString(inv)),
                            new XAttribute("RadFrac", f.RadiativeFraction.ToString(inv)),
                            new XAttribute("IsPool", f.IsPoolFire),
                            new XAttribute("PoolDia", f.PoolDiameterM.ToString(inv)),
                            new XAttribute("BurnRate", f.PoolBurnRateKgM2S.ToString(inv)),
                            new XAttribute("RadModel", f.RadiationModel.ToString()),
                            new XAttribute("FlameDia", f.FlameDiameterM.ToString(inv)),
                            new XAttribute("Sep", f.SepKwM2.ToString(inv)),
                            new XAttribute("FuelMolar", f.FuelMolarMassKgMol.ToString(inv)),
                            new XAttribute("IsVisible", f.IsVisible)))));
        }

        private static XElement? SerializeGasDetectors(Scene3D scene, CultureInfo inv)
        {
            if (scene.GasDetectors.Count == 0) return null;
            return new XElement("GasDetectors",
                scene.GasDetectors.Select(d =>
                    new XElement("Detector",
                        new XAttribute("Id", d.Id),
                        new XAttribute("Name", d.Name ?? ""),
                        new XAttribute("PosX", d.Position.X.ToString(inv)),
                        new XAttribute("PosY", d.Position.Y.ToString(inv)),
                        new XAttribute("PosZ", d.Position.Z.ToString(inv)),
                        new XAttribute("Threshold", d.ThresholdKgM3.ToString(inv)),
                        new XAttribute("MeasuredQuantity", d.MeasuredQuantity.ToString()),
                        new XAttribute("MeasuredThreshold", d.Threshold.ToString(inv)),
                        new XAttribute("Visible", d.Visible))));
        }

        private static XElement? SerializeCfdSimulations(Scene3D scene, CultureInfo inv, string projectFilePath)
        {
            if (scene.CfdSimulations.Count == 0) return null;

            string projectDir = Path.GetDirectoryName(projectFilePath) ?? "";
            string projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            string resultsDir = Path.Combine(projectDir, projectName + "_results");

            foreach (var entry in scene.CfdSimulations)
            {
                if (!entry.HasResults || string.IsNullOrEmpty(entry.CasePath)) continue;
                if (!Directory.Exists(entry.CasePath)) continue;

                string destDir = Path.Combine(resultsDir, entry.Id);
                if (entry.CasePath == destDir) continue;

                try
                {
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    bool hasFoamCtrl = File.Exists(Path.Combine(entry.CasePath, "system", "controlDict"));
                    bool hasRootBins = Directory.GetFiles(entry.CasePath, "*.bin",
                        SearchOption.TopDirectoryOnly).Length > 0;
                    if (!hasFoamCtrl && hasRootBins)
                    {
                        foreach (var f in Directory.GetFiles(entry.CasePath, "*.bin"))
                            File.Copy(f, Path.Combine(destDir, Path.GetFileName(f)), true);
                    }
                    else
                    {
                        CopyEssentialCfdResults(entry.CasePath, destDir);
                    }

                    entry.CasePath = destDir;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to copy results for " + entry.Id + ": " + ex.Message);
                }
            }

            return new XElement("CfdSimulations",
                scene.CfdSimulations.Select(e =>
                    new XElement("Simulation",
                        new XAttribute("Id", e.Id ?? ""),
                        new XAttribute("Name", e.Name ?? ""),
                        new XAttribute("ScenarioName", e.ScenarioName ?? ""),
                        new XAttribute("CasePath", e.CasePath ?? ""),
                        new XAttribute("CreatedAt", e.CreatedAt.ToString("o", inv)),
                        new XAttribute("DurationS", e.DurationS.ToString(inv)),
                        new XAttribute("TimeStepCount", e.TimeStepCount.ToString(inv)),
                        new XAttribute("GridNx", e.GridNx.ToString(inv)),
                        new XAttribute("GridNy", e.GridNy.ToString(inv)),
                        new XAttribute("GridNz", e.GridNz.ToString(inv)),
                        new XAttribute("DomainSizeM", e.DomainSizeM.ToString(inv)),
                        new XAttribute("HasResults", e.HasResults),
                        new XAttribute("SolverType", e.SolverType ?? ""))));
        }

        private static void CopyEssentialCfdResults(string srcCase, string destCase)
        {
            string sysDir = Path.Combine(destCase, "system");
            if (!Directory.Exists(sysDir))
                Directory.CreateDirectory(sysDir);

            string srcBlockMesh = Path.Combine(srcCase, "system", "blockMeshDict");
            if (File.Exists(srcBlockMesh))
                File.Copy(srcBlockMesh, Path.Combine(sysDir, "blockMeshDict"), true);

            foreach (var dir in Directory.GetDirectories(srcCase))
            {
                string name = Path.GetFileName(dir);
                if (!double.TryParse(name, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double t)) continue;
                if (t <= 0) continue;

                string tFile = Path.Combine(dir, "T");
                if (!File.Exists(tFile)) continue;

                string destTimeDir = Path.Combine(destCase, name);
                if (!Directory.Exists(destTimeDir))
                    Directory.CreateDirectory(destTimeDir);

                File.Copy(tFile, Path.Combine(destTimeDir, "T"), true);

                foreach (var extra in new[] { "C", "Cx", "Cy", "Cz" })
                {
                    string src = Path.Combine(dir, extra);
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(destTimeDir, extra), true);
                }
            }
        }
    }
}
