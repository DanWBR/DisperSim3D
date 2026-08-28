using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    public static class SceneFileLoader
    {
        /// <summary>
        /// Backward-compatible <see cref="CfdSolverType"/> parser. As the
        /// engine evolved we removed seven OpenFOAM solver variants that
        /// were either redundant with <c>RhoReactingBuoyantFoam</c> or
        /// superseded by the FluidX3D path. Older .dsproj files may still
        /// reference those names — map them to the closest survivor so the
        /// project still loads instead of falling back to a Gaussian model
        /// silently.
        /// </summary>
        private static CfdSolverType ParseSolverType(string s, CfdSolverType fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (Enum.TryParse<CfdSolverType>(s, ignoreCase: true, out var parsed))
                return parsed;

            // Legacy names → closest current solver. All of the deprecated
            // OpenFOAM variants are subsets of rhoReactingBuoyantFoam, so
            // they migrate cleanly.
            switch (s.Trim().ToLowerInvariant())
            {
                case "scalartransportfoam":
                case "scalartransportfoamsteady":
                case "scalarsimplefoam":
                case "pimplefoam":
                case "buoyantpimplefoam":
                case "reactingfoam":
                case "rhosimplefoam":
                    return CfdSolverType.RhoReactingBuoyantFoam;
                default:
                    return fallback;
            }
        }

        public static Scene3D Load(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                throw new System.IO.FileNotFoundException("Scene file not found", filePath);

            var inv = CultureInfo.InvariantCulture;

            XDocument doc;
            if (ProjectBundle.IsBundleFile(filePath))
            {
                // .dsproj — extract bundle, return the path-resolved project.xml.
                // The temp dir leaks intentionally for CLI runs since CFD case paths
                // inside the bundle need to remain readable until the run finishes.
                var bundle = ProjectBundle.Open(filePath);
                doc = bundle.ProjectXml;
            }
            else
            {
                doc = XDocument.Load(filePath);
            }

            var root = doc.Root;
            if (root == null || (root.Name.LocalName != "Scene3D" && root.Name.LocalName != "Flowsheet3D"))
                throw new InvalidOperationException("Invalid scene file format");

            var scene = new Scene3D();
            scene.Name = (string)root.Attribute("Name") ?? "New Scene";
            scene.Description = (string)root.Attribute("Description") ?? "";

            var gridEl = root.Element("GridSettings");
            if (gridEl != null)
            {
                scene.GridSpacing = double.Parse((string)gridEl.Attribute("Spacing") ?? "5", inv);
                scene.SnapToGrid = bool.Parse((string)gridEl.Attribute("SnapToGrid") ?? "True");
            }

            DeserializeGeneralSettings(root, inv, scene);
            DeserializeGasLibrary(root, inv, scene);
            DeserializeTopLevelSources(root, inv, scene);
            DeserializeWindFieldScenarios(root, inv, scene);
            DeserializeSimulations(root, inv, scene);
            DeserializeViews(root, inv, scene);
            DeserializeDispersionScenarios(root, inv, scene);
            LegacyProjectMigrator.MigrateInPlace(scene);
            DeserializeMonitorPoints(root, inv, scene);
            DeserializeFireScenario(root, inv, scene);
            DeserializeIgnitions(root, inv, scene);
            DeserializeGasDetectors(root, inv, scene);
            DeserializeDecorations(root, inv, scene);
            DeserializeEnvironment(root, inv, scene);
            DeserializeDispersionStudies(root, inv, scene);
            DeserializeDetectorAllocations(root, inv, scene);

            return scene;
        }

        private static void DeserializeGeneralSettings(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("GeneralSettings");
            if (el == null) return;
            var s = new ProjectSettings
            {
                Name = (string)el.Attribute("Name") ?? "",
                Description = (string)el.Attribute("Description") ?? "",
                Author = (string)el.Attribute("Author") ?? "",
                DefaultDomainSizeM = double.Parse((string)el.Attribute("DefaultDomainSize") ?? "200", inv),
                DefaultGridResolution = int.Parse((string)el.Attribute("DefaultGridRes") ?? "40", inv)
            };
            var mEl = el.Element("DefaultMeteo");
            if (mEl != null)
            {
                s.DefaultMeteo = ParseMeteo(mEl, inv);
            }
            scene.GeneralSettings = s;
        }

        private static void DeserializeGasLibrary(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("GasLibrary");
            if (el == null) return;
            foreach (var ge in el.Elements("Gas"))
            {
                var item = new GasLibraryItem
                {
                    Id = (string)ge.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)ge.Attribute("Name") ?? "Gas",
                    IsCryogenic = ((string)ge.Attribute("Cryogenic") ?? "0") == "1"
                };
                if (((string)ge.Attribute("Kind") ?? "Pure") == "Mixture")
                {
                    item.Kind = GasLibraryItemKind.Mixture;
                    item.Mixture = new GasMixture();
                    var mxEl = ge.Element("Mixture");
                    if (mxEl != null)
                    {
                        foreach (var ce in mxEl.Elements("Component"))
                        {
                            item.Mixture.Components.Add(new GasComponent
                            {
                                Name = (string)ce.Attribute("Name") ?? "",
                                MolarMass = double.Parse((string)ce.Attribute("MolarMass") ?? "0.016", inv),
                                MoleFraction = double.Parse((string)ce.Attribute("MoleFrac") ?? "1", inv),
                                LFL = double.Parse((string)ce.Attribute("LFL") ?? "0", inv),
                                IDLH = double.Parse((string)ce.Attribute("IDLH") ?? "0", inv)
                            });
                        }
                    }
                }
                else
                {
                    item.Kind = GasLibraryItemKind.Pure;
                    item.PureGas = new GasProperties
                    {
                        Name = item.Name,
                        MolarMass = double.Parse((string)ge.Attribute("MolarMass") ?? "0.016", inv),
                        LFL = double.Parse((string)ge.Attribute("LFL") ?? "0", inv),
                        IDLH = double.Parse((string)ge.Attribute("IDLH") ?? "0", inv),
                        ERPG1 = double.Parse((string)ge.Attribute("ERPG1") ?? "0", inv),
                        ERPG2 = double.Parse((string)ge.Attribute("ERPG2") ?? "0", inv),
                        ERPG3 = double.Parse((string)ge.Attribute("ERPG3") ?? "0", inv)
                    };
                }
                scene.GasLibrary.Add(item);
            }
        }

        private static void DeserializeTopLevelSources(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("TopLevelSources");
            if (el == null) return;
            foreach (var se in el.Elements("Source"))
                scene.TopLevelSources.Add(ParseSource(se, inv));
        }

        private static void DeserializeSimulations(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("Simulations");
            if (el == null) return;
            foreach (var se in el.Elements("Simulation"))
            {
                var sim = new Simulation
                {
                    Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)se.Attribute("Name") ?? "Simulation",
                    SourceId = (string)se.Attribute("SourceId") ?? "",
                    WindFieldId = (string)se.Attribute("WindFieldId") ?? "",
                    StatusMessage = (string)se.Attribute("StatusMessage") ?? "",
                    SnapshotDomainSizeM = double.Parse((string)se.Attribute("DomainSize") ?? "200", inv),
                    SnapshotGridResolution = int.Parse((string)se.Attribute("GridRes") ?? "40", inv),
                    SnapshotDurationS = double.Parse((string)se.Attribute("Duration") ?? "300", inv),
                    SnapshotTimeStepS = double.Parse((string)se.Attribute("TimeStep") ?? "0.5", inv),
                    SnapshotCount = int.Parse((string)se.Attribute("SnapshotCount") ?? "20", inv),
                    CasePath = (string)se.Attribute("CasePath") ?? "",
                    MaxConcentration = double.Parse((string)se.Attribute("MaxC") ?? "0", inv)
                };
                sim.SolverType = ParseSolverType(
                    (string)se.Attribute("SolverType") ?? "GaussianPuff",
                    CfdSolverType.GaussianPuff);
                SimulationStatus statusVal;
                if (Enum.TryParse((string)se.Attribute("Status") ?? "Configured", out statusVal))
                    sim.Status = statusVal;

                var snapMeteoEl = se.Element("SnapshotMeteo");
                if (snapMeteoEl != null) sim.SnapshotMeteo = ParseMeteo(snapMeteoEl, inv);
                var snapSrcEl = se.Element("SnapshotSource");
                if (snapSrcEl != null) sim.SnapshotSource = ParseSource(snapSrcEl, inv);

                sim.SnapshotCfdConfig = ParseAtmosphericCfd(se, inv);
                scene.Simulations.Add(sim);
            }
        }

        private static MeteorologicalConditions ParseMeteo(XElement mEl, CultureInfo inv)
        {
            return new MeteorologicalConditions
            {
                WindSpeed = double.Parse((string)mEl.Attribute("WindSpeed") ?? "5", inv),
                WindDirectionDeg = double.Parse((string)mEl.Attribute("WindDir") ?? "270", inv),
                StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                    (string)mEl.Attribute("Stability") ?? "D"),
                AmbientTemperature = double.Parse((string)mEl.Attribute("Temp") ?? "293.15", inv),
                AmbientPressure = double.Parse((string)mEl.Attribute("Pressure") ?? "101325", inv),
                RelativeHumidity = AttrDouble(mEl, inv, 0.5, "Humidity"),
                RoughnessLengthM = double.Parse((string)mEl.Attribute("Roughness") ?? "0.03", inv)
            };
        }

        private static ReleaseSource3D ParseSource(XElement se, CultureInfo inv)
        {
            var s = new ReleaseSource3D
            {
                Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString(),
                Name = (string)se.Attribute("Name") ?? "",
                AttachedUnitId = (string)se.Attribute("AttachedUnitId"),
                GasRefId = (string)se.Attribute("GasRefId"),
                Position = new DisperSim3D.Geometry.Point3D(
                    double.Parse((string)se.Attribute("PosX") ?? "0", inv),
                    double.Parse((string)se.Attribute("PosY") ?? "0", inv),
                    double.Parse((string)se.Attribute("PosZ") ?? "0", inv)),
                ReleaseRateKgPerS = double.Parse((string)se.Attribute("ReleaseRate") ?? "0.5", inv),
                PuffIntervalS = double.Parse((string)se.Attribute("PuffInterval") ?? "1", inv),
                ReleaseHeightOffset = double.Parse((string)se.Attribute("HeightOffset") ?? "2", inv),
                ReleaseAzimuthDeg = double.Parse((string)se.Attribute("Azimuth") ?? "0", inv),
                ReleaseElevationDeg = double.Parse((string)se.Attribute("Elevation") ?? "0", inv)
            };
            if (string.IsNullOrEmpty(s.AttachedUnitId)) s.AttachedUnitId = null;
            if (string.IsNullOrEmpty(s.GasRefId)) s.GasRefId = null;
            return s;
        }

        private static CfdConfiguration ParseAtmosphericCfd(XElement parent, CultureInfo inv)
        {
            var cfd = new CfdConfiguration();
            var el = parent.Element("Cfd");
            if (el == null) return cfd;
            cfd.UseAtmosphericBL = ((string)el.Attribute("AtmBL") ?? "0") == "1";
            cfd.TurbulentSchmidtNumber = double.Parse((string)el.Attribute("Sct") ?? "0.7", inv);
            cfd.TurbulentPrandtlNumber = double.Parse((string)el.Attribute("Prt") ?? "0.85", inv);
            cfd.KEpsilonSigmaEpsilon = double.Parse((string)el.Attribute("SigmaEps") ?? "1.3", inv);
            GroundThermalBoundary gbc;
            if (Enum.TryParse((string)el.Attribute("GroundBC") ?? "Adiabatic", out gbc))
                cfd.GroundThermalBC = gbc;
            cfd.GroundTemperatureK = double.Parse((string)el.Attribute("GroundT") ?? "293.15", inv);
            cfd.GroundHeatFluxWPerM2 = double.Parse((string)el.Attribute("GroundQ") ?? "0", inv);
            var ceps3Attr = (string)el.Attribute("Ceps3");
            cfd.BuoyancyEpsCoefficient = string.IsNullOrEmpty(ceps3Attr)
                ? (double?)null : double.Parse(ceps3Attr, inv);
            cfd.UsePatchedSctSolver = ((string)el.Attribute("PatchedSct") ?? "0") == "1";
            var binAttr = (string)el.Attribute("PatchedBin");
            if (!string.IsNullOrEmpty(binAttr)) cfd.PatchedSctSolverBinary = binAttr;
            var distroAttr = (string)el.Attribute("PatchedDistro");
            if (!string.IsNullOrEmpty(distroAttr)) cfd.PatchedSctSolverWslDistro = distroAttr;
            var bashrcAttr = (string)el.Attribute("PatchedBashrc");
            if (!string.IsNullOrEmpty(bashrcAttr)) cfd.PatchedSctSolverBashrc = bashrcAttr;
            cfd.UseVu2019MeshRefinement = ((string)el.Attribute("VuMesh") ?? "0") == "1";
            cfd.UseAblPrecursor = ((string)el.Attribute("AblPrec") ?? "0") == "1";
            var iterAttr = (string)el.Attribute("AblPrecIters");
            if (!string.IsNullOrEmpty(iterAttr) && int.TryParse(iterAttr, System.Globalization.NumberStyles.Integer, inv, out int it))
                cfd.AblPrecursorIterations = it;
            cfd.UseCryogenicPatchInjection = ((string)el.Attribute("CryoPatch") ?? "0") == "1";
            // Missing attribute → keep the constructor default (true). Only
            // an explicit "0" turns the GPU tracer off on load.
            cfd.UseGpuBuoyantTracer = ((string)el.Attribute("GpuTracer") ?? "1") != "0";
            return cfd;
        }

        private static void DeserializeWindFieldScenarios(XElement root, CultureInfo inv, Scene3D scene)
        {
            var listEl = root.Element("WindFieldScenarios");
            if (listEl == null) return;
            foreach (var wfEl in listEl.Elements("WindFieldScenario"))
            {
                var wf = new WindFieldScenario
                {
                    Id = (string)wfEl.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)wfEl.Attribute("Name") ?? "Wind Field",
                    DomainSizeM = double.Parse((string)wfEl.Attribute("DomainSize") ?? "200", inv),
                    DomainHeightM = double.Parse((string)wfEl.Attribute("DomainHeight") ?? "100", inv),
                    GridResolution = int.Parse((string)wfEl.Attribute("GridRes") ?? "40", inv),
                    CasePath = (string)wfEl.Attribute("CasePath")
                };
                var statusStr = (string)wfEl.Attribute("Status");
                if (!string.IsNullOrEmpty(statusStr))
                {
                    WindFieldStatus parsedStatus;
                    if (Enum.TryParse(statusStr, out parsedStatus))
                        wf.Status = parsedStatus;
                }
                bool useFx;
                if (bool.TryParse((string)wfEl.Attribute("UseFluidX3D") ?? "False", out useFx))
                    wf.UseFluidX3D = useFx;
                FluidX3DQuality fxQual;
                if (Enum.TryParse((string)wfEl.Attribute("FluidX3DQuality") ?? "Fast", out fxQual))
                    wf.FluidX3DQuality = fxQual;
                FluidX3DGroundBC fxGround;
                if (Enum.TryParse((string)wfEl.Attribute("FluidX3DGroundBC") ?? "FreeSlip", out fxGround))
                    wf.FluidX3DGroundBC = fxGround;
                var mEl = wfEl.Element("Meteo");
                if (mEl != null) wf.Meteo = ParseMeteo(mEl, inv);
                wf.CfdConfig = ParseAtmosphericCfd(wfEl, inv);
                wf.IsVisible = bool.Parse((string)wfEl.Attribute("IsVisible") ?? "True");
                scene.WindFieldScenarios.Add(wf);
            }
        }

        private static void DeserializeViews(XElement root, CultureInfo inv, Scene3D scene)
        {
            scene.Views.Clear();
            var el = root.Element("Views");
            if (el == null) return;
            foreach (var ve in el.Elements("View"))
            {
                var v = new DisperSim3D.Models.View
                {
                    Id = (string)ve.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)ve.Attribute("Name") ?? "View",
                    SimulationId = (string)ve.Attribute("SimulationId") ?? ""
                };
                if (Enum.TryParse((string)ve.Attribute("Kind") ?? "Isosurface", out ViewKind k)) v.Kind = k;
                if (Enum.TryParse((string)ve.Attribute("FieldProperty") ?? "Concentration", out ViewFieldProperty fp)) v.FieldProperty = fp;
                if (Enum.TryParse((string)ve.Attribute("TimeMode") ?? "PeakOverTime", out ViewTimeMode tm)) v.TimeMode = tm;
                v.SpecificTimeS = double.Parse((string)ve.Attribute("SpecificTime") ?? "0", inv);
                v.IsVisible = bool.Parse((string)ve.Attribute("IsVisible") ?? "True");
                v.Opacity = double.Parse((string)ve.Attribute("Opacity") ?? "0.5", inv);
                v.IsoValue = double.Parse((string)ve.Attribute("IsoValue") ?? "0.05", inv);
                try { v.IsoColor = DisperSim3D.Geometry.Color.Parse((string)ve.Attribute("IsoColor") ?? "#FF00FFFF"); }
                catch { v.IsoColor = DisperSim3D.Geometry.Colors.Cyan; }
                v.PlanePosition = double.Parse((string)ve.Attribute("PlanePosition") ?? "1", inv);
                if (Enum.TryParse((string)ve.Attribute("ColorMap") ?? "Jet", out ColorMapName cm)) v.ColorMap = cm;
                v.MinValue = double.Parse((string)ve.Attribute("MinValue") ?? "0", inv);
                v.MaxValue = double.Parse((string)ve.Attribute("MaxValue") ?? "0", inv);
                v.SampleResolution = int.Parse((string)ve.Attribute("SampleResolution") ?? "80", inv);
                v.UseCloudAppearance = bool.Parse((string)ve.Attribute("CloudAppearance") ?? "False");
                try { v.CloudColor = DisperSim3D.Geometry.Color.Parse((string)ve.Attribute("CloudColor") ?? "#FFC8C8D2"); }
                catch { v.CloudColor = DisperSim3D.Geometry.Color.FromRgb(200, 200, 210); }
                scene.Views.Add(v);
            }
        }

        private static void DeserializeDispersionScenarios(XElement root, CultureInfo inv, Scene3D scene)
        {
            var multiEl = root.Element("DispersionScenarios");
            if (multiEl != null)
            {
                scene.ActiveScenarioIndex = int.Parse((string)multiEl.Attribute("ActiveIndex") ?? "0", inv);
                foreach (var dEl in multiEl.Elements("DispersionScenario"))
                    scene.DispersionScenarios.Add(DeserializeSingleScenario(dEl, inv));
                return;
            }

            var singleEl = root.Element("DispersionScenario");
            if (singleEl == null) return;
            scene.DispersionScenarios.Add(DeserializeSingleScenario(singleEl, inv));
            scene.ActiveScenarioIndex = 0;
        }

        private static DispersionScenario DeserializeSingleScenario(XElement dEl, CultureInfo inv)
        {
            var sc = new DispersionScenario();
            sc.Name = (string)dEl.Attribute("Name") ?? "";
            sc.SimulationDurationS = double.Parse((string)dEl.Attribute("Duration") ?? "300", inv);
            sc.TimeStepS = double.Parse((string)dEl.Attribute("TimeStep") ?? "0.5", inv);
            sc.DomainSizeM = double.Parse((string)dEl.Attribute("DomainSize") ?? "200", inv);
            sc.GridResolution = int.Parse((string)dEl.Attribute("GridRes") ?? "80", inv);

            var solverTypeStr = (string)dEl.Attribute("SolverType");
            if (!string.IsNullOrEmpty(solverTypeStr))
                sc.SolverType = ParseSolverType(solverTypeStr, sc.SolverType);

            var wfId = (string)dEl.Attribute("WindFieldId");
            sc.WindFieldScenarioId = string.IsNullOrEmpty(wfId) ? null : wfId;

            var meteoEl = dEl.Element("Meteo");
            if (meteoEl != null) sc.Meteo = ParseMeteo(meteoEl, inv);

            var srcEl = dEl.Element("Sources");
            if (srcEl != null)
            {
                foreach (var se in srcEl.Elements("Source"))
                {
                    var source = new ReleaseSource3D();
                    source.Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString();
                    source.Name = (string)se.Attribute("Name") ?? "";
                    source.AttachedUnitId = (string)se.Attribute("AttachedUnitId");
                    if (string.IsNullOrEmpty(source.AttachedUnitId)) source.AttachedUnitId = null;
                    source.Position = new Point3D(
                        double.Parse((string)se.Attribute("PosX") ?? "0", inv),
                        double.Parse((string)se.Attribute("PosY") ?? "0", inv),
                        double.Parse((string)se.Attribute("PosZ") ?? "0", inv));
                    source.ReleaseRateKgPerS = double.Parse((string)se.Attribute("ReleaseRate") ?? "0.5", inv);
                    source.PuffIntervalS = double.Parse((string)se.Attribute("PuffInterval") ?? "1", inv);
                    source.ReleaseHeightOffset = double.Parse((string)se.Attribute("HeightOffset") ?? "2", inv);
                    source.ReleaseAzimuthDeg = double.Parse((string)se.Attribute("Azimuth") ?? "0", inv);
                    source.ReleaseElevationDeg = double.Parse((string)se.Attribute("Elevation") ?? "0", inv);
                    source.StackDiameterM = double.Parse((string)se.Attribute("StackDia") ?? "0", inv);
                    source.ExitVelocityMPerS = double.Parse((string)se.Attribute("ExitVel") ?? "0", inv);
                    source.ExitTemperatureK = double.Parse((string)se.Attribute("ExitTemp") ?? "293.15", inv);

                    var hpEl = se.Element("HPLeak");
                    if (hpEl != null)
                    {
                        source.HighPressureLeak = new HighPressureLeakParams
                        {
                            VesselPressurePa = double.Parse((string)hpEl.Attribute("VesselP") ?? "1000000", inv),
                            VesselTemperatureK = double.Parse((string)hpEl.Attribute("VesselT") ?? "293.15", inv),
                            OrificeDiameterM = double.Parse((string)hpEl.Attribute("Orifice") ?? "0.01", inv),
                            VesselVolumeM3 = double.Parse((string)hpEl.Attribute("Volume") ?? "10", inv),
                            GasGamma = double.Parse((string)hpEl.Attribute("Gamma") ?? "1.4", inv),
                            GasMolarMassKgMol = double.Parse((string)hpEl.Attribute("MolarMass") ?? "0.016", inv),
                            DischargeCoefficient = double.Parse((string)hpEl.Attribute("Cd") ?? "0.65", inv),
                            SpecifyMassFlow = ((string)hpEl.Attribute("SpecifyMdot") ?? "0") == "1",
                            SpecifiedMassFlowKgPerS = double.Parse((string)hpEl.Attribute("Mdot") ?? "1", inv)
                        };
                    }

                    var gasEl = se.Element("Gas");
                    if (gasEl != null)
                    {
                        source.Gas = new GasProperties
                        {
                            Name = (string)gasEl.Attribute("Name") ?? "",
                            MolarMass = double.Parse((string)gasEl.Attribute("MolarMass") ?? "0.016", inv),
                            LFL = double.Parse((string)gasEl.Attribute("LFL") ?? "0", inv),
                            IDLH = double.Parse((string)gasEl.Attribute("IDLH") ?? "0", inv),
                            ERPG1 = double.Parse((string)gasEl.Attribute("ERPG1") ?? "0", inv),
                            ERPG2 = double.Parse((string)gasEl.Attribute("ERPG2") ?? "0", inv),
                            ERPG3 = double.Parse((string)gasEl.Attribute("ERPG3") ?? "0", inv)
                        };
                    }

                    sc.Sources.Add(source);
                }
            }

            var thrEl = dEl.Element("Thresholds");
            if (thrEl != null)
            {
                sc.Thresholds.Clear();
                foreach (var te in thrEl.Elements("Threshold"))
                {
                    var threshold = new DispersionThreshold();
                    threshold.Name = (string)te.Attribute("Name") ?? "";
                    threshold.Type = (DispersionThresholdType)Enum.Parse(typeof(DispersionThresholdType),
                        (string)te.Attribute("Type") ?? "Custom");
                    threshold.ConcentrationValue = double.Parse((string)te.Attribute("Value") ?? "0.01", inv);
                    try { threshold.Color = DisperSim3D.Geometry.Color.Parse((string)te.Attribute("Color") ?? "#FFFF0000"); }
                    catch { threshold.Color = DisperSim3D.Geometry.Colors.Red; }
                    threshold.Opacity = double.Parse((string)te.Attribute("Opacity") ?? "0.3", inv);
                    threshold.Visible = bool.Parse((string)te.Attribute("Visible") ?? "True");
                    threshold.UseCloudAppearance = bool.Parse((string)te.Attribute("CloudAppearance") ?? "False");
                    try { threshold.CloudColor = DisperSim3D.Geometry.Color.Parse((string)te.Attribute("CloudColor") ?? "#FFC8C8D2"); }
                    catch { threshold.CloudColor = DisperSim3D.Geometry.Color.FromRgb(200, 200, 210); }
                    sc.Thresholds.Add(threshold);
                }
            }

            var twEl = dEl.Element("TransientWind");
            if (twEl != null)
            {
                sc.TransientWind = new TransientWindProfile
                {
                    Enabled = bool.Parse((string)twEl.Attribute("Enabled") ?? "False"),
                    ESDTimeS = double.Parse((string)twEl.Attribute("ESD") ?? "-1", inv)
                };
                foreach (var we in twEl.Elements("Entry"))
                {
                    sc.TransientWind.Entries.Add(new WindProfileEntry
                    {
                        TimeS = double.Parse((string)we.Attribute("Time") ?? "0", inv),
                        WindSpeed = double.Parse((string)we.Attribute("Speed") ?? "5", inv),
                        WindDirectionDeg = double.Parse((string)we.Attribute("Dir") ?? "270", inv),
                        StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                            (string)we.Attribute("Stability") ?? "D")
                    });
                }
            }

            var gmEl = dEl.Element("GasMixture");
            if (gmEl != null)
            {
                sc.GasMixture = new GasMixture();
                foreach (var ce in gmEl.Elements("Component"))
                {
                    sc.GasMixture.Components.Add(new GasComponent
                    {
                        Name = (string)ce.Attribute("Name") ?? "",
                        MolarMass = double.Parse((string)ce.Attribute("MolarMass") ?? "0.016", inv),
                        MoleFraction = double.Parse((string)ce.Attribute("MoleFrac") ?? "1", inv),
                        LFL = double.Parse((string)ce.Attribute("LFL") ?? "0", inv),
                        IDLH = double.Parse((string)ce.Attribute("IDLH") ?? "0", inv)
                    });
                }
            }

            return sc;
        }

        private static void DeserializeMonitorPoints(XElement root, CultureInfo inv, Scene3D scene)
        {
            var monEl = root.Element("MonitorPoints");
            if (monEl == null) return;
            foreach (var me in monEl.Elements("Monitor"))
            {
                scene.MonitorPoints.Add(new MonitorPoint3D
                {
                    Name = (string)me.Attribute("Name") ?? "",
                    Position = new Point3D(
                        double.Parse((string)me.Attribute("PosX") ?? "0", inv),
                        double.Parse((string)me.Attribute("PosY") ?? "0", inv),
                        double.Parse((string)me.Attribute("PosZ") ?? "0", inv))
                });
            }
        }

        /// <summary>
        /// Reads the fire scenario: contour levels plus every fire source.
        ///
        /// Two element shapes are accepted. <see cref="SceneFileSaver"/> nests the
        /// sources as <c>&lt;FireSources&gt;&lt;Fire/&gt;</c> — as does the legacy
        /// Scene3D writer in the WPF editor control — while hand-written files may
        /// carry <c>&lt;FireSource/&gt;</c> straight under <c>&lt;FireScenario&gt;</c>.
        /// Each numeric attribute likewise accepts the saver's short name and the
        /// older long form, so a project written by any past version comes back with
        /// its fire sources intact.
        /// </summary>
        private static void DeserializeFireScenario(XElement root, CultureInfo inv, Scene3D scene)
        {
            var fireEl = root.Element("FireScenario");
            if (fireEl == null) return;

            string scenarioName = (string)fireEl.Attribute("Name");
            if (!string.IsNullOrEmpty(scenarioName)) scene.FireScenario.Name = scenarioName;

            string receiverMode = (string)fireEl.Attribute("ReceiverMode");
            if (!string.IsNullOrEmpty(receiverMode)
                && Enum.TryParse(receiverMode, out ReceiverMode parsedReceiverMode))
                scene.FireScenario.ReceiverMode = parsedReceiverMode;

            var levelsEl = fireEl.Element("RadLevels");
            if (levelsEl != null && !string.IsNullOrWhiteSpace(levelsEl.Value))
            {
                var levels = new List<double>();
                foreach (var part in levelsEl.Value.Split(','))
                {
                    if (double.TryParse(part.Trim(), NumberStyles.Float, inv, out double level))
                        levels.Add(level);
                }
                if (levels.Count > 0) scene.FireScenario.RadiationContourLevels = levels;
            }

            var sourceElements = new List<XElement>();
            var nested = fireEl.Element("FireSources");
            if (nested != null) sourceElements.AddRange(nested.Elements("Fire"));
            sourceElements.AddRange(fireEl.Elements("FireSource"));

            foreach (var fe in sourceElements)
            {
                var fire = new FireSource
                {
                    Name = (string)fe.Attribute("Name") ?? "",
                    Position = new Point3D(
                        AttrDouble(fe, inv, 0, "PosX"),
                        AttrDouble(fe, inv, 0, "PosY"),
                        AttrDouble(fe, inv, 0, "PosZ")),
                    Direction = new Vector3D(
                        AttrDouble(fe, inv, 1, "DirX"),
                        AttrDouble(fe, inv, 0, "DirY"),
                        AttrDouble(fe, inv, 0, "DirZ")),
                    MassFlowRateKgS = AttrDouble(fe, inv, 1.0, "MassFlow"),
                    OrificeDiameterM = AttrDouble(fe, inv, 0.025, "Orifice"),
                    HeatOfCombustionJKg = AttrDouble(fe, inv, 50000000, "HeatComb", "HeatCombustion"),
                    RadiativeFraction = AttrDouble(fe, inv, 0.2, "RadFrac", "RadFraction"),
                    IsPoolFire = AttrBool(fe, false, "IsPool", "IsPoolFire"),
                    PoolDiameterM = AttrDouble(fe, inv, 5.0, "PoolDia"),
                    PoolBurnRateKgM2S = AttrDouble(fe, inv, 0.05, "BurnRate"),
                    FlameDiameterM = AttrDouble(fe, inv, 0, "FlameDia"),
                    SepKwM2 = AttrDouble(fe, inv, 0, "Sep"),
                    FuelMolarMassKgMol = AttrDouble(fe, inv, 0.016, "FuelMolar"),
                    IsVisible = AttrBool(fe, true, "IsVisible")
                };

                // Files written before the solid-flame model carry no RadModel;
                // they were computed as point sources, so that is what they reload as.
                string radModel = (string)fe.Attribute("RadModel");
                fire.RadiationModel = !string.IsNullOrEmpty(radModel)
                    && Enum.TryParse(radModel, out RadiationModel parsedRadModel)
                    ? parsedRadModel
                    : RadiationModel.PointSource;

                // Keep the saved Id: Views, detectors and studies reference sources
                // by Id, and a fresh Guid on every load would break those links.
                string id = (string)fe.Attribute("Id");
                if (!string.IsNullOrEmpty(id)) fire.Id = id;

                scene.FireScenario.Sources.Add(fire);
            }
        }

        /// <summary>Reads the ignition events written by
        /// <c>SceneFileSaver.SerializeIgnitions</c>.</summary>
        private static void DeserializeIgnitions(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("Ignitions");
            if (el == null) return;
            foreach (var ge in el.Elements("Ignition"))
            {
                var ignition = new IgnitionEvent
                {
                    Name = (string)ge.Attribute("Name") ?? "Ignition",
                    SimulationId = (string)ge.Attribute("SimulationId") ?? "",
                    Position = new Point3D(
                        AttrDouble(ge, inv, 0, "PosX"),
                        AttrDouble(ge, inv, 0, "PosY"),
                        AttrDouble(ge, inv, 0, "PosZ")),
                    TimeS = AttrDouble(ge, inv, 0, "TimeS"),
                    EnvelopeFraction = AttrDouble(ge, inv, 0.5, "EnvelopeFraction"),
                    FlameSpeedMS = AttrDouble(ge, inv, 10.0, "FlameSpeed"),
                    IsVisible = AttrBool(ge, true, "IsVisible")
                };

                string id = (string)ge.Attribute("Id");
                if (!string.IsNullOrEmpty(id)) ignition.Id = id;

                scene.Ignitions.Add(ignition);
            }
        }

        /// <summary>Value of the first attribute present among <paramref name="names"/>,
        /// parsed as an invariant double. Falls back to <paramref name="fallback"/> when
        /// none is present or the value doesn't parse.</summary>
        private static double AttrDouble(XElement e, CultureInfo inv, double fallback, params string[] names)
        {
            foreach (var name in names)
            {
                var attr = e.Attribute(name);
                if (attr != null && double.TryParse(attr.Value, NumberStyles.Float, inv, out double value))
                    return value;
            }
            return fallback;
        }

        /// <summary>Boolean counterpart of <see cref="AttrDouble"/>.</summary>
        private static bool AttrBool(XElement e, bool fallback, params string[] names)
        {
            foreach (var name in names)
            {
                var attr = e.Attribute(name);
                if (attr != null && bool.TryParse(attr.Value, out bool value))
                    return value;
            }
            return fallback;
        }

        private static void DeserializeGasDetectors(XElement root, CultureInfo inv, Scene3D scene)
        {
            var detEl = root.Element("GasDetectors");
            if (detEl == null) return;
            foreach (var de in detEl.Elements("Detector"))
            {
                scene.GasDetectors.Add(new GasDetector3D
                {
                    Name = (string)de.Attribute("Name") ?? "",
                    Position = new Point3D(
                        double.Parse((string)de.Attribute("PosX") ?? "0", inv),
                        double.Parse((string)de.Attribute("PosY") ?? "0", inv),
                        double.Parse((string)de.Attribute("PosZ") ?? "0", inv)),
                    ThresholdKgM3 = double.Parse((string)de.Attribute("Threshold") ?? "0.033", inv)
                });
            }
        }

        private static void DeserializeDecorations(XElement root, CultureInfo inv, Scene3D scene)
        {
            var decosEl = root.Element("Decorations");
            if (decosEl == null) return;

            foreach (var de in decosEl.Elements("Decoration"))
            {
                var deco = new Decoration3D
                {
                    Id = (string)de.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)de.Attribute("Name") ?? "",
                    FilePath = (string)de.Attribute("FilePath") ?? "",
                    TexturePath = (string)de.Attribute("TexturePath") ?? "",
                    Position = new Point3D(
                        double.Parse((string)de.Attribute("PosX") ?? "0", inv),
                        double.Parse((string)de.Attribute("PosY") ?? "0", inv),
                        double.Parse((string)de.Attribute("PosZ") ?? "0", inv)),
                    Rotation = new Vector3D(
                        double.Parse((string)de.Attribute("RotX") ?? "0", inv),
                        double.Parse((string)de.Attribute("RotY") ?? "0", inv),
                        double.Parse((string)de.Attribute("RotZ") ?? "0", inv)),
                    Scale = double.Parse((string)de.Attribute("Scale") ?? "1", inv),
                    Opacity = double.Parse((string)de.Attribute("Opacity") ?? "1", inv),
                    SpecularPower = double.Parse((string)de.Attribute("SpecularPower") ?? "40", inv),
                };

                var clipAttr = (string)de.Attribute("ClipEnabled");
                if (clipAttr != null)
                {
                    deco.ClipEnabled = bool.Parse(clipAttr);
                    if (Enum.TryParse((string)de.Attribute("ClipAxis") ?? "Y", out ClipAxis ca))
                        deco.ClipAxis = ca;
                    deco.ClipValue = double.Parse((string)de.Attribute("ClipValue") ?? "0", inv);
                    deco.ClipAbove = bool.Parse((string)de.Attribute("ClipAbove") ?? "True");
                }

                var useCustomAttr = (string)de.Attribute("UseCustomMaterial");
                if (useCustomAttr != null)
                {
                    deco.UseCustomMaterial = bool.Parse(useCustomAttr);
                    if (Enum.TryParse((string)de.Attribute("MaterialType") ?? "Matte", out MaterialType3D mt))
                        deco.MaterialType = mt;
                    try { deco.MaterialColor = Geometry.Color.Parse((string)de.Attribute("MaterialColor") ?? "#FFD3D3D3"); }
                    catch { deco.MaterialColor = Geometry.Colors.LightGray; }
                }

                deco.IsVisible = bool.Parse((string)de.Attribute("IsVisible") ?? "True");
                scene.Decorations.Add(deco);
            }
        }

        private static void DeserializeEnvironment(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("Environment");
            if (el == null) return;

            var env = scene.Environment ?? new EnvironmentSettings();

            var useSun = (string)el.Attribute("UseSunLighting");
            if (useSun != null) env.UseSunLighting = bool.Parse(useSun);

            var azimuth = (string)el.Attribute("SunAzimuthDeg");
            if (azimuth != null) env.SunAzimuthDeg = double.Parse(azimuth, inv);

            var elevation = (string)el.Attribute("SunElevationDeg");
            if (elevation != null) env.SunElevationDeg = double.Parse(elevation, inv);

            var sunInt = (string)el.Attribute("SunIntensity");
            if (sunInt != null) env.SunIntensity = double.Parse(sunInt, inv);

            var ambInt = (string)el.Attribute("AmbientIntensity");
            if (ambInt != null) env.AmbientIntensity = double.Parse(ambInt, inv);

            var solarClock = (string)el.Attribute("UseSolarClock");
            if (solarClock != null) env.UseSolarClock = bool.Parse(solarClock);

            var lat = (string)el.Attribute("Latitude");
            if (lat != null) env.Latitude = double.Parse(lat, inv);

            var doy = (string)el.Attribute("DayOfYear");
            if (doy != null) env.DayOfYear = int.Parse(doy, inv);

            var tod = (string)el.Attribute("TimeOfDayHours");
            if (tod != null) env.TimeOfDayHours = double.Parse(tod, inv);

            var skyEnabled = (string)el.Attribute("SkydomeEnabled");
            if (skyEnabled != null) env.SkydomeEnabled = bool.Parse(skyEnabled);

            var zenith = (string)el.Attribute("SkyZenith");
            if (zenith != null)
                try { env.SkyZenithColor = Geometry.Color.Parse(zenith); } catch { }

            var horizon = (string)el.Attribute("SkyHorizon");
            if (horizon != null)
                try { env.SkyHorizonColor = Geometry.Color.Parse(horizon); } catch { }

            var ground = (string)el.Attribute("Ground");
            if (ground != null && Enum.TryParse(ground, out GroundMaterial gm))
                env.Ground = gm;

            var showGrid = (string)el.Attribute("ShowGridOverlay");
            if (showGrid != null) env.ShowGridOverlay = bool.Parse(showGrid);

            var showClouds = (string)el.Attribute("ShowClouds");
            if (showClouds != null) env.ShowClouds = bool.Parse(showClouds);

            var cloudSpeed = (string)el.Attribute("CloudSpeed");
            if (cloudSpeed != null) env.CloudSpeed = double.Parse(cloudSpeed, inv);

            var showGrass = (string)el.Attribute("ShowGrassBlades");
            if (showGrass != null) env.ShowGrassBlades = bool.Parse(showGrass);

            var grassCount = (string)el.Attribute("GrassBladeCount");
            if (grassCount != null) env.GrassBladeCount = int.Parse(grassCount, inv);

            var skyTex = (string)el.Attribute("SkyTexturePath");
            if (skyTex != null) env.SkyTexturePath = skyTex;

            var gndTex = (string)el.Attribute("GroundTexturePath");
            if (gndTex != null) env.GroundTexturePath = gndTex;

            var gndTile = (string)el.Attribute("GroundTextureTileSize");
            if (gndTile != null) env.GroundTextureTileSize = double.Parse(gndTile, inv);

            var gridMinor = (string)el.Attribute("GridMinorSpacing");
            if (gridMinor != null) env.GridMinorSpacing = double.Parse(gridMinor, inv);

            var gridMajor = (string)el.Attribute("GridMajorSpacing");
            if (gridMajor != null) env.GridMajorSpacing = double.Parse(gridMajor, inv);

            var gridHalf = (string)el.Attribute("GridHalfSize");
            if (gridHalf != null) env.GridHalfSize = double.Parse(gridHalf, inv);

            var shadowsEn = (string)el.Attribute("ShadowsEnabled");
            if (shadowsEn != null) env.ShadowsEnabled = bool.Parse(shadowsEn);

            var fogEn = (string)el.Attribute("FogEnabled");
            if (fogEn != null) env.FogEnabled = bool.Parse(fogEn);

            var fogDens = (string)el.Attribute("FogDensity");
            if (fogDens != null) env.FogDensity = double.Parse(fogDens, inv);

            var skyBright = (string)el.Attribute("SkyTextureBrightness");
            if (skyBright != null) env.SkyTextureBrightness = double.Parse(skyBright, inv);

            var skyVOff = (string)el.Attribute("SkyTextureVOffset");
            if (skyVOff != null) env.SkyTextureVOffset = double.Parse(skyVOff, inv);

            var night = (string)el.Attribute("NightMode");
            if (night != null) env.NightMode = bool.Parse(night);
            var moonAz = (string)el.Attribute("MoonAzimuthDeg");
            if (moonAz != null) env.MoonAzimuthDeg = double.Parse(moonAz, inv);
            var moonEl = (string)el.Attribute("MoonElevationDeg");
            if (moonEl != null) env.MoonElevationDeg = double.Parse(moonEl, inv);
            var moonI = (string)el.Attribute("MoonIntensity");
            if (moonI != null) env.MoonIntensity = double.Parse(moonI, inv);
            var stars = (string)el.Attribute("ShowStars");
            if (stars != null) env.ShowStars = bool.Parse(stars);

            scene.Environment = env;
        }

        private static void DeserializeDispersionStudies(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("DispersionStudies");
            if (el == null) return;
            scene.DispersionStudies.Clear();
            foreach (var se in el.Elements("Study"))
            {
                var st = new DispersionStudy
                {
                    Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)se.Attribute("Name") ?? "Dispersion Study",
                    Description = (string)se.Attribute("Description") ?? "",
                    DetectionThreshold = double.Parse((string)se.Attribute("DetectionThreshold") ?? "50", inv),
                    IsVisible = bool.Parse((string)se.Attribute("IsVisible") ?? "True")
                };
                if (Enum.TryParse((string)se.Attribute("DetectionQuantity") ?? "PercentLFL", out ViewFieldProperty dq))
                    st.DetectionQuantity = dq;
                var created = (string)se.Attribute("CreatedAt");
                if (created != null && DateTime.TryParse(created, inv, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    st.CreatedAt = dt;

                var simsEl = se.Element("Simulations");
                if (simsEl != null)
                    foreach (var simRef in simsEl.Elements("Simulation"))
                    {
                        var sid = (string)simRef.Attribute("Id");
                        if (!string.IsNullOrEmpty(sid)) st.SimulationIds.Add(sid);
                    }

                var rwEl = se.Element("RiskWeights");
                if (rwEl != null)
                    foreach (var r in rwEl.Elements("R"))
                    {
                        var simId = (string)r.Attribute("SimId") ?? "";
                        if (string.IsNullOrEmpty(simId)) continue;
                        var risk = new ScenarioRisk
                        {
                            FreqPerYear = double.Parse((string)r.Attribute("FreqValue") ?? "1", inv),
                            Consequence = double.Parse((string)r.Attribute("ConsValue") ?? "1", inv)
                        };
                        if (Enum.TryParse((string)r.Attribute("FreqMode") ?? "Auto", out RiskValueMode fm))
                            risk.FreqMode = fm;
                        if (Enum.TryParse((string)r.Attribute("ConsMode") ?? "Auto", out RiskValueMode cm))
                            risk.ConsMode = cm;
                        st.RiskWeights[simId] = risk;
                    }

                scene.DispersionStudies.Add(st);
            }
        }

        private static void DeserializeDetectorAllocations(XElement root, CultureInfo inv, Scene3D scene)
        {
            var el = root.Element("DetectorAllocations");
            if (el == null) return;
            scene.DetectorAllocations.Clear();
            foreach (var ae in el.Elements("Allocation"))
            {
                var a = new DetectorAllocation
                {
                    Id = (string)ae.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)ae.Attribute("Name") ?? "Detector Allocation",
                    DispersionStudyId = (string)ae.Attribute("DispersionStudyId") ?? "",
                    TargetCoveragePercent = double.Parse((string)ae.Attribute("TargetCoveragePercent") ?? "100", inv),
                    MaxDetectors = int.Parse((string)ae.Attribute("MaxDetectors") ?? "0", inv),
                    DetectionRadiusM = double.Parse((string)ae.Attribute("DetectionRadiusM") ?? "5", inv),
                    MinZ = double.Parse((string)ae.Attribute("MinZ") ?? "1.5", inv),
                    MaxZ = double.Parse((string)ae.Attribute("MaxZ") ?? "3", inv),
                    CandidateNx = int.Parse((string)ae.Attribute("CandidateNx") ?? "60", inv),
                    CandidateNy = int.Parse((string)ae.Attribute("CandidateNy") ?? "60", inv),
                    CandidateNz = int.Parse((string)ae.Attribute("CandidateNz") ?? "3", inv),
                    UseExistingDetectors = bool.Parse((string)ae.Attribute("UseExistingDetectors") ?? "False"),
                    DetectionProbability = double.Parse((string)ae.Attribute("DetectionProbability") ?? "1", inv),
                    UseDistanceWeighting = bool.Parse((string)ae.Attribute("UseDistanceWeighting") ?? "False"),
                    DistanceWeightMin = double.Parse((string)ae.Attribute("DistanceWeightMin") ?? "0.5", inv),
                    DistanceWeightMax = double.Parse((string)ae.Attribute("DistanceWeightMax") ?? "1", inv),
                    AchievedCoveragePercent = double.Parse((string)ae.Attribute("AchievedCoveragePercent") ?? "0", inv),
                    TotalRisk = double.Parse((string)ae.Attribute("TotalRisk") ?? "0", inv),
                    ResidualRisk = double.Parse((string)ae.Attribute("ResidualRisk") ?? "0", inv),
                    RiskReductionFraction = double.Parse((string)ae.Attribute("RiskReductionFraction") ?? "0", inv),
                    StatusMessage = (string)ae.Attribute("StatusMessage") ?? "",
                    IsVisible = bool.Parse((string)ae.Attribute("IsVisible") ?? "True")
                };
                if (Enum.TryParse((string)ae.Attribute("Objective") ?? "CoverAll", out AllocationObjective obj))
                    a.Objective = obj;
                if (Enum.TryParse((string)ae.Attribute("Strategy") ?? "GreedyMaxCoverage", out AllocationStrategy strat))
                    a.Strategy = strat;
                if (Enum.TryParse((string)ae.Attribute("Status") ?? "Configured", out AllocationStatus status))
                    a.Status = status;
                var runAt = (string)ae.Attribute("RunAt");
                if (runAt != null && DateTime.TryParse(runAt, inv, System.Globalization.DateTimeStyles.RoundtripKind, out var ra))
                    a.RunAt = ra;

                var posEl = ae.Element("Positions");
                if (posEl != null)
                    foreach (var p in posEl.Elements("P"))
                        a.AllocatedPositions.Add(new Geometry.Point3D(
                            double.Parse((string)p.Attribute("X") ?? "0", inv),
                            double.Parse((string)p.Attribute("Y") ?? "0", inv),
                            double.Parse((string)p.Attribute("Z") ?? "0", inv)));

                var covEl = ae.Element("Coverage");
                if (covEl != null)
                    foreach (var c in covEl.Elements("C"))
                    {
                        var simId = (string)c.Attribute("SimId") ?? "";
                        if (!string.IsNullOrEmpty(simId))
                            a.PerCloudCovered[simId] = bool.Parse((string)c.Attribute("Covered") ?? "False");
                    }

                var rrEl = ae.Element("ResidualRisks");
                if (rrEl != null)
                    foreach (var r in rrEl.Elements("R"))
                    {
                        var simId = (string)r.Attribute("SimId") ?? "";
                        if (!string.IsNullOrEmpty(simId))
                            a.PerCloudResidualRisk[simId] = double.Parse((string)r.Attribute("R") ?? "0", inv);
                    }

                var rcEl = ae.Element("RiskCurve");
                if (rcEl != null)
                    foreach (var p in rcEl.Elements("P"))
                    {
                        a.RiskCurveK.Add(int.Parse((string)p.Attribute("K") ?? "0", inv));
                        a.RiskCurveRRF.Add(double.Parse((string)p.Attribute("RRF") ?? "0", inv));
                    }

                scene.DetectorAllocations.Add(a);
            }
        }
    }
}
