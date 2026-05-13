using System.Collections.ObjectModel;
using System.Globalization;
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
        private const string SectionIconWind      = "mdi-weather-windy";
        private const string SectionIconSim       = "mdi-play-circle-outline";
        private const string SectionIconStudy     = "mdi-chart-line";
        private const string SectionIconAlloc     = "mdi-target";
        private const string SectionIconView      = "mdi-image-outline";
        private const string SectionIconCamera    = "mdi-camera-outline";
        private const string SectionIconMonitor   = "mdi-circle-medium";
        private const string SectionIconDetector  = "mdi-radar";
        private const string SectionIconWindRose  = "mdi-compass-outline";
        private const string ProjectIcon          = "mdi-folder-outline";
        private const string LeafIcon             = "mdi-chevron-right";

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
                string.IsNullOrWhiteSpace(projectName) ? "Untitled" : projectName);

            // ── General Settings ───────────────────────────────────────────
            project.Children.Add(new ProjectTreeNode(
                "general", SectionIconGeneral, "General Settings",
                tag: scene.GeneralSettings));

            // ── Gases ──────────────────────────────────────────────────────
            var gases = new ProjectTreeNode("gases", SectionIconGas,
                "Gases", Count(scene.GasLibrary?.Count));
            if (scene.GasLibrary != null)
                foreach (var g in scene.GasLibrary)
                    gases.Children.Add(new ProjectTreeNode(
                        "gas:" + g.Id, LeafIcon,
                        string.IsNullOrEmpty(g.Name) ? "(unnamed)" : g.Name,
                        tag: g));
            project.Children.Add(gases);

            // ── Geometry ───────────────────────────────────────────────────
            var geometry = new ProjectTreeNode("geometry", SectionIconGeometry,
                "Geometry", Count(scene.Decorations?.Count));
            if (scene.Decorations != null)
                foreach (var d in scene.Decorations)
                    geometry.Children.Add(new ProjectTreeNode(
                        "deco:" + d.Id, LeafIcon,
                        string.IsNullOrEmpty(d.Name) ? "(decoration)" : d.Name,
                        tag: d));
            project.Children.Add(geometry);

            // ── Sources ────────────────────────────────────────────────────
            var sources = new ProjectTreeNode("sources", SectionIconSource,
                "Sources", Count(scene.TopLevelSources?.Count));
            if (scene.TopLevelSources != null)
                foreach (var s in scene.TopLevelSources)
                    sources.Children.Add(new ProjectTreeNode(
                        "src:" + s.Id, LeafIcon,
                        string.IsNullOrEmpty(s.Name) ? "(source)" : s.Name,
                        tag: s));
            project.Children.Add(sources);

            // ── Fire Sources ───────────────────────────────────────────────
            // FireScenario.Sources is the canonical list of jet/pool fires
            // for thermal radiation analysis. Section node carries the scenario
            // itself so the inspector can edit the contour levels list.
            var fireSources = scene.FireScenario?.Sources;
            var fires = new ProjectTreeNode("fires", SectionIconFire,
                "Fire Sources", Count(fireSources?.Count),
                tag: scene.FireScenario);
            if (fireSources != null)
                foreach (var f in fireSources)
                    fires.Children.Add(new ProjectTreeNode(
                        "fire:" + f.Id, LeafIcon,
                        string.IsNullOrEmpty(f.Name) ? "(fire)" : f.Name,
                        tag: f));
            project.Children.Add(fires);

            // ── Wind Fields ────────────────────────────────────────────────
            var winds = new ProjectTreeNode("winds", SectionIconWind,
                "Wind Fields", Count(scene.WindFieldScenarios?.Count));
            if (scene.WindFieldScenarios != null)
                foreach (var w in scene.WindFieldScenarios)
                    winds.Children.Add(new ProjectTreeNode(
                        "wind:" + w.Id, LeafIcon,
                        string.IsNullOrEmpty(w.Name) ? "(wind field)" : w.Name,
                        tag: w));
            project.Children.Add(winds);

            // ── Simulations ────────────────────────────────────────────────
            var sims = new ProjectTreeNode("simulations", SectionIconSim,
                "Simulations", Count(scene.Simulations?.Count));
            if (scene.Simulations != null)
                foreach (var s in scene.Simulations)
                    sims.Children.Add(new ProjectTreeNode(
                        "sim:" + s.Id, LeafIcon,
                        string.IsNullOrEmpty(s.Name) ? "(simulation)" : s.Name,
                        tag: s));
            project.Children.Add(sims);

            // ── Dispersion Studies ─────────────────────────────────────────
            var studies = new ProjectTreeNode("studies", SectionIconStudy,
                "Dispersion Studies", Count(scene.DispersionStudies?.Count));
            if (scene.DispersionStudies != null)
                foreach (var s in scene.DispersionStudies)
                    studies.Children.Add(new ProjectTreeNode(
                        "study:" + s.Id, LeafIcon,
                        string.IsNullOrEmpty(s.Name) ? "(study)" : s.Name,
                        tag: s));
            project.Children.Add(studies);

            // ── Detector Allocations ───────────────────────────────────────
            var allocs = new ProjectTreeNode("allocations", SectionIconAlloc,
                "Detector Allocations", Count(scene.DetectorAllocations?.Count));
            if (scene.DetectorAllocations != null)
                foreach (var a in scene.DetectorAllocations)
                    allocs.Children.Add(new ProjectTreeNode(
                        "alloc:" + a.Id, LeafIcon,
                        string.IsNullOrEmpty(a.Name) ? "(allocation)" : a.Name,
                        tag: a));
            project.Children.Add(allocs);

            // ── Views ──────────────────────────────────────────────────────
            var views = new ProjectTreeNode("views", SectionIconView,
                "Views", Count(scene.Views?.Count));
            if (scene.Views != null)
                foreach (var v in scene.Views)
                    views.Children.Add(new ProjectTreeNode(
                        "view:" + v.Id, LeafIcon,
                        string.IsNullOrEmpty(v.Name) ? "(view)" : v.Name,
                        tag: v));
            project.Children.Add(views);

            // ── Camera Presets ─────────────────────────────────────────────
            var cams = new ProjectTreeNode("cameras", SectionIconCamera,
                "Camera Presets", Count(scene.CameraPresets?.Count));
            if (scene.CameraPresets != null)
                foreach (var c in scene.CameraPresets)
                    cams.Children.Add(new ProjectTreeNode(
                        "cam:" + c.Name, LeafIcon,
                        string.IsNullOrEmpty(c.Name) ? "(camera)" : c.Name,
                        tag: c));
            project.Children.Add(cams);

            // ── Monitors ───────────────────────────────────────────────────
            var monitors = new ProjectTreeNode("monitors", SectionIconMonitor,
                "Monitors", Count(scene.MonitorPoints?.Count));
            if (scene.MonitorPoints != null)
                foreach (var m in scene.MonitorPoints)
                    monitors.Children.Add(new ProjectTreeNode(
                        "mon:" + m.Id, LeafIcon,
                        string.IsNullOrEmpty(m.Name) ? "(monitor)" : m.Name,
                        tag: m));
            project.Children.Add(monitors);

            // ── Detectors ──────────────────────────────────────────────────
            var detectors = new ProjectTreeNode("detectors", SectionIconDetector,
                "Gas Detectors", Count(scene.GasDetectors?.Count));
            if (scene.GasDetectors != null)
                foreach (var d in scene.GasDetectors)
                    detectors.Children.Add(new ProjectTreeNode(
                        "det:" + d.Id, LeafIcon,
                        string.IsNullOrEmpty(d.Name) ? "(detector)" : d.Name,
                        tag: d));
            project.Children.Add(detectors);

            // ── Wind Rose ──────────────────────────────────────────────────
            if (scene.WindRose != null)
                project.Children.Add(new ProjectTreeNode(
                    "windrose", SectionIconWindRose, "Wind Rose",
                    tag: scene.WindRose));

            roots.Add(project);
            return roots;
        }

        private static string Count(int? n)
            => (n is null || n == 0) ? "" : n.Value.ToString(CultureInfo.InvariantCulture);
    }
}
