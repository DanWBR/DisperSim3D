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
            DeserializeGasDetectors(root, inv, scene);
            DeserializeDecorations(root, inv, scene);
            DeserializeEnvironment(root, inv, scene);

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
                CfdSolverType solverType;
                if (Enum.TryParse((string)se.Attribute("SolverType") ?? "GaussianPuff", out solverType))
                    sim.SolverType = solverType;
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
            {
                CfdSolverType parsed;
                if (Enum.TryParse(solverTypeStr, out parsed))
                    sc.SolverType = parsed;
            }

            var wfId = (string)dEl.Attribute("WindFieldId");
            sc.WindFieldScenarioId = string.IsNullOrEmpty(wfId) ? null : wfId;

            var meteoEl = dEl.Element("Meteo");
            if (meteoEl != null)
            {
                sc.Meteo = new MeteorologicalConditions
                {
                    WindSpeed = double.Parse((string)meteoEl.Attribute("WindSpeed") ?? "5", inv),
                    WindDirectionDeg = double.Parse((string)meteoEl.Attribute("WindDir") ?? "270", inv),
                    StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                        (string)meteoEl.Attribute("Stability") ?? "D"),
                    AmbientTemperature = double.Parse((string)meteoEl.Attribute("Temp") ?? "293.15", inv),
                    AmbientPressure = double.Parse((string)meteoEl.Attribute("Pressure") ?? "101325", inv)
                };
            }

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
                    threshold.Visible = bool.Parse((string)te.Attribute("Visible") ?? "True");
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

        private static void DeserializeFireScenario(XElement root, CultureInfo inv, Scene3D scene)
        {
            var fireEl = root.Element("FireScenario");
            if (fireEl == null) return;
            foreach (var fe in fireEl.Elements("FireSource"))
            {
                scene.FireScenario.Sources.Add(new FireSource
                {
                    Name = (string)fe.Attribute("Name") ?? "",
                    Position = new Point3D(
                        double.Parse((string)fe.Attribute("PosX") ?? "0", inv),
                        double.Parse((string)fe.Attribute("PosY") ?? "0", inv),
                        double.Parse((string)fe.Attribute("PosZ") ?? "0", inv)),
                    MassFlowRateKgS = double.Parse((string)fe.Attribute("MassFlow") ?? "1", inv),
                    OrificeDiameterM = double.Parse((string)fe.Attribute("Orifice") ?? "0.025", inv),
                    HeatOfCombustionJKg = double.Parse((string)fe.Attribute("HeatCombustion") ?? "50000000", inv),
                    RadiativeFraction = double.Parse((string)fe.Attribute("RadFraction") ?? "0.2", inv),
                    IsPoolFire = bool.Parse((string)fe.Attribute("IsPoolFire") ?? "False"),
                    PoolDiameterM = double.Parse((string)fe.Attribute("PoolDia") ?? "5", inv),
                    PoolBurnRateKgM2S = double.Parse((string)fe.Attribute("BurnRate") ?? "0.05", inv),
                    IsVisible = bool.Parse((string)fe.Attribute("IsVisible") ?? "True")
                });
            }
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

            scene.Environment = env;
        }
    }
}
