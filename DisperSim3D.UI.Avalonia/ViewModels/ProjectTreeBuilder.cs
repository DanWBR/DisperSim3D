using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.ViewModels
{
    /// <summary>
    /// Translates a <see cref="Scene3D"/> into the
    /// <see cref="ProjectTreeNode"/> hierarchy displayed in the left dock.
    /// Sections mirror the WPF <c>ProjectTreeWpfPanel</c> layout one-for-one
    /// so users moving between the two UIs see the same structure.
    /// </summary>
    public static class ProjectTreeBuilder
    {
        // Icon names map to Material Design Icons (resolved at render time
        // by Projektanker.Icons.Avalonia via the "mdi-" prefix). Section
        // nodes get a distinct icon per kind; leaf nodes get a generic
        // chevron so the row decoration doesn't compete with the name.
        private const string SectionIconGeneral   = "mdi-cog";
        private const string SectionIconGas       = "mdi-gas-cylinder";
        private const string SectionIconGeometry  = "mdi-cube-outline";
        private const string SectionIconSource    = "mdi-water";
        private const string SectionIconFire      = "mdi-fire";
        private const string SectionIconIgnition  = "mdi-flare";
        private const string SectionIconWind      = "mdi-weather-windy";
        private const string SectionIconSim       = "mdi-play-circle-outline";
        private const string SectionIconStudy     = "mdi-chart-line";
        private const string SectionIconAlloc     = "mdi-target";
        private const string SectionIconView      = "mdi-image-outline";
        private const string SectionIconCamera    = "mdi-camera-outline";
        private const string SectionIconMonitor   = "mdi-circle-medium";
        private const string SectionIconDetector  = "mdi-radar";
        private const string SectionIconWindRose  = "mdi-compass-outline";
        private const string SectionIconEnv       = "mdi-weather-partly-cloudy";
        private const string ProjectIcon          = "mdi-folder-outline";
        private const string LeafIcon             = "mdi-chevron-right";

        // Section icon colours — give each category a distinct tint
        private const string ColGeneral  = "#6B7280"; // gray
        private const string ColEnv      = "#0EA5E9"; // sky blue
        private const string ColGas      = "#8B5CF6"; // violet
        private const string ColGeometry = "#F59E0B"; // amber
        private const string ColSource   = "#EF4444"; // red
        private const string ColFire     = "#F97316"; // orange
        private const string ColWind     = "#06B6D4"; // cyan
        private const string ColSim      = "#10B981"; // emerald
        private const string ColStudy    = "#6366F1"; // indigo
        private const string ColAlloc    = "#EC4899"; // pink
        private const string ColView     = "#14B8A6"; // teal
        private const string ColCamera   = "#64748B"; // slate
        private const string ColMonitor  = "#F59E0B"; // amber
        private const string ColDetector = "#D946EF"; // fuchsia
        private const string ColWindRose = "#0284C7"; // blue
        private const string ColProject  = "#A3A3A3"; // neutral
        private const string ColLeaf     = "#9CA3AF"; // gray-400

        public static ObservableCollection<ProjectTreeNode> Build(Scene3D? scene, string projectName)
        {
            var roots = new ObservableCollection<ProjectTreeNode>();
            if (scene == null)
            {
                roots.Add(new ProjectTreeNode("empty", "mdi-folder-open-outline",
                    "(no project loaded — File → Open or File → New)"));
                return roots;
            }

            var project = new ProjectTreeNode("project", ProjectIcon,
                string.IsNullOrWhiteSpace(projectName) ? "Untitled" : projectName,
                iconColor: ColProject);

            // ── General Settings ───────────────────────────────────────────
            project.Children.Add(new ProjectTreeNode(
                "general", SectionIconGeneral, "General Settings",
                tag: scene.GeneralSettings, iconColor: ColGeneral));

            // ── Environment ───────────────────────────────────────────────
            project.Children.Add(new ProjectTreeNode(
                "environment", SectionIconEnv, "Environment",
                tag: scene.Environment, iconColor: ColEnv));

            // ── Gases ──────────────────────────────────────────────────────
            var gases = new ProjectTreeNode("gases", SectionIconGas,
                "Gases", Count(scene.GasLibrary?.Count), iconColor: ColGas);
            if (scene.GasLibrary != null)
                foreach (var g in scene.GasLibrary)
                    gases.Children.Add(new ProjectTreeNode(
                        "gas:" + g.Id, LeafIcon,
                        string.IsNullOrEmpty(g.Name) ? "(unnamed)" : g.Name,
                        tag: g, iconColor: ColLeaf));
            project.Children.Add(gases);

            // ── Geometry ───────────────────────────────────────────────────
            var geometry = new ProjectTreeNode("geometry", SectionIconGeometry,
                "Geometry", Count(scene.Decorations?.Count), iconColor: ColGeometry);
            if (scene.Decorations != null)
                foreach (var d in scene.Decorations)
                    geometry.Children.Add(new ProjectTreeNode(
                        "deco:" + d.Id, LeafIcon,
                        string.IsNullOrEmpty(d.Name) ? "(decoration)" : d.Name,
                        tag: d, hasVisibilityToggle: true,
                        initialVisibility: d.IsVisible,
                        iconColor: ColLeaf));
            project.Children.Add(geometry);

            // ── Sources ────────────────────────────────────────────────────
            var sources = new ProjectTreeNode("sources", SectionIconSource,
                "Sources", Count(scene.TopLevelSources?.Count), iconColor: ColSource);
            if (scene.TopLevelSources != null)
                foreach (var s in scene.TopLevelSources)
                    sources.Children.Add(new ProjectTreeNode(
                        "src:" + s.Id, LeafIcon,
                        string.IsNullOrEmpty(s.Name) ? "(source)" : s.Name,
                        tag: s, hasVisibilityToggle: true,
                        initialVisibility: s.IsVisible,
                        iconColor: ColLeaf));
            project.Children.Add(sources);

            // ── Fire Sources ───────────────────────────────────────────────
            // FireScenario.Sources is the canonical list of jet/pool fires
            // for thermal radiation analysis. Section node carries the scenario
            // itself so the inspector can edit the contour levels list.
            var fireSources = scene.FireScenario?.Sources;
            var fires = new ProjectTreeNode("fires", SectionIconFire,
                "Fire Sources", Count(fireSources?.Count),
                tag: scene.FireScenario, iconColor: ColFire);
            if (fireSources != null)
                foreach (var f in fireSources)
                    fires.Children.Add(new ProjectTreeNode(
                        "fire:" + f.Id, LeafIcon,
                        string.IsNullOrEmpty(f.Name) ? "(fire)" : f.Name,
                        tag: f, hasVisibilityToggle: true,
                        initialVisibility: f.IsVisible,
                        iconColor: ColLeaf));
            project.Children.Add(fires);

            // ── Ignitions ──────────────────────────────────────────────────
            // Each one burns a Simulation's cloud into a flash-fire envelope.
            var ignitions = new ProjectTreeNode("ignitions", SectionIconIgnition,
                "Ignitions", Count(scene.Ignitions?.Count), iconColor: ColFire);
            if (scene.Ignitions != null)
                foreach (var g in scene.Ignitions)
                    ignitions.Children.Add(new ProjectTreeNode(
                        "ignition:" + g.Id, LeafIcon,
                        string.IsNullOrEmpty(g.Name) ? "(ignition)" : g.Name,
                        tag: g, hasVisibilityToggle: true,
                        initialVisibility: g.IsVisible,
                        iconColor: ColLeaf));
            project.Children.Add(ignitions);

            // ── Wind Fields ────────────────────────────────────────────────
            var winds = new ProjectTreeNode("winds", SectionIconWind,
                "Wind Fields", Count(scene.WindFieldScenarios?.Count), iconColor: ColWind);
            if (scene.WindFieldScenarios != null)
                foreach (var w in scene.WindFieldScenarios)
                    winds.Children.Add(new ProjectTreeNode(
                        "wind:" + w.Id, LeafIcon,
                        string.IsNullOrEmpty(w.Name) ? "(wind field)" : w.Name,
                        tag: w, hasVisibilityToggle: true,
                        initialVisibility: w.IsVisible,
                        iconColor: ColLeaf));
            project.Children.Add(winds);

            // ── Simulations ────────────────────────────────────────────────
            var sims = new ProjectTreeNode("simulations", SectionIconSim,
                "Simulations", Count(scene.Simulations?.Count), iconColor: ColSim);
            if (scene.Simulations != null)
                foreach (var sim in scene.Simulations)
                {
                    string srcName = scene.TopLevelSources?.FirstOrDefault(s => s.Id == sim.SourceId)?.Name
                        ?? sim.SnapshotSource?.Name ?? "?";
                    string wfName = scene.WindFieldScenarios?.FirstOrDefault(w => w.Id == sim.WindFieldId)?.Name
                        ?? "?";
                    string solverTag = DisperSim3D.Core.SolverCode.Of(sim.SolverType);
                    string label = string.Format("{0}  [{1}]  [{2} / {3}]",
                        string.IsNullOrEmpty(sim.Name) ? "(simulation)" : sim.Name,
                        solverTag, srcName, wfName);
                    string statusColor = sim.Status == SimulationStatus.Completed ? "#2E8B57"
                        : sim.Status == SimulationStatus.Failed ? "#DC143C"
                        : sim.Status == SimulationStatus.Running || sim.Status == SimulationStatus.Queued
                            ? "#FF8C00" : "#888888";
                    sims.Children.Add(new ProjectTreeNode(
                        "sim:" + sim.Id, LeafIcon, label,
                        tag: sim, hasVisibilityToggle: true,
                        initialVisibility: sim.IsVisible,
                        statusText: sim.Status.ToString(),
                        statusColor: statusColor,
                        iconColor: ColLeaf));
                }
            project.Children.Add(sims);

            // ── Dispersion Studies ─────────────────────────────────────────
            var studies = new ProjectTreeNode("studies", SectionIconStudy,
                "Dispersion Studies", Count(scene.DispersionStudies?.Count), iconColor: ColStudy);
            if (scene.DispersionStudies != null)
                foreach (var s in scene.DispersionStudies)
                    studies.Children.Add(new ProjectTreeNode(
                        "study:" + s.Id, LeafIcon,
                        string.IsNullOrEmpty(s.Name) ? "(study)" : s.Name,
                        tag: s, iconColor: ColLeaf));
            project.Children.Add(studies);

            // ── Detector Allocations ───────────────────────────────────────
            var allocs = new ProjectTreeNode("allocations", SectionIconAlloc,
                "Detector Allocations", Count(scene.DetectorAllocations?.Count), iconColor: ColAlloc);
            if (scene.DetectorAllocations != null)
                foreach (var a in scene.DetectorAllocations)
                    allocs.Children.Add(new ProjectTreeNode(
                        "alloc:" + a.Id, LeafIcon,
                        string.IsNullOrEmpty(a.Name) ? "(allocation)" : a.Name,
                        tag: a, iconColor: ColLeaf));
            project.Children.Add(allocs);

            // ── Views ──────────────────────────────────────────────────────
            var views = new ProjectTreeNode("views", SectionIconView,
                "Views", Count(scene.Views?.Count), iconColor: ColView);
            if (scene.Views != null)
                foreach (var v in scene.Views)
                {
                    var pinnedSim = scene.Simulations?.FirstOrDefault(s => s.Id == v.SimulationId);
                    bool simReady = pinnedSim != null && pinnedSim.Status == SimulationStatus.Completed;
                    string statusText = pinnedSim == null ? ""
                        : simReady ? pinnedSim.Name : pinnedSim.Status.ToString();
                    string statusColor = pinnedSim == null ? ""
                        : simReady ? "#2E8B57" : "#FF8C00";
                    string label = (string.IsNullOrEmpty(v.Name) ? "(view)" : v.Name)
                        + "  [" + v.Kind + "]";
                    views.Children.Add(new ProjectTreeNode(
                        "view:" + v.Id, LeafIcon, label,
                        tag: v, hasVisibilityToggle: true,
                        initialVisibility: v.IsVisible,
                        statusText: statusText, statusColor: statusColor,
                        iconColor: ColLeaf));
                }
            project.Children.Add(views);

            // ── Camera Presets ─────────────────────────────────────────────
            var cams = new ProjectTreeNode("cameras", SectionIconCamera,
                "Camera Presets", Count(scene.CameraPresets?.Count), iconColor: ColCamera);
            if (scene.CameraPresets != null)
                foreach (var c in scene.CameraPresets)
                    cams.Children.Add(new ProjectTreeNode(
                        "cam:" + c.Id, LeafIcon,
                        string.IsNullOrEmpty(c.Name) ? "(camera)" : c.Name,
                        tag: c, iconColor: ColLeaf));
            project.Children.Add(cams);

            // ── Monitors ───────────────────────────────────────────────────
            var monitors = new ProjectTreeNode("monitors", SectionIconMonitor,
                "Monitors", Count(scene.MonitorPoints?.Count), iconColor: ColMonitor);
            if (scene.MonitorPoints != null)
                foreach (var m in scene.MonitorPoints)
                    monitors.Children.Add(new ProjectTreeNode(
                        "mon:" + m.Id, LeafIcon,
                        string.IsNullOrEmpty(m.Name) ? "(monitor)" : m.Name,
                        tag: m, hasVisibilityToggle: true,
                        initialVisibility: m.Visible,
                        iconColor: ColLeaf));
            project.Children.Add(monitors);

            // ── Detectors ──────────────────────────────────────────────────
            var detectors = new ProjectTreeNode("detectors", SectionIconDetector,
                "Gas Detectors", Count(scene.GasDetectors?.Count), iconColor: ColDetector);
            if (scene.GasDetectors != null)
                foreach (var d in scene.GasDetectors)
                    detectors.Children.Add(new ProjectTreeNode(
                        "det:" + d.Id, LeafIcon,
                        string.IsNullOrEmpty(d.Name) ? "(detector)" : d.Name,
                        tag: d, hasVisibilityToggle: true,
                        initialVisibility: d.Visible,
                        iconColor: ColLeaf));
            project.Children.Add(detectors);

            // ── Wind Rose ──────────────────────────────────────────────────
            if (scene.WindRose != null)
                project.Children.Add(new ProjectTreeNode(
                    "windrose", SectionIconWindRose, "Wind Rose",
                    tag: scene.WindRose, iconColor: ColWindRose));

            roots.Add(project);
            return roots;
        }

        private static string Count(int? n)
            => (n is null || n == 0) ? "" : n.Value.ToString(CultureInfo.InvariantCulture);
    }
}
